using System;
using TableFootball.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The ranked screen: which league you are in, your pod of fifteen, and where the promotion (top
    /// three) and relegation (bottom two) lines fall — plus how long is left before the weekly lock.
    ///
    /// Like every other screen it is built in code, reads its data from <see cref="Ladder"/> and
    /// redraws on <see cref="Ladder.OnChanged"/>, and talks out only through a callback. It holds no
    /// ladder logic of its own.
    ///
    /// The three DEBUG buttons (editor-only, see <see cref="BuildDebug"/>) stand in for real ranked
    /// results until the online ranked queue is exercised in practice: they let a single developer
    /// move points and trigger the weekly rollover to see the whole system behave without two
    /// devices. They are the one temporary thing here.
    /// </summary>
    public class LeagueMenu : MonoBehaviour
    {
        private GameObject root;
        private CanvasGroup group;

        private TextMeshProUGUI leagueLabel;
        private TextMeshProUGUI statusLabel;
        private Image headerBackground;
        private RectTransform listContent;

        private TextMeshProUGUI rewardLabel;
        private Image rewardBackground;
        private GameObject rewardClaimHolder;

        /// <summary>Raised when the player backs out.</summary>
        public Action OnBack;

        /// <summary>Raised when the player starts a ranked match from here.</summary>
        public Action OnPlayRanked;

        public bool IsOpen => root != null && root.activeSelf;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "LeagueMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();
            UIFactory.ScrimDim(root.transform);

            var title = UIFactory.Text(root.transform, "RANKED", ArcadeTheme.FsTitle, ArcadeTheme.Ink,
                                       display: true, bold: true, upper: true, tracking: 8f);
            var trt = UIFactory.Rt(title.gameObject);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(0f, -150f);
            trt.offsetMax = new Vector2(0f, -60f);

            var panel = UIFactory.Panel(root.transform, "LeaguePanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(660f, 680f);
            prt.anchoredPosition = new Vector2(0f, -20f);

            // The panel's own children are its background (shadow/border/fill); the laid-out content
            // goes in a separate stretched column on top, so a layout group never touches the frame.
            var content = UIFactory.Child(panel.transform, "Content");
            UIFactory.Stretch(UIFactory.Rt(content), 0f);

            var col = content.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(24, 24, 24, 24);
            col.spacing = ArcadeTheme.Md;
            col.childAlignment = TextAnchor.UpperCenter;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            col.childControlWidth = true;
            col.childControlHeight = true;

            BuildHeader(content.transform);
            BuildReward(content.transform);
            BuildLegend(content.transform);
            BuildList(content.transform);
            BuildPlay(content.transform);
            BuildDebug(content.transform);
            BuildBack(content.transform);

            root.SetActive(false);
        }

        private void BuildHeader(Transform parent)
        {
            var header = UIFactory.Child(parent, "Header");
            // Was 78 with no background at all — the league/points line and the status line were bare
            // text floating on the scrim, reading as two disconnected labels rather than one header.
            // A real panel behind them, with real padding, is what "unified header panel" means.
            header.AddComponent<LayoutElement>().preferredHeight = 100f;
            headerBackground = UIFactory.RoundedImage(header, ArcadeTheme.RadSm, ArcadeTheme.BgRaised, false);

            var v = header.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 16, 14, 14);
            v.spacing = ArcadeTheme.Sm;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            leagueLabel = UIFactory.Text(header.transform, "RANKED", 34f, ArcadeTheme.Gold,
                                         display: true, bold: true, upper: true, tracking: 6f);
            leagueLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;

            statusLabel = UIFactory.Text(header.transform, "loading…", ArcadeTheme.FsCaption,
                                         ArcadeTheme.InkMuted, display: false, bold: true, upper: true,
                                         tracking: 3f);
            statusLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        }

        /// <summary>
        /// The money line: what this week has already paid, or what it is on course to pay.
        ///
        /// One row that is always saying something, rather than a claim banner that appears out of
        /// nowhere once a week. Ranked is the game's main source of coins and, before this row, the
        /// screen never mentioned coins at all — a player could climb three leagues without learning
        /// that the ladder is what funds the store.
        ///
        /// The payout is <see cref="RankedRewards"/>'s to compute; this only draws it.
        /// </summary>
        private void BuildReward(Transform parent)
        {
            var strip = UIFactory.Child(parent, "WeeklyReward");
            strip.AddComponent<LayoutElement>().preferredHeight = 56f;
            rewardBackground = UIFactory.RoundedImage(strip, ArcadeTheme.RadSm,
                                                      ArcadeTheme.Coin.WithAlpha(0.10f), false);

            var h = strip.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(14, 10, 0, 0);
            h.spacing = ArcadeTheme.Sm;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var coinCell = UIFactory.Child(strip.transform, "Coin");
            var cle = coinCell.AddComponent<LayoutElement>();
            cle.preferredWidth = 30f;
            cle.minWidth = 30f;
            cle.preferredHeight = 30f;
            UIFactory.CoinGlyph(coinCell.transform, 30f);

            rewardLabel = UIFactory.Text(strip.transform, string.Empty, ArcadeTheme.FsCaption,
                                         ArcadeTheme.Coin, display: false, bold: true, upper: true,
                                         tracking: 2f, align: TextAlignmentOptions.Left,
                                         richText: false);
            rewardLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // The strip is a fixed-width row inside a 660-wide panel and the Collect button takes 168
            // of it, so the message has about thirty characters when there is something to claim. It
            // is written to fit; the ellipsis is the guard for when it one day is not.
            rewardLabel.overflowMode = TextOverflowModes.Ellipsis;

            rewardClaimHolder = UIFactory.Child(strip.transform, "ClaimHolder");
            var hle = rewardClaimHolder.AddComponent<LayoutElement>();
            hle.preferredWidth = 168f;
            hle.minWidth = 168f;

            var v = rewardClaimHolder.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;

            UIFactory.Button(rewardClaimHolder.transform, "Collect", MenuButton.Variant.Primary,
                             ClaimWeekly, 42f);
        }

        /// <summary>
        /// Collects the weekly payout and says so on the strip itself. The coins land in
        /// <see cref="Wallet"/>, which every coin pill in the game is already listening to, so the
        /// number moves everywhere at once without this screen telling any of them.
        /// </summary>
        private void ClaimWeekly()
        {
            int coins = RankedRewards.Claim();
            RedrawReward();

            if (coins > 0 && rewardLabel != null)
            {
                rewardLabel.text = $"+{coins} COINS COLLECTED";
            }
        }

        private void RedrawReward()
        {
            if (rewardLabel == null)
            {
                return;
            }

            if (RankedRewards.HasPending)
            {
                League league = RankedRewards.PendingLeague;
                rewardLabel.text = $"{Leagues.Name(league).ToUpperInvariant()} WEEK REWARD  ·  " +
                                   $"{RankedRewards.PendingCoins}";
                rewardLabel.color = ArcadeTheme.Coin;
                rewardBackground.color = ArcadeTheme.Coin.WithAlpha(0.18f);
                rewardClaimHolder.SetActive(true);
                return;
            }

            // Nothing to collect, so the row does the other half of its job: telling the player what
            // holding this position through to the lock is worth. A projection, not a promise — the
            // pod is still moving, and so is this number.
            int projected = RankedRewards.ProjectedCoins();
            rewardLabel.text = projected > 0
                ? $"FINISH HERE AND THIS WEEK PAYS {projected} COINS"
                : "PLAY RANKED TO EARN COINS EACH WEEK";
            rewardLabel.color = ArcadeTheme.InkMuted;
            rewardBackground.color = ArcadeTheme.BgRaised;
            rewardClaimHolder.SetActive(false);
        }

        private static void BuildLegend(Transform parent)
        {
            var row = UIFactory.Child(parent, "Legend");
            row.AddComponent<LayoutElement>().preferredHeight = 22f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Lg;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childControlWidth = true;
            h.childControlHeight = true;

            Tag(row.transform, "▲ TOP 3 PROMOTE", ArcadeTheme.Pitch);
            Tag(row.transform, "▼ BOTTOM 2 RELEGATE", ArcadeTheme.Red);
        }

        private static void Tag(Transform parent, string text, Color color)
        {
            var t = UIFactory.Text(parent, text, ArcadeTheme.FsCaption, color,
                                   display: false, bold: true, upper: true, tracking: 2f);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = 260f;
        }

        private void BuildList(Transform parent)
        {
            var holder = UIFactory.Child(parent, "ListHolder");
            var le = holder.AddComponent<LayoutElement>();
            le.preferredHeight = 420f;
            le.flexibleHeight = 1f;
            // The one element allowed to give up space when the panel is too short for everything
            // below it — never below a scrollable minimum — so the fixed-height rows beneath it
            // (Play/Debug/Back) always get the room they asked for instead of drifting into overlap.
            le.minHeight = 140f;

            listContent = UIFactory.ScrollList(holder.transform, ArcadeTheme.Xs);
        }

        private void BuildPlay(Transform parent)
        {
            var holder = UIFactory.Child(parent, "PlayRow");
            holder.AddComponent<LayoutElement>().preferredHeight = 62f;

            var v = holder.AddComponent<VerticalLayoutGroup>();
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = true;
            v.childControlWidth = true;
            v.childControlHeight = true;

            UIFactory.Button(holder.transform, "Play Ranked", MenuButton.Variant.Primary,
                             () => OnPlayRanked?.Invoke());
        }

        /// <summary>
        /// Editor-only, and deliberately not just hidden: <c>#if UNITY_EDITOR</c> means a player build
        /// never compiles this method's body in at all, which is what "not visible in the public
        /// build" actually requires — a runtime visibility flag would still ship the row (and its
        /// direct calls into <see cref="Ladder.SubmitResult"/>/<see cref="Ladder.AdvanceWeekForTesting"/>)
        /// inside the shipped assembly, findable and callable by anyone who went looking.
        /// </summary>
        private void BuildDebug(Transform parent)
        {
#if UNITY_EDITOR
            var row = UIFactory.Child(parent, "DebugRow");
            row.AddComponent<LayoutElement>().preferredHeight = 56f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Sm;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            UIFactory.Button(row.transform, "DEBUG Win", MenuButton.Variant.Neutral,
                             () => Ladder.SubmitResult(0, true));
            UIFactory.Button(row.transform, "DEBUG Loss", MenuButton.Variant.Neutral,
                             () => Ladder.SubmitResult(0, false));
            UIFactory.Button(row.transform, "DEBUG +Week", MenuButton.Variant.Ghost,
                             Ladder.AdvanceWeekForTesting);
#endif
        }

        /// <summary>
        /// Built INSIDE the panel's own column, immediately after Play/Debug, rather than pinned to a
        /// fixed offset from the whole screen's bottom edge. A screen-anchored Back sat at a position
        /// computed independently of the panel's actual content — correct on the aspect ratio it was
        /// tuned against, but free to drift onto Play Ranked on a very different one (a phone's aspect
        /// under this canvas's width/height-blended scaler is nothing like a desktop's). Inside the
        /// same VerticalLayoutGroup, Back can never land on its neighbours: the layout group resizes
        /// the scrollable list above it to make room instead.
        /// </summary>
        private void BuildBack(Transform parent)
        {
            var holder = UIFactory.Child(parent, "BackRow");
            holder.AddComponent<LayoutElement>().preferredHeight = 62f;

            var layout = holder.AddComponent<VerticalLayoutGroup>();
            // Fixed-width and centred, not stretched to the panel's full width — the same fix as
            // StoreMenu's identical Back button, and the same reason: full-bleed made it read as a
            // dim background bar rather than a control with its own focus.
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var back = UIFactory.Button(holder.transform, "Back", MenuButton.Variant.Ghost, Back);
            back.gameObject.GetComponent<LayoutElement>().preferredWidth = 260f;
        }

        public void Open()
        {
            if (root == null) return;

            Ladder.OnChanged -= Redraw;
            Ladder.OnChanged += Redraw;
            // Its own event as well as the ladder's: a payout is banked by RankedRewards.Sync when the
            // week turns over, which happens in response to the ladder refresh below rather than
            // before it — so the reward strip has to be told separately or it draws the week that has
            // just ended as if it were still running.
            RankedRewards.OnChanged -= Redraw;
            RankedRewards.OnChanged += Redraw;

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;

            Ladder.Refresh(); // fills the cache, then fires Redraw through OnChanged
            Redraw();          // draw whatever is already cached, immediately
        }

        public void Close()
        {
            Ladder.OnChanged -= Redraw;
            RankedRewards.OnChanged -= Redraw;

            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            Ladder.OnChanged -= Redraw;
            RankedRewards.OnChanged -= Redraw;
        }

        private void Back()
        {
            Close();
            OnBack?.Invoke();
        }

        private void Redraw()
        {
            if (leagueLabel == null) return;

            RedrawReward();

            LadderStanding s = Ladder.Standing;

            if (!s.Valid)
            {
                leagueLabel.text = "RANKED";
                leagueLabel.color = ArcadeTheme.Gold;
                statusLabel.text = "loading…";
                UIFactory.ClearChildren(listContent);
                return;
            }

            Color tier = LeagueColor(s.League);
            leagueLabel.text = $"{Leagues.Name(s.League).ToUpperInvariant()}   ·   {s.WeeklyPoints} PTS";
            leagueLabel.color = tier;
            // Tinted with the same tier colour the text itself uses, matching the reward strip's own
            // coin-tinted background below it — the header now visibly belongs to the league it names.
            if (headerBackground != null) headerBackground.color = Color.Lerp(ArcadeTheme.BgRaised, tier, 0.14f);

            string zone = s.InPromotionZone ? "PROMOTION ZONE"
                        : s.InRelegationZone ? "RELEGATION ZONE"
                        : "HOLDING";
            statusLabel.text = $"{Ordinal(s.RankInPod)} OF {s.PodSize}   ·   {zone}   ·   " +
                               $"RESETS IN {FormatCountdown(s.SecondsToRollover)}";

            UIFactory.ClearChildren(listContent);

            var pod = Ladder.Pod;
            for (int i = 0; i < pod.Count; i++)
            {
                int rank = i + 1;
                bool promo = rank <= Leagues.PromoteCount && Leagues.CanPromote(s.League);
                bool releg = rank > s.PodSize - Leagues.RelegateCount && Leagues.CanRelegate(s.League);
                AddRow(i, rank, pod[i], promo, releg, s.PodSize);
            }
        }

        /// <summary>
        /// A zone tint that TAPERS toward the boundary rather than cutting off flat — 1st place in the
        /// promotion zone reads strongest, the last promoting place reads weakest, so the row right
        /// below the cutoff is a small step down rather than a hard edge from full green to none.
        /// Softer and more legible than a flat colour block, and it costs nothing extra to compute:
        /// each row already knows its own rank.
        /// </summary>
        private void AddRow(int index, int rank, LadderEntry entry, bool promo, bool releg, int podSize)
        {
            Color bg;
            if (entry.IsYou)
            {
                bg = ArcadeTheme.Gold.WithAlpha(0.16f);
            }
            else if (promo)
            {
                float t = Leagues.PromoteCount > 1 ? (float)(rank - 1) / (Leagues.PromoteCount - 1) : 0f;
                bg = ArcadeTheme.Pitch.WithAlpha(Mathf.Lerp(0.22f, 0.09f, t));
            }
            else if (releg)
            {
                int zoneTop = podSize - Leagues.RelegateCount + 1;
                float t = Leagues.RelegateCount > 1 ? (float)(rank - zoneTop) / (Leagues.RelegateCount - 1) : 1f;
                bg = ArcadeTheme.Red.WithAlpha(Mathf.Lerp(0.08f, 0.20f, t));
            }
            else
            {
                // A subtle zebra stripe for the rows with no zone of their own — the flat single
                // colour every non-zone row shared read as "no pattern" rather than as consistent
                // banding; alternating two very close shades gives the list a rhythm to scan by.
                bg = index % 2 == 0 ? ArcadeTheme.BgRaised : Color.Lerp(ArcadeTheme.BgRaised, ArcadeTheme.BgPanel, 0.6f);
            }

            Color fg = entry.IsYou ? ArcadeTheme.Gold : ArcadeTheme.Ink;

            var row = UIFactory.Child(listContent, "Row");
            // Raycastable: with nothing else in the scroll view to hit, a non-raycast row leaves no
            // surface for a drag or mouse-wheel event to land on, and the list cannot be scrolled at
            // all — not merely scrolled poorly. The row itself never handles the event; it only needs
            // to be hit so Unity's event system can bubble the drag up to the ScrollRect above it.
            UIFactory.RoundedImage(row, ArcadeTheme.RadSm, bg, true);
            row.AddComponent<LayoutElement>().preferredHeight = 44f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 0, 0);
            h.spacing = ArcadeTheme.Md;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var rankT = UIFactory.Text(row.transform, rank.ToString(), ArcadeTheme.FsBody, fg,
                                       display: false, bold: true, upper: false, tracking: 0f,
                                       align: TextAlignmentOptions.Left);
            rankT.gameObject.AddComponent<LayoutElement>().preferredWidth = 44f;

            var nameT = UIFactory.Text(row.transform, entry.Name, ArcadeTheme.FsBody, fg,
                                       display: false, bold: entry.IsYou, upper: false, tracking: 0f,
                                       align: TextAlignmentOptions.Left);
            nameT.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var ptsT = UIFactory.Text(row.transform, entry.Points.ToString(), ArcadeTheme.FsBody, fg,
                                      display: false, bold: true, upper: false, tracking: 0f,
                                      align: TextAlignmentOptions.Right);
            ptsT.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;
        }

        /// <summary>
        /// Delegated rather than switched on here. The badge glyph, the store's badge tab and this
        /// screen all have to agree on what Silver looks like, and three copies of the same switch is
        /// three places for one of them to be edited alone.
        /// </summary>
        private static Color LeagueColor(League league) => UIFactory.LeagueColor(league);

        private static string Ordinal(int n)
        {
            int mod100 = n % 100;
            if (mod100 >= 11 && mod100 <= 13) return n + "TH";
            switch (n % 10)
            {
                case 1: return n + "ST";
                case 2: return n + "ND";
                case 3: return n + "RD";
                default: return n + "TH";
            }
        }

        private static string FormatCountdown(long seconds)
        {
            if (seconds <= 0) return "0M";
            long d = seconds / 86400;
            long h = (seconds % 86400) / 3600;
            long m = (seconds % 3600) / 60;
            if (d > 0) return $"{d}D {h}H";
            if (h > 0) return $"{h}H {m}M";
            return $"{m}M";
        }
    }
}
