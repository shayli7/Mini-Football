using System;
using System.Collections;
using TableFootball.Net;
using TableFootball.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The center score HUD: RED | VS | BLUE with big display digits, team-colored underbars and glow.
    /// Purely a listener — it subscribes to <see cref="MatchManager"/>'s events and never touches the
    /// match itself, exactly as MatchManager's own comments intend ("the Step 6 UI can subscribe
    /// without this class knowing anything about menus or canvases").
    ///
    /// It also owns the goal flash and the win banner, since both are score-driven.
    /// </summary>
    public class ScoreHud : MonoBehaviour
    {
        private MatchManager match;
        private TextMeshProUGUI redNum, blueNum, bannerText, bannerReason;
        private int lastRed, lastBlue;

        private CanvasGroup flashCg;
        private CanvasGroup goalPopupCg;
        private TextMeshProUGUI goalPopupText;
        private Coroutine goalPopupRoutine;
        private Image flashImg;
        private GameObject banner;
        private MenuButton playAgainButton;
        private GameObject hudRoot;
        private GameObject clockRoot;
        private GameObject suddenDeathRoot;
        private TextMeshProUGUI clockText;
        private TextMeshProUGUI bannerRed, bannerBlue;

        private Coroutine redPop, bluePop, flashRoutine, clockPop, bannerRoutine;
        private QuestLedger ledger;
        private RectTransform bannerPanel;
        private int lastTick = -1;
        private UIBurst burst;

        // The post-match progression block on the banner (XP earned, level pill, XP bar). Populated by
        // GameFlow via ShowMatchProgress; left hidden for local PvP, where there is no single player.
        private GameObject progressBlock;
        private TextMeshProUGUI progressEarnedText, progressLabel, progressLevelText;
        private UIFillBar progressFill;
        private RectTransform bannerPanelRt;
        private Coroutine progressRoutine;

        // The ranked-points flourish: a trophy popping in beside "+N". Populated by GameFlow via
        // ShowRankedProgress — only for a WON ranked match, never for casual or local play, where
        // there is nothing ranked to have gained.
        private GameObject rankedBlock;
        private TextMeshProUGUI rankedPointsText;
        private Coroutine rankedRoutine;

        // The banner grows to fit whichever of the quest ledger, progression and ranked blocks are
        // shown, and sits at the base height for a result with none of them — a local-PvP win, or an
        // online/vs-AI win that cleared no quest. bannerPanel and bannerPanelRt name the SAME
        // RectTransform (see BuildBanner) — two fields because two features grew this panel
        // independently; kept both rather than rewiring every call site that already reads one.
        private const float BannerBaseHeight = 430f;
        private const float BannerTallHeight = 520f;
        // Tall enough for BOTH the ranked row and the XP block beneath it — a ranked win always shows
        // its XP progress too (progression is credited for every vs-AI/online match, not only casual
        // ones), so the two blocks stack on the same banner rather than one replacing the other.
        private const float BannerRankedHeight = BannerTallHeight + 56f;

        /// <summary>Set by the bootstrap so the HUD's pause button can open the pause menu.</summary>
        public Action OnOpenMenu;

        /// <summary>Set by GameFlow so a finished match can leave for the main menu.</summary>
        public Action OnReturnToMenu;

        /// <summary>
        /// Raised instead of restarting when the match was online, because a rematch is an agreement
        /// between two players rather than a decision either can take alone.
        /// </summary>
        public Action OnRematchRequested;

        /// <summary>Set by GameFlow at kick-off. Decides which of the two Play Again paths runs.</summary>
        public bool OnlineMatch { get; set; }

        /// <summary>
        /// Which team the person holding this device plays, or null when both players are at it.
        ///
        /// Set by GameFlow at kick-off. It is what lets the result be stated as YOU WIN or YOU LOSE
        /// rather than as a team name: online, and against the AI, there is a side that belongs to
        /// the player and a result they can be congratulated or commiserated on. Two people sharing
        /// one screen have no such side between them, so that match keeps RED WINS / BLUE WINS —
        /// telling both of them YOU WIN would be right for one and wrong for the other.
        /// </summary>
        public Team? LocalTeam { get; set; }

        /// <summary>
        /// Set while this player is on their way out, so no result is put on their screen.
        ///
        /// Somebody who walks out of a match has answered the question the banner exists to ask. The
        /// match still finishes underneath them — their opponent is owed the win, and the loss still
        /// goes on their own record — but being shown a scoreboard on the way to the menu tells them
        /// the outcome of a game they chose to stop watching.
        /// </summary>
        public bool ResultsHidden { get; set; }

        /// <summary>
        /// Hides the whole HUD while the front end is up. The score and pause button belong to a
        /// match in progress, and would otherwise sit on top of the main menu.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (hudRoot != null)
            {
                hudRoot.SetActive(visible);
            }

            // Only one of the clock and the sudden-death banner is ever up, so coming back into view
            // has to restore whichever the match is actually in — not simply switch the clock on.
            bool sudden = match != null && match.InSuddenDeath;
            if (clockRoot != null) clockRoot.SetActive(visible && !sudden);
            if (suddenDeathRoot != null) suddenDeathRoot.SetActive(visible && sudden);

            if (!visible)
            {
                if (banner != null) banner.SetActive(false);
                HideGoalPopup();
            }
        }

        /// <summary>Stops any running GOAL! popup and clears it. Used when the HUD leaves or a match restarts.</summary>
        private void HideGoalPopup()
        {
            if (goalPopupRoutine != null)
            {
                StopCoroutine(goalPopupRoutine);
                goalPopupRoutine = null;
            }

            if (goalPopupCg != null) goalPopupCg.alpha = 0f;
        }

        public void Build(Transform canvasRoot, MatchManager matchManager)
        {
            match = matchManager;

            BuildFlash(canvasRoot);
            BuildGoalPopup(canvasRoot);
            BuildHud(canvasRoot);
            BuildClock(canvasRoot);
            BuildBanner(canvasRoot);

            if (match != null)
            {
                lastRed = match.RedScore;
                lastBlue = match.BlueScore;
                redNum.text = lastRed.ToString();
                blueNum.text = lastBlue.ToString();

                match.ScoreChanged += OnScoreChanged;
                match.GoalScored += OnGoalScored;
                match.MatchWon += OnMatchWon;
                match.MatchRestarted += OnMatchRestarted;
                match.TimeChanged += OnTimeChanged;
                match.SuddenDeathStarted += OnSuddenDeath;

                OnTimeChanged(match.TimeRemaining);
            }
        }

        private void OnDestroy()
        {
            if (match == null) return;
            match.ScoreChanged -= OnScoreChanged;
            match.GoalScored -= OnGoalScored;
            match.MatchWon -= OnMatchWon;
            match.MatchRestarted -= OnMatchRestarted;
            match.TimeChanged -= OnTimeChanged;
            match.SuddenDeathStarted -= OnSuddenDeath;
        }

        // ---------- build ----------

        private void BuildHud(Transform root)
        {
            var hud = UIFactory.Panel(root, "ScoreHUD");
            hudRoot = hud;
            var rt = UIFactory.Rt(hud);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // Tight to the top edge. The table fills the screen underneath and every pixel the HUD
            // takes is pitch the players cannot see — the safe-area root already keeps it clear of
            // any cutout, so it does not need a margin of its own as well.
            rt.anchoredPosition = new Vector2(0f, -ArcadeTheme.Xs);
            // Shorter, not just higher. It already sits 4px off the top edge, so the only pitch left
            // to give back is the panel's own height — 132 was sized around 88px digits that do not
            // need to be that big to be read across a phone held at arm's length.
            rt.sizeDelta = new Vector2(400f, 100f);

            // horizontal layout inside the fill
            var fill = hud.transform.Find("Fill");
            var row = fill.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 0f;
            row.padding = new RectOffset(6, 6, 6, 6);
            row.childForceExpandHeight = true;

            redNum = BuildTeamColumn(fill, "RED", ArcadeTheme.Red, out _);
            BuildVs(fill);
            blueNum = BuildTeamColumn(fill, "BLUE", ArcadeTheme.Blue, out _);
        }

        private TextMeshProUGUI BuildTeamColumn(Transform parent, string teamName, Color teamColor, out GameObject col)
        {
            col = UIFactory.Child(parent, "Team_" + teamName);
            var le = col.AddComponent<LayoutElement>();
            le.preferredWidth = 180f; le.flexibleWidth = 1f;

            var v = col.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleCenter;
            v.spacing = 2f;
            v.childForceExpandHeight = false;

            // glow behind the digit — opt out of the layout group so it isn't repositioned
            var glow = UIFactory.Child(col.transform, "Glow");
            UIFactory.GlowImage(glow, ArcadeTheme.RadMd, 30f, teamColor.WithAlpha(0.28f));
            glow.AddComponent<LayoutElement>().ignoreLayout = true;
            UIFactory.Stretch(UIFactory.Rt(glow), -6);
            glow.transform.SetAsFirstSibling();

            var label = UIFactory.Text(col.transform, teamName, ArcadeTheme.FsTeam, teamColor,
                                       display: false, bold: true, upper: true, tracking: 22f);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            // Scaled down from FsScore rather than using it: the result banner still wants the full
            // 88 for a number the player is meant to stop and look at, while this one is read at a
            // glance mid-rally and only has to be unmistakable.
            var num = UIFactory.Text(col.transform, "0", ArcadeTheme.FsScore * 0.62f, Color.white,
                                     display: true, bold: true);
            num.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;

            // underbar
            var bar = UIFactory.Child(col.transform, "Bar");
            UIFactory.RoundedImage(bar, ArcadeTheme.RadSm, teamColor, false);
            bar.AddComponent<LayoutElement>().preferredHeight = 3f;
            var brt = UIFactory.Rt(bar);
            brt.sizeDelta = new Vector2(110f, 3f);

            return num;
        }

        /// <summary>
        /// The match clock, sitting just under the score. Its own panel rather than a third column
        /// inside the score row, so the red/vs/blue layout is untouched and the clock can be
        /// replaced wholesale by the SUDDEN DEATH notice.
        /// </summary>
        private void BuildClock(Transform root)
        {
            var panel = UIFactory.Panel(root, "MatchClock");
            clockRoot = panel;

            var rt = UIFactory.Rt(panel);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            // Clears the 100-high score panel plus its margin.
            rt.anchoredPosition = new Vector2(0f, -(ArcadeTheme.Xs + 100f + ArcadeTheme.Xs));
            rt.sizeDelta = new Vector2(180f, 46f);

            var fill = panel.transform.Find("Fill");
            clockText = UIFactory.Text(fill, "3:00", ArcadeTheme.FsTitle * 0.6f, ArcadeTheme.Ink,
                                       display: true, bold: true, tracking: 2f);
            UIFactory.Stretch(UIFactory.Rt(clockText.gameObject), 0);

            BuildSuddenDeath(root);
        }

        /// <summary>
        /// The sudden-death notice: its own wide banner, taking the clock's place.
        ///
        /// It used to be written into the clock as "SUDDEN\nDEATH" at 19px — two cramped lines in a
        /// 200x54 pill built for four digits. It is the most dramatic moment the match has, and it
        /// was the smallest text on screen. Replacing the clock rather than sitting beside it is
        /// right: once the timer is gone there is no time left to show.
        /// </summary>
        private void BuildSuddenDeath(Transform root)
        {
            var panel = UIFactory.Panel(root, "SuddenDeath");
            suddenDeathRoot = panel;

            var rt = UIFactory.Rt(panel);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -(ArcadeTheme.Xs + 100f + ArcadeTheme.Xs));
            rt.sizeDelta = new Vector2(320f, 54f);

            // Red border rather than the neutral Line, so the panel itself carries the alarm and the
            // text does not have to do it alone.
            var border = panel.transform.Find("Border");
            if (border != null) border.GetComponent<Image>().color = ArcadeTheme.Red;

            var fill = panel.transform.Find("Fill");
            var t = UIFactory.Text(fill, "SUDDEN DEATH", ArcadeTheme.FsButton, ArcadeTheme.Red,
                                   display: true, bold: true, upper: true, tracking: 10f);
            UIFactory.Stretch(UIFactory.Rt(t.gameObject), 0);

            panel.SetActive(false);
        }

        /// <summary>
        /// The divider between the two score blocks: a hairline broken by the word itself.
        ///
        /// A bare "VS" floating between two 88px digits reads as a gap the layout forgot to fill.
        /// Drawing the rule makes it a deliberate separator, which is what it always was.
        /// </summary>
        private void BuildVs(Transform parent)
        {
            var vs = UIFactory.Child(parent, "VS");
            vs.AddComponent<LayoutElement>().preferredWidth = 56f;

            Tick(vs.transform, 1f);
            Tick(vs.transform, -1f);

            var t = UIFactory.Text(vs.transform, "VS", ArcadeTheme.FsTeam, ArcadeTheme.InkMuted,
                                   display: false, bold: true, upper: true, tracking: 12f);
            UIFactory.Stretch(UIFactory.Rt(t.gameObject), 0);
        }

        /// <summary>One half of the divider rule, above or below the word.</summary>
        private static void Tick(Transform parent, float dir)
        {
            var go = UIFactory.Child(parent, "Tick");
            UIFactory.RoundedImage(go, ArcadeTheme.RadSm, ArcadeTheme.Line, false);
            var rt = UIFactory.Rt(go);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(2f, 26f);
            rt.anchoredPosition = new Vector2(0f, dir * 28f);
        }

        private void BuildFlash(Transform root)
        {
            var go = UIFactory.Child(root, "GoalFlash");
            var img = go.AddComponent<Image>();
            img.color = ArcadeTheme.Red.WithAlpha(1f);
            img.raycastTarget = false;
            flashImg = img;
            UIFactory.Stretch(UIFactory.Rt(go), -ArcadeTheme.Bleed);
            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false;
            flashCg = cg;
            go.transform.SetAsFirstSibling(); // behind the HUD
        }

        /// <summary>
        /// The big "GOAL!" that lands on top of the goal flash.
        ///
        /// Its own object with its own CanvasGroup so it can pop and fade on its own timeline,
        /// independent of the flash behind it. Non-interactive, above the flash, centred on the
        /// screen — clear of the top-centre score, over the empty middle of the table.
        /// </summary>
        private void BuildGoalPopup(Transform root)
        {
            var go = UIFactory.Child(root, "GoalPopup");
            UIFactory.Stretch(UIFactory.Rt(go));
            goalPopupCg = go.AddComponent<CanvasGroup>();
            goalPopupCg.alpha = 0f;
            goalPopupCg.blocksRaycasts = false;
            goalPopupCg.interactable = false;

            goalPopupText = UIFactory.Text(go.transform, "GOAL!", 140f, ArcadeTheme.Gold,
                                           display: true, bold: true, upper: true, tracking: 6f);
            var rt = UIFactory.Rt(goalPopupText.gameObject);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, 220f);
            rt.anchoredPosition = Vector2.zero;

            go.transform.SetAsLastSibling(); // above the flash and the HUD
        }

        /// <summary>
        /// Pops "GOAL!" in the scorer's colour, holds, then fades it out.
        ///
        /// Unscaled, like the flash: a goal is scored at full speed but the celebration that follows
        /// stops play, and the popup must animate to completion either side of that. The scale
        /// overshoots on the way in (EaseOutBack) so it reads as a stamp, then drifts fractionally
        /// larger as it fades so it feels like it is receding rather than simply switching off.
        /// </summary>
        private IEnumerator GoalPopup(Team scorer)
        {
            if (goalPopupCg == null || goalPopupText == null) yield break;

            goalPopupText.color = scorer == Team.Red ? ArcadeTheme.Red : ArcadeTheme.Blue;
            Transform t = goalPopupText.transform;

            const float inDur = 0.22f;
            const float hold = 0.85f;
            const float outDur = 0.3f;

            if (ArcadeTheme.ReducedMotion)
            {
                // No motion, but still announce and clear it, so reduced-motion players get the word
                // "GOAL!" rather than nothing.
                t.localScale = Vector3.one;
                goalPopupCg.alpha = 1f;
                yield return new WaitForSecondsRealtime(hold);
                goalPopupCg.alpha = 0f;
                goalPopupRoutine = null;
                yield break;
            }

            float e = 0f;
            while (e < inDur)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / inDur);
                t.localScale = Vector3.one * Mathf.LerpUnclamped(1.4f, 1f, ArcadeTheme.EaseOutBack(k));
                goalPopupCg.alpha = ArcadeTheme.EaseOut(k);
                yield return null;
            }

            t.localScale = Vector3.one;
            goalPopupCg.alpha = 1f;

            yield return new WaitForSecondsRealtime(hold);

            e = 0f;
            while (e < outDur)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / outDur);
                t.localScale = Vector3.one * Mathf.LerpUnclamped(1f, 1.12f, k);
                goalPopupCg.alpha = 1f - ArcadeTheme.EaseIn(k);
                yield return null;
            }

            goalPopupCg.alpha = 0f;
            t.localScale = Vector3.one;
            goalPopupRoutine = null;
        }

        private void BuildBanner(Transform root)
        {
            banner = UIFactory.Child(root, "WinBanner");
            var dim = banner.AddComponent<Image>();
            dim.color = ArcadeTheme.BgDeep.WithAlpha(0.72f);
            dim.raycastTarget = true;
            UIFactory.Stretch(UIFactory.Rt(banner), -ArcadeTheme.Bleed);

            var panel = UIFactory.Panel(banner.transform, "BannerPanel");
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            // Widened from the original 460 to fit the quest ledger's rows comfortably; the
            // ranked and progress blocks below just stretch to fill whatever width the panel has.
            prt.sizeDelta = new Vector2(520f, BannerBaseHeight);
            bannerPanelRt = prt;
            bannerPanel = prt;

            var fill = panel.transform.Find("Fill");
            var v = fill.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleCenter;
            v.spacing = ArcadeTheme.Lg;
            v.padding = new RectOffset(28, 28, 28, 28);
            v.childForceExpandWidth = true; v.childControlWidth = true; v.childControlHeight = true;

            BuildBannerScore(fill);

            // Placeholder only — OnMatchWon sets both the wording and the colour before this is ever
            // shown. Left as the longest of the three so the layout is measured against the worst case.
            bannerText = UIFactory.Text(fill, "BLUE WINS", ArcadeTheme.FsTitle, ArcadeTheme.Red,
                                        display: true, bold: true, upper: true, tracking: 2f);
            bannerText.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;

            // Empty for an ordinary win, so the banner keeps its current shape. Only a forfeit fills
            // it in — a win that arrives without the ball crossing a line needs saying why.
            bannerReason = UIFactory.Text(fill, string.Empty, ArcadeTheme.FsCaption,
                                          ArcadeTheme.InkMuted, display: false, bold: true,
                                          upper: true, tracking: 8f);
            bannerReason.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            // Above the XP block: for a ranked win, the trophy is the headline reward and the XP a
            // secondary one, so it reads result -> ranked -> progression -> quests -> buttons.
            BuildRankedBlock(fill);

            // Between the result and the quest ledger, so the XP earned reads as a consequence of it.
            BuildProgressBlock(fill);

            // Last of the three, right before the buttons — the quest ledger is today's specific
            // wins, after the match's general progression. Starts hidden and contributes no height
            // until a match actually clears something.
            ledger = gameObject.AddComponent<QuestLedger>();
            ledger.Build(fill);

            playAgainButton = UIFactory.Button(fill, "Play Again", MenuButton.Variant.Primary,
                                               PlayAgain);
            UIFactory.Button(fill, "Main Menu", MenuButton.Variant.Ghost, () => OnReturnToMenu?.Invoke());

            // Above the panel in sibling order so the dots read as thrown over it, not trapped
            // behind. Centred on the banner, which is centred on the screen.
            var burstGo = UIFactory.Child(banner.transform, "Burst");
            var burt = UIFactory.Rt(burstGo);
            burt.anchorMin = burt.anchorMax = new Vector2(0.5f, 0.5f);
            burt.pivot = new Vector2(0.5f, 0.5f);
            burt.sizeDelta = Vector2.zero;
            burt.anchoredPosition = Vector2.zero;
            burst = burstGo.AddComponent<UIBurst>();
            ledger.OnSlam = () => burst.Play(ArcadeTheme.Gold);

            banner.SetActive(false);
        }

        /// <summary>
        /// The final score, in team colours, at the top of the result panel.
        ///
        /// The banner used to say only who won, which left the player looking past it at the HUD
        /// behind the dim to find out by how much. A result screen should be readable on its own.
        /// </summary>
        private void BuildBannerScore(Transform parent)
        {
            var row = UIFactory.Child(parent, "FinalScore");
            row.AddComponent<LayoutElement>().preferredHeight = 96f;

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = ArcadeTheme.Lg;
            h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            h.childControlWidth = true; h.childControlHeight = true;

            bannerRed = Digit(row.transform, ArcadeTheme.Red);

            var dash = UIFactory.Text(row.transform, "–", ArcadeTheme.FsScore * 0.6f,
                                      ArcadeTheme.InkMuted, display: true, bold: true);
            dash.gameObject.AddComponent<LayoutElement>().preferredWidth = 40f;

            bannerBlue = Digit(row.transform, ArcadeTheme.Blue);
        }

        private static TextMeshProUGUI Digit(Transform parent, Color color)
        {
            var t = UIFactory.Text(parent, "0", ArcadeTheme.FsScore, color, display: true, bold: true);
            t.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;
            return t;
        }

        // ---------- events ----------

        private void OnScoreChanged(int red, int blue)
        {
            if (redNum != null) redNum.text = red.ToString();
            if (blueNum != null) blueNum.text = blue.ToString();

            // The scoring digit punches and flashes its own team colour before settling back to
            // white, so a glance at the HUD says who scored as well as what the score now is.
            if (red != lastRed)
            {
                redPop = Restart(redPop, UITween.Pop(redNum.transform, ArcadeTheme.ScorePop, ArcadeTheme.TSlow));
                StartCoroutine(TintBack(redNum, ArcadeTheme.Red));
            }

            if (blue != lastBlue)
            {
                bluePop = Restart(bluePop, UITween.Pop(blueNum.transform, ArcadeTheme.ScorePop, ArcadeTheme.TSlow));
                StartCoroutine(TintBack(blueNum, ArcadeTheme.Blue));
            }

            lastRed = red; lastBlue = blue;
        }

        /// <summary>Flashes a digit to its team colour and fades it back to white.</summary>
        private static IEnumerator TintBack(TextMeshProUGUI label, Color color)
        {
            if (label == null) yield break;
            if (ArcadeTheme.ReducedMotion) { label.color = Color.white; yield break; }

            label.color = color;

            float e = 0f;
            while (e < ArcadeTheme.TFlash)
            {
                e += Time.unscaledDeltaTime;
                label.color = Color.LerpUnclamped(color, Color.white,
                                                  ArcadeTheme.EaseOut(Mathf.Clamp01(e / ArcadeTheme.TFlash)));
                yield return null;
            }

            label.color = Color.white;
        }

        private void OnGoalScored(Team scorer)
        {
            if (flashImg != null)
            {
                flashImg.color = (scorer == Team.Red ? ArcadeTheme.Red : ArcadeTheme.Blue).WithAlpha(1f);
                flashRoutine = Restart(flashRoutine, UITween.Flash(flashCg, 0.35f, ArcadeTheme.TFlash));
            }

            goalPopupRoutine = Restart(goalPopupRoutine, GoalPopup(scorer));
        }

        /// <summary>
        /// Locally a rematch is immediate; online it is a request.
        ///
        /// The waiting state is shown here rather than waiting for a message back, because the player
        /// pressed the button and the screen has to answer that press now — the other machine's reply
        /// is a round trip away and may never come at all.
        /// </summary>
        private void PlayAgain()
        {
            if (!OnlineMatch)
            {
                match?.RestartMatch();
                return;
            }

            ShowRematchWaiting();
            OnRematchRequested?.Invoke();
        }

        /// <summary>Pressed, and now waiting on the other player.</summary>
        public void ShowRematchWaiting()
        {
            if (playAgainButton != null) playAgainButton.interactable = false;
            if (bannerReason != null)
            {
                bannerReason.text = "WAITING FOR YOUR OPPONENT…";
                bannerReason.color = ArcadeTheme.Gold;
            }
        }

        /// <summary>Nobody is coming. Says so on the banner the player is already looking at.</summary>
        public void ShowRematchDeclined(string message)
        {
            if (playAgainButton != null) playAgainButton.gameObject.SetActive(false);
            if (bannerReason != null)
            {
                bannerReason.text = message;
                bannerReason.color = ArcadeTheme.Red;
            }
        }

        /// <summary>
        /// A trophy and "+N", hidden until <see cref="ShowRankedProgress"/> fills it in. The same
        /// TrophyGlyph the main menu's ranked pill draws, so a ranked-points gain is recognisably the
        /// same reward on both screens.
        /// </summary>
        private void BuildRankedBlock(Transform parent)
        {
            rankedBlock = UIFactory.Child(parent, "RankedProgress");
            rankedBlock.AddComponent<LayoutElement>().preferredHeight = 36f;

            var h = rankedBlock.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = ArcadeTheme.Sm;
            h.childForceExpandWidth = false; h.childControlWidth = true; h.childControlHeight = true;

            var cupHolder = UIFactory.Child(rankedBlock.transform, "Cup");
            var cle = cupHolder.AddComponent<LayoutElement>();
            cle.preferredWidth = 30f; cle.minWidth = 30f; cle.preferredHeight = 30f;
            // BgPanel, matching the banner's own fill colour — the trophy sits directly on it, and the
            // handle holes are punched through in whatever colour it is resting on.
            UIFactory.TrophyGlyph(cupHolder.transform, ArcadeTheme.Gold, ArcadeTheme.BgPanel, 1f);

            rankedPointsText = UIFactory.Text(rankedBlock.transform, "+0", ArcadeTheme.FsBody,
                                              ArcadeTheme.Gold, display: true, bold: true, upper: true,
                                              tracking: 2f);
            rankedPointsText.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;

            rankedBlock.SetActive(false);
        }

        /// <summary>
        /// The XP-earned line, level pill and XP bar, hidden until <see cref="ShowMatchProgress"/> fills
        /// it in. Reuses the same widgets the profile chip does, so a level reads the same everywhere.
        /// </summary>
        private void BuildProgressBlock(Transform parent)
        {
            progressBlock = UIFactory.Child(parent, "MatchProgress");
            progressBlock.AddComponent<LayoutElement>().preferredHeight = 66f;

            var col = progressBlock.AddComponent<VerticalLayoutGroup>();
            col.childAlignment = TextAnchor.MiddleCenter;
            col.spacing = ArcadeTheme.Xs;
            col.childForceExpandWidth = true; col.childControlWidth = true; col.childControlHeight = true;

            // "+120 XP" on the left, the level pill on the right.
            var row = UIFactory.Child(progressBlock.transform, "Row");
            row.AddComponent<LayoutElement>().preferredHeight = 28f;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleCenter;
            h.spacing = ArcadeTheme.Sm;
            h.childForceExpandWidth = false; h.childControlWidth = true; h.childControlHeight = true;

            progressEarnedText = UIFactory.Text(row.transform, "+0 XP", ArcadeTheme.FsBody, ArcadeTheme.Gold,
                                                display: true, bold: true, upper: true, tracking: 2f,
                                                align: TextAlignmentOptions.Left);
            progressEarnedText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var pill = UIFactory.Child(row.transform, "LevelPill");
            var ple = pill.AddComponent<LayoutElement>();
            ple.preferredWidth = 84f; ple.minWidth = 84f; ple.preferredHeight = 26f;
            UIFactory.RoundedImage(pill, ArcadeTheme.RadSm, ArcadeTheme.Gold.WithAlpha(0.16f), false);
            progressLevelText = UIFactory.Text(pill.transform, "LV —", ArcadeTheme.FsCaption * 0.92f,
                                               ArcadeTheme.Gold, display: true, bold: true, upper: true,
                                               tracking: 3f);
            UIFactory.Stretch(UIFactory.Rt(progressLevelText.gameObject), 0);

            // 26, matching ProfileChip's own bar exactly: XpBar sizes its fill radius AND its label
            // text off this height, so the two numbers travel together. At the old 16 the label
            // shrank to ~10px (height * 0.62) — a different, smaller design from the chip's bar
            // rather than the same bar shown twice.
            var xp = UIFactory.XpBar(progressBlock.transform, 0f, "0 / 0 XP", 26f);
            progressFill = xp.GetComponentInChildren<UIFillBar>();
            progressLabel = xp.GetComponentInChildren<TextMeshProUGUI>();

            progressBlock.SetActive(false);
        }

        /// <summary>
        /// Shows the post-match progression on the banner: XP earned, the level, and the XP bar sweeping
        /// from the old fill to the new — with a level-up flourish when the match crossed a threshold.
        /// Called by GameFlow after it credits the result; never for local PvP, so that banner stays
        /// clean. Assumes the banner is already up (GameFlow calls this right after the result fires).
        /// </summary>
        /// <summary>
        /// The ranked-points flourish: a trophy popping in beside "+N". Called by GameFlow only for a
        /// WON ranked match — a casual or local result never calls this, so the trophy never appears
        /// for a match that was not ranked, and never for a loss. Independent of
        /// <see cref="ShowMatchProgress"/>'s XP sequence: GameFlow calls both, back to back, for a
        /// ranked win, and neither waits on the other to start.
        /// </summary>
        public void ShowRankedProgress(int pointsGained)
        {
            if (rankedBlock == null || banner == null || !banner.activeSelf) return;

            if (bannerPanelRt != null)
            {
                float target = Mathf.Max(bannerPanelRt.sizeDelta.y, BannerRankedHeight);
                bannerPanelRt.sizeDelta = new Vector2(bannerPanelRt.sizeDelta.x, target);
            }

            rankedPointsText.text = $"+{pointsGained}";
            rankedBlock.transform.localScale = Vector3.one;
            rankedBlock.SetActive(true);

            if (rankedRoutine != null) StopCoroutine(rankedRoutine);
            rankedRoutine = StartCoroutine(RankedSequence());
        }

        /// <summary>Lets the score and result land first, then pops the trophy in with the same burst
        /// flourish a level-up gets — a ranked win is exactly that kind of moment.</summary>
        private IEnumerator RankedSequence()
        {
            yield return new WaitForSecondsRealtime(ArcadeTheme.TNormal);

            if (rankedBlock != null)
            {
                yield return UITween.PopIn(rankedBlock.transform, ArcadeTheme.TSlow);
            }

            if (burst != null) burst.Play(ArcadeTheme.Gold);
            GameSfx.PlayUiClick();

            rankedRoutine = null;
        }

        public void ShowMatchProgress(PlayerProgress.MatchOutcome d)
        {
            if (progressBlock == null || banner == null || !banner.activeSelf) return;

            if (bannerPanelRt != null)
            {
                // Max, not a flat assignment: a ranked win calls ShowRankedProgress first, which may
                // already have grown the banner to BannerRankedHeight to fit the trophy row too. This
                // must not shrink that back down — it only ever needs to guarantee AT LEAST enough
                // room for the XP block.
                float target = Mathf.Max(bannerPanelRt.sizeDelta.y, BannerTallHeight);
                bannerPanelRt.sizeDelta = new Vector2(bannerPanelRt.sizeDelta.x, target);
            }

            progressEarnedText.transform.localScale = Vector3.one;
            progressEarnedText.color = ArcadeTheme.Gold;
            progressEarnedText.text = $"+{d.XpEarned} XP";
            progressLevelText.transform.localScale = Vector3.one;
            progressLevelText.text = $"LV {d.PrevLevel}";
            progressLabel.text = PlayerProgress.XpLabel;

            // Settle at the old fill BEFORE the block goes active. UIFillBar auto-plays itself the
            // moment its GameObject is enabled (OnEnable), using whatever from/target it was last set
            // to — and on every match after the first, that is the PREVIOUS match's numbers, still
            // sitting in those fields from its own animation. Activating the block first let that
            // stale replay flash for a frame before this call overwrote it; setting the real values
            // first means OnEnable's own auto-play already has this match's correct starting point.
            if (progressFill != null) progressFill.SetFraction(d.PrevFraction, d.PrevFraction);
            progressBlock.SetActive(true);

            if (progressRoutine != null) StopCoroutine(progressRoutine);
            progressRoutine = StartCoroutine(ProgressSequence(d));
        }

        private IEnumerator ProgressSequence(PlayerProgress.MatchOutcome d)
        {
            // Let the panel land and the score count first, so the XP arrives as its own beat.
            yield return new WaitForSecondsRealtime(ArcadeTheme.TNormal);

            if (!d.LeveledUp)
            {
                if (progressFill != null) progressFill.SetFraction(d.NewFraction, d.PrevFraction);
                if (progressEarnedText != null)
                {
                    yield return UITween.Pop(progressEarnedText.transform, 1.14f, ArcadeTheme.TNormal);
                }
                progressRoutine = null;
                yield break;
            }

            // Level up: fill to the top, celebrate, then carry the remainder into the new level.
            if (progressFill != null) progressFill.SetFraction(1f, d.PrevFraction);
            if (progressEarnedText != null)
            {
                yield return UITween.Pop(progressEarnedText.transform, 1.14f, ArcadeTheme.TNormal);
            }

            // Wait for the bar to reach the top (its pre-roll plus the sweep).
            if (!ArcadeTheme.ReducedMotion)
            {
                yield return new WaitForSecondsRealtime(0.12f + ArcadeTheme.TFlash);
            }

            if (progressEarnedText != null)
            {
                progressEarnedText.text = "LEVEL UP!";
                yield return UITween.Pop(progressEarnedText.transform, 1.25f, ArcadeTheme.TSlow);
            }
            if (progressLevelText != null)
            {
                progressLevelText.text = $"LV {d.NewLevel}";
                yield return UITween.Pop(progressLevelText.transform, 1.2f, ArcadeTheme.TNormal);
            }
            if (burst != null) burst.Play(ArcadeTheme.Gold);
            GameSfx.PlayUiClick();

            if (progressFill != null) progressFill.SetFraction(d.NewFraction, 0f);
            progressRoutine = null;
        }

        private void OnMatchWon(Team winner)
        {
            if (banner == null || ResultsHidden) return;

            // Every banner starts without the progression or ranked blocks and at the base height.
            // GameFlow calls ShowMatchProgress straight after this for vs-AI and online, and
            // ShowRankedProgress too for a won ranked match; local PvP and every loss leave both hidden.
            if (progressRoutine != null) { StopCoroutine(progressRoutine); progressRoutine = null; }
            if (progressBlock != null) progressBlock.SetActive(false);
            if (rankedRoutine != null) { StopCoroutine(rankedRoutine); rankedRoutine = null; }
            if (rankedBlock != null) rankedBlock.SetActive(false);
            if (bannerPanelRt != null)
            {
                bannerPanelRt.sizeDelta = new Vector2(bannerPanelRt.sizeDelta.x, BannerBaseHeight);
            }

            Color teamColor = winner == Team.Red ? ArcadeTheme.Red : ArcadeTheme.Blue;

            // Told to the player rather than reported about the teams, wherever there is a player to
            // tell. A team name makes the reader work out which side was theirs before they can know
            // how it went, which is the one thing this banner should never make anybody do.
            bool localKnown = LocalTeam.HasValue;
            bool localWon = localKnown && LocalTeam.Value == winner;

            bannerText.text = localKnown
                ? (localWon ? "YOU WIN" : "YOU LOSE")
                : (winner == Team.Red ? "RED" : "BLUE") + " WINS";

            // Still the winner's colour, even in defeat: it is the colour that has just beaten you,
            // and the banner then reads the same way in both mouths.
            bannerText.color = teamColor;

            // The winner's colour on the panel edge, so the result reads before any of the text does.
            var bpanel = banner.transform.Find("BannerPanel");
            var bborder = bpanel != null ? bpanel.Find("Border") : null;
            if (bborder != null) bborder.GetComponent<Image>().color = teamColor;

            bool forfeit = match != null && match.LastWinWasForfeit;
            if (bannerReason != null)
            {
                bannerReason.text = forfeit ? "OPPONENT LEFT" : string.Empty;
                bannerReason.color = ArcadeTheme.InkMuted;
            }

            // Nobody to play again against. Offering it would restart the match on this machine
            // alone, against an empty table.
            if (playAgainButton != null) playAgainButton.gameObject.SetActive(!forfeit);

            // Filled before the banner is shown, so the panel is already the right height when it
            // scales in — growing it afterwards would be a visible jolt under the player's eyes.
            //
            // Reading the tracker's snapshot here is only safe because it subscribed to MatchWon
            // first (see TableFootballUI) and has therefore already banked this match. Swap that
            // order and this silently shows the last match's rewards.
            bool hasLedger = ledger != null && ledger.Populate(QuestTracker.LastAwards);
            if (bannerPanel != null)
            {
                bannerPanel.sizeDelta = new Vector2(bannerPanel.sizeDelta.x,
                                                    BannerBaseHeight + (hasLedger ? ledger.Height : 0f));
            }

            banner.SetActive(true);
            banner.transform.SetAsLastSibling();

            if (bannerRoutine != null) StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(WinSequence(bpanel, teamColor, !localKnown || localWon));
        }

        /// <summary>
        /// The result lands in order rather than all at once: panel, then the score counting up,
        /// then the winner's name with a burst behind it.
        ///
        /// Sequencing it is the whole point — a win that simply appears carries no more weight than
        /// a menu opening, and this is the moment the match was for.
        /// </summary>
        private IEnumerator WinSequence(Transform panel, Color teamColor, bool celebrate)
        {
            if (bannerText != null) bannerText.transform.localScale = Vector3.one;

            if (panel != null)
            {
                yield return UITween.ScaleTo(panel, Vector3.one * ArcadeTheme.MenuFrom, Vector3.one,
                                             ArcadeTheme.TNormal, ArcadeTheme.EaseOutBack);
            }

            // Counted from zero, with the click landing on each digit change.
            var up = StartCoroutine(UITween.CountUp(bannerRed, 0, lastRed, ArcadeTheme.TSlow, GameSfx.PlayUiClick));
            yield return UITween.CountUp(bannerBlue, 0, lastBlue, ArcadeTheme.TSlow, GameSfx.PlayUiClick);
            yield return up;

            if (bannerText != null)
            {
                yield return UITween.Pop(bannerText.transform, 1.18f, ArcadeTheme.TSlow);
            }

            // Only the winner gets a burst — what this comment always claimed, but could not be
            // acted on until the screen knew whose it was. A loser watching confetti over YOU LOSE
            // is being congratulated on losing. Two players sharing one screen still get it: one of
            // them did just win, and it is their screen too.
            if (burst != null && celebrate) burst.Play(teamColor);

            // Last, and deliberately after the result has finished landing: the match is what the
            // player came for, and the quests are what they get for it.
            if (ledger != null)
            {
                yield return ledger.Play();
            }

            bannerRoutine = null;
        }

        private void OnTimeChanged(float remaining)
        {
            if (clockText == null || (match != null && match.InSuddenDeath))
            {
                return; // sudden death owns the display from here
            }

            int total = Mathf.CeilToInt(remaining);
            clockText.text = $"{total / 60}:{total % 60:00}";

            // Warn on the last half minute. The clock is small and peripheral, so colour carries
            // this far better than the digits do.
            clockText.color = remaining <= 30f ? ArcadeTheme.Gold : ArcadeTheme.Ink;

            // The last ten seconds go red and beat once a second, on the second — a pulse that ran
            // on its own timer would drift off the digits it belongs to.
            if (remaining <= 10f)
            {
                clockText.color = ArcadeTheme.Red;
                if (total != lastTick && clockRoot != null)
                {
                    lastTick = total;
                    clockPop = Restart(clockPop, UITween.Pop(clockRoot.transform, 1.12f, ArcadeTheme.TNormal));
                }
            }
            else
            {
                lastTick = -1;
            }
        }

        private void OnSuddenDeath()
        {
            if (clockRoot != null) clockRoot.SetActive(false);
            if (suddenDeathRoot != null) suddenDeathRoot.SetActive(true);
        }

        private void OnMatchRestarted()
        {
            if (banner != null) banner.SetActive(false);
            HideGoalPopup();

            // The next result draws its own ledger. Putting this one away now, while the banner is
            // hidden, is what stops a rematch opening on the last match's rewards.
            if (ledger != null) ledger.Hide();
            if (bannerPanel != null)
            {
                bannerPanel.sizeDelta = new Vector2(bannerPanel.sizeDelta.x, BannerBaseHeight);
            }

            // Put the clock back, or a fresh match starts with the sudden-death banner still up.
            if (suddenDeathRoot != null) suddenDeathRoot.SetActive(false);
            if (clockRoot != null) clockRoot.SetActive(true);
            if (clockText != null) clockText.color = ArcadeTheme.Ink;

            // Undo whatever the last rematch left on the button, so the NEXT result offers it again.
            // The banner is hidden now, so this is invisible — which is the point of doing it here
            // rather than trusting the next OnMatchWon to reset everything it might find.
            if (playAgainButton != null)
            {
                playAgainButton.gameObject.SetActive(true);
                playAgainButton.interactable = true;
            }
        }

        private Coroutine Restart(Coroutine current, IEnumerator routine)
        {
            if (current != null) StopCoroutine(current);
            return StartCoroutine(routine);
        }
    }
}
