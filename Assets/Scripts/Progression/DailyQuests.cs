using System;
using TableFootball.Net;
using UnityEngine;

namespace TableFootball.Progression
{
    /// <summary>
    /// Today's three quests, and how far through them the player is.
    ///
    /// Local and instant, shaped deliberately like <see cref="MatchStats"/>: a quest clears the
    /// moment the result banner appears, with nothing to wait on and nothing to fail. The whole
    /// thing lives in <see cref="PlayerPrefs"/>.
    ///
    /// Writes use <c>SetInt</c> freely and call <c>Save</c> only from <see cref="Flush"/>. SetInt is
    /// a dictionary write; Save is the one that touches the disk, and doing that per goal is a stall
    /// mid-rally on the phones this game is for.
    /// </summary>
    public static class DailyQuests
    {
        /// <summary>How many quests a day holds: one Bronze, one Silver, one Gold, in that order.</summary>
        public const int Slots = 3;

        /// <summary>
        /// The hour the day turns over, local time. Deliberately not midnight: a set swapped out
        /// from under somebody mid-session is the set they were three matches into.
        /// </summary>
        private const int RolloverHour = 3;

        private static readonly DateTime Epoch = new DateTime(2020, 1, 1);

        private const string DayKey = "tf_quest_day";
        private const string IdKey = "tf_quest_id";        // + slot
        private const string ProgressKey = "tf_quest_p";   // + slot
        private const string AwardedKey = "tf_quest_a";    // + slot
        private const string SlamKey = "tf_quest_slam";
        private const string SeenKey = "tf_quest_seen";
        private const string StreakKey = "tf_quest_streak";
        private const string LastDoneKey = "tf_quest_lastdone";
        private const string WinRunKey = "tf_quest_winrun";

        /// <summary>Raised whenever progress, the set or the streak changes.</summary>
        public static event Action OnChanged;

        // ---------- the day ----------

        /// <summary>
        /// Days since the epoch, measured against a day that begins at <see cref="RolloverHour"/>.
        /// The device's own clock, like everything else here.
        /// </summary>
        private static int Today =>
            (int)(DateTime.Now.AddHours(-RolloverHour).Date - Epoch).TotalDays;

        /// <summary>Seconds until the next set, for the countdown on the quests screen.</summary>
        public static double SecondsUntilReset
        {
            get
            {
                DateTime next = DateTime.Now.AddHours(-RolloverHour).Date
                                        .AddDays(1).AddHours(RolloverHour);
                return Math.Max(0d, (next - DateTime.Now).TotalSeconds);
            }
        }

        /// <summary>
        /// Draws a new set if the day has moved on. Call before reading anything.
        ///
        /// A day number that has gone <b>backwards</b> — the clock wound back, a timezone flight —
        /// is ignored rather than treated as a new day. Winding forward hands out a fresh set, which
        /// is the same trade <see cref="MatchStats"/> already makes by living on the device; winding
        /// back must never destroy progress that was honestly earned.
        /// </summary>
        public static void EnsureToday()
        {
            int today = Today;
            int saved = PlayerPrefs.GetInt(DayKey, int.MinValue);

            if (saved == today)
            {
                return;
            }

            // A clock that has gone backwards is not a new day. Keeping the stored set is what makes
            // "progress never rolls backwards" true.
            if (saved != int.MinValue && today < saved)
            {
                return;
            }

            Roll(today);
        }

        /// <summary>
        /// Picks one quest from each tier and clears the counters.
        ///
        /// The seed mixes the day with the player's id, so two people sitting next to each other get
        /// different sets and the same person gets the same set back after a reinstall. The drawn ids
        /// are then <b>stored</b>, which matters more than the seed does: sign-in is asynchronous and
        /// may land after the first draw of the day, and a set recomputed from a seed that changed
        /// underneath it would silently become a different set mid-session.
        /// </summary>
        private static void Roll(int day)
        {
            uint seed = Mix((uint)day, HashOf(GameServices.PlayerId));

            for (int slot = 0; slot < Slots; slot++)
            {
                var pool = QuestDefinition.AtTier((QuestTier)slot);
                int index = pool.Count > 0 ? (int)(Mix(seed, (uint)(slot + 1)) % (uint)pool.Count) : 0;

                PlayerPrefs.SetInt(IdKey + slot, pool.Count > 0 ? (int)pool[index].Id : 0);
                PlayerPrefs.SetInt(ProgressKey + slot, 0);
                PlayerPrefs.SetInt(AwardedKey + slot, 0);
            }

            PlayerPrefs.SetInt(SlamKey, 0);
            PlayerPrefs.SetInt(SeenKey, 0);
            PlayerPrefs.SetInt(WinRunKey, 0);
            PlayerPrefs.SetInt(DayKey, day);
            PlayerPrefs.Save();

            OnChanged?.Invoke();
        }

        // ---------- reading ----------

        public static QuestDefinition DefinitionAt(int slot) =>
            QuestDefinition.Get((QuestId)PlayerPrefs.GetInt(IdKey + Clamp(slot), 0));

        public static int ProgressAt(int slot) => PlayerPrefs.GetInt(ProgressKey + Clamp(slot), 0);

        public static bool IsCompleteAt(int slot) => PlayerPrefs.GetInt(AwardedKey + Clamp(slot), 0) != 0;

        public static float Progress01At(int slot)
        {
            QuestDefinition definition = DefinitionAt(slot);
            return definition.Target > 0
                ? Mathf.Clamp01(ProgressAt(slot) / (float)definition.Target)
                : 0f;
        }

        /// <summary>How many of the three are done. Three means the slam is owed, or already paid.</summary>
        public static int CompletedCount
        {
            get
            {
                int done = 0;
                for (int slot = 0; slot < Slots; slot++)
                {
                    if (IsCompleteAt(slot)) done++;
                }

                return done;
            }
        }

        public static bool SlamAwarded => PlayerPrefs.GetInt(SlamKey, 0) != 0;

        /// <summary>Consecutive days on which at least one quest was cleared. 0 before the first.</summary>
        public static int DayStreak => PlayerPrefs.GetInt(StreakKey, 0);

        /// <summary>
        /// How many quests have cleared since the player last opened the quests screen — the number
        /// on the main menu's count pip (see <c>UIFactory.CountPip</c>). <see cref="HasUnseen"/> is
        /// just this being greater than zero.
        /// </summary>
        public static int UnseenCount => Mathf.Max(0, CompletedCount - PlayerPrefs.GetInt(SeenKey, 0));

        /// <summary>
        /// True when something has been cleared that the player has not yet been shown on the quests
        /// screen — what puts the count pip on the main menu's quests button.
        /// </summary>
        public static bool HasUnseen => UnseenCount > 0;

        /// <summary>Called when the quests screen opens. Clears the dot.</summary>
        public static void MarkSeen()
        {
            PlayerPrefs.SetInt(SeenKey, CompletedCount);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }

        // ---------- writing ----------

        /// <summary>
        /// Reports progress towards one quest kind. Does nothing if today's set does not hold it.
        ///
        /// Returns true <b>only on the report that completes it</b>, which is what makes it safe to
        /// award XP from the return value: an already-finished quest reports false forever after.
        /// </summary>
        public static bool Report(QuestId id, int amount = 1)
        {
            if (amount <= 0)
            {
                return false;
            }

            EnsureToday();

            for (int slot = 0; slot < Slots; slot++)
            {
                if ((QuestId)PlayerPrefs.GetInt(IdKey + slot, 0) != id || IsCompleteAt(slot))
                {
                    continue;
                }

                QuestDefinition definition = DefinitionAt(slot);
                int progress = Mathf.Min(definition.Target, ProgressAt(slot) + amount);
                PlayerPrefs.SetInt(ProgressKey + slot, progress);

                if (progress < definition.Target)
                {
                    OnChanged?.Invoke();
                    return false;
                }

                PlayerPrefs.SetInt(AwardedKey + slot, 1);
                NoteCompletionDay();
                OnChanged?.Invoke();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Pays the all-three bonus, once. Returns false when the set is not finished, or when the
        /// bonus has already been paid.
        /// </summary>
        public static bool TryAwardSlam()
        {
            if (SlamAwarded || CompletedCount < Slots)
            {
                return false;
            }

            PlayerPrefs.SetInt(SlamKey, 1);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// The day-scoped run of online wins, for <see cref="QuestId.BackToBack"/>.
        ///
        /// Persisted rather than held by the tracker because a player may well close the game between
        /// two matches, and a run that only existed in memory would quietly restart every time they
        /// did. Cleared by the daily roll, so it is a run <em>today</em>, not an all-time streak.
        /// </summary>
        public static int WinRun => PlayerPrefs.GetInt(WinRunKey, 0);

        public static void RecordOnlineWin()
        {
            EnsureToday();
            PlayerPrefs.SetInt(WinRunKey, WinRun + 1);
        }

        /// <summary>
        /// Breaks the run. Called for a loss <b>and for walking out of a live match</b> — quitting
        /// has to cost the run, or leaving becomes the cheapest way to protect it.
        /// </summary>
        public static void BreakWinRun()
        {
            EnsureToday();
            PlayerPrefs.SetInt(WinRunKey, 0);
        }

        /// <summary>Pushes everything written since the last flush to disk. Called at match end.</summary>
        public static void Flush() => PlayerPrefs.Save();

        /// <summary>
        /// Wipes the set, the streak and the run.
        ///
        /// Called on the same two paths as <see cref="MatchStats.ResetLocal"/> and
        /// <see cref="PlayerXp.ResetForNewPlayer"/> — signing in as somebody else, and deleting the
        /// account — because a day's progress belongs to a person, not to a handset.
        /// </summary>
        public static void ResetForNewPlayer()
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                PlayerPrefs.DeleteKey(IdKey + slot);
                PlayerPrefs.DeleteKey(ProgressKey + slot);
                PlayerPrefs.DeleteKey(AwardedKey + slot);
            }

            PlayerPrefs.DeleteKey(DayKey);
            PlayerPrefs.DeleteKey(SlamKey);
            PlayerPrefs.DeleteKey(SeenKey);
            PlayerPrefs.DeleteKey(StreakKey);
            PlayerPrefs.DeleteKey(LastDoneKey);
            PlayerPrefs.DeleteKey(WinRunKey);
            PlayerPrefs.Save();

            // Straight back to a fresh set rather than an empty screen: the new player is standing in
            // the game right now, and a quests screen with nothing on it looks broken.
            EnsureToday();
            OnChanged?.Invoke();
        }

        // ---------- plumbing ----------

        /// <summary>
        /// Extends the day streak on the first completion of each day. A gap of more than one day
        /// starts again at 1 — the streak is consecutive days, and saying otherwise would be a lie
        /// the player can check.
        /// </summary>
        private static void NoteCompletionDay()
        {
            int today = Today;
            int last = PlayerPrefs.GetInt(LastDoneKey, int.MinValue);

            if (last == today)
            {
                return;
            }

            PlayerPrefs.SetInt(StreakKey, last == today - 1 ? DayStreak + 1 : 1);
            PlayerPrefs.SetInt(LastDoneKey, today);
        }

        private static int Clamp(int slot) => Mathf.Clamp(slot, 0, Slots - 1);

        /// <summary>Deterministic 32-bit mix. Same inputs, same draw, on every device and every run.</summary>
        private static uint Mix(uint a, uint b)
        {
            uint h = a * 2654435761u ^ b * 2246822519u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return h;
        }

        private static uint HashOf(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0u;
            }

            uint h = 2166136261u;
            foreach (char c in value)
            {
                h = (h ^ c) * 16777619u;
            }

            return h;
        }
    }
}
