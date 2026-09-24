using System;

namespace TableFootball.Net
{
    /// <summary>
    /// The ranked ladder's cosmetic reward: reaching a league unlocks that league's badge, which the
    /// player can then wear beside their name.
    ///
    /// Unlocking is CUMULATIVE and permanent. Reaching Gold grants Bronze and Silver too — you did in
    /// fact pass through them — and relegating back down never takes a badge away. A badge says where
    /// you have BEEN, which is why it is a collectible and not a live rank display; the live rank is
    /// the ranked pill on the main menu, and it already moves week to week.
    ///
    /// The badges themselves are ordinary catalogue entries of kind <see cref="CosmeticKind.Badge"/>,
    /// so the inventory owns and equips them exactly like a skin. What makes them earned rather than
    /// bought is only that they are priced zero, which keeps them out of the store and out of every
    /// chest without a rule anywhere else.
    /// </summary>
    public static class LeagueBadges
    {
        /// <summary>Raised when a new badge is unlocked, so a screen can announce it.</summary>
        public static event Action<League> OnUnlocked;

        /// <summary>The badge id for a league. Parallel to <see cref="Leagues.Name"/>.</summary>
        public static string IdFor(League league)
        {
            switch (league)
            {
                case League.Bronze: return "badge.bronze";
                case League.Silver: return "badge.silver";
                case League.Gold: return "badge.gold";
                case League.Diamond: return "badge.diamond";
                default: return "badge.bronze";
            }
        }

        /// <summary>The league a badge belongs to — the inverse of <see cref="IdFor"/>, for drawing a
        /// badge in its own tier colour without the caller carrying a second table.</summary>
        public static League LeagueOf(string badgeId)
        {
            for (int i = 0; i < Leagues.Count; i++)
            {
                var league = (League)i;
                if (IdFor(league) == badgeId) return league;
            }
            return League.Bronze;
        }

        public static bool IsBadge(string id) => CosmeticCatalog.Get(id).Kind == CosmeticKind.Badge;

        /// <summary>The badge currently worn beside the player name, or invalid when none is.</summary>
        public static CosmeticItem Worn => Inventory.Equipped(CosmeticKind.Badge);

        /// <summary>Every badge unlocked so far, lowest league first.</summary>
        public static bool Owns(League league) => Inventory.Owns(IdFor(league));

        /// <summary>The highest league whose badge is owned, or null with none yet.</summary>
        public static League? Highest
        {
            get
            {
                League? best = null;
                for (int i = 0; i < Leagues.Count; i++)
                {
                    var league = (League)i;
                    if (Owns(league)) best = league;
                }
                return best;
            }
        }

        /// <summary>
        /// Brings the badge collection up to date with the ladder: grants the current league's badge
        /// and every one below it.
        ///
        /// Driven off <see cref="Ladder.Standing"/> rather than off a promotion event, because there
        /// is no promotion event to hang it on — the ladder rolls over inside the backend and the game
        /// only ever sees the standing that came back afterwards. Reading the standing catches the
        /// unlock whether the player was watching or not, and re-running it costs nothing:
        /// <see cref="Inventory.Grant"/> is idempotent.
        ///
        /// Called from <c>GameFlow</c> on <see cref="Ladder.OnChanged"/>, the same place every other
        /// cross-system consequence of a match already lives.
        /// </summary>
        public static void Sync()
        {
            LadderStanding standing = Ladder.Standing;
            if (!standing.Valid)
            {
                return;
            }

            bool grantedAny = false;
            League highest = standing.League;

            for (int i = 0; i <= (int)highest; i++)
            {
                var league = (League)i;
                if (Inventory.Grant(IdFor(league)))
                {
                    grantedAny = true;
                    OnUnlocked?.Invoke(league);
                }
            }

            // Worn automatically the first time one is earned. A badge nobody equips is a reward the
            // player never sees, and there is no meaningful choice to protect here — the alternative
            // to their first badge is no badge at all.
            if (grantedAny && !Worn.Valid)
            {
                Inventory.Equip(IdFor(highest));
            }
        }
    }
}
