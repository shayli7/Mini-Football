using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// A flat-top hexagon with a coloured ring and a dark centre — the player's avatar. Looks come from
    /// code (<see cref="Edge"/>, <see cref="Fill"/>) because a hexagon is a shape USS cannot draw;
    /// size it from the stylesheet, and put a label in it for the initial.
    /// </summary>
    public class UiHex : VisualElement
    {
        private Color edge = ArcadeTheme.Pulse;
        private Color fill = ArcadeTheme.Plate;

        public UiHex()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }

        public Color Edge
        {
            get => edge;
            set { edge = value; MarkDirtyRepaint(); }
        }

        public Color Fill
        {
            get => fill;
            set { fill = value; MarkDirtyRepaint(); }
        }

        private void Draw(MeshGenerationContext ctx)
        {
            float w = resolvedStyle.width, h = resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;

            var p = ctx.painter2D;
            float ring = Mathf.Min(w, h) * 0.06f;

            p.fillColor = edge;
            Hexagon(p, 0f, 0f, w, h);
            p.Fill();

            p.fillColor = fill;
            Hexagon(p, ring, ring, w - ring * 2f, h - ring * 2f);
            p.Fill();
        }

        private static void Hexagon(Painter2D p, float x, float y, float w, float h)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x + w * 0.25f, y));
            p.LineTo(new Vector2(x + w * 0.75f, y));
            p.LineTo(new Vector2(x + w, y + h * 0.5f));
            p.LineTo(new Vector2(x + w * 0.75f, y + h));
            p.LineTo(new Vector2(x + w * 0.25f, y + h));
            p.LineTo(new Vector2(x, y + h * 0.5f));
            p.ClosePath();
        }
    }

    /// <summary>
    /// A row of slanted cells that fills from the left, like a signal meter — used for volume and
    /// sensitivity. Tap a cell to set the level to it, or drag along the row. Tapping the last lit cell
    /// steps down by one, so a level can be lowered (down to <c>min</c>) without a separate button.
    ///
    /// <see cref="OnChanged"/> fires only for a change the PLAYER made; setting <see cref="Value"/> from
    /// code does not raise it.
    /// </summary>
    public class UiCells : VisualElement
    {
        private readonly List<UiFrame> cells = new List<UiFrame>();
        private readonly int min;
        private int value;

        /// <summary>Raised with the new level when the player changes it.</summary>
        public Action<int> OnChanged;

        public UiCells(int count, int min = 0)
        {
            this.min = min;
            AddToClassList("cells");
            pickingMode = PickingMode.Position;

            for (int i = 0; i < count; i++)
            {
                var cell = new UiFrame("cell") { pickingMode = PickingMode.Ignore };
                cells.Add(cell);
                Add(cell);
            }

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCancelEvent>(OnCancel);
        }

        public int Count => cells.Count;

        public int Value
        {
            get => value;
            set
            {
                this.value = Mathf.Clamp(value, min, cells.Count);
                for (int i = 0; i < cells.Count; i++) cells[i].EnableInClassList("is-lit", i < this.value);
            }
        }

        private void OnDown(PointerDownEvent evt)
        {
            this.CapturePointer(evt.pointerId);

            int hit = LevelAt(evt.position.x);
            // Pressing the last lit cell steps down one, the way to turn a level down from the row itself.
            if (hit == value && hit > min) hit--;
            Apply(hit);
            evt.StopPropagation();
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (!this.HasPointerCapture(evt.pointerId)) return;
            Apply(LevelAt(evt.position.x));
        }

        private void OnUp(PointerUpEvent evt)
        {
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
        }

        private void OnCancel(PointerCancelEvent evt)
        {
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
        }

        /// <summary>The level a pointer at panel x means: how many cells start at or left of it.</summary>
        private int LevelAt(float panelX)
        {
            int level = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].worldBound.xMin <= panelX) level = i + 1;
            }
            return Mathf.Clamp(level, min, cells.Count);
        }

        private void Apply(int level)
        {
            if (level == value) return;
            Value = level;
            OnChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// An on/off switch: a cut-corner track with a square knob and the word ON or OFF. It only shows a
    /// state — the row it sits in is the tap target, so the whole row can be pressed.
    /// </summary>
    public class UiSwitch : UiFrame
    {
        private readonly Label word;

        public UiSwitch() : base("switch")
        {
            pickingMode = PickingMode.Ignore;
            word = UiKit.Text("OFF", "switch__word f-display", this);
            UiKit.El("switch__knob", this).pickingMode = PickingMode.Ignore;
        }

        public bool On
        {
            get => ClassListContains("is-on");
            set
            {
                EnableInClassList("is-on", value);
                word.text = value ? "ON" : "OFF";
            }
        }
    }
}
