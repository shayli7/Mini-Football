using System;
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
        public enum Icon { Door, Person, Sliders }

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
                picture.sprite = ArcadeTheme.RoundedSolid(ArcadeTheme.RadMd);
                picture.type = Image.Type.Sliced;
                picture.pixelsPerUnitMultiplier = 1f;
                picture.color = ArcadeTheme.BgPanel;
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
                var hint = Text(pictureGo.transform, "ARTWORK", ArcadeTheme.FsCaption,
                                ArcadeTheme.InkMuted.WithAlpha(0.5f), bold: true, upper: true,
                                tracking: 6f);
                Stretch(Rt(hint.gameObject), 0);
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
            v.spacing = 0f;
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
    }
}
