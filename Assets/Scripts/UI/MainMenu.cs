using System;
using System.Collections;
using TableFootball.Net;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The front door: pick Online or Local, then pick a local mode.
    ///
    /// Two groups on one screen rather than two screens, matching <see cref="GameMenu"/> — going
    /// "back" then costs a SetActive rather than a rebuild, and there is only ever one menu object
    /// to show or hide.
    ///
    /// It reports choices through callbacks and never starts a match itself; <see cref="GameFlow"/>
    /// owns the sequence. That keeps the menu ignorant of rods, AI and match state, exactly as
    /// ScoreHud is ignorant of everything but MatchManager's events.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        private GameObject root;
        private CanvasGroup group;
        private Transform panel;
        private GameObject rootGroup;
        private GameObject difficultyGroup;
        private GameObject backHolder;
        private GameObject inviteBanner;
        private TMPro.TextMeshProUGUI inviteText;
        private TMPro.TextMeshProUGUI rankedPoints;
        private GameObject pathPip;
        private Coroutine anim;

        /// <summary>Set from the friends service callback, acted on in <see cref="Update"/>.</summary>
        private bool inviteDirty;

        /// <summary>Raised with true for "AI vs Player", false for "Player vs Player".</summary>
        public Action<bool> OnStartLocal;

        /// <summary>Raised when Online is chosen. The online screen is its own panel, not a group here.</summary>
        public Action OnOpenOnline;

        /// <summary>Raised by the settings icon. GameFlow hands this to the existing settings panel.</summary>
        public Action OnOpenSettings;

        /// <summary>Raised by the person icon. The friends list carries the account screen behind it.</summary>
        public Action OnOpenFriends;

        /// <summary>Raised by the avatar, top left. Goes straight to the account screen.</summary>
        public Action OnOpenProfile;

        /// <summary>Raised by the trophy button beside the chip. Opens the league screen directly —
        /// the same door <see cref="OnlineMenu"/>'s own Ranked button leads to.</summary>
        public Action OnOpenRanked;

        /// <summary>Raised by the shop icon in the bottom bar. Opens the cosmetics store.</summary>
        public Action OnOpenStore;

        /// <summary>Raised by the path icon in the bottom bar. Opens the level path.</summary>
        public Action OnOpenLevelPath;

        /// <summary>Raised with a friend's join code when their invitation is accepted from here.</summary>
        public Action<string> OnAcceptInvite;

        public bool IsOpen => root != null && root.activeSelf;

        public void Build(Transform canvasRoot, Sprite onlineArt, Sprite localArt,
                          Sprite playerVsPlayerArt, Sprite aiVsPlayerArt)
        {
            root = UIFactory.Child(canvasRoot, "MainMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();

            // A translucent scrim, not the opaque Backdrop the other screens use: MenuStageCamera
            // renders the real 3D table behind the menu, and this darkens it just enough to keep the
            // logo, cards and footer readable over the top.
            UIFactory.StageScrim(root.transform);

            // One header row owns the whole top edge — chip and trophy on the same plane, sharing one
            // reserved height — so the logo below it has a KNOWN band to start from instead of an
            // eyeballed gap. See BuildHeader.
            BuildHeader();

            // Moved below the header rather than sharing its band — at full size the wordmark and the
            // header's own content used to occupy overlapping vertical territory by construction, both
            // anchored to the top independently with no shared accounting of how tall either one was.
            // Sized as big as the gap between the header's known bottom edge and the card panel's own
            // (unmoved) top edge allows, rather than shrunk arbitrarily.
            var logo = UIFactory.LogoLockup(root.transform, scale: 0.9f);
            var trt = UIFactory.Rt(logo);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(0f, -292f);
            trt.offsetMax = new Vector2(0f, -132f);

            panel = UIFactory.Child(root.transform, "Choices").transform;
            var prt = UIFactory.Rt(panel.gameObject);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            // Must comfortably exceed three tiles plus the gaps, or the layout group shrinks them back
            // down to fit and the cards never actually get bigger. Three cards at 336 plus two of
            // BuildRow's widened gaps (60 each) is 1128; 1180 leaves a little slack, and still clears
            // the ~1385 the narrowest supported aspect (4:3) gives the canvas.
            prt.sizeDelta = new Vector2(1180f, 400f);
            // Sits the cards in the middle of the band between the logo and the footer, rather than
            // in the middle of the screen. Centring them on the screen is what left the big dead gap
            // under the title, because the logo above them is not balanced by anything below.
            prt.anchoredPosition = new Vector2(0f, -60f);

            rootGroup = BuildRow(panel, "RootGroup");

            // Three top-level cards, shown at once — no "Local" step in between. The old two-then-two
            // structure buried the modes a tap deeper than they deserve.
            const float cardW = 336f;
            UIFactory.Tile(rootGroup.transform, "Player vs Player", playerVsPlayerArt,
                           () => Choose(aiOpponent: false), note: "Play a friend beside you",
                           width: cardW, badge: UIFactory.Badge.PlayerVsPlayer);
            UIFactory.Tile(rootGroup.transform, "Player vs AI", aiVsPlayerArt,
                           ShowDifficulty, note: "Challenge the computer",
                           width: cardW, badge: UIFactory.Badge.AiVsPlayer);
            UIFactory.Tile(rootGroup.transform, "Online", onlineArt, () => OnOpenOnline?.Invoke(),
                           note: "Play over the internet",
                           width: cardW, badge: UIFactory.Badge.Online);

            BuildDifficulty(aiVsPlayerArt);

            BuildBottomBar();
            BuildInviteBanner();

            ShowRoot();
            root.SetActive(false);
        }

        /// <summary>
        /// The difficulty step, shown after "AI vs Player" is chosen.
        ///
        /// Picking a level starts the match immediately rather than needing a confirm — the whole
        /// step is one extra tap, which is about as much ceremony as choosing an opponent deserves.
        ///
        /// All three cards are styled identically. Accenting one as the recommended choice makes
        /// the other two read as worse options rather than as different ones.
        ///
        /// The three cards share the AI mode's artwork. They are the same mode at three strengths,
        /// so a distinct picture for each would be inventing a difference that is not there, and a
        /// null sprite draws the ARTWORK placeholder.
        ///
        /// No subtitle under the captions: the level names carry it on their own, and the cards sit
        /// side by side where the caption alone is the thing being compared.
        /// </summary>
        private void BuildDifficulty(Sprite art)
        {
            difficultyGroup = BuildRow(panel, "DifficultyGroup");

            // Narrower than the two-card rows: three of these have to share the same panel width.
            const float w = 296f;
            const float h = 340f;

            UIFactory.Tile(difficultyGroup.transform, "Easy", art, () => StartAi(AiLevel.Easy),
                           width: w, height: h, badge: UIFactory.Badge.AiVsPlayer);

            UIFactory.Tile(difficultyGroup.transform, "Normal", art, () => StartAi(AiLevel.Normal),
                           width: w, height: h, badge: UIFactory.Badge.AiVsPlayer);

            UIFactory.Tile(difficultyGroup.transform, "Hard", art, () => StartAi(AiLevel.Hard),
                           width: w, height: h, badge: UIFactory.Badge.AiVsPlayer);
        }

        /// <summary>
        /// Records the chosen level, then starts the match.
        ///
        /// Writing to GameAudio.Difficulty is what actually applies it: its setter persists the
        /// choice and pushes it into every TeamAI already in the scene. Doing it before the match
        /// starts means the AI is at the right strength from the first touch, and the Settings
        /// panel opens on whatever was picked here.
        /// </summary>
        private void StartAi(AiLevel level)
        {
            GameAudio.Difficulty = level;
            OnStartLocal?.Invoke(true);
        }

        private static GameObject BuildRow(Transform parent, string name)
        {
            var row = UIFactory.Child(parent, name);
            UIFactory.Stretch(UIFactory.Rt(row));

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            // Wider than a single spacing step (Xl3 = 48) — three equal-weight cards at that gap read
            // as one boxy wall rather than three separate, tactile choices. More air between them is
            // what makes each card its own object.
            layout.spacing = ArcadeTheme.Xl3 + ArcadeTheme.Md;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            return row;
        }

        /// <summary>
        /// One header row spanning the top edge, height-locked so nothing below it has to guess where
        /// it ends. The identity chip and the ranked pill sit on the same horizontal plane, on the
        /// same row, sharing the same reserved height — which is what "same plane" actually requires:
        /// two elements anchored independently can never guarantee that on their own, however carefully
        /// their numbers are tuned, because neither one knows the other's height.
        ///
        /// The pause button used to live here too (GameMenu's own top-right icon, positioned
        /// independently again). It is dropped from this screen entirely rather than squeezed in as a
        /// third card: before a match exists there is nothing to pause, "Resume" and "Main Menu" made
        /// no sense on the front door, and Settings already has its own icon in the bottom bar. See
        /// <see cref="TableFootball.UI.GameMenu.SetPauseButtonVisible"/> — GameFlow shows it only once
        /// a match is actually live.
        /// </summary>
        private void BuildHeader()
        {
            const float headerHeight = 92f;
            const float margin = 24f;

            var header = UIFactory.Child(root.transform, "HeaderBar");
            var hrt = UIFactory.Rt(header);
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.offsetMin = new Vector2(margin, -(margin + headerHeight));
            hrt.offsetMax = new Vector2(-margin, -margin);

            var h = header.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Md;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            // Height IS force-expanded: both cells fill the row exactly, which is the mechanism that
            // guarantees "same plane" rather than merely aiming for it.
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var chipCell = UIFactory.Child(header.transform, "ChipCell");
            // 400, not 340 — at 340 the name column was left ~70px after the avatar, badge and level
            // pill took their share, so any real name truncated to "Sh…". The header has ~700px of
            // spacer to give back, so widening the chip costs nothing else on the row.
            chipCell.AddComponent<LayoutElement>().preferredWidth = 400f;
            BuildProfileChip(chipCell.transform);

            // The purse, immediately beside the chip and on the same plane by the same mechanism: a
            // cell in this row, height-expanded like the others. Next to the player rather than off in
            // a corner because a balance is part of who you are in a game with a shop — and putting it
            // anywhere else would leave the player checking two places before opening the store.
            var coinCell = UIFactory.Child(header.transform, "CoinCell");
            var cle = coinCell.AddComponent<LayoutElement>();
            cle.preferredWidth = 200f;
            cle.minWidth = 200f;

            // The pill is a fixed-height slab centred in the cell rather than something that fills it.
            // The row force-expands its children to the full header height, and a purse stretched to
            // the chip's 92 units would be a mostly-empty box with a small coin adrift in it — the
            // chip is that tall because it holds four things; this holds one.
            var pillHolder = UIFactory.Child(coinCell.transform, "PillHolder");
            var hrt2 = UIFactory.Rt(pillHolder);
            hrt2.anchorMin = new Vector2(0f, 0.5f);
            hrt2.anchorMax = new Vector2(1f, 0.5f);
            hrt2.pivot = new Vector2(0.5f, 0.5f);
            hrt2.sizeDelta = new Vector2(0f, 54f);
            hrt2.anchoredPosition = Vector2.zero;

            // The component goes on a child of the holder, not on the holder itself — Build reparents
            // its own GameObject to what it is handed, and a transform cannot be its own parent. Same
            // shape as BuildProfileChip below.
            var pillGo = UIFactory.Child(pillHolder.transform, "Pill");
            pillGo.AddComponent<CoinPill>().Build(pillHolder.transform);

            var spacer = UIFactory.Child(header.transform, "Spacer");
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var rankedCell = UIFactory.Child(header.transform, "RankedCell");
            rankedCell.AddComponent<LayoutElement>().preferredWidth = 168f;
            BuildRankedButton(rankedCell.transform);
        }

        /// <summary>
        /// The player-identity chip: avatar, name, level and an XP bar, and the profile screen's front
        /// door. Fills whatever cell <see cref="BuildHeader"/> hands it.
        ///
        /// The lone avatar button this replaced said only "you"; the chip says who, what level and how
        /// far to the next — the compact identity a sports game leads with. Level and XP are visual
        /// placeholders (<see cref="PlayerProgress"/>); the name is real. The chip keeps itself in
        /// step with the account, so a rename on the screen it opens is reflected without wiring here.
        /// </summary>
        private void BuildProfileChip(Transform cell)
        {
            var chipGo = UIFactory.Child(cell, "Chip");
            chipGo.AddComponent<ProfileChip>().Build(cell);

            // A transparent sheet over the whole chip opens the profile. Cheaper and steadier than
            // making the chip itself a button — the chip is a layout of several parts, and a tap
            // target that is one flat rectangle over all of them never fights the layout.
            var tap = UIFactory.Child(cell, "Tap");
            var img = tap.AddComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = true;
            UIFactory.Stretch(UIFactory.Rt(tap), 0);
            var btn = tap.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnOpenProfile?.Invoke());
        }

        /// <summary>
        /// The ranked pill beside the chip — a compact stat, not a second identity card, that opens the
        /// league screen directly rather than making the player go by way of the online menu. Fills
        /// whatever cell <see cref="BuildHeader"/> hands it, so it is always exactly the chip's height.
        ///
        /// It reads its own score from <see cref="Ladder"/> exactly as the league screen does: cached
        /// and synchronous, redrawing off <see cref="Ladder.OnChanged"/>, so this button never fetches
        /// anything of its own — it only ever shows what the ladder already has.
        /// </summary>
        private void BuildRankedButton(Transform cell)
        {
            var btn = UIFactory.Button(cell, string.Empty, MenuButton.Variant.IconGold,
                                       () => OnOpenRanked?.Invoke(), 0f);
            UIFactory.Stretch(UIFactory.Rt(btn.gameObject), 0);

            // The label built by Button is centred and empty; the cup and the score are laid out over
            // it instead, so the button keeps its usual hover/press visuals underneath.
            var row = UIFactory.Child(btn.transform, "Row");
            UIFactory.Stretch(UIFactory.Rt(row), 14f, 0f, 14f, 0f);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Sm;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            var cupHolder = UIFactory.Child(row.transform, "Cup");
            var cle = cupHolder.AddComponent<LayoutElement>();
            cle.preferredWidth = 34f;
            cle.minWidth = 34f;
            UIFactory.TrophyGlyph(cupHolder.transform, ArcadeTheme.Gold, ArcadeTheme.BgRaised, 1.15f);

            rankedPoints = UIFactory.Text(row.transform, "—", ArcadeTheme.FsBody, ArcadeTheme.Gold,
                                          display: true, bold: true, upper: false, tracking: 1f);
            rankedPoints.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Draws whatever the ladder cache already holds. The live subscription is owned by
            // Open()/Close(), matching FriendsHub below — subscribing here once at build time would be
            // torn down by the first Close() and never renewed.
            RefreshRanked();
        }

        private void RefreshRanked()
        {
            if (rankedPoints == null)
            {
                return;
            }

            LadderStanding s = Ladder.Standing;
            rankedPoints.text = s.Valid ? s.WeeklyPoints.ToString() : "—";
        }

        /// <summary>Redraws the "rewards waiting" count on the path button.</summary>
        private void RefreshPathPip() => UIFactory.SetCountPip(pathPip, LevelPath.UnclaimedCount);

        private void OnDestroy()
        {
            FriendsHub.OnChanged -= MarkInviteDirty;
            Ladder.OnChanged -= RefreshRanked;
            LevelPath.OnChanged -= RefreshPathPip;
            PlayerProgress.OnChanged -= RefreshPathPip;
        }

        /// <summary>
        /// One bottom-anchored row for every "your things" action: Friends, Settings, and — only while
        /// the difficulty step is up — Back. There is no Quit here any more: the app is left the way a
        /// mobile game normally is, through the OS (home / task-switch / back gesture), rather than a
        /// button competing with Play for the player's attention on the one action nobody opened the
        /// menu to take. Friends/Settings used to sit bottom-left while Quit floated bottom-centre,
        /// unrelated systems sharing the bottom edge — one row fixes that structurally, the same way
        /// <see cref="BuildHeader"/> fixes the top.
        ///
        /// Back keeps its own cell (the same <see cref="backHolder"/> field
        /// <see cref="ShowRoot"/>/<see cref="ShowDifficulty"/> already toggle) sitting last in the row,
        /// so it can appear and disappear without shifting Friends/Settings.
        /// </summary>
        private void BuildBottomBar()
        {
            const float barHeight = 64f;
            const float margin = 32f;

            var bar = UIFactory.Child(root.transform, "BottomBar");
            var rt = UIFactory.Rt(bar);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(margin, margin);
            rt.offsetMax = new Vector2(-margin, margin + barHeight);

            var h = bar.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Md;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            // A visible resting border on all three plain nav icons — the shared Neutral default (a
            // faint Line-grey border) reads fine on a panel but got lost against the darkened table
            // backdrop these sit on, leaving them hard to pick out at rest. A light, even glow gives
            // them a constant presence without borrowing Store's gold, which has to stay the one
            // accent that says "something new lives here."
            Color navAccent = ArcadeTheme.Ink.WithAlpha(0.4f);
            const float navGlow = 0.12f;

            var friends = UIFactory.IconButton(bar.transform, "Friends", UIFactory.Icon.Person,
                                               MenuButton.Variant.Neutral,
                                               () => OnOpenFriends?.Invoke(), barHeight);
            friends.SetAccent(navAccent, navGlow);
            SquareCell(friends.gameObject, barHeight);

            var settings = UIFactory.IconButton(bar.transform, "Settings", UIFactory.Icon.Sliders,
                                                MenuButton.Variant.Neutral,
                                                () => OnOpenSettings?.Invoke(), barHeight);
            settings.SetAccent(navAccent, navGlow);
            SquareCell(settings.gameObject, barHeight);

            // Gold, alone among the four. The shop is the one button here that leads somewhere new
            // rather than to a list the player has already seen, and the accent is what stops it being
            // read as a third settings icon. IconGold is the same treatment the ranked pill gets, and
            // for the same reason.
            var store = UIFactory.IconButton(bar.transform, "Store", UIFactory.Icon.Store,
                                             MenuButton.Variant.IconGold,
                                             () => OnOpenStore?.Invoke(), barHeight);
            SquareCell(store.gameObject, barHeight);

            var path = UIFactory.IconButton(bar.transform, "LevelPath", UIFactory.Icon.Path,
                                            MenuButton.Variant.Neutral,
                                            () => OnOpenLevelPath?.Invoke(), barHeight);
            path.SetAccent(navAccent, navGlow);
            SquareCell(path.gameObject, barHeight);

            // The count of rewards waiting on the path, pinned to its button. The path is behind an
            // icon, and an icon cannot say "there are three things here for you" — which is the only
            // thing that would make a player open it on a day they levelled up without noticing.
            pathPip = UIFactory.CountPip(path.transform, 0);

            var spacer = UIFactory.Child(bar.transform, "Spacer");
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Back sits directly in the bar with an explicit fixed width, exactly like the icon
            // buttons above (SquareCell) — the previous BottomCell wrapper reported a preferred width
            // the row did not honour, so the Ghost button stretched into a wide dark bar across the
            // right half of the screen. A plainly-sized button at the icons' own height reads as one
            // more control in the row instead.
            var back = UIFactory.Button(bar.transform, "Back", MenuButton.Variant.Ghost, GoBack, barHeight);
            var ble = back.gameObject.GetComponent<LayoutElement>();
            ble.preferredWidth = 200f;
            ble.minWidth = 200f;
            ble.flexibleWidth = 0f;
            backHolder = back.gameObject;
        }

        /// <summary>Locks an icon button to a fixed square so the row's layout group cannot stretch it
        /// to the row's own (taller, if ever changed) height independently of its width.</summary>
        private static void SquareCell(GameObject go, float size)
        {
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.minWidth = size;
        }

        /// <summary>
        /// An invitation from a friend, across the top of the front screen.
        ///
        /// Here rather than only in the friends list because an invitation is the one thing in this
        /// game with somebody waiting on the other end of it. Buried behind the friends icon it was
        /// found by players who happened to go looking, which is not who it is for — a friend opens a
        /// table and sits at it, and the person they asked is standing on this screen.
        ///
        /// Top right, as a notice rather than a row in the layout. There is no empty band across this
        /// screen to push into — the header bar spans the top edge and the cards sit below the logo —
        /// so a full-width bar would have to sit on top of one of them. The right-hand corner is the
        /// one part of the menu genuinely holding nothing: the chip and ranked pill occupy the header's
        /// left and right, the bottom bar holds Friends/Settings/Quit, and the logo's text is centred.
        /// </summary>
        private void BuildInviteBanner()
        {
            var panel = UIFactory.Panel(root.transform, "InviteBanner");
            inviteBanner = panel;

            var rt = UIFactory.Rt(panel);
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(560f, 84f);
            // Y is pinned below HeaderBar's own reserved band (24 margin + 92 tall = 116, plus an 8
            // gap) rather than an independently guessed -32. The header now spans the FULL width —
            // including the ranked pill sitting at its own right edge — so anything positioned here by
            // guesswork risks landing on top of it; anchoring off the header's known height cannot.
            rt.anchoredPosition = new Vector2(-32f, -124f);

            // Gold edge, like the friends list's copy of this banner. The two are the same event and
            // should be recognisable as such from either screen.
            var border = panel.transform.Find("Border");
            if (border != null) border.GetComponent<Image>().color = ArcadeTheme.Gold;

            var fill = panel.transform.Find("Fill");
            var row = UIFactory.Child(fill, "Row");
            UIFactory.Stretch(UIFactory.Rt(row), 18f, 14f, 18f, 14f);

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = ArcadeTheme.Sm;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            // Carries a friend's chosen name, so it is drawn as text and not as markup.
            inviteText = UIFactory.Text(row.transform, string.Empty, ArcadeTheme.FsCaption,
                                        ArcadeTheme.Gold, display: false, bold: true, upper: true,
                                        tracking: 3f, align: TMPro.TextAlignmentOptions.Left,
                                        richText: false);
            inviteText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            // Narrower than the friends list's pair: this banner is 560 rather than the full width of
            // that screen, and the name has to fit beside them.
            var play = UIFactory.Button(row.transform, "Play", MenuButton.Variant.Primary,
                                        AcceptInvite, 48f);
            play.gameObject.GetComponent<LayoutElement>().preferredWidth = 118f;

            var later = UIFactory.Button(row.transform, "Later", MenuButton.Variant.Ghost,
                                         DeclineInvite, 48f);
            later.gameObject.GetComponent<LayoutElement>().preferredWidth = 104f;

            inviteBanner.SetActive(false);
        }

        private void MarkInviteDirty() => inviteDirty = true;

        /// <summary>
        /// Service notifications arrive off whatever thread the SDK pleases, and several can land at
        /// once. Collapsed into one redraw a frame, exactly as the friends list does it.
        /// </summary>
        private void Update()
        {
            if (inviteDirty && IsOpen)
            {
                inviteDirty = false;
                RefreshInvite();
            }
        }

        private void RefreshInvite()
        {
            if (inviteBanner == null)
            {
                return;
            }

            FriendsHub.Invite? invite = FriendsHub.PendingInvite;
            inviteBanner.SetActive(invite.HasValue);

            if (invite.HasValue && inviteText != null)
            {
                inviteText.text = $"{invite.Value.FromName} wants to play";
            }
        }

        private void AcceptInvite()
        {
            FriendsHub.Invite? invite = FriendsHub.PendingInvite;
            if (!invite.HasValue)
            {
                return;
            }

            // Cleared before joining, not after. Whether the join succeeds or the table has since
            // filled, the invitation has been answered and should stop asking.
            string code = invite.Value.JoinCode;
            FriendsHub.DismissInvite();
            RefreshInvite();

            OnAcceptInvite?.Invoke(code);
        }

        private async void DeclineInvite()
        {
            await FriendsHub.DeclineInviteAsync();
            RefreshInvite();
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null)
            {
                return;
            }

            ShowRoot();
            // The profile chip keeps its own avatar and name current off PlayerAccount.OnChanged, so
            // there is nothing to refresh here by hand.

            // Removed first, so reopening cannot stack a second handler onto a static event that
            // outlives this panel.
            FriendsHub.OnChanged -= MarkInviteDirty;
            FriendsHub.OnChanged += MarkInviteDirty;
            RefreshInvite();

            Ladder.OnChanged -= RefreshRanked;
            Ladder.OnChanged += RefreshRanked;
            // Asked for here rather than only by the league screen, so the trophy button already has a
            // real number the first time this menu is seen — a player who never opens Ranked would
            // otherwise stare at "—" forever.
            Ladder.Refresh();

            LevelPath.OnChanged -= RefreshPathPip;
            LevelPath.OnChanged += RefreshPathPip;
            // Levelling up is what CREATES an unclaimed reward, and it happens on the results screen
            // rather than here — so the pip follows progression as well as the path itself, or a player
            // who levelled in their last match would return to a menu that had nothing to say about it.
            PlayerProgress.OnChanged -= RefreshPathPip;
            PlayerProgress.OnChanged += RefreshPathPip;
            RefreshPathPip();

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;

            if (anim != null)
            {
                StopCoroutine(anim);
            }

            anim = StartCoroutine(OpenAnim());
        }

        public void Close()
        {
            FriendsHub.OnChanged -= MarkInviteDirty;
            Ladder.OnChanged -= RefreshRanked;
            LevelPath.OnChanged -= RefreshPathPip;
            PlayerProgress.OnChanged -= RefreshPathPip;

            if (anim != null)
            {
                StopCoroutine(anim);
                anim = null;
            }

            if (root != null)
            {
                group.blocksRaycasts = false;
                root.SetActive(false);
            }
        }

        /// <summary>
        /// The menu assembles rather than appears: the screen fades up, then the logo, the active
        /// card row and the footer pop in one after another.
        ///
        /// The rows are staggered by their own children, so the two cards land separately — which is
        /// what makes the pair read as two choices rather than as one image.
        /// </summary>
        private IEnumerator OpenAnim()
        {
            group.alpha = 0f;
            yield return UITween.Fade(group, 0f, 1f, ArcadeTheme.TFast, ArcadeTheme.EaseOut);

            var logo = root.transform.Find("Logo");
            if (logo != null) StartCoroutine(UITween.PopIn(logo, ArcadeTheme.TSlow));

            yield return new WaitForSecondsRealtime(ArcadeTheme.Stagger * 2f);

            var activeRow = rootGroup != null && rootGroup.activeSelf ? rootGroup : difficultyGroup;
            if (activeRow != null)
            {
                yield return UITween.Stagger(this, activeRow.transform, ArcadeTheme.Stagger * 2f,
                                             ArcadeTheme.TSlow);
            }

            // Nothing pops in for the root state any more — Friends/Settings are permanent fixtures of
            // the bottom bar now rather than a toggled footer, and Back (the one thing that still
            // toggles) is inactive here by definition.
            if (backHolder != null && backHolder.activeSelf)
            {
                StartCoroutine(UITween.PopIn(backHolder.transform, ArcadeTheme.TNormal));
            }

            anim = null;
        }

        private void ShowRoot()
        {
            if (rootGroup == null)
            {
                return;
            }

            rootGroup.SetActive(true);
            if (difficultyGroup != null) difficultyGroup.SetActive(false);
            if (backHolder != null) backHolder.SetActive(false);
            RestageOnSwap(rootGroup, null);
        }

        private void ShowDifficulty()
        {
            rootGroup.SetActive(false);
            if (difficultyGroup != null) difficultyGroup.SetActive(true);
            if (backHolder != null) backHolder.SetActive(true);
            RestageOnSwap(difficultyGroup, backHolder);
        }

        /// <summary>
        /// Back from the difficulty step returns to the three cards. It is the only step-in the menu
        /// has left now that the modes are all top-level, so there is only ever the one place to go.
        /// </summary>
        private void GoBack()
        {
            ShowRoot();
        }

        /// <summary>
        /// Re-runs the card entrance when the groups swap, so stepping into the difficulty step and
        /// back out feels like the same menu rebuilding rather than a hard cut between two screens.
        ///
        /// Skipped while the menu is closed: Build and Open both call ShowRoot before anything is on
        /// screen, and animating there would be a tween nobody sees, racing the one Open starts.
        /// </summary>
        private void RestageOnSwap(GameObject row, GameObject footer)
        {
            if (root == null || !root.activeSelf || anim != null) return;

            if (row != null) StartCoroutine(UITween.Stagger(this, row.transform, ArcadeTheme.Stagger, ArcadeTheme.TNormal));
            if (footer != null) StartCoroutine(UITween.PopIn(footer.transform, ArcadeTheme.TNormal));
        }

        /// <summary>Not named Start: Unity reserves that, and an overload here invites confusion.</summary>
        private void Choose(bool aiOpponent)
        {
            OnStartLocal?.Invoke(aiOpponent);
        }
    }
}
