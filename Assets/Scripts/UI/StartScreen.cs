using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The title screen: the wordmark on a colored wash, and a prompt to touch anywhere to begin.
    ///
    /// It replaces the loading bar at boot rather than joining it. The bar was covering a wait the
    /// player was never told about; this covers the same wait with something to look at, and the tap
    /// that dismisses it buys more time than the bar ever did. Nothing behind it depends on the
    /// sign-in having finished — every online entry point awaits GameServices for itself.
    ///
    /// <see cref="LoadingScreen"/> keeps its own job: the curtain between a finished match and the
    /// menu, where there really is a table being torn down behind it.
    ///
    /// Everything here runs on unscaled time. The world is frozen at timeScale 0 while this is up.
    /// </summary>
    public class StartScreen : MonoBehaviour
    {
        /// <summary>How far the prompt fades down between breaths. Not to nothing — a prompt that
        /// vanishes reads as a glitch rather than as an invitation.</summary>
        private const float PromptDim = 0.35f;

        /// <summary>Seconds for one half of the breath. Slow enough to be calm, quick enough to still
        /// be the thing on screen that is moving.</summary>
        private const float PromptBreath = 1.1f;

        /// <summary>The acknowledgement punch on the wordmark when the screen is tapped. Small: this
        /// is a whole lockup, not a score digit.</summary>
        private const float LogoPunch = 1.08f;

        private const float LogoScale = 1.6f;

        private GameObject root;
        private CanvasGroup group;
        private GameObject logo;
        private CanvasGroup promptGroup;

        private Coroutine anim;
        private Coroutine breathe;
        private Action onStart;

        /// <summary>Guards against a double tap opening the menu twice.</summary>
        private bool started;

        public bool IsVisible => root != null && root.activeSelf;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "StartScreen");
            UIFactory.Stretch(UIFactory.Rt(root));

            group = root.AddComponent<CanvasGroup>();

            BuildBackdrop();
            BuildLogo();
            BuildPrompt();
            BuildTapCatcher();

            root.SetActive(false);
        }

        // ---------- build ----------

        private void BuildBackdrop()
        {
            var backdrop = UIFactory.Child(root.transform, "Backdrop");
            var img = backdrop.AddComponent<Image>();
            img.sprite = ArcadeTheme.SplashBackdrop();
            img.type = Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = true;
            UIFactory.Stretch(UIFactory.Rt(backdrop), -ArcadeTheme.Bleed);
            backdrop.AddComponent<UIAmbientDrift>();

            // Three halos in the team colors, drifting independently. They are what stops the wash
            // reading as a printed gradient: the color under the wordmark is never quite the same
            // twice, but no single one of them is ever caught moving.
            Halo("HaloRed", ArcadeTheme.Red, new Vector2(-430f, 170f), 820f, 26f, 31f);
            Halo("HaloBlue", ArcadeTheme.Blue, new Vector2(450f, -120f), 760f, 22f, 43f);
            Halo("HaloGold", ArcadeTheme.Gold, new Vector2(90f, 300f), 640f, 30f, 37f);
        }

        /// <summary>One soft color blob behind the logo.</summary>
        private void Halo(string name, Color color, Vector2 at, float size, float amplitude, float period)
        {
            var go = UIFactory.Child(root.transform, name);

            var img = go.AddComponent<Image>();
            // DiscGlow rather than the 9-sliced Glow: a rounded-rect halo stretched to 800 units
            // square keeps its straight edges and reads as a lozenge, not a light source.
            img.sprite = ArcadeTheme.DiscGlow(40f);
            img.type = Image.Type.Simple;
            img.color = color.WithAlpha(0.22f);
            img.raycastTarget = false;

            var rt = UIFactory.Rt(go);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = at;

            go.AddComponent<UIAmbientDrift>().Configure(amplitude, period);
        }

        private void BuildLogo()
        {
            logo = UIFactory.LogoLockup(root.transform, LogoScale);

            var rt = UIFactory.Rt(logo);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1100f, 280f);
            // Above centre, not on it: the prompt sits at the bottom, and a lockup centred on the
            // screen leaves the whole upper half with nothing to balance it.
            rt.anchoredPosition = new Vector2(0f, 40f);
        }

        private void BuildPrompt()
        {
            var prompt = UIFactory.Text(root.transform, "TOUCH TO START", ArcadeTheme.FsBody,
                                        ArcadeTheme.InkMuted, display: false, bold: true, upper: true,
                                        tracking: 10f);

            var rt = UIFactory.Rt(prompt.gameObject);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(ArcadeTheme.Xl2, 96f);
            rt.offsetMax = new Vector2(-ArcadeTheme.Xl2, 136f);

            // Its own group, so it can breathe without taking the rest of the screen with it.
            promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f;
        }

        /// <summary>
        /// A transparent sheet over everything, so the tap target is the screen rather than a button
        /// the player has to find. Added last, so it covers the wordmark too — the logo is the most
        /// obvious thing to press and it would otherwise be the one dead spot on the screen.
        /// </summary>
        private void BuildTapCatcher()
        {
            var tap = UIFactory.Child(root.transform, "Tap");

            var img = tap.AddComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = true;
            // Past the safe area: a tap in the notch margin is still a tap on the screen.
            UIFactory.Stretch(UIFactory.Rt(tap), -ArcadeTheme.Bleed);

            var button = tap.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = img;
            button.onClick.AddListener(Begin);
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

            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.blocksRaycasts = true;

            // Silence would be this screen's first impression of the game. Safe to call every time:
            // GameSfx ignores a request for the track it is already playing, which is also why
            // reaching the menu after this does not restart it.
            GameSfx.PlayMenuMusic();

            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(Enter());
        }

        private IEnumerator Enter()
        {
            group.alpha = 0f;

            // Started before the screen fades up rather than after it: PopIn zeroes the logo's own
            // alpha the moment it runs, so kicking it off afterwards would show the wordmark at full
            // strength and then blink it out in order to pop it back in.
            Coroutine pop = StartCoroutine(UITween.PopIn(logo.transform, ArcadeTheme.TSlow * 1.6f));

            yield return UITween.Fade(group, 0f, 1f, ArcadeTheme.TSlow, ArcadeTheme.EaseOut);
            yield return pop;

            // Last, so the invitation arrives after the thing it is inviting you into.
            breathe = StartCoroutine(Breathe());
            anim = null;
        }

        /// <summary>
        /// The prompt fading in and out for as long as the screen is up, so there is always one thing
        /// alive on it.
        ///
        /// Reduced motion is checked here rather than left to the tweens: they snap to their end
        /// state, and a loop of snaps is a strobe — the opposite of what was asked for.
        /// </summary>
        private IEnumerator Breathe()
        {
            if (ArcadeTheme.ReducedMotion)
            {
                promptGroup.alpha = 1f;
                yield break;
            }

            yield return UITween.Fade(promptGroup, 0f, 1f, ArcadeTheme.TSlow, ArcadeTheme.EaseOut);

            while (true)
            {
                yield return UITween.Fade(promptGroup, 1f, PromptDim, PromptBreath, ArcadeTheme.EaseOut);
                yield return UITween.Fade(promptGroup, PromptDim, 1f, PromptBreath, ArcadeTheme.EaseOut);
            }
        }

        private void Begin()
        {
            if (started || root == null)
            {
                return;
            }

            started = true;
            GameSfx.PlayUiClick();

            group.blocksRaycasts = false;

            if (breathe != null)
            {
                StopCoroutine(breathe);
                breathe = null;
            }

            if (anim != null)
            {
                StopCoroutine(anim);
            }

            anim = StartCoroutine(Exit());
        }

        private IEnumerator Exit()
        {
            // The prompt has been answered; leaving it breathing through the handover reads as though
            // the tap was not registered.
            if (promptGroup != null) promptGroup.alpha = 0f;

            yield return UITween.Pop(logo.transform, LogoPunch, ArcadeTheme.TNormal);

            // Handed over first, so the menu is already assembling underneath while this fades out.
            // It raises itself to the front as it opens, so this has to take the front back —
            // otherwise the menu simply appears on top and there is no crossfade at all.
            onStart?.Invoke();
            root.transform.SetAsLastSibling();

            yield return UITween.Fade(group, 1f, 0f, ArcadeTheme.TNormal, ArcadeTheme.EaseIn);

            root.SetActive(false);
            anim = null;
        }
    }
}
