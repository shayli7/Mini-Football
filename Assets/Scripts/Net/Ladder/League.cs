namespace TableFootball
{
    /// <summary>
    /// The four rungs of the ranked ladder. Order is load-bearing: a higher enum value is a higher
    /// league, so promotion is +1 and relegation is -1. Bronze never relegates and Diamond never
    /// promotes — those are the ends of the ladder.
    /// </summary>
    public enum League
    {
        Bronze = 0,
        Silver = 1,
        Gold = 2,
        Diamond = 3
    }

    /// <summary>
    /// League facts shared by every backend (mock and real), so the pod size, the promote/relegate
    /// counts, and the up/down navigation live in exactly one place. Colours are NOT here — that is a
    /// UI concern and stays in ArcadeTheme, so Net/ never depends on the UI layer.
    /// </summary>
    public static class Leagues
    {
        public const int Count = 4;

        /// <summary>Players per pod. The whole promote-3 / relegate-2 design assumes this.</summary>
        public const int PodSize = 15;

        /// <summary>Top N of a pod promote at the weekly rollover.</summary>
        public const int PromoteCount = 3;

        /// <summary>Bottom N of a pod relegate at the weekly rollover.</summary>
        public const int RelegateCount = 2;

        /// <summary>
        /// What a player sees. The enum keeps its old names (Bronze…Diamond) because they are saved and
        /// sent to the service; only this table says Academy, Challenger, Premier and Legend League.
        /// </summary>
        public static string Name(League league)
        {
            switch (league)
            {
                case League.Bronze: return "Academy League";
                case League.Silver: return "Challenger League";
                case League.Gold: return "Premier League";
                case League.Diamond: return "Legend League";
                default: return "Academy League";
            }
        }

        /// <summary>The name without the word League — for a chip with no room for it.</summary>
        public static string ShortName(League league) => Name(league).Replace(" League", string.Empty);

        public static bool CanPromote(League league) => (int)league < Count - 1;
        public static bool CanRelegate(League league) => (int)league > 0;

        public static League Up(League league) =>
            CanPromote(league) ? (League)((int)league + 1) : league;

        public static League Down(League league) =>
            CanRelegate(league) ? (League)((int)league - 1) : league;
    }
}
