using System;
using TableFootball.Net;
using TableFootball.Progression;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// Today's three quests, the level they feed, and how long the set has left. Built on UI Toolkit,
    /// styled by <c>Resources/UI/Styles/Quests.uss</c>.
    ///
    /// A read-only screen on purpose: there is no Claim button anywhere in it. A claim step on a
    /// phone is a chore, and a quest left unclaimed overnight is a sour surprise — so quests bank
    /// themselves the moment they are cleared, in front of the player, on the result screen. This is
    /// where they come to check, not to collect. A quest in progress shows the XP it will pay; a
    /// cleared one shows DONE.
    ///
    /// Left: the level as a ring and the day streak. Right: the three quests and, under them, the
    /// bonus for clearing all three.
    /// </summary>
    public class QuestsMenu : MonoBehaviour
    {
        private const int StreakPips = 7;

        private VisualElement root;
        private VisualElement questList;
        private Label resetLabel, levelNumber, levelCaption, streakLabel, slamNote;
        private UiRing ring;
        private VisualElement streakRow;
        private VisualElement slamPips;
        private UiFrame slamRow;

        private float nextTick;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        /// <summary><paramref name="canvasRoot"/> is the uGUI canvas the older screens hang off, unused
        /// here and kept so the boot order in TableFootballUI does not change.</summary>
        public void Build(Transform canvasRoot)
        {
            root = UiKit.El("screen quests", UiToolkitHost.Root, "Quests");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Quests");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.pickingMode = PickingMode.Position;

            UiBackdrop.AddTo(root);

            BuildHeader();
            BuildLevel();
            BuildRight();

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        // ---------- build ----------

        private void BuildHeader()
        {
            var header = UiKit.El("q__header", root);

            var back = UiKit.Frame("sq q__back", header);
            back.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(back, () => OnBack?.Invoke(), UiSound.Back);

            var box = UiKit.El("q__title-box", header);
            var row = UiKit.El("q__title-row", box);
            row.Add(new UiIcon(UiIcon.Glyph.Quests, ArcadeTheme.Pulse, 2f));
            UiKit.Text("DAILY QUESTS", "q__title f-display", row);
            UiKit.El("q__bar", box);

            var reset = UiKit.Frame("q__reset", header);
            reset.Add(new UiIcon(UiIcon.Glyph.Clock, ArcadeTheme.InkMuted, 2f));
            resetLabel = UiKit.Text(string.Empty, "q__reset-label f-display", reset);
        }

        private void BuildLevel()
        {
            var panel = UiKit.Frame("frame--panel q__level", root);

            ring = new UiRing { Thickness = 14f, Fill = ArcadeTheme.Pulse, Track = ArcadeTheme.BgRaised };
            ring.AddToClassList("q__ring");
            levelNumber = UiKit.Text("1", "q__level-number f-display", ring);
            UiKit.Text("LEVEL", "q__level-word f-display-semi", ring);
            panel.Add(ring);

            levelCaption = UiKit.Text(string.Empty, "q__level-caption f-display-semi", panel);

            UiKit.El("q__rule", panel);

            var streakHead = UiKit.El("q__streak-head", panel);
            streakHead.Add(new UiIcon(UiIcon.Glyph.Flame, ArcadeTheme.Reward, 2f));
            streakLabel = UiKit.Text(string.Empty, "q__streak-label f-display", streakHead);
            streakRow = UiKit.El("q__pips", panel);
            for (int i = 0; i < StreakPips; i++) UiKit.El("q__pip", streakRow);
        }

        private void BuildRight()
        {
            var right = UiKit.El("q__right", root);
            questList = UiKit.El("q__list", right);

            slamRow = UiKit.Frame("q__slam", right);
            var tile = UiKit.Frame("q__slam-tile", slamRow);
            tile.Add(new UiIcon(UiIcon.Glyph.Trophy, ArcadeTheme.Reward, 2f));
            var words = UiKit.El("q__slam-words", slamRow);
            UiKit.Text("CLEAR ALL THREE", "q__slam-title f-display", words);
            slamNote = UiKit.Text(string.Empty, "q__slam-note f-display-semi", words);
            slamPips = UiKit.El("q__slam-pips", slamRow);
            var xp = UiKit.Frame("q__reward q__reward--slam", slamRow);
            UiKit.Text("+" + QuestDefinition.SlamXp + " XP", "q__reward-label f-display", xp);
        }

        // ---------- rows ----------

        /// <summary>The picture for a quest. Each is a glyph from the shared set; a quest keeps its picture
        /// whether it is finished or not, so a cleared one is still recognisably the same quest.</summary>
        private static UiIcon.Glyph GlyphFor(QuestId id)
        {
            switch (id)
            {
                case QuestId.PlayThreeOnline: return UiIcon.Glyph.Globe;
                case QuestId.FirstBlood: return UiIcon.Glyph.Bolt;
                case QuestId.ScoreFive: return UiIcon.Glyph.Star;
                case QuestId.CleanSheet: return UiIcon.Glyph.Shield;
                case QuestId.BackToBack: return UiIcon.Glyph.Levels;
                case QuestId.BeatFriend: return UiIcon.Glyph.TwoPlayers;
                case QuestId.WinByThree: return UiIcon.Glyph.Trophy;
                case QuestId.Comeback: return UiIcon.Glyph.Flame;
                case QuestId.SuddenDeathWin: return UiIcon.Glyph.Clock;
                case QuestId.BeatHardAi: return UiIcon.Glyph.Robot;
                default: return UiIcon.Glyph.Quests;
            }
        }

        private static int Diamonds(QuestTier tier) => (int)tier + 1;

        private void BuildRow(int slot)
        {
            QuestDefinition definition = DailyQuests.DefinitionAt(slot);
            bool done = DailyQuests.IsCompleteAt(slot);
            int progress = DailyQuests.ProgressAt(slot);
            Color tier = QuestIcons.TierColor(definition.Tier);

            var row = UiKit.Frame("q__row", questList);
            row.OverrideEdge(done ? ArcadeTheme.Reward : ArcadeTheme.LineLit);

            var tile = UiKit.Frame("q__tile", row);
            tile.OverrideEdge(done ? ArcadeTheme.Reward : tier);
            tile.Add(new UiIcon(GlyphFor(definition.Id), done ? ArcadeTheme.Reward : tier, 2.2f));

            var words = UiKit.El("q__words", row);
            var top = UiKit.El("q__title-line", words);
            UiKit.Text(definition.Title.ToUpperInvariant(), "q__quest-title f-display", top);
            var diamonds = UiKit.El("q__diamonds", top);
            for (int i = 0; i < Diamonds(definition.Tier); i++)
            {
                var d = UiKit.El("q__diamond", diamonds);
                d.style.backgroundColor = tier;
            }

            // The AI quest is the one exception to "online only", and the row has to say so — a
            // player who never goes online should be able to see which one they can still reach.
            string where = definition.OnlineOnly ? string.Empty : "  ·  OFFLINE OK";
            UiKit.Text(definition.Description + where, "q__desc f-body-medium", words);

            // Segmented progress: a cell per step for small targets, ten for anything bigger.
            var bar = UiKit.El("q__segments", words);
            int cells = Mathf.Clamp(definition.Target, 1, 10);
            int lit = done ? cells : Mathf.FloorToInt(DailyQuests.Progress01At(slot) * cells + 0.0001f);
            for (int i = 0; i < cells; i++)
            {
                var cell = UiKit.El("q__seg", bar);
                if (i < lit) cell.style.backgroundColor = done ? ArcadeTheme.Reward : tier;
            }

            UiKit.Text(progress + "/" + definition.Target, "q__count f-display", bar);

            if (done)
            {
                var badge = UiKit.Frame("q__reward q__reward--done", row);
                badge.OverrideEdge(ArcadeTheme.Reward);
                badge.Add(new UiIcon(UiIcon.Glyph.Check, ArcadeTheme.Reward, 2.6f));
                UiKit.Text("DONE", "q__reward-label q__reward-label--done f-display", badge);
            }
            else
            {
                var reward = UiKit.Frame("q__reward", row);
                UiKit.Text("+" + definition.Xp + " XP", "q__reward-label f-display", reward);
            }
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null) return;

            DailyQuests.EnsureToday();

            // Opening the screen is what "seeing" a cleared quest means, so the dot on the main menu
            // goes out here rather than when the quest was banked — the player may well have banked
            // it on a result screen they walked straight past.
            DailyQuests.MarkSeen();

            Unsubscribe();
            DailyQuests.OnChanged += Redraw;
            PlayerXp.OnChanged += Redraw;

            Redraw();

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(UiSound.Open);

            int i = 0;
            foreach (var row in questList.Children()) UiKit.Enter(row, 0.05f * i++);
            UiKit.Enter(slamRow, 0.05f * i);
        }

        public void Close()
        {
            Unsubscribe();
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
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
            if (!IsOpen || Time.unscaledTime < nextTick) return;

            nextTick = Time.unscaledTime + 1f;
            RefreshReset();
        }

        private void RefreshReset()
        {
            double seconds = DailyQuests.SecondsUntilReset;
            int hours = (int)(seconds / 3600d);
            int minutes = (int)((seconds % 3600d) / 60d);

            resetLabel.text = hours > 0
                ? "NEW SET IN " + hours + "H " + minutes + "M"
                : "NEW SET IN " + minutes + "M";
        }

        private void Redraw()
        {
            if (root == null) return;

            questList.Clear();
            for (int slot = 0; slot < DailyQuests.Slots; slot++) BuildRow(slot);

            levelNumber.text = PlayerXp.Level.ToString();
            ring.Progress = PlayerXp.Progress01;
            levelCaption.text = PlayerProgress.AtMaxLevel
                ? "MAX LEVEL"
                : (PlayerProgress.XpToNext - PlayerProgress.Xp) + " XP TO LEVEL " + (PlayerXp.Level + 1);

            int streak = DailyQuests.DayStreak;
            streakLabel.text = streak > 0 ? "DAY " + streak + " STREAK" : "NO STREAK YET";
            streakLabel.style.color = streak > 0 ? ArcadeTheme.Reward : ArcadeTheme.InkMuted;
            int litDays = Mathf.Min(streak, StreakPips);
            int p = 0;
            foreach (var pip in streakRow.Children()) pip.EnableInClassList("is-lit", p++ < litDays);

            RedrawSlam();
            RefreshReset();
            UiFonts.Apply(questList);
        }

        private void RedrawSlam()
        {
            int done = DailyQuests.CompletedCount;

            slamPips.Clear();
            for (int i = 0; i < DailyQuests.Slots; i++)
            {
                var pip = UiKit.El("q__slam-pip", slamPips);
                pip.EnableInClassList("is-lit", i < done);
            }

            bool banked = DailyQuests.SlamAwarded;
            slamNote.text = banked ? "BANKED" : done + " OF " + DailyQuests.Slots;
            slamNote.style.color = banked ? ArcadeTheme.Reward : ArcadeTheme.InkMuted;
            slamRow.OverrideEdge(banked ? ArcadeTheme.Reward : ArcadeTheme.LineLit);
        }
    }
}
