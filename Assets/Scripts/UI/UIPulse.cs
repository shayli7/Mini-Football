using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// Gently pulses a graphic's opacity and scale, forever, so a "live" marker — an online dot —
    /// reads as active rather than as a painted-on spot.
    ///
    /// On unscaled time, since the menus it lives on sit at timeScale 0. Honours
    /// <see cref="ArcadeTheme.ReducedMotion"/> by holding still at full strength.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIPulse : MonoBehaviour
    {
        [SerializeField] private float period = 1.4f;
        [SerializeField] private float minAlpha = 0.45f;
        [SerializeField] private float scaleAmount = 0.14f;

        private CanvasGroup group;
        private float t;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>
        /// Retunes the pulse for a component added in code, where the inspector defaults are not a
        /// choice anybody made — a slow, faint swell for a spotlight reads nothing like the brisk throb
        /// of an online pip, and both come from here.
        /// </summary>
        public void Configure(float pulsePeriod, float pulseMinAlpha, float pulseScaleAmount)
        {
            period = pulsePeriod;
            minAlpha = pulseMinAlpha;
            scaleAmount = pulseScaleAmount;
        }

        private void OnDisable()
        {
            if (group != null) group.alpha = 1f;
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (ArcadeTheme.ReducedMotion || period <= 0f)
            {
                if (group != null) group.alpha = 1f;
                transform.localScale = Vector3.one;
                return;
            }

            t += Time.unscaledDeltaTime;
            // 0..1..0 triangle-ish via a cosine, so both ends ease rather than snap.
            float k = 0.5f - 0.5f * Mathf.Cos(t / period * Mathf.PI * 2f);

            if (group != null) group.alpha = Mathf.Lerp(minAlpha, 1f, k);
            transform.localScale = Vector3.one * (1f + scaleAmount * k);
        }
    }
}
