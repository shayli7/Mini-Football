using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TableFootball
{
    /// <summary>
    /// The real ladder backend: the pod, the standings and the weekly promote/relegate all live on
    /// the server (Cloud Code), because fifteen-player pods and a scheduled rollover cannot be trusted
    /// to a client. This class only calls three Cloud Code endpoints and maps their replies onto the
    /// same <see cref="ILadderService"/> the mock satisfies, so swapping to it changes nothing above.
    ///
    /// It is gated behind the <c>LADDER_UGS</c> scripting define, because it needs
    /// <c>com.unity.services.cloudcode</c> installed and the <c>ladder</c> Cloud Code module deployed
    /// (see <c>cloudcode/ladder.js</c> and its README). With the define off — the default, and what
    /// the compile-check harness builds — only the safe stub below is compiled, and the game runs on
    /// <see cref="MockLadderService"/>.
    ///
    /// VERIFY ON DEPLOY: the exact CloudCode call signature and JSON field names below must be checked
    /// against the installed SDK version, exactly as the other Net/ wrappers were built by reading the
    /// package first. Everything is wrapped so a wrong call degrades to "unavailable", never a throw.
    /// </summary>
#if LADDER_UGS
    public class UgsLadderService : ILadderService
    {
        public event Action OnChanged;

        public bool IsAvailable => !string.IsNullOrEmpty(TableFootball.Net.GameServices.PlayerId);

        public int CurrentWeek { get; private set; }

        public async Task<LadderStanding> GetMyStandingAsync()
        {
            if (!IsAvailable) return default;

            try
            {
                var dto = await Unity.Services.CloudCode.CloudCodeService.Instance
                    .CallEndpointAsync<StandingDto>("ladder",
                        new Dictionary<string, object> { { "action", "getStanding" } });

                CurrentWeek = dto.week;
                return new LadderStanding
                {
                    Valid = true,
                    League = (League)dto.league,
                    PodId = dto.podId,
                    RankInPod = dto.rankInPod,
                    PodSize = dto.podSize,
                    WeeklyPoints = dto.points,
                    InPromotionZone = dto.promo,
                    InRelegationZone = dto.releg,
                    WeekIndex = dto.week,
                    SecondsToRollover = dto.secondsToRollover
                };
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Ladder standing unavailable: {e.Message}");
                return default;
            }
        }

        public async Task<IReadOnlyList<LadderEntry>> GetMyPodAsync()
        {
            if (!IsAvailable) return Array.Empty<LadderEntry>();

            try
            {
                var dto = await Unity.Services.CloudCode.CloudCodeService.Instance
                    .CallEndpointAsync<PodDto>("ladder",
                        new Dictionary<string, object> { { "action", "getPod" } });

                var rows = new List<LadderEntry>(dto.entries?.Length ?? 0);
                if (dto.entries != null)
                {
                    foreach (var e in dto.entries)
                    {
                        rows.Add(new LadderEntry { Name = e.name, Points = e.points, IsYou = e.you });
                    }
                }
                return rows;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Ladder pod unavailable: {e.Message}");
                return Array.Empty<LadderEntry>();
            }
        }

        public async Task SubmitResultAsync(int opponentRating, bool won)
        {
            if (!IsAvailable) return;

            try
            {
                // The server applies the league's asymmetric points itself — never trust a client to
                // report its own new rating. It just needs the outcome (and the opponent, later, for
                // validation / skill-scaled scoring).
                await Unity.Services.CloudCode.CloudCodeService.Instance
                    .CallEndpointAsync<object>("ladder",
                        new Dictionary<string, object>
                        {
                            { "action", "submitResult" },
                            { "won", won },
                            { "opponentRating", opponentRating }
                        });

                OnChanged?.Invoke();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Ladder submit failed: {e.Message}");
            }
        }

        // JSON shapes returned by the Cloud Code endpoints. Field names must match the module's output.
        [Serializable]
        private class StandingDto
        {
            public int league;
            public int podId;
            public int rankInPod;
            public int podSize;
            public int points;
            public bool promo;
            public bool releg;
            public int week;
            public long secondsToRollover;
        }

        [Serializable]
        private class EntryDto
        {
            public string name;
            public int points;
            public bool you;
        }

        [Serializable]
        private class PodDto
        {
            public EntryDto[] entries;
        }
    }
#else
    /// <summary>Inert stand-in when LADDER_UGS is off: reports unavailable so the facade falls back to
    /// the mock. Present unconditionally so <see cref="Ladder"/> can name the type either way.</summary>
    public class UgsLadderService : ILadderService
    {
        public event Action OnChanged { add { } remove { } }
        public bool IsAvailable => false;
        public int CurrentWeek => 0;
        public Task<LadderStanding> GetMyStandingAsync() => Task.FromResult(default(LadderStanding));
        public Task<IReadOnlyList<LadderEntry>> GetMyPodAsync() =>
            Task.FromResult<IReadOnlyList<LadderEntry>>(Array.Empty<LadderEntry>());
        public Task SubmitResultAsync(int opponentRating, bool won) => Task.CompletedTask;
    }
#endif
}
