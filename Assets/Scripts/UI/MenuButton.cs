using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TableFootball.UI
{
    /// <summary>
    /// The Arcade Neon button. Subclasses <see cref="Selectable"/> so it gets keyboard / gamepad
    /// navigation and focus for free, then overrides the state transition to drive the design-system
    /// visuals: scale to 1.04 + neon glow on hover/focus, dip to 0.97 on press, dim to 0.4 disabled.
    ///
    /// All five states from MASTER.md are covered: default, hover, focus (Selected), active (Pressed),
    /// disabled. Motion runs on unscaled time so it works while the game is paused.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MenuButton : Selectable, IPointerClickHandler, ISubmitHandler
    {
        public enum Variant { Primary, Ghost, Danger, Neutral, IconGold }

        public UnityEvent onClick = new UnityEvent();

        [HideInInspector] public Variant variant = Variant.Neutral;
        [HideInInspector] public Image fill;
        [HideInInspector] public Image border;
        [HideInInspector] public Image glow;
        [HideInInspector] public TextMeshProUGUI label;

        private Coroutine scaleRoutine;
        private bool noScale;

        // Resolved per-variant colors, filled by Configure().
        private Color fillBase, borderBase, borderHover, labelBase, labelHover, glowColor;
        private float glowBase, glowHover;

        public void Configure(Variant v)
        {
            variant = v;
            switch (v)
            {
                case Variant.Primary:
                    fillBase = ArcadeTheme.Gold; borderBase = ArcadeTheme.Gold; borderHover = ArcadeTheme.Gold;
                    labelBase = ArcadeTheme.OnGold; labelHover = ArcadeTheme.OnGold;
                    // No resting glow. A halo that is always on is not feedback, it is decoration,
                    // and it reads as a stuck highlight sitting around the button.
                    glowColor = ArcadeTheme.Gold; glowBase = 0f; glowHover = 0.9f;
                    break;
                case Variant.Ghost:
                    fillBase = ArcadeTheme.BgRaised; borderBase = ArcadeTheme.Line; borderHover = ArcadeTheme.Blue;
                    labelBase = ArcadeTheme.Ink; labelHover = Color.white;
                    glowColor = ArcadeTheme.Blue; glowBase = 0f; glowHover = 0.85f;
                    break;
                case Variant.Danger:
                    fillBase = ArcadeTheme.BgRaised; borderBase = ArcadeTheme.Line; borderHover = ArcadeTheme.Red;
                    labelBase = ArcadeTheme.Ink; labelHover = Color.white;
                    glowColor = ArcadeTheme.Red; glowBase = 0f; glowHover = 0.85f;
                    break;
                case Variant.IconGold:
                    fillBase = ArcadeTheme.BgRaised.WithAlpha(0.55f); borderBase = ArcadeTheme.Gold; borderHover = ArcadeTheme.Gold;
                    labelBase = ArcadeTheme.Gold; labelHover = ArcadeTheme.Gold;
                    glowColor = ArcadeTheme.Gold; glowBase = 0f; glowHover = 0.9f;
                    break;
                default:
                    fillBase = ArcadeTheme.BgRaised; borderBase = ArcadeTheme.Line; borderHover = ArcadeTheme.Gold;
                    labelBase = ArcadeTheme.Ink; labelHover = Color.white;
                    glowColor = ArcadeTheme.Gold; glowBase = 0f; glowHover = 0.8f;
                    break;
            }

            // Hover does not repaint the control: the border and halo hold whatever they rest at, and
            // the highlight is carried by the scale lift and the label alone. A lit outline reads as a
            // box being drawn around the thing rather than as a response to the cursor, and it was
            // loudest on exactly the controls that matter most — the big picture cards.
            //
            // The per-variant hover values above still set the accent colour SetAccent uses, so a
            // control that genuinely should glow can still ask for one.
            borderHover = borderBase;
            glowHover = glowBase;

            ApplyStatic();
        }

        /// <summary>
        /// Marks this control as the screen's primary choice: a permanent accent border and a faint
        /// resting glow.
        ///
        /// The variants above deliberately have no resting glow, because a halo that is always on is
        /// decoration rather than feedback. This is the exception that proves it — exactly one control
        /// per screen may light up at rest, and because it is the only one it reads as "start here"
        /// rather than as a highlight stuck on the last thing tapped.
        /// </summary>
        public void SetAccent(Color color, float restingGlow)
        {
            borderBase = color;
            borderHover = color;
            glowColor = color;
            glowBase = restingGlow;
            // Held equal to the resting value, or hovering an accented control would dim the very
            // halo that marks it out.
            glowHover = restingGlow;
            ApplyStatic();
        }

        /// <summary>
        /// Stops this control growing under the pointer.
        ///
        /// For a list row: a strip the full width of the panel scaling up has nowhere to grow into,
        /// so it pushes past the panel edge and shoves its neighbours, and a list where pointing at
        /// one row nudges the others reads as unstable rather than responsive.
        /// </summary>
        public void SetNoScale()
        {
            noScale = true;
            transform.localScale = Vector3.one;
        }

        protected override void Awake()
        {
            base.Awake();
            transition = Transition.None; // we do our own visuals in DoStateTransition
        }

        private void ApplyStatic()
        {
            if (fill != null) fill.color = fillBase;
            if (border != null) border.color = borderBase;
            if (label != null) label.color = labelBase;
            if (glow != null) glow.color = glowColor.WithAlpha(glowBase);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            if (!Application.isPlaying) return;

            bool disabled = state == SelectionState.Disabled;
            bool pressed = state == SelectionState.Pressed;

            // Selected deliberately does NOT light the button. The EventSystem leaves whatever was
            // last tapped selected, and a touchscreen never sends a pointer-exit to clear it, so
            // treating selection as a highlight leaves a glow stranded on the last button pressed —
            // and on any button a menu focuses when it opens. Only genuine hover and press light up.
            bool active = state == SelectionState.Highlighted;

            // Disabled
            if (disabled)
            {
                SetScale(Vector3.one, instant);
                if (fill != null) fill.color = fillBase.WithAlpha(0.4f);
                if (border != null) border.color = borderBase.WithAlpha(0.4f);
                if (label != null) label.color = labelBase.WithAlpha(0.4f);
                if (glow != null) glow.color = glowColor.WithAlpha(0f);
                return;
            }

            float targetScale = noScale
                ? 1f
                : (pressed ? ArcadeTheme.PressScale : (active ? ArcadeTheme.HoverScale : 1f));
            // Springs back past rest when released, but goes to hover and press flat. Overshooting
            // into a hover state fights the pointer that is still arriving; overshooting on the way
            // home is what makes the release feel like a release.
            bool released = !pressed && !active;
            SetScale(Vector3.one * targetScale, instant,
                     released ? ArcadeTheme.EaseOutBack : ArcadeTheme.EaseSnap);

            bool lit = active || pressed;
            if (fill != null) fill.color = fillBase;
            if (border != null) border.color = lit ? borderHover : borderBase;
            if (label != null) label.color = lit ? labelHover : labelBase;
            if (glow != null) glow.color = glowColor.WithAlpha(lit ? glowHover : glowBase);
        }

        private void SetScale(Vector3 target, bool instant, Func<float, float> ease = null)
        {
            var t = transform;
            if (instant || !isActiveAndEnabled || ArcadeTheme.ReducedMotion)
            {
                if (scaleRoutine != null) { StopCoroutine(scaleRoutine); scaleRoutine = null; }
                t.localScale = target;
                return;
            }
            if (scaleRoutine != null) StopCoroutine(scaleRoutine);
            scaleRoutine = StartCoroutine(UITween.ScaleTo(t, t.localScale, target, ArcadeTheme.TFast,
                                                          ease ?? ArcadeTheme.EaseSnap));
        }

        // Selectable already tracks hover state and drives the visuals through DoStateTransition,
        // so these only need to forward.
        public override void OnPointerEnter(PointerEventData e) { base.OnPointerEnter(e); }
        public override void OnPointerExit(PointerEventData e)  { base.OnPointerExit(e); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsInteractable() && eventData.button == PointerEventData.InputButton.Left)
            {
                // Hooked here rather than at each call site, so every button and tile in the game
                // clicks — including any added later — without anyone remembering to wire it.
                GameSfx.PlayUiClick();
                onClick.Invoke();
            }
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!IsInteractable()) return;
            // Clicks here too: submitting with a keyboard or gamepad is the same action as tapping,
            // and it was the one path through the button that made no sound.
            GameSfx.PlayUiClick();
            onClick.Invoke();
        }
    }
}
