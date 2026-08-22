using System;
using System.Collections;
using TMPro;
using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// Coroutine tween helpers for the Arcade Neon UI. Two rules make them safe for a paused game:
    ///   1. Everything runs on <see cref="Time.unscaledDeltaTime"/>, so menus still animate while
    ///      <c>Time.timeScale == 0</c>.
    ///   2. When <see cref="ArcadeTheme.ReducedMotion"/> is on, every tween snaps straight to its
    ///      final value — no interpolation.
    /// Each caller StartCoroutine's these on itself, so stopping is the caller's own concern.
    /// </summary>
    public static class UITween
    {
        public static IEnumerator ScaleTo(Transform t, Vector3 from, Vector3 to, float dur, Func<float, float> ease)
        {
            if (t == null) yield break;
            if (ArcadeTheme.ReducedMotion || dur <= 0f) { t.localScale = to; yield break; }

            t.localScale = from;
            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = ease(Mathf.Clamp01(e / dur));
                t.localScale = Vector3.LerpUnclamped(from, to, k);
                yield return null;
            }
            t.localScale = to;
        }

        public static IEnumerator Fade(CanvasGroup g, float from, float to, float dur, Func<float, float> ease)
        {
            if (g == null) yield break;
            if (ArcadeTheme.ReducedMotion || dur <= 0f) { g.alpha = to; yield break; }

            g.alpha = from;
            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = ease(Mathf.Clamp01(e / dur));
                g.alpha = Mathf.LerpUnclamped(from, to, k);
                yield return null;
            }
            g.alpha = to;
        }

        /// <summary>The score digit punch: 1 → peak → 1, ease-out on both halves.</summary>
        public static IEnumerator Pop(Transform t, float peak, float dur)
        {
            if (t == null) yield break;
            if (ArcadeTheme.ReducedMotion || dur <= 0f) { t.localScale = Vector3.one; yield break; }

            float half = dur * 0.4f; // quick up
            float back = dur * 0.6f; // settle down
            float e = 0f;
            while (e < half)
            {
                e += Time.unscaledDeltaTime;
                float k = ArcadeTheme.EaseOut(Mathf.Clamp01(e / half));
                t.localScale = Vector3.one * Mathf.LerpUnclamped(1f, peak, k);
                yield return null;
            }
            e = 0f;
            while (e < back)
            {
                e += Time.unscaledDeltaTime;
                float k = ArcadeTheme.EaseOut(Mathf.Clamp01(e / back));
                t.localScale = Vector3.one * Mathf.LerpUnclamped(peak, 1f, k);
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        /// <summary>
        /// The entrance: scale up past 1 and settle, fading in alongside. Adds a CanvasGroup if the
        /// object has none, since the fade needs one and callers should not have to arrange it.
        /// </summary>
        public static IEnumerator PopIn(Transform t, float dur, float delay = 0f)
        {
            if (t == null) yield break;

            var g = t.GetComponent<CanvasGroup>();
            if (g == null) g = t.gameObject.AddComponent<CanvasGroup>();

            // Fade up to whatever alpha the object already carries, not to 1. A disabled tile is
            // dimmed by its own CanvasGroup at 0.45, and animating it to full would quietly undo
            // that — the card would arrive looking enabled and then never correct itself.
            float target = g.alpha;

            if (ArcadeTheme.ReducedMotion || dur <= 0f)
            {
                t.localScale = Vector3.one;
                g.alpha = target;
                yield break;
            }

            t.localScale = Vector3.one * 0.86f;
            g.alpha = 0f;

            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / dur);
                t.localScale = Vector3.one * Mathf.LerpUnclamped(0.86f, 1f, ArcadeTheme.EaseOutBack(k));
                // Alpha rides a plain ease-out: overshooting opacity would clip at the ceiling and stall.
                g.alpha = Mathf.LerpUnclamped(0f, target, ArcadeTheme.EaseOut(k));
                yield return null;
            }

            t.localScale = Vector3.one;
            g.alpha = target;
        }

        /// <summary>
        /// Pops in each child of <paramref name="parent"/> in turn.
        ///
        /// A menu that assembles reads as built; a menu that appears all at once reads as a
        /// screenshot. The step is small enough to feel like one motion rather than a queue.
        /// </summary>
        public static IEnumerator Stagger(MonoBehaviour host, Transform parent, float step, float dur)
        {
            if (host == null || parent == null) yield break;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.gameObject.activeSelf) continue;

                host.StartCoroutine(PopIn(child, dur));
                if (!ArcadeTheme.ReducedMotion && step > 0f)
                {
                    yield return new WaitForSecondsRealtime(step);
                }
            }
        }

        /// <summary>
        /// Counts a label from one integer to another, calling <paramref name="onTick"/> each time
        /// the displayed value changes — which is where the sound goes, so it lands on the digit
        /// rather than on a timer that merely runs alongside it.
        /// </summary>
        public static IEnumerator CountUp(TMP_Text label, int from, int to, float dur, Action onTick = null)
        {
            if (label == null) yield break;

            if (ArcadeTheme.ReducedMotion || dur <= 0f || from == to)
            {
                label.text = to.ToString();
                yield break;
            }

            int shown = from;
            label.text = shown.ToString();

            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = ArcadeTheme.EaseOut(Mathf.Clamp01(e / dur));
                int next = Mathf.RoundToInt(Mathf.Lerp(from, to, k));
                if (next != shown)
                {
                    shown = next;
                    label.text = shown.ToString();
                    onTick?.Invoke();
                }
                yield return null;
            }

            if (shown != to)
            {
                label.text = to.ToString();
                onTick?.Invoke();
            }
        }

        /// <summary>
        /// A slow breathing scale, forever, so the screen is never completely static. The caller owns
        /// stopping it. Amplitude is tiny on purpose — this should be felt, not watched.
        /// </summary>
        public static IEnumerator Pulse(Transform t, float amount, float period)
        {
            if (t == null || ArcadeTheme.ReducedMotion || period <= 0f) yield break;

            float e = 0f;
            while (t != null)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Sin(e / period * Mathf.PI * 2f) * 0.5f + 0.5f;
                t.localScale = Vector3.one * Mathf.Lerp(1f, 1f + amount, k);
                yield return null;
            }
        }

        /// <summary>A one-shot alpha pulse 0 → peak → 0, for the goal flash. Uses unscaled time.</summary>
        public static IEnumerator Flash(CanvasGroup g, float peak, float dur)
        {
            if (g == null) yield break;
            if (ArcadeTheme.ReducedMotion) { g.alpha = 0f; yield break; }

            float up = dur * 0.18f;
            float down = dur * 0.82f;
            float e = 0f;
            while (e < up)
            {
                e += Time.unscaledDeltaTime;
                g.alpha = Mathf.LerpUnclamped(0f, peak, ArcadeTheme.EaseOut(Mathf.Clamp01(e / up)));
                yield return null;
            }
            e = 0f;
            while (e < down)
            {
                e += Time.unscaledDeltaTime;
                g.alpha = Mathf.LerpUnclamped(peak, 0f, ArcadeTheme.EaseIn(Mathf.Clamp01(e / down)));
                yield return null;
            }
            g.alpha = 0f;
        }
    }
}
