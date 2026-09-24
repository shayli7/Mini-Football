using System;
using TableFootball.Net;

namespace TableFootball.Progression
{
    /// <summary>
    /// The quest side's view of the player's experience.
    ///
    /// It used to keep its own total and its own level curve, so the quest screen showed a different
    /// level from the profile chip and the menu. There is now ONE total, <see cref="PlayerProgress"/>'s:
    /// quests bank into it and this class only reads it back in the shapes the quest screens want.
    /// Nothing is stored here.
    ///
    /// Quests still pay XP on top of what a match does — <see cref="Award"/> is the quest payout.
    /// </summary>
    public static class PlayerXp
    {
        /// <summary>Raised whenever the shared total changes, for the UI to redraw.</summary>
        public static event Action OnChanged
        {
            add { PlayerProgress.OnChanged += value; }
            remove { PlayerProgress.OnChanged -= value; }
        }

        public static int Total => PlayerProgress.Xp;

        public static int Level => PlayerProgress.Level;

        /// <summary>How far into the current level the player is, in XP.</summary>
        public static int IntoLevel => PlayerProgress.XpIntoLevel;

        /// <summary>0..1 through the current level, for the ring and the bar. Full at the cap.</summary>
        public static float Progress01 => PlayerProgress.XpFraction;

        /// <summary>The level, and how far through it, for a total that is not the current one — the
        /// result screen runs its bar from the state before the payout.</summary>
        public static int LevelOf(int total) => PlayerProgress.LevelOfXp(total);

        public static float Progress01Of(int total) => PlayerProgress.FractionOfXp(total);

        /// <summary>Banks quest XP. Returns the levels gained, so the result screen knows whether it
        /// has a level-up to celebrate.</summary>
        public static int Award(int amount) => PlayerProgress.AddXp(amount);

        /// <summary>Nothing to wipe: the total lives in <see cref="PlayerProgress"/>, which
        /// <see cref="PlayerProgress.ResetLocal"/> already clears on the same paths.</summary>
        public static void ResetForNewPlayer() { }
    }
}
