using System;
using TableFootball.Net;
using UnityEngine;

namespace TableFootball.Progression
{
    /// <summary>
    /// The quest system's view of the player's experience — a thin window onto
    /// <see cref="PlayerProgress"/>, which owns the one XP total and the one level.
    ///
    /// This used to be a second, separate store with its own curve (400 XP for level 2, +100 each
    /// level after), fed only by quests, while matches paid into <see cref="PlayerProgress"/>. The two
    /// arrived from different branches and both survived the merge, so the quests and account screens
    /// read one level and the header chip another ("Level 1" beside "LV 3"), and quest XP never
    /// reached the level path. Now quests pay into the same total as matches, and every screen reads
    /// the same level.
    ///
    /// Kept as its own class, with the same members, so the quest code and its screens did not have to
    /// change; everything here forwards.
    /// </summary>
    public static class PlayerXp
    {
        /// <summary>Where the old separate quest total was kept. Read once, folded into
        /// <see cref="PlayerProgress"/>, and deleted — see <see cref="MigrateLegacyTotal"/>.</summary>
        private const string LegacyTotalKey = "tf_xp_total";

        private static bool migrated;

        /// <summary>Raised whenever the XP total changes. The same event as
        /// <see cref="PlayerProgress.OnChanged"/>, so a match and a quest both redraw quest screens.</summary>
        public static event Action OnChanged
        {
            add => PlayerProgress.OnChanged += value;
            remove => PlayerProgress.OnChanged -= value;
        }

        public static int Total
        {
            get
            {
                MigrateLegacyTotal();
                return PlayerProgress.Xp;
            }
        }

        /// <summary>What it costs to go from <paramref name="level"/> to the next one.</summary>
        public static int CostOfLevel(int level) =>
            PlayerProgress.XpForLevel(level + 1) - PlayerProgress.XpForLevel(level);

        /// <summary>The player's level. 1 with nothing earned.</summary>
        public static int Level => PlayerProgress.LevelOf(Total);

        /// <summary>How far into the current level the player is, in XP.</summary>
        public static int IntoLevel => Total - PlayerProgress.XpForLevel(Level);

        /// <summary>What the current level costs in total — the denominator of the bar.</summary>
        public static int LevelSpan => CostOfLevel(Level);

        /// <summary>0..1 through the current level, for the ring and the bar. Full at the cap.</summary>
        public static float Progress01 => PlayerProgress.FractionOf(Total);

        /// <summary>True once the player is at <see cref="PlayerProgress.MaxLevel"/>.</summary>
        public static bool AtMaxLevel => PlayerProgress.AtMaxLevel;

        /// <summary>
        /// The level for a total that is not the current one — the result screen needs the state the
        /// player was in before the quests paid out, so the bar can run from there to here.
        /// </summary>
        public static int LevelOf(int total) => PlayerProgress.LevelOf(total);

        public static float Progress01Of(int total) => PlayerProgress.FractionOf(total);

        /// <summary>Banks quest XP into the shared total. Returns the number of levels gained.</summary>
        public static int Award(int amount)
        {
            MigrateLegacyTotal();
            return PlayerProgress.AddXp(amount);
        }

        /// <summary>
        /// Called on the two paths where this device stops belonging to the same person. The total
        /// itself is wiped by <see cref="PlayerProgress.ResetLocal"/> on those same paths; this only
        /// makes sure an unmigrated old quest total cannot be folded into the next player's XP.
        /// </summary>
        public static void ResetForNewPlayer()
        {
            PlayerPrefs.DeleteKey(LegacyTotalKey);
            PlayerPrefs.Save();
            migrated = true;
        }

        /// <summary>
        /// Moves XP earned under the old separate quest store into the shared total, once. A player
        /// who cleared quests before the fix keeps every point of it — it now counts towards their
        /// real level and the level path, which is where it should have gone all along.
        /// </summary>
        private static void MigrateLegacyTotal()
        {
            if (migrated) return;
            migrated = true;

            int legacy = PlayerPrefs.GetInt(LegacyTotalKey, 0);
            PlayerPrefs.DeleteKey(LegacyTotalKey);
            PlayerPrefs.Save();

            if (legacy > 0) PlayerProgress.AddXp(legacy);
        }
    }
}
