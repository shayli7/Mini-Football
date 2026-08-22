using System;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// This device's count of online wins and losses.
    ///
    /// Local and immediate, unlike everything else "profile"-shaped in this game: friends and the
    /// account live on Unity's servers and answer slowly, but a player finishing a match wants their
    /// own record to update the instant the banner shows, with nothing to wait on. So the count that
    /// lives here in <see cref="PlayerPrefs"/> is the one <see cref="UI.ProfileMenu"/> reads for the
    /// player's own screen, and it is authoritative for that — it is what actually happened on this
    /// device, whatever a server round trip might say.
    ///
    /// It does not, on its own, answer "what is my friend's record" — that needs a place both players
    /// can read regardless of whose device it happened on, which is <see cref="LeaderboardHub"/>. This
    /// class also pushes there after every result, so the two stay in step: this device's number is
    /// immediate, the leaderboard's is what everyone else eventually sees.
    /// </summary>
    public static class MatchStats
    {
        private const string WinsKey = "TableFootball.OnlineWins";
        private const string LossesKey = "TableFootball.OnlineLosses";

        public static int OnlineWins => PlayerPrefs.GetInt(WinsKey, 0);

        public static int OnlineLosses => PlayerPrefs.GetInt(LossesKey, 0);

        public static int OnlinePlayed => OnlineWins + OnlineLosses;

        /// <summary>0..100, or -1 with nothing played yet — a 0% win rate and "you have not played" are
        /// different facts and the caller should not have to guess which one it is looking at.</summary>
        public static int WinPercent => OnlinePlayed > 0 ? Mathf.RoundToInt(100f * OnlineWins / OnlinePlayed) : -1;

        /// <summary>Raised whenever the count changes, for the UI to redraw.</summary>
        public static event Action OnChanged;

        /// <summary>
        /// Records one online match's result. Called exactly once per match, from the same
        /// <c>MatchWon</c> event whether the win came from a goal or a forfeit — both are equally real
        /// results, and splitting them would only make "record a normal win" and "record a forfeit
        /// win" two behaviours to keep in sync instead of one.
        /// </summary>
        public static void RecordResult(bool won)
        {
            if (won)
            {
                PlayerPrefs.SetInt(WinsKey, OnlineWins + 1);
            }
            else
            {
                PlayerPrefs.SetInt(LossesKey, OnlineLosses + 1);
            }

            PlayerPrefs.Save();
            OnChanged?.Invoke();

            // Fire-and-forget, matching every other courtesy broadcast in this game (see
            // OnlineSession.PublishPresence): the match the player is looking at just ended, and
            // whether the leaderboard accepted the update is no reason to hold up the win banner.
            _ = LeaderboardHub.SubmitAsync(OnlineWins, OnlineLosses);
        }

        /// <summary>
        /// Zeroes the count. Called when the player this device belongs to changes — deleting the
        /// account, or signing in as somebody else — because a wins/losses count is a record of what
        /// a PERSON did, and carrying the previous person's numbers onto the next one would misreport
        /// both of them.
        ///
        /// Once <see cref="LeaderboardHub"/> can actually be read from, signing in should instead
        /// re-fetch that account's real total here; today there is nothing to fetch it from, so the
        /// honest thing this device can do is admit it does not know and start counting again.
        /// </summary>
        public static void ResetLocal()
        {
            PlayerPrefs.DeleteKey(WinsKey);
            PlayerPrefs.DeleteKey(LossesKey);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }
}
