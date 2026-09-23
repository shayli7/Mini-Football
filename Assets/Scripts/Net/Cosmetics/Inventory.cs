using System;
using System.Collections.Generic;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// What the player owns and what they are wearing.
    ///
    /// Two facts, kept apart on purpose: OWNING an item is permanent and comes from a purchase, a
    /// chest or a league; EQUIPPING one is a choice that changes freely and only ever names something
    /// already owned. Collapsing them into a single "current skin" would lose the collection the
    /// moment the player switched back to the default.
    ///
    /// Persisted the same way as everything else in progression — PlayerPrefs, local, authoritative
    /// for this device, with an <see cref="OnChanged"/> event for screens to redraw off. The owned set
    /// is one comma-separated key rather than one key per item, so a wipe is a single delete and there
    /// are no orphan keys left behind when the catalogue changes.
    /// </summary>
    public static class Inventory
    {
        private const string OwnedKey = "TableFootball.Inventory.Owned";
        private const string EquipPrefix = "TableFootball.Inventory.Equip.";

        /// <summary>Raised when the owned set or an equipped slot changes.</summary>
        public static event Action OnChanged;

        private static HashSet<string> owned;

        // ---- owning ---------------------------------------------------------------------------

        /// <summary>
        /// Whether the player has this item. The catalogue defaults always answer true without being
        /// stored: they are owned by definition, and writing them into the save on first launch would
        /// make "what have you unlocked" indistinguishable from "what did you start with".
        /// </summary>
        public static bool Owns(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            CosmeticItem item = CosmeticCatalog.Get(id);
            if (item.Valid && item.IsDefault)
            {
                return true;
            }

            return Owned().Contains(id);
        }

        /// <summary>
        /// Grants an item and reports whether it was NEW. The bool is the whole point of the return:
        /// a chest that rolls something already owned has to pay out differently, and that decision
        /// cannot be made by a caller that only knows the grant "succeeded".
        /// </summary>
        public static bool Grant(string id)
        {
            if (string.IsNullOrEmpty(id) || Owns(id))
            {
                return false;
            }

            Owned().Add(id);
            Save();
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Every owned item of one kind, including the default, in catalogue order.</summary>
        public static List<CosmeticItem> OwnedOfKind(CosmeticKind kind)
        {
            var list = new List<CosmeticItem>();
            foreach (var item in CosmeticCatalog.All)
            {
                if (item.Kind == kind && Owns(item.Id)) list.Add(item);
            }
            return list;
        }

        /// <summary>How many of the catalogue's items are owned — the collection counter.</summary>
        public static int OwnedCount
        {
            get
            {
                int n = 0;
                foreach (var item in CosmeticCatalog.All)
                {
                    if (Owns(item.Id)) n++;
                }
                return n;
            }
        }

        // ---- equipping ------------------------------------------------------------------------

        /// <summary>
        /// The id equipped for a kind, falling back to the catalogue default. Never returns something
        /// the player does not own: an item can leave the catalogue between builds, and a stale
        /// equipped id has to resolve to the default rather than to nothing.
        /// </summary>
        public static string EquippedId(CosmeticKind kind)
        {
            string saved = PlayerPrefs.GetString(EquipPrefix + (int)kind, null);
            if (!string.IsNullOrEmpty(saved) && CosmeticCatalog.Get(saved).Valid && Owns(saved))
            {
                return saved;
            }

            return CosmeticCatalog.DefaultFor(kind).Id;
        }

        /// <summary>The equipped item for a kind, resolved through <see cref="EquippedId"/>.</summary>
        public static CosmeticItem Equipped(CosmeticKind kind) =>
            CosmeticCatalog.Get(EquippedId(kind));

        public static bool IsEquipped(string id) =>
            !string.IsNullOrEmpty(id) && CosmeticCatalog.Get(id).Valid &&
            EquippedId(CosmeticCatalog.Get(id).Kind) == id;

        /// <summary>
        /// Wears an owned item. Refuses silently for anything unowned — the store equips straight off
        /// a purchase, and an "equip" that quietly worked on an item the player has not bought would
        /// be a free skin.
        /// </summary>
        public static void Equip(string id)
        {
            CosmeticItem item = CosmeticCatalog.Get(id);
            if (!item.Valid || !Owns(id))
            {
                return;
            }

            PlayerPrefs.SetString(EquipPrefix + (int)item.Kind, id);
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
            OnChanged?.Invoke();
        }

        /// <summary>
        /// Takes off whatever is equipped for a kind, returning to the default. Only really meaningful
        /// for <see cref="CosmeticKind.Badge"/>, which has no default and so genuinely can be empty —
        /// a player who wants no badge beside their name needs a way to say so.
        /// </summary>
        public static void Unequip(CosmeticKind kind)
        {
            PlayerPrefs.DeleteKey(EquipPrefix + (int)kind);
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
            OnChanged?.Invoke();
        }

        // ---- lifecycle ------------------------------------------------------------------------

        /// <summary>
        /// Wipes the collection. Called when the player this device belongs to changes, alongside
        /// <see cref="PlayerProgress.ResetLocal"/> and <see cref="Wallet.ResetLocal"/> — a collection
        /// is a record of what a PERSON earned, and inheriting somebody else's skins would be wrong in
        /// both directions.
        /// </summary>
        public static void ResetLocal()
        {
            PlayerPrefs.DeleteKey(OwnedKey);
            // Every kind the enum declares, not a hand-written upper bound. This used to stop at
            // Badge, so adding a kind after it (Background) would have left that slot's equipped id
            // behind on a player change — the next person would inherit a room they never unlocked.
            foreach (int kind in System.Enum.GetValues(typeof(CosmeticKind)))
            {
                PlayerPrefs.DeleteKey(EquipPrefix + kind);
            }
            PlayerPrefs.Save();
            owned = null;
            OnChanged?.Invoke();
        }

        private static HashSet<string> Owned()
        {
            if (owned != null)
            {
                return owned;
            }

            owned = new HashSet<string>();
            string csv = PlayerPrefs.GetString(OwnedKey, string.Empty);
            if (!string.IsNullOrEmpty(csv))
            {
                foreach (string id in csv.Split(','))
                {
                    if (!string.IsNullOrEmpty(id)) owned.Add(id);
                }
            }

            return owned;
        }

        private static void Save()
        {
            PlayerPrefs.SetString(OwnedKey, string.Join(",", Owned()));
            PlayerPrefs.Save();
            CloudSync.MarkDirty();
        }

        // ── Cloud sync ─────────────────────────────────────────────────────────────────────────
        // Inventory owns the owned set and the per-kind equip keys; CloudSync only sees the document.

        /// <summary>Copies the owned set and every kind's equipped id into the save document.</summary>
        internal static void ExportTo(CloudSync.SaveDoc d)
        {
            d.owned = PlayerPrefs.GetString(OwnedKey, string.Empty);

            var kinds = System.Enum.GetValues(typeof(CosmeticKind));
            d.equipped = new string[kinds.Length];
            foreach (int kind in kinds)
            {
                d.equipped[kind] = PlayerPrefs.GetString(EquipPrefix + kind, string.Empty);
            }
        }

        /// <summary>
        /// Writes the reconciled owned set and equip choices back, then redraws. Straight to
        /// PlayerPrefs so it does not re-mark the store dirty, and invalidates the owned cache so the
        /// next <see cref="Owns"/> reads the merged set rather than the pre-load one.
        /// </summary>
        internal static void ImportFrom(CloudSync.SaveDoc d)
        {
            PlayerPrefs.SetString(OwnedKey, d.owned ?? string.Empty);

            var kinds = System.Enum.GetValues(typeof(CosmeticKind));
            foreach (int kind in kinds)
            {
                string id = d.equipped != null && kind < d.equipped.Length ? d.equipped[kind] : null;
                if (string.IsNullOrEmpty(id))
                {
                    PlayerPrefs.DeleteKey(EquipPrefix + kind);
                }
                else
                {
                    PlayerPrefs.SetString(EquipPrefix + kind, id);
                }
            }

            PlayerPrefs.Save();
            owned = null;
            OnChanged?.Invoke();
        }
    }
}
