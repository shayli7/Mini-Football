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

        /// <summary>The logo's ball-and-rods emblem, bobbed gently so the mark reads as hovering rather
        /// than printed. Null if the logo was built without its mark.</summary>
        private RectTransform markRt;
        private Vector2 markOrigin;

        private Coroutine anim;
        private Coroutine breathe;
        private Coroutine bob;
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

            // Two soft blue halos low on the screen, drifting independently. They are what stops the
            // sky reading as a printed gradient: the light near the prompt is never quite the same
            // twice, but neither is ever caught moving. Kept below the wordmark so the top of the
            // screen stays the dark end of the fade.
            Halo("HaloLeft", ArcadeTheme.BlueSoft, new Vector2(-460f, -260f), 820f, 26f, 31f);
            Halo("HaloRight", ArcadeTheme.Blue, new Vector2(470f, -300f), 760f, 22f, 43f);

            // A warm spotlight behind the wordmark, breathing slowly — the single thing that turns the
            // flat wash into a lit stage. It sits under everything but the base wash, so the logo and
            // the halos read as standing in its light.
            Spotlight();

            // A scatter of slow motes over the top, drifting on their own clocks. Faint enough to be
            // atmosphere rather than confetti — stadium dust caught in the spotlight, the texture the
            // bare gradient was missing.
            Motes();
        }

        /// <summary>The breathing gold pool of light behind the logo.</summary>
        private void Spotlight()
        {
            var go = UIFactory.Child(root.transform, "Spotlight");
            var img = go.AddComponent<Image>();
            img.sprite = ArcadeTheme.DiscGlow(60f);
            img.type = Image.Type.Simple;
            img.color = ArcadeTheme.Gold.WithAlpha(0.08f);
            img.raycastTarget = false;

            var rt = UIFactory.Rt(go);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1180f, 1180f);
            rt.anchoredPosition = new Vector2(0f, 90f);

            // A gentle swell, slower and softer than the pip's pulse, so it reads as light shifting
            // rather than something blinking.
            var pulse = go.AddComponent<UIPulse>();
            pulse.Configure(4.6f, 0.62f, 0.06f);
        }

        /// <summary>A handful of faint drifting motes, for atmosphere over the wash.</summary>
        private void Motes()
        {
            var colors = new[] { ArcadeTheme.Gold, ArcadeTheme.BlueSoft, ArcadeTheme.Ink };

            for (int i = 0; i < 12; i++)
            {
                var go = UIFactory.Child(root.transform, "Mote");
                var img = go.AddComponent<Image>();
                img.sprite = ArcadeTheme.Disc();
                img.type = Image.Type.Simple;
                img.color = colors[i % colors.Length].WithAlpha(UnityEngine.Random.Range(0.05f, 0.16f));
                img.raycastTarget = false;

                var rt = UIFactory.Rt(go);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                float s = UnityEngine.Random.Range(5f, 13f);
                rt.sizeDelta = new Vector2(s, s);
                rt.anchoredPosition = new Vector2(UnityEngine.Random.Range(-780f, 780f),
                                                  UnityEngine.Random.Range(-400f, 400f));

                // Each on its own amplitude and period, so they never sail in formation.
                go.AddComponent<UIAmbientDrift>().Configure(UnityEngine.Random.Range(26f, 62f),
                                                            UnityEngine.Random.Range(16f, 44f));
            }
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

            // The ball-and-rods emblem, cached so it can be bobbed once the entrance settles. Found by
            // name rather than passed back, so LogoLockup stays a one-call black box for every other
            // caller; a build without the mark simply leaves this null and the bob no-ops.
            var mark = logo.transform.Find("Mark");
            if (mark != null)
            {
                markRt = (RectTransform)mark;
                markOrigin = markRt.anchoredPosition;
            }
        }

        /// <summary>
        /// The call to action, rebuilt with real weight: the display face at button size in gold, over
        /// a soft halo and above a gold underline, so it reads as the one thing to do here rather than
        /// a caption the eye slides past. The old prompt was body-sized muted grey and disappeared into
        /// the wash.
        ///
        /// The three parts live in one holder with the breathing <see cref="CanvasGroup"/> on it, so
        /// the glow, the type and the rule breathe as a single object.
        /// </summary>
        private void BuildPrompt()
        {
            var holder = UIFactory.Child(root.transform, "Prompt");
            var hrt = UIFactory.Rt(holder);
            hrt.anchorMin = new Vector2(0.5f, 0f);
            hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(760f, 104f);
            hrt.anchoredPosition = new Vector2(0f, 96f);

            promptGroup = holder.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f;

            // A soft pool of shade, so the words keep their contrast where the sky is lightest.
            var glowGo = UIFactory.Child(holder.transform, "Glow");
            var glow = glowGo.AddComponent<Image>();
            glow.sprite = ArcadeTheme.DiscGlow(50f);
            glow.type = Image.Type.Simple;
            glow.color = ArcadeTheme.BgDeep.WithAlpha(0.28f);
            glow.raycastTarget = false;
            var grt = UIFactory.Rt(glowGo);
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(600f, 168f);
            grt.anchoredPosition = new Vector2(0f, 6f);

            // Display face, button-sized, in ink. Gold would sink into the light blue at the bottom of
            // the sky; the gold stays in the underline, which is all it needs to say "press here".
            var prompt = UIFactory.Text(holder.transform, "TOUCH TO START", ArcadeTheme.FsButton * 1.22f,
                                        ArcadeTheme.Ink, display: true, bold: true, upper: true,
                                        tracking: 16f);
            var prt = UIFactory.Rt(prompt.gameObject);
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.offsetMin = new Vector2(0f, 26f);
            prt.offsetMax = Vector2.zero;

            // A short gold rule under the words, centred — the underline that gives the line a base to
            // stand on instead of floating at the bottom of the screen.
            var line = UIFactory.Child(holder.transform, "Underline");
            UIFactory.RoundedImage(line, ArcadeTheme.RadSm, ArcadeTheme.Gold.WithAlpha(0.85f), false);
            var lrt = UIFactory.Rt(line);
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.sizeDelta = new Vector2(160f, 3f);
            lrt.anchoredPosition = new Vector2(0f, 12f);
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

            // Started only now, not at build: the entrance PopIn drives the whole logo's scale, and a
            // bob running under it would fight for the same transform. Once the pop has settled the
            // mark is the bob's alone.
            if (bob != null) StopCoroutine(bob);
            bob = StartCoroutine(Bob());

            anim = null;
        }

        /// <summary>
        /// The emblem hovering: a slow vertical bob on the ball-and-rods mark, forever. Small on
        /// purpose — it should read as the mark being alive, not as it drifting off the wordmark.
        /// Unscaled, since the world is frozen behind this; snaps still and centred under reduced
        /// motion.
        /// </summary>
        private IEnumerator Bob()
        {
            if (markRt == null || ArcadeTheme.ReducedMotion)
            {
                yield break;
            }

            float e = 0f;
            while (markRt != null)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Sin(e / 2.2f * Mathf.PI * 2f);
                markRt.anchoredPosition = markOrigin + new Vector2(0f, k * 7f);
                yield return null;
            }
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

            if (bob != null)
            {
                StopCoroutine(bob);
                bob = null;
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
