using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// A line icon drawn with UI Toolkit's vector API — the same glyphs as the design, drawn as
    /// strokes on a 24-unit grid and scaled to whatever size the element is given. No image files:
    /// icons stay crisp at any size and take their colour from code.
    ///
    /// Size it from the stylesheet (width/height) and set <see cref="Color"/> in code; the stroke
    /// scales with the element.
    /// </summary>
    public class UiIcon : VisualElement
    {
        public enum Glyph
        {
            Person, Sliders, Table, Levels, Quests, Store, Coin, Trophy, TwoPlayers, Robot, Globe,
            Play, ChevronLeft, Close, Check, Lock, Clock, Star, Flame, Bolt, Copy, AddPerson, Trash,
            Chest, Music, Speaker
        }

        private Glyph glyph;
        private Color color;
        private float stroke;

        public UiIcon(Glyph glyph, Color color, float stroke = 2f)
        {
            this.glyph = glyph;
            this.color = color;
            this.stroke = stroke;
            pickingMode = PickingMode.Ignore;
            AddToClassList("icon");
            generateVisualContent += Draw;
        }

        public Color Color
        {
            get => color;
            set { color = value; MarkDirtyRepaint(); }
        }

        public Glyph Kind
        {
            get => glyph;
            set { glyph = value; MarkDirtyRepaint(); }
        }

        // ---------- drawing ----------

        private Painter2D p;
        private float s;
        private Vector2 o;

        private Vector2 V(float x, float y) => new Vector2(o.x + x * s, o.y + y * s);

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;

            s = Mathf.Min(r.width, r.height) / 24f;
            o = new Vector2(r.x + (r.width - 24f * s) * 0.5f, r.y + (r.height - 24f * s) * 0.5f);

            p = ctx.painter2D;
            p.strokeColor = color;
            p.fillColor = color;
            p.lineWidth = stroke * s;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            switch (glyph)
            {
                case Glyph.Person:
                    Circle(12f, 8f, 4f);
                    Path(4f, 21f); Curve(4f, 16.6f, 7.6f, 14f, 12f, 14f); Curve(16.4f, 14f, 20f, 16.6f, 20f, 21f); p.Stroke();
                    break;

                case Glyph.Sliders:
                    Line(4f, 6f, 13.5f, 6f); Line(18.5f, 6f, 20f, 6f);
                    Line(4f, 12f, 7.5f, 12f); Line(12.5f, 12f, 20f, 12f);
                    Line(4f, 18f, 15.5f, 18f);
                    Circle(16f, 6f, 2f); Circle(10f, 12f, 2f); Circle(18f, 18f, 2f);
                    break;

                case Glyph.Table:
                    RoundRect(3f, 5f, 18f, 14f, 2f); p.Stroke();
                    Line(12f, 5f, 12f, 19f);
                    Circle(12f, 12f, 2.2f);
                    Path(3f, 10f); To(6f, 10f); To(6f, 14f); To(3f, 14f); p.Stroke();
                    Path(21f, 10f); To(18f, 10f); To(18f, 14f); To(21f, 14f); p.Stroke();
                    break;

                case Glyph.Levels:
                    Path(4f, 20f); To(8f, 20f); To(8f, 15f); To(12f, 15f); To(12f, 10f); To(16f, 10f); To(16f, 5f); To(20f, 5f);
                    p.Stroke();
                    break;

                case Glyph.Quests:
                    Line(9f, 6f, 20f, 6f); Line(9f, 12f, 20f, 12f); Line(9f, 18f, 20f, 18f);
                    Path(3.5f, 6f); To(5f, 7.5f); To(7f, 4.5f); p.Stroke();
                    Path(3.5f, 12f); To(5f, 13.5f); To(7f, 10.5f); p.Stroke();
                    Line(4f, 18f, 6f, 18f);
                    break;

                case Glyph.Store:
                    Path(4.5f, 8.5f); To(19.5f, 8.5f); To(18.2f, 20f); To(5.8f, 20f); p.ClosePath(); p.Stroke();
                    p.BeginPath(); p.MoveTo(V(9f, 11f)); p.LineTo(V(9f, 7f));
                    p.Arc(V(12f, 7f), 3f * s, Angle.Degrees(180f), Angle.Degrees(360f));
                    p.LineTo(V(15f, 11f)); p.Stroke();
                    break;

                case Glyph.Coin:
                    Circle(12f, 12f, 9f); Circle(12f, 12f, 4.5f);
                    break;

                case Glyph.Trophy:
                    Line(8f, 21f, 16f, 21f); Line(12f, 16.5f, 12f, 21f);
                    p.BeginPath(); p.MoveTo(V(7f, 4f)); p.LineTo(V(17f, 4f)); p.LineTo(V(17f, 9f));
                    p.Arc(V(12f, 9f), 5f * s, Angle.Degrees(0f), Angle.Degrees(180f));
                    p.ClosePath(); p.Stroke();
                    Path(7f, 6f); To(4f, 6f); Curve(4f, 8.5f, 5.3f, 10f, 7.3f, 10.2f); p.Stroke();
                    Path(17f, 6f); To(20f, 6f); Curve(20f, 8.5f, 18.7f, 10f, 16.7f, 10.2f); p.Stroke();
                    break;

                case Glyph.TwoPlayers:
                    Circle(8f, 8f, 3f); Circle(16f, 8f, 3f);
                    Path(2f, 20f); Curve(2f, 16.7f, 4.7f, 14f, 8f, 14f); p.Stroke();
                    Path(22f, 20f); Curve(22f, 16.7f, 19.3f, 14f, 16f, 14f); p.Stroke();
                    Path(8f, 14f); Curve(9.5f, 14f, 10.9f, 14.6f, 12f, 15.5f); Curve(13.1f, 14.6f, 14.5f, 14f, 16f, 14f); p.Stroke();
                    break;

                case Glyph.Robot:
                    RoundRect(4f, 7f, 16f, 12f, 3f); p.Stroke();
                    Line(12f, 3f, 12f, 7f);
                    Dot(9f, 12f, 1.3f); Dot(15f, 12f, 1.3f);
                    Line(9f, 16f, 15f, 16f);
                    break;

                case Glyph.Globe:
                    Circle(12f, 12f, 9f);
                    Line(3f, 12f, 21f, 12f);
                    Path(12f, 3f); Curve(15.5f, 6f, 15.5f, 18f, 12f, 21f); Curve(8.5f, 18f, 8.5f, 6f, 12f, 3f); p.Stroke();
                    break;

                case Glyph.Play:
                    p.BeginPath(); p.MoveTo(V(7f, 4f)); p.LineTo(V(20f, 12f)); p.LineTo(V(7f, 20f)); p.ClosePath(); p.Fill();
                    break;

                case Glyph.ChevronLeft:
                    Path(15f, 5f); To(8f, 12f); To(15f, 19f); p.Stroke();
                    break;

                case Glyph.Close:
                    Line(6f, 6f, 18f, 18f); Line(18f, 6f, 6f, 18f);
                    break;

                case Glyph.Check:
                    Path(5f, 12f); To(10f, 17f); To(19f, 7f); p.Stroke();
                    break;

                case Glyph.Lock:
                    RoundRect(5f, 11f, 14f, 10f, 2f); p.Stroke();
                    p.BeginPath(); p.MoveTo(V(8f, 11f)); p.LineTo(V(8f, 8f));
                    p.Arc(V(12f, 8f), 4f * s, Angle.Degrees(180f), Angle.Degrees(360f));
                    p.LineTo(V(16f, 11f)); p.Stroke();
                    break;

                case Glyph.Clock:
                    Circle(12f, 12f, 9f);
                    Path(12f, 7f); To(12f, 12f); To(15f, 14f); p.Stroke();
                    break;

                case Glyph.Star:
                    Path(12f, 2.5f); To(14.4f, 7.5f); To(20f, 8.3f); To(16f, 12.2f); To(17f, 17.7f);
                    To(12f, 15.1f); To(7f, 17.7f); To(8f, 12.2f); To(4f, 8.3f); To(9.6f, 7.5f); p.ClosePath(); p.Fill();
                    break;

                case Glyph.Flame:
                    Path(12f, 3f); Curve(13f, 7f, 17f, 8.5f, 17f, 13f); Curve(17f, 16.3f, 14.8f, 18.5f, 12f, 18.5f);
                    Curve(9.2f, 18.5f, 7f, 16.3f, 7f, 13f); Curve(7f, 10.5f, 8.5f, 9.5f, 9f, 8f);
                    Curve(10f, 9.5f, 11f, 10f, 12f, 10f); Curve(12f, 7.5f, 11f, 5.5f, 12f, 3f); p.Stroke();
                    break;

                case Glyph.Bolt:
                    Path(13f, 2f); To(4f, 14f); To(11f, 14f); To(10f, 22f); To(19f, 10f); To(12f, 10f); p.ClosePath(); p.Stroke();
                    break;

                case Glyph.Copy:
                    RoundRect(8f, 8f, 12f, 12f, 2f); p.Stroke();
                    Path(16f, 8f); To(16f, 5f); To(4f, 5f); To(4f, 16f); To(8f, 16f); p.Stroke();
                    break;

                case Glyph.AddPerson:
                    Circle(9f, 8f, 3.5f);
                    Path(2.5f, 20f); Curve(2.5f, 16.4f, 5.4f, 14f, 9f, 14f); Curve(12.6f, 14f, 15.5f, 16.4f, 15.5f, 20f); p.Stroke();
                    Line(19f, 8f, 19f, 14f); Line(16f, 11f, 22f, 11f);
                    break;

                case Glyph.Trash:
                    Line(4f, 7f, 20f, 7f); Line(10f, 11f, 10f, 17f); Line(14f, 11f, 14f, 17f);
                    Path(6f, 7f); To(7f, 20f); To(17f, 20f); To(18f, 7f); p.Stroke();
                    Path(9f, 7f); To(9f, 4f); To(15f, 4f); To(15f, 7f); p.Stroke();
                    break;

                case Glyph.Chest:
                    Path(3f, 10f); To(21f, 10f); To(21f, 20f); To(3f, 20f); p.ClosePath(); p.Stroke();
                    Path(3f, 10f); To(3f, 8f); Curve(3f, 5.8f, 4.8f, 4f, 7f, 4f); To(17f, 4f);
                    Curve(19.2f, 4f, 21f, 5.8f, 21f, 8f); To(21f, 10f); p.Stroke();
                    RoundRect(10f, 9f, 4f, 5f, 1f); p.Stroke();
                    break;

                case Glyph.Music:
                    Path(9f, 18f); To(9f, 5f); To(20f, 3f); To(20f, 16f); p.Stroke();
                    Circle(6f, 18f, 3f); Circle(17f, 16f, 3f);
                    break;

                case Glyph.Speaker:
                    Path(4f, 9f); To(4f, 15f); To(8f, 15f); To(13f, 19f); To(13f, 5f); To(8f, 9f); p.ClosePath(); p.Stroke();
                    Path(16.5f, 8.5f); Curve(18.5f, 10.5f, 18.5f, 13.5f, 16.5f, 15.5f); p.Stroke();
                    break;
            }
        }

        private void Path(float x, float y)
        {
            p.BeginPath();
            p.MoveTo(V(x, y));
        }

        private void To(float x, float y) => p.LineTo(V(x, y));

        private void Curve(float c1x, float c1y, float c2x, float c2y, float x, float y) =>
            p.BezierCurveTo(V(c1x, c1y), V(c2x, c2y), V(x, y));

        private void Line(float x1, float y1, float x2, float y2)
        {
            Path(x1, y1);
            To(x2, y2);
            p.Stroke();
        }

        private void Circle(float cx, float cy, float r)
        {
            p.BeginPath();
            p.Arc(V(cx, cy), r * s, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Stroke();
        }

        private void Dot(float cx, float cy, float r)
        {
            p.BeginPath();
            p.Arc(V(cx, cy), r * s, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        private void RoundRect(float x, float y, float w, float h, float r)
        {
            p.BeginPath();
            p.MoveTo(V(x + r, y));
            p.LineTo(V(x + w - r, y));
            p.ArcTo(V(x + w, y), V(x + w, y + r), r * s);
            p.LineTo(V(x + w, y + h - r));
            p.ArcTo(V(x + w, y + h), V(x + w - r, y + h), r * s);
            p.LineTo(V(x + r, y + h));
            p.ArcTo(V(x, y + h), V(x, y + h - r), r * s);
            p.LineTo(V(x, y + r));
            p.ArcTo(V(x, y), V(x + r, y), r * s);
            p.ClosePath();
        }
    }
}
