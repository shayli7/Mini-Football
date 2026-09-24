using System;
using System.Collections.Generic;

namespace TableFootball
{
    /// <summary>
    /// The one thing the UI and GameFlow talk to for ranked. It owns the active backend (the mock
    /// today; the UGS + Cloud Code service later) and a cached snapshot of the current standing and
    /// pod, so screens can read synchronously and just redraw on <see cref="OnChanged"/> — the same
    /// read-and-redraw pattern PlayerProgress uses.
    ///
    /// Everything here is safe: the backend never throws, and the calls below swallow anything that
    /// slips through, so ranked can never take down a match or a menu.
    /// </summary>
    public static class Ladder
    {
        private static ILadderService service;

        private static ILadderService Service => service ??= CreateDefault();

        /// <summary>
        /// The real server backend when it is compiled in and the ladder module is deployed
        /// (LADDER_UGS on); otherwise the offline mock, which is also what the compile-check harness
        /// and any editor play without the server use.
        /// </summary>
        private static ILadderService CreateDefault()
        {
#if LADDER_UGS
            var ugs = new UgsLadderService();
            if (ugs.IsAvailable) return ugs;
#endif
            return new MockLadderService();
        }

        /// <summary>Swap the backend (e.g. mock → UGS once it is available). Refreshes the cache.</summary>
        public static void UseService(ILadderService replacement)
        {
            if (replacement == null) return;
            service = replacement;
            Refresh();
        }

        /// <summary>Raised after the cached standing/pod change, for screens to redraw.</summary>
        public static event Action OnChanged;

        /// <summary>Last known standing. <c>Valid == false</c> until the first refresh completes.</summary>
        public static LadderStanding Standing { get; private set; }

        /// <summary>Last known pod rows, best first. Empty until the first refresh.</summary>
        public static IReadOnlyList<LadderEntry> Pod { get; private set; } = new List<LadderEntry>();

        public static bool IsAvailable => Service.IsAvailable;
        public static int CurrentWeek => Service.CurrentWeek;

        /// <summary>Re-reads the standing and pod from the backend and notifies listeners.</summary>
        public static async void Refresh()
        {
            try
            {
                Standing = await Service.GetMyStandingAsync();
                Pod = await Service.GetMyPodAsync();
            }
            catch
            {
                // Leave the last good cache in place.
            }

            OnChanged?.Invoke();
        }

        /// <summary>Records a ranked result, then refreshes the cache.</summary>
        public static async void SubmitResult(int opponentRating, bool won)
        {
            try
            {
                await Service.SubmitResultAsync(opponentRating, won);
            }
            catch
            {
                // Ignored — a lost submission must not break the end-of-match flow.
            }

            Refresh();
        }

        /// <summary>Testing only: advance the ladder week (mock backend) to see the rollover now.</summary>
        public static void AdvanceWeekForTesting()
        {
            (Service as MockLadderService)?.AdvanceWeekForTesting();
            Refresh();
        }
    }
}
