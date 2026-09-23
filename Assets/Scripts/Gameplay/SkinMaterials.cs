using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// The shared plumbing every cosmetic skin needs: load a texture out of Resources, clone the
    /// model's own material, and write shader properties without caring which render pipeline
    /// produced that material.
    ///
    /// Here rather than repeated in <see cref="BallSkins"/>, <see cref="FieldSkins"/> and
    /// <see cref="FigureSkins"/> because writing a property a shader does not have is a SILENT
    /// no-op in Unity — not an error, not a warning. Three copies of the URP/built-in name table
    /// would be three places for one of them to be edited alone, and the symptom of getting it
    /// wrong is "the skin does nothing" with no clue anywhere.
    ///
    /// Materials are always CLONED FROM THE MODEL'S OWN rather than built from
    /// <see cref="Shader.Find"/>. The model's material already uses whatever shader this project's
    /// pipeline wants, so cloning inherits it and cannot pick the wrong one or come back magenta in
    /// a build where a name-looked-up shader was stripped.
    /// </summary>
    public static class SkinMaterials
    {
        // URP's Lit names first, the built-in Standard shader's second.
        private static readonly string[] BaseMapNames = { "_BaseMap", "_MainTex" };
        private static readonly string[] BaseColorNames = { "_BaseColor", "_Color" };
        private static readonly string[] MetallicNames = { "_Metallic" };
        private static readonly string[] SmoothnessNames = { "_Smoothness", "_Glossiness" };

        /// <summary>What scales a smoothness MAP: URP reuses `_Smoothness`, built-in has a separate
        /// `_GlossMapScale`. Both must read 1 for an authored map to pass through unchanged.</summary>
        private static readonly string[] SmoothnessScaleNames = { "_Smoothness", "_GlossMapScale" };

        private const string MetallicGlossMap = "_MetallicGlossMap";
        private const string SmoothnessChannel = "_SmoothnessTextureChannel";
        private const string EmissionMap = "_EmissionMap";
        private const string EmissionColor = "_EmissionColor";
        private const string MetallicMapKeyword = "_METALLICSPECGLOSSMAP";
        private const string AlbedoAlphaSmoothnessKeyword = "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A";
        private const string EmissionKeyword = "_EMISSION";

        /// <summary>
        /// Loads a texture from Resources, warning by name if it is missing.
        ///
        /// <see cref="Resources.Load"/> only reads from a folder called exactly <c>Resources</c>, and
        /// a miss returns null rather than throwing — so without this warning a texture saved to the
        /// wrong folder degrades to "the skin silently does nothing".
        /// </summary>
        public static Texture2D Load(string resourcePath)
        {
            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex == null)
            {
                Debug.LogWarning($"Skin texture missing: Resources/{resourcePath}");
            }
            return tex;
        }

        /// <summary>A copy of the model's material, ready to be re-textured.</summary>
        public static Material Clone(Material original, string name)
        {
            return original == null ? null : new Material(original) { name = name };
        }

        /// <summary>
        /// Points the material at a new albedo and resets its tint to white. Returns false when the
        /// shader has no base-map property at all, which is the one failure worth reporting: nothing
        /// else set below would have any visible effect either.
        /// </summary>
        public static bool SetAlbedo(Material mat, Texture texture)
        {
            if (mat == null || texture == null)
            {
                return false;
            }

            if (!SetTextureAny(mat, BaseMapNames, texture))
            {
                Debug.LogWarning($"Skin: shader '{mat.shader.name}' has no base-map property " +
                                 $"({string.Join("/", BaseMapNames)}); the texture cannot be applied.");
                return false;
            }

            // The model's material may carry a tint that would multiply the new texture into the
            // wrong colour. The skin's texture IS the colour, so the tint goes back to white.
            SetColorAny(mat, BaseColorNames, Color.white);
            return true;
        }

        /// <summary>Tints a material a flat colour, for a skin that is a colour rather than art.</summary>
        public static void SetFlatColor(Material mat, Color color)
        {
            if (mat == null) return;
            SetColorAny(mat, BaseColorNames, color);
        }

        public static void SetFinish(Material mat, float metallic, float smoothness)
        {
            if (mat == null) return;
            SetFloatAny(mat, MetallicNames, metallic);
            SetFloatAny(mat, SmoothnessNames, smoothness);
        }

        /// <summary>
        /// Applies a packed metallic/smoothness map (R = metallic, A = smoothness).
        ///
        /// Neither pipeline REPLACES smoothness with the map — both MULTIPLY, so the scale is forced
        /// to 1 or every authored value would be dimmed by the fallback float. And without the
        /// keyword URP ignores the map entirely and quietly falls back to those floats, which looks
        /// almost right and so survives a play test.
        /// </summary>
        public static void SetMetallicMap(Material mat, Texture texture)
        {
            if (mat == null || texture == null || !mat.HasProperty(MetallicGlossMap)) return;
            mat.SetTexture(MetallicGlossMap, texture);
            mat.EnableKeyword(MetallicMapKeyword);
            SetFloatAny(mat, SmoothnessScaleNames, 1f);

            // Smoothness is packed in the METALLIC map's alpha, not the albedo's. That is URP's
            // default, but these materials are cloned from the model's own and inherit whatever it
            // was set to, so it is stated rather than assumed.
            if (mat.HasProperty(SmoothnessChannel)) mat.SetFloat(SmoothnessChannel, 0f);
            mat.DisableKeyword(AlbedoAlphaSmoothnessKeyword);
        }

        /// <summary>
        /// Makes a material glow from a map. The keyword is what actually switches emission on;
        /// setting the map alone renders nothing, which is how a "glowing" skin ends up merely
        /// looking painted.
        /// </summary>
        public static void SetEmission(Material mat, Texture texture, Color tint)
        {
            if (mat == null || texture == null || !mat.HasProperty(EmissionMap)) return;
            mat.SetTexture(EmissionMap, texture);
            if (mat.HasProperty(EmissionColor)) mat.SetColor(EmissionColor, tint);
            mat.EnableKeyword(EmissionKeyword);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        /// <summary>
        /// Shaders to try for a trail or particle effect, best first.
        ///
        /// <see cref="Shader.Find"/> is unreliable in a BUILD — a shader no material references gets
        /// stripped and the lookup returns null, which is why <paramref name="fallbackSource"/> below
        /// exists rather than this being a straight Find.
        /// </summary>
        private static readonly string[] TrailShaders =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Unlit",
            "Sprites/Default",
            "Unlit/Transparent"
        };

        /// <summary>
        /// A soft additive material for a trail or particle effect.
        ///
        /// Tries the unlit shaders first, because those honour the per-vertex colour a
        /// <see cref="TrailRenderer"/> gradient and a particle system's colour-over-lifetime write —
        /// which is what makes a trail FADE rather than end in a hard edge. If none survived build
        /// stripping it falls back to cloning <paramref name="fallbackSource"/> (the ball's own
        /// material, so the shader is guaranteed to exist) switched to transparent: the fade is lost
        /// but the effect still draws, tapering by width instead.
        ///
        /// Returns null only if there is nothing to clone either, in which case the caller should
        /// simply not show an effect.
        /// </summary>
        public static Material CreateTrailMaterial(Color color, Material fallbackSource)
        {
            foreach (string name in TrailShaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null) continue;

                var m = new Material(shader) { name = "SkinTrail" };
                SetColorAny(m, BaseColorNames, color);
                SetTextureAny(m, BaseMapNames, Texture2D.whiteTexture);
                MakeAdditive(m);
                return m;
            }

            if (fallbackSource == null)
            {
                return null;
            }

            var clone = new Material(fallbackSource) { name = "SkinTrail_Fallback" };
            SetTextureAny(clone, BaseMapNames, Texture2D.whiteTexture);
            SetColorAny(clone, BaseColorNames, color);
            SetFloatAny(clone, MetallicNames, 0f);
            if (clone.HasProperty(EmissionColor)) clone.SetColor(EmissionColor, color);
            clone.EnableKeyword(EmissionKeyword);
            MakeAdditive(clone);
            return clone;
        }

        /// <summary>
        /// Switches a material to additive transparency. URP's Lit needs the whole set — surface
        /// type, blend factors, depth write, keyword AND render queue — because setting only some of
        /// them leaves it opaque with no complaint.
        /// </summary>
        private static void MakeAdditive(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);       // additive
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static bool SetTextureAny(Material mat, string[] names, Texture value)
        {
            foreach (string n in names)
            {
                if (mat.HasProperty(n)) { mat.SetTexture(n, value); return true; }
            }
            return false;
        }

        private static void SetFloatAny(Material mat, string[] names, float value)
        {
            // Every match, not just the first: URP and built-in names can coexist on a shader that
            // supports both, and leaving one of them stale is how a value half-applies.
            foreach (string n in names)
            {
                if (mat.HasProperty(n)) mat.SetFloat(n, value);
            }
        }

        private static void SetColorAny(Material mat, string[] names, Color value)
        {
            foreach (string n in names)
            {
                if (mat.HasProperty(n)) mat.SetColor(n, value);
            }
        }
    }
}
