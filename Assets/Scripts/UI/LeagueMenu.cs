using System;
using System.Collections.Generic;
using TableFootball.Net;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The ranked screen: which league you are in and what this week pays (left), a podium for the
    /// top three — the ones who go up (middle) — and the rest of your pod of fifteen with the bottom
    /// two, who go down, marked in red (right). Your own row is taller, cyan and wider than the list so
    /// you find yourself at a glance. Built on UI Toolkit, styled by
    /// <c>Resources/UI/Styles/Ranked.uss</c>.
    ///
    /// Like every other screen it reads its data from <see cref="Ladder"/> and
    /// <see cref="RankedRewards"/>, redraws on their change events and talks out only through
    /// callbacks. It holds no ladder logic of its own.
    ///
    /// The three DEBUG buttons (editor-only, see <see cref="BuildDebug"/>) stand in for real ranked
    /// results until the online ranked queue is exercised in practice: they let a single developer
    /// move points and trigger the weekly rollover to see the whole system behave without two
    /// devices. They are the one temporary thing here.
    /// </summary>
    public class LeagueMenu : MonoBehaviour
    {
        // Podium metals. Gold here is the medal, not the reward colour; silver and bronze are used
        // nowhere else.
        private static readonly Color Silver = new Color32(0xC9, 0xD6, 0xE8, 0xFF);
        private static readonly Color Bronze = new Color32(0xD9, 0x89, 0x4B, 0xFF);

        /// <summary>Step heights on the podium, by rank, in panel units (first is tallest).</summary>
        private static readonly float[] StepHeights = { 150f, 108f, 80f };

        private VisualElement root;

        // The week and points the player last saw here, so a gain since then can fly in. Static: it has
        // to outlive the screen, which is closed and reopened around every match.
        private static int seenWeek = -1;
        private static int seenPoints = -1;

        private UiCoinChip coinChip;
        private UiPointsChip pointsChip;
        private bool pointsFlying;

        private VisualElement badgeHolder;
        private Label leagueName, standingLine, zoneChip, timerLabel;
        private VisualElement zoneBox;
        private readonly List<VisualElement> ladderRows = new List<VisualElement>();

        private UiFrame payBox;
        private UiCoin payCoin;
        private Label payLabel;
        private VisualElement payClaim;
        private VisualElement play;

        private VisualElement podium;
        private ScrollView list;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        /// <summary>Raised when the player starts a ranked match from here.</summary>
        public Action OnPlayRanked;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        /// <summary><paramref name="canvasRoot"/> is the uGUI canvas the older screens hang off, unused
        /// here and kept so the boot order in TableFootballUI does not change.</summary>
        public void Build(Transform canvasRoot)
        {
            root = UiKit.El("screen ranked", UiToolkitHost.Root, "Ranked");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/Ranked");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.pickingMode = PickingMode.Position;

            UiBackdrop.AddTo(root);

            BuildHeader();
            BuildSide();
            BuildPodiumArea();
            BuildListArea();

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        // ---------- build ----------

        private void BuildHeader()
        {
            var header = UiKit.El("rk__header", root);

            var back = UiKit.Frame("sq rk__back", header);
            back.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(back, Back, UiSound.Back);

            var box = UiKit.El("rk__title-box", header);
            var row = UiKit.El("rk__title-row", box);
            row.Add(new UiIcon(UiIcon.Glyph.Trophy, ArcadeTheme.Pulse, 2f));
            UiKit.Text("RANKED", "rk__title f-display", row);
            UiKit.El("rk__bar", box);

            // This week's points and the coin balance: the two numbers a ranked result moves.
            var tools = UiKit.El("rk__tools", header);
            pointsChip = new UiPointsChip();
            tools.Add(pointsChip);
            coinChip = new UiCoinChip();
            coinChip.AddToClassList("rk__coin-chip");
            tools.Add(coinChip);
        }

        private void BuildSide()
        {
            var side = UiKit.Frame("frame--panel rk__side", root);

            // Who you are in the ladder: the badge, the league, and where you sit.
            var top = UiKit.El("rk__id", side);
            badgeHolder = UiKit.El("rk__badge", top);
            var words = UiKit.El("rk__id-words", top);
            leagueName = UiKit.Text("RANKED", "rk__league f-display", words);
            standingLine = UiKit.Text(string.Empty, "rk__standing f-display-semi", words);

            zoneBox = UiKit.El("rk__zone", side);
            zoneChip = UiKit.Text(string.Empty, "rk__zone-label f-display", zoneBox);
            var timer = UiKit.El("rk__timer", zoneBox);
            timer.Add(new UiIcon(UiIcon.Glyph.Clock, ArcadeTheme.InkMuted, 2f));
            timerLabel = UiKit.Text(string.Empty, "rk__timer-label f-display-semi", timer);

            // The four rungs, top one first; yours is lit.
            var rungs = UiKit.El("rk__rungs", side);
            for (int i = Leagues.Count - 1; i >= 0; i--)
            {
                League league = (League)i;
                Color metal = UIFactory.LeagueColor(league);

                var rung = UiKit.El("rk__rung", rungs);
                rung.userData = league;
                var dot = UiKit.El("rk__rung-dot", rung);
                dot.style.backgroundColor = metal;
                UiKit.Text(Leagues.ShortName(league).ToUpperInvariant(), "rk__rung-name f-display-semi", rung);
                var you = UiKit.Text("YOU", "rk__rung-you f-display", rung);
                you.style.color = metal;
                ladderRows.Add(rung);
            }

            // What the week pays.
            payBox = UiKit.Frame("rk__pay", side);
            payCoin = new UiCoin();
            payBox.Add(payCoin);
            payLabel = UiKit.Text(string.Empty, "rk__pay-label f-display-semi", payBox);
            payClaim = UiKit.Button("COLLECT", "reward", ClaimWeekly, payBox);
            payClaim.AddToClassList("rk__pay-claim");

            play = UiKit.Button("PLAY RANKED", "pulse", () => OnPlayRanked?.Invoke(), side);
            play.AddToClassList("rk__play");

            BuildDebug(side);
        }

        private void BuildPodiumArea()
        {
            var area = UiKit.El("rk__center", root);
            Heading(area, UiIcon.Glyph.Trophy, "TOP 3 GO UP", ArcadeTheme.Pulse);
            podium = UiKit.El("rk__podium", area);
        }

        private void BuildListArea()
        {
            var area = UiKit.El("rk__right", root);
            Heading(area, UiIcon.Glyph.TwoPlayers, "YOUR POD", ArcadeTheme.Pulse);
            list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            list.AddToClassList("rk__list");
            list.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            area.Add(list);
        }

        private static void Heading(VisualElement parent, UiIcon.Glyph glyph, string text, Color color)
        {
            var head = UiKit.El("rk__heading", parent);
            head.Add(new UiIcon(glyph, color, 2f));
            var label = UiKit.Text(text, "rk__heading-label f-display", head);
            label.style.color = color;
            UiKit.El("rk__heading-rule", head);
        }

        /// <summary>
        /// Editor-only, and deliberately not just hidden: <c>#if UNITY_EDITOR</c> means a player build
        /// never compiles this method's body in at all, which is what "not visible in the public
        /// build" actually requires — a runtime visibility flag would still ship the row (and its
        /// direct calls into <see cref="Ladder.SubmitResult"/>/<see cref="Ladder.AdvanceWeekForTesting"/>)
        /// inside the shipped assembly, findable and callable by anyone who went looking.
        /// </summary>
        private void BuildDebug(VisualElement parent)
        {
#if UNITY_EDITOR
            var row = UiKit.El("rk__debug", parent);
            UiKit.Button("DEBUG WIN", "ghost", () => Ladder.SubmitResult(0, true), row);
            UiKit.Button("DEBUG LOSS", "ghost", () => Ladder.SubmitResult(0, false), row);
            UiKit.Button("+WEEK", "ghost", Ladder.AdvanceWeekForTesting, row);
#endif
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null) return;

            Unsubscribe();
            Ladder.OnChanged += Redraw;
            // Its own event as well as the ladder's: a payout is banked by RankedRewards.Sync when the
            // week turns over, which happens in response to the ladder refresh below rather than
            // before it — so the reward strip has to be told separately or it draws the week that has
            // just ended as if it were still running.
            RankedRewards.OnChanged += Redraw;

            Redraw();

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);
            GameSfx.PlayUi(UiSound.Open);

            Ladder.Refresh(); // fills the cache, then fires Redraw through OnChanged
        }

        public void Close()
        {
            Unsubscribe();
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            Ladder.OnChanged -= Redraw;
            RankedRewards.OnChanged -= Redraw;
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }

        /// <summary>
        /// Collects the weekly payout. The coins land in <see cref="Wallet"/>, which every coin chip in
        /// the game is already listening to, so the number moves everywhere at once without this
        /// screen telling any of them.
        /// </summary>
        private void ClaimWeekly()
        {
            int coins = RankedRewards.Claim();
            RedrawReward();

            if (coins > 0)
            {
                payLabel.text = "+" + coins.ToString("N0") + " COLLECTED";
                RewardFly.Play(RewardFly.Kind.Coins, payBox, coinChip, coins);
            }
        }

        /// <summary>
        /// Flies trophies from the league badge to the points chip when this week's points are higher
        /// than the last time the player looked — which is how a ranked win shows up when they come back
        /// here. The first look of a session, and a new week, only set the baseline.
        /// </summary>
        private void CheckPointsGain()
        {
            LadderStanding s = Ladder.Standing;
            if (!s.Valid || pointsFlying) return;

            int gain = seenWeek == s.WeekIndex && seenPoints >= 0 ? s.WeeklyPoints - seenPoints : 0;
            int before = seenPoints;
            seenWeek = s.WeekIndex;
            seenPoints = s.WeeklyPoints;

            if (gain <= 0) return;

            // Hold the chip at what it showed, then let the trophies carry it up.
            pointsFlying = true;
            pointsChip.Freeze(true);
            pointsChip.SetNow(before);
            pointsChip.schedule.Execute(() =>
            {
                // Left the screen in the meantime: nothing to watch, so just settle the number.
                if (!IsOpen)
                {
                    pointsChip.Freeze(false);
                    pointsFlying = false;
                    return;
                }

                RewardFly.Play(RewardFly.Kind.Trophies, badgeHolder, pointsChip, gain, () => pointsFlying = false);
            }).StartingIn(450);
        }

        private void Redraw()
        {
            if (root == null) return;

            RedrawReward();
            CheckPointsGain();

            LadderStanding s = Ladder.Standing;
            badgeHolder.Clear();

            if (!s.Valid)
            {
                badgeHolder.Add(UiArt.LeagueBadge(League.Bronze, 84f));
                leagueName.text = "RANKED";
                leagueName.style.color = ArcadeTheme.Ink;
                standingLine.text = "LOADING…";
                UiKit.Show(zoneBox, false);
                foreach (var rung in ladderRows) rung.RemoveFromClassList("is-here");
                podium.Clear();
                list.Clear();
                return;
            }

            Color metal = UIFactory.LeagueColor(s.League);
            badgeHolder.Add(UiArt.LeagueBadge(s.League, 84f));
            leagueName.text = Leagues.ShortName(s.League).ToUpperInvariant();
            leagueName.style.color = metal;
            standingLine.text = "#" + s.RankInPod + " OF " + s.PodSize + "  ·  " + s.WeeklyPoints + " PTS";

            UiKit.Show(zoneBox, true);
            zoneChip.text = s.InPromotionZone ? "PROMOTION ZONE"
                          : s.InRelegationZone ? "RELEGATION ZONE"
                          : "HOLDING";
            zoneChip.style.color = s.InPromotionZone ? ArcadeTheme.Pulse
                                 : s.InRelegationZone ? ArcadeTheme.Danger
                                 : ArcadeTheme.InkMuted;
            timerLabel.text = FormatCountdown(s.SecondsToRollover);

            foreach (var rung in ladderRows)
            {
                rung.EnableInClassList("is-here", (League)rung.userData == s.League);
            }

            var pod = Ladder.Pod;
            RedrawPodium(s, pod);
            RedrawList(s, pod);
            UiFonts.Apply(podium);
            UiFonts.Apply(list);
        }

        private void RedrawReward()
        {
            if (payLabel == null) return;

            if (RankedRewards.HasPending)
            {
                payLabel.text = Leagues.ShortName(RankedRewards.PendingLeague).ToUpperInvariant() +
                                " WEEK  ·  " + RankedRewards.PendingCoins.ToString("N0");
                payLabel.style.color = ArcadeTheme.Reward;
                payBox.OverrideEdge(ArcadeTheme.Reward);
                UiKit.Show(payClaim, true);
                return;
            }

            // Nothing to collect, so the row does the other half of its job: telling the player what
            // holding this position through to the lock is worth. A projection, not a promise — the
            // pod is still moving, and so is this number.
            int projected = RankedRewards.ProjectedCoins();
            payLabel.text = projected > 0
                ? "THIS WEEK PAYS  " + projected.ToString("N0")
                : "PLAY TO EARN COINS";
            payLabel.style.color = ArcadeTheme.InkMuted;
            payBox.OverrideEdge(ArcadeTheme.LineLit);
            UiKit.Show(payClaim, false);
        }

        // ---------- podium ----------

        private void RedrawPodium(LadderStanding s, IReadOnlyList<LadderEntry> pod)
        {
            podium.Clear();
            bool goesUp = Leagues.CanPromote(s.League);

            // Second, first, third: the winner stands in the middle and tallest.
            int[] order = { 2, 1, 3 };
            foreach (int rank in order)
            {
                LadderEntry entry = rank <= pod.Count ? pod[rank - 1] : default;
                bool present = rank <= pod.Count;
                Color metal = rank == 1 ? ArcadeTheme.Reward : rank == 2 ? Silver : Bronze;

                var col = UiKit.El("rk__step-col", podium);
                if (rank == 1) col.AddToClassList("rk__step-col--first");

                if (present)
                {
                    var hex = new UiHex { Edge = entry.IsYou ? ArcadeTheme.Pulse : metal };
                    hex.AddToClassList("rk__pod-hex");
                    if (rank == 1) hex.AddToClassList("rk__pod-hex--first");
                    string initial = string.IsNullOrEmpty(entry.Name) ? "?" : entry.Name.Substring(0, 1).ToUpperInvariant();
                    var letter = UiKit.Text(initial, "rk__pod-initial f-display", hex);
                    letter.style.color = entry.IsYou ? ArcadeTheme.Pulse : metal;
                    col.Add(hex);

                    var name = new UiShineLabel();
                    name.AddToClassList("rk__pod-name");
                    name.AddToClassList("f-display-semi");
                    col.Add(name);
                    name.Set(entry.IsYou ? "YOU" : entry.Name, entry.IsYou);
                    // Your own step keeps the cyan unless the gold name style is worn, which paints itself.
                    if (!entry.IsYou) name.style.color = ArcadeTheme.Ink;
                    else if (!Inventory.IsEquipped(LevelPath.GoldNameId)) name.style.color = ArcadeTheme.Pulse;
                    UiKit.Text(entry.Points + " PTS", "rk__pod-pts f-display", col);
                }

                var step = UiKit.Frame("rk__step", col);
                step.style.height = StepHeights[rank - 1];
                step.OverrideEdge(present && entry.IsYou ? ArcadeTheme.Pulse : metal);
                step.OverrideFill(Color.Lerp(ArcadeTheme.BgPanel, metal, rank == 1 ? 0.18f : 0.10f));

                var number = UiKit.Text(rank.ToString(), "rk__step-rank f-display", step);
                number.style.color = metal;

                if (goesUp)
                {
                    var up = new UiIcon(UiIcon.Glyph.ChevronRight, ArcadeTheme.Pulse, 2.8f);
                    up.AddToClassList("rk__up");
                    step.Add(up);
                }

                if (present && entry.IsYou)
                {
                    var tag = UiKit.Text("YOU", "rk__you-tag f-display", step);
                    tag.AddToClassList("rk__you-tag--pod");
                }
            }
        }

        // ---------- the rest of the pod ----------

        private void RedrawList(LadderStanding s, IReadOnlyList<LadderEntry> pod)
        {
            list.Clear();

            int firstOutsidePodium = Leagues.PromoteCount;
            for (int i = firstOutsidePodium; i < pod.Count; i++)
            {
                int rank = i + 1;
                LadderEntry entry = pod[i];
                bool releg = rank > s.PodSize - Leagues.RelegateCount && Leagues.CanRelegate(s.League);

                var row = UiKit.Frame("rk__row", list.contentContainer);
                if (entry.IsYou)
                {
                    row.AddToClassList("rk__row--you");
                    var glow = UiKit.El("rk__row-glow", row);
                    glow.pickingMode = PickingMode.Ignore;
                    glow.SendToBack();
                    row.OverrideEdge(ArcadeTheme.Pulse);
                    row.OverrideFill(new Color32(0x0B, 0x2A, 0x3C, 0xFF));
                }
                else if (releg)
                {
                    row.AddToClassList("rk__row--down");
                    row.OverrideEdge(new Color(ArcadeTheme.Danger.r, ArcadeTheme.Danger.g, ArcadeTheme.Danger.b, 0.55f));
                    row.OverrideFill(new Color32(0x1A, 0x0B, 0x11, 0xFF));
                }

                UiKit.Text(rank.ToString(), "rk__row-rank f-display", row);

                var words = UiKit.El("rk__row-words", row);
                var rowName = new UiShineLabel();
                rowName.AddToClassList("rk__row-name");
                rowName.AddToClassList("f-display-semi");
                words.Add(rowName);
                rowName.Set(entry.IsYou ? entry.Name + "  (YOU)" : entry.Name, entry.IsYou);
                if (entry.IsYou && pod.Count >= Leagues.PromoteCount)
                {
                    int gap = pod[Leagues.PromoteCount - 1].Points - entry.Points;
                    string chase = gap <= 0 ? "TIED FOR TOP 3" : gap + " PTS TO TOP 3";
                    UiKit.Text(chase, "rk__row-gap f-display", words);
                }

                if (releg)
                {
                    var down = new UiIcon(UiIcon.Glyph.ChevronRight, ArcadeTheme.Danger, 2.8f);
                    down.AddToClassList("rk__down");
                    row.Add(down);
                }

                UiKit.Text(entry.Points.ToString(), "rk__row-pts f-display", row);
            }
        }

        private static string FormatCountdown(long seconds)
        {
            if (seconds <= 0) return "0M";
            long d = seconds / 86400;
            long h = (seconds % 86400) / 3600;
            long m = (seconds % 3600) / 60;
            if (d > 0) return d + "D " + h + "H";
            if (h > 0) return h + "H " + m + "M";
            return m + "M";
        }
    }
}
