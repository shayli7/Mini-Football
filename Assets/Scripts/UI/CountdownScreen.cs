using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The "3 - 2 - 1 - GO!" that runs over the table before a match begins.
    ///
    /// Purely a presentation layer: it counts, and that is all. It never touches the ball, the rods
    /// or the score, and it does not decide when the match starts - <see cref="GameFlow"/> waits for
    /// <see cref="Run"/> to finish and then kicks off. That ordering is the whole point of the
    /// countdown being its own screen: the kick-off whistle rides MatchManager's KickedOff event, so
    /// moving the kick-off to after the count moves the whistle with it, and neither MatchManager nor
    /// GameSfx needs to know a countdown happened at all.
    ///
    /// Everything runs on unscaled time, like the rest of the front end, because the world is held at
    /// timeScale 0 for the duration - otherwise the count would freeze along with the table it is
    /// counting over.
    ///
    /// Deliberately does NOT block raycasts. The table stays reachable while the numbers run, so both
    /// players can set their rods up before the ball is live, which is what you do at a real table
    /// anyway.
    /// </summary>
    public class CountdownScreen : MonoBehaviour
    {
        /// <summary>Seconds each of "3", "2" and "1" is on screen.</summary>
        private const float StepSeconds = 1f;

        /// <summary>How long "GO!" holds before play starts. Short - it is a starting gun, not a beat.</summary>
        private const float GoSeconds = 0.45f;

        /// <summary>How far above its final size a numeral enters. It shrinks into place, like a stamp.</summary>
        private const float EnterScale = 1.75f;

        /// <summary>The halo's alpha at full strength, faded in proportion with the numeral.</summary>
        private const float HaloAlpha = 0.16f;

        private GameObject root;
        private CanvasGroup group;
        private TextMeshProUGUI number;
        private Image halo;
        private Coroutine running;

        public bool IsRunning => running != null;

        public void Build(Transform canvasRoot)
        {
            root = UIFactory.Child(canvasRoot, "Countdown");
            UIFactory.Stretch(UIFactory.Rt(root));

            group = root.AddComponent<CanvasGroup>();

            // Never takes input: see the class summary. Set on the group as well as on every graphic,
            // so nothing added here later can quietly start swallowing taps meant for the rods.
            group.blocksRaycasts = false;
            group.interactable = false;

            BuildDim();
            BuildHalo();
            BuildNumber();

            root.SetActive(false);
        }

        /// <summary>
        /// A light wash over the table so the numerals stay legible against a bright pitch. Kept well
        /// short of the opaque curtain <see cref="LoadingScreen"/> uses - the player is meant to be
        /// looking at the table and lining their rods up, not waiting for it to reappear.
        /// </summary>
        private void BuildDim()
        {
            var dim = UIFactory.Child(root.transform, "Dim");
            var img = UIFactory.RoundedImage(dim, ArcadeTheme.RadSm,
                                             ArcadeTheme.BgDeep.WithAlpha(0.38f), false);
            img.raycastTarget = false;
            UIFactory.Stretch(UIFactory.Rt(dim), -ArcadeTheme.Bleed);
        }

        private void BuildHalo()
        {
            var go = UIFactory.Child(root.transform, "Halo");
            halo = go.AddComponent<Image>();
            halo.sprite = ArcadeTheme.DiscGlow(56f);
            halo.color = ArcadeTheme.Gold.WithAlpha(HaloAlpha);
            halo.raycastTarget = false;

            var rt = UIFactory.Rt(go);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(460f, 460f);
            rt.anchoredPosition = Vector2.zero;
        }

        private void BuildNumber()
        {
            number = UIFactory.Text(root.transform, "3", 210f, ArcadeTheme.Ink,
                                    display: true, bold: true, upper: true, tracking: 4f);

            var rt = UIFactory.Rt(number.gameObject);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, 300f);
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Runs the whole count. Yield on this from the flow - it completes as "GO!" clears, which is
        /// the moment play should start.
        /// </summary>
        public IEnumerator Run()
        {
            if (root == null)
            {
                yield break;
            }

            if (running != null)
            {
                StopCoroutine(running);
            }

            running = StartCoroutine(Sequence());
            yield return running;
        }

        /// <summary>Stops the count and clears the screen, for a match abandoned before it began.</summary>
        public void Cancel()
        {
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }

            if (root != null)
            {
                root.SetActive(false);
            }
        }

        private IEnumerator Sequence()
        {
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            group.alpha = 1f;

            for (int n = 3; n >= 1; n--)
            {
                yield return Step(n.ToString(), ArcadeTheme.Ink, ArcadeTheme.Gold, StepSeconds, finale: false);
            }

            yield return Step("GO!", ArcadeTheme.Go, ArcadeTheme.Go, GoSeconds, finale: true);

            root.SetActive(false);
            running = null;
        }

        /// <summary>
        /// One beat: the numeral drops in oversized and settles, holds, then leaves. The exit is a
        /// fade rather than another scale, so the next numeral's entrance is the only movement the
        /// eye has to follow.
        /// </summary>
        private IEnumerator Step(string text, Color ink, Color glow, float seconds, bool finale)
        {
            number.text = text;
            number.color = ink;

            if (halo != null)
            {
                halo.color = glow.WithAlpha(HaloAlpha);
            }

            Transform t = number.transform;

            // The countdown's own audio: a beep per numeral, a distinct GO at the end. The kick-off
            // whistle is deliberately NOT played here - it belongs to KickOff, which GameFlow calls
            // once this whole sequence has finished, so it lands on the start of play rather than on
            // the "GO".
            if (finale) GameSfx.PlayCountdownGo();
            else GameSfx.PlayCountdownTick();

            float enter = Mathf.Min(ArcadeTheme.TSlow, seconds * 0.55f);
            float exit = Mathf.Min(ArcadeTheme.TFast, seconds * 0.25f);

            group.alpha = 1f;
            t.localScale = Vector3.one * EnterScale;

            float e = 0f;
            while (e < enter)
            {
                e += Time.unscaledDeltaTime;
                float k = ArcadeTheme.EaseOut(Mathf.Clamp01(e / enter));
                t.localScale = Vector3.one * Mathf.LerpUnclamped(EnterScale, 1f, k);
                yield return null;
            }

            t.localScale = Vector3.one;

            float hold = seconds - enter - exit;
            if (hold > 0f)
            {
                yield return new WaitForSecondsRealtime(hold);
            }

            // Fade the numeral alone, not the group: the dim behind it has to stay put, or the table
            // brightens and darkens once per second for the length of the count.
            yield return FadeNumeral(ink, glow, 1f, 0f, exit);

            // Back to full for the next numeral, which sets its own colour on the way in.
            FadeNumeralTo(ink, glow, 1f);
        }

        /// <summary>Fades the numeral and its halo together, leaving the dim untouched.</summary>
        private IEnumerator FadeNumeral(Color ink, Color glow, float from, float to, float dur)
        {
            if (ArcadeTheme.ReducedMotion || dur <= 0f)
            {
                FadeNumeralTo(ink, glow, to);
                yield break;
            }

            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = ArcadeTheme.EaseIn(Mathf.Clamp01(e / dur));
                FadeNumeralTo(ink, glow, Mathf.LerpUnclamped(from, to, k));
                yield return null;
            }

            FadeNumeralTo(ink, glow, to);
        }

        private void FadeNumeralTo(Color ink, Color glow, float alpha)
        {
            number.color = ink.WithAlpha(alpha);

            if (halo != null)
            {
                halo.color = glow.WithAlpha(HaloAlpha * alpha);
            }
        }
    }
}
