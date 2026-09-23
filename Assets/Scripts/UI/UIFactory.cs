using System;
using TableFootball.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// Builds Arcade Neon UI elements in code — panels, buttons, text, sliders, segmented controls —
    /// all styled from <see cref="ArcadeTheme"/>. This is why the whole UI needs no imported art or
    /// hand-assembled prefabs: one component builds the tree at runtime.
    /// </summary>
    public static class UIFactory
    {
        // ---------- low-level helpers ----------

        public static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static RectTransform Rt(GameObject go) => (RectTransform)go.transform;

        /// <summary>Stretch to fill the parent with per-edge insets (positive = inset).</summary>
        public static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void Stretch(RectTransform rt, float inset = 0f) => Stretch(rt, inset, inset, inset, inset);

        public static Image RoundedImage(GameObject go, int radius, Color color, bool raycast)
        {
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.RoundedSolid(radius);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Image GlowImage(GameObject go, int radius, float feather, Color color)
        {
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.Glow(radius, feather);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // ---------- text ----------

        /// <summary>
        /// A label.
        ///
        /// <paramref name="richText"/> is a safety switch, not a styling one. TextMeshPro parses
        /// markup in whatever string it is handed — <c>&lt;size=400%&gt;</c>, <c>&lt;color&gt;</c>,
        /// <c>&lt;voffset&gt;</c>, <c>&lt;sprite&gt;</c> — and it is on by default. That is fine for
        /// the wording in this file, which is written here. It is not fine for a name another player
        /// chose: nothing in this game validates what arrives from the service, so a name is markup
        /// until it is told not to be. Pass false for anything that came from outside this build.
        /// </summary>
        public static TextMeshProUGUI Text(Transform parent, string content, float size, Color color,
                                           bool display = false, bool bold = true, bool upper = false,
                                           float tracking = 0f, TextAlignmentOptions align = TextAlignmentOptions.Center,
                                           bool richText = true)
        {
            var go = Child(parent, "Text");
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = display ? ArcadeTheme.ResolveDisplay() : ArcadeTheme.ResolveUi();
            t.fontSize = size;
            t.color = color;
            t.richText = richText;
            t.text = content;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.characterSpacing = tracking;
            FontStyles style = FontStyles.Normal;
            if (bold) style |= FontStyles.Bold;
            if (upper) style |= FontStyles.UpperCase;
            t.fontStyle = style;
            return t;
        }

        // ---------- backdrop ----------

        /// <summary>
        /// The full-screen menu backdrop. Replaces the flat <see cref="ArcadeTheme.BgDeep"/> fill that
        /// made every menu read as an empty black rectangle.
        ///
        /// Raycast-blocking, since it is the floor of a menu and nothing behind it should be
        /// clickable.
        /// </summary>
        public static Image Backdrop(Transform parent, string name = "Backdrop")
        {
            var go = Child(parent, name);
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.RadialBackdrop();
            img.type = Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = true;
            Stretch(Rt(go), -ArcadeTheme.Bleed);
            go.AddComponent<UIAmbientDrift>();
            return img;
        }

        /// <summary>
        /// The shared dim behind a menu that floats over the game: a translucent <see cref="ArcadeTheme.BgDeep"/>
        /// sheet, full-bleed and raycast-blocking. The lit table (or the frozen match) shows through it
        /// darkened, and nothing behind is clickable.
        ///
        /// One definition, used by the pause/settings overlay AND the account, friends and online
        /// screens, so they all wear the exact same background rather than each inventing its own — the
        /// single design language the front end reads in. It replaced the opaque <see cref="Backdrop"/>
        /// on those screens, which sealed the table off and made each one look like a different app
        /// from the menu it was opened from.
        /// </summary>
        public static Image ScrimDim(Transform parent, float alpha = 0.72f)
        {
            var go = Child(parent, "ScrimDim");
            var img = go.AddComponent<Image>();
            img.color = ArcadeTheme.BgDeep.WithAlpha(alpha);
            img.raycastTarget = true;
            Stretch(Rt(go), -ArcadeTheme.Bleed);
            return img;
        }

        /// <summary>
        /// The main menu's floor when the live 3D table is showing behind it: a translucent scrim
        /// instead of the opaque <see cref="Backdrop"/>, so the table reads through while the UI stays
        /// legible.
        ///
        /// Three layers: a flat dark tint over the whole screen for baseline contrast, then a stronger
        /// gradient banked into the top (behind the logo) and the bottom (behind the cards and footer)
        /// where the text actually sits — the middle, where the table is the hero, stays clearest.
        /// A pair of faint coloured glows read as stadium lights.
        ///
        /// Raycast-blocking on the base layer: it is still the floor of a menu, and the table behind
        /// it must not be clickable.
        /// </summary>
        public static Image StageScrim(Transform parent, string name = "StageScrim")
        {
            var go = Child(parent, name);
            var tint = go.AddComponent<Image>();
            tint.sprite = ArcadeTheme.RoundedSolid(ArcadeTheme.RadSm);
            tint.type = Image.Type.Sliced;
            tint.pixelsPerUnitMultiplier = 1f;
            // The top/bottom gradients below and the flat wash here were both tuned to fade OUT by
            // mid-screen, so the live table showed through most strongly in exactly the band the mode
            // cards sit in — a bright, busy pitch felt fighting the dark panels for attention rather
            // than sitting behind them. The flat wash now carries real weight on its own; the gradients
            // and stadium lights below layer accents on top of it rather than being the only thing
            // darkening the middle of the screen.
            tint.color = ArcadeTheme.BgDeep.WithAlpha(0.58f);
            tint.raycastTarget = true;
            Stretch(Rt(go), -ArcadeTheme.Bleed);

            // Bottom gradient: darkest at the very bottom, fading out by mid-screen. Behind the cards
            // and the footer.
            var bottom = Child(go.transform, "Bottom");
            var bimg = bottom.AddComponent<Image>();
            bimg.sprite = ArcadeTheme.Scrim();
            bimg.color = Color.white.WithAlpha(0.75f);
            bimg.raycastTarget = false;
            var brt = Rt(bottom);
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0.5f);
            brt.offsetMin = new Vector2(-ArcadeTheme.Bleed, -ArcadeTheme.Bleed);
            brt.offsetMax = new Vector2(ArcadeTheme.Bleed, 0f);

            // Top gradient: the same ramp flipped, behind the logo.
            var top = Child(go.transform, "Top");
            var timg = top.AddComponent<Image>();
            timg.sprite = ArcadeTheme.Scrim();
            timg.color = Color.white.WithAlpha(0.7f);
            timg.raycastTarget = false;
            var trt = Rt(top);
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(-ArcadeTheme.Bleed, 0f);
            trt.offsetMax = new Vector2(ArcadeTheme.Bleed, ArcadeTheme.Bleed);
            trt.localRotation = Quaternion.Euler(0f, 0f, 180f);

            // Stadium lights across the top, drifting so the light is never quite still. A warm gold
            // key light hangs over the centre — the dramatic overhead the flat scrim was missing —
            // flanked by the two cooler team-coloured lights that used to be the whole rig.
            StageGlow(go.transform, ArcadeTheme.Gold, new Vector2(0.5f, 1.02f), 0.17f, 1040f);
            StageGlow(go.transform, ArcadeTheme.Pitch, new Vector2(0.2f, 0.9f), 0.16f);
            StageGlow(go.transform, ArcadeTheme.Blue, new Vector2(0.8f, 0.92f), 0.16f);

            return tint;
        }

        private static void StageGlow(Transform parent, Color color, Vector2 anchor,
                                      float alpha = 0.14f, float size = 760f)
        {
            var go = Child(parent, "StadiumLight");
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.DiscGlow(40f);
            img.type = Image.Type.Simple;
            img.color = color.WithAlpha(alpha);
            img.raycastTarget = false;
            var rt = Rt(go);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            go.AddComponent<UIAmbientDrift>().Configure(20f, 34f);
        }

        // ---------- logo ----------

        /// <summary>
        /// The Mini Football wordmark: a ball-and-rods mark above "MINI" in ink and "FOOTBALL" in
        /// gold. Drawn from theme primitives rather than imported art, so it stays crisp at any size
        /// and re-tints with the palette — the same reason the rest of the UI has no art assets.
        ///
        /// Used at two sizes: full scale on the main menu, smaller on the pause overlay. Both call
        /// this, which is what finally makes the branding identical in both places.
        /// </summary>
        public static GameObject LogoLockup(Transform parent, float scale = 1f, bool withMark = true)
        {
            var root = Child(parent, "Logo");

            float markSize = 54f * scale;
            float gap = withMark ? markSize + ArcadeTheme.Md * scale : 0f;

            if (withMark)
            {
                var mark = Child(root.transform, "Mark");
                var mrt = Rt(mark);
                mrt.anchorMin = new Vector2(0.5f, 1f);
                mrt.anchorMax = new Vector2(0.5f, 1f);
                mrt.pivot = new Vector2(0.5f, 1f);
                mrt.sizeDelta = new Vector2(markSize * 2.6f, markSize);
                mrt.anchoredPosition = Vector2.zero;

                // Two rods crossing behind the ball, in the team colors — the mark ties the brand to
                // what the player actually grabs on the table.
                Bar(mark.transform, ArcadeTheme.Red, -markSize * 0.62f, markSize, scale);
                Bar(mark.transform, ArcadeTheme.Blue, markSize * 0.62f, markSize, scale);

                // A soft halo behind the ball, so the mark reads as lit rather than pasted flat onto
                // the scrim. Faint — it is identity, not a highlight.
                var halo = Child(mark.transform, "Halo");
                var haloImg = halo.AddComponent<Image>();
                haloImg.sprite = ArcadeTheme.DiscGlow(40f);
                haloImg.color = ArcadeTheme.Gold.WithAlpha(0.28f);
                haloImg.raycastTarget = false;
                var hrt = Rt(halo);
                hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
                hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = new Vector2(markSize * 1.5f, markSize * 1.5f);
                hrt.anchoredPosition = Vector2.zero;

                var ball = Child(mark.transform, "Ball");
                var ballImg = ball.AddComponent<Image>();
                ballImg.sprite = ArcadeTheme.Disc();
                ballImg.color = ArcadeTheme.Ink;
                ballImg.raycastTarget = false;
                var brt = Rt(ball);
                brt.anchorMin = new Vector2(0.5f, 0.5f);
                brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = new Vector2(markSize * 0.62f, markSize * 0.62f);
                brt.anchoredPosition = Vector2.zero;
            }

            float sizeA = ArcadeTheme.FsTitle * 0.62f * scale;
            float sizeB = ArcadeTheme.FsTitle * scale;

            var a = Text(root.transform, ArcadeTheme.GameNameA, sizeA, ArcadeTheme.Ink,
                         display: true, bold: true, upper: true, tracking: 22f);
            var art = Rt(a.gameObject);
            art.anchorMin = new Vector2(0f, 1f);
            art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(0.5f, 1f);
            art.offsetMin = new Vector2(0f, -(gap + sizeA * 1.15f));
            art.offsetMax = new Vector2(0f, -gap);

            var b = Text(root.transform, ArcadeTheme.GameNameB, sizeB, ArcadeTheme.Gold,
                         display: true, bold: true, upper: true, tracking: 10f);
            var brt2 = Rt(b.gameObject);
            brt2.anchorMin = new Vector2(0f, 1f);
            brt2.anchorMax = new Vector2(1f, 1f);
            brt2.pivot = new Vector2(0.5f, 1f);
            brt2.offsetMin = new Vector2(0f, -(gap + sizeA * 1.15f + sizeB * 1.2f));
            brt2.offsetMax = new Vector2(0f, -(gap + sizeA * 1.15f));

            return root;
        }

        /// <summary>One tilted rod in the logo mark.</summary>
        private static void Bar(Transform parent, Color color, float x, float markSize, float scale)
        {
            var bar = Child(parent, "Rod");
            RoundedImage(bar, ArcadeTheme.RadSm, color, false);
            var rt = Rt(bar);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(markSize * 1.15f, 7f * scale);
            rt.anchoredPosition = new Vector2(x, 0f);
        }

        // ---------- panel ----------

        /// <summary>A charcoal panel with a 1.5px neon-neutral border and a soft drop shadow.</summary>
        public static GameObject Panel(Transform parent, string name = "Panel")
        {
            var root = Child(parent, name);

            // Soft drop shadow behind. Spread nearly evenly with only a slight downward bias: the
            // old version reached 14px below and pulled 2px in at the top, which read as a hard black
            // rectangle offset behind the panel rather than as depth.
            var shadow = Child(root.transform, "Shadow");
            GlowImage(shadow, ArcadeTheme.RadLg, ArcadeTheme.ShadowFeather,
                      Color.black.WithAlpha(ArcadeTheme.ShadowAlpha));
            Stretch(Rt(shadow), -12, -16, -12, -8);

            // border
            var border = Child(root.transform, "Border");
            RoundedImage(border, ArcadeTheme.RadLg, ArcadeTheme.Line, false);
            Stretch(Rt(border), 0);

            // fill (inset 1.5px shows the border)
            var fill = Child(root.transform, "Fill");
            RoundedImage(fill, ArcadeTheme.RadLg, ArcadeTheme.BgPanel, true);
            Stretch(Rt(fill), 1.5f);

            return root;
        }

        // ---------- button ----------

        /// <summary>
        /// A thin, faint bright bar hugging the top inside edge of a control, read as a bevel catching
        /// the light. The cheapest honest way to add depth without a second baked sprite per radius.
        /// </summary>
        private static void InnerHighlight(Transform fill)
        {
            var hi = Child(fill, "InnerHighlight");
            var img = hi.AddComponent<Image>();
            img.sprite = ArcadeTheme.RoundedSolid(ArcadeTheme.RadSm);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = Color.white.WithAlpha(0.06f);
            img.raycastTarget = false;
            var rt = Rt(hi);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(10f, -6f);
            rt.offsetMax = new Vector2(-10f, -2f);
        }

        public static MenuButton Button(Transform parent, string label, MenuButton.Variant variant,
                                        Action onClick, float height = 62f)
        {
            var root = Child(parent, "Button_" + label);
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            // glow (back)
            var glowGo = Child(root.transform, "Glow");
            var glow = GlowImage(glowGo, ArcadeTheme.RadMd, 26f, ArcadeTheme.Gold.WithAlpha(0f));
            Stretch(Rt(glowGo), -16, -16, -16, -16);

            // border
            var borderGo = Child(root.transform, "Border");
            var border = RoundedImage(borderGo, ArcadeTheme.RadMd, ArcadeTheme.Line, false);
            Stretch(Rt(borderGo), 0);

            // fill (raycast target)
            var fillGo = Child(root.transform, "Fill");
            var fill = RoundedImage(fillGo, ArcadeTheme.RadMd, ArcadeTheme.BgRaised, true);
            Stretch(Rt(fillGo), 1.5f);

            // A faint inner highlight along the top edge, catching the light — the touch that turns a
            // flat rectangle into a raised, moulded control. Non-raycast, so it never intercepts a
            // tap, and thin enough to read as a bevel rather than a second bar.
            InnerHighlight(fillGo.transform);

            // label
            var lab = Text(root.transform, label, ArcadeTheme.FsButton, ArcadeTheme.Ink,
                           display: false, bold: true, upper: true, tracking: 8f);
            Stretch(Rt(lab.gameObject), 0);

            var btn = root.AddComponent<MenuButton>();
            btn.fill = fill; btn.border = border; btn.glow = glow; btn.label = lab;
            btn.targetGraphic = fill;
            btn.Configure(variant);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        // ---------- loader ----------

        /// <summary>
        /// A spinning ring with a caption under it, for waits with nothing to report but that they
        /// are still going.
        ///
        /// The ring is its own child so it can rotate without taking the caption round with it.
        /// </summary>
        public static GameObject Loader(Transform parent, string caption = "loading…", float size = 84f)
        {
            var root = Child(parent, "Loader");

            var v = root.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleCenter;
            v.spacing = ArcadeTheme.Lg;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            v.childControlWidth = true; v.childControlHeight = true;

            // A holder the layout can size, with the ring spinning freely inside it — rotating a
            // layout-controlled rect makes the group re-measure it every frame and the caption
            // underneath jitters.
            var holder = Child(root.transform, "RingHolder");
            var hle = holder.AddComponent<LayoutElement>();
            hle.preferredHeight = size;
            hle.minHeight = size;

            var ring = Child(holder.transform, "Ring");
            var img = ring.AddComponent<Image>();
            img.sprite = ArcadeTheme.SpinnerRing();
            img.color = ArcadeTheme.Gold;
            img.raycastTarget = false;
            var rrt = Rt(ring);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(size, size);
            rrt.anchoredPosition = Vector2.zero;
            ring.AddComponent<UISpinner>();

            var label = Text(root.transform, caption, ArcadeTheme.FsBody, ArcadeTheme.InkMuted,
                             display: false, bold: true, upper: true, tracking: 6f);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            return root;
        }

        // ---------- round icon buttons ----------

        /// <summary>Which glyph a round icon button draws.</summary>
        public enum Icon { Door, Person, Sliders, Eye, Dice, Trophy, Store, Path }

        private static Image CircleImage(GameObject go, Color color, bool raycast)
        {
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.Disc();
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>
        /// A round icon button: border ring, fill, halo and a glyph drawn from theme primitives.
        ///
        /// Built on <see cref="MenuButton"/> like every other control, so hover, press, focus, submit
        /// and the click sound all behave identically — an icon button that grew its own input
        /// handling would be a second thing to keep in step with the first.
        /// </summary>
        public static MenuButton IconButton(Transform parent, string name, Icon icon,
                                            MenuButton.Variant variant, Action onClick,
                                            float size = 64f, bool interactable = true)
        {
            var root = Child(parent, "Icon_" + name);
            var rrt = Rt(root);
            rrt.sizeDelta = new Vector2(size, size);

            var glowGo = Child(root.transform, "Glow");
            var glow = glowGo.AddComponent<Image>();
            glow.sprite = ArcadeTheme.DiscGlow(22f);
            glow.raycastTarget = false;
            glow.color = ArcadeTheme.Gold.WithAlpha(0f);
            Stretch(Rt(glowGo), -16);

            var borderGo = Child(root.transform, "Border");
            var border = CircleImage(borderGo, ArcadeTheme.Line, false);
            Stretch(Rt(borderGo), 0);

            var fillGo = Child(root.transform, "Fill");
            var fill = CircleImage(fillGo, ArcadeTheme.BgRaised, true);
            Stretch(Rt(fillGo), 2f);

            var glyph = Child(root.transform, "Glyph");
            Stretch(Rt(glyph), size * 0.22f);
            BuildIcon(glyph.transform, icon);

            var btn = root.AddComponent<MenuButton>();
            btn.fill = fill; btn.border = border; btn.glow = glow; btn.label = null;
            btn.targetGraphic = fill;
            btn.Configure(variant);
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            btn.interactable = interactable;
            if (!interactable)
            {
                var dim = root.AddComponent<CanvasGroup>();
                dim.alpha = 0.4f;
                dim.interactable = false;
                dim.blocksRaycasts = true;
            }

            return btn;
        }

        /// <summary>
        /// A round button carrying the player's initial — the profile entry point.
        ///
        /// A letter rather than another person glyph: the friends list already owns that silhouette,
        /// and two identical figures on one screen would be asking the player to work out which of
        /// them is themselves. The initial is also the fastest confirmation that the game knows who
        /// they are.
        ///
        /// The caller sets the letter through <see cref="MenuButton.label"/>, so it re-tints on hover
        /// with everything else.
        /// </summary>
        public static MenuButton AvatarButton(Transform parent, Action onClick, float size = 64f)
        {
            var root = Child(parent, "Icon_Profile");
            Rt(root).sizeDelta = new Vector2(size, size);

            var glowGo = Child(root.transform, "Glow");
            var glow = glowGo.AddComponent<Image>();
            glow.sprite = ArcadeTheme.DiscGlow(22f);
            glow.raycastTarget = false;
            glow.color = ArcadeTheme.Gold.WithAlpha(0f);
            Stretch(Rt(glowGo), -16);

            var borderGo = Child(root.transform, "Border");
            var border = CircleImage(borderGo, ArcadeTheme.Line, false);
            Stretch(Rt(borderGo), 0);

            var fillGo = Child(root.transform, "Fill");
            var fill = CircleImage(fillGo, ArcadeTheme.BgRaised, true);
            Stretch(Rt(fillGo), 2f);

            // Filled in with the first letter of the player's name — see MainMenu.RefreshAvatar — so
            // it is a name's worth of untrusted text however short it is.
            var initial = Text(root.transform, "?", size * 0.46f, ArcadeTheme.Ink,
                               display: true, bold: true, upper: true, richText: false);
            Stretch(Rt(initial.gameObject), 0);

            var btn = root.AddComponent<MenuButton>();
            btn.fill = fill; btn.border = border; btn.glow = glow; btn.label = initial;
            btn.targetGraphic = fill;
            btn.Configure(MenuButton.Variant.Neutral);
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            return btn;
        }

        private static void BuildIcon(Transform g, Icon icon)
        {
            switch (icon)
            {
                case Icon.Door:
                    // A door panel with its handle. An exit arrow as well would be two ideas fighting
                    // for the same 30 pixels; the door alone already means "out".
                    var door = Child(g, "Door");
                    RoundedImage(door, ArcadeTheme.RadSm, ArcadeTheme.Ink, false);
                    var drt = Rt(door);
                    drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
                    drt.pivot = new Vector2(0.5f, 0.5f);
                    drt.sizeDelta = new Vector2(22f, 30f);
                    drt.anchoredPosition = Vector2.zero;
                    Dot(door.transform, ArcadeTheme.Red, new Vector2(6f, -1f), 5f);
                    break;

                case Icon.Person:
                    // Head over shoulders, the shoulders being a disc whose lower half the mask cuts
                    // away — the silhouette every avatar placeholder uses.
                    //
                    // The mask clips the head too, so the head has to sit entirely inside the glyph
                    // rect. It did not: a 15px disc centred 10 above the middle reached 17.5 up in a
                    // box only 14 tall, and the top of the head was sliced flat.
                    var clip = Child(g, "Clip");
                    Stretch(Rt(clip), 0);
                    clip.AddComponent<RectMask2D>();
                    Dot(clip.transform, ArcadeTheme.Ink, new Vector2(0f, 7f), 14f);
                    Dot(clip.transform, ArcadeTheme.Ink, new Vector2(0f, -17f), 28f);
                    break;

                case Icon.Sliders:
                    // Three sliders, because that is literally what the settings panel contains:
                    // music volume, sfx volume, difficulty. A gear would say "options" in the
                    // abstract; this says what is behind the button.
                    Bar3(g, 10f, -6f);
                    Bar3(g, 0f, 5f);
                    Bar3(g, -10f, -2f);
                    break;

                case Icon.Eye:
                    // A lens with a pupil — the show/hide-password toggle every text app uses. Drawn
                    // as an outer Ink lens, a lighter inner, and a dark pupil, so it reads as an eye at
                    // this size where a single outline would just be a blob.
                    var lens = Child(g, "Lens");
                    RoundedImage(lens, ArcadeTheme.RadLg, ArcadeTheme.Ink, false);
                    var lrt = Rt(lens);
                    lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                    lrt.pivot = new Vector2(0.5f, 0.5f);
                    lrt.sizeDelta = new Vector2(30f, 18f);
                    lrt.anchoredPosition = Vector2.zero;

                    var lensInner = Child(g, "LensInner");
                    RoundedImage(lensInner, ArcadeTheme.RadLg, ArcadeTheme.BgRaised, false);
                    var irt = Rt(lensInner);
                    irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                    irt.pivot = new Vector2(0.5f, 0.5f);
                    irt.sizeDelta = new Vector2(24f, 12f);
                    irt.anchoredPosition = Vector2.zero;

                    Dot(g, ArcadeTheme.Ink, Vector2.zero, 9f);
                    break;

                case Icon.Trophy:
                    // A cup: bowl, two handles, stem and base. The ranked mark — a league badge would
                    // have to change with the tier, and this button means "ranked" whatever tier you
                    // are in. Drawn in Gold by TrophyGlyph's caller tint.
                    TrophyGlyph(g, ArcadeTheme.Gold, ArcadeTheme.BgRaised);
                    break;

                case Icon.Dice:
                    // A five-pip die — "roll me a random one". The dots are the panel colour punched
                    // out of an Ink body, the same figure-and-ground trick the other glyphs use.
                    var die = Child(g, "Die");
                    RoundedImage(die, ArcadeTheme.RadSm, ArcadeTheme.Ink, false);
                    var drt2 = Rt(die);
                    drt2.anchorMin = drt2.anchorMax = new Vector2(0.5f, 0.5f);
                    drt2.pivot = new Vector2(0.5f, 0.5f);
                    drt2.sizeDelta = new Vector2(30f, 30f);
                    drt2.anchoredPosition = Vector2.zero;
                    Dot(die.transform, ArcadeTheme.BgRaised, new Vector2(-7f, 7f), 6f);
                    Dot(die.transform, ArcadeTheme.BgRaised, new Vector2(7f, 7f), 6f);
                    Dot(die.transform, ArcadeTheme.BgRaised, new Vector2(0f, 0f), 6f);
                    Dot(die.transform, ArcadeTheme.BgRaised, new Vector2(-7f, -7f), 6f);
                    Dot(die.transform, ArcadeTheme.BgRaised, new Vector2(7f, -7f), 6f);
                    break;

                case Icon.Store:
                    // A shop awning over a counter: three coloured stripes on a bar, with the shop
                    // front under it. A shopping trolley or a bag would be the wrong promise — nothing
                    // here is bought with money, and a till reads as a real-currency store. An awning
                    // says "a place with things on display", which is exactly what it is.
                    Rod(g, ArcadeTheme.Coin, new Vector2(0f, 9f), 32f, 10f, 0f);
                    Rod(g, ArcadeTheme.CoinDark, new Vector2(-8f, 9f), 8f, 10f, 0f);
                    Rod(g, ArcadeTheme.CoinDark, new Vector2(8f, 9f), 8f, 10f, 0f);

                    var front = Child(g, "Front");
                    RoundedImage(front, ArcadeTheme.RadSm, ArcadeTheme.Ink, false);
                    var frt = Rt(front);
                    frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
                    frt.pivot = new Vector2(0.5f, 0.5f);
                    frt.sizeDelta = new Vector2(26f, 18f);
                    frt.anchoredPosition = new Vector2(0f, -6f);

                    // The doorway, punched out of the shop front in the button's own fill colour.
                    var doorway = Child(front.transform, "Doorway");
                    RoundedImage(doorway, 2, ArcadeTheme.BgRaised, false);
                    var wrt = Rt(doorway);
                    wrt.anchorMin = new Vector2(0.5f, 0f);
                    wrt.anchorMax = new Vector2(0.5f, 0f);
                    wrt.pivot = new Vector2(0.5f, 0f);
                    wrt.sizeDelta = new Vector2(10f, 12f);
                    wrt.anchoredPosition = Vector2.zero;
                    break;

                case Icon.Path:
                    // Three rungs climbing to the right, the shape of the level path itself seen from
                    // the side — and the top one gold, because the thing worth walking it for is the
                    // reward at the end. A star or a chest would say "reward" without saying "path",
                    // and the button is the way IN to the path, not the prize.
                    Rod(g, ArcadeTheme.InkMuted, new Vector2(-10f, -10f), 14f, 7f, 0f);
                    Rod(g, ArcadeTheme.Ink, new Vector2(0f, 0f), 14f, 7f, 0f);
                    Rod(g, ArcadeTheme.Coin, new Vector2(10f, 10f), 14f, 7f, 0f);
                    break;
            }
        }

        /// <summary>One slider line with its knob, for the settings glyph.</summary>
        private static void Bar3(Transform parent, float y, float knobX)
        {
            var line = Child(parent, "Line");
            RoundedImage(line, 2, ArcadeTheme.Ink.WithAlpha(0.7f), false);
            var lrt = Rt(line);
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(30f, 3f);
            lrt.anchoredPosition = new Vector2(0f, y);

            Dot(parent, ArcadeTheme.Ink, new Vector2(knobX, y), 9f);
        }

        /// <summary>
        /// The trophy cup — handles, bowl, stem and base — sized from <paramref name="scale"/> (1 being
        /// the ~32-unit glyph an <see cref="IconButton"/> holds).
        ///
        /// Public so the ranked chip can draw the same cup, larger, beside its score. A second trophy
        /// hand-drawn at the other size would be a subtly different trophy, and the two sit two
        /// centimetres apart on the same screen.
        ///
        /// <paramref name="holeColor"/> is punched through the handles, so it must match whatever the
        /// cup is sitting on or the loops fill in solid.
        /// </summary>
        public static void TrophyGlyph(Transform parent, Color color, Color holeColor, float scale = 1f)
        {
            // Handles first, so the bowl draws over their inner edge and they read as attached to the
            // cup rather than as two rings parked beside it.
            TrophyHandle(parent, color, holeColor, -13f * scale, scale);
            TrophyHandle(parent, color, holeColor, 13f * scale, scale);

            var bowl = Child(parent, "Bowl");
            RoundedImage(bowl, ArcadeTheme.RadSm, color, false);
            var brt = Rt(bowl);
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(24f * scale, 21f * scale);
            brt.anchoredPosition = new Vector2(0f, 7f * scale);

            Rod(parent, color, new Vector2(0f, -6f * scale), 9f * scale, 5f * scale, 90f);  // stem
            Rod(parent, color, new Vector2(0f, -12f * scale), 20f * scale, 5f * scale, 0f); // base
        }

        /// <summary>One looped handle on the side of the cup.</summary>
        private static void TrophyHandle(Transform parent, Color color, Color holeColor,
                                         float x, float scale)
        {
            var ring = Child(parent, "Handle");
            var img = ring.AddComponent<Image>();
            img.sprite = ArcadeTheme.Disc();
            img.color = color;
            img.raycastTarget = false;
            var rt = Rt(ring);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(16f * scale, 16f * scale);
            rt.anchoredPosition = new Vector2(x, 8f * scale);

            // Punched out, so the handle reads as a loop rather than a blob on the side of the cup.
            var hole = Child(ring.transform, "Hole");
            var himg = hole.AddComponent<Image>();
            himg.sprite = ArcadeTheme.Disc();
            himg.color = holeColor;
            himg.raycastTarget = false;
            Stretch(Rt(hole), 4.5f * scale);
        }

        /// <summary>
        /// A pulsing green "online" pip, as a fixed-width layout cell. The dot pulses inside a cell the
        /// layout has already sized, so the throb never makes the row re-measure and jitter.
        /// </summary>
        /// <summary>
        /// A presence dot, always drawn — green and pulsing when online, a static muted grey when not.
        /// Used to be built only for the online case, which left an offline row with no status marker
        /// at all (just its own text drawn muted) rather than a consistent slot every row carries; a
        /// list of friends reads at a glance when every row has the same dot in the same place, lit
        /// differently, rather than some rows growing a dot and others not.
        /// </summary>
        public static GameObject OnlinePip(Transform parent, bool online, float size = 18f)
        {
            var cell = Child(parent, "OnlinePip");
            var le = cell.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.minWidth = size;
            le.preferredHeight = size;

            var dot = Child(cell.transform, "Dot");
            var img = dot.AddComponent<Image>();
            img.sprite = ArcadeTheme.Disc();
            img.color = online ? ArcadeTheme.Go : ArcadeTheme.InkMuted.WithAlpha(0.55f);
            img.raycastTarget = false;
            var rt = Rt(dot);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);

            // Pulsing only when online — a "live" animation on someone who is NOT online would claim
            // an activity that is not happening.
            if (online) dot.AddComponent<UIPulse>();
            return cell;
        }

        // ---------- tile badges ----------

        /// <summary>
        /// Which glyph a mode card carries in its corner.
        ///
        /// The cards are all photographs of the same table from the same locked camera — that is what
        /// makes them look like a set. It also means the photo cannot say which mode it is. The badge
        /// carries that meaning instead, which is the only way to distinguish Online from Local when
        /// there is one table in the world and no second one to photograph.
        /// </summary>
        public enum Badge { None, Online, Local, PlayerVsPlayer, AiVsPlayer }

        /// <summary>Draws the badge chip in the top-left of a card's picture area.</summary>
        private static void BuildBadge(Transform picture, Badge kind)
        {
            if (kind == Badge.None) return;

            const float chip = 56f;

            var chipGo = Child(picture, "Badge");
            RoundedImage(chipGo, ArcadeTheme.RadMd, ArcadeTheme.BgDeep.WithAlpha(0.72f), false);
            var crt = Rt(chipGo);
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.sizeDelta = new Vector2(chip, chip);
            crt.anchoredPosition = new Vector2(ArcadeTheme.Md, -ArcadeTheme.Md);

            switch (kind)
            {
                case Badge.Online:
                    // Two nodes joined by a link — a connection, not a globe. Reads at 56px; a globe
                    // turns to mush at this size.
                    Dot(chipGo.transform, ArcadeTheme.Gold, new Vector2(-13f, 9f), 13f);
                    Dot(chipGo.transform, ArcadeTheme.Gold, new Vector2(13f, -9f), 13f);
                    Rod(chipGo.transform, ArcadeTheme.Gold, new Vector2(0f, 0f), 26f, 5f, -35f);
                    break;

                case Badge.Local:
                    // One table seen from above, ball at the centre spot.
                    Rod(chipGo.transform, ArcadeTheme.Ink, new Vector2(0f, 13f), 34f, 4f, 0f);
                    Rod(chipGo.transform, ArcadeTheme.Ink, new Vector2(0f, -13f), 34f, 4f, 0f);
                    Rod(chipGo.transform, ArcadeTheme.Ink.WithAlpha(0.55f), new Vector2(0f, 0f), 34f, 3f, 0f);
                    Dot(chipGo.transform, ArcadeTheme.Gold, Vector2.zero, 11f);
                    break;

                case Badge.PlayerVsPlayer:
                    // Two handles facing off, one per team.
                    Rod(chipGo.transform, ArcadeTheme.Red, new Vector2(-10f, 0f), 30f, 8f, 90f);
                    Rod(chipGo.transform, ArcadeTheme.Blue, new Vector2(10f, 0f), 30f, 8f, 90f);
                    break;

                case Badge.AiVsPlayer:
                    // A human handle on the left, a machine's eye on the right.
                    Rod(chipGo.transform, ArcadeTheme.Blue, new Vector2(-13f, 0f), 30f, 8f, 90f);
                    var head = Child(chipGo.transform, "Head");
                    RoundedImage(head, ArcadeTheme.RadSm, ArcadeTheme.Red, false);
                    var hrt = Rt(head);
                    hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
                    hrt.pivot = new Vector2(0.5f, 0.5f);
                    hrt.sizeDelta = new Vector2(24f, 22f);
                    hrt.anchoredPosition = new Vector2(12f, -2f);
                    Dot(head.transform, ArcadeTheme.BgDeep, new Vector2(-5f, 1f), 6f);
                    Dot(head.transform, ArcadeTheme.BgDeep, new Vector2(5f, 1f), 6f);
                    Rod(chipGo.transform, ArcadeTheme.Red, new Vector2(12f, 13f), 8f, 3f, 90f);
                    break;
            }
        }

        /// <summary>A filled circle inside a badge.</summary>
        private static void Dot(Transform parent, Color color, Vector2 pos, float size)
        {
            var go = Child(parent, "Dot");
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.Disc();
            img.color = color;
            img.raycastTarget = false;
            var rt = Rt(go);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;
        }

        /// <summary>A rounded bar inside a badge, optionally rotated.</summary>
        private static void Rod(Transform parent, Color color, Vector2 pos, float length, float thickness, float angle)
        {
            var go = Child(parent, "Rod");
            RoundedImage(go, ArcadeTheme.RadSm, color, false);
            var rt = Rt(go);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(length, thickness);
            rt.anchoredPosition = pos;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// A small green disc with a check mark, pinned to a corner to say "this is what you have
        /// equipped" — unambiguous at a glance, rather than the footer's own small "EQUIPPED" word
        /// being the only signal a card is worn versus merely owned.
        /// </summary>
        public static GameObject EquippedCheck(Transform parent, float size = 28f)
        {
            var badge = Child(parent, "EquippedCheck");
            var rt = Rt(badge);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);

            // A dark ring behind the green disc, so the badge reads as its own chip against whatever
            // colour the card happens to be behind it, rather than blending into the card fill.
            var ring = Child(badge.transform, "Ring");
            var ringImg = ring.AddComponent<Image>();
            ringImg.sprite = ArcadeTheme.Disc();
            ringImg.color = ArcadeTheme.BgDeep;
            ringImg.raycastTarget = false;
            var ringRt = Rt(ring);
            ringRt.anchorMin = ringRt.anchorMax = new Vector2(0.5f, 0.5f);
            ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.sizeDelta = new Vector2(size * 1.18f, size * 1.18f);

            var disc = Child(badge.transform, "Disc");
            var discImg = disc.AddComponent<Image>();
            discImg.sprite = ArcadeTheme.Disc();
            discImg.color = ArcadeTheme.Go;
            discImg.raycastTarget = false;
            Stretch(Rt(disc), 0);

            // The check: two short bars meeting at an elbow, derived from three points at build time
            // rather than hand-tuned numbers, so it stays a correct check mark at any size passed in.
            Vector2 a = new Vector2(-0.30f, -0.02f) * size;
            Vector2 b = new Vector2(-0.06f, -0.24f) * size;
            Vector2 c = new Vector2(0.32f, 0.22f) * size;
            float thickness = Mathf.Max(2.4f, size * 0.14f);
            CheckSegment(disc.transform, a, b, thickness);
            CheckSegment(disc.transform, b, c, thickness);

            return badge;
        }

        private static void CheckSegment(Transform parent, Vector2 from, Vector2 to, float thickness)
        {
            Vector2 delta = to - from;
            // A hair longer than the raw distance so the two segments' rounded ends overlap cleanly
            // at the elbow instead of leaving a visible notch.
            float length = delta.magnitude + thickness * 0.5f;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Vector2 mid = (from + to) * 0.5f;
            Rod(parent, Color.white, mid, length, thickness, angle);
        }

        // ---------- pitch illustration ----------

        /// <summary>
        /// A stylised top-down pitch, drawn into a mode card that has no photo. It replaces the flat
        /// "ARTWORK" placeholder, which read as an empty frame waiting for a picture — the very thing
        /// that made the cards look like images pasted onto the menu. Chalk on turf, from the same
        /// primitives as the badges, so the whole set stays art-free and consistent.
        /// </summary>
        private static void PitchArt(Transform picture)
        {
            Color chalk = Color.white.WithAlpha(0.30f);

            // The halfway line, across the middle.
            var line = Child(picture, "Halfway");
            var limg = line.AddComponent<Image>();
            limg.sprite = ArcadeTheme.RoundedSolid(2);
            limg.type = Image.Type.Sliced;
            limg.pixelsPerUnitMultiplier = 1f;
            limg.color = chalk;
            limg.raycastTarget = false;
            var lrt = Rt(line);
            lrt.anchorMin = new Vector2(0.08f, 0.5f);
            lrt.anchorMax = new Vector2(0.92f, 0.5f);
            lrt.offsetMin = new Vector2(0f, -1.5f);
            lrt.offsetMax = new Vector2(0f, 1.5f);

            // The centre circle — a ring left by punching turf out of a white disc, the same
            // figure-and-ground trick the eye glyph uses.
            var ring = Child(picture, "CentreRing");
            var rimg = ring.AddComponent<Image>();
            rimg.sprite = ArcadeTheme.Disc();
            rimg.color = chalk;
            rimg.raycastTarget = false;
            var rrt = Rt(ring);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(92f, 92f);
            var ringTurf = Child(ring.transform, "Turf");
            var rtimg = ringTurf.AddComponent<Image>();
            rtimg.sprite = ArcadeTheme.Disc();
            rtimg.color = ArcadeTheme.Pitch;
            rtimg.raycastTarget = false;
            Stretch(Rt(ringTurf), 3f);

            GoalArea(picture, chalk, true);
            GoalArea(picture, chalk, false);

            // The ball on the centre spot — the one warm point among the chalk.
            Dot(picture, ArcadeTheme.Gold, Vector2.zero, 14f);
        }

        /// <summary>One penalty box, a white outline near the top or bottom edge of the pitch.</summary>
        private static void GoalArea(Transform picture, Color chalk, bool top)
        {
            var box = Child(picture, "GoalArea");
            var bimg = box.AddComponent<Image>();
            bimg.sprite = ArcadeTheme.RoundedSolid(ArcadeTheme.RadSm);
            bimg.type = Image.Type.Sliced;
            bimg.pixelsPerUnitMultiplier = 1f;
            bimg.color = chalk;
            bimg.raycastTarget = false;
            var brt = Rt(box);
            brt.anchorMin = new Vector2(0.30f, top ? 0.80f : 0.04f);
            brt.anchorMax = new Vector2(0.70f, top ? 0.96f : 0.20f);
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;

            var turf = Child(box.transform, "Turf");
            var timg = turf.AddComponent<Image>();
            timg.sprite = ArcadeTheme.RoundedSolid(ArcadeTheme.RadSm);
            timg.type = Image.Type.Sliced;
            timg.pixelsPerUnitMultiplier = 1f;
            timg.color = ArcadeTheme.Pitch;
            timg.raycastTarget = false;
            Stretch(Rt(turf), 2.5f);
        }

        // ---------- tile ----------

        /// <summary>
        /// A large picture card with a caption under it — the menu's main choices (Online / Local,
        /// Player vs Player / AI vs Player).
        ///
        /// The artwork is optional. With no sprite it draws a flat placeholder carrying a small
        /// "artwork" hint, so a menu built before the art exists reads as deliberately unfinished
        /// rather than broken. Drop a sprite in later and nothing else changes.
        ///
        /// Built on <see cref="MenuButton"/> so hover, press, focus and submit behave exactly as
        /// every other control, rather than being a second thing that merely happens to be clickable.
        /// </summary>
        public static MenuButton Tile(Transform parent, string caption, Sprite art, Action onClick,
                                      bool interactable = true, string note = null,
                                      float width = 430f, float height = 380f,
                                      Badge badge = Badge.None, bool primary = false)
        {
            var root = Child(parent, "Tile_" + caption);
            var le = root.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.preferredHeight = height;
            le.minHeight = height;

            // A real drop shadow, always on — separate from the Gold interactive glow right below it,
            // which sits at alpha 0 until hovered/pressed and so gives the card no lift at rest at
            // all. Every other panel in the game (Panel itself, the store's cards) casts a shadow
            // whether or not anything is touching it; the mode cards were the one surface that
            // floated flush with the background until something interacted with them, which is what
            // let a busy backdrop behind them compete for attention instead of sitting visibly behind.
            var shadowGo = Child(root.transform, "Shadow");
            GlowImage(shadowGo, ArcadeTheme.RadLg, ArcadeTheme.ShadowFeather,
                      Color.black.WithAlpha(ArcadeTheme.ShadowAlpha));
            Stretch(Rt(shadowGo), -10, -14, -10, -6);

            var glowGo = Child(root.transform, "Glow");
            var glow = GlowImage(glowGo, ArcadeTheme.RadLg, 30f, ArcadeTheme.Gold.WithAlpha(0f));
            Stretch(Rt(glowGo), -18, -18, -18, -18);

            var borderGo = Child(root.transform, "Border");
            var border = RoundedImage(borderGo, ArcadeTheme.RadLg, ArcadeTheme.Line, false);
            Stretch(Rt(borderGo), 0);

            var fillGo = Child(root.transform, "Fill");
            var fill = RoundedImage(fillGo, ArcadeTheme.RadLg, ArcadeTheme.BgRaised, true);
            Stretch(Rt(fillGo), 2f);

            // Picture area: the upper portion of the card, leaving room for the caption below.
            var pictureGo = Child(root.transform, "Picture");
            var picture = pictureGo.AddComponent<Image>();
            picture.raycastTarget = false;

            if (art != null)
            {
                picture.sprite = art;
                picture.color = interactable ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
                picture.preserveAspect = true;
            }
            else
            {
                // Turf, not a flat panel: with no photo the card draws a pitch instead of an empty
                // frame (see PitchArt below), and the green is that pitch's grass.
                picture.sprite = ArcadeTheme.RoundedSolid(ArcadeTheme.RadMd);
                picture.type = Image.Type.Sliced;
                picture.pixelsPerUnitMultiplier = 1f;
                picture.color = ArcadeTheme.Pitch;
            }

            // The picture takes everything above the caption strip, so a bigger card is mostly a
            // bigger picture rather than a bigger margin.
            var prt = Rt(pictureGo);
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(16f, note != null ? 78f : 62f);
            prt.offsetMax = new Vector2(-16f, -16f);

            if (art == null)
            {
                // A drawn pitch instead of the old "ARTWORK" word. The placeholder used to announce
                // that the card was unfinished; this makes the card look designed on its own terms,
                // with the badge below still saying which mode it is.
                PitchArt(pictureGo.transform);
            }

            // Scrim over the lower third of the photo. Added even on the placeholder so the card's
            // proportions do not shift the moment real art arrives.
            var scrimGo = Child(pictureGo.transform, "Scrim");
            var scrim = scrimGo.AddComponent<Image>();
            scrim.sprite = ArcadeTheme.Scrim();
            scrim.color = Color.white;
            scrim.raycastTarget = false;
            var scrt = Rt(scrimGo);
            scrt.anchorMin = new Vector2(0f, 0f);
            scrt.anchorMax = new Vector2(1f, 0.42f);
            scrt.offsetMin = Vector2.zero;
            scrt.offsetMax = Vector2.zero;

            BuildBadge(pictureGo.transform, badge);

            var label = Text(root.transform, caption, ArcadeTheme.FsButton, ArcadeTheme.Ink,
                             display: false, bold: true, upper: true, tracking: 6f);
            var lrt = Rt(label.gameObject);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.offsetMin = new Vector2(8f, note != null ? 26f : 12f);
            lrt.offsetMax = new Vector2(-8f, note != null ? 52f : 46f);

            if (note != null)
            {
                var sub = Text(root.transform, note, ArcadeTheme.FsCaption, ArcadeTheme.Gold,
                               bold: true, upper: true, tracking: 4f);
                var srt = Rt(sub.gameObject);
                srt.anchorMin = Vector2.zero;
                srt.anchorMax = new Vector2(1f, 0f);
                srt.offsetMin = new Vector2(8f, 8f);
                srt.offsetMax = new Vector2(-8f, 26f);
            }

            var btn = root.AddComponent<MenuButton>();
            btn.fill = fill; btn.border = border; btn.glow = glow; btn.label = label;
            btn.targetGraphic = fill;
            btn.Configure(MenuButton.Variant.Neutral);
            if (primary) btn.SetAccent(ArcadeTheme.Gold, 0.3f);
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            btn.interactable = interactable;
            if (!interactable)
            {
                // Dim the whole card so "disabled" reads at a glance, rather than only becoming
                // apparent after a click that does nothing.
                var dim = root.AddComponent<CanvasGroup>();
                dim.alpha = 0.45f;
                dim.interactable = false;
                dim.blocksRaycasts = true;
            }

            return btn;
        }

        // ---------- code input ----------

        /// <summary>
        /// A short single-line field, built for typing a join code into: centred, wide-tracked and
        /// upper-case, so a six-character code reads like a code rather than like prose.
        ///
        /// Validation is Alphanumeric and the length is capped, which between them make most bad
        /// input impossible to type rather than something to report afterwards.
        /// </summary>
        public static TMP_InputField CodeInput(Transform parent, string placeholder,
                                               int characterLimit, float height = 62f)
        {
            return TextInput(parent, placeholder, characterLimit,
                             TMP_InputField.CharacterValidation.Alphanumeric,
                             password: false, upper: true, tracking: 10f, height: height);
        }

        /// <summary>
        /// The general single-line field. <see cref="CodeInput"/> is this with the join code's
        /// settings baked in.
        ///
        /// Case is the reason for the <paramref name="upper"/> switch rather than always forcing it:
        /// a join code is case-insensitive so shouting it looks right, but a username and especially
        /// a password are neither, and a field that renders them upper-case while storing what was
        /// typed is a field that lies about the value it holds.
        /// </summary>
        public static TMP_InputField TextInput(Transform parent, string placeholder,
                                               int characterLimit,
                                               TMP_InputField.CharacterValidation validation =
                                                   TMP_InputField.CharacterValidation.None,
                                               bool password = false, bool upper = false,
                                               float tracking = 2f, float height = 62f)
        {
            var root = Child(parent, password ? "PasswordInput" : "TextInput");
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var borderGo = Child(root.transform, "Border");
            RoundedImage(borderGo, ArcadeTheme.RadMd, ArcadeTheme.Line, false);
            Stretch(Rt(borderGo), 0);

            // Deep rather than raised: a field is somewhere to put something, so it reads as a well
            // in the panel while the buttons beside it stand out of it.
            var fillGo = Child(root.transform, "Fill");
            var fill = RoundedImage(fillGo, ArcadeTheme.RadMd, ArcadeTheme.BgDeep, true);
            Stretch(Rt(fillGo), 1.5f);

            // TMP_InputField scrolls its text inside this viewport and needs the mask to clip it.
            var area = Child(root.transform, "TextArea");
            area.AddComponent<RectMask2D>();
            Stretch(Rt(area), 16f, 6f, 16f, 6f);

            // The placeholder stays upper-case regardless: it is a label, not the player's value.
            var hint = Text(area.transform, placeholder, ArcadeTheme.FsButton, ArcadeTheme.InkMuted,
                            display: false, bold: true, upper: true, tracking: tracking);
            Stretch(Rt(hint.gameObject), 0);

            var text = Text(area.transform, string.Empty, ArcadeTheme.FsButton, ArcadeTheme.Ink,
                            display: false, bold: true, upper: upper, tracking: tracking);
            Stretch(Rt(text.gameObject), 0);

            // Added last: the field wires itself to the parts above, so they have to exist first.
            var input = root.AddComponent<TMP_InputField>();
            input.textViewport = Rt(area);
            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = fill;
            input.characterLimit = characterLimit;
            input.characterValidation = validation;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.onFocusSelectAll = !password;
            input.restoreOriginalTextOnEscape = true;
            input.text = string.Empty;

            if (password)
            {
                input.contentType = TMP_InputField.ContentType.Password;
                input.asteriskChar = '*';
            }

            return input;
        }

        // ---------- fields with a trailing icon button ----------

        /// <summary>
        /// A password field with a show/hide eye button beside it — the toggle every text app has. The
        /// eye flips the field between masked and plain and lifts a slash off its lens to say which
        /// state it is in; masked by default, so the slash starts on.
        ///
        /// Returns the <see cref="TMP_InputField"/> itself, exactly like <see cref="TextInput"/>, so a
        /// caller swaps one for the other without changing anything downstream.
        /// </summary>
        public static TMP_InputField PasswordField(Transform parent, string placeholder,
                                                   int characterLimit, float height = 54f)
        {
            var row = FieldRow(parent, height);

            var input = TextInput(row.transform, placeholder, characterLimit,
                                  TMP_InputField.CharacterValidation.None, password: true, height: height);
            input.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var eye = IconButton(row.transform, "Eye", Icon.Eye, MenuButton.Variant.Neutral,
                                 null, size: height);
            PinSquare(eye, height);

            var slash = EyeSlash(eye.transform.Find("Glyph"));
            bool shown = false;
            eye.onClick.AddListener(() =>
            {
                shown = !shown;
                input.contentType = shown
                    ? TMP_InputField.ContentType.Standard
                    : TMP_InputField.ContentType.Password;
                // The field only re-masks/unmasks on the next redraw; force it so the toggle is felt on
                // the text already typed, not only on the next character entered.
                input.ForceLabelUpdate();
                if (slash != null) slash.SetActive(!shown);
            });

            return input;
        }

        /// <summary>
        /// A username field with a dice beside it that fills a valid random name in one tap. It writes
        /// straight into the field, so the name stays the player's to edit or clear.
        /// </summary>
        public static TMP_InputField UsernameField(Transform parent, string placeholder,
                                                   int characterLimit, float height = 54f)
        {
            var row = FieldRow(parent, height);

            var input = TextInput(row.transform, placeholder, characterLimit,
                                  TMP_InputField.CharacterValidation.None, height: height);
            input.GetComponent<LayoutElement>().flexibleWidth = 1f;

            var dice = IconButton(row.transform, "Random", Icon.Dice, MenuButton.Variant.Neutral,
                                  () => input.text = RandomUsername(), size: height);
            PinSquare(dice, height);

            return input;
        }

        /// <summary>
        /// The row a field-plus-button pair sits in: the field flexes to take the width the button
        /// leaves, and the button is pinned square to the row height by <see cref="PinSquare"/> so it
        /// lines up with the field rather than being stretched by the layout group.
        /// </summary>
        private static GameObject FieldRow(Transform parent, float height)
        {
            var row = Child(parent, "FieldRow");
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Sm;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            return row;
        }

        /// <summary>Pins an icon button to a fixed square so the row's layout group cannot stretch it.</summary>
        private static void PinSquare(MenuButton button, float size)
        {
            var le = button.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.minWidth = size;
            le.preferredHeight = size;
            le.minHeight = size;
            le.flexibleWidth = 0f;
        }

        /// <summary>The diagonal bar drawn over the eye's lens to mark the masked state.</summary>
        private static GameObject EyeSlash(Transform glyph)
        {
            if (glyph == null)
            {
                return null;
            }

            var go = Child(glyph, "Slash");
            RoundedImage(go, ArcadeTheme.RadSm, ArcadeTheme.Ink, false);
            var rt = Rt(go);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(36f, 3.5f);
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            return go;
        }

        // ---------- random username ----------

        private static readonly string[] NameAdjectives =
            { "Swift", "Turbo", "Mega", "Neon", "Iron", "Wild", "Rapid", "Cosmic", "Golden", "Silent",
              "Fierce", "Lucky", "Rogue", "Blazing" };

        private static readonly string[] NameNouns =
            { "Striker", "Keeper", "Baller", "Rocket", "Comet", "Falcon", "Tiger", "Viper", "Maverick",
              "Blitz", "Champ", "Bolt" };

        /// <summary>
        /// A readable random username within the service's rules (3-20 of letters, digits and a few
        /// symbols — this uses only letters and digits, which are always allowed). Trimmed to 20 as a
        /// belt-and-braces cap; the word pairs above never reach it.
        /// </summary>
        public static string RandomUsername()
        {
            string a = NameAdjectives[UnityEngine.Random.Range(0, NameAdjectives.Length)];
            string n = NameNouns[UnityEngine.Random.Range(0, NameNouns.Length)];
            string candidate = a + n + UnityEngine.Random.Range(1, 100);
            return candidate.Length > 20 ? candidate.Substring(0, 20) : candidate;
        }

        /// <summary>Blank vertical space inside a layout group, for separating one run of controls
        /// from the next without a divider.</summary>
        public static GameObject Spacer(Transform parent, float height)
        {
            var go = Child(parent, "Spacer");
            go.AddComponent<LayoutElement>().preferredHeight = height;
            return go;
        }

        /// <summary>
        /// Empties a container that is about to be rebuilt.
        ///
        /// Unparents before destroying, which is the whole point: <see cref="UnityEngine.Object.Destroy"/>
        /// runs at the end of the frame, so children merely marked for destruction are still counted
        /// by a layout group while their replacements are being added beside them — a list briefly
        /// twice its length, and a ContentSizeFitter that measures it that way.
        /// </summary>
        public static void ClearChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                child.SetParent(null, false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        // ---------- profile pieces ----------

        /// <summary>
        /// An XP / progress bar: a deep well with a gold fill that sweeps in when shown, and an
        /// optional "740 / 1000 XP" caption riding on top of it.
        ///
        /// The fill is a <see cref="Image.Type.Filled"/> so a <see cref="UIFillBar"/> can animate its
        /// <c>fillAmount</c> rather than resizing a rect every frame. Returns the bar root; call
        /// <see cref="UIFillBar.SetFraction"/> on the returned <see cref="UIFillBar"/> (fetched via
        /// GetComponentInChildren) to change the value later.
        ///
        /// Visual only. Whatever fraction it is handed is a picture of progress, not a claim that any
        /// progression system computed it — see <see cref="TableFootball.Net.PlayerProgress"/>.
        /// </summary>
        public static GameObject XpBar(Transform parent, float fraction, string label = null,
                                       float height = 22f)
        {
            var root = Child(parent, "XpBar");
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1f;

            // Everything below is sized FROM the height rather than from constants tuned for one bar.
            // The radius, the fill inset and the caption all scale together, so the same bar at 16 and
            // at 30 is the same design twice and not a small one and a stretched one — which is what
            // made an enlarged chip's bar look wrong.
            int radius = Mathf.Max(4, Mathf.RoundToInt(height * 0.42f));

            // The track: a well sunk into the panel, like the input fields, so the fill reads as
            // sitting inside something rather than floating on the surface.
            //
            // It is also the MASK for the fill. A Filled image ignores 9-slicing and stretches its
            // whole sprite across the rect, so a rounded fill drew its corners smeared the length of
            // the bar. Clipping a square-ended fill to the track's rounded shape instead gives clean
            // ends at any width, and the wider the bar the more that mattered.
            var track = Child(root.transform, "Track");
            RoundedImage(track, radius, ArcadeTheme.BgDeep, false);
            Stretch(Rt(track), 0);
            var mask = track.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            // A rect with zero baked-in rounding, not the outer radius. Image.Type.Filled ignores
            // 9-slice border data entirely — it stretch-crops the raw sprite texture rather than
            // preserving corner size — so a sprite baked with the real radius got its corner-rounding
            // "ramp" stretched across the bar's full width, dragging the left edge into a squashed,
            // proportionally-smaller-looking chunk of the bar. Radius 0 bakes a plain rectangle with
            // no curvature to distort, so stretching it is invisible — flat colour is flat colour at
            // any width.
            //
            // It still needs to be a REAL sprite, though — Filled with `sprite == null` isn't merely
            // "a rounder rectangle", it silently skips ALL of Image's type-specific rendering (Simple,
            // Sliced, Filled alike) and falls back to Graphic's own OnPopulateMesh, which draws a
            // plain, full, un-clipped quad. fillAmount is then simply never read: the bar renders
            // permanently full and SetFraction's sweep has nothing left to animate.
            var fillGo = Child(track.transform, "Fill");
            var fill = RoundedImage(fillGo, 0, ArcadeTheme.Gold, false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = Mathf.Clamp01(fraction);
            // Inset a hair top and bottom so a sliver of the well still shows around the fill, but
            // flush left and right where the mask is what shapes the ends.
            Stretch(Rt(fillGo), 0f, 1.5f, 0f, 1.5f);

            var bar = fillGo.AddComponent<UIFillBar>();
            bar.SetFraction(fraction);

            if (label != null)
            {
                // Scaled to the bar it rides on, and capped so a tall bar does not grow a caption
                // bigger than the name above it.
                float size = Mathf.Min(height * 0.62f, ArcadeTheme.FsCaption);
                var t = Text(root.transform, label, size, ArcadeTheme.Ink,
                             display: false, bold: true, upper: true, tracking: 3f);
                Stretch(Rt(t.gameObject), 8f, 0f, 8f, 0f);

                // A hard dark shadow rather than nothing: the label rides on top of TWO different
                // colours at once — dark track on one side of the fill edge, bright gold on the other
                // — and plain Ink text has almost no contrast against gold specifically (a light label
                // on a light-ish fill). The shadow gives every character a dark edge regardless of
                // which side of the fill it happens to fall on, so it reads correctly whatever the
                // fraction is doing underneath it.
                var shadow = t.gameObject.AddComponent<Shadow>();
                shadow.effectColor = Color.black.WithAlpha(0.85f);
                shadow.effectDistance = new Vector2(0f, -1f);
                shadow.useGraphicAlpha = true;
            }

            return root;
        }

        /// <summary>
        /// One big number with a word under it, for a record: 12 WON, 3 LOST, 80% WIN RATE.
        ///
        /// Returns the number's own text so the caller can rewrite it as the value changes without
        /// rebuilding the block. Sized by the layout group it is dropped into, so a row of these
        /// divides the width evenly on its own.
        /// </summary>
        public static TextMeshProUGUI StatBlock(Transform parent, string caption, Color color)
        {
            var root = Child(parent, "Stat_" + caption);
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = 76f;
            le.flexibleWidth = 1f;

            var v = root.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleCenter;
            v.spacing = 0f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            var value = Text(root.transform, "–", ArcadeTheme.FsTitle * 0.78f, color,
                             display: true, bold: true, upper: true, tracking: 1f);
            value.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;

            var label = Text(root.transform, caption, ArcadeTheme.FsCaption * 0.86f,
                             ArcadeTheme.InkMuted, display: false, bold: true, upper: true,
                             tracking: 6f);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            return value;
        }

        /// <summary>
        /// A horizontal rule broken by a caption. Groups the controls beneath it into one idea, which
        /// a bare line of muted text floating between two buttons does not.
        /// </summary>
        public static void SectionRule(Transform parent, string caption)
        {
            var row = Child(parent, "Section_" + caption);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = ArcadeTheme.Md;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            HairLine(row.transform);

            var t = Text(row.transform, caption, ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                         display: false, bold: true, upper: true, tracking: 4f);
            var tle = t.gameObject.AddComponent<LayoutElement>();
            tle.preferredWidth = 220f;
            tle.preferredHeight = 20f;

            HairLine(row.transform);
        }

        private static void HairLine(Transform parent)
        {
            var go = Child(parent, "Rule");
            RoundedImage(go, ArcadeTheme.RadSm, ArcadeTheme.Line, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 60f;
            le.flexibleWidth = 1f;
            le.preferredHeight = 2f;
        }

        /// <summary>
        /// A small disc carrying a player's initial, for a list row.
        ///
        /// The same idea as <see cref="AvatarButton"/> without the button: a friends list of names
        /// alone is a wall of text, and a coloured initial gives each row something to aim at. The
        /// colour carries whether they are online, so the row does not need a separate dot as well.
        /// </summary>
        public static GameObject AvatarDisc(Transform parent, string name, Color tint, float size = 38f)
        {
            var root = Child(parent, "Avatar");
            var le = root.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            // A disc has one correct size and no smaller one. Without a minimum, a horizontal layout
            // group short on width shrinks every child from preferred toward min — and a min of zero
            // collapses the ring to a sliver, which the inset fill then covers all but the edge of.
            le.minWidth = size;
            le.minHeight = size;

            // Sized on the RectTransform as well as the LayoutElement. A LayoutElement is only read
            // by a parent layout group, and dropped into a plain holder this disc fell back to the
            // RectTransform default of 100x100 — a ring two and a half times its intended size,
            // sitting outside whatever it was meant to sit inside.
            var rrt = Rt(root);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(size, size);

            // Ring outside, fill inset by two — the same two-layer disc IconButton uses, so an avatar
            // and an icon button sitting near each other read as the same family of shape.
            CircleImage(root, tint.WithAlpha(0.55f), false);

            var fill = Child(root.transform, "Fill");
            CircleImage(fill, ArcadeTheme.BgPanel, false);
            Stretch(Rt(fill), 2f);

            string initial = string.IsNullOrWhiteSpace(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            // One character of somebody's name is still somebody's name: a leading '<' is the start
            // of a tag as far as TextMeshPro is concerned, and it would swallow the letter rather
            // than draw it.
            var t = Text(root.transform, initial, size * 0.46f, tint,
                         display: true, bold: true, upper: true, richText: false);
            Stretch(Rt(t.gameObject), 0);

            return root;
        }

        /// <summary>
        /// The tappable left-hand part of a list row: an avatar, a name, and a line under it.
        ///
        /// A row where the whole strip is one button and the action buttons sit ON it would work —
        /// the topmost graphic wins the raycast — but it puts two overlapping hit targets in the same
        /// place, and the row-wide hover would light up every time the pointer crossed a button that
        /// does something else. So the identity is its own control, sized to the space the buttons
        /// leave, and nothing overlaps anything.
        /// </summary>
        public static MenuButton RowIdentity(Transform parent, string name, string subtitle,
                                             Color tint, Action onClick, float height = 60f)
        {
            var root = Child(parent, "Identity");
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;

            var glowGo = Child(root.transform, "Glow");
            var glow = GlowImage(glowGo, ArcadeTheme.RadLg, 22f, ArcadeTheme.Gold.WithAlpha(0f));
            Stretch(Rt(glowGo), -12);

            // Colours here are placeholders: Configure below repaints both from the variant, which is
            // what keeps a row's resting and hover states identical to every other control.
            var borderGo = Child(root.transform, "Border");
            var border = RoundedImage(borderGo, ArcadeTheme.RadLg, ArcadeTheme.Line, false);
            Stretch(Rt(borderGo), 0);

            var fillGo = Child(root.transform, "Fill");
            var fill = RoundedImage(fillGo, ArcadeTheme.RadLg, ArcadeTheme.BgRaised, true);
            Stretch(Rt(fillGo), 1.5f);

            var inner = Child(root.transform, "Inner");
            Stretch(Rt(inner), 18f, 6f, 18f, 6f);
            var h = inner.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Md;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            AvatarDisc(inner.transform, name, tint, height - 16f);

            var textCol = Child(inner.transform, "Text");
            var tle = textCol.AddComponent<LayoutElement>();
            // Takes the space left over rather than asking for the width its text would like. Asking
            // is what starved the avatar: a long name reports a preferred width past the end of the
            // row, and the group answers by shrinking everything in it, discs included.
            tle.preferredWidth = 0f;
            tle.flexibleWidth = 1f;
            var v = textCol.AddComponent<VerticalLayoutGroup>();
            // Was 0 — the name and its subtitle sat jammed together with no gap, reading as cramped
            // against the taller avatar disc beside them.
            v.spacing = ArcadeTheme.Xs;
            v.childAlignment = TextAnchor.MiddleLeft;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            // Never rich: this row exists to show a name somebody else chose, and the whole point of
            // a list is that every row is the same size and in the same place. One <size=400%> would
            // be enough to push the rest of the list off the screen.
            var nameText = Text(textCol.transform, name, ArcadeTheme.FsCaption, ArcadeTheme.Ink,
                                display: false, bold: true, upper: false, tracking: 1f,
                                align: TextAlignmentOptions.Left, richText: false);
            nameText.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.enableWordWrapping = false;

            var sub = Text(textCol.transform, subtitle, ArcadeTheme.FsCaption * 0.82f, tint,
                           display: false, bold: true, upper: true, tracking: 3f,
                           align: TextAlignmentOptions.Left);
            sub.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            sub.overflowMode = TextOverflowModes.Ellipsis;
            sub.enableWordWrapping = false;

            var btn = root.AddComponent<MenuButton>();
            btn.fill = fill; btn.border = border; btn.glow = glow; btn.label = nameText;
            btn.targetGraphic = fill;
            btn.Configure(MenuButton.Variant.Ghost);
            btn.SetNoScale();
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            return btn;
        }

        // ---------- scrolling list ----------

        /// <summary>
        /// A vertically scrolling column, for lists with no fixed length — the friends list being the
        /// first thing in this game that has one.
        ///
        /// Returns the CONTENT transform to parent rows to, not the scroll view: callers only ever
        /// want to add rows, and handing back the outer object invites parenting them one level too
        /// high, where they render but never scroll.
        /// </summary>
        public static RectTransform ScrollList(Transform parent, float spacing = ArcadeTheme.Sm)
        {
            var view = Child(parent, "ScrollView");
            var scroll = view.AddComponent<ScrollRect>();

            // Fills whatever it was dropped into. A scroll view that has to be sized by every caller
            // is one that silently ends up 0x0 the first time somebody forgets.
            Stretch(Rt(view), 0);

            // Without this the content spills over the panel edge while scrolling instead of being
            // clipped by it.
            var viewport = Child(view.transform, "Viewport");
            viewport.AddComponent<RectMask2D>();
            Stretch(Rt(viewport), 0);

            var content = Child(viewport.transform, "Content");
            var crt = Rt(content);
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            // Width zeroed against the anchors, not left at the RectTransform default. Stretched
            // between two anchors a sizeDelta of 100 does not mean "100 wide", it means "100 wider
            // than the viewport" — and a centre pivot hangs half of that off each side, straight
            // under the mask. The row survived; the avatar at its left edge was cut in half.
            crt.sizeDelta = new Vector2(0f, crt.sizeDelta.y);
            crt.anchoredPosition = new Vector2(0f, crt.anchoredPosition.y);

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            // Height follows the rows; width is left to the anchors, or the fitter fights the layout
            // group for it and rows end up as wide as their longest label.
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = crt;
            scroll.viewport = Rt(viewport);
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 24f;

            return crt;
        }

        // ---------- slider ----------

        public static Slider Slider(Transform parent, float value, Action<float> onChange, float height = 26f)
        {
            var root = Child(parent, "Slider");
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            // Fully qualified: this method is itself called Slider, which shadows the type inside
            // its own body.
            var slider = root.AddComponent<UnityEngine.UI.Slider>();
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = value;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;

            // track
            var track = Child(root.transform, "Track");
            RoundedImage(track, ArcadeTheme.RadSm, ArcadeTheme.BgRaised, true);
            var trt = Rt(track);
            trt.anchorMin = new Vector2(0, 0.5f); trt.anchorMax = new Vector2(1, 0.5f);
            trt.sizeDelta = new Vector2(0, 10f); trt.anchoredPosition = Vector2.zero;

            // fill
            var fillArea = Child(root.transform, "Fill Area");
            var fart = Rt(fillArea);
            fart.anchorMin = new Vector2(0, 0.5f); fart.anchorMax = new Vector2(1, 0.5f);
            fart.sizeDelta = new Vector2(-14f, 10f); fart.anchoredPosition = Vector2.zero;
            var fillGo = Child(fillArea.transform, "Fill");
            RoundedImage(fillGo, ArcadeTheme.RadSm, ArcadeTheme.Gold, false);
            var frt = Rt(fillGo);
            frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0, 1); frt.sizeDelta = new Vector2(14f, 0);

            // handle
            var handleArea = Child(root.transform, "Handle Slide Area");
            var hart = Rt(handleArea);
            hart.anchorMin = Vector2.zero; hart.anchorMax = Vector2.one;
            hart.offsetMin = new Vector2(7, 0); hart.offsetMax = new Vector2(-7, 0);
            var handleGo = Child(handleArea.transform, "Handle");
            RoundedImage(handleGo, ArcadeTheme.RadSm, ArcadeTheme.Ink, true);
            var hrt = Rt(handleGo);
            hrt.sizeDelta = new Vector2(20f, 26f);

            slider.fillRect = frt;
            slider.handleRect = hrt;
            slider.targetGraphic = handleGo.GetComponent<Image>();

            if (onChange != null) slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }

        // ---------- segmented control (e.g. difficulty) ----------

        /// <summary>A row of pills; the selected one is gold. Returns the row GameObject.</summary>
        public static GameObject Segmented(Transform parent, string[] labels, int initial, Action<int> onChange,
                                           float height = 48f)
        {
            var root = Child(parent, "Segmented");
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            var row = root.AddComponent<HorizontalLayoutGroup>();
            row.spacing = ArcadeTheme.Sm;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            row.childControlWidth = true;
            row.childControlHeight = true;

            var pills = new Image[labels.Length];
            var pillLabels = new TextMeshProUGUI[labels.Length];
            int selected = Mathf.Clamp(initial, 0, labels.Length - 1);

            void Refresh()
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    bool on = i == selected;
                    pills[i].color = on ? ArcadeTheme.Gold : ArcadeTheme.BgRaised;
                    pillLabels[i].color = on ? ArcadeTheme.OnGold : ArcadeTheme.InkMuted;
                }
            }

            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var pill = Child(root.transform, "Pill_" + labels[i]);
                var img = RoundedImage(pill, ArcadeTheme.RadSm, ArcadeTheme.BgRaised, true);
                pills[i] = img;
                var b = pill.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.targetGraphic = img;
                b.onClick.AddListener(() => { selected = idx; Refresh(); onChange?.Invoke(idx); });
                var lab = Text(pill.transform, labels[i], ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                               display: false, bold: true, upper: true, tracking: 6f);
                Stretch(Rt(lab.gameObject), 0);
                pillLabels[i] = lab;
            }

            Refresh();
            return root;
        }

        // ---------- rewards: coins, rarity, chests, badges ----------

        /// <summary>
        /// The colour that stands for a rarity, everywhere it appears — a card edge, a glow, a label,
        /// a chest. Here rather than in <see cref="ArcadeTheme"/>'s raw palette because this is the
        /// MAPPING, and a mapping that lived in two files would be two files to keep in step.
        /// </summary>
        public static Color RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Rare: return ArcadeTheme.RarityRare;
                case Rarity.Epic: return ArcadeTheme.RarityEpic;
                case Rarity.Legendary: return ArcadeTheme.RarityLegendary;
                default: return ArcadeTheme.RarityCommon;
            }
        }

        /// <summary>The tier accent for a league. The one place Net's <see cref="League"/> becomes a
        /// colour — see <see cref="ArcadeTheme"/>, which holds the metals but not this mapping.</summary>
        public static Color LeagueColor(League league)
        {
            switch (league)
            {
                case League.Bronze: return ArcadeTheme.Bronze;
                case League.Silver: return ArcadeTheme.Silver;
                case League.Gold: return ArcadeTheme.Gold;
                case League.Diamond: return ArcadeTheme.Diamond;
                default: return ArcadeTheme.Gold;
            }
        }

        /// <summary>A chest's colour is its tier's rarity colour — the same four steps, so a Rare chest
        /// and a Rare skin read as the same promise.</summary>
        public static Color ChestColor(ChestTier tier) => RarityColor((Rarity)(int)tier);

        /// <summary>
        /// The gold coin, drawn from three discs: a dark rim, a bright face, and a punched emblem.
        ///
        /// Every coin in the game comes from here — the header pill, a reward chip, a price tag — so
        /// the currency is one object the player learns once. Non-raycast throughout: a coin is
        /// decoration on top of whatever it is labelling, and it must never eat that thing's tap.
        /// </summary>
        public static GameObject CoinGlyph(Transform parent, float size = 26f)
        {
            var root = Child(parent, "Coin");
            var rrt = Rt(root);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(size, size);

            var rim = Child(root.transform, "Rim");
            var rimImg = rim.AddComponent<Image>();
            rimImg.sprite = ArcadeTheme.Disc();
            rimImg.color = ArcadeTheme.CoinDark;
            rimImg.raycastTarget = false;
            Stretch(Rt(rim), 0);

            var face = Child(root.transform, "Face");
            var faceImg = face.AddComponent<Image>();
            faceImg.sprite = ArcadeTheme.Disc();
            faceImg.color = ArcadeTheme.Coin;
            faceImg.raycastTarget = false;
            Stretch(Rt(face), size * 0.10f);

            // The mark stamped into the face. A plain dot rather than a letter or a symbol: at the
            // 18px this is usually drawn at, anything with strokes in it turns to mush, and a coin
            // with a mark on it is already unmistakably a coin.
            Dot(face.transform, ArcadeTheme.CoinDark, Vector2.zero, size * 0.30f);

            return root;
        }

        /// <summary>
        /// A coin followed by a number — a balance, a price, a reward. Returns the root; fetch the
        /// number with <c>GetComponentInChildren&lt;TextMeshProUGUI&gt;()</c> to rewrite it later,
        /// the same way <see cref="XpBar"/> hands back its fill.
        ///
        /// Laid out as a row so the number can grow from "0" to "12,400" without the coin drifting or
        /// the pair needing to be re-centred by hand.
        /// </summary>
        public static GameObject CoinAmount(Transform parent, string amount, float height = 30f,
                                            Color? textColor = null)
        {
            var root = Child(parent, "CoinAmount");
            var le = root.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var h = root.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Xs + 2f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            float coin = height * 0.78f;
            var coinCell = Child(root.transform, "CoinCell");
            var cle = coinCell.AddComponent<LayoutElement>();
            cle.preferredWidth = coin;
            cle.minWidth = coin;
            cle.preferredHeight = coin;
            CoinGlyph(coinCell.transform, coin);

            // Not rich text and never upper-cased: it is a number, and both would only give a stray
            // character somewhere to do damage.
            var text = Text(root.transform, amount, Mathf.Min(height * 0.62f, ArcadeTheme.FsBody),
                            textColor ?? ArcadeTheme.Coin, display: true, bold: true, upper: false,
                            tracking: 1f, align: TextAlignmentOptions.Left, richText: false);
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            return root;
        }

        /// <summary>
        /// A treasure chest in its tier's colour: body, lid, a band down the front and a coin-gold
        /// clasp. Drawn rather than imported like everything else here, and sized entirely from
        /// <paramref name="size"/> so the same chest works as a 40px reward chip and a 140px reveal.
        /// </summary>
        public static GameObject ChestGlyph(Transform parent, ChestTier tier, float size = 64f)
        {
            Color tint = ChestColor(tier);
            Color dark = Color.Lerp(tint, ArcadeTheme.BgDeep, 0.45f);

            var root = Child(parent, "Chest");
            var rrt = Rt(root);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(size, size);

            // Body: the lower box.
            var body = Child(root.transform, "Body");
            RoundedImage(body, Mathf.Max(3, Mathf.RoundToInt(size * 0.09f)), dark, false);
            var brt = Rt(body);
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(size * 0.80f, size * 0.44f);
            brt.anchoredPosition = new Vector2(0f, -size * 0.16f);

            // Lid: brighter, and wider than the body so it overhangs — the one detail that stops a
            // chest reading as a plain box with a line across it.
            var lid = Child(root.transform, "Lid");
            RoundedImage(lid, Mathf.Max(3, Mathf.RoundToInt(size * 0.11f)), tint, false);
            var lrt = Rt(lid);
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(size * 0.88f, size * 0.34f);
            lrt.anchoredPosition = new Vector2(0f, size * 0.14f);

            // The band down the front, and the clasp where it meets the lid. Coin gold on every tier:
            // the fittings are the same on all four chests, only the wood changes.
            Rod(root.transform, ArcadeTheme.Coin, new Vector2(0f, -size * 0.10f),
                size * 0.62f, size * 0.11f, 90f);
            var clasp = Child(root.transform, "Clasp");
            RoundedImage(clasp, 3, ArcadeTheme.Coin, false);
            var crt = Rt(clasp);
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(size * 0.20f, size * 0.16f);
            crt.anchoredPosition = new Vector2(0f, -size * 0.02f);
            Dot(clasp.transform, ArcadeTheme.CoinDark, Vector2.zero, size * 0.07f);

            return root;
        }

        /// <summary>
        /// A league badge: a ring in the tier's metal with the ranked cup inside it.
        ///
        /// The same <see cref="TrophyGlyph"/> the ranked pill and the results banner draw, so a badge
        /// is recognisably the ranked system's own mark rather than a fifth unrelated trophy. Only the
        /// metal changes between tiers, which is exactly what a tier IS here.
        ///
        /// <paramref name="holeColor"/> fills the badge's face and is punched through the cup's
        /// handles, so it must match nothing but itself — the face is opaque, so the caller never has
        /// to know what the badge is sitting on.
        /// </summary>
        public static GameObject LeagueBadgeGlyph(Transform parent, League league, float size = 34f)
        {
            Color metal = LeagueColor(league);

            var root = Child(parent, "Badge_" + Leagues.Name(league));
            var rrt = Rt(root);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(size, size);

            var ring = Child(root.transform, "Ring");
            var ringImg = ring.AddComponent<Image>();
            ringImg.sprite = ArcadeTheme.Disc();
            ringImg.color = metal;
            ringImg.raycastTarget = false;
            Stretch(Rt(ring), 0);

            var face = Child(root.transform, "Face");
            var faceImg = face.AddComponent<Image>();
            faceImg.sprite = ArcadeTheme.Disc();
            faceImg.color = ArcadeTheme.BgDeep;
            faceImg.raycastTarget = false;
            Stretch(Rt(face), size * 0.13f);

            // The cup must be scaled against its TRUE width — 42 units, because the two handles splay
            // out to ±21 — not the ~32 its body suggests. The old `size/32 * 0.62` treated it as 32
            // wide, so the handles overran the face circle and read as a cup cut off by the rim. And
            // the cup's bounding box sits 1.5 units high of its own origin (bowl reaches higher than
            // the base drops), so a holder centred on the face still left the cup riding high; the
            // nudge drops it back to true centre.
            float faceDiameter = size * 0.74f;                 // matches the Stretch inset above
            float scale = faceDiameter * 0.82f / 42f;          // 0.82 leaves a rim of face around the cup

            var cup = Child(face.transform, "Cup");
            var crt = Rt(cup);
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(size, size);
            crt.anchoredPosition = new Vector2(0f, -1.5f * scale);
            TrophyGlyph(cup.transform, metal, ArcadeTheme.BgDeep, scale);

            return root;
        }

        /// <summary>
        /// Placeholder artwork for a cosmetic: a rarity-tinted field with the kind's own glyph on it.
        ///
        /// EXPLICITLY A PLACEHOLDER, and shaped like one on purpose. It says what KIND of thing the
        /// item is and how rare it is — the two facts a player needs to shop — without pretending to
        /// be the skin. When real art arrives, this is the one function that changes and every card,
        /// reward chip and reveal in the game picks it up.
        ///
        /// Fills whatever rect it is given, so the caller sizes the card and this fills it.
        /// </summary>
        public static GameObject CosmeticSwatch(Transform parent, CosmeticItem item)
        {
            Color tint = RarityColor(item.Rarity);

            var root = Child(parent, "Swatch");
            Stretch(Rt(root), 0);
            RoundedImage(root, ArcadeTheme.RadMd, Color.Lerp(ArcadeTheme.BgDeep, tint, 0.14f), false);

            // Clipped, so the diagonals below can run past the edges and be cut square by the card
            // rather than having to be measured to fit it.
            var clip = Child(root.transform, "Clip");
            Stretch(Rt(clip), 1.5f);
            clip.AddComponent<RectMask2D>();

            // Three diagonal bands, offset by a hash of the id so two items of the same rarity are not
            // the same picture. Cosmetic only — nothing reads this back.
            int seed = StableHash(item.Id);
            for (int i = 0; i < 3; i++)
            {
                float offset = -70f + i * 62f + (seed % 26);
                Rod(clip.transform, tint.WithAlpha(0.13f), new Vector2(offset, 0f), 300f, 22f, 58f);
            }

            // A real render of the real thing when one exists, and the drawn placeholder when it does
            // not. The rarity backdrop and its diagonals stay either way: the tint is how a wall of
            // cards is scanned, and the thumbnails are transparent so it still shows through.
            Sprite thumb = CosmeticThumbnails.For(item.Id);
            if (thumb != null)
            {
                var art = Child(clip.transform, "Art");
                var img = art.AddComponent<Image>();
                img.sprite = thumb;
                img.raycastTarget = false;
                // The thumbnails are 2:1 but this is drawn into square reward chips as well as the
                // store's wide cards, so it letterboxes rather than stretching the ball into an egg.
                img.preserveAspect = true;
                Stretch(Rt(art), 2f);
            }
            else
            {
                KindGlyph(clip.transform, item.Kind, tint);
            }

            return root;
        }

        /// <summary>
        /// The silhouette that says which part of the table a cosmetic dresses — a pitch, a ball, a
        /// player, a table. Centred in whatever it is dropped into, at a fixed ~64 units, because it
        /// is a symbol rather than a picture and scaling it with the card would make the small cards
        /// illegible before the big ones looked any better.
        /// </summary>
        private static void KindGlyph(Transform parent, CosmeticKind kind, Color tint)
        {
            var holder = Child(parent, "KindGlyph");
            var grt = Rt(holder);
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(64f, 64f);
            Transform g = holder.transform;

            switch (kind)
            {
                case CosmeticKind.FieldSkin:
                {
                    // A pitch seen from above: outline, halfway line, centre spot.
                    var pitch = Child(g, "Pitch");
                    RoundedImage(pitch, ArcadeTheme.RadSm, tint.WithAlpha(0.30f), false);
                    Stretch(Rt(pitch), 6f, 2f, 6f, 2f);
                    Rod(pitch.transform, tint, Vector2.zero, 42f, 3f, 0f);
                    Dot(pitch.transform, tint, Vector2.zero, 13f);
                    break;
                }

                case CosmeticKind.BallSkin:
                {
                    // A ball with three panels punched out of it.
                    Dot(g, tint, Vector2.zero, 44f);
                    Dot(g, ArcadeTheme.BgDeep.WithAlpha(0.8f), new Vector2(0f, 8f), 12f);
                    Dot(g, ArcadeTheme.BgDeep.WithAlpha(0.8f), new Vector2(-11f, -8f), 12f);
                    Dot(g, ArcadeTheme.BgDeep.WithAlpha(0.8f), new Vector2(11f, -8f), 12f);
                    break;
                }

                case CosmeticKind.FigureSkin:
                {
                    // A foosball figure on its rod: the bar across the top, the body hanging from it.
                    Rod(g, tint.WithAlpha(0.55f), new Vector2(0f, 20f), 60f, 6f, 0f);
                    Dot(g, tint, new Vector2(0f, 6f), 20f);
                    var legs = Child(g, "Legs");
                    RoundedImage(legs, ArcadeTheme.RadSm, tint, false);
                    var lrt = Rt(legs);
                    lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                    lrt.pivot = new Vector2(0.5f, 0.5f);
                    lrt.sizeDelta = new Vector2(26f, 24f);
                    lrt.anchoredPosition = new Vector2(0f, -12f);
                    break;
                }

                case CosmeticKind.TableSkin:
                {
                    // The cabinet from above, with its four rods across it.
                    var box = Child(g, "Cabinet");
                    RoundedImage(box, ArcadeTheme.RadSm, tint.WithAlpha(0.28f), false);
                    Stretch(Rt(box), 4f, 8f, 4f, 8f);
                    for (int i = 0; i < 4; i++)
                    {
                        Rod(box.transform, tint, new Vector2(0f, 18f - i * 12f), 56f, 4f, 0f);
                    }
                    break;
                }

                default:
                {
                    Dot(g, tint, Vector2.zero, 40f);
                    break;
                }
            }
        }

        /// <summary>
        /// A small count pip pinned to the top-right of whatever it is dropped into — "there are three
        /// rewards waiting". Ignored by layout and non-raycast, so it can be added to a finished button
        /// without disturbing it.
        ///
        /// Returns the pip so the caller can show and hide it as the count changes; the number is its
        /// only child text.
        /// </summary>
        public static GameObject CountPip(Transform parent, int count, float size = 26f)
        {
            var pip = Child(parent, "CountPip");
            pip.AddComponent<LayoutElement>().ignoreLayout = true;

            var rt = Rt(pip);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-2f, -2f);

            var img = pip.AddComponent<Image>();
            img.sprite = ArcadeTheme.Disc();
            img.color = ArcadeTheme.Red;
            img.raycastTarget = false;

            // Capped rather than truncated: "9+" is a count a player can act on, and a three-digit
            // number inside a 26px disc is not.
            var label = Text(pip.transform, count > 9 ? "9+" : count.ToString(), size * 0.52f,
                             ArcadeTheme.Ink, display: true, bold: true, upper: false, tracking: 0f,
                             richText: false);
            Stretch(Rt(label.gameObject), 0);

            pip.SetActive(count > 0);
            return pip;
        }

        /// <summary>
        /// Rewrites an existing pip's count. Paired with <see cref="CountPip"/> so the "9+" cap lives
        /// in one place — and so a count that changes often does not destroy and rebuild three objects
        /// every time it does.
        /// </summary>
        public static void SetCountPip(GameObject pip, int count)
        {
            if (pip == null)
            {
                return;
            }

            var label = pip.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = count > 9 ? "9+" : count.ToString();
            pip.SetActive(count > 0);
        }

        /// <summary>
        /// A deterministic, non-negative hash of a string, for choosing between visual variants.
        ///
        /// Not <see cref="string.GetHashCode"/>: that is explicitly allowed to differ between runs and
        /// platforms, and a swatch that reshuffled its stripes on every launch would look like a bug.
        /// The same rolling hash <c>PlayerProgress.MockLevelFor</c> already uses.
        /// </summary>
        private static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;

            int h = 17;
            foreach (char c in s) h = unchecked(h * 31 + c);
            return h & 0x7fffffff;
        }
    }
}
