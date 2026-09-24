using System;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The weekly ranked payout — where gold coins actually come from.
    ///
    /// When the ladder week rolls over, the pod locks and everyone is paid for where they finished:
    /// more for a higher league, and much more for finishing in the promotion places. The payout waits
    /// to be COLLECTED on the ranked screen rather than landing silently, for the same reason the level
    /// path does — a reward the player never saw arrive is not a reward.
    ///
    /// The rollover is detected rather than announced. <see cref="ILadderService"/> has no "the week
    /// ended" event and deliberately so: the mock rolls over inside its own Sync, and a real server
    /// would roll over while the app was closed. So this keeps a snapshot of the last week it saw —
    /// week index, league and rank — and pays out for THAT week the moment the standing comes back
    /// carrying a different week index. It works identically for a player who watched it happen and
    /// one who opened the game on Tuesday.
    ///
    /// Following the <c>Net/</c> contract, nothing here throws: a missing or invalid standing simply
    /// means there is nothing to pay yet.
    /// </summary>
    public static class RankedRewards
    {
        // Last week actually observed, and how it was going when we last looked.
        private const string SeenWeekKey = "TableFootball.Ranked.SeenWeek";
        private const string SeenLeagueKey = "TableFootball.Ranked.SeenLeague";
        private const string SeenRankKey = "TableFootball.Ranked.SeenRank";

        // A finished week waiting to be collected.
        private const string PendingCoinsKey = "TableFootball.Ranked.PendingCoins";
        private const string PendingLeagueKey = "TableFootball.Ranked.PendingLeague";
        private const string PendingRankKey = "TableFootball.Ranked.PendingRank";

        /// <summary>
        /// Base coins for finishing a week in each league, indexed by <see cref="League"/>. Climbing a
        /// league is the main way to earn more, which is what makes the ladder worth playing for
        /// somebody who only wants the cosmetics.
        /// </summary>
        private static readonly int[] LeagueBase = { 120, 220, 380, 600 };

        /// <summary>Raised when a payout becomes available or is collected.</summary>
        public static event Action OnChanged;

        /// <summary>How much is waiting to be collected. Zero when there is nothing.</summary>
        public static int PendingCoins => PlayerPrefs.GetInt(PendingCoinsKey, 0);

        public static bool HasPending => PendingCoins > 0;

        /// <summary>The league the pending payout was earned in, for the claim card to name it.</summary>
        public static League PendingLeague =>
            (League)Mathf.Clamp(PlayerPrefs.GetInt(PendingLeagueKey, 0), 0, Leagues.Count - 1);

        /// <summary>Where the player finished, for the claim card. 0 when unknown.</summary>
        public static int PendingRank => PlayerPrefs.GetInt(PendingRankKey, 0);

        /// <summary>
        /// What this week would pay if it ended right now. Shown on the ranked screen as an incentive
        /// rather than a promise — the pod is still moving, and so is this number.
        /// </summary>
        public static int ProjectedCoins()
        {
            LadderStanding s = Ladder.Standing;
            return s.Valid ? CoinsFor(s.League, s.RankInPod, s.PodSize) : 0;
        }

        /// <summary>
        /// Coins for finishing at <paramref name="rank"/> in <paramref name="league"/>.
        ///
        /// Three bands, matching the three outcomes the pod screen already draws, so the reward and
        /// the promotion/relegation lines a player is watching are the same three groups: promoting
        /// doubles the league's base, relegating halves it, and holding pays it straight.
        /// </summary>
        public static int CoinsFor(League league, int rank, int podSize)
        {
            int basis = LeagueBase[Mathf.Clamp((int)league, 0, LeagueBase.Length - 1)];
            if (podSize <= 0) podSize = Leagues.PodSize;

            if (rank >= 1 && rank <= Leagues.PromoteCount)
            {
                return basis * 2;
            }

            if (rank > podSize - Leagues.RelegateCount)
            {
                return Mathf.Max(1, basis / 2);
            }

            return basis;
        }

        /// <summary>
        /// Checks whether the ladder week has turned over since the last look and, if it has, banks the
        /// payout for the week that just ended.
        ///
        /// Safe to call as often as the standing changes: it only pays when the week index actually
        /// moves, and it records the new week in the same breath, so a second call for the same
        /// rollover finds nothing to do.
        ///
        /// Called from <c>GameFlow</c> on <see cref="Ladder.OnChanged"/>.
        /// </summary>
        public static void Sync()
        {
            LadderStanding standing = Ladder.Standing;
            if (!standing.Valid)
            {
                return;
            }

            int seenWeek = PlayerPrefs.GetInt(SeenWeekKey, int.MinValue);

            // First look on this device. Nothing has finished yet — record where we came in, so the
            // FIRST rollover pays for a week we actually watched rather than for a guessed one.
            if (seenWeek == int.MinValue)
            {
                Remember(standing);
                return;
            }

            if (seenWeek == standing.WeekIndex)
            {
                // Same week, but the standing moved. Keep the snapshot current so a rollover detected
                // later pays for how the week actually ENDED, not for how it looked days ago.
                Remember(standing);
                return;
            }

            var finishedLeague = (League)Mathf.Clamp(PlayerPrefs.GetInt(SeenLeagueKey, 0), 0,
                                                     Leagues.Count - 1);
            int finishedRank = PlayerPrefs.GetInt(SeenRankKey, Leagues.PodSize);
            int coins = CoinsFor(finishedLeague, finishedRank, Leagues.PodSize);

            // Added to whatever is already waiting rather than replacing it: a player away for three
            // weeks earned three payouts, and the last one arriving must not erase the first two.
            PlayerPrefs.SetInt(PendingCoinsKey, PendingCoins + coins);
            PlayerPrefs.SetInt(PendingLeagueKey, (int)finishedLeague);
            PlayerPrefs.SetInt(PendingRankKey, finishedRank);

            Remember(standing);
            OnChanged?.Invoke();
        }

        /// <summary>
        /// Collects the waiting payout into the wallet and reports how much it was, so the screen can
        /// announce a real number. Returns 0 when there was nothing.
        /// </summary>
        public static int Claim()
        {
            int coins = PendingCoins;
            if (coins <= 0)
            {
                return 0;
            }

            PlayerPrefs.DeleteKey(PendingCoinsKey);
            PlayerPrefs.DeleteKey(PendingLeagueKey);
            PlayerPrefs.DeleteKey(PendingRankKey);
            PlayerPrefs.Save();

            Wallet.Add(coins);
            OnChanged?.Invoke();
            return coins;
        }

        /// <summary>Wipes the payout state along with the rest of a player change.</summary>
        public static void ResetLocal()
        {
            PlayerPrefs.DeleteKey(SeenWeekKey);
            PlayerPrefs.DeleteKey(SeenLeagueKey);
            PlayerPrefs.DeleteKey(SeenRankKey);
            PlayerPrefs.DeleteKey(PendingCoinsKey);
            PlayerPrefs.DeleteKey(PendingLeagueKey);
            PlayerPrefs.DeleteKey(PendingRankKey);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }

        private static void Remember(LadderStanding standing)
        {
            PlayerPrefs.SetInt(SeenWeekKey, standing.WeekIndex);
            PlayerPrefs.SetInt(SeenLeagueKey, (int)standing.League);
            PlayerPrefs.SetInt(SeenRankKey, standing.RankInPod);
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
        }

        // ── Cloud sync ─────────────────────────────────────────────────────────────────────────
        // RankedRewards owns the week snapshot and the pending payout. CloudSync moves the snapshot
        // as a unit (the more recent week wins) and never lowers the pending total.

        /// <summary>Copies the week snapshot and any pending payout into the save document. The
        /// snapshot is only marked present when a week has actually been observed — a first look uses
        /// <see cref="int.MinValue"/> as its "never" sentinel, which must not read as a real week.</summary>
        internal static void ExportTo(CloudSync.SaveDoc d)
        {
            d.hasSeen = PlayerPrefs.HasKey(SeenWeekKey);
            d.seenWeek = PlayerPrefs.GetInt(SeenWeekKey, 0);
            d.seenLeague = PlayerPrefs.GetInt(SeenLeagueKey, 0);
            d.seenRank = PlayerPrefs.GetInt(SeenRankKey, 0);
            d.pendingCoins = PlayerPrefs.GetInt(PendingCoinsKey, 0);
            d.pendingLeague = PlayerPrefs.GetInt(PendingLeagueKey, 0);
            d.pendingRank = PlayerPrefs.GetInt(PendingRankKey, 0);
        }

        /// <summary>Writes the reconciled snapshot and pending payout back, then redraws. The snapshot
        /// keys are only written when the merged document actually has one, so an unobserved account
        /// stays unobserved rather than being handed a bogus week 0.</summary>
        internal static void ImportFrom(CloudSync.SaveDoc d)
        {
            if (d.hasSeen)
            {
                PlayerPrefs.SetInt(SeenWeekKey, d.seenWeek);
                PlayerPrefs.SetInt(SeenLeagueKey, d.seenLeague);
                PlayerPrefs.SetInt(SeenRankKey, d.seenRank);
            }

            if (d.pendingCoins > 0)
            {
                PlayerPrefs.SetInt(PendingCoinsKey, d.pendingCoins);
                PlayerPrefs.SetInt(PendingLeagueKey, d.pendingLeague);
                PlayerPrefs.SetInt(PendingRankKey, d.pendingRank);
            }
            else
            {
                PlayerPrefs.DeleteKey(PendingCoinsKey);
                PlayerPrefs.DeleteKey(PendingLeagueKey);
                PlayerPrefs.DeleteKey(PendingRankKey);
            }

            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }
}
