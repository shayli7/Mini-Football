using System;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The level path: one reward per level from 2 to <see cref="PlayerProgress.MaxLevel"/>, unlocked
    /// by levelling and CLAIMED by hand.
    ///
    /// Claiming rather than auto-granting is the whole design. A reward that lands silently while a
    /// results banner is on screen is a reward the player never saw; a path they walk back to and tap
    /// is a reason to open the game. It also means the path screen always has something to do on it,
    /// which is what turns a progress bar into a destination.
    ///
    /// Levels come from <see cref="PlayerProgress"/> and are not duplicated here — this file owns WHAT
    /// each level pays and WHETHER it has been collected, nothing about XP. The two big milestones
    /// kinds are a chest (a random skin, see <see cref="ChestLoot"/>) and a named skin; every other
    /// level pays coins.
    ///
    /// State is a fixed-length bitmask string in PlayerPrefs — one character per level, so the whole
    /// path is one key, a wipe is one delete, and the save is readable when something goes wrong.
    /// </summary>
    public static class LevelPath
    {
        private const string ClaimedKey = "TableFootball.LevelPath.Claimed";

        /// <summary>
        /// The levels that pay something other than coins. Every other level on the path pays coins,
        /// so this list IS the design of the path — the shape a player feels climbing it.
        /// </summary>
        public static readonly int[] Milestones = { 5, 10, 20, 30, 40, 50, 60 };

        /// <summary>What a level pays.</summary>
        public enum RewardKind
        {
            Coins = 0,
            Chest = 1,
            Skin = 2
        }

        /// <summary>
        /// One rung of the path. A value type built on demand from the rules below rather than stored:
        /// the table is a formula, and a 60-row array would be 60 places to get one number wrong.
        /// </summary>
        public readonly struct Reward
        {
            public readonly int Level;
            public readonly RewardKind Kind;

            /// <summary>Coins paid, for <see cref="RewardKind.Coins"/>.</summary>
            public readonly int Coins;

            /// <summary>Which chest, for <see cref="RewardKind.Chest"/>.</summary>
            public readonly ChestTier Tier;

            /// <summary>Which skin, for <see cref="RewardKind.Skin"/>.</summary>
            public readonly string CosmeticId;

            /// <summary>Drawn large on the path, with its own node. True for chests and skins.</summary>
            public bool IsMilestone => Kind != RewardKind.Coins;

            public Reward(int level, RewardKind kind, int coins, ChestTier tier, string cosmeticId)
            {
                Level = level;
                Kind = kind;
                Coins = coins;
                Tier = tier;
                CosmeticId = cosmeticId;
            }
        }

        /// <summary>
        /// What a claim actually produced, for the reveal card to draw. A chest resolves to a real
        /// item here, so the screen showing the result and the inventory holding it can never disagree.
        /// </summary>
        public readonly struct ClaimResult
        {
            public readonly bool Ok;
            public readonly int Level;

            /// <summary>Coins credited — a coin reward, a chest with nothing left to give, or a skin
            /// the player already owned.</summary>
            public readonly int Coins;

            /// <summary>The skin won, or invalid when the claim paid coins.</summary>
            public readonly CosmeticItem Item;

            /// <summary>True when the reward came out of a chest, so the reveal can say which chest.</summary>
            public readonly bool FromChest;

            public readonly ChestTier Tier;

            public ClaimResult(bool ok, int level, int coins, CosmeticItem item, bool fromChest,
                               ChestTier tier)
            {
                Ok = ok;
                Level = level;
                Coins = coins;
                Item = item;
                FromChest = fromChest;
                Tier = tier;
            }
        }

        /// <summary>Raised when a reward is claimed, for the path and the coin pill to redraw.</summary>
        public static event Action OnChanged;

        // ── The table ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// What <paramref name="level"/> pays. Level 1 pays nothing — it is where everybody starts,
        /// and a reward for arriving there would be a reward for installing the game.
        /// </summary>
        public static Reward For(int level)
        {
            switch (level)
            {
                case 5:  return new Reward(level, RewardKind.Chest, 0, ChestTier.Common, null);
                case 10: return new Reward(level, RewardKind.Skin, 0, ChestTier.Common, "field.deep_blue");
                case 20: return new Reward(level, RewardKind.Chest, 0, ChestTier.Rare, null);
                case 30: return new Reward(level, RewardKind.Skin, 0, ChestTier.Common, "ball.basketball");
                case 40: return new Reward(level, RewardKind.Chest, 0, ChestTier.Epic, null);
                case 50: return new Reward(level, RewardKind.Skin, 0, ChestTier.Common, "figure.stripes");
                case 60: return new Reward(level, RewardKind.Chest, 0, ChestTier.Legendary, null);
                default: return new Reward(level, RewardKind.Coins, CoinsForLevel(level),
                                           ChestTier.Common, null);
            }
        }

        /// <summary>
        /// Coins paid by an ordinary level. Banded rather than smooth, so the number visibly steps up
        /// as the path climbs — a reward that creeps by 7 coins a level reads as no reward at all.
        /// </summary>
        private static int CoinsForLevel(int level)
        {
            if (level <= 1) return 0;
            if (level < 10) return 100;
            if (level < 20) return 150;
            if (level < 30) return 200;
            if (level < 40) return 250;
            if (level < 50) return 300;
            return 400;
        }

        // ── Claim state ──────────────────────────────────────────────────────────────────────────

        public static bool IsClaimed(int level)
        {
            if (level < 2 || level > PlayerProgress.MaxLevel) return false;
            string mask = Mask();
            return mask[level - 1] == '1';
        }

        /// <summary>Reached, and not yet collected.</summary>
        public static bool CanClaim(int level) =>
            level >= 2 && level <= PlayerProgress.MaxLevel &&
            level <= PlayerProgress.Level && !IsClaimed(level);

        /// <summary>
        /// How many rewards are sitting unclaimed. Drives the badge on the main menu's path button —
        /// the one thing that tells a player there is something waiting without their going to look.
        /// </summary>
        public static int UnclaimedCount
        {
            get
            {
                int n = 0;
                int top = Mathf.Min(PlayerProgress.Level, PlayerProgress.MaxLevel);
                for (int level = 2; level <= top; level++)
                {
                    if (!IsClaimed(level)) n++;
                }
                return n;
            }
        }

        /// <summary>
        /// Collects one level's reward: marks it claimed, then pays it out. Refuses anything not
        /// reached or already collected, so a double tap and a stale screen both cost nothing.
        ///
        /// Marked claimed BEFORE paying, deliberately. A chest roll grants an item and saves, and if
        /// the claim flag were written after that it would be possible — through an interrupted save —
        /// to bank the item and still owe the reward.
        /// </summary>
        public static ClaimResult Claim(int level)
        {
            if (!CanClaim(level))
            {
                return default;
            }

            SetClaimed(level);
            Reward reward = For(level);

            switch (reward.Kind)
            {
                case RewardKind.Chest:
                {
                    ChestDrop drop = ChestLoot.Open(reward.Tier);
                    OnChanged?.Invoke();
                    return new ClaimResult(true, level, drop.Coins, drop.Item, true, reward.Tier);
                }

                case RewardKind.Skin:
                {
                    CosmeticItem item = CosmeticCatalog.Get(reward.CosmeticId);

                    // Already bought from the store. Paid out at the shop price instead, so climbing to
                    // a skin you happened to own early is never worse than not owning it.
                    if (!item.Valid || !Inventory.Grant(item.Id))
                    {
                        int refund = item.Valid ? Mathf.Max(item.Price, 200) : 200;
                        Wallet.Add(refund);
                        OnChanged?.Invoke();
                        return new ClaimResult(true, level, refund, default, false, reward.Tier);
                    }

                    OnChanged?.Invoke();
                    return new ClaimResult(true, level, 0, item, false, reward.Tier);
                }

                default:
                {
                    Wallet.Add(reward.Coins);
                    OnChanged?.Invoke();
                    return new ClaimResult(true, level, reward.Coins, default, false, reward.Tier);
                }
            }
        }

        /// <summary>
        /// Sweeps up every unclaimed COIN reward and reports the total.
        ///
        /// Milestones are deliberately left behind. A "claim all" that silently opened four chests
        /// would hide the one part of the path worth watching, so chests and skins stay one tap each
        /// and get their reveal; this only clears the small change.
        ///
        /// The coins are added ONCE at the end rather than level by level. Going through
        /// <see cref="Claim"/> per level would fire <see cref="Wallet.OnChanged"/> and
        /// <see cref="OnChanged"/> up to fifty-nine times, restarting the coin pill's count-up on each
        /// one — so the player would watch a single animation from the last level's value instead of
        /// the whole sum they just collected.
        /// </summary>
        public static int ClaimAllCoins()
        {
            int total = 0;
            int top = Mathf.Min(PlayerProgress.Level, PlayerProgress.MaxLevel);

            for (int level = 2; level <= top; level++)
            {
                if (!CanClaim(level)) continue;

                Reward reward = For(level);
                if (reward.IsMilestone) continue;

                SetClaimed(level);
                total += reward.Coins;
            }

            if (total > 0)
            {
                Wallet.Add(total);
                OnChanged?.Invoke();
            }

            return total;
        }

        /// <summary>Whether anything claimable is a plain coin reward — what "Claim All" would take.</summary>
        public static bool HasClaimableCoins()
        {
            int top = Mathf.Min(PlayerProgress.Level, PlayerProgress.MaxLevel);
            for (int level = 2; level <= top; level++)
            {
                if (CanClaim(level) && !For(level).IsMilestone) return true;
            }
            return false;
        }

        /// <summary>Wipes the path, alongside the rest of a player change. See
        /// <see cref="Inventory.ResetLocal"/>.</summary>
        public static void ResetLocal()
        {
            PlayerPrefs.DeleteKey(ClaimedKey);
            PlayerPrefs.Save();
            cached = null;
            OnChanged?.Invoke();
        }

        private static string cached;

        /// <summary>
        /// The claim bitmask, always exactly <see cref="PlayerProgress.MaxLevel"/> characters. Padded
        /// on read rather than trusted: a save written by a build with a lower cap is shorter, and
        /// indexing past its end would throw on the one screen that shows the whole path.
        /// </summary>
        private static string Mask()
        {
            if (cached != null && cached.Length == PlayerProgress.MaxLevel)
            {
                return cached;
            }

            string saved = PlayerPrefs.GetString(ClaimedKey, string.Empty);
            if (saved.Length < PlayerProgress.MaxLevel)
            {
                saved = saved.PadRight(PlayerProgress.MaxLevel, '0');
            }
            else if (saved.Length > PlayerProgress.MaxLevel)
            {
                saved = saved.Substring(0, PlayerProgress.MaxLevel);
            }

            cached = saved;
            return cached;
        }

        private static void SetClaimed(int level)
        {
            char[] mask = Mask().ToCharArray();
            mask[level - 1] = '1';
            cached = new string(mask);
            PlayerPrefs.SetString(ClaimedKey, cached);
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
        }

        // ── Cloud sync ─────────────────────────────────────────────────────────────────────────
        // LevelPath owns the claim bitmask; CloudSync ORs two masks so a level claimed on either
        // device stays claimed.

        /// <summary>Copies the claim mask (padded to the cap) into the save document.</summary>
        internal static void ExportTo(CloudSync.SaveDoc d)
        {
            d.levelClaimed = Mask();
        }

        /// <summary>Writes the reconciled mask back and redraws. Straight to PlayerPrefs, and clears
        /// the cache so the next read reflects the merged mask rather than the pre-load one.</summary>
        internal static void ImportFrom(CloudSync.SaveDoc d)
        {
            PlayerPrefs.SetString(ClaimedKey, d.levelClaimed ?? string.Empty);
            PlayerPrefs.Save();
            cached = null;
            OnChanged?.Invoke();
        }
    }
}
