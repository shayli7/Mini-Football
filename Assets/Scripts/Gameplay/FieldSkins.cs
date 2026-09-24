using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Turns a field-skin id into the material the playing surface wears.
    ///
    /// The textures are equirectangular-free plain uv maps painted against the Field box's own
    /// unwrap — see <c>Art/Blender/make_field_textures.py</c>. Both of the box's large faces carry
    /// the pattern, so the skin cannot land upside down or on the underside.
    ///
    /// The white pitch markings are NOT part of this. They are separate geometry with their own
    /// <c>Foos_Line</c> material and are drawn over whatever skin is worn, exactly as they are over
    /// the default turf.
    ///
    /// Purely visual: the field is a render surface, and the table's collision comes from
    /// <see cref="TablePhysicsBuilder"/>, so no skin can change how the ball rolls.
    /// </summary>
    public static class FieldSkins
    {
        private const string Folder = "FieldSkins/";

        /// <summary>Catalogue id to texture stem. Ids absent from here have no art and mean "wear the
        /// model's own material" — that is how <c>field.classic</c> stays the untouched default.</summary>
        private static readonly Dictionary<string, string> Stems = new()
        {
            { "field.deep_blue",     "DeepBlue" },
            { "field.court",         "IndoorCourt" },
            { "field.wild_west",     "WildWest" },
            { "field.pitch_perfect", "PitchPerfect" },
            { "field.neon_rave",     "NeonRave" },
            { "field.chessboard",    "Chessboard" }
        };

        /// <summary>Per-skin finish. A pitch is matte; a polished court and a chessboard catch more
        /// light; Neon Rave is the only one that emits.</summary>
        private static readonly Dictionary<string, (float metallic, float smoothness, bool emissive)>
            Finishes = new()
        {
            { "field.deep_blue",     (0f, 0.35f, false) },
            { "field.court",         (0f, 0.62f, false) },
            { "field.wild_west",     (0f, 0.18f, false) },
            { "field.pitch_perfect", (0f, 0.22f, false) },
            { "field.neon_rave",     (0f, 0.70f, true) },
            { "field.chessboard",    (0f, 0.55f, false) }
        };

        private static readonly Dictionary<string, Material> Cache = new();

        public static bool HasArt(string id) => !string.IsNullOrEmpty(id) && Stems.ContainsKey(id);

        /// <summary>
        /// The material for <paramref name="id"/>, or <paramref name="original"/> for the default, an
        /// unknown id, or a missing texture. Falling back to the original rather than to null means
        /// every failure lands on a perfectly playable plain pitch.
        /// </summary>
        public static Material Resolve(string id, Material original)
        {
            if (!HasArt(id) || original == null)
            {
                return original;
            }

            if (Cache.TryGetValue(id, out Material cached) && cached != null)
            {
                return cached;
            }

            string stem = Stems[id];
            var albedo = SkinMaterials.Load($"{Folder}FieldSkin_{stem}_Albedo");
            if (albedo == null)
            {
                return original;
            }

            var mat = SkinMaterials.Clone(original, "FieldSkin_" + stem);
            if (!SkinMaterials.SetAlbedo(mat, albedo))
            {
                return original;
            }

            var finish = Finishes.TryGetValue(id, out var f) ? f : (0f, 0.4f, false);
            SkinMaterials.SetFinish(mat, finish.Item1, finish.Item2);

            if (finish.Item3)
            {
                // Neon Rave's lines are LIGHT, not paint. Without a real emission map they would be
                // bright stripes that go dull in shadow, which is the whole point of the skin lost.
                var emis = SkinMaterials.Load($"{Folder}FieldSkin_{stem}_Emission");
                SkinMaterials.SetEmission(mat, emis, Color.white);
            }

            Cache[id] = mat;
            return mat;
        }

        public static void Clear() => Cache.Clear();
    }
}
