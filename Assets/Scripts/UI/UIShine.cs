using UnityEngine;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// A soft band of light that sweeps across a control now and then — the "start here" cue on a
    /// screen's one gold control. A quick pass (<see cref="ArcadeTheme.TShine"/>) and then a long rest
    /// (<see cref="ArcadeTheme.ShineRest"/>), so it catches the eye rather than flickering.
    ///
    /// Clipped to the control's own rounded fill with a stencil <see cref="Mask"/>, so the band never
    /// shows past the corners. On unscaled time, since the menus sit at timeScale 0, and hidden
    /// entirely under <see cref="ArcadeTheme.ReducedMotion"/>.
    ///
    /// One per screen. Two shining buttons are two highlights, which is none.
    /// </summary>
    [DisallowMultipleComponent]
    public class UIShine : MonoBehaviour
    {
        /// <summary>Width of the band, in reference units.</summary>
        private const float BandWidth = 46f;

        /// <summary>The band's lean, in degrees — upright reads as a wiper, not light.</summary>
        private const float BandTilt = -20f;

        private Selectable owner;
        private RectTransform area;
        private RectTransform band;
        private float t;

        /// <summary>
        /// Adds a shine to <paramref name="button"/>, clipped to its fill. Returns null if the button
        /// has no fill to clip to.
        /// </summary>
        public static UIShine AddTo(MenuButton button)
        {
            if (button == null) return null;
            return AddTo(button.fill, button);
        }

        /// <summary>
        /// Adds a shine clipped to <paramref name="fill"/> — a surface inside a larger control, such as
        /// the gold pill on an action card — that rests while <paramref name="owner"/> is not
        /// interactable.
        /// </summary>
        public static UIShine AddTo(Image fill, Selectable owner)
        {
            if (fill == null) return null;

            if (fill.GetComponent<Mask>() == null)
            {
                var mask = fill.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = true;
            }

            var shine = fill.gameObject.GetComponent<UIShine>();
            if (shine == null) shine = fill.gameObject.AddComponent<UIShine>();
            shine.owner = owner;
            shine.Build(fill.rectTransform);
            return shine;
        }

        private void Build(RectTransform clip)
        {
            if (band != null) return;
            area = clip;

            var go = UIFactory.Child(clip, "Shine");
            var img = go.AddComponent<Image>();
            img.color = Color.white.WithAlpha(ArcadeTheme.ShineAlpha);
            img.raycastTarget = false;

            band = UIFactory.Rt(go);
            // Anchored to the left edge and taller than the control, so the tilted band still spans
            // it top to bottom.
            band.anchorMin = new Vector2(0f, -0.5f);
            band.anchorMax = new Vector2(0f, 1.5f);
            band.pivot = new Vector2(0.5f, 0.5f);
            band.sizeDelta = new Vector2(BandWidth, 0f);
            band.localRotation = Quaternion.Euler(0f, 0f, BandTilt);
            band.anchoredPosition = new Vector2(-BandWidth * 2f, 0f);

            // Start partway into the rest, so a screen that opens does not fire its shine the same
            // instant its entrance animation plays.
            t = ArcadeTheme.TShine;
        }

        private void OnDisable()
        {
            if (band != null) band.anchoredPosition = new Vector2(-BandWidth * 2f, 0f);
        }

        private void Update()
        {
            if (band == null || area == null) return;

            // Nothing to invite a press on a control that cannot be pressed.
            bool still = ArcadeTheme.ReducedMotion || (owner != null && !owner.IsInteractable());
            if (band.gameObject.activeSelf == still) band.gameObject.SetActive(!still);
            if (still) return;

            t += Time.unscaledDeltaTime;
            float cycle = ArcadeTheme.TShine + ArcadeTheme.ShineRest;
            if (t >= cycle) t -= cycle;

            float from = -BandWidth * 2f;
            float to = area.rect.width + BandWidth * 2f;
            float p = t < ArcadeTheme.TShine ? t / ArcadeTheme.TShine : 1f;
            // Eased at both ends, so the band glides in and out rather than snapping to speed.
            float k = p * p * (3f - 2f * p);
            band.anchoredPosition = new Vector2(Mathf.Lerp(from, to, k), 0f);
        }
    }
}
