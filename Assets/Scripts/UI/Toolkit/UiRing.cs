using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// A ring that fills clockwise from the top: a dim track and a bright arc over it. Anything can sit
    /// inside it — the quests screen puts the player's level there. Size comes from the stylesheet;
    /// the ring is drawn inside whatever square it is given.
    /// </summary>
    public class UiRing : VisualElement
    {
        private float progress;
        private Color track = ArcadeTheme.BgRaised;
        private Color fill = ArcadeTheme.Pulse;
        private float thickness = 10f;

        public UiRing()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        /// <summary>0..1 around the ring.</summary>
        public float Progress
        {
            get => progress;
            set { progress = Mathf.Clamp01(value); MarkDirtyRepaint(); }
        }

        public Color Track
        {
            get => track;
            set { track = value; MarkDirtyRepaint(); }
        }

        public Color Fill
        {
            get => fill;
            set { fill = value; MarkDirtyRepaint(); }
        }

        public float Thickness
        {
            get => thickness;
            set { thickness = value; MarkDirtyRepaint(); }
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float w = resolvedStyle.width, h = resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;

            var p = ctx.painter2D;
            var center = new Vector2(w * 0.5f, h * 0.5f);
            float radius = Mathf.Min(w, h) * 0.5f - thickness * 0.5f;

            p.lineWidth = thickness;
            p.lineCap = LineCap.Butt;

            p.strokeColor = track;
            p.BeginPath();
            p.Arc(center, radius, 0f, 360f);
            p.Stroke();

            if (progress <= 0f) return;

            // Angles in Painter2D run clockwise from 3 o'clock; the ring starts at 12.
            p.strokeColor = fill;
            p.BeginPath();
            p.Arc(center, radius, -90f, -90f + 360f * progress);
            p.Stroke();
        }
    }
}
