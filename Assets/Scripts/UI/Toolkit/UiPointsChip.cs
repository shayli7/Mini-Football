using TableFootball.Net;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// This week's ranked points as a chip: the trophy and the number. Follows
    /// <see cref="Ladder.OnChanged"/>, counting up when the points rise, and is what
    /// <see cref="RewardFly"/> flies trophies to. Reads zero until the ladder has a standing.
    /// </summary>
    public class UiPointsChip : UiCounterChip
    {
        public UiPointsChip() : base(new UiIcon(UiIcon.Glyph.Trophy, ArcadeTheme.Reward, 2.2f)) { }

        public override int Target => Ladder.Standing.Valid ? Ladder.Standing.WeeklyPoints : 0;

        protected override void Follow(bool on)
        {
            Ladder.OnChanged -= Changed;
            if (on) Ladder.OnChanged += Changed;
        }
    }
}
