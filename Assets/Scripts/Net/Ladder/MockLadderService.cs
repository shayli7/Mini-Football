using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// A complete, self-contained ladder that runs with no server, so the full pod design — a pod of
    /// fifteen, per-league points, and the weekly promote-3 / relegate-2 rollover — is playable and
    /// testable in the editor today. It stands in for the real UGS + Cloud Code backend behind the
    /// same ILadderService, so nothing above it changes when the real one is wired in.
    ///
    /// You share a pod with fourteen bots. Winning a ranked match adds your league's points; the bots
    /// drift each time you play, so the table is alive and holding a top-three (or escaping the
    /// bottom-two) takes real results. When the ladder week ticks over, the pod locks: the top three
    /// go up a league, the bottom two go down, and everyone is re-podded with points reset.
    ///
    /// State lives in PlayerPrefs as one JSON blob. Per the Net/ contract this never throws.
    /// </summary>
    public class MockLadderService : ILadderService
    {
        private const string Key = "TableFootball.Ladder.Mock";
        private static readonly DateTime Epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly string[] NamePool =
        {
            "Volley", "Rocket", "Pixel", "Turbo", "Nova", "Blitz", "Comet", "Zephyr", "Falcon",
            "Echo", "Sonic", "Ranger", "Vortex", "Titan", "Ace", "Bolt", "Maverick", "Gizmo",
            "Rune", "Dash", "Flint", "Onyx", "Storm", "Quill", "Jinx", "Karma", "Neon", "Riff"
        };

        [Serializable]
        private class State
        {
            public bool initialized;
            public int league;
            public int podId;
            public int week;        // the ladder week this pod belongs to
            public int weekOffset;  // debug: lets a tester advance the week without waiting
            public int myPoints;
            public string[] botNames;
            public int[] botPoints;
        }

        private State state;
        private System.Random rng = new System.Random();

        public event Action OnChanged;

        public bool IsAvailable => true;

        public int CurrentWeek => RealWeek() + Load().weekOffset;

        public Task<LadderStanding> GetMyStandingAsync()
        {
            Sync();
            return Task.FromResult(BuildStanding());
        }

        public Task<IReadOnlyList<LadderEntry>> GetMyPodAsync()
        {
            Sync();
            return Task.FromResult<IReadOnlyList<LadderEntry>>(BuildPod());
        }

        public Task SubmitResultAsync(int opponentRating, bool won, string matchId, string opponentId)
        {
            // No second player to corroborate with in a solo simulation — credit on the spot, as
            // this always has. matchId/opponentId exist for the real backend's anti-cheat check
            // (see ILadderService.SubmitResultAsync); the mock has nothing to check them against.
            Sync();

            var s = state;
            s.myPoints = Mathf.Max(0, s.myPoints + EloRating.Delta((League)s.league, won));

            // The rest of the pod keeps playing too, so standings shift under you rather than sitting
            // still. Bots drift up on average, a little faster in the lower leagues (busier ladders).
            int drift = EloRating.WinDelta((League)s.league);
            for (int i = 0; i < s.botPoints.Length; i++)
            {
                s.botPoints[i] = Mathf.Max(0, s.botPoints[i] + rng.Next(-drift / 2, drift));
            }

            Save();
            OnChanged?.Invoke();
            return Task.CompletedTask;
        }

        /// <summary>Testing only: jump to the next ladder week so the rollover can be seen at once.</summary>
        public void AdvanceWeekForTesting()
        {
            var s = Load();
            s.weekOffset += 1;
            state = s;
            Save();
            Sync();              // applies the rollover immediately
            OnChanged?.Invoke();
        }

        // --- internals ---------------------------------------------------------------------------

        private int RealWeek() => (int)((DateTime.UtcNow - Epoch).TotalDays / 7.0);

        private long SecondsToRollover()
        {
            DateTime next = Epoch.AddDays((RealWeek() + 1) * 7);
            double secs = (next - DateTime.UtcNow).TotalSeconds;
            return (long)Math.Max(0, secs);
        }

        private State Load()
        {
            if (state != null) return state;

            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json))
                {
                    state = JsonUtility.FromJson<State>(json);
                }
            }
            catch
            {
                state = null;
            }

            if (state == null) state = new State();
            return state;
        }

        private void Save()
        {
            try
            {
                PlayerPrefs.SetString(Key, JsonUtility.ToJson(state));
                PlayerPrefs.Save();
            }
            catch
            {
                // Persisting is best-effort; a locked prefs file must not crash a match result.
            }
        }

        /// <summary>Brings state up to the current week: first-run seeds a pod; a new week rolls over.</summary>
        private void Sync()
        {
            var s = Load();
            int week = RealWeek() + s.weekOffset;

            if (!s.initialized)
            {
                GeneratePod(League.Bronze, week);
                return;
            }

            if (week != s.week)
            {
                Rollover(week);
            }
        }

        /// <summary>Fills the pod for a league and week: fourteen fresh bots + your reset points.</summary>
        private void GeneratePod(League league, int week)
        {
            var s = state;
            s.initialized = true;
            s.league = (int)league;
            s.week = week;
            s.podId = Mathf.Abs(unchecked(week * 31 + (int)league * 7 + rng.Next(1000)));
            s.myPoints = 0;

            int n = Leagues.PodSize - 1; // you take the last seat
            s.botNames = new string[n];
            s.botPoints = new int[n];

            var used = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int pick;
                do { pick = rng.Next(NamePool.Length); } while (!used.Add(pick) && used.Count < NamePool.Length);
                // A little starting spread so the week doesn't begin dead level.
                s.botNames[i] = NamePool[pick] + rng.Next(10, 99);
                s.botPoints[i] = rng.Next(0, EloRating.WinDelta(league) * 2);
            }

            Save();
        }

        /// <summary>The weekly lock: rank the pod, move you up/down, then re-pod and reset.</summary>
        private void Rollover(int newWeek)
        {
            int rank = RankInPod();
            League league = (League)state.league;

            if (rank <= Leagues.PromoteCount && Leagues.CanPromote(league))
            {
                league = Leagues.Up(league);
            }
            else if (rank > Leagues.PodSize - Leagues.RelegateCount && Leagues.CanRelegate(league))
            {
                league = Leagues.Down(league);
            }

            GeneratePod(league, newWeek);
        }

        /// <summary>Your 1-based place in the pod, ties broken in your favour.</summary>
        private int RankInPod()
        {
            var s = Load();
            int above = 0;
            for (int i = 0; i < s.botPoints.Length; i++)
            {
                if (s.botPoints[i] > s.myPoints) above++;
            }
            return above + 1;
        }

        private LadderStanding BuildStanding()
        {
            var s = state;
            int rank = RankInPod();
            League league = (League)s.league;

            return new LadderStanding
            {
                Valid = true,
                League = league,
                PodId = s.podId,
                RankInPod = rank,
                PodSize = Leagues.PodSize,
                WeeklyPoints = s.myPoints,
                InPromotionZone = rank <= Leagues.PromoteCount && Leagues.CanPromote(league),
                InRelegationZone = rank > Leagues.PodSize - Leagues.RelegateCount && Leagues.CanRelegate(league),
                WeekIndex = s.week,
                SecondsToRollover = SecondsToRollover()
            };
        }

        private IReadOnlyList<LadderEntry> BuildPod()
        {
            var s = state;
            var rows = new List<LadderEntry>(Leagues.PodSize);

            for (int i = 0; i < s.botNames.Length; i++)
            {
                rows.Add(new LadderEntry { Name = s.botNames[i], Points = s.botPoints[i], IsYou = false });
            }
            rows.Add(new LadderEntry { Name = "You", Points = s.myPoints, IsYou = true });

            rows.Sort((a, b) => b.Points.CompareTo(a.Points));
            return rows;
        }
    }
}
