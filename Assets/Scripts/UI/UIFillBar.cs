using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// Fills a horizontal <see cref="Image"/> from empty to a target fraction whenever it is shown,
    /// so a progress bar arrives by sweeping across rather than snapping to a value the player never
    /// saw move.
    ///
    /// On unscaled time, like the rest of the front end — the menus it lives on sit at timeScale 0.
    /// Honours <see cref="ArcadeTheme.ReducedMotion"/> by snapping straight to the target.
    /// </summary>
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class UIFillBar : MonoBehaviour
    {
        private Image image;
        private float target;
        private float from;
        private Coroutine run;

        private void Awake()
        {
            image = GetComponent<Image>();
        }

        /// <summary>Sets the value the bar sweeps to (0..1), starting from empty. Re-runs if visible.</summary>
        public void SetFraction(float fraction) => SetFraction(fraction, 0f);

        /// <summary>Sweeps from an explicit start to <paramref name="fraction"/> — for showing a gain
        /// (e.g. XP earned) rather than refilling from empty. Re-runs the sweep if already visible.</summary>
        public void SetFraction(float fraction, float start)
        {
            target = Mathf.Clamp01(fraction);
            from = Mathf.Clamp01(start);
            if (isActiveAndEnabled) Play();
        }

        private void OnEnable() => Play();

        private void OnDisable()
        {
            if (run != null) { StopCoroutine(run); run = null; }
        }

        private void Play()
        {
            if (image == null) return;

            if (run != null) StopCoroutine(run);

            if (ArcadeTheme.ReducedMotion)
            {
                image.fillAmount = target;
                return;
            }

            run = StartCoroutine(Sweep());
        }

        private IEnumerator Sweep()
        {
            image.fillAmount = from;

            // A beat before it moves, so the sweep is seen starting rather than already under way as
            // the screen settles in.
            yield return new WaitForSecondsRealtime(0.12f);

            float e = 0f;
            while (e < ArcadeTheme.TFlash)
            {
                e += Time.unscaledDeltaTime;
                image.fillAmount = Mathf.LerpUnclamped(from, target,
                    ArcadeTheme.EaseOut(Mathf.Clamp01(e / ArcadeTheme.TFlash)));
                yield return null;
            }

            image.fillAmount = target;
            run = null;
        }
    }
}
