using System;
using System.Collections.Generic;
using TableFootball.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI
{
    /// <summary>
    /// The title screen: the wordmark under a night sky that lightens towards the bottom, and a prompt
    /// to touch anywhere to begin. Built on UI Toolkit (see <see cref="UiToolkitHost"/>), styled by
    /// <c>Resources/UI/Styles/StartScreen.uss</c>.
    ///
    /// It replaces the loading bar at boot rather than joining it. The bar was covering a wait the
    /// player was never told about; this covers the same wait with something to look at, and the tap
    /// that dismisses it buys more time than the bar ever did. Nothing behind it depends on the
    /// sign-in having finished — every online entry point awaits GameServices for itself.
    ///
    /// Everything here runs on unscaled time. The world is frozen at timeScale 0 while this is up.
    /// </summary>
    public class StartScreen : MonoBehaviour
    {
        /// <summary>How far the prompt fades down between breaths. Not to nothing — a prompt that
        /// vanishes reads as a glitch rather than as an invitation.</summary>
        private const float PromptDim = 0.4f;

        /// <summary>Seconds for one full breath of the prompt.</summary>
        private const float PromptBreath = 2.4f;

        /// <summary>Seconds for the knob to slide end to end and back — a rod being worked.</summary>
        private const float KnobPeriod = 5.2f;

        /// <summary>How far the knob travels either side of centre, in panel units.</summary>
        private const float KnobTravel = 58f;

        /// <summary>Seconds a mote takes to rise the height of the screen.</summary>
        private const float MoteRise = 9f;

        private VisualElement root;
        private VisualElement logo;
        private VisualElement knob;
        private VisualElement prompt;
        private VisualElement underline;
        private readonly List<VisualElement> motes = new List<VisualElement>();
        private IVisualElementScheduledItem loop;
        private Action onStart;
        private float shownAt;

        /// <summary>Guards against a double tap opening the menu twice.</summary>
        private bool started;

        public bool IsVisible => root != null && root.style.display != DisplayStyle.None;

        /// <summary>
        /// <paramref name="canvasRoot"/> is the uGUI canvas the other screens still hang off. Unused
        /// here — this screen lives on the UI Toolkit panel — and kept so the boot order in
        /// TableFootballUI does not change.
        /// </summary>
        public void Build(Transform canvasRoot)
        {
            root = UiKit.El("screen start", UiToolkitHost.Root, "StartScreen");
            var sheet = Resources.Load<StyleSheet>("UI/Styles/StartScreen");
            if (sheet != null) root.styleSheets.Add(sheet);
            // The whole screen is the button: the logo is the most obvious thing to press, and it
            // would otherwise be the one dead spot.
            root.pickingMode = PickingMode.Position;
            root.RegisterCallback<PointerDownEvent>(_ => Begin());

            var sky = UiKit.El("bleed start__sky", root);
            sky.pickingMode = PickingMode.Ignore;
            sky.style.backgroundImage = new StyleBackground(ArcadeTheme.SplashBackdrop());

            var circle = UiKit.El("start__circle", root);
            circle.pickingMode = PickingMode.Ignore;
            var halfway = UiKit.El("start__halfway", root);
            halfway.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < 6; i++)
            {
                var mote = UiKit.El("start__mote" + (i % 3 == 1 ? " start__mote--gold" : string.Empty), root);
                mote.pickingMode = PickingMode.Ignore;
                mote.style.left = Length.Percent(12f + i * 15f);
                motes.Add(mote);
            }

            logo = UiKit.El("start__logo", root);
            logo.pickingMode = PickingMode.Ignore;
            var rod = UiKit.El("start__rod", logo);
            UiKit.El("start__rod-bar", rod);
            knob = UiKit.El("start__knob", rod);
            UiKit.Text(ArcadeTheme.GameNameA, "start__mini f-display-semi", logo);
            UiKit.Text(ArcadeTheme.GameNameB, "start__football f-display", logo);

            prompt = UiKit.El("start__prompt", root);
            prompt.pickingMode = PickingMode.Ignore;
            UiKit.Text("TOUCH TO START", "start__prompt-text f-display-semi", prompt);
            underline = UiKit.El("start__underline", prompt);

            UiFonts.Apply(root);
            UiKit.Show(root, false);
        }

        // ---------- show / dismiss ----------

        /// <summary>
        /// Raises the title screen. <paramref name="onStart"/> fires when the player taps, while this
        /// screen is still fading out over whatever it opens.
        /// </summary>
        public void Show(Action onStart)
        {
            if (root == null)
            {
                onStart?.Invoke();
                return;
            }

            this.onStart = onStart;
            started = false;
            shownAt = Time.unscaledTime;

            UiKit.Show(root, true);
            root.BringToFront();
            root.pickingMode = PickingMode.Position;
            root.style.opacity = 0f;
            logo.style.scale = new Scale(Vector3.one);
            prompt.style.opacity = 0f;

            // Silence would be this screen's first impression of the game. Safe to call every time:
            // GameSfx ignores a request for the track it is already playing.
            GameSfx.PlayMenuMusic();

            UiKit.Fade(root, 1f, ArcadeTheme.TSlow);
            UiKit.Enter(logo, ArcadeTheme.TFast);

            loop?.Pause();
            loop = UiKit.Loop(root, Tick);
        }

        /// <summary>
        /// The ambient motion, one clock for all of it: the knob working the rod, the prompt
        /// breathing, the motes rising. Under reduced motion everything holds still at rest.
        /// </summary>
        private void Tick(float now)
        {
            float t = now - shownAt;
            bool still = ArcadeTheme.ReducedMotion;

            float slide = still ? 0f : Mathf.Sin(t / KnobPeriod * Mathf.PI * 2f) * KnobTravel;
            knob.style.translate = new Translate(slide, 0f);

            if (!started)
            {
                // The prompt arrives after the logo has landed, then breathes.
                float arrive = Mathf.Clamp01((t - 0.6f) / ArcadeTheme.TSlow);
                float breath = still ? 1f
                    : Mathf.Lerp(PromptDim, 1f, 0.5f + 0.5f * Mathf.Cos(Mathf.Max(0f, t - 0.6f) / PromptBreath * Mathf.PI * 2f));
                prompt.style.opacity = arrive * breath;
                underline.style.scale = new Scale(new Vector3(still ? 1f : Mathf.Lerp(0.35f, 1f, breath), 1f, 1f));
            }

            for (int i = 0; i < motes.Count; i++)
            {
                if (still)
                {
                    motes[i].style.opacity = 0f;
                    continue;
                }

                float phase = Mathf.Repeat(t / MoteRise + i / (float)motes.Count, 1f);
                motes[i].style.translate = new Translate(0f, -phase * 620f);
                motes[i].style.opacity = Mathf.Sin(phase * Mathf.PI) * 0.7f;
            }
        }

        private void Begin()
        {
            if (started || root == null || !IsVisible)
            {
                return;
            }

            started = true;
            GameSfx.PlayUiClick();
            root.pickingMode = PickingMode.Ignore;

            // The prompt has been answered; leaving it breathing through the handover reads as though
            // the tap was not registered.
            prompt.style.opacity = 0f;

            // A small punch on the wordmark to acknowledge the tap.
            logo.style.scale = new Scale(new Vector3(1.06f, 1.06f, 1f));
            logo.schedule.Execute(() => logo.style.scale = new Scale(Vector3.one)).StartingIn(120);

            // Handed over first, so the menu is already assembling underneath while this fades out.
            // The menu brings itself to the front as it opens, so this takes the front back —
            // otherwise the menu simply appears on top and there is no crossfade at all.
            onStart?.Invoke();
            root.BringToFront();

            UiKit.Fade(root, 0f, ArcadeTheme.TSlow, () =>
            {
                loop?.Pause();
                UiKit.Show(root, false);
            });
        }
    }
}
