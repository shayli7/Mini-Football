using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Turns a ball-skin id into the material the ball wears.
    ///
    /// Materials are built at RUNTIME rather than shipped as .mat assets, for the same reason the
    /// physics values and the whole UI are built in code here: the numbers stay version-controlled
    /// and readable in one file instead of spread across inspector-only asset files that nothing can
    /// diff. Textures come from <c>Resources/BallSkins</c>, exactly as recorded audio comes from
    /// <c>Resources/Audio</c> — and, exactly as with audio, <see cref="Resources.Load"/> only reads
    /// from a folder named precisely <c>Resources</c>, so moving these anywhere else silently falls
    /// back to the plain ball with no error.
    ///
    /// Each material is CLONED FROM THE BALL'S OWN, not built from <c>Shader.Find</c>. The model's
    /// material already uses whatever shader this project's render pipeline wants, so cloning
    /// inherits it and cannot pick the wrong one or come back magenta in a build where a
    /// name-looked-up shader was stripped. Every property write is guarded by
    /// <see cref="Material.HasProperty"/> for the same reason — an unexpected shader should cost the
    /// skin its metallic map, not throw in the middle of a kick-off.
    ///
    /// PURELY VISUAL. The ball's physics is the SphereCollider and the Rigidbody, neither of which
    /// this touches, so no skin can change how the ball plays. That is what makes "the same size as
    /// the normal ball" true by construction rather than by matching numbers.
    /// </summary>
    public static class BallSkins
    {
        private const string ResourceFolder = "BallSkins/";

        /// <summary>
        /// Shader property names, URP's first and the built-in Standard shader's second.
        ///
        /// Both are listed because writing a property a shader does not have is a SILENT no-op in
        /// Unity — not an error, not a warning. The model's materials are embedded in the FBX and
        /// are regenerated against whatever pipeline is active, so they should be URP Lit; but if
        /// they were ever produced under the built-in pipeline they would answer to `_MainTex` and
        /// `_Glossiness` instead, every write here would be quietly dropped, and the ball would wear
        /// its plain material with nothing anywhere saying why. Trying both names costs one failed
        /// lookup and removes that entire failure mode.
        /// </summary>
        /// <summary>
        /// How each skin finishes, keyed by catalogue id. Smoothness is 1 − the roughness the skin
        /// was authored at in Blender, so the game and the source file agree.
        ///
        /// <c>packed</c> marks the three skins that ship a metallic/smoothness map: the disco ball
        /// needs per-tile variation (mirror tiles and dark grout are not one material), and ice needs
        /// its frosted cell borders. The rest are uniform enough that two floats say it exactly, and
        /// a texture would be six hundred kilobytes to store one number twice.
        /// </summary>
        private struct Finish
        {
            public readonly float Metallic;
            public readonly float Smoothness;
            public readonly bool Packed;

            public Finish(float metallic, float smoothness, bool packed = false)
            {
                Metallic = metallic;
                Smoothness = smoothness;
                Packed = packed;
            }
        }

        private static readonly Dictionary<string, string> TextureNames = new()
        {
            { "ball.retro_orange", "RetroOrange" },
            { "ball.solid_red",    "SolidRed" },
            { "ball.solid_blue",   "SolidBlue" },
            { "ball.soccer",       "Soccer" },
            { "ball.basketball",   "Basketball" },
            { "ball.camo",         "Camo" },
            { "ball.disco",        "Disco" },
            { "ball.ice",          "Ice" },
            { "ball.golden",       "Golden" }
        };

        private static readonly Dictionary<string, Finish> Finishes = new()
        {
            { "ball.retro_orange", new Finish(0f, 0.58f) },
            { "ball.solid_red",    new Finish(0f, 0.62f) },
            { "ball.solid_blue",   new Finish(0f, 0.62f) },
            { "ball.soccer",       new Finish(0f, 0.66f) },
            { "ball.basketball",   new Finish(0f, 0.38f) },
            { "ball.camo",         new Finish(0f, 0.45f) },
            { "ball.disco",        new Finish(1f, 0.85f, packed: true) },
            { "ball.ice",          new Finish(0f, 0.74f, packed: true) },
            { "ball.golden",       new Finish(1f, 0.88f, packed: true) }
        };

        /// <summary>Built materials, so a skin is assembled once per session however often the ball
        /// is re-skinned. Cleared by <see cref="Clear"/> when the textures could have changed.</summary>
        private static readonly Dictionary<string, Material> Cache = new();

        /// <summary>Whether this id names a skin with art. False for the default and for anything a
        /// later build removed, both of which mean "wear the model's own material".</summary>
        public static bool HasArt(string id) => !string.IsNullOrEmpty(id) && TextureNames.ContainsKey(id);

        /// <summary>
        /// The material for <paramref name="id"/>, or <paramref name="original"/> when the id is the
        /// default, unknown, or its texture is missing.
        ///
        /// Falling back to the original rather than to null is the whole safety story here: every
        /// failure — a stale id in a save, a texture that did not ship, an unexpected shader — lands
        /// on a perfectly playable plain ball rather than on an invisible or magenta one.
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

            string file = TextureNames[id];
            var albedo = SkinMaterials.Load($"{ResourceFolder}BallSkin_{file}_Albedo");
            if (albedo == null)
            {
                return original;
            }

            var mat = SkinMaterials.Clone(original, "BallSkin_" + file);
            if (!SkinMaterials.SetAlbedo(mat, albedo))
            {
                return original;
            }

            Finish finish = Finishes.TryGetValue(id, out Finish f) ? f : new Finish(0f, 0.5f);
            SkinMaterials.SetFinish(mat, finish.Metallic, finish.Smoothness);

            if (finish.Packed)
            {
                SkinMaterials.SetMetallicMap(
                    mat, SkinMaterials.Load($"{ResourceFolder}BallSkin_{file}_MetalSmooth"));
            }

            Cache[id] = mat;
            return mat;
        }

        /// <summary>Drops the built materials. Only needed if the textures themselves change.</summary>
        public static void Clear() => Cache.Clear();
    }
}
