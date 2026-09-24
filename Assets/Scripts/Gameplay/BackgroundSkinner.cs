using TableFootball.Net;
using TableFootball.UI;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Puts the equipped background skin behind the table.
    ///
    /// Unlike the ball, field and figure skinners this one owns no renderer of its own — a background
    /// is <see cref="RenderSettings.skybox"/>, a scene-wide setting. It also has to tell
    /// <see cref="MenuStageCamera"/> to stop clearing to a flat colour, because that camera is the
    /// menu's hero view and would otherwise paint over the sky entirely.
    ///
    /// The default draws NO skybox and leaves the menu camera exactly as it was, so a player who
    /// never buys a room sees the front end they have always seen. That is the whole reason the
    /// default resolves to null rather than to some "plain" panorama.
    ///
    /// Input-agnostic like its siblings: offline it reads <see cref="Inventory"/>, online the host
    /// pushes a choice in through <see cref="SetOverride"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class BackgroundSkinner : MonoBehaviour
    {
        private MenuStageCamera stage;
        private string overrideId;

        /// <summary>The skybox the scene had before any skin was applied, so removing a background
        /// restores whatever the project shipped with rather than leaving the last one on.</summary>
        private Material originalSkybox;
        private bool capturedOriginal;

        private void Awake()
        {
            if (!capturedOriginal)
            {
                originalSkybox = RenderSettings.skybox;
                capturedOriginal = true;
            }

            // Optional and found rather than passed, matching how GameFlow treats the stage camera:
            // the menu works without it, and a background must never be why a scene fails to build.
            stage = FindAnyObjectByType<MenuStageCamera>();
        }

        private void OnEnable()
        {
            Inventory.OnChanged -= Apply;
            Inventory.OnChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            Inventory.OnChanged -= Apply;
        }

        /// <summary>Wears the room somebody else chose — online, the host's.</summary>
        public void SetOverride(string id)
        {
            overrideId = id;
            Apply();
        }

        /// <summary>Hands the choice back to the local inventory. The end of an online match.</summary>
        public void ClearOverride()
        {
            overrideId = null;
            Apply();
        }

        public string CurrentId =>
            string.IsNullOrEmpty(overrideId)
                ? Inventory.EquippedId(CosmeticKind.Background)
                : overrideId;

        private void Apply()
        {
            string id = CurrentId;

            // The floor first, and OUTSIDE the skybox branch below, because it is the half of a
            // background that a match can actually show. The skybox is front-end dressing; this is
            // what the player sees around the table while playing.
            ApplyFloor(id);

            Material sky = BackgroundSkins.Resolve(id);

            if (sky != null)
            {
                RenderSettings.skybox = sky;
                if (stage != null) stage.UseSkybox(true);
                return;
            }

            // No art, or no shader to draw it with. Put the scene's own skybox back and let the menu
            // camera return to its flat backdrop, so the front end looks exactly as it did before
            // backgrounds existed.
            RenderSettings.skybox = originalSkybox;

            // A skin that HAS art but could not be drawn still gets its colour across, so a player
            // who bought Dark Room does not simply see nothing happen.
            Color? tint = BackgroundSkins.HasArt(id) ? BackgroundSkins.FallbackColor(id) : null;
            if (stage != null) stage.UseSkybox(false, tint);
        }

        // ── The room floor ─────────────────────────────────────────────────────────────────────

        /// <summary>Raised a hair above the table's lowest point so it cannot z-fight the feet.</summary>
        private const float FloorLift = 0.002f;

        /// <summary>
        /// Unity's Plane primitive is 10 units across at scale 1, so this is a 4-unit square, and
        /// the floor art is rendered to cover exactly 4 metres — one world unit to the metre, the
        /// same scale the table is built at.
        ///
        /// Sized to the camera rather than made huge on purpose. The gameplay view spans about
        /// 1.8 x 1.0 units at 16:9 (2.3 wide even at 21:9), so 4 units clears every sane aspect
        /// ratio with room to spare while keeping the texture's pixels concentrated where they are
        /// actually seen. A far larger plane would only spend its resolution off-screen.
        /// </summary>
        private const float FloorScale = 0.4f;

        private Transform roomFloor;
        private MeshRenderer floorRenderer;

        /// <summary>Shows the room's floor under the table, or hides it when no room is equipped.
        /// Nothing is created at all until a room actually needs one, so a player who never buys a
        /// background has no extra object in their scene.</summary>
        private void ApplyFloor(string id)
        {
            Material floorMaterial = BackgroundSkins.ResolveFloor(id);

            if (floorMaterial == null)
            {
                if (floorRenderer != null) floorRenderer.enabled = false;
                return;
            }

            EnsureFloor();
            if (floorRenderer == null) return;

            floorRenderer.sharedMaterial = floorMaterial;
            floorRenderer.enabled = true;
        }

        private void EnsureFloor()
        {
            if (roomFloor != null)
            {
                return;
            }

            // Measured BEFORE the plane exists, so the floor's own renderer can never be folded into
            // the bounds that position it.
            Bounds table = MeasureTable();

            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "RoomFloor";

            // CreatePrimitive ships a collider. A stray one under the table would join the ball's
            // physics world and every raycast the rods do — this is decoration and must touch
            // neither.
            var unwanted = go.GetComponent<Collider>();
            if (unwanted != null) Destroy(unwanted);

            go.transform.SetParent(transform, false);

            // Placed from the table's MEASURED bounds rather than typed-in coordinates, and in world
            // space rather than local. Hardcoded world positions are exactly what left the menu's
            // stage camera framing the underside of the table after the rig moved; bounds cannot go
            // stale that way.
            go.transform.position =
                new Vector3(table.center.x, table.min.y + FloorLift, table.center.z);
            go.transform.rotation = Quaternion.identity;   // Plane already lies flat, facing +Y
            go.transform.localScale = Vector3.one * FloorScale;

            floorRenderer = go.GetComponent<MeshRenderer>();
            if (floorRenderer != null)
            {
                // It receives the table's shadow, which is most of what sells the table as standing
                // in a room, but casts none of its own — there is nothing below it to catch one.
                floorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                floorRenderer.receiveShadows = true;
            }

            roomFloor = go.transform;
        }

        /// <summary>The table's world bounds, from every renderer under it. Falls back to a zero-size
        /// bounds at this object when there is nothing to measure, which still puts the floor
        /// somewhere sane rather than at the world origin.</summary>
        private Bounds MeasureTable()
        {
            Transform root = transform;
            var refs = FindAnyObjectByType<TableReferences>();
            if (refs != null) root = refs.transform;

            var bounds = new Bounds(root.position, Vector3.zero);
            bool any = false;

            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (r == null || r == floorRenderer) continue;

                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return bounds;
        }
    }
}
