using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// Spins a loader ring at a constant rate.
    ///
    /// Runs on unscaled time, because everywhere it is used the world is stopped — a loader frozen
    /// at <c>timeScale 0</c> says the game has hung, which is the opposite of what it is there for.
    ///
    /// Deliberately ignores <see cref="ArcadeTheme.ReducedMotion"/>. That flag exists to stop
    /// decorative movement; this is the only thing on screen reporting that work is happening, and
    /// a still ring under the word "loading" is a bug rather than an accommodation.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class UISpinner : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = -220f;

        private RectTransform rt;
        private float angle;

        private void Awake() => rt = GetComponent<RectTransform>();

        private void OnEnable()
        {
            angle = 0f;
            if (rt != null) rt.localRotation = Quaternion.identity;
        }

        private void Update()
        {
            if (rt == null) return;

            angle = Mathf.Repeat(angle + degreesPerSecond * Time.unscaledDeltaTime, 360f);
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
