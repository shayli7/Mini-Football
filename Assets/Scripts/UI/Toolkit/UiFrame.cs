using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// A box with its top-left and bottom-right corners cut on the diagonal — the shape of every panel
    /// and button in the Holo Arena style. USS can round a corner but cannot cut one, so the shape is
    /// drawn here with the vector API, and the element's own USS background and border stay unused.
    ///
    /// Looks are read from custom USS properties, so they stay in the stylesheet:
    /// <c>--frame-fill</c>, <c>--frame-edge</c>, <c>--frame-edge-width</c> (px), <c>--frame-cut</c>
    /// (px along each edge), <c>--frame-tab</c> (a short accent bar on the top edge; transparent
    /// means none) and <c>--frame-slant</c> (px; above zero the shape is a parallelogram instead —
    /// an energy cell). Base.uss defines the <c>frame</c> classes and the button variants.
    ///
    /// The values are written as literal colours in the stylesheet, not <c>var()</c> references:
    /// custom properties are read once per style change, and a literal is the case that can be relied on.
    /// </summary>
    public class UiFrame : VisualElement
    {
        private static readonly CustomStyleProperty<Color> FillProp = new CustomStyleProperty<Color>("--frame-fill");
        private static readonly CustomStyleProperty<Color> EdgeProp = new CustomStyleProperty<Color>("--frame-edge");
        private static readonly CustomStyleProperty<Color> TabProp = new CustomStyleProperty<Color>("--frame-tab");
        private static readonly CustomStyleProperty<float> EdgeWidthProp = new CustomStyleProperty<float>("--frame-edge-width");
        private static readonly CustomStyleProperty<float> CutProp = new CustomStyleProperty<float>("--frame-cut");
        private static readonly CustomStyleProperty<float> SlantProp = new CustomStyleProperty<float>("--frame-slant");

        // Sensible defaults for a frame that is drawn before its style resolves, or whose stylesheet is
        // missing: a visible panel, never nothing.
        private Color fill = ArcadeTheme.BgPanel;
        private Color edge = ArcadeTheme.LineLit;
        private Color tab = new Color(0f, 0f, 0f, 0f);
        private float edgeWidth = 1f;
        private float cut = ArcadeTheme.Cut;
        private float slant;

        private const float TabLeft = 30f, TabWidth = 64f, TabHeight = 2f;

        public UiFrame()
        {
            AddToClassList("frame");
            RegisterCallback<CustomStyleResolvedEvent>(OnStyleResolved);
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            generateVisualContent += Draw;
        }

        public UiFrame(string classes) : this()
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (var c in classes.Split(' '))
            {
                if (c.Length > 0) AddToClassList(c);
            }
        }

        // A colour chosen in code (a rarity, a league) that the stylesheet must not paint over.
        private bool fillOverridden, edgeOverridden;

        /// <summary>Sets the edge colour from code, for a colour only known at run time. The stylesheet's
        /// <c>--frame-edge</c> no longer applies to this frame.</summary>
        public void OverrideEdge(Color color)
        {
            edge = color;
            edgeOverridden = true;
            MarkDirtyRepaint();
        }

        /// <summary>Sets the fill colour from code. The stylesheet's <c>--frame-fill</c> no longer applies.</summary>
        public void OverrideFill(Color color)
        {
            fill = color;
            fillOverridden = true;
            MarkDirtyRepaint();
        }

        private void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            var style = evt.customStyle;
            if (!fillOverridden && style.TryGetValue(FillProp, out Color f)) fill = f;
            if (!edgeOverridden && style.TryGetValue(EdgeProp, out Color e)) edge = e;
            if (style.TryGetValue(TabProp, out Color t)) tab = t;
            if (style.TryGetValue(EdgeWidthProp, out float w)) edgeWidth = w;
            if (style.TryGetValue(CutProp, out float c)) cut = c;
            slant = style.TryGetValue(SlantProp, out float s) ? s : 0f;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float width = resolvedStyle.width;
            float height = resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f) return;

            var p = ctx.painter2D;
            float maxCut = Mathf.Min(width, height) * 0.5f;
            float outerCut = Mathf.Min(cut, maxCut);

            // A slanted frame is a parallelogram (an energy cell) instead of a cut-corner box.
            bool slanted = slant > 0f;

            // The edge is the whole shape in the edge colour, and the fill sits inside it. Where the
            // fill is opaque that leaves a ring of even thickness, diagonals included.
            if (edgeWidth > 0f && edge.a > 0f)
            {
                p.fillColor = edge;
                if (slanted) Slanted(p, 0f, 0f, width, height, slant);
                else Polygon(p, 0f, 0f, width, height, outerCut);
                p.Fill();
            }

            float inset = edge.a > 0f ? edgeWidth : 0f;
            if (fill.a > 0f)
            {
                p.fillColor = fill;
                if (slanted)
                {
                    Slanted(p, inset, inset, width - inset * 2f, height - inset * 2f, slant);
                }
                else
                {
                    // A diagonal moved in by `inset` needs its cut shortened by inset * (2 - sqrt 2)
                    // to stay parallel to the outer one at the same distance.
                    float innerCut = Mathf.Max(0f, outerCut - inset * 0.5858f);
                    Polygon(p, inset, inset, width - inset * 2f, height - inset * 2f, innerCut);
                }
                p.Fill();
            }

            if (tab.a > 0f)
            {
                p.fillColor = tab;
                p.BeginPath();
                p.MoveTo(new Vector2(TabLeft, inset));
                p.LineTo(new Vector2(TabLeft + TabWidth, inset));
                p.LineTo(new Vector2(TabLeft + TabWidth, inset + TabHeight));
                p.LineTo(new Vector2(TabLeft, inset + TabHeight));
                p.ClosePath();
                p.Fill();
            }
        }

        /// <summary>A parallelogram leaning right: the top edge is pushed <paramref name="lean"/> to the
        /// right of the bottom edge.</summary>
        private static void Slanted(Painter2D p, float x, float y, float w, float h, float lean)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x + lean, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w - lean, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
        }

        /// <summary>A rectangle with the top-left and bottom-right corners cut by <paramref name="cutLength"/>.</summary>
        private static void Polygon(Painter2D p, float x, float y, float w, float h, float cutLength)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x + cutLength, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h - cutLength));
            p.LineTo(new Vector2(x + w - cutLength, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.LineTo(new Vector2(x, y + cutLength));
            p.ClosePath();
        }
    }
}
