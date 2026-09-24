using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Turns a figure-skin id into the three materials a player figure wears.
    ///
    /// Every figure has three material slots, and which one carries what is load-bearing:
    ///
    ///     slot 0  body   (~46% of the surface)  the wooden body
    ///     slot 1  kit    (~45%)                 RED OR BLUE — this is what tells the teams apart
    ///     slot 2  head   (~9%)
    ///
    /// THE KIT ALWAYS CARRIES TEAM IDENTITY. Foosball is unplayable if the two sides look alike, so
    /// a skin never gets to paint both teams the same: it supplies a separate red and blue kit
    /// texture, or leaves the slot on the model's own team colour. The kit is authored per team
    /// rather than tinted from one greyscale map because a multiply tint can never produce WHITE
    /// stripes on a red shirt — white x red is red.
    ///
    /// Only the 0.50-0.85 height band of a figure is really visible in play (the body covers the
    /// rest), which is why every skin's team colour lives there. See
    /// <c>Art/Blender/make_figure_textures.py</c>.
    /// </summary>
    public static class FigureSkins
    {
        private const string Folder = "FigureSkins/";

        /// <summary>
        /// What a skin is made of. A skin with no <see cref="Stem"/> is colour-only: it tints the
        /// body and leaves the kit and head exactly as the model has them, which is how the three
        /// Common colours stay team-legible for free.
        /// </summary>
        private readonly struct Recipe
        {
            public readonly string Stem;        // kit texture stem, or null for a flat-colour kit
            public readonly Color? KitColor;    // a flat SHIRT colour, for the plain skins
            public readonly Color? BodyColor;   // a flat body colour, when there is no body texture
            public readonly bool BodyTex;
            public readonly bool HeadTex;
            public readonly float Metallic;
            public readonly float Smoothness;
            public readonly string KeeperStem;  // a distinct goalkeeper, or null

            public Recipe(string stem, Color? kitColor, Color? bodyColor, bool bodyTex, bool headTex,
                          float metallic, float smoothness, string keeperStem = null)
            {
                Stem = stem;
                KitColor = kitColor;
                BodyColor = bodyColor;
                BodyTex = bodyTex;
                HeadTex = headTex;
                Metallic = metallic;
                Smoothness = smoothness;
                KeeperStem = keeperStem;
            }
        }

        private static Color Hex(string s)
        {
            ColorUtility.TryParseHtmlString(s, out Color c);
            return c;
        }

        private static readonly Dictionary<string, Recipe> Recipes = new()
        {
            // Plain colours are SHIRT colours: the kit slot is the jersey, so "Yellow" means a team
            // in yellow shirts. Body and head are left as the model has them.
            { "figure.yellow", new Recipe(null, Hex("#E8C21C"), null, false, false, 0f, 0.45f) },
            { "figure.black",  new Recipe(null, Hex("#1E1E22"), null, false, false, 0f, 0.45f) },
            { "figure.green",  new Recipe(null, Hex("#2E8B3A"), null, false, false, 0f, 0.45f) },

            // Textured kit over plain shorts.
            { "figure.stripes", new Recipe("Stripes", null, Hex("#8A5F33"), false, false, 0f, 0.45f) },

            // Fully textured.
            { "figure.gladiators", new Recipe("Gladiators", null, null, true, true, 0.50f, 0.55f) },
            { "figure.robots",     new Recipe("Robots",     null, null, true, true, 0.85f, 0.72f) },
            { "figure.trojan",     new Recipe("Trojan",     null, null, true, true, 0.50f, 0.62f,
                                              keeperStem: "TrojanKeeper") }
        };

        /// <summary>Built materials, keyed by id + team + keeper + slot.</summary>
        private static readonly Dictionary<string, Material> Cache = new();

        public static bool HasArt(string id) => !string.IsNullOrEmpty(id) && Recipes.ContainsKey(id);

        /// <summary>Whether this skin dresses its goalkeeper differently. Only Trojan Warriors does.</summary>
        public static bool HasKeeperVariant(string id) =>
            HasArt(id) && Recipes[id].KeeperStem != null;

        /// <summary>
        /// The three materials for one figure, in slot order (body, kit, head).
        ///
        /// <paramref name="originals"/> is that figure's own material array — it differs per team
        /// (Foos_Blue vs Foos_Red), so it is both the clone source and the fallback. Any slot this
        /// skin does not dress comes back as the original, untouched.
        /// </summary>
        public static Material[] Resolve(string id, Team team, bool keeper, Material[] originals)
        {
            if (originals == null || originals.Length < 3 || !HasArt(id))
            {
                return originals;
            }

            Recipe r = Recipes[id];
            string stem = keeper && r.KeeperStem != null ? r.KeeperStem : r.Stem;
            string key = $"{id}|{team}|{(keeper && r.KeeperStem != null ? "K" : "-")}";

            var result = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                result[i] = originals[i];
            }

            // Body: a texture, a flat colour, or left exactly as the model has it.
            if (r.BodyTex || r.BodyColor.HasValue)
            {
                result[0] = Build(key + "|body", originals[0], () =>
                {
                    var m = SkinMaterials.Clone(originals[0], "FigBody_" + (stem ?? id));
                    if (r.BodyTex && stem != null)
                    {
                        var t = SkinMaterials.Load($"{Folder}FigureSkin_{stem}_Body");
                        if (t == null || !SkinMaterials.SetAlbedo(m, t)) return null;
                    }
                    else
                    {
                        SkinMaterials.SetFlatColor(m, r.BodyColor.Value);
                    }
                    SkinMaterials.SetFinish(m, r.Metallic, r.Smoothness);
                    return m;
                });
            }

            // Kit — the SHIRT. Either an authored per-team texture, or a flat jersey colour.
            if (stem != null)
            {
                result[1] = Build(key + "|kit", originals[1], () =>
                {
                    // Red or blue is chosen HERE, so a textured skin still reads as its wearer's
                    // side: a red player's Stripes are red-and-white, a blue player's blue-and-white.
                    string teamName = team == Team.Red ? "Red" : "Blue";
                    var t = SkinMaterials.Load($"{Folder}FigureSkin_{stem}_Kit{teamName}");
                    if (t == null) return null;
                    var m = SkinMaterials.Clone(originals[1], $"FigKit_{stem}_{teamName}");
                    if (!SkinMaterials.SetAlbedo(m, t)) return null;
                    SkinMaterials.SetFinish(m, r.Metallic, r.Smoothness);
                    return m;
                });
            }
            else if (r.KitColor.HasValue)
            {
                // A plain jersey colour REPLACES the team colour on this team's shirts. That is the
                // point of the skin — "Yellow" means yellow shirts — and it is safe because a skin
                // only ever dresses its own wearer's team; the opposing side keeps whatever it has.
                result[1] = Build(key + "|kitflat", originals[1], () =>
                {
                    var m = SkinMaterials.Clone(originals[1], "FigKit_" + id);
                    SkinMaterials.SetFlatColor(m, r.KitColor.Value);
                    SkinMaterials.SetFinish(m, r.Metallic, r.Smoothness);
                    return m;
                });
            }

            if (r.HeadTex && stem != null)
            {
                result[2] = Build(key + "|head", originals[2], () =>
                {
                    var t = SkinMaterials.Load($"{Folder}FigureSkin_{stem}_Head");
                    if (t == null) return null;
                    var m = SkinMaterials.Clone(originals[2], "FigHead_" + stem);
                    if (!SkinMaterials.SetAlbedo(m, t)) return null;
                    SkinMaterials.SetFinish(m, r.Metallic, r.Smoothness);
                    return m;
                });
            }

            return result;
        }

        /// <summary>Caches a built material, falling back to the original if the build failed — a
        /// missing texture must cost the figure one slot, not leave it invisible.</summary>
        private static Material Build(string key, Material fallback, System.Func<Material> make)
        {
            if (Cache.TryGetValue(key, out Material cached) && cached != null)
            {
                return cached;
            }

            Material built = make();
            if (built == null)
            {
                return fallback;
            }

            Cache[key] = built;
            return built;
        }

        public static void Clear() => Cache.Clear();
    }
}
