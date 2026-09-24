using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TableFootball
{
    /// <summary>
    /// The ladder backend, seen from the game. Two implementations satisfy it: MockLadderService (a
    /// full offline simulation, so the whole feature is playable and testable with no server) and,
    /// later, UgsLadderService (Cloud Code + Leaderboards). The UI and GameFlow only ever see this
    /// interface, through the Ladder facade, so swapping backends touches nothing else.
    ///
    /// Following the Net/ contract (GameServices, OnlineSession, LeaderboardHub): these calls NEVER
    /// throw. On any failure they return a safe empty/invalid result, so a phone with no signal plays
    /// exactly like one online.
    /// </summary>
    public interface ILadderService
    {
        /// <summary>Whether this backend can actually serve ranked data (mock: always true).</summary>
        bool IsAvailable { get; }

        /// <summary>The current ladder week (weeks since a fixed epoch).</summary>
        int CurrentWeek { get; }

        /// <summary>Raised whenever the standing changes (a result submitted, or a weekly rollover).</summary>
        event Action OnChanged;

        /// <summary>The local player's current standing.</summary>
        Task<LadderStanding> GetMyStandingAsync();

        /// <summary>The local player's pod — up to Leagues.PodSize rows, sorted best first.</summary>
        Task<IReadOnlyList<LadderEntry>> GetMyPodAsync();

        /// <summary>Records a ranked result and applies the league's points delta.</summary>
        Task SubmitResultAsync(int opponentRating, bool won);
    }
}
