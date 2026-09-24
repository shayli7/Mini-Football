using System;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The player's progression — XP, level, and the win/loss record earned by playing.
    ///
    /// Local and immediate, the same shape as <see cref="MatchStats"/>: a match ends and the player
    /// wants their level and bar to move the instant the banner shows, with nothing to wait on. So the
    /// totals live here in <see cref="PlayerPrefs"/> and are authoritative for this device.
    ///
    /// Deliberately kept OUT of the UI: screens only read these getters and the <see cref="MatchOutcome"/>
    /// returned by <see cref="RecordMatch"/>. All the "how much XP, what level" rules are in this one
    /// file, so the system can later grow achievements, cosmetics, or ranks without touching a screen.
    ///
    /// Scope note: this is the OVERALL record (vs-AI and Online). <see cref="MatchStats"/> stays the
    /// separate online-only feed for the leaderboard — an online match credits both. Local
    /// player-vs-player earns nothing here (two people share one device, so there is no single
    /// account owner to credit); <see cref="UI.GameFlow"/> is where that gate lives.
    /// </summary>
    public static class PlayerProgress
    {
        // ── Tuning ───────────────────────────────────────────────────────────────────────────────
        // Everything a designer might want to change lives here.

        /// <summary>XP awarded per completed match. A win is worth more than a loss, and a loss still
        /// pays out — you always get something for finishing. Win larger than loss.</summary>
        public const int XpForWin = 120;
        public const int XpForLoss = 40;

        /// <summary>
        /// Cumulative XP needed to REACH each level: index 0 → level 1 (0 XP), index 1 → level 2, and
        /// so on. Edit these numbers to reshape the early curve. Levels past the last entry keep
        /// climbing at the final step (here 2600−1800 = 800 XP each) up to <see cref="MaxLevel"/> —
        /// add more entries if you want to shape the higher levels by hand.
        /// </summary>
        private static readonly int[] LevelXp = { 0, 500, 1100, 1800, 2600 };

        /// <summary>
        /// The top of the ladder. The level path pays a reward for every level up to this one and
        /// finishes on a Legendary chest at 60, so the cap is not a cosmetic limit — it is the end of
        /// the path, and the two have to agree. <see cref="LevelPath"/> reads it from here rather than
        /// holding a second copy.
        ///
        /// XP keeps accruing past the cap (the total is still the honest number of matches played),
        /// but the level stops and the bar sits full. A bar that kept sweeping toward a level that
        /// never arrives would promise something the game does not have.
        /// </summary>
        public const int MaxLevel = 60;

        // ── Persistence ──────────────────────────────────────────────────────────────────────────
        private const string XpKey = "TableFootball.Progress.Xp";
        private const string WinsKey = "TableFootball.Progress.Wins";
        private const string LossesKey = "TableFootball.Progress.Losses";
        private const string GoalsKey = "TableFootball.Progress.Goals";
        private const string TimeKey = "TableFootball.Progress.PlayTime"; // whole seconds

        // ── The record (persisted) ───────────────────────────────────────────────────────────────
        public static int Xp => PlayerPrefs.GetInt(XpKey, 0);
        public static int Wins => PlayerPrefs.GetInt(WinsKey, 0);
        public static int Losses => PlayerPrefs.GetInt(LossesKey, 0);
        public static int Played => Wins + Losses;

        /// <summary>Goals the local player's team has scored, across every counted match. Real, and
        /// credited by <see cref="UI.GameFlow"/> off <c>MatchManager.GoalScored</c> — only for matches
        /// with a single owner (vs-AI and online), the same gate the win/loss record uses.</summary>
        public static int Goals => PlayerPrefs.GetInt(GoalsKey, 0);

        /// <summary>Total seconds spent in a live match on this device. Accumulated by
        /// <see cref="UI.GameFlow"/> while a match is playing and not paused.</summary>
        public static int PlayTimeSeconds => PlayerPrefs.GetInt(TimeKey, 0);

        /// <summary>0..100, or -1 with nothing played yet — a 0% win rate and "you have not played"
        /// are different facts and the caller should not have to guess which one it is looking at.</summary>
        public static int WinPercent => Played > 0 ? Mathf.RoundToInt(100f * Wins / Played) : -1;

        // ── Level, derived from XP ───────────────────────────────────────────────────────────────
        /// <summary>The player's current level (highest whose threshold the total XP has reached),
        /// capped at <see cref="MaxLevel"/>.</summary>
        public static int Level => LevelForXp(Xp);

        /// <summary>True once the ladder is finished and there is no next level to climb to.</summary>
        public static bool AtMaxLevel => Level >= MaxLevel;

        /// <summary>Cumulative XP total that reaches the NEXT level — the label's right-hand number,
        /// e.g. "740 / 1100 XP". At the cap it is the cap's own threshold, so the label reads as a
        /// finished total rather than as a target that can never be met.</summary>
        public static int XpToNext =>
            AtMaxLevel ? CumulativeXpForLevel(MaxLevel) : CumulativeXpForLevel(Level + 1);

        /// <summary>0..1 progress across the current level's band, for the XP bar's fill. Full at the
        /// cap — see <see cref="FractionForXp"/>, which is where that is decided so the before/after
        /// fractions in a <see cref="MatchOutcome"/> cap with it.</summary>
        public static float XpFraction => FractionForXp(Xp);

        /// <summary>
        /// The XP bar's caption, in one place because three screens draw it — the profile chip, the
        /// post-match banner and the level path.
        ///
        /// At the cap it reads MAX LEVEL rather than a fraction. XP keeps accruing past level 60, so
        /// the honest arithmetic there is "48,200 / 46,600", which looks like a bug and invites the
        /// player to wait for a level-up that is never coming.
        /// </summary>
        public static string XpLabel => AtMaxLevel ? "MAX LEVEL" : $"{Xp} / {XpToNext} XP";

        // ── Change notification (shape matches MatchStats.OnChanged) ─────────────────────────────
        /// <summary>Raised whenever the record moves, for the UI to redraw.</summary>
        public static event Action OnChanged
        {
            add { _onChanged += value; }
            remove { _onChanged -= value; }
        }

        private static Action _onChanged;

        /// <summary>For external systems to poke the UI after mutating progression out-of-band.</summary>
        public static void RaiseChanged() => _onChanged?.Invoke();

        // ── Crediting a match ────────────────────────────────────────────────────────────────────
        /// <summary>
        /// The delta from one match, handed to the post-match screen so it can animate the gain: how
        /// much XP, whether it was a win, and the before/after level and bar fill (a level-up is
        /// simply <see cref="NewLevel"/> &gt; <see cref="PrevLevel"/>).
        /// </summary>
        public readonly struct MatchOutcome
        {
            public readonly int XpEarned;
            public readonly bool Won;
            public readonly int PrevLevel;
            public readonly int NewLevel;
            public readonly int NewXp;
            public readonly float PrevFraction;
            public readonly float NewFraction;

            public bool LeveledUp => NewLevel > PrevLevel;

            public MatchOutcome(int xpEarned, bool won, int prevLevel, int newLevel, int newXp,
                                float prevFraction, float newFraction)
            {
                XpEarned = xpEarned;
                Won = won;
                PrevLevel = prevLevel;
                NewLevel = newLevel;
                NewXp = newXp;
                PrevFraction = prevFraction;
                NewFraction = newFraction;
            }
        }

        /// <summary>
        /// Records one completed match: adds XP (win or loss), bumps the win/loss count, saves, and
        /// raises <see cref="OnChanged"/>. Returns the before/after delta for the post-match screen.
        /// Called once per counted match from <see cref="UI.GameFlow"/> (vs-AI and Online only).
        /// </summary>
        public static MatchOutcome RecordMatch(bool won)
        {
            int prevXp = Xp;
            int prevLevel = LevelForXp(prevXp);
            float prevFraction = FractionForXp(prevXp);

            int earned = won ? XpForWin : XpForLoss;
            int newXp = prevXp + earned;

            PlayerPrefs.SetInt(XpKey, newXp);
            PlayerPrefs.SetInt(won ? WinsKey : LossesKey, (won ? Wins : Losses) + 1);
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
            _onChanged?.Invoke();

            return new MatchOutcome(earned, won, prevLevel, LevelForXp(newXp), newXp,
                                    prevFraction, FractionForXp(newXp));
        }

        /// <summary>
        /// Adds XP that did not come from a match — today, daily quests (through
        /// <see cref="Progression.PlayerXp.Award"/>). Returns the number of levels gained, so the
        /// result screen knows whether it has a level-up to show.
        ///
        /// Quests and matches pay into this ONE total. They used to keep two: quests had their own
        /// store and curve, so the quests and account screens showed one level and the header chip
        /// another, and quest XP never reached the level path.
        /// </summary>
        public static int AddXp(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int before = Level;
            PlayerPrefs.SetInt(XpKey, Xp + amount);
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
            _onChanged?.Invoke();
            return Mathf.Max(0, Level - before);
        }

        /// <summary>The level an XP total buys — for a total that is not the current one, such as
        /// the result screen's "before the quests paid out".</summary>
        public static int LevelOf(int xp) => LevelForXp(Mathf.Max(0, xp));

        /// <summary>0..1 through its level for an XP total that is not the current one.</summary>
        public static float FractionOf(int xp) => FractionForXp(Mathf.Max(0, xp));

        /// <summary>Cumulative XP that reaches <paramref name="level"/>.</summary>
        public static int XpForLevel(int level) => CumulativeXpForLevel(level);

        /// <summary>
        /// Credits one goal to the local player. Called by <see cref="UI.GameFlow"/> when the team it
        /// is crediting scores; local player-vs-player credits nothing, since there is no single owner
        /// to credit — the same gate <see cref="RecordMatch"/> lives behind.
        /// </summary>
        public static void RecordGoal()
        {
            PlayerPrefs.SetInt(GoalsKey, Goals + 1);
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
            _onChanged?.Invoke();
        }

        /// <summary>
        /// Adds whole seconds of play time. Handed the accumulated time in chunks by
        /// <see cref="UI.GameFlow"/> rather than one second at a time, so this is not a per-frame write.
        /// </summary>
        public static void AddPlayTime(int seconds)
        {
            if (seconds <= 0)
            {
                return;
            }

            PlayerPrefs.SetInt(TimeKey, PlayTimeSeconds + seconds);
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
            _onChanged?.Invoke();
        }

        /// <summary>
        /// Zeroes the record. Called when the player this device belongs to changes — deleting the
        /// account, or signing in as somebody else — because progression is a record of what a PERSON
        /// did; carrying it onto the next person would misreport both. Mirrors
        /// <see cref="MatchStats.ResetLocal"/>.
        ///
        /// It clears <see cref="LevelPath"/> too, and that is an invariant rather than a convenience:
        /// the path's claim state is an index into THESE levels. Wiping the XP while leaving the
        /// claims behind would hand the next player a path already marked collected, and every level
        /// they earned back would pay nothing — a dead reward track with no error anywhere.
        /// </summary>
        public static void ResetLocal()
        {
            PlayerPrefs.DeleteKey(XpKey);
            PlayerPrefs.DeleteKey(WinsKey);
            PlayerPrefs.DeleteKey(LossesKey);
            PlayerPrefs.DeleteKey(GoalsKey);
            PlayerPrefs.DeleteKey(TimeKey);
            PlayerPrefs.Save();
            LevelPath.ResetLocal();
            _onChanged?.Invoke();
        }

        // ── Cloud sync ─────────────────────────────────────────────────────────────────────────
        // The XP record only. LevelPath is a separate store and syncs its own claim mask.

        /// <summary>Copies xp, wins, losses, goals and play time into the save document.</summary>
        internal static void ExportTo(CloudSync.SaveDoc d)
        {
            d.xp = PlayerPrefs.GetInt(XpKey, 0);
            d.wins = PlayerPrefs.GetInt(WinsKey, 0);
            d.losses = PlayerPrefs.GetInt(LossesKey, 0);
            d.goals = PlayerPrefs.GetInt(GoalsKey, 0);
            d.playTime = PlayerPrefs.GetInt(TimeKey, 0);
        }

        /// <summary>Writes the reconciled record back and redraws. Straight to PlayerPrefs so it does
        /// not re-mark the store dirty.</summary>
        internal static void ImportFrom(CloudSync.SaveDoc d)
        {
            PlayerPrefs.SetInt(XpKey, d.xp);
            PlayerPrefs.SetInt(WinsKey, d.wins);
            PlayerPrefs.SetInt(LossesKey, d.losses);
            PlayerPrefs.SetInt(GoalsKey, d.goals);
            PlayerPrefs.SetInt(TimeKey, d.playTime);
            PlayerPrefs.Save();
            _onChanged?.Invoke();
        }

        // ── Level maths ──────────────────────────────────────────────────────────────────────────
        /// <summary>Cumulative XP that reaches <paramref name="level"/> (level 1 = 0). Past the table,
        /// keeps climbing at the final step so it never caps.</summary>
        private static int CumulativeXpForLevel(int level)
        {
            if (level <= 1) return 0;

            int top = LevelXp.Length; // highest level the table defines a threshold for
            if (level <= top) return LevelXp[level - 1];

            int tailStep = LevelXp[top - 1] - LevelXp[top - 2];
            return LevelXp[top - 1] + (level - top) * tailStep;
        }

        private static int LevelForXp(int xp)
        {
            int level = 1;
            while (level < MaxLevel && CumulativeXpForLevel(level + 1) <= xp) level++;
            return level;
        }

        private static float FractionForXp(int xp)
        {
            int level = LevelForXp(xp);

            // Full at the cap, rather than the honest fraction across a band the player can never
            // finish. Capped HERE and not at the XpFraction property, so a MatchOutcome's before/after
            // fractions cap with it — the post-match banner animates from raw fractions, and a bar
            // that crept forward there while the chip's sat full would be two views of one number
            // disagreeing on screen.
            if (level >= MaxLevel) return 1f;

            int floor = CumulativeXpForLevel(level);
            int ceil = CumulativeXpForLevel(level + 1);
            if (ceil <= floor) return 0f;
            return Mathf.Clamp01((float)(xp - floor) / (ceil - floor));
        }

        // ── Not yet real ─────────────────────────────────────────────────────────────────────────
        // TODO(real-data): sporting stats the game does not track yet. Mock placeholders. Goals and
        // total play time are now real (above); win streak still needs the match loop to record it.
        public static int WinStreak => 0;
        public static int BestStreak => 0;

        /// <summary>
        /// A stable, invented level for another player, from their id. Purely cosmetic — friends have
        /// no level this device can read — but derived from the id so a given friend always shows the
        /// same number rather than flickering a new one each redraw.
        /// </summary>
        public static int MockLevelFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return 1;

            int h = 17;
            foreach (char c in id) h = unchecked(h * 31 + c);
            return 1 + (h & 0x7fffffff) % 40; // 1..40
        }
    }
}
