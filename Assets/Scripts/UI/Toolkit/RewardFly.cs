using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// The reward fly-in: a handful of coins (or trophies) burst out of where a reward came from, then
    /// speed home to the counter that holds it while the number runs up, swelling and glowing at every
    /// hit with a note that climbs. It makes the number arriving something you watch rather than a
    /// figure that changes.
    ///
    /// Call it AFTER the reward has been credited — the counter's <see cref="IRewardCounter.Target"/>
    /// is where the count ends, and the start is worked back from it. The counter is frozen for the
    /// flight and snapped to the real value at the end, so the animation can never leave it wrong.
    /// Coins first and trophies second is done by starting the second from the first's
    /// <paramref name="onDone"/>.
    ///
    /// Everything is on unscaled time (the front end runs at <c>timeScale 0</c>). With reduced motion
    /// nothing flies: the counter simply counts up by itself.
    /// </summary>
    public static class RewardFly
    {
        public enum Kind { Coins, Trophies }

        private const float PieceSize = 30f;
        private const float BurstSeconds = 0.30f;
        private const float HoldSeconds = 0.05f;
        private const float HomeSeconds = 0.55f;
        private const float StaggerSeconds = 0.05f;

        private static VisualElement overlay;

        /// <summary>
        /// Flies <paramref name="amount"/> worth of pieces from <paramref name="from"/> (the middle of
        /// the screen if null) to <paramref name="to"/>. <paramref name="onDone"/> runs when the last
        /// piece has landed and the counter is at its real value.
        /// </summary>
        public static void Play(Kind kind, VisualElement from, IRewardCounter to, int amount, Action onDone = null)
        {
            if (to == null || amount <= 0 || to.Element.panel == null)
            {
                // Nothing to fly, but the caller may have frozen the counter to hold its number.
                to?.Freeze(false);
                onDone?.Invoke();
                return;
            }

            // The counter does its own quick count-up, and that is all there is to show.
            if (ArcadeTheme.ReducedMotion)
            {
                to.Freeze(false);
                onDone?.Invoke();
                return;
            }

            EnsureOverlay();
            new Flight(kind, from, to, amount, onDone).Start();
        }

        private static void EnsureOverlay()
        {
            VisualElement host = UiToolkitHost.Root;
            if (overlay == null || overlay.parent != host)
            {
                overlay?.RemoveFromHierarchy();
                overlay = new VisualElement { name = "RewardFly", pickingMode = PickingMode.Ignore };
                overlay.style.position = Position.Absolute;
                overlay.style.left = 0;
                overlay.style.top = 0;
                overlay.style.right = 0;
                overlay.style.bottom = 0;
                host.Add(overlay);
            }

            overlay.BringToFront();
        }

        private static Vector2 CenterOf(VisualElement e)
        {
            Rect r = e != null ? e.worldBound : default;
            Vector2 world = float.IsNaN(r.x) || r.width <= 0f ? overlay.worldBound.center : r.center;
            return overlay.WorldToLocal(world);
        }

        private sealed class Piece
        {
            public VisualElement E;
            public Vector2 BurstEnd, Control;
            public float Delay;
            public bool Landed;
        }

        private sealed class Flight
        {
            private readonly Kind kind;
            private readonly VisualElement from;
            private readonly IRewardCounter to;
            private readonly int amount;
            private readonly Action onDone;

            private Piece[] pieces;
            private Vector2 origin, target;
            private int startValue, landed;
            private float begin;
            private IVisualElementScheduledItem tick;

            public Flight(Kind kind, VisualElement from, IRewardCounter to, int amount, Action onDone)
            {
                this.kind = kind;
                this.from = from;
                this.to = to;
                this.amount = amount;
                this.onDone = onDone;
            }

            public void Start()
            {
                origin = CenterOf(from);
                target = CenterOf(to.Element);

                to.Freeze(true);
                startValue = Mathf.Max(0, to.Target - amount);
                to.SetNow(startValue);

                int count = Mathf.Clamp(8 + amount / 150, 8, 12);
                pieces = new Piece[count];
                for (int i = 0; i < count; i++)
                {
                    VisualElement e = kind == Kind.Coins
                        ? new UiCoin()
                        : (VisualElement)new UiIcon(UiIcon.Glyph.Trophy, ArcadeTheme.Reward, 2.4f);
                    e.pickingMode = PickingMode.Ignore;
                    e.style.position = Position.Absolute;
                    e.style.width = PieceSize;
                    e.style.height = PieceSize;
                    e.style.opacity = 0f;
                    overlay.Add(e);

                    // Spread round the source, with a little jitter so it reads as a burst, not a ring.
                    float angle = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) / count * Mathf.PI * 2f;
                    float radius = UnityEngine.Random.Range(55f, 120f);
                    Vector2 burstEnd = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                    // The home path bows to alternate sides so the stream does not travel in one line.
                    Vector2 mid = (burstEnd + target) * 0.5f;
                    Vector2 dir = (target - burstEnd).normalized;
                    Vector2 side = new Vector2(-dir.y, dir.x) * (i % 2 == 0 ? 1f : -1f);
                    pieces[i] = new Piece
                    {
                        E = e,
                        BurstEnd = burstEnd,
                        Control = mid + side * UnityEngine.Random.Range(30f, 80f),
                        Delay = i * StaggerSeconds
                    };
                }

                begin = Time.unscaledTime;
                GameSfx.PlayUi(UiSound.Reward);
                tick = overlay.schedule.Execute(Tick).Every(16);
            }

            private void Tick()
            {
                float t = Time.unscaledTime - begin;

                foreach (Piece p in pieces)
                {
                    if (p.Landed) continue;

                    float local = t - p.Delay;
                    if (local < 0f) continue;

                    Vector2 pos;
                    float scale;
                    if (local < BurstSeconds)
                    {
                        float u = local / BurstSeconds;
                        float k = 1f - (1f - u) * (1f - u) * (1f - u);
                        pos = Vector2.LerpUnclamped(origin, p.BurstEnd, k);
                        scale = Mathf.Lerp(0.5f, 1.1f, k);
                    }
                    else if (local < BurstSeconds + HoldSeconds)
                    {
                        pos = p.BurstEnd;
                        scale = 1.1f;
                    }
                    else if (local < BurstSeconds + HoldSeconds + HomeSeconds)
                    {
                        float u = (local - BurstSeconds - HoldSeconds) / HomeSeconds;
                        float k = u * u;
                        float a = 1f - k;
                        pos = a * a * p.BurstEnd + 2f * a * k * p.Control + k * k * target;
                        scale = Mathf.Lerp(1.1f, 0.8f, u);
                    }
                    else
                    {
                        p.Landed = true;
                        p.E.style.opacity = 0f;
                        Land();
                        continue;
                    }

                    p.E.style.opacity = 1f;
                    p.E.style.left = pos.x - PieceSize * 0.5f;
                    p.E.style.top = pos.y - PieceSize * 0.5f;
                    p.E.style.scale = new Scale(new Vector3(scale, scale, 1f));
                }
            }

            private void Land()
            {
                landed++;
                to.SetNow(startValue + Mathf.RoundToInt(amount * (float)landed / pieces.Length));
                to.Pulse();
                GameSfx.PlayUi(UiSound.Step, 1f + 0.05f * landed);

                if (landed < pieces.Length) return;

                tick.Pause();
                foreach (Piece p in pieces) p.E.RemoveFromHierarchy();
                to.Freeze(false);
                onDone?.Invoke();
            }
        }
    }
}
