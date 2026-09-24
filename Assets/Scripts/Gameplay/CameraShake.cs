using System.Collections;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// A short shake of the camera when a goal is scored, so a goal lands as an event rather than a
    /// silent increment on the scoreboard.
    ///
    /// Attaches to whichever camera it sits on and listens to <see cref="MatchManager.GoalScored"/> —
    /// subscribed to rather than called, exactly as <see cref="GameSfx"/> and the HUD are, so the
    /// match logic stays unaware anything reacts to a goal. Nothing else drives the camera, so it
    /// owns the transform outright.
    ///
    /// The shake is expressed along the camera's own screen axes (<c>right</c> and <c>up</c>), not in
    /// world X/Z, so it reads as a screen shake whatever angle the camera is mounted at — the table's
    /// is a top-down orthographic rig, but this does not depend on that.
    ///
    /// Runs on unscaled time. A goal itself happens at full speed, but the celebration that follows
    /// stops play, and a juice effect should not stutter if it ever overlaps a frozen world.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraShake : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [SerializeField] private MatchManager match;

        [Header("Shake")]
        [Tooltip("Peak offset in world units. The table's orthographic half-height is about 0.5, so " +
                 "0.02 is a slight knock rather than a screen-wrecking jolt. Raise for more impact.")]
        [SerializeField] private float amplitude = 0.02f;

        [Tooltip("How long the shake lasts, in seconds. Short — a goal is a punch, not an earthquake.")]
        [SerializeField] private float duration = 0.35f;

        [Tooltip("How jittery the shake is, in shakes per second. Higher is sharper and more frantic; " +
                 "lower is a slower wobble.")]
        [SerializeField] private float frequency = 24f;

        private Vector3 baseLocalPosition;
        private Coroutine running;

        private void Awake()
        {
            // Captured once, and every shake is computed from it. Reading it live would fold each
            // frame's offset back into the origin and let the camera drift away from where it started.
            baseLocalPosition = transform.localPosition;
        }

        private void Start()
        {
            if (match == null)
            {
                match = FindAnyObjectByType<MatchManager>();
            }

            if (match != null)
            {
                match.GoalScored += OnGoalScored;
            }
            else
            {
                Debug.LogWarning($"{name}: no MatchManager found — the camera will not shake on a goal.", this);
            }
        }

        private void OnDestroy()
        {
            if (match != null)
            {
                match.GoalScored -= OnGoalScored;
            }
        }

        private void OnGoalScored(Team scorer) => Shake();

        /// <summary>Starts a shake, or restarts one already running so a quick second goal re-hits.</summary>
        [ContextMenu("Test shake")]
        public void Shake()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (running != null)
            {
                StopCoroutine(running);
            }

            running = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            // Seeded per shake so two goals in a row do not trace the same path. Perlin noise wants
            // its samples spread out, so the two channels are read from far-apart origins.
            float seedX = Random.value * 100f;
            float seedY = Random.value * 100f + 100f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                // Squared falloff: the shake hits hardest immediately and eases out, rather than
                // fading linearly, which reads as mushy.
                float envelope = 1f - Mathf.Clamp01(elapsed / duration);
                envelope *= envelope;

                float t = elapsed * frequency;
                // Perlin returns 0..1; centre it to -1..1 so the camera shakes both ways.
                float ox = (Mathf.PerlinNoise(seedX, t) * 2f - 1f) * amplitude * envelope;
                float oy = (Mathf.PerlinNoise(seedY, t) * 2f - 1f) * amplitude * envelope;

                transform.localPosition = baseLocalPosition + transform.right * ox + transform.up * oy;
                yield return null;
            }

            // Always land exactly back home, never a hair off from wherever the last frame stopped.
            transform.localPosition = baseLocalPosition;
            running = null;
        }
    }
}
