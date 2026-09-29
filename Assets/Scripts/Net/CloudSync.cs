using System;
using System.Threading.Tasks;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Makes progression portable and durable by backing the local PlayerPrefs stores with Unity
    /// Cloud Save — without any screen changing how it reads them.
    ///
    /// The stores (<see cref="Wallet"/>, <see cref="Inventory"/>, <see cref="PlayerProgress"/>,
    /// <see cref="LevelPath"/>, <see cref="RankedRewards"/>, <see cref="MatchStats"/>) stay exactly
    /// what they were: synchronous, PlayerPrefs-backed, read this frame and redraw off an OnChanged
    /// event. This sits ON TOP of them as a sync layer, never between them and the UI:
    ///
    /// - On sign-in, <see cref="LoadAsync"/> pulls the player's cloud document, merges it into the
    ///   local stores, and lets the UI redraw off the change events it already listens to.
    /// - On every write, a store calls <see cref="MarkDirty"/>; a debounced background flush
    ///   (<see cref="FlushAsync"/>) pushes the merged result back up. Reads never touch the network.
    ///
    /// That is the swap <see cref="Wallet"/>'s own header predicted — "when a real server owns the
    /// balance this file is where that swap happens and no screen changes." It happens here instead,
    /// in one place, so the six stores needed only an export/import pair each.
    ///
    /// MONOTONIC MERGE. Two devices (or offline play then a reconnect) are reconciled so the player
    /// can never LOSE anything to a stale device: owned skins are unioned, claimed levels are OR'd,
    /// and every counter (xp, wins, coins, …) takes the larger value. The one honest cost is that a
    /// spend made on one device can be briefly refunded by a staler one choosing the higher balance —
    /// player-favourable, rare (it needs two devices), and acceptable for a currency that is earned,
    /// never bought. Making coins exact would need a lifetime-earned ledger; see the header on
    /// <see cref="MergeInt"/>.
    ///
    /// Nothing here throws outward, matching <see cref="GameServices"/>: a phone with no signal plays
    /// exactly like one with a dead UGS project. A failed load leaves the local save standing; a
    /// failed flush keeps the dirty flag so the next good connection carries it up.
    ///
    /// SERVER-AUTHORITATIVE WRITE. <see cref="CloudSaveBackend"/> does not write Unity Cloud Save's
    /// player data directly — that is client-writable by design, and this class's own monotonic merge
    /// ("every counter takes the larger value") would keep a forged flush forever if it did. Both
    /// calls instead go through the <c>progress</c> Cloud Code script, which stores the document
    /// where the client SDK cannot write it at all and bounds coins/newly-owned cosmetics before
    /// persisting — see <c>cloudcode/progress.js</c> and <c>cloudcode/README.md</c>. This class's own
    /// <see cref="Merge"/> still runs client-side in <see cref="LoadAsync"/>, purely to give the UI an
    /// immediate, optimistic value to redraw with before the network answers; what actually persists
    /// and reaches other devices is decided by the server's own merge, applied back locally from
    /// whatever <see cref="FlushAsync"/> gets handed back.
    /// </summary>
    public static class CloudSync
    {
        /// <summary>The single Cloud Save key the whole document lives under. Versioned so a future
        /// shape change can migrate rather than collide with saves written by an older build.</summary>
        private const string Key = "progress_v1";

        /// <summary>Current document schema. Written into every save; read back for migrations.</summary>
        private const int Schema = 1;

        /// <summary>How long after the last write to wait before flushing, in unscaled seconds. Long
        /// enough that a burst of rewards (a Claim All firing many OnChanged) becomes one upload, short
        /// enough that a player who earns something and closes the app has almost always flushed.</summary>
        private const float DebounceSeconds = 4f;

        private static bool dirty;
        private static float lastDirtyTime;
        private static bool flushing;

        /// <summary>Set across a load and a player-change so a flush triggered mid-hydration cannot
        /// push a half-applied document back up. The dirty flag survives, so the flush still happens
        /// once the gate lifts.</summary>
        private static bool suspendFlush;

        /// <summary>True once a load has actually applied a cloud document (or confirmed there was
        /// none). The UI does not wait on this — the merge is resilient to reads that land first, and
        /// the OnChanged redraw corrects them — but boot flow can check it if it wants to.</summary>
        public static bool Loaded { get; private set; }

        /// <summary>
        /// The whole save document, as one flat serializable shape for <see cref="JsonUtility"/>.
        ///
        /// Each store owns the fields that are its own and nothing reaches across: a store fills its
        /// slice in its <c>ExportTo</c> and writes it back in its <c>ImportFrom</c>, so the key names
        /// stay private to the store that invented them. This type is only the transport and the
        /// thing the merge rules are written against.
        /// </summary>
        [Serializable]
        public class SaveDoc
        {
            public int schema = Schema;

            // Wallet
            public int coins;
            public bool walletSeeded;

            // Inventory
            public string owned = string.Empty;   // comma-separated owned ids
            public string[] equipped = Array.Empty<string>(); // indexed by (int)CosmeticKind

            // PlayerProgress
            public int xp;
            public int wins;
            public int losses;
            public int goals;
            public int playTime;                   // whole seconds

            // LevelPath
            public string levelClaimed = string.Empty; // up-to-MaxLevel-char '0'/'1' bitmask

            // MatchStats
            public int onlineWins;
            public int onlineLosses;

            // RankedRewards
            public bool hasSeen;                   // has a week snapshot been recorded at all
            public int seenWeek;
            public int seenLeague;
            public int seenRank;
            public int pendingCoins;
            public int pendingLeague;
            public int pendingRank;
        }

        // ── Dirty tracking ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by a store after it changes something worth persisting. Cheap by design — it sets a
        /// flag and a timestamp; the runner does the actual upload once writes go quiet. Safe to call
        /// before anyone has signed in: with no cloud the flag simply waits, and a flush no-ops until
        /// there is somewhere to send it.
        /// </summary>
        public static void MarkDirty()
        {
            dirty = true;
            lastDirtyTime = Time.unscaledTime;
            Ensure();
        }

        /// <summary>Whether the runner should flush this frame: dirty, idle past the debounce, not
        /// already flushing, not suspended, and actually signed in.</summary>
        internal static bool ShouldFlush() =>
            dirty && !flushing && !suspendFlush && GameServices.IsSignedIn
            && Time.unscaledTime - lastDirtyTime >= DebounceSeconds;

        // ── Load ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Pulls this player's cloud document and merges it into the local stores, then lets every
        /// store raise its OnChanged so the UI redraws to the reconciled values.
        ///
        /// Awaited by the boot path and by a player change (<see cref="PlayerAccount"/>), but the UI
        /// does not block on it: reads that land before it completes see the local save and are
        /// corrected by the redraw, because the merge can never take something away. Returns quietly
        /// when offline — the local save is authoritative until there is a cloud to reconcile with.
        /// </summary>
        public static async Task LoadAsync()
        {
            Ensure();

            if (!await GameServices.EnsureSignedInAsync())
            {
                return; // Offline. Local save stands; a later MarkDirty/flush will carry it up.
            }

            suspendFlush = true;
            try
            {
                SaveDoc local = ExportLocal();
                SaveDoc remote = null;

                try
                {
                    string json = await CloudSaveBackend.LoadAsync(Key);
                    if (!string.IsNullOrEmpty(json))
                    {
                        remote = JsonUtility.FromJson<SaveDoc>(json);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"CloudSync load failed, keeping local save: {e.Message}");
                    return;
                }

                SaveDoc merged = remote == null ? local : Merge(local, remote);
                ApplyLocal(merged);
                Loaded = true;

                // Push the merge back up when the cloud did not already hold exactly it — a first-ever
                // save, or a local that had earned something offline. A no-op otherwise.
                if (remote == null || JsonUtility.ToJson(remote) != JsonUtility.ToJson(merged))
                {
                    MarkDirty();
                }
            }
            finally
            {
                suspendFlush = false;
            }
        }

        // ── Flush ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Uploads the local save. The server (<c>cloudcode/progress.js</c>, via
        /// <see cref="CloudSaveBackend"/>) does its own load-merge-bound-save against whatever it
        /// actually has stored and hands back what it accepted — which is applied back locally rather
        /// than trusted because it was what was sent. That is what makes a second device's progress
        /// merge in rather than get clobbered (a skin bought elsewhere appears here without a reload),
        /// AND what stops a tampered local save from persisting: if this device's export claims more
        /// than the server's bounds allow, what comes back is the server's clamped figure, not this
        /// device's.
        ///
        /// Clears the dirty flag up front and restores it on failure, so a write that arrives mid-flush
        /// is not lost and a failed upload is retried.
        /// </summary>
        public static async Task FlushAsync()
        {
            if (flushing || suspendFlush || !GameServices.IsSignedIn || !dirty)
            {
                return;
            }

            flushing = true;
            dirty = false;
            try
            {
                SaveDoc local = ExportLocal();

                string json = await CloudSaveBackend.SaveAsync(Key, JsonUtility.ToJson(local));
                SaveDoc accepted = string.IsNullOrEmpty(json) ? local : JsonUtility.FromJson<SaveDoc>(json);

                // Reflect anything the server pulled in from another device, or clamped away from
                // this one, without looping: Apply writes PlayerPrefs and raises OnChanged but never
                // calls MarkDirty.
                if (JsonUtility.ToJson(local) != JsonUtility.ToJson(accepted))
                {
                    ApplyLocal(accepted);
                }
            }
            catch (Exception e)
            {
                dirty = true; // keep it pending so the next attempt retries
                Debug.LogWarning($"CloudSync flush failed, will retry: {e.Message}");
            }
            finally
            {
                flushing = false;
            }
        }

        /// <summary>Flushes immediately, skipping the debounce — for app pause and quit, where waiting
        /// is not an option. Still a no-op when there is nothing dirty or nowhere to send it.</summary>
        public static void FlushNow()
        {
            if (dirty)
            {
                _ = FlushAsync();
            }
        }

        // ── Merge rules ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reconciles two documents so the player keeps the best of both. Symmetric for every counter
        /// (max) and every collection (union / OR); asymmetric only where "the device in the player's
        /// hand" is the better authority — the equipped choices prefer <paramref name="local"/>.
        /// </summary>
        private static SaveDoc Merge(SaveDoc local, SaveDoc remote)
        {
            var m = new SaveDoc
            {
                schema = Schema,

                coins = MergeInt(local.coins, remote.coins),
                walletSeeded = local.walletSeeded || remote.walletSeeded,

                owned = UnionCsv(local.owned, remote.owned),
                equipped = MergeEquipped(local.equipped, remote.equipped),

                xp = MergeInt(local.xp, remote.xp),
                wins = MergeInt(local.wins, remote.wins),
                losses = MergeInt(local.losses, remote.losses),
                goals = MergeInt(local.goals, remote.goals),
                playTime = MergeInt(local.playTime, remote.playTime),

                levelClaimed = OrMask(local.levelClaimed, remote.levelClaimed),

                onlineWins = MergeInt(local.onlineWins, remote.onlineWins),
                onlineLosses = MergeInt(local.onlineLosses, remote.onlineLosses)
            };

            // Ranked snapshot moves as a UNIT: the side that watched the more recent week owns the
            // whole (week, league, rank) triple, because a rollover is detected by comparing against
            // it and a mixed triple could mis-detect. A side that never looked (hasSeen == false)
            // always loses to one that did.
            bool takeRemoteSeen =
                remote.hasSeen && (!local.hasSeen || remote.seenWeek > local.seenWeek);
            SaveDoc seen = takeRemoteSeen ? remote : local;
            m.hasSeen = local.hasSeen || remote.hasSeen;
            m.seenWeek = seen.seenWeek;
            m.seenLeague = seen.seenLeague;
            m.seenRank = seen.seenRank;

            // Pending payout: never lose owed coins (max), and carry the describing league/rank from
            // whichever side actually held that larger amount.
            m.pendingCoins = MergeInt(local.pendingCoins, remote.pendingCoins);
            SaveDoc pend = remote.pendingCoins > local.pendingCoins ? remote : local;
            m.pendingLeague = pend.pendingLeague;
            m.pendingRank = pend.pendingRank;

            return m;
        }

        /// <summary>
        /// The monotonic rule for a counter: the larger value wins. Correct for xp, wins, goals and
        /// the rest, which only ever climb. Applied to coins too, which is the one field it is not
        /// strictly right for — a spend lowers coins legitimately, so a staler device's higher balance
        /// can refund it. The exact fix is a lifetime-earned/spent pair merged by max each; left out
        /// on purpose as more machinery than an earned-only currency warrants. See the class header.
        /// </summary>
        private static int MergeInt(int a, int b) => Mathf.Max(a, b);

        private static string[] MergeEquipped(string[] local, string[] remote)
        {
            int n = Mathf.Max(local?.Length ?? 0, remote?.Length ?? 0);
            var result = new string[n];
            for (int i = 0; i < n; i++)
            {
                string l = local != null && i < local.Length ? local[i] : null;
                string r = remote != null && i < remote.Length ? remote[i] : null;
                result[i] = !string.IsNullOrEmpty(l) ? l : (r ?? string.Empty);
            }
            return result;
        }

        private static string UnionCsv(string a, string b)
        {
            var set = new System.Collections.Generic.HashSet<string>();
            AddCsv(set, a);
            AddCsv(set, b);
            return string.Join(",", set);
        }

        private static void AddCsv(System.Collections.Generic.HashSet<string> set, string csv)
        {
            if (string.IsNullOrEmpty(csv)) return;
            foreach (string id in csv.Split(','))
            {
                if (!string.IsNullOrEmpty(id)) set.Add(id);
            }
        }

        /// <summary>Bitwise OR of two '0'/'1' masks, padded to the longer — a level claimed on either
        /// device stays claimed.</summary>
        private static string OrMask(string a, string b)
        {
            a ??= string.Empty;
            b ??= string.Empty;
            int n = Mathf.Max(a.Length, b.Length);
            var chars = new char[n];
            for (int i = 0; i < n; i++)
            {
                bool set = (i < a.Length && a[i] == '1') || (i < b.Length && b[i] == '1');
                chars[i] = set ? '1' : '0';
            }
            return new string(chars);
        }

        // ── Bridging to the stores ───────────────────────────────────────────────────────────────

        private static SaveDoc ExportLocal()
        {
            var d = new SaveDoc();
            Wallet.ExportTo(d);
            Inventory.ExportTo(d);
            PlayerProgress.ExportTo(d);
            LevelPath.ExportTo(d);
            MatchStats.ExportTo(d);
            RankedRewards.ExportTo(d);
            return d;
        }

        private static void ApplyLocal(SaveDoc d)
        {
            Wallet.ImportFrom(d);
            Inventory.ImportFrom(d);
            PlayerProgress.ImportFrom(d);
            LevelPath.ImportFrom(d);
            MatchStats.ImportFrom(d);
            RankedRewards.ImportFrom(d);
        }

        // ── Runner ─────────────────────────────────────────────────────────────────────────────

        private static CloudSyncRunner runner;

        /// <summary>Creates the lifecycle runner once, mirroring <see cref="GameServices.Ensure"/> —
        /// a build that never earns anything never spins it up.</summary>
        internal static CloudSyncRunner Ensure()
        {
            if (runner == null)
            {
                runner = UnityEngine.Object.FindAnyObjectByType<CloudSyncRunner>();
            }

            if (runner == null)
            {
                runner = new GameObject("CloudSync").AddComponent<CloudSyncRunner>();
            }

            return runner;
        }
    }
}
