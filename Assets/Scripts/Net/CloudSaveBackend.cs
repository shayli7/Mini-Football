using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TableFootball.Net
{
    /// <summary>
    /// The one place that talks to the Unity Cloud Code SDK — the whole reason
    /// <see cref="CloudSync"/> can stay backend-agnostic.
    ///
    /// Kept to two calls on purpose, exactly as before. What changed is WHERE the document lives.
    /// This used to write straight to Unity Cloud Save's player data
    /// (<c>CloudSaveService.Instance.Data.Player</c>), which is client-writable by design — any
    /// signed-in player can put anything under their own key. <see cref="CloudSync"/>'s merge takes
    /// the LARGER value for every counter across devices ("never lose progress to a stale device"),
    /// so a document written that way, once, with an inflated <c>coins</c> or an <c>owned</c> list
    /// holding every cosmetic in the game, would stick forever and sync to every device.
    ///
    /// Both calls now go through the <c>progress</c> Cloud Code script (see
    /// <c>cloudcode/progress.js</c>), which stores the document as game-scoped CUSTOM data instead —
    /// the client SDK has no write access to that at all, only Cloud Code does — and bounds the two
    /// fields that are actually worth forging (coins, and how many cosmetics newly appear) before
    /// persisting. <see cref="SaveAsync"/> returns the server's own resulting document rather than
    /// void, so <see cref="CloudSync"/> can apply whatever the server actually accepted back over an
    /// optimistic local merge — a tampered client is corrected on its next flush, not trusted on it.
    ///
    /// It is also the only file in the project that depends on <c>com.unity.services.cloudcode</c>
    /// for progression sync specifically (the ladder already required it, gated behind
    /// <c>LADDER_UGS</c>; this file needs it unconditionally, matching how it previously needed
    /// <c>com.unity.services.cloudsave</c> unconditionally). Both packages are already in
    /// <c>Packages/manifest.json</c>.
    ///
    /// VERIFY ON DEPLOY: written to the documented Cloud Code call shape, exactly like every other
    /// Net/ wrapper built by reading the installed package first — see <c>cloudcode/README.md</c>.
    /// </summary>
    internal static class CloudSaveBackend
    {
        [Serializable]
        private class DocDto
        {
            public string doc;
        }

        /// <summary>Reads the stored document as a JSON string, or null when the player has none yet
        /// (a first-ever save). Throws only on a genuine service/network failure, which
        /// <see cref="CloudSync"/> catches.</summary>
        internal static async Task<string> LoadAsync(string key)
        {
            var result = await Unity.Services.CloudCode.CloudCodeService.Instance
                .CallEndpointAsync<DocDto>("progress",
                    new Dictionary<string, object> { { "action", "load" } });

            return result?.doc;
        }

        /// <summary>
        /// Sends this device's document to be merged, bound and persisted server-side, and returns
        /// what the server actually stored — which may differ from what was sent, if a bound was hit.
        /// The key argument is accepted for symmetry with <see cref="LoadAsync"/> and because the
        /// document is still one JSON string end to end; the server keys it by the signed-in player,
        /// the same as the Cloud Save player data this replaced did.
        /// </summary>
        internal static async Task<string> SaveAsync(string key, string json)
        {
            var result = await Unity.Services.CloudCode.CloudCodeService.Instance
                .CallEndpointAsync<DocDto>("progress",
                    new Dictionary<string, object> { { "action", "save" }, { "docJson", json } });

            return result?.doc;
        }
    }
}
