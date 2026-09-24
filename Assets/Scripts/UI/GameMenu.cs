using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TableFootball.UI
{
    /// <summary>
    /// Owns the pause access and the menu overlay: the top-right pause button, the main/pause menu
    /// (Resume / Settings / Quit) and the Settings panel (Master + SFX volume, AI difficulty).
    ///
    /// Pausing is real: <c>Time.timeScale = 0</c> and <c>AudioListener.pause = true</c> while the menu
    /// is open. Because MatchManager and TeamAI both idle on their own timers, that alone freezes play;
    /// all menu animation runs on unscaled time so the UI still moves while the world is stopped.
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        private MatchManager match;
        private Transform canvasRoot;

        private GameObject pauseButton;
        private GameObject overlay;
        private CanvasGroup overlayCg;
        private Transform menuPanel;
        private GameObject mainGroup;
        private GameObject settingsGroup;
        private GameObject confirmGroup;
        private MenuButton resumeButton;
        private MenuButton backButton;
        private MenuButton stayButton;
        private TextMeshProUGUI confirmBody;

        private bool isOpen;
        private bool standalone;
        private Coroutine overlayRoutine;

        /// <summary>
        /// Whether the pause icon is allowed to show at all, independent of whether the overlay it
        /// opens is up. False by default: before a match exists there is nothing to pause, and the
        /// overlay's own "Resume" / "Main Menu" make no sense on a front-end screen. See
        /// <see cref="SetPauseButtonVisible"/>.
        /// </summary>
        private bool pauseButtonAllowed;

        public bool IsOpen => isOpen;

        /// <summary>
        /// Set by GameFlow when a match starts. Only changes what the leave confirmation warns about:
        /// online, walking out hands the match to the opponent; locally it merely ends it.
        /// </summary>
        public bool OnlineMatch { get; set; }

        public void Build(Transform root, MatchManager matchManager, bool openOnStart)
        {
            match = matchManager;
            canvasRoot = root;

            BuildPauseButton();
            BuildOverlay();
            SetOpen(openOnStart, instant: true);
        }

        private void Update()
        {
            if (BackPressedThisFrame())
            {
                // Back steps out of whichever sub-screen is up before it closes the menu. Escape on
                // the confirmation must mean "no" — closing the menu outright there would resume a
                // match the player was in the middle of deciding to leave.
                if (isOpen && confirmGroup.activeSelf) HideLeaveConfirm();
                else if (isOpen && settingsGroup.activeSelf) HideSettings();
                else Toggle();
            }
        }

        private bool BackPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            bool esc = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool start = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            return esc || start;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        // ---------- public control ----------

        public void Toggle() => SetOpen(!isOpen);
        public void Open() => SetOpen(true);
        public void Resume() => SetOpen(false);

        /// <summary>
        /// Shows or hides the pause icon itself, separate from opening/closing the overlay it
        /// triggers. GameFlow calls this true only once a match is actually live (alongside
        /// <c>hud.SetVisible(true)</c>) and false the moment play returns to any front-end screen
        /// (alongside every <c>hud.SetVisible(false)</c> that follows from <c>OpenMenu</c>) — the same
        /// lifecycle the score HUD already follows, since a pause icon means nothing without a match
        /// under it.
        /// </summary>
        public void SetPauseButtonVisible(bool visible)
        {
            pauseButtonAllowed = visible;
            if (!standalone && pauseButton != null)
            {
                pauseButton.SetActive(!isOpen && pauseButtonAllowed);
            }
        }

        /// <summary>
        /// Set by GameFlow. Leaving a match returns to the main menu instead of closing the game:
        /// the main menu carries the real Quit, so a mistap mid-match costs a match rather than the
        /// whole session.
        /// </summary>
        public Action OnLeaveMatch;

        public void Quit()
        {
            if (OnLeaveMatch != null)
            {
                // GameFlow takes over, including restoring timeScale behind the loading curtain.
                OnLeaveMatch.Invoke();
                return;
            }

            // No flow wired (the pause menu used on its own): fall back to closing the game.
            Time.timeScale = 1f;
            AudioListener.pause = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Opens straight into Settings from the main menu, without pausing anything.
        ///
        /// The pause path owns timeScale and AudioListener while it is up, which is right mid-match
        /// and wrong here: the front end is already frozen by GameFlow, and closing this would hand
        /// back a timeScale of 1 and set the table running behind the menu.
        /// </summary>
        public void OpenSettingsStandalone()
        {
            standalone = true;
            SetOpen(true);
            ShowSettings();
        }

        private void SetOpen(bool open, bool instant = false)
        {
            isOpen = open;

            if (!standalone)
            {
                Time.timeScale = open ? 0f : 1f;
                AudioListener.pause = open;
                if (pauseButton != null) pauseButton.SetActive(!open && pauseButtonAllowed);
            }

            overlayCg.blocksRaycasts = open;
            overlayCg.interactable = open;

            if (open)
            {
                overlay.SetActive(true);
                overlay.transform.SetAsLastSibling();
                ShowMain();
                if (overlayRoutine != null) StopCoroutine(overlayRoutine);
                overlayRoutine = StartCoroutine(OpenAnim(instant));
            }
            else
            {
                if (overlayRoutine != null) StopCoroutine(overlayRoutine);
                if (instant) { overlayCg.alpha = 0f; overlay.SetActive(false); }
                else overlayRoutine = StartCoroutine(CloseAnim());
            }
        }

        private IEnumerator OpenAnim(bool instant)
        {
            float dur = instant ? 0f : ArcadeTheme.TNormal;
            StartCoroutine(UITween.ScaleTo(menuPanel, Vector3.one * ArcadeTheme.MenuFrom, Vector3.one, dur, ArcadeTheme.EaseOut));
            yield return UITween.Fade(overlayCg, 0f, 1f, dur, ArcadeTheme.EaseOut);
            // focus the primary action for keyboard / gamepad
            if (EventSystem.current != null && resumeButton != null)
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        private IEnumerator CloseAnim()
        {
            // exit is subtler than enter: fade only, faster ease-in (per motion-principles)
            yield return UITween.Fade(overlayCg, overlayCg.alpha, 0f, ArcadeTheme.TFast, ArcadeTheme.EaseIn);
            overlay.SetActive(false);
        }

        private void ShowMain()
        {
            mainGroup.SetActive(true);
            settingsGroup.SetActive(false);
            confirmGroup.SetActive(false);
        }

        public void ShowSettings()
        {
            mainGroup.SetActive(false);
            settingsGroup.SetActive(true);
            confirmGroup.SetActive(false);
            if (EventSystem.current != null && backButton != null)
                EventSystem.current.SetSelectedGameObject(backButton.gameObject);
        }

        public void HideSettings()
        {
            // Opened from the main menu there is no pause screen to go back to, so Back closes the
            // overlay outright — and clears the flag, so the next real pause behaves normally again.
            if (standalone)
            {
                SetOpen(false);
                standalone = false;
                return;
            }

            ShowMain();
            if (EventSystem.current != null && resumeButton != null)
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        /// <summary>
        /// Asks before leaving. Worth the extra tap: Main Menu sits directly under Settings in a menu
        /// reached mid-match, and online it does not merely end the match — it hands it over.
        /// </summary>
        public void ShowLeaveConfirm()
        {
            mainGroup.SetActive(false);
            settingsGroup.SetActive(false);
            confirmGroup.SetActive(true);

            if (confirmBody != null)
            {
                confirmBody.text = OnlineMatch
                    ? "YOUR OPPONENT WINS"
                    : "THE MATCH WILL BE LOST";
            }

            // Focus Stay, not Leave. A confirmation that arrives with the destructive option already
            // selected is answered by the same reflex that opened it.
            if (EventSystem.current != null && stayButton != null)
                EventSystem.current.SetSelectedGameObject(stayButton.gameObject);
        }

        public void HideLeaveConfirm()
        {
            ShowMain();
            if (EventSystem.current != null && resumeButton != null)
                EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        }

        // ---------- build ----------

        private void BuildPauseButton()
        {
            var root = UIFactory.Child(canvasRoot, "PauseButton");
            var rt = UIFactory.Rt(root);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            // Level with the score strip and the same height, so the two read as one band across the
            // top of the screen rather than two objects parked over the table.
            rt.anchoredPosition = new Vector2(-ArcadeTheme.Lg, -ArcadeTheme.Sm);
            rt.sizeDelta = new Vector2(ScoreHud.StripHeight, ScoreHud.StripHeight);

            var glowGo = UIFactory.Child(root.transform, "Glow");
            var glow = UIFactory.GlowImage(glowGo, ArcadeTheme.RadMd, 24f, ArcadeTheme.BlueSoft.WithAlpha(0f));
            UIFactory.Stretch(UIFactory.Rt(glowGo), -14);

            var borderGo = UIFactory.Child(root.transform, "Border");
            var border = UIFactory.RoundedImage(borderGo, ArcadeTheme.RadMd, ArcadeTheme.Line, false);
            UIFactory.Stretch(UIFactory.Rt(borderGo), 0);

            var fillGo = UIFactory.Child(root.transform, "Fill");
            var fill = UIFactory.RoundedImage(fillGo, ArcadeTheme.RadMd, ArcadeTheme.BgPanel, true);
            UIFactory.Stretch(UIFactory.Rt(fillGo), 1.5f);

            // two-bar pause glyph
            var glyph = UIFactory.Child(root.transform, "Glyph");
            var grow = glyph.AddComponent<HorizontalLayoutGroup>();
            grow.childAlignment = TextAnchor.MiddleCenter; grow.spacing = 5f;
            grow.childControlWidth = false; grow.childControlHeight = false;
            UIFactory.Stretch(UIFactory.Rt(glyph), 0);
            for (int i = 0; i < 2; i++)
            {
                var bar = UIFactory.Child(glyph.transform, "Bar");
                UIFactory.RoundedImage(bar, 2, ArcadeTheme.Ink, false);
                UIFactory.Rt(bar).sizeDelta = new Vector2(4f, 16f);
                bar.AddComponent<LayoutElement>();
            }

            var btn = root.AddComponent<MenuButton>();
            btn.fill = fill; btn.border = border; btn.glow = glow; btn.label = null;
            btn.targetGraphic = fill;
            // Blue, not gold: pausing is always available but never the thing the player should do
            // next, and gold on screen for the whole match wore the highlight out.
            btn.Configure(MenuButton.Variant.Ghost);
            btn.onClick.AddListener(Open);

            pauseButton = root;
        }

        private void BuildOverlay()
        {
            overlay = UIFactory.Child(canvasRoot, "MenuOverlay");
            UIFactory.Stretch(UIFactory.Rt(overlay), 0);
            // The same shared dim the account/friends/online screens now use, so the settings overlay
            // and those screens read as one design language rather than two. See UIFactory.ScrimDim.
            UIFactory.ScrimDim(overlay.transform);
            overlayCg = overlay.AddComponent<CanvasGroup>();

            var panel = UIFactory.Panel(overlay.transform, "MenuPanel");
            menuPanel = panel.transform;
            var prt = UIFactory.Rt(panel);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(400f, 500f);

            var fill = panel.transform.Find("Fill");
            var vlg = fill.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.spacing = ArcadeTheme.Md;
            vlg.padding = new RectOffset(28, 28, 28, 28);
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true; vlg.childControlHeight = true;

            BuildMainGroup(fill);
            BuildSettingsGroup(fill);
            BuildConfirmGroup(fill);
        }

        private VerticalLayoutGroup Group(Transform parent, string name)
        {
            var go = UIFactory.Child(parent, name);
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.UpperCenter;
            v.spacing = ArcadeTheme.Md;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            v.childControlWidth = true; v.childControlHeight = true;
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            return v;
        }

        private void BuildMainGroup(Transform parent)
        {
            var group = Group(parent, "MainGroup");
            mainGroup = group.gameObject;

            // The same lockup the main menu uses, at panel scale and without the mark — the panel is
            // 400 wide and the ball-and-rods would crowd it. This replaced a hand-written
            // "<color=#FF3355>FOOS</color>…" string, the only place in the UI that bypassed the theme.
            var logo = UIFactory.LogoLockup(group.transform, scale: 0.62f, withMark: false);
            logo.AddComponent<LayoutElement>().preferredHeight = 76f;

            var tagline = UIFactory.Text(group.transform, "PAUSED", ArcadeTheme.FsBody, ArcadeTheme.Ink,
                                         display: false, bold: true, upper: true, tracking: 18f);
            tagline.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            Spacer(group.transform, 8f);
            resumeButton = UIFactory.Button(group.transform, "Resume", MenuButton.Variant.Primary, Resume);
            UIFactory.Button(group.transform, "Settings", MenuButton.Variant.Ghost, ShowSettings);
            UIFactory.Button(group.transform, "Main Menu", MenuButton.Variant.Danger, ShowLeaveConfirm);
        }

        private void BuildConfirmGroup(Transform parent)
        {
            var group = Group(parent, "ConfirmGroup");
            confirmGroup = group.gameObject;

            var title = UIFactory.Text(group.transform, "LEAVE MATCH?", ArcadeTheme.FsTitle * 0.6f,
                                       ArcadeTheme.Ink, display: true, bold: true, upper: true,
                                       tracking: 4f);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

            // Filled in by ShowLeaveConfirm, which is the only place that knows the mode.
            confirmBody = UIFactory.Text(group.transform, string.Empty, ArcadeTheme.FsBody,
                                         ArcadeTheme.Gold, display: false, bold: true, upper: true,
                                         tracking: 4f);
            confirmBody.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            Spacer(group.transform, 12f);

            // Stay is the primary and comes first: the safe answer should be the one under the thumb
            // and the one that looks like the default.
            stayButton = UIFactory.Button(group.transform, "Stay", MenuButton.Variant.Primary,
                                          HideLeaveConfirm);
            UIFactory.Button(group.transform, "Leave", MenuButton.Variant.Danger, Quit);
        }

        private void BuildSettingsGroup(Transform parent)
        {
            var group = Group(parent, "SettingsGroup");
            settingsGroup = group.gameObject;

            var title = UIFactory.Text(group.transform, "SETTINGS", ArcadeTheme.FsTitle * 0.6f, ArcadeTheme.Ink,
                                       display: true, bold: true, upper: true, tracking: 6f);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            Spacer(group.transform, 4f);

            SettingLabel(group.transform, "MUSIC VOLUME");
            UIFactory.Slider(group.transform, GameAudio.Music, v => GameAudio.Music = v);

            SettingLabel(group.transform, "SFX VOLUME");
            UIFactory.Slider(group.transform, GameAudio.Sfx, v => GameAudio.Sfx = v);

            SettingLabel(group.transform, "AI DIFFICULTY");
            UIFactory.Segmented(group.transform, new[] { "EASY", "NORMAL", "HARD" }, (int)GameAudio.Difficulty,
                                i => GameAudio.Difficulty = (AiLevel)i);

            Spacer(group.transform, 12f);
            backButton = UIFactory.Button(group.transform, "Back", MenuButton.Variant.Ghost, HideSettings, 54f);
        }

        private void SettingLabel(Transform parent, string text)
        {
            var t = UIFactory.Text(parent, text, ArcadeTheme.FsCaption, ArcadeTheme.InkMuted,
                                   display: false, bold: true, upper: true, tracking: 14f,
                                   align: TextAlignmentOptions.Left);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        }

        private static void Spacer(Transform parent, float height) => UIFactory.Spacer(parent, height);
    }
}
