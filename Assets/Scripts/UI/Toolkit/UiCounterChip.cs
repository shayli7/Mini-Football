using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// A number that <see cref="RewardFly"/> can count up as pieces land on it: the coin balance, the
    /// ranked points. The fly-in freezes it, drives the number with <see cref="SetNow"/> and
    /// <see cref="Pulse"/> for each piece that arrives, then unfreezes it.
    /// </summary>
    public interface IRewardCounter
    {
        /// <summary>The element pieces fly to.</summary>
        VisualElement Element { get; }

        /// <summary>What the number reads once everything has landed — the real balance. The fly-in
        /// works out where to count up from by taking the reward off this, so it must be called after
        /// the reward has been credited.</summary>
        int Target { get; }

        /// <summary>While frozen the counter does not follow its source on its own. Unfreezing snaps to
        /// <see cref="Target"/>.</summary>
        void Freeze(bool value);

        void SetNow(int value);

        void Pulse();
    }

    /// <summary>
    /// A chip holding an icon and a number that keeps itself current: it follows its source for as long
    /// as it is on a panel, and when the number rises it runs to the new value quickly and pulses, so a
    /// gain is seen arriving rather than appearing. A fall is shown at once.
    ///
    /// Subclasses say what the number is (<see cref="Target"/>) and which event to follow
    /// (<see cref="Follow"/>). Size and colour come from the <c>coinchip</c> classes in Base.uss.
    /// </summary>
    public abstract class UiCounterChip : UiFrame, IRewardCounter
    {
        /// <summary>How long the number takes to run to a new value, in seconds. Quick on purpose.</summary>
        private const float CountSeconds = 0.55f;

        private readonly Label label;
        private IVisualElementScheduledItem run;
        private int shown;
        private int from, to;
        private float runStart;
        private bool frozen;

        protected UiCounterChip(VisualElement icon) : base("coinchip")
        {
            Add(icon);
            label = UiKit.Text("0", "coinchip__value f-display", this);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Follow(false);
                Follow(true);
                SetNow(Target);
            });
            RegisterCallback<DetachFromPanelEvent>(_ => Follow(false));
        }

        public VisualElement Element => this;

        /// <summary>The real number, from its source.</summary>
        public abstract int Target { get; }

        /// <summary>Subscribes (<c>true</c>) or unsubscribes (<c>false</c>) <see cref="Changed"/> to the
        /// source's change event. Always called with <c>false</c> first, so it never double-subscribes.</summary>
        protected abstract void Follow(bool on);

        /// <summary>The number currently drawn.</summary>
        public int Shown => shown;

        public void Freeze(bool value)
        {
            frozen = value;
            if (value)
            {
                // A run already under way would keep moving the number the fly-in is about to drive.
                run?.Pause();
                run = null;
            }
            else
            {
                SetNow(Target);
            }
        }

        /// <summary>Shows a number at once, cancelling any run in progress.</summary>
        public void SetNow(int value)
        {
            run?.Pause();
            run = null;
            shown = value;
            label.text = value.ToString("N0");
        }

        /// <summary>A quick swell and glow, for each piece that lands.</summary>
        public void Pulse()
        {
            if (ArcadeTheme.ReducedMotion) return;

            style.scale = new Scale(new Vector3(1.14f, 1.14f, 1f));
            OverrideEdge(ArcadeTheme.Reward);
            schedule.Execute(() =>
            {
                style.scale = new Scale(Vector3.one);
                OverrideEdge(ArcadeTheme.LineLit);
            }).StartingIn(140);
        }

        /// <summary>The source's change handler.</summary>
        protected void Changed()
        {
            if (frozen) return;

            int target = Target;
            if (target == shown) return;

            // A fall, or a reduced-motion player: no run, just the number.
            if (target < shown || ArcadeTheme.ReducedMotion)
            {
                SetNow(target);
                return;
            }

            from = shown;
            to = target;
            runStart = Time.unscaledTime;
            run?.Pause();
            run = schedule.Execute(Step).Every(16);
            Pulse();
        }

        private void Step()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - runStart) / CountSeconds);
            float k = 1f - (1f - t) * (1f - t) * (1f - t);
            shown = Mathf.RoundToInt(Mathf.Lerp(from, to, k));
            label.text = shown.ToString("N0");

            if (t >= 1f)
            {
                run.Pause();
                run = null;
            }
        }
    }
}
