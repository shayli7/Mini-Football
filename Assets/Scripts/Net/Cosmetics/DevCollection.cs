#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Threading.Tasks;

namespace TableFootball.Net
{
    /// <summary>
    /// DEVELOPMENT ONLY — unlock or clear the whole collection, for checking skins and the store.
    ///
    /// Compiled only into the Editor and Development Builds (the whole file is inside
    /// <c>#if UNITY_EDITOR || DEVELOPMENT_BUILD</c>), so a release build carries none of it and no
    /// player can reach it.
    ///
    /// Both actions put the device into <see cref="CloudSync.DevSandbox"/> first, and that is the part
    /// that matters. The collection is synced with a MONOTONIC merge — owned skins union, nothing is
    /// ever taken away — and the server caps how many new cosmetics one save may add. Left alone,
    /// "remove all" would be undone by the next sync pulling the account's skins back in, and "add
    /// all" would be clamped by the server and could write a fake full collection into a real
    /// account. In the sandbox progression sync is paused entirely, so what you set here is exactly
    /// what you see, and nothing leaves the device.
    ///
    /// <see cref="RestoreRealAsync"/> is the way back: it drops the sandbox, clears the local
    /// collection and reloads the account's own from the cloud. Coins, XP and the rest are merged
    /// back by the same max rule as always, so nothing earned during testing is lost either.
    /// </summary>
    public static class DevCollection
    {
        /// <summary>Owns every cosmetic in the catalogue, badges included. Defaults are owned by
        /// definition and are skipped rather than written into the save.</summary>
        public static int GrantAll()
        {
            CloudSync.DevSandbox = true;

            int granted = 0;
            foreach (CosmeticItem item in CosmeticCatalog.All)
            {
                if (item.IsDefault) continue;
                if (Inventory.Grant(item.Id)) granted++;
            }
            return granted;
        }

        /// <summary>Owns nothing but the defaults, and wears the defaults. Uses the same wipe as a
        /// player change, which also clears every equipped slot — so nothing is left wearing a skin
        /// that is no longer owned.</summary>
        public static void RemoveAll()
        {
            CloudSync.DevSandbox = true;
            Inventory.ResetLocal();
        }

        /// <summary>
        /// Leaves the sandbox and brings back the account's real collection. Returns false when the
        /// cloud could not be reached — the sandbox is still left and the collection still cleared,
        /// and the real one arrives on the next successful sign-in, since boot loads it anyway.
        /// </summary>
        public static async Task<bool> RestoreRealAsync()
        {
            CloudSync.DevSandbox = false;
            Inventory.ResetLocal();
            await CloudSync.LoadAsync();
            return CloudSync.LastLoadSucceeded;
        }
    }
}
#endif
