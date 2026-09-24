using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// The C# mirror of MASTER.md — the single source of truth for the Arcade Neon UI.
    /// Every color, size, radius, glow and timing used by the UI components comes from here.
    /// No component is allowed to hard-code a hex value or a duration; if it isn't in this file,
    /// it doesn't ship.
    ///
    /// Rounded corners and neon glows are baked procedurally into 9-sliced sprites at runtime, so
    /// the whole UI works with zero imported art assets.
    /// </summary>
    public static class ArcadeTheme
    {
        // ---- Palette ----
        // Black, blue and gold at roughly 60 / 30 / 10. Black is the floor every screen stands on;
        // blue is the panels and the ordinary buttons; gold is kept for the one thing on a screen the
        // player should press next, so it still means something when it appears. All three are held
        // back from full brightness — the earlier neon set read as a warning label, not a game.
        public static readonly Color BgDeep   = Hex("#07090E"); // the black floor
        public static readonly Color BgPanel  = Hex("#0F2036"); // panel blue
        public static readonly Color BgRaised = Hex("#16304F"); // rows, inputs and quiet buttons inside a panel
        public static readonly Color Line      = Hex("#1E3A5E"); // panel hairlines
        public static readonly Color Ink       = Hex("#E9EEF5");
        public static readonly Color InkMuted  = Hex("#93A1B5");
        public static readonly Color Gold      = Hex("#D6A73A"); // highlight — the next thing to press
        public static readonly Color OnGold    = Hex("#1A1304"); // dark ink for gold primary buttons
        public static readonly Color Go        = Hex("#4CC38A"); // online status ONLY — a live presence dot

        // The main blue button: a step brighter than a panel so it reads as pressable against one.
        public static readonly Color BlueFill = Hex("#1C4675");
        public static readonly Color BlueLine = Hex("#2A5A8F");
        // Soft blue for icons and small accents that sit on blue panels.
        public static readonly Color BlueSoft = Hex("#7FA6D1");

        // Team colours. Muted to sit inside the palette rather than shout over it; they still have
        // to tell two sides apart at a glance, so they differ in lightness as well as hue.
        public static readonly Color Red       = Hex("#C24A5A");
        public static readonly Color Blue      = Hex("#3E78B8");

        // Destructive actions (Delete Player): a pale red that reads as a warning on a blue panel.
        public static readonly Color Danger = Hex("#D98A93");

        // The title screen's sky: deep navy at the top lightening to a clear blue at the bottom.
        public static readonly Color SplashTop    = Hex("#02060F");
        public static readonly Color SplashBottom = Hex("#28609A");

        // The football accent — turf green. Deliberately a calmer, deeper green than Go, which is a
        // bright signal colour reserved for "this person is online right now". Pitch is a surface, a
        // backdrop, a mode's identity; Go is a light that turns on. Keeping them apart is what stops a
        // green card reading as an online indicator.
        public static readonly Color Pitch     = Hex("#2FBF6B");
        public static readonly Color PitchDark = Hex("#134A2C"); // turf in shadow, for pitch fills/lines

        // ---- Ranked league tiers ----
        // One accent per league, so a badge or a pod row reads its rank at a glance. Gold reuses the
        // existing brand gold; the other three are tier metals. Kept here, not in Net/, because a
        // colour is a UI fact — the ladder logic never needs to know what Bronze looks like.
        public static readonly Color Bronze  = Hex("#C77B3B");
        public static readonly Color Silver  = Hex("#B9C4D0");
        public static readonly Color Diamond = Hex("#5EE6E0");

        // ---- Currency ----
        // The coin is its own yellow, brighter and greener than the brand Gold. They sit two
        // centimetres apart in the main menu header — the ranked pill in Gold, the coin pill beside it
        // — and at the same hue one would read as a dimmer version of the other rather than as a
        // different thing. CoinDark is the coin's rim and the ink punched into its face.
        public static readonly Color Coin     = Hex("#FFD24A");
        public static readonly Color CoinDark = Hex("#A96E12");

        // ---- Cosmetic rarity ----
        // Four steps, deliberately not four arbitrary colours: Common is a neutral so it reads as "no
        // rarity" rather than as a fifth tier, and the other three climb through the spectrum the way
        // every collection game's players already expect. Used for a card's edge, its glow and its
        // rarity label, so one item is one colour everywhere it appears.
        // Common stays exactly as it was — it is deliberately neutral, the "no rarity" reading, and
        // brightening it would give it a rarity it is not supposed to have. Rare/Epic/Legendary are
        // each pushed a notch more saturated so a wall of cards sorts itself by colour at a glance
        // instead of the three upper tiers reading as similarly muted.
        public static readonly Color RarityCommon    = Hex("#8FA0B4");
        public static readonly Color RarityRare      = Hex("#2E9CFF");
        public static readonly Color RarityEpic      = Hex("#BD4DFF");
        public static readonly Color RarityLegendary = Hex("#FF7A1A");

        // ---- Brand ----
        // The game's name lives here and nowhere else. It used to be spelled three different ways
        // across three screens ("TABLE FOOTBALL", "FOOSBALL", and the window title); one constant is
        // what stops that happening again.
        public const string GameName = "MINI FOOTBALL";
        public const string GameNameA = "MINI";     // upper half of the two-tone lockup
        public const string GameNameB = "FOOTBALL"; // lower half, in Gold

        // ---- Spacing (4px base) ----
        public const float Xs = 4, Sm = 8, Md = 12, Lg = 16, Xl = 24, Xl2 = 32, Xl3 = 48, Xl4 = 64;

        // ---- Elevation ----
        // One shadow definition for every panel in the game. Wide and faint so it reads as the panel
        // lifting off the backdrop, rather than as a black rectangle nudged down and to the right.
        public const float ShadowFeather = 40f;
        public const float ShadowAlpha = 0.35f;

        /// <summary>
        /// How far full-screen backgrounds over-extend past their parent, in reference pixels.
        ///
        /// Menus live inside the safe-area inset so their controls clear the notch, but a backdrop
        /// that stopped at that inset would leave a strip of live table showing around the edge of an
        /// opaque menu. Backdrops and dims bleed past it instead. Comfortably larger than any real
        /// cutout, and invisible because these layers carry no detail near their edges.
        /// </summary>
        public const float Bleed = 200f;

        // ---- Screen furniture ----
        // The shared header row (UIFactory.ScreenHeader): its height, and the Back button's width.
        public const float HeaderHeight = 60f;
        public const float BackWidth = 170f;
        // Action cards (UIFactory.ActionCard): the icon tile, and the gold call-to-action pill.
        public const float CardIcon = 56f;
        public const float CallToActionWidth = 260f;
        // A stat on its own tile (UIFactory.StatTile).
        public const float StatTileHeight = 92f;

        // ---- Radii ----
        public const int RadSm = 8, RadMd = 14, RadLg = 20;

        // ---- Type scale (px at the 1080p reference the CanvasScaler uses) ----
        // Weighted, not uniform: the reference resolution already enlarges everything by 20%, and the
        // sizes that actually fail on a phone are the small ones. Captions and labels get a further
        // lift on top of that; the title and the score digits were never the problem.
        public const float FsScore = 88, FsTitle = 50, FsButton = 26, FsTeam = 18, FsBody = 22, FsCaption = 17;

        // ---- Motion durations (seconds) ----
        public const float TFast = 0.12f, TNormal = 0.20f, TSlow = 0.32f, TFlash = 0.70f;
        public const float Stagger = 0.04f;

        // The light sweep across the screen's one gold "start here" control (UIShine): a quick pass,
        // then a long rest, so it catches the eye now and then instead of flickering.
        public const float TShine = 0.9f, ShineRest = 2.8f;
        public const float ShineAlpha = 0.22f;

        // ---- Motion magnitudes ----
        public const float HoverScale = 1.04f, PressScale = 0.97f, ScorePop = 1.28f, MenuFrom = 0.92f;

        /// <summary>When true every tween snaps to its final state (accessibility). Defaults from PlayerPrefs.</summary>
        public static bool ReducedMotion
        {
            get => PlayerPrefs.GetInt("tf_reducedmotion", 0) == 1;
            set => PlayerPrefs.SetInt("tf_reducedmotion", value ? 1 : 0);
        }

        // ---- Easing (analytic, exact — no AnimationCurve assets) ----
        public static float EaseOut(float t) { t = Mathf.Clamp01(t); float 
                u = 1f - t; return 1f - u * u * u * u * u; } // quintic out
        public static float EaseIn(float t)  { t = Mathf.Clamp01(t); return t * t; }                                   // quad in (exits)
        public static float EaseSnap(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }        // cubic out

        /// <summary>
        /// Overshoots past the target and settles back. Used for entrances and for a control
        /// returning to rest after a press, which is what makes the UI feel sprung rather than
        /// merely animated. Deliberately mild: the classic 1.70158 constant is a visible bounce at
        /// menu scale, and this runs on every button.
        /// </summary>
        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.05f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ---- Fonts ----
        // Assign a condensed bold TMP font here (Oswald / Anton / Bebas) for the full arcade look;
        // both fall back to the TMP default so the UI renders before you assign anything.
        public static TMP_FontAsset DisplayFont;
        public static TMP_FontAsset UiFont;

        public static TMP_FontAsset ResolveDisplay() => DisplayFont != null ? DisplayFont : DefaultFont();
        public static TMP_FontAsset ResolveUi()      => UiFont != null ? UiFont : DefaultFont();
        private static TMP_FontAsset DefaultFont()
        {
            var f = TMP_Settings.defaultFontAsset;
            if (f == null) Debug.LogWarning("ArcadeTheme: no TMP default font found. Import TMP Essentials " +
                                            "(Window > TextMeshPro > Import TMP Essential Resources).");
            return f;
        }

        // ---- Procedural sprites (cached) ----
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>Crisp, anti-aliased filled rounded rectangle, 9-sliced. Tint via Image.color.</summary>
        public static Sprite RoundedSolid(int radius) => Bake($"solid{radius}", radius, 0f, false);

        /// <summary>Soft rounded halo — opaque inside, feathers outward. Used behind elements as neon glow.</summary>
        public static Sprite Glow(int radius, float feather) => Bake($"glow{radius}_{feather}", radius, feather, true);

        /// <summary>
        /// The menu backdrop: a radial wash from <see cref="BgRaised"/> at the centre out to
        /// <see cref="BgDeep"/>, with a vignette pulling the corners down further.
        ///
        /// Stretched across the whole screen, so it is baked small and deliberately not 9-sliced —
        /// a border would pin the gradient's edges and flatten it. The ±1/255 dither is the point of
        /// the whole thing: a smooth dark gradient banks into visible bands on mobile panels, and
        /// scattering the rounding error breaks the bands up before the eye can find them.
        /// </summary>
        public static Sprite RadialBackdrop()
        {
            const string key = "backdrop";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[size * size];
            float half = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = ((x + 0.5f) - half) / half;
                    float dy = ((y + 0.5f) - half) / half;
                    float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 1.41421356f);

                    // Eased so the lit centre stays broad and the falloff happens out near the edges,
                    // instead of a bright spot in the middle of an otherwise flat screen.
                    float t = r * r * (3f - 2f * r);
                    Color c = Color.Lerp(BgRaised, BgDeep, t);

                    // Vignette on top of the wash, strongest in the corners.
                    c *= Mathf.Lerp(1f, 0.72f, t * t);

                    float d = (Hash(x, y) - 0.5f) * (2f / 255f);
                    px[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt((c.r + d) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((c.g + d) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((c.b + d) * 255f), 0, 255),
                        255);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect);
            sprite.name = "ArcadeTheme_" + key;
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// The title screen backdrop: <see cref="SplashTop"/> at the top of the screen lightening to
        /// <see cref="SplashBottom"/> at the bottom, like looking up out of a floodlit stadium.
        ///
        /// Eased rather than linear, so the top half stays dark behind the wordmark — the ink and
        /// gold type need something to be brighter than — and the light gathers down by the prompt.
        ///
        /// Baked small, stretched to fill, and not 9-sliced — a border would pin the gradient's edges
        /// and flatten it. The ±1/255 dither matters more here than anywhere else in the UI: a
        /// full-screen gradient is exactly where a mobile panel banks into visible bands.
        /// </summary>
        public static Sprite SplashBackdrop()
        {
            const string key = "splash";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[size * size];
            const float last = size - 1;
            float half = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 0 at the top, 1 at the bottom. Texture y runs upward, hence the flip.
                    float t = (last - y) / last;
                    Color c = Color.Lerp(SplashTop, SplashBottom, t * t * (3f - 2f * t) * 0.35f + t * t * 0.65f);

                    // A faint side vignette, so the edges fall away and the light reads as centred.
                    float dx = ((x + 0.5f) - half) / half;
                    c *= Mathf.Lerp(1f, 0.82f, dx * dx);

                    float d = (Hash(x, y) - 0.5f) * (2f / 255f);
                    px[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt((c.r + d) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((c.g + d) * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt((c.b + d) * 255f), 0, 255),
                        255);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect);
            sprite.name = "ArcadeTheme_" + key;
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// A plain anti-aliased circle. Tint via Image.color.
        ///
        /// Not <see cref="RoundedSolid"/> with a big radius: that sprite is 9-sliced, so stretching it
        /// to anything other than a square pulls the straight edges apart and the circle turns into a
        /// capsule. The logo's ball needs to stay a ball.
        /// </summary>
        public static Sprite Disc()
        {
            const string key = "disc";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[size * size];
            float half = size / 2f;
            float radius = half - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - half;
                    float dy = (y + 0.5f) - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    float a = Mathf.Clamp01(0.5f - dist / 1.5f); // 1.5px AA edge, as in Bake
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect);
            sprite.name = "ArcadeTheme_" + key;
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// A black vertical scrim: clear at the top, opaque at the bottom.
        ///
        /// Laid over the bottom of a mode card's photo so the caption stays readable regardless of
        /// what the shot happens to contain. A screenshot with a pale table edge under the label is
        /// otherwise unreadable, and that cannot be fixed by choosing a better shot — the label has
        /// to survive every shot.
        /// </summary>
        public static Sprite Scrim()
        {
            const string key = "scrim";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int w = 8, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                // y = 0 is the bottom of a Unity texture, so the ramp runs from opaque up to clear.
                float t = y / (float)(h - 1);
                float a = Mathf.Clamp01(1f - t);
                a = a * a * 0.88f; // eased, so the fade starts gently instead of banding at the top
                var c = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                for (int x = 0; x < w; x++) px[y * w + x] = c;
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect);
            sprite.name = "ArcadeTheme_" + key;
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// A circular halo — opaque inside, feathering outward. The round counterpart to
        /// <see cref="Glow"/>, for the round icon buttons; the 9-sliced rounded-rect version turns
        /// into a lozenge as soon as the glow rect is stretched past its natural size.
        /// </summary>
        public static Sprite DiscGlow(float feather)
        {
            string key = $"discglow{feather}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            int radius = 24;
            int size = (radius + Mathf.CeilToInt(feather) + 2) * 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[size * size];
            float half = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - half;
                    float dy = (y + 0.5f) - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    float a = dist <= 0f ? 1f : Mathf.Clamp01(1f - dist / Mathf.Max(feather, 0.001f));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect);
            sprite.name = "ArcadeTheme_" + key;
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// A loader ring: an annulus whose opacity sweeps from full round to nearly nothing, so
        /// spinning it reads as a comet chasing its own tail rather than as a wheel that might not
        /// be turning at all.
        ///
        /// Baked rather than assembled from segments — a ring made of discrete pieces shows its
        /// seams at exactly the size a loader is used at.
        /// </summary>
        public static Sprite SpinnerRing()
        {
            const string key = "spinner";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[size * size];
            float half = size / 2f;
            const float outer = 60f;
            const float inner = 47f;
            float mid = (outer + inner) * 0.5f;
            float band = (outer - inner) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - half;
                    float dy = (y + 0.5f) - half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    // Distance from the centre line of the band, so both edges anti-alias together.
                    float edge = Mathf.Clamp01(0.5f - (Mathf.Abs(r - mid) - band) / 1.5f);

                    // 0 at the head of the sweep, rising to 1 all the way round.
                    float angle = Mathf.Atan2(dy, dx);
                    float t = (angle + Mathf.PI) / (Mathf.PI * 2f);
                    float tail = Mathf.Lerp(1f, 0.08f, t);

                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(edge * tail * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect);
            sprite.name = "ArcadeTheme_" + key;
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Deterministic 0–1 hash. Used to dither the backdrop; same pixel, same value.</summary>
        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        private static Sprite Bake(string key, int radius, float feather, bool glow)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            int border = radius + Mathf.CeilToInt(feather) + 2;
            int size = border * 2 + 2;
            float half = size / 2f;
            // A glow's solid core has to stop short of the texture edge by the feather, or the fade
            // falls outside the texture and is cut off — which is how every "soft" shadow in the game
            // used to render as a hard dark rectangle around its panel.
            float inset = glow ? Mathf.Ceil(feather) + 1f : 0f;
            float bx = half - radius - inset;
            float by = half - radius - inset;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - half;
                    float dy = (y + 0.5f) - half;
                    float qx = Mathf.Abs(dx) - bx;
                    float qy = Mathf.Abs(dy) - by;
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                               Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float dist = outside + inside - radius; // <0 inside, 0 on edge, >0 outside

                    float a = glow
                        ? (dist <= 0f ? 1f : Mathf.Clamp01(1f - dist / Mathf.Max(feather, 0.001f)))
                        : Mathf.Clamp01(0.5f - dist / 1.5f); // 1.5px AA edge

                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = "ArcadeTheme_" + key;
            Cache[key] = sprite;
            return sprite;
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        public static Color WithAlpha(this Color c, float a) { c.a = a; return c; }
    }
}
