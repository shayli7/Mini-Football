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
    /// The gold card is the mode the player last started, so the one highlighted thing on the screen
    /// is "play again" — it defaults to Player vs AI for a new player. Every card still starts its
    /// mode on the first tap: the highlight is a suggestion, not a selection step.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        private const string LastModeKey = "tf_lastmode";

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

        private Label nameLabel;
        private Label levelLabel;
        private Label xpLabel;
        private VisualElement xpFill;
        private Label avatarInitial;
        private Label coinLabel;
        private Label rankedLabel;
        private Label pathPip;
        private Label questPip;

        private readonly List<VisualElement> modeCards = new List<VisualElement>();
        private readonly List<VisualElement> difficultyCards = new List<VisualElement>();
        private readonly List<IVisualElementScheduledItem> loops = new List<IVisualElementScheduledItem>();

        /// <summary>Set from the friends service callback, acted on in <see cref="Update"/>.</summary>
        private bool inviteDirty;

        /// <summary>Raised with true for "AI vs Player", false for "Player vs Player".</summary>
        public Action<bool> OnStartLocal;

        /// <summary>Raised when Online is chosen. The online screen is its own screen.</summary>
        public Action OnOpenOnline;

        /// <summary>Raised by the settings button. GameFlow hands this to the settings panel.</summary>
        public Action OnOpenSettings;

        /// <summary>Raised by the friends button.</summary>
        public Action OnOpenFriends;

        /// <summary>Raised by the profile chip, top left. Goes straight to the account screen.</summary>
        public Action OnOpenProfile;

        /// <summary>Raised by the trophy chip, top right. Opens the league screen directly.</summary>
        public Action OnOpenRanked;

        /// <summary>Raised by the store button in the bottom bar.</summary>
        public Action OnOpenStore;

        /// <summary>Raised by the levels button in the bottom bar. Opens the level path.</summary>
        public Action OnOpenLevelPath;

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

            var scrim = UiKit.El("bleed menu__scrim", root);
            scrim.pickingMode = PickingMode.Ignore;
            var glow = UiKit.El("bleed menu__glow", root);
            glow.pickingMode = PickingMode.Ignore;
            glow.style.backgroundImage = new StyleBackground(TopGlow());

            BuildHeader();

            logo = UiKit.El("menu__logo", root);
            UiKit.Text(ArcadeTheme.GameNameA, "menu__mini f-display-semi", logo);
            UiKit.Text(ArcadeTheme.GameNameB, "menu__football f-display", logo);

            caption = UiKit.Text("CHOOSE A DIFFICULTY", "menu__caption f-body-semi", root);

            modeRow = UiKit.El("menu__cards", root);
            modeCards.Add(ModeCard("PLAYER VS PLAYER", "Play a friend beside you", UiIcon.Glyph.TwoPlayers,
                                   playerVsPlayerArt, () => ChooseMode(Mode.PlayerVsPlayer)));
            modeCards.Add(ModeCard("PLAYER VS AI", "Challenge the computer", UiIcon.Glyph.Robot,
                                   aiVsPlayerArt, () => ChooseMode(Mode.PlayerVsAi)));
            modeCards.Add(ModeCard("ONLINE", "Play over the internet", UiIcon.Glyph.Globe,
                                   onlineArt, () => ChooseMode(Mode.Online)));

            difficultyRow = UiKit.El("menu__cards", root);
            difficultyCards.Add(DifficultyCard("EASY", "Learn the table.", 1, aiVsPlayerArt, AiLevel.Easy));
            difficultyCards.Add(DifficultyCard("NORMAL", "A fair match for most players.", 2, aiVsPlayerArt, AiLevel.Normal));
            difficultyCards.Add(DifficultyCard("HARD", "Fast and sharp.", 3, aiVsPlayerArt, AiLevel.Hard));

            BuildBottomBar();

            backButton = UiKit.El("btn btn--ghost menu__back", root);
            backButton.Add(new UiIcon(UiIcon.Glyph.ChevronLeft, ArcadeTheme.Ink, 2.5f) { name = "BackIcon" });
            UiKit.Text("BACK", "btn__label f-display", backButton);
            UiKit.OnTap(backButton, ShowModes);

            BuildInviteBanner();

            UiFonts.Apply(root);
            ShowModes();
            UiKit.Show(root, false);
        }

        // ---------- build: header ----------

        private void BuildHeader()
        {
            var header = UiKit.El("menu__header", root);

            // The identity chip: avatar, name, level and XP. The whole chip opens the account screen.
            var chip = UiKit.El("menu__profile", header);
            UiKit.OnTap(chip, () => OnOpenProfile?.Invoke());
            var avatar = UiKit.El("avatar", chip);
            avatarInitial = UiKit.Text("?", "avatar__initial f-display", avatar);

            var info = UiKit.El("menu__profile-info", chip);
            var nameRow = UiKit.El("row", info);
            nameLabel = UiKit.Text("—", "menu__name f-display-semi", nameRow);
            levelLabel = UiKit.Text("LV 1", "level-pill f-display", nameRow);
            var xpRow = UiKit.El("row menu__xp-row", info);
            var bar = UiKit.El("bar menu__xp-bar", xpRow);
            xpFill = UiKit.El("bar__fill", bar);
            xpLabel = UiKit.Text(string.Empty, "menu__xp-label", xpRow);

            var coins = UiKit.El("chip", header);
            coins.Add(new UiIcon(UiIcon.Glyph.Coin, ArcadeTheme.Gold, 2f));
            coinLabel = UiKit.Text("0", "chip__value f-display", coins);

            UiKit.El("grow", header);

            var ranked = UiKit.El("chip chip--tap", header);
            ranked.Add(new UiIcon(UiIcon.Glyph.Trophy, ArcadeTheme.Gold, 2f));
            rankedLabel = UiKit.Text("—", "chip__value f-display", ranked);
            UiKit.OnTap(ranked, () => OnOpenRanked?.Invoke());
        }

        // ---------- build: cards ----------

        private VisualElement ModeCard(string title, string subtitle, UiIcon.Glyph glyph, Sprite art, Action onTap)
        {
            var card = UiKit.El("card", modeRow);
            UiKit.OnTap(card, onTap);

            var picture = UiKit.El("card__picture", card);
            if (art != null) picture.style.backgroundImage = new StyleBackground(art);
            UiKit.El("card__shade", picture);

            var titleRow = UiKit.El("row card__title-row", card);
            var tile = UiKit.El("card__icon-tile", titleRow);
            tile.Add(new UiIcon(glyph, ArcadeTheme.BlueSoft, 2f));
            var words = UiKit.El("col card__words", titleRow);
            UiKit.Text(title, "card__title f-display", words);
            UiKit.Text(subtitle, "card__subtitle", words);

            PlayBar(card);
            return card;
        }

        private VisualElement DifficultyCard(string title, string line, int strength, Sprite art, AiLevel level)
        {
            var card = UiKit.El("card card--tall", difficultyRow);
            UiKit.OnTap(card, () => StartAi(level));

            var picture = UiKit.El("card__picture", card);
            if (art != null) picture.style.backgroundImage = new StyleBackground(art);
            UiKit.El("card__shade", picture);

            var nameRow = UiKit.El("row card__title-row", card);
            UiKit.Text(title, "card__name f-display grow", nameRow);
            var meter = UiKit.El("meter", nameRow);
            for (int i = 0; i < 3; i++)
            {
                var b = UiKit.El("meter__bar meter__bar--" + (i + 1), meter);
                if (i < strength) b.AddToClassList("is-lit");
            }

            UiKit.Text(line, "card__line", card);
            PlayBar(card);
            return card;
        }

        /// <summary>The PLAY strip along a card's foot, with the shine that runs on the gold one.</summary>
        private static void PlayBar(VisualElement card)
        {
            var play = UiKit.El("card__play clip", card);
            UiKit.Text("PLAY", "card__play-label f-display", play);
            play.Add(new UiIcon(UiIcon.Glyph.Play, ArcadeTheme.Ink, 2f) { name = "PlayIcon" });
            UiKit.El("shine", play).name = "Shine";
        }

        /// <summary>Makes <paramref name="index"/> the gold card of <paramref name="cards"/>.</summary>
        private static void Highlight(List<VisualElement> cards, int index)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                // No card is picked out any more: all PLAY strips look alike, same size, no gold.
                cards[i].EnableInClassList("is-selected", false);
            }
        }

        // ---------- build: bottom bar ----------

        private void BuildBottomBar()
        {
            // Centred by a full-width wrapper rather than by a translate, so the entrance animation
            // (which uses translate) can run on the bar itself.
            var wrap = UiKit.El("nav-wrap", root);
            wrap.pickingMode = PickingMode.Ignore;
            nav = UiKit.El("nav", wrap);
            NavItem(UiIcon.Glyph.Person, "FRIENDS", () => OnOpenFriends?.Invoke());
            NavItem(UiIcon.Glyph.Sliders, "SETTINGS", () => OnOpenSettings?.Invoke());
            NavItem(UiIcon.Glyph.Store, "STORE", () => OnOpenStore?.Invoke());
            pathPip = NavItem(UiIcon.Glyph.Levels, "LEVELS", () => OnOpenLevelPath?.Invoke(), withPip: true);
            questPip = NavItem(UiIcon.Glyph.Quests, "QUESTS", () => OnOpenQuests?.Invoke(), withPip: true);
        }

        private Label NavItem(UiIcon.Glyph glyph, string label, Action onTap, bool withPip = false)
        {
            var item = UiKit.El("nav__item", nav);
            UiKit.OnTap(item, onTap);
            item.Add(new UiIcon(glyph, ArcadeTheme.InkMuted, 2f));
            UiKit.Text(label, "nav__label f-display-semi", item);
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
            inviteBanner = UiKit.El("invite", root);
            inviteText = UiKit.Text(string.Empty, "invite__text f-body-semi grow", inviteBanner);
            UiKit.Button("PLAY", "gold", AcceptInvite, inviteBanner, "invite__btn");
            UiKit.Button("LATER", "ghost", DeclineInvite, inviteBanner, "invite__btn");
            UiKit.Show(inviteBanner, false);
        }

        // ---------- behaviour ----------

        private void ChooseMode(Mode mode)
        {
            PlayerPrefs.SetInt(LastModeKey, (int)mode);
            Highlight(modeCards, (int)mode);

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
            Highlight(modeCards, Mathf.Clamp(PlayerPrefs.GetInt(LastModeKey, (int)Mode.PlayerVsAi), 0, 2));
            if (IsOpen) EnterCards(modeCards);
        }

        private void ShowDifficulty()
        {
            UiKit.Show(modeRow, false);
            UiKit.Show(difficultyRow, true);
            UiKit.Show(backButton, true);
            UiKit.Show(caption, true);
            // Gold on the level the player last chose — the one they are most likely to want again.
            Highlight(difficultyCards, (int)GameAudio.Difficulty);
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
            nameLabel.text = string.IsNullOrWhiteSpace(name) ? "—" : name;
            avatarInitial.text = string.IsNullOrWhiteSpace(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            levelLabel.text = $"LV {PlayerProgress.Level}";
            xpLabel.text = PlayerProgress.XpLabel;
            xpFill.style.width = Length.Percent(Mathf.Clamp01(PlayerProgress.XpFraction) * 100f);
        }

        private void RefreshCoins() => coinLabel.text = Wallet.Coins.ToString("N0");

        private void RefreshRanked()
        {
            LadderStanding s = Ladder.Standing;
            rankedLabel.text = s.Valid ? s.WeeklyPoints.ToString() : "—";
        }

        private void RefreshPathPip() => SetPip(pathPip, LevelPath.UnclaimedCount);
        private void RefreshQuestPip() => SetPip(questPip, DailyQuests.UnseenCount);

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
            RefreshPathPip();
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
            PlayerProgress.OnChanged -= OnProgressChanged;
            PlayerProgress.OnChanged += OnProgressChanged;
            Wallet.OnChanged -= RefreshCoins;
            Wallet.OnChanged += RefreshCoins;
            FriendsHub.OnChanged -= MarkInviteDirty;
            FriendsHub.OnChanged += MarkInviteDirty;
            Ladder.OnChanged -= RefreshRanked;
            Ladder.OnChanged += RefreshRanked;
            LevelPath.OnChanged -= RefreshPathPip;
            LevelPath.OnChanged += RefreshPathPip;
            DailyQuests.EnsureToday();
            DailyQuests.OnChanged -= RefreshQuestPip;
            DailyQuests.OnChanged += RefreshQuestPip;

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
            PlayerProgress.OnChanged -= OnProgressChanged;
            Wallet.OnChanged -= RefreshCoins;
            FriendsHub.OnChanged -= MarkInviteDirty;
            Ladder.OnChanged -= RefreshRanked;
            LevelPath.OnChanged -= RefreshPathPip;
            DailyQuests.OnChanged -= RefreshQuestPip;

            StopLoops();
            if (root != null) UiKit.Show(root, false);
        }

        private void OnDestroy() => Close();

        /// <summary>Levelling up is what creates an unclaimed path reward, so the path pip follows
        /// progression as well as the path itself.</summary>
        private void OnProgressChanged()
        {
            RefreshProfile();
            RefreshPathPip();
        }

        // ---------- ambient motion ----------

        /// <summary>
        /// The two things that move while the menu sits still: the shine sweeping the gold card's PLAY
        /// strip every few seconds, and the waiting pips breathing. Paused while the menu is closed,
        /// and off under reduced motion.
        /// </summary>
        private void StartLoops()
        {
            StopLoops();
            if (ArcadeTheme.ReducedMotion) return;

            float cycle = ArcadeTheme.TShine + ArcadeTheme.ShineRest;
            loops.Add(UiKit.Loop(root, now =>
            {
                float t = Mathf.Repeat(now, cycle);
                float p = t < ArcadeTheme.TShine ? t / ArcadeTheme.TShine : 1f;
                float k = p * p * (3f - 2f * p);
                SweepShine(modeCards, k);
                SweepShine(difficultyCards, k);

                float breath = 1f + 0.12f * (0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2f / 1.4f));
                if (pathPip != null) pathPip.style.scale = new Scale(new Vector3(breath, breath, 1f));
                if (questPip != null) questPip.style.scale = new Scale(new Vector3(breath, breath, 1f));
            }));
        }

        private static void SweepShine(List<VisualElement> cards, float k)
        {
            foreach (var card in cards)
            {
                var shine = card.Q("Shine");
                if (shine == null) continue;
                bool on = card.ClassListContains("is-selected");
                shine.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                float width = card.Q(className: "card__play")?.resolvedStyle.width ?? 300f;
                shine.style.left = Mathf.Lerp(-100f, width + 60f, k);
            }
        }

        private void StopLoops()
        {
            foreach (var l in loops) l.Pause();
            loops.Clear();
        }

        // ---------- textures ----------

        private static Texture2D topGlow;

        /// <summary>
        /// A soft blue light falling from the top centre of the screen — the one gradient the design
        /// has, which USS cannot draw. Baked once, small, and stretched to fill.
        /// </summary>
        private static Texture2D TopGlow()
        {
            if (topGlow != null) return topGlow;

            const int w = 64, h = 64;
            topGlow = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "MenuTopGlow"
            };

            Color c = ArcadeTheme.BlueFill;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Texture rows run bottom-up; the light sits at the top edge.
                    float dx = (x + 0.5f) / w - 0.5f;
                    float dy = 1f - (y + 0.5f) / h;
                    float d = Mathf.Sqrt(dx * dx * 1.4f + dy * dy * 2.2f);
                    float a = Mathf.Clamp01(1f - d / 0.75f);
                    a = a * a * 0.55f;
                    px[y * w + x] = new Color(c.r, c.g, c.b, a);
                }
            }

            topGlow.SetPixels32(px);
            topGlow.Apply(false, false);
            return topGlow;
        }
    }
}
