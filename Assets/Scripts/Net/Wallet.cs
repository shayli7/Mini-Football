using System;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Gold coins — the game's one currency, and the only thing the store spends.
    ///
    /// Deliberately the same shape as <see cref="PlayerProgress"/> and <see cref="MatchStats"/>: a
    /// static, PlayerPrefs-backed total with an <see cref="OnChanged"/> event, so screens read it
    /// synchronously and redraw rather than polling. A reward lands and the coin pill moves in the
    /// same frame, with nothing to wait on.
    ///
    /// Coins are EARNED, never bought. Two sources credit this, and they are the only two:
    /// <see cref="RankedRewards"/> at the weekly ladder rollover, and <see cref="LevelPath"/> when a
    /// level's reward is claimed. Both call <see cref="Add"/>; nothing else should.
    ///
    /// Local and authoritative for this device, like the rest of progression. When a real server owns
    /// the balance this file is where that swap happens and no screen changes.
    /// </summary>
    public static class Wallet
    {
        private const string CoinsKey = "TableFootball.Wallet.Coins";

        /// <summary>
        /// What a brand-new player starts with. Enough to buy one cheap cosmetic on day one, so the
        /// store has something to show a player who opens it before earning anything — an empty shop
        /// with an empty purse teaches nothing about what coins are for.
        /// </summary>
        public const int StartingCoins = 250;

        private const string SeededKey = "TableFootball.Wallet.Seeded";

        /// <summary>The current balance. Never negative.</summary>
        public static int Coins
        {
            get
            {
                EnsureSeeded();
                return PlayerPrefs.GetInt(CoinsKey, 0);
            }
        }

        /// <summary>Raised whenever the balance moves, for the UI to redraw.</summary>
        public static event Action OnChanged;

        /// <summary>
        /// Credits coins. Non-positive amounts are ignored rather than treated as a spend — a reward
        /// that computed to zero should be a no-op, not a silent debit.
        /// </summary>
        public static void Add(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            EnsureSeeded();
            Write(PlayerPrefs.GetInt(CoinsKey, 0) + amount);
        }

        /// <summary>
        /// Spends coins if there are enough, and reports whether it happened. The check and the debit
        /// are one call on purpose: a caller that asks "can I afford it?" and then subtracts is two
        /// steps that can disagree, and the one that gets skipped is always the check.
        /// </summary>
        public static bool TrySpend(int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            EnsureSeeded();
            int balance = PlayerPrefs.GetInt(CoinsKey, 0);
            if (balance < amount)
            {
                return false;
            }

            Write(balance - amount);
            return true;
        }

        public static bool CanAfford(int amount) => Coins >= amount;

        /// <summary>
        /// Zeroes the purse and re-seeds the starting float. Called when the player this device
        /// belongs to changes, for the same reason <see cref="PlayerProgress.ResetLocal"/> is: coins
        /// are a record of what a PERSON earned, and carrying them onto the next person would hand
        /// them somebody else's rewards.
        /// </summary>
        public static void ResetLocal()
        {
            PlayerPrefs.DeleteKey(CoinsKey);
            PlayerPrefs.DeleteKey(SeededKey);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }

        private static void Write(int value)
        {
            PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, value));
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
            OnChanged?.Invoke();
        }

        /// <summary>
        /// Grants the opening float exactly once per player.
        ///
        /// Guarded by its own flag rather than by "the balance is 0", which is not the same question:
        /// a player who spends down to nothing would be handed the float a second time, every time
        /// they hit zero.
        /// </summary>
        private static void EnsureSeeded()
        {
            if (PlayerPrefs.GetInt(SeededKey, 0) != 0)
            {
                return;
            }

            PlayerPrefs.SetInt(SeededKey, 1);
            PlayerPrefs.SetInt(CoinsKey, PlayerPrefs.GetInt(CoinsKey, 0) + StartingCoins);
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
        }

        // ── Cloud sync ─────────────────────────────────────────────────────────────────────────
        // Wallet owns these two keys; CloudSync sees only the document fields, never the key names.

        /// <summary>Copies the balance and the seeded flag into the save document.</summary>
        internal static void ExportTo(CloudSync.SaveDoc d)
        {
            d.coins = PlayerPrefs.GetInt(CoinsKey, 0);
            d.walletSeeded = PlayerPrefs.GetInt(SeededKey, 0) != 0;
        }

        /// <summary>Writes a reconciled balance and seeded flag back, then redraws. Goes straight to
        /// PlayerPrefs rather than through <see cref="Add"/>/<see cref="Write"/> so it does not mark
        /// the store dirty again — this value came FROM the cloud.</summary>
        internal static void ImportFrom(CloudSync.SaveDoc d)
        {
            PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, d.coins));
            PlayerPrefs.SetInt(SeededKey, d.walletSeeded ? 1 : 0);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }
}
