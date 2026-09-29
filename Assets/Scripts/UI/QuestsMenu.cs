using System;
using TableFootball.Net;
using TableFootball.Progression;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// Today's three quests, the level they feed, and how long the set has left.
    ///
    /// A read-only screen on purpose: there is no Claim button anywhere in it. A claim step on a
    /// phone is a chore, and a quest left unclaimed overnight is a sour surprise — so quests bank
    /// themselves the moment they are cleared, in front of the player, on the result screen. This is
    /// where they come to check, not to collect: a cleared quest gets the gold edge and its XP chip
    /// lights up, and that is the whole story.
    ///
    /// Built on UI Toolkit (see <see cref="UiToolkitHost"/>), styled by
    /// <c>Resources/UI/Styles/Quests.uss</c>. Laid out wide rather than tall, like the design: a level
    /// card on the left, quest rows and the daily slam strip on the right.
    /// </summary>
    public class QuestsMenu : MonoBehaviour
    {
        private VisualElement root;
        private Label resetLabel;
        private Label levelNumber;
        private Label levelXp;
        private Label levelCaption;
        private Label streakTitle;
        private Label streakNote;
        private RingMeter levelRing;
        private VisualElement xpFill;
        private VisualElement rows;
        private VisualElement slamPips;
        private Label slamNote;

        private float nextTick;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && UiKit.IsShown(root);

        public void Build(Transform canvasRoot)
        {
            root = UiKit.El("screen quests", UiToolkitHost.Root, "QuestsMenu");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Quests");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.pickingMode = PickingMode.Position;

            UiKit.El("bleed quests__scrim", root).pickingMode = PickingMode.Ignore;
            var glow = UiKit.El("bleed quests__glow", root);
            glow.pickingMode = PickingMode.Ignore;
            glow.style.backgroundImage = new StyleBackground(UiKit.TopGlow());

            var header = UiKit.Header(root, "DAILY QUESTS", Back);
            var right = UiKit.El("header__right", header);
            var timer = UiKit.El("chip quests__timer", right);
            timer.Add(new UiIcon(UiIcon.Glyph.Clock, ArcadeTheme.BlueSoft, 2f));
            resetLabel = UiKit.Text(string.Empty, "quests__timer-label f-body-semi", timer);

            var main = UiKit.El("quests__main", root);
            BuildLevelCard(main);
            BuildQuestColumn(main);

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        // ---------- left: the level ----------

        private void BuildLevelCard(VisualElement parent)
        {
            var card = UiKit.El("panel quests__level", parent);

            var ringBox = UiKit.El("quests__ring-box", card);
            levelRing = new RingMeter();
            ringBox.Add(levelRing);
            var ringText = UiKit.El("quests__ring-text", ringBox);
            UiKit.Text("LEVEL", "quests__ring-caption f-body-semi", ringText);
            levelNumber = UiKit.Text("1", "quests__ring-number f-display", ringText);

            levelXp = UiKit.Text(string.Empty, "quests__xp f-display-semi", card);
            levelCaption = UiKit.Text(string.Empty, "quests__xp-note f-body-medium", card);

            UiKit.El("quests__divider", card);

            var streakLabel = UiKit.Text("DAILY STREAK", "caption f-body-semi", card);
            streakLabel.style.marginBottom = 4f;

            var streakRow = UiKit.El("quests__streak-row", card);
            var streakIcon = UiKit.El("quests__streak-icon", streakRow);
            streakIcon.Add(new UiIcon(UiIcon.Glyph.Flame, ArcadeTheme.BlueSoft, 2f));
            var streakWords = UiKit.El("col", streakRow);
            streakTitle = UiKit.Text(string.Empty, "quests__streak-title f-display-semi", streakWords);
            streakNote = UiKit.Text(string.Empty, "quests__streak-note f-body-medium", streakWords);
        }

        // ---------- right: the quests ----------

        private void BuildQuestColumn(VisualElement parent)
        {
            var column = UiKit.El("quests__column", parent);
            rows = UiKit.El("quests__rows", column);
            BuildSlamStrip(column);
        }

        private void BuildSlamStrip(VisualElement parent)
        {
            var strip = UiKit.El("quests__slam", parent);
            var iconRing = UiKit.El("quests__slam-icon", strip);
            iconRing.Add(new UiIcon(UiIcon.Glyph.Star, ArcadeTheme.Gold, 2f));

            var words = UiKit.El("col grow", strip);
            UiKit.Text("DAILY SLAM", "quests__slam-title f-display-semi", words);
            UiKit.Text("Clear all three quests for a bonus", "quests__slam-note f-body-medium", words);

            slamPips = UiKit.El("row quests__slam-pips", strip);

            slamNote = UiKit.Text(string.Empty, "quests__slam-xp f-display-semi", strip);
        }

        /// <summary>
        /// One quest row: an icon in a coloured ring, the title and tier, its description, a progress
        /// bar, and the XP it pays. Nothing here is a button — see the class remarks.
        /// </summary>
        private VisualElement BuildRow(int slot)
        {
            QuestDefinition definition = DailyQuests.DefinitionAt(slot);
            bool done = DailyQuests.IsCompleteAt(slot);
            int progress = DailyQuests.ProgressAt(slot);
            Color tier = TierColor(definition.Tier);

            var row = UiKit.El("quests__row" + (done ? " is-done" : string.Empty));
            row.style.borderTopColor = row.style.borderBottomColor =
                row.style.borderLeftColor = row.style.borderRightColor = done ? ArcadeTheme.Gold : tier.WithAlpha(0.5f);

            var ring = UiKit.El("quests__row-icon", row);
            ring.style.borderTopColor = ring.style.borderBottomColor =
                ring.style.borderLeftColor = ring.style.borderRightColor = done ? ArcadeTheme.Gold : tier;
            ring.Add(new UiIcon(QuestGlyph(definition.Id), ArcadeTheme.Ink, 2f));

            var words = UiKit.El("col grow", row);
            var titleRow = UiKit.El("row quests__row-title", words);
            UiKit.Text(definition.Title.ToUpperInvariant(), "quests__row-name f-display", titleRow);
            var tierChip = UiKit.Text(definition.Tier.ToString().ToUpperInvariant(), "quests__row-tier f-body-semi", titleRow);
            tierChip.style.color = tier;
            tierChip.style.backgroundColor = tier.WithAlpha(0.16f);
            if (!definition.OnlineOnly)
            {
                UiKit.Text("OFFLINE", "quests__row-offline f-body-semi", titleRow);
            }

            UiKit.Text(definition.Description, "quests__row-desc f-body-medium", words);

            var progressRow = UiKit.El("row quests__row-progress", words);
            var track = UiKit.El("bar grow", progressRow);
            var fill = UiKit.El("bar__fill", track);
            fill.style.width = Length.Percent(Mathf.Clamp01(DailyQuests.Progress01At(slot)) * 100f);
            fill.style.backgroundColor = done ? ArcadeTheme.Gold : tier;
            var count = UiKit.Text($"{progress}/{definition.Target}", "quests__row-count f-display-semi", progressRow);
            count.style.color = done ? ArcadeTheme.Gold : ArcadeTheme.Ink;

            var xp = UiKit.El("quests__row-xp", row);
            xp.style.color = done ? ArcadeTheme.Gold : ArcadeTheme.Ink;
            UiKit.Text("+" + definition.Xp + " XP", "f-display-semi", xp);

            return row;
        }

        private static UiIcon.Glyph QuestGlyph(QuestId id) => id switch
        {
            QuestId.PlayThreeOnline => UiIcon.Glyph.Globe,
            QuestId.FirstBlood => UiIcon.Glyph.Bolt,
            QuestId.ScoreFive => UiIcon.Glyph.Target,
            QuestId.CleanSheet => UiIcon.Glyph.Shield,
            QuestId.BackToBack => UiIcon.Glyph.Repeat,
            QuestId.BeatFriend => UiIcon.Glyph.Crown,
            QuestId.WinByThree => UiIcon.Glyph.TrendUp,
            QuestId.Comeback => UiIcon.Glyph.Flame,
            QuestId.SuddenDeathWin => UiIcon.Glyph.Clock,
            QuestId.BeatHardAi => UiIcon.Glyph.Robot,
            _ => UiIcon.Glyph.Star,
        };

        private static Color TierColor(QuestTier tier) => tier switch
        {
            QuestTier.Bronze => ArcadeTheme.Bronze,
            QuestTier.Silver => ArcadeTheme.Silver,
            _ => ArcadeTheme.Gold,
        };

        // ---------- state ----------

        public void Open()
        {
            if (root == null) return;

            DailyQuests.EnsureToday();

            // Opening the screen is what "seeing" a cleared quest means, so the dot on the main menu
            // goes out here rather than when the quest was banked — the player may well have banked
            // it on a result screen they walked straight past.
            DailyQuests.MarkSeen();

            DailyQuests.OnChanged -= Redraw;
            DailyQuests.OnChanged += Redraw;
            PlayerXp.OnChanged -= Redraw;
            PlayerXp.OnChanged += Redraw;

            Redraw();

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);

            for (int i = 0; i < rows.childCount; i++) UiKit.Enter(rows.ElementAt(i), i * 0.06f);
        }

        public void Close()
        {
            DailyQuests.OnChanged -= Redraw;
            PlayerXp.OnChanged -= Redraw;
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy()
        {
            DailyQuests.OnChanged -= Redraw;
            PlayerXp.OnChanged -= Redraw;
        }

        /// <summary>
        /// Keeps the countdown honest while the screen is up. Once a second, unscaled — the front end
        /// runs at <c>timeScale 0</c> and a scaled timer here would simply never move.
        /// </summary>
        private void Update()
        {
            if (root == null || !IsOpen || Time.unscaledTime < nextTick) return;

            nextTick = Time.unscaledTime + 1f;
            RefreshReset();
        }

        private void RefreshReset()
        {
            if (resetLabel == null) return;

            double seconds = DailyQuests.SecondsUntilReset;
            int hours = (int)(seconds / 3600d);
            int minutes = (int)((seconds % 3600d) / 60d);

            resetLabel.text = hours > 0
                ? $"NEW SET IN {hours}H {minutes}M"
                : $"NEW SET IN {minutes}M";
        }

        private void Redraw()
        {
            if (root == null) return;

            rows.Clear();
            for (int slot = 0; slot < DailyQuests.Slots; slot++) rows.Add(BuildRow(slot));

            levelNumber.text = PlayerXp.Level.ToString();
            levelRing.Progress = PlayerXp.Progress01;
            levelXp.text = PlayerXp.AtMaxLevel
                ? $"{PlayerXp.Total:N0} XP"
                : $"{PlayerXp.IntoLevel:N0} / {PlayerXp.LevelSpan:N0} XP";

            int left = PlayerXp.LevelSpan - PlayerXp.IntoLevel;
            levelCaption.text = PlayerXp.AtMaxLevel ? "max level" : $"{left} XP to level {PlayerXp.Level + 1}";

            int streak = DailyQuests.DayStreak;
            streakTitle.text = streak > 0 ? $"Day {streak} streak" : "No streak yet";
            streakTitle.style.color = streak > 0 ? ArcadeTheme.Gold : ArcadeTheme.Ink;
            streakNote.text = streak > 0 ? "Cleared all three today" : "Clear all three today to start one";

            RedrawSlam();
            RefreshReset();
        }

        private void RedrawSlam()
        {
            if (slamPips == null) return;

            int done = DailyQuests.CompletedCount;

            slamPips.Clear();
            for (int i = 0; i < DailyQuests.Slots; i++)
            {
                var pip = UiKit.El("quests__slam-pip" + (i < done ? " is-lit" : string.Empty), slamPips);
            }

            slamNote.text = "+" + QuestDefinition.SlamXp + " XP";
            slamNote.style.color = DailyQuests.SlamAwarded ? ArcadeTheme.Gold : ArcadeTheme.InkMuted;
        }

        private void Back() => OnBack?.Invoke();

        /// <summary>
        /// The level ring: a track and a gold arc filled clockwise from the top, drawn with the vector
        /// API the same way <see cref="UiIcon"/> draws a glyph — the theme has no annulus asset and
        /// the icon primitives have no stroke arc long enough to serve as a meter.
        /// </summary>
        private sealed class RingMeter : VisualElement
        {
            private float progress;

            public RingMeter()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("quests__ring");
                generateVisualContent += Draw;
            }

            public float Progress
            {
                get => progress;
                set { progress = Mathf.Clamp01(value); MarkDirtyRepaint(); }
            }

            private void Draw(MeshGenerationContext ctx)
            {
                Rect r = contentRect;
                if (r.width <= 0f || r.height <= 0f) return;

                float radius = Mathf.Min(r.width, r.height) * 0.5f - 5f;
                Vector2 center = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

                Painter2D p = ctx.painter2D;
                p.lineWidth = 10f;
                p.lineCap = LineCap.Round;

                p.strokeColor = ArcadeTheme.BgDeep;
                p.BeginPath();
                p.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();

                if (progress <= 0f) return;

                p.strokeColor = ArcadeTheme.Gold;
                p.BeginPath();
                // From the top (-90deg), clockwise, however far progress goes.
                p.Arc(center, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + progress * 360f));
                p.Stroke();
            }
        }
    }
}
