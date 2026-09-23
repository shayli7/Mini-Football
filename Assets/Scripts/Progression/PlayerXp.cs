using System;
using UnityEngine;

namespace TableFootball.Progression
{
    /// <summary>
    /// The player's experience total, and the level it buys.
    ///
    /// Local and immediate, for the same reason <see cref="Net.MatchStats"/> is: a player finishing
    /// a match wants the bar to move while they are still looking at the result, with no round trip
    /// to wait on. Nothing here talks to a server.
    ///
    /// Quests are the <b>only</b> source. Matches deliberately pay nothing on their own — if simply
    /// playing awarded XP, the quests would stop being the reason to open the game.
    /// </summary>
    public static class PlayerXp
    {
        private const string TotalKey = "tf_xp_total";

        /// <summary>
        /// Level <c>n</c> to <c>n+1</c> costs <c>Base + Step × (n − 1)</c>.
        ///
        /// Linear growth rather than geometric: the cost keeps rising, so late levels stay an
        /// achievement, but it never walls the way a doubling curve does. A full three-quest day
        /// with the slam bonus is 500 XP — a level and a quarter at the start, about a third of one
        /// by level 12.
        /// </summary>
        private const int Base = 400;
        private const int Step = 100;

        /// <summary>Guards the level walk below against an absurd saved total (a corrupt pref).</summary>
        private const int MaxLevel = 999;

        /// <summary>Raised whenever the total changes, for the UI to redraw.</summary>
        public static event Action OnChanged;

        public static int Total
        {
            get => PlayerPrefs.GetInt(TotalKey, 0);
            private set
            {
                PlayerPrefs.SetInt(TotalKey, Mathf.Max(0, value));
                PlayerPrefs.Save();
                OnChanged?.Invoke();
            }
        }

        /// <summary>What it costs to leave <paramref name="level"/>. Level 1 is where everyone starts.</summary>
        public static int CostOfLevel(int level) => Base + Step * (Mathf.Max(1, level) - 1);

        /// <summary>The level the current total buys. 1 with nothing earned.</summary>
        public static int Level => LevelAt(Total, out _);

        /// <summary>How far into the current level the player is, in XP.</summary>
        public static int IntoLevel
        {
            get
            {
                LevelAt(Total, out int into);
                return into;
            }
        }

        /// <summary>What the current level costs in total — the denominator of the bar.</summary>
        public static int LevelSpan => CostOfLevel(Level);

        /// <summary>0..1 through the current level, for the ring and the bar.</summary>
        public static float Progress01
        {
            get
            {
                int span = LevelSpan;
                return span > 0 ? Mathf.Clamp01(IntoLevel / (float)span) : 0f;
            }
        }

        /// <summary>
        /// The level, and how far through it, for a total that is not the current one.
        ///
        /// The result screen needs the state the player was in <em>before</em> the match paid out,
        /// so the bar can run from there to here. Recomputing it from <c>Total - awarded</c> beats
        /// stashing a copy, which would be one more thing to keep in step.
        /// </summary>
        public static int LevelOf(int total) => LevelAt(total, out _);

        public static float Progress01Of(int total)
        {
            int level = LevelAt(total, out int into);
            int span = CostOfLevel(level);
            return span > 0 ? Mathf.Clamp01(into / (float)span) : 0f;
        }

        /// <summary>
        /// Banks XP. Returns the number of levels gained, so the result screen knows whether it has
        /// a level-up to celebrate — a return of 0 means the bar simply moved.
        /// </summary>
        public static int Award(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int before = Level;
            Total = Total + amount;
            return Mathf.Max(0, Level - before);
        }

        /// <summary>
        /// Wipes the total back to nothing.
        ///
        /// Called on the two paths where this device stops belonging to the same person — signing in
        /// as somebody else, and deleting the account — alongside <see cref="Net.MatchStats.ResetLocal"/>
        /// and <see cref="DailyQuests.ResetForNewPlayer"/>. A level is a record of what a PERSON did,
        /// and handing the next one an inherited level would misreport both of them.
        /// </summary>
        public static void ResetForNewPlayer()
        {
            PlayerPrefs.DeleteKey(TotalKey);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }

        /// <summary>
        /// Walks the curve rather than solving it. The quadratic inverse is exact but reads as a
        /// magic formula, and at these level counts the loop costs nothing.
        /// </summary>
        private static int LevelAt(int total, out int into)
        {
            int level = 1;
            int left = Mathf.Max(0, total);

            while (level < MaxLevel)
            {
                int cost = CostOfLevel(level);
                if (left < cost)
                {
                    break;
                }

                left -= cost;
                level++;
            }

            into = left;
            return level;
        }
    }
}
