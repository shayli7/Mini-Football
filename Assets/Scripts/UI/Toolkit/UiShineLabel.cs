using System.Text;
using TableFootball.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// A label for a player's OWN name: plain text, or — when the Shining Gold name style is worn — gold
    /// text with a glint that sweeps across it, rests, and sweeps again, for as long as it is on screen.
    ///
    /// A drop-in for <c>Label</c>: give it the same classes and set its text with <see cref="Set"/>
    /// rather than <c>.text</c>, because the animation rewrites the text every few frames and needs the
    /// plain string to start from. It follows <see cref="Inventory.OnChanged"/> on its own, so equipping
    /// or taking off the style in the Collection is reflected without the screen doing anything.
    ///
    /// The glint is per-letter colour (rich text), since a Label cannot take a gradient. Reduced motion
    /// leaves the gold and drops the glint. A name that contains markup characters is shown gold but
    /// still, rather than risk the text being read as tags.
    /// </summary>
    public class UiShineLabel : Label
    {
        private static readonly Color Gold = ArcadeTheme.Reward;
        private static readonly Color Glint = new Color(1f, 0.96f, 0.8f);

        /// <summary>One loop: the sweep, then a rest.</summary>
        private const float PeriodSeconds = 2.8f;
        private const float SweepSeconds = 1.0f;
        private const float FrameMs = 40;

        private string plain = string.Empty;
        private bool showcase;
        private bool mine = true;
        private IVisualElementScheduledItem tick;
        private readonly StringBuilder sb = new StringBuilder();

        public UiShineLabel()
        {
            enableRichText = true;
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Inventory.OnChanged -= Refresh;
                Inventory.OnChanged += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Inventory.OnChanged -= Refresh;
                StopTick();
            });
        }

        /// <summary>
        /// Sets the name. <paramref name="isMine"/> false draws it plain whatever is worn — another
        /// player's name never takes your style. <paramref name="alwaysGold"/> shows the style
        /// regardless of what is worn, for the picture of the prize itself.
        /// </summary>
        public void Set(string name, bool isMine = true, bool alwaysGold = false)
        {
            plain = name ?? string.Empty;
            mine = isMine;
            showcase = alwaysGold;
            Refresh();
        }

        private void Refresh()
        {
            bool gold = showcase || (mine && Inventory.IsEquipped(LevelPath.GoldNameId));

            if (!gold)
            {
                StopTick();
                style.color = StyleKeyword.Null;
                text = plain;
                return;
            }

            style.color = Gold;
            text = plain;

            if (ArcadeTheme.ReducedMotion || plain.IndexOf('<') >= 0 || plain.Length == 0) return;

            if (tick == null) tick = schedule.Execute(Step).Every((long)FrameMs);
            else tick.Resume();
        }

        private void StopTick()
        {
            tick?.Pause();
        }

        private float lastPhase = -1f;

        private void Step()
        {
            // Nothing to draw on a screen that is not being shown.
            if (!(layout.width > 0f)) return;

            float t = Time.unscaledTime % PeriodSeconds;
            if (t >= SweepSeconds)
            {
                // Resting: plain gold, written once.
                if (lastPhase != -2f)
                {
                    text = plain;
                    lastPhase = -2f;
                }
                return;
            }

            lastPhase = t;
            int n = plain.Length;
            float u = t / SweepSeconds;
            // The glint enters one width off the left and leaves one width off the right.
            float width = Mathf.Max(2.5f, n * 0.22f);
            float centre = Mathf.Lerp(-width, n + width, u);

            sb.Clear();
            for (int i = 0; i < n; i++)
            {
                float k = Mathf.Clamp01(1f - Mathf.Abs(i + 0.5f - centre) / width);
                k = k * k * (3f - 2f * k);
                Color c = Color.Lerp(Gold, Glint, k);
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append('>').Append(plain[i]).Append("</color>");
            }

            text = sb.ToString();
        }
    }
}
