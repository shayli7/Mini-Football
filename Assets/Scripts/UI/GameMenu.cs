using System;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TableFootball.UI
{
    /// <summary>
    /// Owns the pause access and the menu overlay: the top-right pause button, the pause menu
    /// (Resume / Settings / Main Menu), the leave confirmation, and the Settings panel (music and
    /// effects volume, AI difficulty).
    ///
    /// The overlay is drawn by UI Toolkit (<c>Resources/UI/Styles/GameMenu.uss</c>); the pause button
    /// is still uGUI, beside the score strip it belongs with, and moves when the match HUD does.
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

        private VisualElement root;
        private VisualElement dim;
        private VisualElement panel;
        private VisualElement mainGroup;
        private VisualElement settingsGroup;
        private VisualElement confirmGroup;
        private Label confirmBody;
        private VolumeSlider musicSlider;
        private VolumeSlider sfxSlider;
        private Label musicValue;
        private Label sfxValue;
        private readonly VisualElement[] difficultyItems = new VisualElement[3];
        private IVisualElementScheduledItem tween;

        private bool isOpen;
        private bool standalone;

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
                if (isOpen && UiKit.IsShown(confirmGroup)) HideLeaveConfirm();
                else if (isOpen && UiKit.IsShown(settingsGroup)) HideSettings();
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

        /// <summary>
        /// Raised when Settings, opened straight from the main menu, is closed. The overlay and the
        /// main menu are both UI Toolkit now, so the menu can stay up underneath it; this is kept for
        /// any caller that wants to know when the player is back.
        /// </summary>
        public Action OnStandaloneClosed;

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

            if (open)
            {
                ShowMain();
                root.style.opacity = 1f;
                UiKit.Show(root, true);
                // Over whichever UI Toolkit screen is up — the main menu, when Settings is opened
                // from it.
                root.BringToFront();

                // Pops in: the dim fades, the panel scales up from a touch smaller.
                Animate(instant ? 0f : ArcadeTheme.TNormal, t =>
                {
                    float k = ArcadeTheme.EaseOut(t);
                    dim.style.opacity = k;
                    panel.style.opacity = k;
                    float s = Mathf.Lerp(ArcadeTheme.MenuFrom, 1f, k);
                    panel.style.scale = new Scale(new Vector2(s, s));
                });
            }
            else
            {
                // Exit is subtler than enter: a quick fade, no movement.
                float from = root.resolvedStyle.opacity;
                Animate(instant ? 0f : ArcadeTheme.TFast,
                        t => root.style.opacity = Mathf.Lerp(from, 0f, ArcadeTheme.EaseIn(t)),
                        () => UiKit.Show(root, false));
            }
        }

        private void ShowMain()
        {
            UiKit.Show(mainGroup, true);
            UiKit.Show(settingsGroup, false);
            UiKit.Show(confirmGroup, false);
        }

        public void ShowSettings()
        {
            // Read back every time: the difficulty can also be changed from the main menu's cards.
            RefreshSettings();
            UiKit.Show(mainGroup, false);
            UiKit.Show(settingsGroup, true);
            UiKit.Show(confirmGroup, false);
        }

        public void HideSettings()
        {
            // Opened from the main menu there is no pause screen to go back to, so Back closes the
            // overlay outright — and clears the flag, so the next real pause behaves normally again.
            if (standalone)
            {
                SetOpen(false);
                standalone = false;
                OnStandaloneClosed?.Invoke();
                return;
            }

            ShowMain();
        }

        /// <summary>
        /// Asks before leaving. Worth the extra tap: Main Menu sits directly under Settings in a menu
        /// reached mid-match, and online it does not merely end the match — it hands it over.
        /// </summary>
        public void ShowLeaveConfirm()
        {
            UiKit.Show(mainGroup, false);
            UiKit.Show(settingsGroup, false);
            UiKit.Show(confirmGroup, true);

            confirmBody.text = OnlineMatch ? "YOUR OPPONENT WINS" : "THE MATCH WILL BE LOST";
        }

        public void HideLeaveConfirm() => ShowMain();

        // ---------- build: pause button (uGUI, with the score strip) ----------

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
            var grow = glyph.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            grow.childAlignment = TextAnchor.MiddleCenter; grow.spacing = 5f;
            grow.childControlWidth = false; grow.childControlHeight = false;
            UIFactory.Stretch(UIFactory.Rt(glyph), 0);
            for (int i = 0; i < 2; i++)
            {
                var bar = UIFactory.Child(glyph.transform, "Bar");
                UIFactory.RoundedImage(bar, 2, ArcadeTheme.Ink, false);
                UIFactory.Rt(bar).sizeDelta = new Vector2(4f, 16f);
                bar.AddComponent<UnityEngine.UI.LayoutElement>();
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

        // ---------- build: overlay (UI Toolkit) ----------

        private void BuildOverlay()
        {
            root = UiKit.El("screen gm", UiToolkitHost.Root, "GameMenu");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/GameMenu");
            if (sheet != null) root.styleSheets.Add(sheet);
            // Blocks the table and the screen beneath while it is up.
            root.pickingMode = PickingMode.Position;

            dim = UiKit.El("bleed gm__dim", root);
            dim.pickingMode = PickingMode.Ignore;

            panel = UiKit.El("panel gm__panel", root);

            BuildMainGroup();
            BuildSettingsGroup();
            BuildConfirmGroup();

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        private void BuildMainGroup()
        {
            mainGroup = UiKit.El("gm__group", panel);

            var logo = UiKit.El("gm__logo", mainGroup);
            UiKit.Text(ArcadeTheme.GameNameA, "gm__mini f-display-semi", logo);
            UiKit.Text(ArcadeTheme.GameNameB, "gm__football f-display", logo);
            UiKit.Text("PAUSED", "gm__paused f-body-semi", mainGroup);

            var buttons = UiKit.El("gm__buttons", mainGroup);
            UiKit.Button("RESUME", "gold", Resume, buttons);
            UiKit.Button("SETTINGS", "blue", ShowSettings, buttons);
            UiKit.Button("MAIN MENU", "danger", ShowLeaveConfirm, buttons);
        }

        private void BuildConfirmGroup()
        {
            confirmGroup = UiKit.El("gm__group", panel);

            UiKit.Text("LEAVE MATCH?", "gm__title gm__title--center f-display", confirmGroup);
            // Filled in by ShowLeaveConfirm, which is the only place that knows the mode.
            confirmBody = UiKit.Text(string.Empty, "gm__warning f-body-semi", confirmGroup);

            // Stay is the primary and comes first: the safe answer should be the one under the thumb
            // and the one that looks like the default.
            var buttons = UiKit.El("gm__buttons", confirmGroup);
            UiKit.Button("STAY", "gold", HideLeaveConfirm, buttons);
            UiKit.Button("LEAVE", "danger", Quit, buttons);
        }

        private void BuildSettingsGroup()
        {
            settingsGroup = UiKit.El("gm__group gm__settings", panel);

            // Title with a close button beside it, so the way out of a pop-up is where pop-ups keep it.
            var head = UiKit.El("gm__head", settingsGroup);
            UiKit.Text("SETTINGS", "gm__title f-display", head);
            var close = UiKit.El("icon-btn gm__close", head);
            close.Add(new UiIcon(UiIcon.Glyph.Close, ArcadeTheme.Ink, 2.5f));
            UiKit.OnTap(close, HideSettings);

            musicSlider = VolumeBlock(UiIcon.Glyph.Music, "MUSIC", out musicValue, v =>
            {
                GameAudio.Music = v;
                musicValue.text = Percent(v);
            });

            sfxSlider = VolumeBlock(UiIcon.Glyph.Speaker, "SOUND EFFECTS", out sfxValue, v =>
            {
                GameAudio.Sfx = v;
                sfxValue.text = Percent(v);
            });

            UiKit.El("gm__divider", settingsGroup);

            var diff = UiKit.El("gm__block", settingsGroup);
            SettingLabel(diff, UiIcon.Glyph.Robot, "AI DIFFICULTY");
            var seg = UiKit.El("seg", diff);
            string[] names = { "EASY", "NORMAL", "HARD" };
            for (int i = 0; i < names.Length; i++)
            {
                int level = i;
                var item = UiKit.El("seg__item", seg);
                UiKit.Text(names[i], "seg__label f-display", item);
                UiKit.OnTap(item, () =>
                {
                    GameAudio.Difficulty = (AiLevel)level;
                    RefreshDifficulty();
                });
                difficultyItems[i] = item;
            }

            UiKit.El("grow", settingsGroup);
            UiKit.Button("DONE", "blue", HideSettings, settingsGroup, "gm__done");
        }

        /// <summary>A volume setting: its icon and name on the left, the value on the right, and the
        /// slider under them.</summary>
        private VolumeSlider VolumeBlock(UiIcon.Glyph glyph, string text, out Label value, Action<float> changed)
        {
            var block = UiKit.El("gm__block", settingsGroup);
            var row = UiKit.El("gm__row", block);
            SettingLabel(row, glyph, text);
            value = UiKit.Text("0%", "gm__value f-display", row);

            var slider = new VolumeSlider(changed);
            block.Add(slider);
            return slider;
        }

        private static void SettingLabel(VisualElement parent, UiIcon.Glyph glyph, string text)
        {
            var label = UiKit.El("gm__label", parent);
            label.Add(new UiIcon(glyph, ArcadeTheme.BlueSoft, 2f));
            UiKit.Text(text, "gm__label-text f-body-semi", label);
        }

        private void RefreshSettings()
        {
            musicSlider.SetValueWithoutNotify(GameAudio.Music);
            musicValue.text = Percent(GameAudio.Music);
            sfxSlider.SetValueWithoutNotify(GameAudio.Sfx);
            sfxValue.text = Percent(GameAudio.Sfx);
            RefreshDifficulty();
        }

        private void RefreshDifficulty()
        {
            int selected = (int)GameAudio.Difficulty;
            for (int i = 0; i < difficultyItems.Length; i++)
            {
                difficultyItems[i].EnableInClassList("is-selected", i == selected);
            }
        }

        private static string Percent(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";

        /// <summary>Runs <paramref name="step"/> from 0 to 1 over <paramref name="seconds"/> of
        /// unscaled time — the pause menu animates while the world is frozen.</summary>
        private void Animate(float seconds, Action<float> step, Action done = null)
        {
            tween?.Pause();
            tween = null;

            if (seconds <= 0f || ArcadeTheme.ReducedMotion)
            {
                step(1f);
                done?.Invoke();
                return;
            }

            float start = Time.unscaledTime;
            IVisualElementScheduledItem item = null;
            item = root.schedule.Execute(() =>
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / seconds);
                step(t);
                if (t >= 1f)
                {
                    item.Pause();
                    done?.Invoke();
                }
            }).Every(16);
            tween = item;
        }

        /// <summary>
        /// The volume slider: a thin track, a blue fill up to the value, and a round knob on it. The
        /// whole 28-unit-high row takes the pointer, so it is easy to grab with a thumb; dragging
        /// anywhere along it sets the value under the finger.
        /// </summary>
        private sealed class VolumeSlider : VisualElement
        {
            private readonly VisualElement fill;
            private readonly VisualElement knob;
            private readonly Action<float> changed;
            private float value;

            public VolumeSlider(Action<float> changed)
            {
                this.changed = changed;
                AddToClassList("slider");
                pickingMode = PickingMode.Position;

                var track = UiKit.El("slider__track", this);
                fill = UiKit.El("slider__fill", track);
                knob = UiKit.El("slider__knob", this);
                track.pickingMode = fill.pickingMode = knob.pickingMode = PickingMode.Ignore;

                RegisterCallback<PointerDownEvent>(e =>
                {
                    this.CapturePointer(e.pointerId);
                    SetFromPointer(e.localPosition.x);
                    e.StopPropagation();
                });
                RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (this.HasPointerCapture(e.pointerId)) SetFromPointer(e.localPosition.x);
                });
                RegisterCallback<PointerUpEvent>(e =>
                {
                    if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
                });
            }

            public void SetValueWithoutNotify(float v)
            {
                value = Mathf.Clamp01(v);
                fill.style.width = Length.Percent(value * 100f);
                knob.style.left = Length.Percent(value * 100f);
            }

            private void SetFromPointer(float x)
            {
                float w = resolvedStyle.width;
                if (w <= 0f) return;
                float v = Mathf.Clamp01(x / w);
                if (Mathf.Approximately(v, value)) return;
                SetValueWithoutNotify(v);
                changed?.Invoke(value);
            }
        }
    }
}
