using System.Threading.Tasks;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Where a FRIEND's win/loss record comes from — the one piece of this feature that needs a real
    /// server, because it has to be readable by someone on a different device who was not there when
    /// the match happened.
    ///
    /// This is a stub. <c>com.unity.services.leaderboards</c> is not installed in this project, so
    /// nothing here has been written against a verified API — every other service wrapper in this
    /// game (<see cref="GameServices"/>, <see cref="FriendsHub"/>, <see cref="PlayerAccount"/>) was
    /// built by reading the actual installed package under Library/PackageCache first, and this one
    /// deliberately was not, to avoid shipping calls against a guessed method signature.
    ///
    /// What IS real: the shape below. <see cref="MatchStats"/> and every screen that shows a record
    /// already call through this class rather than a concrete SDK type, so wiring up the genuine
    /// service later is a change confined to the bodies of these two methods — nothing that calls
    /// them needs to change.
    ///
    /// To finish this: install com.unity.services.leaderboards from Package Manager, enable
    /// Leaderboards for the project on the Unity Dashboard, and create one leaderboard there (a
    /// sensible id: <c>online_wins</c>, sort descending, no tiers needed for a two-number record).
    /// </summary>
    public static class LeaderboardHub
    {
        public readonly struct PlayerRecord
        {
            public readonly int Wins;
            public readonly int Losses;

            public PlayerRecord(int wins, int losses)
            {
                Wins = wins;
                Losses = losses;
            }
        }

        /// <summary>False until the real service is wired in. Screens use this to show "not available
        /// yet" honestly instead of a record that looks real but is not being kept.</summary>
        public static bool IsAvailable => false;

        private static bool warnedOnce;

        /// <summary>Pushes this device's current total. No-ops until <see cref="IsAvailable"/>.</summary>
        public static Task SubmitAsync(int wins, int losses)
        {
            WarnOnce();
            return Task.CompletedTask;
        }

        /// <summary>The record for another player, or null if it cannot be read — which today is
        /// always, and later will also mean "that player has never recorded a result."</summary>
        public static Task<PlayerRecord?> TryGetRecordAsync(string playerId)
        {
            WarnOnce();
            return Task.FromResult<PlayerRecord?>(null);
        }

        private static void WarnOnce()
        {
            if (warnedOnce)
            {
                return;
            }

            warnedOnce = true;
            Debug.LogWarning("LeaderboardHub: com.unity.services.leaderboards is not wired in yet — " +
                             "friends' win/loss records will show as unavailable. See this class's " +
                             "summary for what installing it needs.");
        }
    }
}
