using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Turns a background-skin id into the skybox the table stands in.
    ///
    /// A background is a SKYBOX rather than geometry because the scene has no environment of its own
    /// — the table floats in Unity's default procedural sky. Each skin is an equirectangular panorama
    /// of a real 3D room, rendered from eye level with the floor 0.8 m below, so the image's vertical
    /// centre lands on the horizon exactly where the Skybox/Panoramic shader expects it. See
    /// <c>Art/Blender/make_backgrounds.py</c>.
    ///
    /// The DEFAULT is deliberately no skybox at all. <c>background.classic</c> resolves to null, and
    /// the front end keeps the flat backdrop it has always had — equipping nothing changes nothing,
    /// which is what makes this safe to ship without redesigning every menu screen around it.
    /// </summary>
    public static class BackgroundSkins
    {
        private const string Folder = "BackgroundSkins/";

        /// <summary>
        /// Shaders that can draw a lat-long panorama, best first. <see cref="Shader.Find"/> returns
        /// null in a BUILD for anything that was stripped, which is why <see cref="FallbackColor"/>
        /// exists rather than this being a straight lookup.
        /// </summary>
        private static readonly string[] PanoramicShaders =
        {
            "Skybox/Panoramic",
            "Skybox/Cubemap"
        };

        private static readonly Dictionary<string, string> Stems = new()
        {
            { "background.living_room",  "LivingRoom" },
            { "background.dark_room",    "DarkRoom" },
            { "background.disco_room",   "DiscoRoom" },
            { "background.royal_palace", "RoyalPalace" }
        };

        /// <summary>
        /// A representative colour per skin, used when no panoramic shader survived build stripping.
        /// The room is then a flat tint rather than a picture — recognisably the right room, and far
        /// better than a magenta sky or the wrong one.
        /// </summary>
        private static readonly Dictionary<string, Color> Fallbacks = new()
        {
            { "background.living_room",  new Color(0.36f, 0.30f, 0.25f) },
            { "background.dark_room",    new Color(0.08f, 0.09f, 0.11f) },
            { "background.disco_room",   new Color(0.14f, 0.11f, 0.20f) },
            { "background.royal_palace", new Color(0.62f, 0.54f, 0.44f) }
        };

        private static readonly Dictionary<string, Material> Cache = new();

        /// <summary>
        /// Shaders for the room FLOOR, best first. Unlit is preferred: the floor is a backdrop, and a
        /// lit one would be dimmed by whatever the match lighting happens to be. The lit entries are
        /// there only so a build that stripped the unlit ones still draws something.
        /// </summary>
        private static readonly string[] FloorShaders =
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Texture",
            "Universal Render Pipeline/Lit",
            "Standard"
        };

        private static readonly Dictionary<string, Material> FloorCache = new();

        /// <summary>Whether this id names a background with art. False for the default, which means
        /// "no skybox — keep the flat backdrop".</summary>
        public static bool HasArt(string id) => !string.IsNullOrEmpty(id) && Stems.ContainsKey(id);

        /// <summary>The flat tint to fall back to, or null when this skin has no art anyway.</summary>
        public static Color? FallbackColor(string id) =>
            Fallbacks.TryGetValue(id ?? string.Empty, out Color c) ? c : (Color?)null;

        /// <summary>
        /// The skybox material for <paramref name="id"/>, or NULL for the default, an unknown id, a
        /// missing texture, or a missing shader. Null is a real answer here, not a failure: it means
        /// "draw no skybox", which is exactly the default's behaviour.
        /// </summary>
        public static Material Resolve(string id)
        {
            if (!HasArt(id))
            {
                return null;
            }

            if (Cache.TryGetValue(id, out Material cached) && cached != null)
            {
                return cached;
            }

            string stem = Stems[id];
            var pano = SkinMaterials.Load($"{Folder}BgSkin_{stem}");
            if (pano == null)
            {
                return null;
            }

            Shader shader = null;
            foreach (string name in PanoramicShaders)
            {
                shader = Shader.Find(name);
                if (shader != null) break;
            }

            if (shader == null)
            {
                Debug.LogWarning("BackgroundSkins: no panoramic skybox shader available " +
                                 $"({string.Join("/", PanoramicShaders)}) — '{id}' will fall back to a " +
                                 "flat colour. Add Skybox/Panoramic to Always Included Shaders.");
                return null;
            }

            var mat = new Material(shader) { name = "BgSkin_" + stem };
            // Skybox/Panoramic names its texture _MainTex and needs to be told the mapping is
            // latitude-longitude; the default is 6-frames layout, which would show one sixth of the
            // image stretched across the whole sky.
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", pano);
            if (mat.HasProperty("_Mapping")) mat.SetFloat("_Mapping", 1f);          // Latitude Longitude
            if (mat.HasProperty("_ImageType")) mat.SetFloat("_ImageType", 0f);      // 360 degrees
            if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1f);
            if (mat.HasProperty("_Tint")) mat.SetColor("_Tint", Color.grey);        // neutral, not a tint

            Cache[id] = mat;
            return mat;
        }

        /// <summary>
        /// The material for the room's FLOOR — the surface the table stands on, and the only way a
        /// room is visible during a match.
        ///
        /// The skybox above cannot do that job. The gameplay camera is ORTHOGRAPHIC and points
        /// straight down, so every pixel of it shares one view direction and a skybox collapses to a
        /// flat patch rather than an image. Geometry under the table is what a top-down view can
        /// actually see, so that is what a room becomes in play.
        ///
        /// Prefers a purpose-made top-down floor image at
        /// <c>Resources/BackgroundSkins/BgFloor_&lt;Stem&gt;</c>. Falling back to
        /// <see cref="FallbackColor"/> when there is none is not a failure path but the expected one
        /// until that art exists: the room still reads as the right room, in flat colour, and drops in
        /// as a picture the moment the texture is added — with no code change.
        ///
        /// Null for the default or an unknown id, which means "no floor, draw nothing".
        /// </summary>
        public static Material ResolveFloor(string id)
        {
            if (!HasArt(id))
            {
                return null;
            }

            if (FloorCache.TryGetValue(id, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = null;
            foreach (string name in FloorShaders)
            {
                shader = Shader.Find(name);
                if (shader != null) break;
            }

            if (shader == null)
            {
                Debug.LogWarning("BackgroundSkins: no shader available for the room floor " +
                                 $"({string.Join("/", FloorShaders)}) — '{id}' will not show in play.");
                return null;
            }

            string stem = Stems[id];
            var mat = new Material(shader) { name = "BgFloor_" + stem };

            // Loaded directly rather than through SkinMaterials.Load, which warns on a miss. A missing
            // floor image is the NORMAL state until the art is rendered, and warning every time would
            // train the console to be ignored.
            var floor = Resources.Load<Texture2D>($"{Folder}BgFloor_{stem}");
            if (floor != null)
            {
                SkinMaterials.SetAlbedo(mat, floor);
            }
            else if (FallbackColor(id) is Color tint)
            {
                SkinMaterials.SetFlatColor(mat, tint);
            }

            FloorCache[id] = mat;
            return mat;
        }

        public static void Clear()
        {
            Cache.Clear();
            FloorCache.Clear();
        }
    }
}
