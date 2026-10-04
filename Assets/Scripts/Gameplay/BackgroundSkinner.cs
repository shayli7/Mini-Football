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
            ApplyWeather(id);

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

        // ── Weather: falling snow on the Snow Mountain stage ───────────────────────────────────

        private ParticleSystem snowfall;
        private Material snowMaterial;
        private Bounds? measured;

        /// <summary>
        /// Lets snow drift round the table while the Snow Mountain stage is worn.
        ///
        /// The flakes fall in the room BELOW the table — between the floor and the underside of the
        /// pitch — never above it. The gameplay camera looks straight down, so the table itself hides
        /// every flake that would be over the pitch: the snow shows only in the margin of room around
        /// the table and physically cannot cover the ball. A snowfall over the pitch would look
        /// livelier for a second and then be in the way of every shot.
        /// </summary>
        private void ApplyWeather(string id)
        {
            bool snowing = id == "background.snow_mountain";
            if (!snowing)
            {
                if (snowfall != null) snowfall.gameObject.SetActive(false);
                return;
            }

            if (snowfall == null && !BuildSnowfall()) return;
            snowfall.gameObject.SetActive(true);
            if (!snowfall.isPlaying) snowfall.Play();
        }

        private bool BuildSnowfall()
        {
            Bounds table = Measured();

            snowMaterial = SkinMaterials.CreateTrailMaterial(Color.white, null);
            if (snowMaterial == null) return false;   // no usable shader: the stage just has no snow
            SkinMaterials.SetBaseMap(snowMaterial, SoftDot());

            var go = new GameObject("Snowfall");
            go.transform.SetParent(transform, false);

            // Between the floor and the pitch's underside, so the table occludes it from above.
            float floorY = table.min.y;
            float ceilingY = floorY + (table.size.y * 0.55f);
            go.transform.position = new Vector3(table.center.x, ceilingY, table.center.z);
            go.transform.rotation = Quaternion.identity;

            snowfall = go.AddComponent<ParticleSystem>();
            snowfall.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = snowfall.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            main.maxParticles = 400;
            main.prewarm = true;                // the stage opens already snowing, not starting to
            main.loop = true;

            // Spawns over the whole visible room, with plenty past the screen edge for the wind.
            var shape = snowfall.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(3.6f, 0.02f, 2.4f);

            var emission = snowfall.emission;
            emission.rateOverTime = 45f;

            // A slow fall with a gentle wind, and a wander so no two flakes move alike.
            var vel = snowfall.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.09f, -0.05f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f);

            var noise = snowfall.noise;
            noise.enabled = true;
            noise.strength = 0.03f;
            noise.frequency = 0.6f;
            noise.scrollSpeed = 0.2f;

            var col = snowfall.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f),
                        new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(fade);

            var rend = snowfall.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sharedMaterial = snowMaterial;
            return true;
        }

        /// <summary>A soft round flake, made in code — no imported art.</summary>
        private static Texture2D SoftDot()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float x = (i + 0.5f) / n * 2f - 1f, y = (j + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y));
                    px[j * n + i] = new Color32(255, 255, 255, (byte)(Mathf.SmoothStep(0f, 1f, a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>The table's bounds, measured once and reused by the floor and the weather.</summary>
        private Bounds Measured()
        {
            if (!measured.HasValue) measured = MeasureTable();
            return measured.Value;
        }

        private void EnsureFloor()
        {
            if (roomFloor != null)
            {
                return;
            }

            // Measured BEFORE the plane exists, so the floor's own renderer can never be folded into
            // the bounds that position it.
            Bounds table = Measured();

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
