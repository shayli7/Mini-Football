using System;
using System.Collections.Generic;
using TableFootball.Net;
using TableFootball.Progression;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The front screen: who you are and what you have along the top, the wordmark, the three ways to
    /// play as cards, and the bottom bar. Built on UI Toolkit (see <see cref="UiToolkitHost"/>) and
    /// styled by <c>Resources/UI/Styles/MainMenu.uss</c>.
    ///
    /// Like every screen it reports choices through callbacks and never starts anything itself —
    /// <see cref="GameFlow"/> owns the sequence. Choosing "Player vs AI" steps into the difficulty
    /// cards in place; everything else leaves the screen.
    ///
    /// Every card starts its mode on the first tap. Offline, the Online card is locked (see
    /// <see cref="ApplyConnectivity"/>) and the header says so; everything else works without a signal.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        private enum Mode { PlayerVsPlayer = 0, PlayerVsAi = 1, Online = 2 }

        private VisualElement root;
        private VisualElement modeRow;
        private VisualElement difficultyRow;
        private VisualElement logo;
        private VisualElement caption;
        private VisualElement nav;
        private VisualElement backButton;
        private VisualElement inviteBanner;
        private Label inviteText;

        private UiShineLabel nameLabel;
        private VisualElement nameBadgeSlot;
        private Label levelLabel;
        private Label xpLabel;
        private VisualElement xpFill;
        private Label avatarInitial;
        private Label coinLabel;
        private Label rankedLabel;
        private Label questPip;
        private VisualElement chipBadge;
        private Label chipBadgeLabel;

        // The parts of the Online card that change with the connection.
        private VisualElement offlineChip;
        private VisualElement onlineCard;
        private VisualElement onlineLive;
        private VisualElement onlineOfflineGlyph;
        private VisualElement onlinePlay;
        private Label onlineLine;
        private Label onlinePlayLabel;
        private UiIcon onlinePlayIcon;
        private UiIcon onlineLockIcon;

        private readonly List<VisualElement> modeCards = new List<VisualElement>();
        private readonly List<VisualElement> difficultyCards = new List<VisualElement>();
        private readonly List<IVisualElementScheduledItem> loops = new List<IVisualElementScheduledItem>();

        /// <summary>Set from the friends service callback, acted on in <see cref="Update"/>.</summary>
        private bool inviteDirty;

        /// <summary>Raised with true for "AI vs Player", false for "Player vs Player".</summary>
        public Action<bool> OnStartLocal;

        /// <summary>Raised when Online is chosen. The online screen is its own screen.</summary>
        public Action OnOpenOnline;

        /// <summary>Raised by the friends button.</summary>
        public Action OnOpenFriends;

        /// <summary>Raised by the profile chip, top left. Goes straight to the account screen.</summary>
        public Action OnOpenProfile;

        /// <summary>Raised by the trophy chip, top right. Opens the league screen directly.</summary>
        public Action OnOpenRanked;

        /// <summary>Raised by the store button in the bottom bar.</summary>
        public Action OnOpenStore;

        /// <summary>Opens the daily quests screen.</summary>
        public Action OnOpenQuests;

        /// <summary>Raised with a friend's join code when their invitation is accepted from here.</summary>
        public Action<string> OnAcceptInvite;

        public bool IsOpen => root != null && root.style.display != DisplayStyle.None;

        /// <summary>
        /// <paramref name="canvasRoot"/> is the uGUI canvas the other screens still hang off, unused
        /// here and kept so TableFootballUI's boot order does not change. <paramref name="localArt"/>
        /// is no longer drawn — the "Local" step it illustrated was folded into the three cards.
        /// </summary>
        public void Build(Transform canvasRoot, Sprite onlineArt, Sprite localArt,
                          Sprite playerVsPlayerArt, Sprite aiVsPlayerArt)
        {
            root = UiKit.El("screen menu", UiToolkitHost.Root, "MainMenu");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/MainMenu");
            if (sheet != null) root.styleSheets.Add(sheet);
            // The floor of the menu: nothing behind it may be touched while it is up.
            root.pickingMode = PickingMode.Position;

            // The Arena, behind everything. Opaque: the live table no longer shows through the menu.
            UiBackdrop.AddTo(root);

            BuildHeader();

            logo = UiKit.El("menu__logo", root);
            UiKit.Text(ArcadeTheme.GameNameA, "menu__mini f-display-semi", logo);
            var wordmark = UiKit.El("menu__wordmark", logo);
            UiKit.El("menu__rule", wordmark);
            UiKit.Text(ArcadeTheme.GameNameB, "menu__football f-display", wordmark);
            UiKit.El("menu__rule", wordmark);

            caption = UiKit.Text("CHOOSE A LEVEL", "menu__caption f-body-semi", root);

            modeRow = UiKit.El("menu__cards", root);
            modeCards.Add(Card(modeRow, "VS FRIEND", "2 players · 1 phone", UiIcon.Glyph.TwoPlayers,
                               playerVsPlayerArt, () => ChooseMode(Mode.PlayerVsPlayer)));
            modeCards.Add(Card(modeRow, "VS AI", "Pick a level next", UiIcon.Glyph.Robot,
                               aiVsPlayerArt, () => ChooseMode(Mode.PlayerVsAi)));
            onlineCard = Card(modeRow, "ONLINE", "Ranked · with friends", UiIcon.Glyph.Globe,
                              onlineArt, () => ChooseMode(Mode.Online));
            modeCards.Add(onlineCard);
            ExposeOnlineParts(onlineCard);

            difficultyRow = UiKit.El("menu__cards", root);
            difficultyCards.Add(Card(difficultyRow, "EASY", "Learn the table", UiIcon.Glyph.Robot,
                                     aiVsPlayerArt, () => StartAi(AiLevel.Easy), 1));
            difficultyCards.Add(Card(difficultyRow, "NORMAL", "A fair match", UiIcon.Glyph.Robot,
                                     aiVsPlayerArt, () => StartAi(AiLevel.Normal), 2));
            difficultyCards.Add(Card(difficultyRow, "HARD", "Fast and sharp", UiIcon.Glyph.Robot,
                                     aiVsPlayerArt, () => StartAi(AiLevel.Hard), 3));

            BuildBottomBar();

            backButton = UiKit.Frame("btn btn--ghost menu__back", root);
            backButton.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f) { name = "BackIcon" });
            UiKit.Text("BACK", "btn__label f-display", backButton);
            UiKit.OnTap(backButton, ShowModes, UiSound.Back);

            BuildInviteBanner();

            UiFonts.Apply(root);
            ShowModes();
            UiKit.Show(root, false);
        }

        // ---------- build: header ----------

        private void BuildHeader()
        {
            var header = UiKit.El("menu__header", root);

            // The identity chip: hex avatar, name, level, waiting rewards and XP. The whole chip opens
            // the account screen.
            var chip = UiKit.Frame("menu__profile", header);
            UiKit.OnTap(chip, () => OnOpenProfile?.Invoke());
            var hex = new UiHex();
            hex.AddToClassList("menu__hex");
            chip.Add(hex);
            avatarInitial = UiKit.Text("?", "avatar__initial f-display", hex);

            var info = UiKit.El("menu__profile-info", chip);
            var nameRow = UiKit.El("row", info);
            nameBadgeSlot = UiKit.El("menu__name-badge", nameRow);
            nameLabel = new UiShineLabel();
            nameLabel.AddToClassList("menu__name");
            nameLabel.AddToClassList("f-display-semi");
            nameRow.Add(nameLabel);
            levelLabel = UiKit.Text("LV 1", "level-pill f-display", nameRow);

            // Rewards waiting on the level path: the small gold chest and a count.
            chipBadge = UiKit.El("menu__badge", nameRow);
            chipBadge.Add(new UiIcon(UiIcon.Glyph.Chest, ArcadeTheme.OnReward, 3f));
            chipBadgeLabel = UiKit.Text("0", "menu__badge-label f-display", chipBadge);
            UiKit.Show(chipBadge, false);

            var xpRow = UiKit.El("row menu__xp-row", info);
            var bar = UiKit.El("bar menu__xp-bar", xpRow);
            xpFill = UiKit.El("bar__fill", bar);
            xpLabel = UiKit.Text(string.Empty, "menu__xp-label", xpRow);

            var coins = UiKit.Frame("menu__chip", header);
            coins.Add(new UiIcon(UiIcon.Glyph.Coin, ArcadeTheme.Coin, 2f));
            coinLabel = UiKit.Text("0", "menu__chip-value f-display", coins);

            UiKit.El("grow", header);

            // Shown only when the phone has no connection.
            offlineChip = UiKit.Frame("menu__chip menu__chip--offline", header);
            offlineChip.Add(new UiIcon(UiIcon.Glyph.WifiOff, ArcadeTheme.Danger, 2f));
            UiKit.Text("OFFLINE", "menu__chip-value menu__chip-value--offline f-display", offlineChip);
            UiKit.Show(offlineChip, false);

            var ranked = UiKit.Frame("menu__chip", header);
            ranked.Add(new UiIcon(UiIcon.Glyph.Trophy, ArcadeTheme.Reward, 2f));
            rankedLabel = UiKit.Text("—", "menu__chip-value f-display", ranked);
            UiKit.OnTap(ranked, () => OnOpenRanked?.Invoke());
        }

        // ---------- build: cards ----------

        /// <summary>
        /// A mode or difficulty card: the picture with its title on it, and a cyan PLAY strip below.
        /// <paramref name="strength"/> above zero adds the three-bar meter beside the title (difficulty
        /// cards). The whole card is the tap target; the strip is only a picture of a button.
        /// </summary>
        private static VisualElement Card(VisualElement row, string title, string line, UiIcon.Glyph glyph,
                                          Sprite art, Action onTap, int strength = 0)
        {
            var card = UiKit.Frame("card", row);
            UiKit.OnTap(card, onTap);

            var picture = UiKit.El("card__picture", card, "Picture");
            picture.pickingMode = PickingMode.Ignore;
            if (art != null) picture.style.backgroundImage = new StyleBackground(art);

            var shade = UiKit.El("card__shade", picture);
            shade.pickingMode = PickingMode.Ignore;
            shade.style.backgroundImage = new StyleBackground(ArcadeTheme.Scrim().texture);
            UiKit.El("card__bracket", picture).pickingMode = PickingMode.Ignore;

            var badge = UiKit.Frame("card__badge", picture);
            badge.pickingMode = PickingMode.Ignore;
            badge.Add(new UiIcon(glyph, ArcadeTheme.Pulse, 2f));

            var foot = UiKit.El("card__foot", picture);
            foot.pickingMode = PickingMode.Ignore;
            var titleRow = UiKit.El("card__title-row", foot);
            titleRow.pickingMode = PickingMode.Ignore;
            UiKit.Text(title, "card__title f-display", titleRow);
            if (strength > 0)
            {
                var meter = UiKit.El("meter", titleRow);
                meter.pickingMode = PickingMode.Ignore;
                for (int i = 0; i < 3; i++)
                {
                    var b = UiKit.El("meter__bar meter__bar--" + (i + 1), meter);
                    if (i < strength) b.AddToClassList("is-lit");
                }
            }
            UiKit.Text(line, "card__line f-body-semi", foot).name = "Line";

            var play = UiKit.Frame("btn btn--pulse card__play", card, "Play");
            play.pickingMode = PickingMode.Ignore;
            play.Add(new UiIcon(UiIcon.Glyph.Play, ArcadeTheme.OnPulse, 2f) { name = "PlayIcon" });
            UiKit.Text("PLAY", "btn__label f-display", play).name = "PlayLabel";
            return card;
        }

        /// <summary>Finds the Online card's parts once, and adds the two that only it has.</summary>
        private void ExposeOnlineParts(VisualElement card)
        {
            var picture = card.Q("Picture");
            onlineLine = card.Q<Label>("Line");
            onlinePlay = card.Q("Play");
            onlinePlayLabel = card.Q<Label>("PlayLabel");
            onlinePlayIcon = card.Q<UiIcon>("PlayIcon");

            // "Live", while there is a connection.
            onlineLive = UiKit.El("card__live", picture);
            onlineLive.pickingMode = PickingMode.Ignore;
            UiKit.El("card__live-dot", onlineLive).pickingMode = PickingMode.Ignore;
            UiKit.Text("LIVE", "card__live-label f-display", onlineLive);

            // A big struck-through signal over the picture, while there is not.
            onlineOfflineGlyph = UiKit.El("card__offline", picture);
            onlineOfflineGlyph.pickingMode = PickingMode.Ignore;
            onlineOfflineGlyph.Add(new UiIcon(UiIcon.Glyph.WifiOff, ArcadeTheme.Danger, 1.6f));

            // The lock that replaces the play arrow in the strip.
            onlineLockIcon = new UiIcon(UiIcon.Glyph.Lock, ArcadeTheme.InkMuted, 2.2f);
            onlinePlay.Insert(0, onlineLockIcon);
        }

        // ---------- build: bottom bar ----------

        private void BuildBottomBar()
        {
            // Centred by a full-width wrapper rather than by a translate, so the entrance animation
            // (which uses translate) can run on the bar itself.
            var wrap = UiKit.El("nav-wrap", root);
            wrap.pickingMode = PickingMode.Ignore;
            nav = UiKit.Frame("nav", wrap);
            NavItem(UiIcon.Glyph.Person, "FRIENDS", () => OnOpenFriends?.Invoke());
            NavItem(UiIcon.Glyph.Store, "STORE", () => OnOpenStore?.Invoke());
            questPip = NavItem(UiIcon.Glyph.Quests, "QUESTS", () => OnOpenQuests?.Invoke(), withPip: true);
        }

        private Label NavItem(UiIcon.Glyph glyph, string label, Action onTap, bool withPip = false)
        {
            var item = UiKit.El("nav__item", nav);
            UiKit.OnTap(item, onTap);
            item.Add(new UiIcon(glyph, ArcadeTheme.Ink, 2f));
            UiKit.Text(label, "nav__label f-display", item);
            if (!withPip) return null;

            // "Something is waiting behind this icon" — rewards on the path, cleared quests unseen.
            var pip = UiKit.Text(string.Empty, "nav__pip f-display", item);
            UiKit.Show(pip, false);
            return pip;
        }

        // ---------- build: invite banner ----------

        /// <summary>
        /// An invitation from a friend, top right under the header. Here rather than only in the
        /// friends list because it is the one thing in this game with somebody waiting on the other
        /// end of it, and the person invited is standing on this screen.
        /// </summary>
        private void BuildInviteBanner()
        {
            inviteBanner = UiKit.Frame("invite", root);
            inviteText = UiKit.Text(string.Empty, "invite__text f-body-semi grow", inviteBanner);
            UiKit.Button("PLAY", "pulse", AcceptInvite, inviteBanner, "invite__btn");
            UiKit.Button("LATER", "ghost", DeclineInvite, inviteBanner, "invite__btn");
            UiKit.Show(inviteBanner, false);
        }

        // ---------- behaviour ----------

        private void ChooseMode(Mode mode)
        {
            switch (mode)
            {
                case Mode.PlayerVsPlayer: OnStartLocal?.Invoke(false); break;
                case Mode.PlayerVsAi: ShowDifficulty(); break;
                case Mode.Online: OnOpenOnline?.Invoke(); break;
            }
        }

        /// <summary>
        /// Records the chosen level, then starts the match. Writing GameAudio.Difficulty is what
        /// applies it: its setter persists the choice and pushes it into every TeamAI in the scene.
        /// </summary>
        private void StartAi(AiLevel level)
        {
            GameAudio.Difficulty = level;
            OnStartLocal?.Invoke(true);
        }

        private void ShowModes()
        {
            UiKit.Show(modeRow, true);
            UiKit.Show(difficultyRow, false);
            UiKit.Show(backButton, false);
            UiKit.Show(caption, false);
            if (IsOpen) EnterCards(modeCards);
        }

        private void ShowDifficulty()
        {
            UiKit.Show(modeRow, false);
            UiKit.Show(difficultyRow, true);
            UiKit.Show(backButton, true);
            UiKit.Show(caption, true);
            EnterCards(difficultyCards);
            UiKit.Enter(backButton, 0.1f);
        }

        private static void EnterCards(List<VisualElement> cards)
        {
            for (int i = 0; i < cards.Count; i++) UiKit.Enter(cards[i], i * 0.06f);
        }

        // ---------- data ----------

        private void RefreshProfile()
        {
            string name = PlayerAccount.DisplayName;
            nameLabel.Set(string.IsNullOrWhiteSpace(name) ? "—" : name);
            nameBadgeSlot.Clear();
            VisualElement worn = UiArt.WornBadge(26f);
            if (worn != null) nameBadgeSlot.Add(worn);
            UiKit.Show(nameBadgeSlot, worn != null);
            avatarInitial.text = string.IsNullOrWhiteSpace(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            levelLabel.text = $"LV {PlayerProgress.Level}";
            xpLabel.text = PlayerProgress.XpLabel;
            xpFill.style.width = Length.Percent(Mathf.Clamp01(PlayerProgress.XpFraction) * 100f);
        }

        private void RefreshCoins() => coinLabel.text = Wallet.Coins.ToString("N0");

        private void RefreshRanked()
        {
            LadderStanding s = Ladder.Standing;
            rankedLabel.text = s.Valid ? $"{Leagues.ShortName(s.League).ToUpperInvariant()} #{s.RankInPod}" : "—";
        }

        private void RefreshPathBadge()
        {
            // Rewards waiting on the Level Road ride on the profile chip, the way in to it.
            int waiting = LevelPath.UnclaimedCount;
            UiKit.Show(chipBadge, waiting > 0);
            chipBadgeLabel.text = waiting > 9 ? "9+" : waiting.ToString();
        }

        private void RefreshQuestPip() => SetPip(questPip, DailyQuests.UnseenCount);

        /// <summary>
        /// Puts the menu in step with the connection. Offline, the header says so and the Online card
        /// is locked: dimmed, its play arrow a padlock, and a tap on it is answered with the dull "no"
        /// note rather than opening a screen that could only fail. (A card with the
        /// <c>is-disabled</c> class is refused by <see cref="UiKit.OnTap"/>.)
        /// </summary>
        private void ApplyConnectivity()
        {
            bool online = Connectivity.IsOnline;

            UiKit.Show(offlineChip, !online);
            UiKit.Show(onlineLive, online);
            UiKit.Show(onlineOfflineGlyph, !online);
            onlineCard.EnableInClassList("is-locked", !online);
            onlineCard.EnableInClassList("is-disabled", !online);
            onlinePlay.EnableInClassList("is-disabled", !online);

            onlineLine.text = online ? "Ranked · with friends" : "Connect to play online";
            onlinePlayLabel.text = online ? "PLAY" : "NO CONNECTION";
            UiKit.Show(onlinePlayIcon, online);
            UiKit.Show(onlineLockIcon, !online);
        }

        private static void SetPip(Label pip, int count)
        {
            if (pip == null) return;
            UiKit.Show(pip, count > 0);
            pip.text = count > 9 ? "9+" : count.ToString();
        }

        private void RefreshAll()
        {
            RefreshProfile();
            RefreshCoins();
            RefreshRanked();
            RefreshPathBadge();
            RefreshQuestPip();
            RefreshInvite();
        }

        // ---------- invites ----------

        private void MarkInviteDirty() => inviteDirty = true;

        /// <summary>
        /// Service notifications arrive off whatever thread the SDK pleases, and several can land at
        /// once. Collapsed into one redraw a frame, exactly as the friends list does it.
        /// </summary>
        private void Update()
        {
            // The connection is read here, once a second, only while the menu is up.
            if (IsOpen) Connectivity.Poll();

            if (inviteDirty && IsOpen)
            {
                inviteDirty = false;
                RefreshInvite();
            }
        }

        private void RefreshInvite()
        {
            if (inviteBanner == null) return;

            FriendsHub.Invite? invite = FriendsHub.PendingInvite;
            bool show = invite.HasValue;
            if (show && !UiKit.IsShown(inviteBanner)) UiKit.Enter(inviteBanner);
            UiKit.Show(inviteBanner, show);
            if (show) inviteText.text = $"{invite.Value.FromName} wants to play";
        }

        private void AcceptInvite()
        {
            FriendsHub.Invite? invite = FriendsHub.PendingInvite;
            if (!invite.HasValue) return;

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

        // ---------- open / close ----------

        public void Open()
        {
            if (root == null) return;

            ShowModes();

            // Each removed first, so reopening cannot stack a second handler onto a static event
            // that outlives this screen.
            PlayerAccount.OnChanged -= RefreshProfile;
            PlayerAccount.OnChanged += RefreshProfile;
            Inventory.OnChanged -= RefreshProfile;
            Inventory.OnChanged += RefreshProfile;
            PlayerProgress.OnChanged -= OnProgressChanged;
            PlayerProgress.OnChanged += OnProgressChanged;
            Wallet.OnChanged -= RefreshCoins;
            Wallet.OnChanged += RefreshCoins;
            FriendsHub.OnChanged -= MarkInviteDirty;
            FriendsHub.OnChanged += MarkInviteDirty;
            Ladder.OnChanged -= RefreshRanked;
            Ladder.OnChanged += RefreshRanked;
            LevelPath.OnChanged -= RefreshPathBadge;
            LevelPath.OnChanged += RefreshPathBadge;
            DailyQuests.EnsureToday();
            DailyQuests.OnChanged -= RefreshQuestPip;
            DailyQuests.OnChanged += RefreshQuestPip;
            Connectivity.OnChanged -= ApplyConnectivity;
            Connectivity.OnChanged += ApplyConnectivity;
            ApplyConnectivity();

            // Asked for here rather than only by the league screen, so the trophy chip already has a
            // real number the first time this menu is seen.
            Ladder.Refresh();
            RefreshAll();

            UiKit.Show(root, true);
            root.BringToFront();
            root.style.opacity = 0f;
            UiKit.Fade(root, 1f, ArcadeTheme.TFast);

            // The menu assembles rather than appears: logo, then the cards one after another, then
            // the bar — so three cards read as three choices rather than as one image.
            UiKit.Enter(logo);
            EnterCards(modeCards);
            UiKit.Enter(nav, 0.22f);

            StartLoops();
        }

        public void Close()
        {
            PlayerAccount.OnChanged -= RefreshProfile;
            Inventory.OnChanged -= RefreshProfile;
            PlayerProgress.OnChanged -= OnProgressChanged;
            Wallet.OnChanged -= RefreshCoins;
            FriendsHub.OnChanged -= MarkInviteDirty;
            Ladder.OnChanged -= RefreshRanked;
            LevelPath.OnChanged -= RefreshPathBadge;
            DailyQuests.OnChanged -= RefreshQuestPip;
            Connectivity.OnChanged -= ApplyConnectivity;

            StopLoops();
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy() => Close();

        /// <summary>Levelling up is what creates an unclaimed path reward, so the path pip follows
        /// progression as well as the path itself.</summary>
        private void OnProgressChanged()
        {
            RefreshProfile();
            RefreshPathBadge();
        }

        // ---------- ambient motion ----------

        /// <summary>
        /// The one thing that moves while the menu sits still: the waiting-reward pips breathing (the
        /// nav's and the profile chip's). Paused while the menu is closed, and off under reduced motion.
        /// </summary>
        private void StartLoops()
        {
            StopLoops();
            if (ArcadeTheme.ReducedMotion) return;

            loops.Add(UiKit.Loop(root, now =>
            {
                float breath = 1f + 0.12f * (0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2f / 1.4f));
                var scale = new Scale(new Vector3(breath, breath, 1f));
                if (questPip != null) questPip.style.scale = scale;
                if (chipBadge != null) chipBadge.style.scale = scale;
            }));
        }

        private void StopLoops()
        {
            foreach (var l in loops) l.Pause();
            loops.Clear();
        }
    }
}
