namespace TableFootball
{
    /// <summary>
    /// The points a ranked result is worth, per league. Pure arithmetic, no Unity dependency, so the
    /// rule can be reasoned about and reused on the server (Cloud Code) unchanged.
    ///
    /// The design, from the brief: a loss costs LESS than a win at the low leagues, so a beginner
    /// climbs quickly; as the league rises the win shrinks and the loss grows, until Diamond, where
    /// they are equal (50/50) and only real skill moves you. The numbers below are the starting
    /// point — deliberately a small, readable table rather than a formula, so they are easy to tune.
    /// </summary>
    public static class EloRating
    {
        /// <summary>Points gained for a win in this league.</summary>
        public static int WinDelta(League league)
        {
            switch (league)
            {
                case League.Bronze: return 32;
                case League.Silver: return 28;
                case League.Gold: return 24;
                case League.Diamond: return 20;
                default: return 24;
            }
        }

        /// <summary>Points lost for a loss in this league, as a positive magnitude.</summary>
        public static int LossDelta(League league)
        {
            switch (league)
            {
                case League.Bronze: return 8;
                case League.Silver: return 14;
                case League.Gold: return 18;
                case League.Diamond: return 20; // equal to the win — 50/50 at the top
                default: return 16;
            }
        }

        /// <summary>
        /// Signed points change for a result: a win adds WinDelta, a loss subtracts LossDelta. The
        /// caller floors weekly points at zero — this only reports the delta.
        ///
        /// opponentRating is accepted for a future skill-scaled adjustment (and is what matchmaking
        /// pools on), but the base rule the brief describes is flat per-league, so it is unused today.
        /// </summary>
        public static int Delta(League league, bool won) => won ? WinDelta(league) : -LossDelta(league);
    }
}
