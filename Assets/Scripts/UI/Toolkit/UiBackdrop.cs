using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// The Arena background for a UI Toolkit screen: the baked floor (<see cref="ArcadeTheme.ArenaBase"/>)
    /// with two drifting stadium light cones over it. The counterpart of <c>UIFactory.Backdrop</c>,
    /// which does the same for uGUI screens.
    ///
    /// Laid out to the true screen edge rather than the safe area, so the rim light painted into the
    /// floor shows on every device: the element reaches out past the screen root by exactly the safe-area
    /// insets <see cref="UiToolkitHost"/> reports, and follows them when they change.
    ///
    /// It never takes the pointer — the screen it sits in decides whether touches reach the table.
    /// </summary>
    public static class UiBackdrop
    {
        /// <summary>Peak drift of a cone, in panel units, and the seconds one sweep takes.</summary>
        private const float DriftAmplitude = 22f;
        private const float LeftPeriod = 27f, RightPeriod = 33f;

        /// <summary>
        /// Adds the Arena as the first child of <paramref name="screen"/>, behind everything else.
        /// <paramref name="screen"/> should be a full-screen element inside <see cref="UiToolkitHost.Root"/>.
        /// </summary>
        public static VisualElement AddTo(VisualElement screen)
        {
            var backdrop = UiKit.El("arena", null, "Backdrop");
            backdrop.pickingMode = PickingMode.Ignore;

            var floor = UiKit.El("arena__base", backdrop);
            floor.pickingMode = PickingMode.Ignore;
            floor.style.backgroundImage = new StyleBackground(ArcadeTheme.ArenaBase().texture);

            var left = Cone(backdrop, "arena__cone arena__cone--left", ArcadeTheme.Pulse, false);
            var right = Cone(backdrop, "arena__cone arena__cone--right", ArcadeTheme.ArenaViolet, true);

            screen.Insert(0, backdrop);

            void Fit()
            {
                Vector4 inset = UiToolkitHost.SafeInsets; // left, top, right, bottom
                backdrop.style.left = -inset.x;
                backdrop.style.top = -inset.y;
                backdrop.style.right = -inset.z;
                backdrop.style.bottom = -inset.w;
            }

            backdrop.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                UiToolkitHost.SafeAreaChanged += Fit;
                Fit();
            });
            backdrop.RegisterCallback<DetachFromPanelEvent>(_ => UiToolkitHost.SafeAreaChanged -= Fit);

            // A slow Lissajous drift, at 15 Hz: the motion is far too slow to show the steps, and
            // this keeps the cost of a backdrop that is hidden (its screen closed) negligible.
            backdrop.schedule.Execute(() =>
            {
                if (ArcadeTheme.ReducedMotion) return;
                Drift(left, Time.unscaledTime, LeftPeriod);
                Drift(right, Time.unscaledTime, RightPeriod);
            }).Every(66);

            return backdrop;
        }

        private static VisualElement Cone(VisualElement parent, string classes, Color color, bool mirrored)
        {
            var cone = UiKit.El(classes, parent);
            cone.pickingMode = PickingMode.Ignore;
            cone.style.backgroundImage = new StyleBackground(ArcadeTheme.ArenaCone().texture);
            cone.style.unityBackgroundImageTintColor = color.WithAlpha(ArcadeTheme.ArenaConeAlpha);
            if (mirrored) cone.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
            return cone;
        }

        private static void Drift(VisualElement cone, float now, float period)
        {
            float x = Mathf.Sin(now / period * Mathf.PI * 2f);
            float y = Mathf.Sin(now / (period * 1.618f) * Mathf.PI * 2f);
            cone.style.translate = new Translate(x * DriftAmplitude, y * DriftAmplitude);
        }
    }
}
