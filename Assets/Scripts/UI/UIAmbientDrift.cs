using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// Drifts a menu backdrop slowly and endlessly, so a still screen is never completely still.
    ///
    /// The motion is a Lissajous figure rather than a loop, so it never visibly repeats — a cycling
    /// drift is worse than none, because the eye finds the period and then watches for it.
    ///
    /// Safe only because backdrops over-extend their parent by <see cref="ArcadeTheme.Bleed"/>; the
    /// travel here stays well inside that margin, so no edge is ever exposed.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class UIAmbientDrift : MonoBehaviour
    {
        [Tooltip("Peak travel in reference pixels. Must stay comfortably under ArcadeTheme.Bleed.")]
        [SerializeField] private float amplitude = 34f;

        [Tooltip("Seconds for the horizontal sweep. The vertical one uses an irrational multiple of " +
                 "this, which is what stops the pattern repeating.")]
        [SerializeField] private float period = 29f;

        private RectTransform rt;
        private Vector2 origin;
        private float t;

        private void Awake()
        {
            rt = GetComponent<RectTransform>();
            origin = rt.anchoredPosition;
        }

        /// <summary>
        /// Retunes the drift for a component added in code, where the inspector defaults are not a
        /// choice anybody made. Several of these on one screen must not share a period, or the
        /// separate elements sail about in formation and read as one moving object.
        /// </summary>
        public void Configure(float driftAmplitude, float driftPeriod)
        {
            amplitude = driftAmplitude;
            period = driftPeriod;
        }

        private void OnDisable()
        {
            if (rt != null) rt.anchoredPosition = origin;
        }

        private void Update()
        {
            if (rt == null || ArcadeTheme.ReducedMotion || period <= 0f) return;

            // Unscaled: the menu sits at timeScale 0, where a scaled clock would leave this frozen.
            t += Time.unscaledDeltaTime;

            float x = Mathf.Sin(t / period * Mathf.PI * 2f);
            float y = Mathf.Sin(t / (period * 1.618f) * Mathf.PI * 2f);

            rt.anchoredPosition = origin + new Vector2(x, y) * amplitude;
        }
    }
}
