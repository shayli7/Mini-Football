using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// Small building blocks for the UI Toolkit screens, the counterpart of <see cref="UIFactory"/>.
    ///
    /// Screens are built in code and styled by class from <c>Resources/UI/Styles</c>: this file only
    /// creates elements, hangs classes on them and wires behaviour. Anything about how a thing LOOKS —
    /// colour, size, radius, spacing, transitions — belongs in the stylesheet, not here.
    /// </summary>
    public static class UiKit
    {
        /// <summary>A plain element with the given space-separated classes, added to
        /// <paramref name="parent"/> if one is given.</summary>
        public static VisualElement El(string classes, VisualElement parent = null, string name = null)
        {
            var e = new VisualElement();
            if (name != null) e.name = name;
            AddClasses(e, classes);
            parent?.Add(e);
            return e;
        }

        /// <summary>A label. Text elements never take the pointer — the thing they sit on does.</summary>
        public static Label Text(string text, string classes, VisualElement parent = null)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            // A name another player chose must never be read as markup.
            l.enableRichText = false;
            AddClasses(l, classes);
            parent?.Add(l);
            return l;
        }

        /// <summary>Makes <paramref name="e"/> tappable: plays the UI click and runs the action.
        /// The <c>:hover</c> and <c>:active</c> styles in the stylesheet do the visual feedback.</summary>
        public static void OnTap(VisualElement e, Action action)
        {
            e.pickingMode = PickingMode.Position;
            e.AddManipulator(new Clickable(() =>
            {
                GameSfx.PlayUiClick();
                action?.Invoke();
            }));
        }

        /// <summary>A button: a tappable element with a label in it. <paramref name="variant"/> is
        /// one of <c>gold</c>, <c>blue</c>, <c>ghost</c> or <c>danger</c>.</summary>
        public static VisualElement Button(string label, string variant, Action onClick,
                                           VisualElement parent = null, string extraClasses = null)
        {
            var b = El("btn btn--" + variant + (extraClasses != null ? " " + extraClasses : string.Empty), parent);
            Text(label, "btn__label f-display", b);
            OnTap(b, onClick);
            return b;
        }

        /// <summary>
        /// The top row every full-screen menu shares: the title centred, Back in the top-left corner
        /// — always the same corner, so the way out never moves between screens. Returns the header,
        /// so a screen can hang something on its right (<c>.header__right</c>).
        /// </summary>
        public static VisualElement Header(VisualElement parent, string title, Action onBack)
        {
            var header = El("header", parent);
            Text(title, "header__title f-display", header);
            if (onBack != null)
            {
                var back = El("btn btn--ghost header__back", header);
                back.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f));
                Text("BACK", "btn__label f-display", back);
                OnTap(back, onBack);
            }
            return header;
        }

        public static void Show(VisualElement e, bool show)
        {
            if (e == null) return;
            e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static bool IsShown(VisualElement e) =>
            e != null && e.resolvedStyle.display != DisplayStyle.None && e.style.display != DisplayStyle.None;

        /// <summary>
        /// Plays the standard entrance on <paramref name="e"/> after <paramref name="delaySeconds"/>:
        /// it rises and fades in, by the transition on the <c>enter</c> class in the stylesheet.
        ///
        /// Three steps over three frames, because a USS transition only runs between two resolved
        /// styles: first the hidden pose is committed with transitions switched off (so it snaps there
        /// rather than animating out), then transitions are switched back on, then the hidden pose is
        /// removed and the element animates to rest. Skipped entirely under reduced motion.
        /// </summary>
        public static void Enter(VisualElement e, float delaySeconds = 0f)
        {
            if (e == null) return;
            e.AddToClassList("enter");
            if (ArcadeTheme.ReducedMotion)
            {
                e.RemoveFromClassList("enter-from");
                return;
            }

            e.AddToClassList("instant");
            e.AddToClassList("enter-from");
            e.schedule.Execute(() =>
            {
                e.RemoveFromClassList("instant");
                e.schedule.Execute(() => e.RemoveFromClassList("enter-from"))
                         .StartingIn((long)(Mathf.Max(0f, delaySeconds) * 1000f) + 16);
            });
        }

        /// <summary>
        /// Runs <paramref name="tick"/> every frame with the unscaled time, for as long as the element
        /// is on a panel. For the endless ambient motion (a breathing prompt, a sliding knob) that USS
        /// transitions cannot loop on their own. Pause the returned item when its screen hides.
        /// </summary>
        public static IVisualElementScheduledItem Loop(VisualElement e, Action<float> tick)
        {
            return e.schedule.Execute(() => tick(Time.unscaledTime)).Every(16);
        }

        /// <summary>Fades an element's opacity to <paramref name="to"/> over
        /// <paramref name="seconds"/>, then runs <paramref name="done"/>.</summary>
        public static void Fade(VisualElement e, float to, float seconds, Action done = null)
        {
            if (ArcadeTheme.ReducedMotion || seconds <= 0f)
            {
                e.style.opacity = to;
                done?.Invoke();
                return;
            }

            float from = e.resolvedStyle.opacity;
            float start = Time.unscaledTime;
            IVisualElementScheduledItem item = null;
            item = e.schedule.Execute(() =>
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / seconds);
                float k = 1f - (1f - t) * (1f - t) * (1f - t);
                e.style.opacity = Mathf.Lerp(from, to, k);
                if (t >= 1f)
                {
                    item.Pause();
                    done?.Invoke();
                }
            }).Every(16);
        }

        // ---------- textures ----------

        private static Texture2D topGlow;

        /// <summary>
        /// A soft blue light falling from the top centre of the screen — the one gradient the design
        /// has, which USS cannot draw. Baked once, small, and stretched to fill.
        /// </summary>
        public static Texture2D TopGlow()
        {
            if (topGlow != null) return topGlow;

            const int w = 64, h = 64;
            topGlow = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "MenuTopGlow"
            };

            Color c = ArcadeTheme.BlueFill;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Texture rows run bottom-up; the light sits at the top edge.
                    float dx = (x + 0.5f) / w - 0.5f;
                    float dy = 1f - (y + 0.5f) / h;
                    float d = Mathf.Sqrt(dx * dx * 1.4f + dy * dy * 2.2f);
                    float a = Mathf.Clamp01(1f - d / 0.75f);
                    a = a * a * 0.55f;
                    px[y * w + x] = new Color(c.r, c.g, c.b, a);
                }
            }

            topGlow.SetPixels32(px);
            topGlow.Apply(false, false);
            return topGlow;
        }

        private static void AddClasses(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (var c in classes.Split(' '))
            {
                if (c.Length > 0) e.AddToClassList(c);
            }
        }
    }
}
