using TableFootball.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The quest glyphs, and the ring that carries a quest's state.
    ///
    /// Drawn from the same two primitives the mode badges in <see cref="UIFactory"/> use — a disc and
    /// a rounded bar — so the set needs no imported art and inherits the theme's palette for free.
    ///
    /// Every glyph is authored in a fixed <see cref="Design"/>-unit square and scaled to fit, which
    /// is what lets the coordinates below be read as a drawing rather than as arithmetic against
    /// whatever size the caller happened to ask for.
    ///
    /// The ring changes with the quest's state; <b>the glyph never does</b>. A player learns the
    /// picture once, and a completed quest has to still be the same quest at a glance.
    /// </summary>
    public static class QuestIcons
    {
        /// <summary>The square every glyph is drawn in. Matches the badge art it was designed against.</summary>
        private const float Design = 56f;

        public static Color TierColor(QuestTier tier) => tier switch
        {
            QuestTier.Bronze => ArcadeTheme.InkMuted,
            QuestTier.Silver => ArcadeTheme.Blue,
            _ => ArcadeTheme.Gold,
        };

        /// <summary>
        /// A quest's icon: the glyph inside a ring that doubles as its progress meter.
        ///
        /// <paramref name="progress01"/> fills the ring clockwise from the top, so a 3-of-5 quest is
        /// legible as a picture before any number is read. Completing it closes the ring, lights a
        /// halo and stamps a check into the corner.
        /// </summary>
        public static GameObject Disc(Transform parent, QuestId id, QuestTier tier,
                                      float progress01, bool complete, bool slam, float size)
        {
            var root = UIFactory.Child(parent, "QuestIcon");
            var rrt = UIFactory.Rt(root);
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(size, size);

            Color tint = slam ? ArcadeTheme.Gold : TierColor(tier);
            float ring = Mathf.Max(3f, size * 0.07f);

            if (complete || slam)
            {
                var haloGo = UIFactory.Child(root.transform, "Halo");
                var halo = haloGo.AddComponent<Image>();
                halo.sprite = ArcadeTheme.DiscGlow(18f);
                halo.color = ArcadeTheme.Gold.WithAlpha(0.28f);
                halo.raycastTarget = false;
                UIFactory.Stretch(UIFactory.Rt(haloGo), -size * 0.14f);
            }

            // The unfilled part of the ring. Faint rather than absent, so an untouched quest still
            // reads as a ring waiting to be closed instead of a floating glyph.
            Circle(root.transform, "Track", tint.WithAlpha(complete ? 1f : 0.45f), 0f);

            if (!complete && progress01 > 0f)
            {
                var fill = Circle(root.transform, "Fill", tint, 0f);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.fillClockwise = true;
                fill.fillAmount = Mathf.Clamp01(progress01);
            }

            Circle(root.transform, "Inner", complete ? ArcadeTheme.BgPanel : ArcadeTheme.BgRaised, ring);

            Glyph(root.transform, id, slam, size * 0.62f,
                  complete ? ArcadeTheme.BgPanel : ArcadeTheme.BgRaised);

            if (complete)
            {
                Check(root.transform, size);
            }

            return root;
        }

        /// <summary>
        /// One glyph, centred in its parent at <paramref name="size"/> across.
        ///
        /// <paramref name="hollow"/> is whatever colour sits behind the glyph. Rings are drawn as a
        /// filled disc with a smaller disc of that colour on top, because the primitives available
        /// here have no stroke — pass the wrong colour and a ring becomes a blob.
        /// </summary>
        public static void Glyph(Transform parent, QuestId id, bool slam, float size, Color hollow)
        {
            var box = UIFactory.Child(parent, "Glyph");
            var rt = UIFactory.Rt(box);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Design, Design);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one * (size / Design);

            Transform g = box.transform;

            if (slam)
            {
                Slam(g);
                return;
            }

            switch (id)
            {
                case QuestId.ScoreFive: Sharpshooter(g); break;
                case QuestId.CleanSheet: Shutout(g); break;
                case QuestId.WinByThree: NoContest(g); break;
                case QuestId.BeatHardAi: MachineKiller(g); break;
                case QuestId.PlayThreeOnline: KickAbout(g); break;
                case QuestId.SuddenDeathWin: GoldenGoal(g, hollow); break;
                case QuestId.Comeback: OffTheRopes(g); break;
                case QuestId.BackToBack: BackToBack(g, hollow); break;
                case QuestId.BeatFriend: BraggingRights(g); break;
                default: FirstBlood(g); break;
            }
        }

        // ---------- the glyphs ----------

        /// <summary>A ball leaving a speed trail. Score goals.</summary>
        private static void Sharpshooter(Transform g)
        {
            Bar(g, ArcadeTheme.Ink.WithAlpha(0.30f), new Vector2(-17f, 6f), 14f, 4f, 0f);
            Bar(g, ArcadeTheme.Ink.WithAlpha(0.20f), new Vector2(-15.5f, -2f), 11f, 4f, 0f);
            Bar(g, ArcadeTheme.Ink.WithAlpha(0.30f), new Vector2(-17f, -10f), 14f, 4f, 0f);

            Ball(g, new Vector2(6f, 0f), 26f);
        }

        /// <summary>The first goal of the match, struck off the spot.</summary>
        private static void FirstBlood(Transform g)
        {
            Bar(g, ArcadeTheme.Ink.WithAlpha(0.28f), new Vector2(0f, -17f), 40f, 4f, 0f);
            Dot(g, ArcadeTheme.Ink.WithAlpha(0.35f), new Vector2(-14f, -9f), 7f);
            Ball(g, new Vector2(4f, 4f), 24f);
            Bar(g, ArcadeTheme.Red, new Vector2(-12f, 14f), 16f, 4.5f, 28f);
        }

        /// <summary>A goal frame with a shield across the mouth. Nothing got through.</summary>
        private static void Shutout(Transform g)
        {
            Bar(g, ArcadeTheme.Ink, new Vector2(0f, 15.5f), 46f, 5f, 0f);
            Bar(g, ArcadeTheme.Ink, new Vector2(-20.5f, 1f), 34f, 5f, 90f);
            Bar(g, ArcadeTheme.Ink, new Vector2(20.5f, 1f), 34f, 5f, 90f);

            // Net, hinted. Two lines are enough at this size; a full mesh turns to grey.
            Bar(g, ArcadeTheme.Ink.WithAlpha(0.22f), new Vector2(0f, 7f), 30f, 2f, 0f);
            Bar(g, ArcadeTheme.Ink.WithAlpha(0.22f), new Vector2(0f, -1f), 30f, 2f, 0f);

            // A crest: a flat-topped panel with a rounded foot under it.
            Box(g, ArcadeTheme.Blue, new Vector2(0f, 5f), 26f, 16f, ArcadeTheme.RadSm);
            Dot(g, ArcadeTheme.Blue, new Vector2(0f, -3f), 26f);
        }

        /// <summary>Three balls climbing off the baseline. Win by three.</summary>
        private static void NoContest(Transform g)
        {
            Bar(g, ArcadeTheme.Ink.WithAlpha(0.28f), new Vector2(0f, -17f), 44f, 4f, 0f);

            Dot(g, ArcadeTheme.Red.WithAlpha(0.60f), new Vector2(-15f, -6f), 12f);
            Dot(g, ArcadeTheme.Ink.WithAlpha(0.75f), new Vector2(0f, -1f), 17f);
            Dot(g, ArcadeTheme.Gold, new Vector2(16f, 5f), 22f);

            Bar(g, ArcadeTheme.Gold, new Vector2(12.5f, 20.5f), 9.9f, 3.6f, 45f);
            Bar(g, ArcadeTheme.Gold, new Vector2(19.5f, 20.5f), 9.9f, 3.6f, -45f);
        }

        /// <summary>The machine's head, with the difficulty wound all the way up.</summary>
        private static void MachineKiller(Transform g)
        {
            Bar(g, ArcadeTheme.Red, new Vector2(-8f, 16.5f), 9f, 3.6f, 90f);
            Dot(g, ArcadeTheme.Gold, new Vector2(-8f, 22.6f), 6.4f);

            Box(g, ArcadeTheme.Red, new Vector2(-8f, -0.5f), 30f, 27f, ArcadeTheme.RadSm);
            Dot(g, ArcadeTheme.BgDeep, new Vector2(-14f, 1f), 8.6f);
            Dot(g, ArcadeTheme.BgDeep, new Vector2(-2f, 1f), 8.6f);
            Bar(g, ArcadeTheme.BgDeep.WithAlpha(0.55f), new Vector2(-8f, -8.5f), 10f, 3f, 0f);

            // Three pips, the way every difficulty control in every game says "hard".
            Bar(g, ArcadeTheme.Gold.WithAlpha(0.5f), new Vector2(14f, -12f), 6f, 4f, 90f);
            Bar(g, ArcadeTheme.Gold.WithAlpha(0.75f), new Vector2(20f, -9.5f), 11f, 4f, 90f);
            Bar(g, ArcadeTheme.Gold, new Vector2(26f, -7f), 16f, 4f, 90f);
        }

        /// <summary>Two nodes and a link — the online badge's own shorthand for a connection.</summary>
        private static void KickAbout(Transform g)
        {
            Bar(g, ArcadeTheme.Gold, Vector2.zero, 28f, 5f, -35f);
            Dot(g, ArcadeTheme.Gold, new Vector2(-13f, -9f), 16f);
            Dot(g, ArcadeTheme.Gold, new Vector2(13f, 9f), 16f);
        }

        /// <summary>A clock run down to nothing, with the ball where the hands were.</summary>
        private static void GoldenGoal(Transform g, Color hollow)
        {
            Bar(g, ArcadeTheme.Ink, new Vector2(0f, 23f), 16f, 4.5f, 0f);

            Dot(g, ArcadeTheme.Ink.WithAlpha(0.35f), new Vector2(0f, -2f), 44f);
            Dot(g, hollow, new Vector2(0f, -2f), 34f);

            // The wedge the clock has eaten, as a single hand rather than an arc — the primitives
            // have no arc, and a hand at the top of the dial says "no time left" just as plainly.
            Bar(g, ArcadeTheme.Red, new Vector2(0f, 6f), 15f, 3.5f, 90f);

            Ball(g, new Vector2(0f, -2f), 18f);
        }

        /// <summary>Two sunk bars and a line climbing out past them.</summary>
        private static void OffTheRopes(Transform g)
        {
            Box(g, ArcadeTheme.Red.WithAlpha(0.55f), new Vector2(-18.5f, -13f), 9f, 14f, ArcadeTheme.RadSm);
            Box(g, ArcadeTheme.Red.WithAlpha(0.35f), new Vector2(-6.5f, -10f), 9f, 20f, ArcadeTheme.RadSm);

            Bar(g, ArcadeTheme.Go, new Vector2(-3f, -3f), 39.7f, 5f, 41f);
            Bar(g, ArcadeTheme.Go, new Vector2(10f, 16f), 14.1f, 5f, 8f);
            Bar(g, ArcadeTheme.Go, new Vector2(16.5f, 10f), 14f, 5f, 86f);
        }

        /// <summary>Two closed rings, each ticked. The count is the picture.</summary>
        private static void BackToBack(Transform g, Color hollow)
        {
            Ticked(g, new Vector2(-12f, 0f), hollow);
            Ticked(g, new Vector2(12f, 0f), hollow);
        }

        private static void Ticked(Transform g, Vector2 at, Color hollow)
        {
            Dot(g, ArcadeTheme.Gold, at, 26f);
            Dot(g, hollow, at, 19f);
            Bar(g, ArcadeTheme.Gold, at + new Vector2(-3f, -2f), 6.5f, 3.2f, -45f);
            Bar(g, ArcadeTheme.Gold, at + new Vector2(2.5f, 1f), 11f, 3.2f, 48f);
        }

        /// <summary>Two silhouettes; the gold one stands higher, and wears the crown.</summary>
        private static void BraggingRights(Transform g)
        {
            // Shoulders are a disc whose lower half the mask cuts away, exactly as the Person icon
            // in UIFactory does it. The mask clips the heads too, so both have to sit inside the box.
            var clip = UIFactory.Child(g, "Clip");
            UIFactory.Stretch(UIFactory.Rt(clip), 0);
            clip.AddComponent<RectMask2D>();
            Transform c = clip.transform;

            Dot(c, ArcadeTheme.InkMuted, new Vector2(-13f, 2f), 14f);
            Dot(c, ArcadeTheme.InkMuted, new Vector2(-13f, -30f), 26f);

            Dot(c, ArcadeTheme.Gold, new Vector2(11f, 12f), 17f);
            Dot(c, ArcadeTheme.Gold, new Vector2(11f, -26f), 30f);

            // Crown, above the head rather than on it, so it still reads at 30px.
            Bar(c, ArcadeTheme.Gold, new Vector2(11f, 22f), 17f, 4f, 0f);
            Dot(c, ArcadeTheme.Gold, new Vector2(3.5f, 25.5f), 5f);
            Dot(c, ArcadeTheme.Gold, new Vector2(11f, 27f), 6f);
            Dot(c, ArcadeTheme.Gold, new Vector2(18.5f, 25.5f), 5f);
        }

        /// <summary>The win burst, frozen, with a check in the middle of it.</summary>
        private static void Slam(Transform g)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                float rad = angle * Mathf.Deg2Rad;
                Bar(g, ArcadeTheme.Gold, new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 22f,
                    9f, 4f, angle);
            }

            Dot(g, ArcadeTheme.Gold, Vector2.zero, 30f);
            Bar(g, ArcadeTheme.OnGold, new Vector2(-4f, -2f), 8f, 4.4f, -45f);
            Bar(g, ArcadeTheme.OnGold, new Vector2(3f, 1.5f), 14f, 4.4f, 48f);
        }

        // ---------- primitives ----------

        /// <summary>A football: gold, with the four dark patches that make it read as one.</summary>
        private static void Ball(Transform g, Vector2 at, float size)
        {
            float k = size / 26f;
            Dot(g, ArcadeTheme.Gold, at, size);
            Dot(g, ArcadeTheme.OnGold, at, 8.8f * k);
            Dot(g, ArcadeTheme.OnGold, at + new Vector2(0f, 10.6f) * k, 5.2f * k);
            Dot(g, ArcadeTheme.OnGold, at + new Vector2(9.4f, -4.6f) * k, 5.2f * k);
            Dot(g, ArcadeTheme.OnGold, at + new Vector2(-9.4f, -4.6f) * k, 5.2f * k);
        }

        private static void Dot(Transform parent, Color color, Vector2 pos, float size)
        {
            var go = UIFactory.Child(parent, "Dot");
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.Disc();
            img.color = color;
            img.raycastTarget = false;
            Place(UIFactory.Rt(go), pos, new Vector2(size, size), 0f);
        }

        private static void Bar(Transform parent, Color color, Vector2 pos,
                                float length, float thickness, float angle)
        {
            var go = UIFactory.Child(parent, "Bar");
            UIFactory.RoundedImage(go, ArcadeTheme.RadSm, color, false);
            Place(UIFactory.Rt(go), pos, new Vector2(length, thickness), angle);
        }

        private static void Box(Transform parent, Color color, Vector2 pos,
                                 float width, float height, int radius)
        {
            var go = UIFactory.Child(parent, "Rect");
            UIFactory.RoundedImage(go, radius, color, false);
            Place(UIFactory.Rt(go), pos, new Vector2(width, height), 0f);
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size, float angle)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>A full-bleed circle, optionally inset — the ring's track, fill and hole.</summary>
        private static Image Circle(Transform parent, string name, Color color, float inset)
        {
            var go = UIFactory.Child(parent, name);
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.Disc();
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            UIFactory.Stretch(UIFactory.Rt(go), inset);
            return img;
        }

        /// <summary>The gold check stamped into the corner of a finished quest.</summary>
        private static void Check(Transform parent, float size)
        {
            float chip = Mathf.Max(18f, size * 0.34f);

            var go = UIFactory.Child(parent, "Check");
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.Disc();
            img.color = ArcadeTheme.Gold;
            img.raycastTarget = false;

            var rt = UIFactory.Rt(go);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(chip, chip);
            rt.anchoredPosition = new Vector2(-chip * 0.18f, chip * 0.18f);

            float k = chip / 24f;
            Bar(go.transform, ArcadeTheme.OnGold, new Vector2(-2.6f, -1.4f) * k, 6.4f * k, 3.2f * k, -45f);
            Bar(go.transform, ArcadeTheme.OnGold, new Vector2(2f, 1f) * k, 11f * k, 3.2f * k, 48f);
        }
    }
}
