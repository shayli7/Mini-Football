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
        private GameObject localGroup;
        private GameObject difficultyGroup;
        private GameObject backHolder;
        private GameObject quitHolder;
        private GameObject confirmRoot;
        private Transform confirmPanel;
        private MenuButton profileButton;
        private GameObject inviteBanner;
        private TMPro.TextMeshProUGUI inviteText;
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

        /// <summary>Raised with a friend's join code when their invitation is accepted from here.</summary>
        public Action<string> OnAcceptInvite;

        public bool IsOpen => root != null && root.activeSelf;

        public void Build(Transform canvasRoot, Sprite onlineArt, Sprite localArt,
                          Sprite playerVsPlayerArt, Sprite aiVsPlayerArt)
        {
            root = UIFactory.Child(canvasRoot, "MainMenu");
            UIFactory.Stretch(UIFactory.Rt(root));
            group = root.AddComponent<CanvasGroup>();

            UIFactory.Backdrop(root.transform);

            var logo = UIFactory.LogoLockup(root.transform);
            var trt = UIFactory.Rt(logo);
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(0f, -238f);
            trt.offsetMax = new Vector2(0f, -40f);

            panel = UIFactory.Child(root.transform, "Choices").transform;
            var prt = UIFactory.Rt(panel.gameObject);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            // Must comfortably exceed two tiles plus the gap, or the layout group shrinks them back
            // down to fit and the cards never actually get bigger.
            prt.sizeDelta = new Vector2(1000f, 400f);
            // Sits the cards in the middle of the band between the logo and the footer, rather than
            // in the middle of the screen. Centring them on the screen is what left the big dead gap
            // under the title, because the logo above them is not balanced by anything below.
            prt.anchoredPosition = new Vector2(0f, -60f);

            rootGroup = BuildRow(panel, "RootGroup");
            localGroup = BuildRow(panel, "LocalGroup");

            UIFactory.Tile(rootGroup.transform, "Online", onlineArt, () => OnOpenOnline?.Invoke(),
                           badge: UIFactory.Badge.Online);
            UIFactory.Tile(rootGroup.transform, "Local", localArt, ShowLocal,
                           badge: UIFactory.Badge.Local);

            UIFactory.Tile(localGroup.transform, "Player vs Player", playerVsPlayerArt,
                           () => Choose(aiOpponent: false), badge: UIFactory.Badge.PlayerVsPlayer);
            UIFactory.Tile(localGroup.transform, "AI vs Player", aiVsPlayerArt,
                           ShowDifficulty, badge: UIFactory.Badge.AiVsPlayer);

            BuildDifficulty(aiVsPlayerArt);

            BuildBack();
            BuildQuit();
            BuildCornerIcons();
            BuildProfileButton();
            BuildInviteBanner();
            BuildQuitConfirm();

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
            layout.spacing = ArcadeTheme.Xl3;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            return row;
        }

        /// <summary>
        /// The footer: one low-emphasis action, bottom centre, directly under the cards.
        ///
        /// Both buttons live in the same slot and only one is ever up — Quit on the front screen,
        /// Back once you are a level in. Quit used to float alone in the bottom-right corner,
        /// attached to nothing, and it appeared on the mode-select screen too, where Back is the
        /// action the player actually wants and leaving the game is a mistap away.
        /// </summary>
        private static GameObject BuildFooterSlot(Transform parent, string name)
        {
            var holder = UIFactory.Child(parent, name);
            var rt = UIFactory.Rt(holder);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(260f, 58f);
            rt.anchoredPosition = new Vector2(0f, 72f);

            var layout = holder.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            return holder;
        }

        private void BuildBack()
        {
            backHolder = BuildFooterSlot(root.transform, "BackHolder");
            UIFactory.Button(backHolder.transform, "Back", MenuButton.Variant.Ghost, GoBack, 58f);
        }

        private void BuildQuit()
        {
            quitHolder = BuildFooterSlot(root.transform, "QuitHolder");
            UIFactory.Button(quitHolder.transform, "Quit", MenuButton.Variant.Ghost, ShowQuitConfirm, 58f);
        }

        /// <summary>
        /// The avatar, top left — the profile screen's front door.
        ///
        /// Above the friends and settings icons rather than opposite them, which puts everything
        /// belonging to the player down one edge: who you are, who you know, how you like it. The
        /// right-hand side is left to the game itself.
        /// </summary>
        private void BuildProfileButton()
        {
            profileButton = UIFactory.AvatarButton(root.transform, () => OnOpenProfile?.Invoke());

            var rt = UIFactory.Rt(profileButton.gameObject);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(32f, -32f);

            // The name is fetched asynchronously and can change while this screen is up — renaming
            // happens on the very screen this button opens — so the letter follows the account rather
            // than being stamped once at build time.
            PlayerAccount.OnChanged += RefreshAvatar;
            RefreshAvatar();
        }

        private void RefreshAvatar()
        {
            if (profileButton == null || profileButton.label == null) return;

            string name = PlayerAccount.DisplayName;
            profileButton.label.text = string.IsNullOrWhiteSpace(name)
                ? "?"
                : name.Substring(0, 1).ToUpperInvariant();
        }

        private void OnDestroy()
        {
            PlayerAccount.OnChanged -= RefreshAvatar;
            FriendsHub.OnChanged -= MarkInviteDirty;
        }

        /// <summary>The corner icons: the player's own things, bottom left.</summary>
        private void BuildCornerIcons()
        {
            const float size = 64f;
            const float margin = 32f;

            var friends = UIFactory.IconButton(root.transform, "Friends", UIFactory.Icon.Person,
                                               MenuButton.Variant.Neutral,
                                               () => OnOpenFriends?.Invoke(), size);
            var frt = UIFactory.Rt(friends.gameObject);
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0f);
            frt.pivot = new Vector2(0f, 0f);
            frt.anchoredPosition = new Vector2(margin, margin);

            var settings = UIFactory.IconButton(root.transform, "Settings", UIFactory.Icon.Sliders,
                                                MenuButton.Variant.Neutral,
                                                () => OnOpenSettings?.Invoke(), size);
            var srt = UIFactory.Rt(settings.gameObject);
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 0f);
            srt.pivot = new Vector2(0f, 0f);
            srt.anchoredPosition = new Vector2(margin + size + ArcadeTheme.Md, margin);
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
        /// screen to push into — the logo runs to 238 and the cards begin around 310 — so a full-width
        /// bar would have to sit on top of one of them. The right-hand corner is the one part of the
        /// menu genuinely holding nothing: the avatar is top left, the icons are bottom left, and the
        /// logo's text is centred.
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
            rt.anchoredPosition = new Vector2(-32f, -32f);

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

        /// <summary>
        /// Asks before quitting. There is no undoing a closed app, and Quit sits in the footer where
        /// Back sits one screen in — so the same tap in the same place means two very different
        /// things depending on where you are.
        /// </summary>
        private void BuildQuitConfirm()
        {
            confirmRoot = UIFactory.Child(root.transform, "QuitConfirm");
            var dim = confirmRoot.AddComponent<Image>();
            dim.color = ArcadeTheme.BgDeep.WithAlpha(0.78f);
            dim.raycastTarget = true;
            UIFactory.Stretch(UIFactory.Rt(confirmRoot), -ArcadeTheme.Bleed);

            var panel = UIFactory.Panel(confirmRoot.transform, "ConfirmPanel");
            confirmPanel = panel.transform;
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(420f, 280f);

            var border = panel.transform.Find("Border");
            if (border != null) border.GetComponent<Image>().color = ArcadeTheme.Red;

            var fill = panel.transform.Find("Fill");
            var v = fill.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.MiddleCenter;
            v.spacing = ArcadeTheme.Md;
            v.padding = new RectOffset(28, 28, 28, 28);
            v.childForceExpandWidth = true;
            v.childControlWidth = true;
            v.childControlHeight = true;

            var title = UIFactory.Text(fill, "QUIT GAME?", ArcadeTheme.FsTitle * 0.6f, ArcadeTheme.Ink,
                                       display: true, bold: true, upper: true, tracking: 4f);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;

            var body = UIFactory.Text(fill, "you will close mini football", ArcadeTheme.FsCaption,
                                      ArcadeTheme.InkMuted, display: false, bold: true, upper: true,
                                      tracking: 4f);
            body.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            // Stay first and primary, matching the pause menu's leave confirmation: the safe answer
            // should be the one that looks like the default.
            UIFactory.Button(fill, "Stay", MenuButton.Variant.Primary, HideQuitConfirm);
            UIFactory.Button(fill, "Quit", MenuButton.Variant.Danger, Quit);

            confirmRoot.SetActive(false);
        }

        private void ShowQuitConfirm()
        {
            if (confirmRoot == null) return;
            confirmRoot.SetActive(true);
            confirmRoot.transform.SetAsLastSibling();
            StartCoroutine(UITween.ScaleTo(confirmPanel, Vector3.one * ArcadeTheme.MenuFrom,
                                           Vector3.one, ArcadeTheme.TNormal, ArcadeTheme.EaseOutBack));
        }

        private void HideQuitConfirm()
        {
            if (confirmRoot != null) confirmRoot.SetActive(false);
        }

        // ---------- state ----------

        public void Open()
        {
            if (root == null)
            {
                return;
            }

            ShowRoot();
            RefreshAvatar();

            // Removed first, so reopening cannot stack a second handler onto a static event that
            // outlives this panel.
            FriendsHub.OnChanged -= MarkInviteDirty;
            FriendsHub.OnChanged += MarkInviteDirty;
            RefreshInvite();

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

            var activeRow = rootGroup != null && rootGroup.activeSelf ? rootGroup : localGroup;
            if (activeRow != null)
            {
                yield return UITween.Stagger(this, activeRow.transform, ArcadeTheme.Stagger * 2f,
                                             ArcadeTheme.TSlow);
            }

            var footer = quitHolder != null && quitHolder.activeSelf ? quitHolder : backHolder;
            if (footer != null) StartCoroutine(UITween.PopIn(footer.transform, ArcadeTheme.TNormal));

            anim = null;
        }

        private void ShowRoot()
        {
            if (rootGroup == null)
            {
                return;
            }

            rootGroup.SetActive(true);
            localGroup.SetActive(false);
            if (difficultyGroup != null) difficultyGroup.SetActive(false);
            if (backHolder != null) backHolder.SetActive(false);
            if (quitHolder != null) quitHolder.SetActive(true);
            RestageOnSwap(rootGroup, quitHolder);
        }

        private void ShowLocal()
        {
            rootGroup.SetActive(false);
            localGroup.SetActive(true);
            if (difficultyGroup != null) difficultyGroup.SetActive(false);
            if (backHolder != null) backHolder.SetActive(true);
            if (quitHolder != null) quitHolder.SetActive(false);
            RestageOnSwap(localGroup, backHolder);
        }

        private void ShowDifficulty()
        {
            rootGroup.SetActive(false);
            localGroup.SetActive(false);
            if (difficultyGroup != null) difficultyGroup.SetActive(true);
            if (backHolder != null) backHolder.SetActive(true);
            if (quitHolder != null) quitHolder.SetActive(false);
            RestageOnSwap(difficultyGroup, backHolder);
        }

        /// <summary>
        /// Back steps one screen rather than always jumping to the root, now that Local leads on to
        /// the difficulty step. Sending the player all the way home from there would make choosing
        /// a difficulty feel like a trap.
        /// </summary>
        private void GoBack()
        {
            if (difficultyGroup != null && difficultyGroup.activeSelf)
            {
                ShowLocal();
                return;
            }

            ShowRoot();
        }

        /// <summary>
        /// Re-runs the card entrance when the two groups swap, so stepping into Local and back out
        /// feels like the same menu rebuilding rather than a hard cut between two screens.
        ///
        /// Skipped while the menu is closed: Build and Open both call ShowRoot before anything is on
        /// screen, and animating there would be a tween nobody sees, racing the one Open starts.
        /// </summary>
        private void RestageOnSwap(GameObject row, GameObject footer)
        {
            if (root == null || !root.activeSelf || anim != null) return;

            HideQuitConfirm();

            if (row != null) StartCoroutine(UITween.Stagger(this, row.transform, ArcadeTheme.Stagger, ArcadeTheme.TNormal));
            if (footer != null) StartCoroutine(UITween.PopIn(footer.transform, ArcadeTheme.TNormal));
        }

        /// <summary>Not named Start: Unity reserves that, and an overload here invites confusion.</summary>
        private void Choose(bool aiOpponent)
        {
            OnStartLocal?.Invoke(aiOpponent);
        }

        private void Quit()
        {
            // Restore time before leaving: in the editor play simply stops, and a build that is
            // suspended rather than killed would otherwise resume frozen.
            Time.timeScale = 1f;
            AudioListener.pause = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
