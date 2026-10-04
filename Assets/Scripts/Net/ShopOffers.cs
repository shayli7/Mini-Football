using System;
using System.Globalization;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The store's day-by-day offers: a free chest once a day (the daily deal), a small coin top-up a few
    /// times a day, and three skins on sale for coins (the daily items). Same shape as <see cref="Wallet"/>: static, PlayerPrefs-backed, read
    /// synchronously, with an <see cref="OnChanged"/> event for screens to redraw on. It holds the
    /// rules, so the store screen only asks and shows.
    ///
    /// The day is the device's LOCAL calendar day, so both offers reset at the player's own midnight.
    /// The limits belong to the DEVICE, not the player: <see cref="PlayerAccount"/> does not reset them
    /// on a player change, or switching players would be a way to watch the same deal twice.
    ///
    /// Nothing here syncs to the cloud. The coins an ad pays go through <see cref="Wallet"/>, which
    /// does; the counters are one day's worth of state and are not worth a merge rule.
    /// </summary>
    public static class ShopOffers
    {
        /// <summary>Coins paid for one watched coin ad.</summary>
        public const int AdCoins = 50;

        /// <summary>How many coin ads can be watched in one day.</summary>
        public const int AdsPerDay = 5;

        /// <summary>
        /// Out of 100, how often the daily deal is a Rare chest rather than a Common one. Rolled once
        /// per day from the date, so reopening the app never rerolls it.
        /// </summary>
        private const int RareDealChance = 30;

        /// <summary>How many skins go on sale each day.</summary>
        public const int DailyItemCount = 3;

        private const string DayKey = "TableFootball.Shop.Day";
        private const string DealClaimedKey = "TableFootball.Shop.DealClaimed";
        private const string AdsUsedKey = "TableFootball.Shop.AdsUsed";
        private const string DailyItemsKey = "TableFootball.Shop.DailyItems";

        /// <summary>Raised when an offer is used. A new day resetting them does not raise it; see RollDayIfNeeded.</summary>
        public static event Action OnChanged;

        /// <summary>Today's free chest: Common or Rare, the same all day.</summary>
        public static ChestTier DealTier
        {
            get
            {
                // Seeded from the date itself, so every launch on the same day gets the same answer
                // without storing it. A plain hash (not string.GetHashCode, which differs between runs)
                // spread over 0-99.
                DateTime d = DateTime.Now.Date;
                int seed = d.Year * 372 + d.Month * 31 + d.Day;
                int roll = (int)((uint)(seed * 2654435761u) % 100u);
                return roll < RareDealChance ? ChestTier.Rare : ChestTier.Common;
            }
        }

        public static bool DealClaimed
        {
            get
            {
                RollDayIfNeeded();
                return PlayerPrefs.GetInt(DealClaimedKey, 0) == 1;
            }
        }

        public static int AdsLeft
        {
            get
            {
                RollDayIfNeeded();
                return Mathf.Max(0, AdsPerDay - PlayerPrefs.GetInt(AdsUsedKey, 0));
            }
        }

        /// <summary>Time until both offers reset: the next local midnight.</summary>
        public static TimeSpan UntilReset => DateTime.Now.Date.AddDays(1) - DateTime.Now;

        /// <summary>
        /// Plays an ad and, if it was watched, opens today's free chest. Reports the drop, or null if
        /// the deal was already used or the ad was not finished. The claim is written BEFORE the chest
        /// opens, for the same reason <see cref="LevelPath.Claim"/> does it: an interrupted save must
        /// never leave the item banked and the deal still open.
        /// </summary>
        public static void ClaimDeal(Action<ChestDrop?> onDone)
        {
            if (DealClaimed)
            {
                onDone?.Invoke(null);
                return;
            }

            ChestTier tier = DealTier;
            AdService.ShowRewarded(watched =>
            {
                // Checked again: the ad takes time, and the day may have turned or another tap may
                // have claimed it meanwhile.
                if (!watched || DealClaimed)
                {
                    onDone?.Invoke(null);
                    return;
                }

                PlayerPrefs.SetInt(DealClaimedKey, 1);
                PlayerPrefs.Save();
                ChestDrop drop = ChestLoot.Open(tier);
                OnChanged?.Invoke();
                onDone?.Invoke(drop);
            });
        }

        /// <summary>
        /// Plays an ad and, if it was watched, pays <see cref="AdCoins"/>. Reports whether it paid.
        /// </summary>
        public static void WatchCoinAd(Action<bool> onDone)
        {
            if (AdsLeft <= 0)
            {
                onDone?.Invoke(false);
                return;
            }

            AdService.ShowRewarded(watched =>
            {
                if (!watched || AdsLeft <= 0)
                {
                    onDone?.Invoke(false);
                    return;
                }

                PlayerPrefs.SetInt(AdsUsedKey, PlayerPrefs.GetInt(AdsUsedKey, 0) + 1);
                PlayerPrefs.Save();
                Wallet.Add(AdCoins);
                OnChanged?.Invoke();
                onDone?.Invoke(true);
            });
        }

        /// <summary>
        /// Today's skins for sale: up to <see cref="DailyItemCount"/> that the player did not own when
        /// the day began, the same all day. Chosen once per local day and then stored, so buying one
        /// leaves it on the shelf marked owned rather than rerolling the other two. Only skins that
        /// can drop from a chest are offered — never a league badge, which is earned.
        /// </summary>
        public static CosmeticItem[] DailyItems
        {
            get
            {
                RollDayIfNeeded();

                var picked = new System.Collections.Generic.List<CosmeticItem>();
                string stored = PlayerPrefs.GetString(DailyItemsKey, string.Empty);
                if (!string.IsNullOrEmpty(stored))
                {
                    foreach (string id in stored.Split(','))
                    {
                        CosmeticItem item = CosmeticCatalog.Get(id);
                        if (item.Valid && item.CanDropFromChest) picked.Add(item);
                    }
                }

                if (picked.Count == 0)
                {
                    picked = RollDailyItems();
                    var ids = new string[picked.Count];
                    for (int i = 0; i < ids.Length; i++) ids[i] = picked[i].Id;
                    PlayerPrefs.SetString(DailyItemsKey, string.Join(",", ids));
                    PlayerPrefs.Save();
                }

                return picked.ToArray();
            }
        }

        /// <summary>What a daily item costs. One place, so the price list is a single edit.</summary>
        public static int DailyItemPrice(CosmeticItem item) => item.Price;

        /// <summary>
        /// Buys one of today's items with coins. False if it is not on today's shelf, is already owned,
        /// or cannot be afforded — nothing is spent then. The coins are taken first and the skin granted
        /// second, in the same call, so an item is never given without being paid for.
        /// </summary>
        public static bool BuyDailyItem(string id)
        {
            CosmeticItem found = default;
            foreach (CosmeticItem item in DailyItems)
            {
                if (item.Id == id) found = item;
            }

            if (!found.Valid || Inventory.Owns(found.Id)) return false;
            if (!Wallet.TrySpend(DailyItemPrice(found))) return false;

            Inventory.Grant(found.Id);
            OnChanged?.Invoke();
            return true;
        }

        private static System.Collections.Generic.List<CosmeticItem> RollDailyItems()
        {
            var pool = new System.Collections.Generic.List<CosmeticItem>();
            foreach (CosmeticItem item in CosmeticCatalog.All)
            {
                if (item.CanDropFromChest && item.IsForSale && !Inventory.Owns(item.Id)) pool.Add(item);
            }

            // A shuffle seeded by the date, so a reinstall on the same day sees the same shelf.
            DateTime d = DateTime.Now.Date;
            var rng = new System.Random(d.Year * 372 + d.Month * 31 + d.Day);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                CosmeticItem tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            if (pool.Count > DailyItemCount) pool.RemoveRange(DailyItemCount, pool.Count - DailyItemCount);
            return pool;
        }

        /// <summary>
        /// Clears yesterday's counters the first time anything is read on a new day. Lazy rather than
        /// timed, so a phone left on the store screen across midnight still resets on the next read.
        /// The store's countdown redraw is what triggers that read.
        ///
        /// Deliberately raises no <see cref="OnChanged"/>: it runs inside the getters, and a screen
        /// that redraws on the event would be redrawn from the middle of its own redraw.
        /// </summary>
        private static void RollDayIfNeeded()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (PlayerPrefs.GetString(DayKey, string.Empty) == today)
            {
                return;
            }

            PlayerPrefs.SetString(DayKey, today);
            PlayerPrefs.SetInt(DealClaimedKey, 0);
            PlayerPrefs.SetInt(AdsUsedKey, 0);
            PlayerPrefs.SetString(DailyItemsKey, string.Empty);
            PlayerPrefs.Save();
        }
    }
}
