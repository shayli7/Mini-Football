using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// A short burst of coloured dots thrown outward from the centre — the win celebration.
    ///
    /// Deliberately UI Images and not a ParticleSystem. The project has no particle systems at all,
    /// so one would be the first; it would need its own material and sorting to appear above a
    /// ScreenSpaceOverlay canvas, and it would render outside the canvas batch. A dozen pooled
    /// Images cost less on a mid-range Android phone and sit in the layer they belong to for free.
    ///
    /// The pool is built once and reused, so a rematch does not allocate.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIBurst : MonoBehaviour
    {
        private const int Count = 14;
        private const float Duration = 0.62f;
        private const float Distance = 260f;

        private RectTransform[] dots;
        private Image[] images;
        private Coroutine running;

        private void Build()
        {
            if (dots != null) return;

            dots = new RectTransform[Count];
            images = new Image[Count];

            for (int i = 0; i < Count; i++)
            {
                var go = UIFactory.Child(transform, "Dot");
                var img = go.AddComponent<Image>();
                img.sprite = ArcadeTheme.Disc();
                img.raycastTarget = false;

                var rt = UIFactory.Rt(go);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(14f, 14f);
                rt.anchoredPosition = Vector2.zero;

                go.SetActive(false);
                dots[i] = rt;
                images[i] = img;
            }
        }

        /// <summary>Fires the burst in the given colour. Calling again restarts it.</summary>
        public void Play(Color color)
        {
            Build();

            if (ArcadeTheme.ReducedMotion) return;

            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Run(color));
        }

        private IEnumerator Run(Color color)
        {
            // A fixed fan with a per-dot jitter: evenly spaced alone looks mechanical, fully random
            // clumps and leaves gaps.
            var dirs = new Vector2[Count];
            var reach = new float[Count];
            for (int i = 0; i < Count; i++)
            {
                float a = (i / (float)Count) * Mathf.PI * 2f + Random.Range(-0.16f, 0.16f);
                dirs[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                reach[i] = Distance * Random.Range(0.55f, 1f);

                dots[i].anchoredPosition = Vector2.zero;
                dots[i].localScale = Vector3.one;
                images[i].color = color;
                dots[i].gameObject.SetActive(true);
            }

            float e = 0f;
            while (e < Duration)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / Duration);
                float out01 = ArcadeTheme.EaseOut(k);

                for (int i = 0; i < Count; i++)
                {
                    // Gravity on the way out, so they arc instead of flying straight — the difference
                    // between a celebration and a test pattern.
                    var p = dirs[i] * (reach[i] * out01);
                    p.y -= 120f * k * k;
                    dots[i].anchoredPosition = p;
                    dots[i].localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, k);

                    var c = color;
                    c.a = 1f - ArcadeTheme.EaseIn(k);
                    images[i].color = c;
                }

                yield return null;
            }

            for (int i = 0; i < Count; i++) dots[i].gameObject.SetActive(false);
            running = null;
        }

        private void OnDisable()
        {
            if (running != null) { StopCoroutine(running); running = null; }
            if (dots == null) return;
            for (int i = 0; i < dots.Length; i++)
            {
                if (dots[i] != null) dots[i].gameObject.SetActive(false);
            }
        }
    }
}
