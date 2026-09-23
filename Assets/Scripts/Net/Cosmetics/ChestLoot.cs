using System.Collections.Generic;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// How good a chest is. The tier picks the odds table, nothing else — a Legendary chest is not a
    /// different mechanism from a Common one, only a different set of weights over the same catalogue.
    /// </summary>
    public enum ChestTier
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3
    }

    /// <summary>What a chest actually gave. Returned by <see cref="ChestLoot.Open"/>.</summary>
    public readonly struct ChestDrop
    {
        /// <summary>The skin that dropped. Invalid when the chest paid coins instead.</summary>
        public readonly CosmeticItem Item;

        /// <summary>Coins paid in place of a skin, when there was no unowned skin left to give.</summary>
        public readonly int Coins;

        public readonly ChestTier Tier;

        public bool IsCoins => Coins > 0;

        public ChestDrop(CosmeticItem item, int coins, ChestTier tier)
        {
            Item = item;
            Coins = coins;
            Tier = tier;
        }
    }

    /// <summary>
    /// The skin chest: rolls a rarity from the chest's odds, then a random skin the player does not
    /// already own at that rarity, and grants it.
    ///
    /// The pool is <see cref="CosmeticCatalog"/> itself, filtered to droppable items — so dropping a
    /// new skin into the game is a row in the catalogue and nothing here changes. Defaults and league
    /// badges are excluded by <see cref="CosmeticItem.CanDropFromChest"/>, which is why there is no
    /// list of exceptions in this file.
    ///
    /// Two rules keep a chest from ever feeling wasted, and both matter more than the odds do:
    ///
    /// - A rarity with nothing unowned left in it does not produce a duplicate. The roll walks OUTWARD
    ///   to the nearest rarity that still has something, preferring the rarer side first. A player
    ///   who has completed the Common tier gets pulled upward by their own progress rather than being
    ///   handed the same skin twice.
    /// - Only when the ENTIRE droppable catalogue is owned does a chest pay coins instead. That is the
    ///   end of the collection, not a bad roll, and the payout scales with the tier so a Legendary
    ///   chest is still worth opening.
    /// </summary>
    public static class ChestLoot
    {
        /// <summary>
        /// Odds per chest tier, indexed [tier][rarity], as relative weights that need not sum to 100.
        /// Reading down a column shows how a rarity's chance grows with the chest; reading across a
        /// row shows one chest's whole distribution — which is the shape a designer actually tunes.
        /// </summary>
        private static readonly int[][] Weights =
        {
            //             Common  Rare  Epic  Legendary
            new[] {            70,   25,    5,         0 }, // Common chest
            new[] {            35,   45,   18,         2 }, // Rare chest
            new[] {            10,   40,   42,         8 }, // Epic chest
            new[] {             0,   20,   50,        30 }  // Legendary chest
        };

        /// <summary>Coins paid per tier when there is nothing left in the catalogue to win.</summary>
        private static readonly int[] ConsolationCoins = { 150, 350, 700, 1400 };

        /// <summary>
        /// Opens one chest: rolls, grants, and reports what happened. Granting is done HERE rather
        /// than by the caller, so a drop that is shown on screen is always a drop that was actually
        /// banked — the two cannot come apart.
        /// </summary>
        public static ChestDrop Open(ChestTier tier)
        {
            int rolled = RollRarity(tier);

            // Outward from the rolled rarity: rarer first (an unlucky roll should never be worse than
            // the tier promised), then downward.
            for (int distance = 0; distance < 4; distance++)
            {
                CosmeticItem up = PickUnowned(rolled + distance);
                if (up.Valid)
                {
                    Inventory.Grant(up.Id);
                    return new ChestDrop(up, 0, tier);
                }

                if (distance == 0) continue; // rolled+0 and rolled-0 are the same bucket

                CosmeticItem down = PickUnowned(rolled - distance);
                if (down.Valid)
                {
                    Inventory.Grant(down.Id);
                    return new ChestDrop(down, 0, tier);
                }
            }

            int coins = ConsolationCoins[Mathf.Clamp((int)tier, 0, ConsolationCoins.Length - 1)];
            Wallet.Add(coins);
            return new ChestDrop(default, coins, tier);
        }

        /// <summary>
        /// Whether a chest still has a skin to give. The level path uses it to word its reward chip
        /// honestly — a chest that can only pay coins should not promise a skin.
        /// </summary>
        public static bool AnySkinsLeft()
        {
            foreach (var item in CosmeticCatalog.All)
            {
                if (item.CanDropFromChest && !Inventory.Owns(item.Id)) return true;
            }
            return false;
        }

        /// <summary>The tier's display name, for a reward chip or a reveal card.</summary>
        public static string TierName(ChestTier tier) =>
            CosmeticCatalog.RarityName((Rarity)(int)tier) + " Chest";

        private static int RollRarity(ChestTier tier)
        {
            int[] row = Weights[Mathf.Clamp((int)tier, 0, Weights.Length - 1)];

            int total = 0;
            foreach (int w in row) total += w;
            if (total <= 0) return (int)Rarity.Common;

            // Random.Range(int,int) is max-exclusive, so this lands in [0, total-1] and the running
            // sum below covers every weight exactly once.
            int pick = Random.Range(0, total);
            for (int i = 0; i < row.Length; i++)
            {
                pick -= row[i];
                if (pick < 0) return i;
            }

            return row.Length - 1;
        }

        /// <summary>A random unowned droppable item at one rarity, or invalid if that bucket is dry.</summary>
        private static CosmeticItem PickUnowned(int rarity)
        {
            if (rarity < 0 || rarity > (int)Rarity.Legendary)
            {
                return default;
            }

            var pool = new List<CosmeticItem>();
            foreach (var item in CosmeticCatalog.All)
            {
                if (item.CanDropFromChest && (int)item.Rarity == rarity && !Inventory.Owns(item.Id))
                {
                    pool.Add(item);
                }
            }

            return pool.Count == 0 ? default : pool[Random.Range(0, pool.Count)];
        }
    }
}
