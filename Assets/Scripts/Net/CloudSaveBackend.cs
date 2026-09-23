using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudSave;

namespace TableFootball.Net
{
    /// <summary>
    /// The one place that talks to the Unity Cloud Save SDK — the whole reason
    /// <see cref="CloudSync"/> can stay backend-agnostic.
    ///
    /// Kept to two calls on purpose. Every decision about WHAT to store, WHEN, and how to reconcile
    /// two devices lives in <see cref="CloudSync"/>, which this file knows nothing about; this file
    /// knows only how to put one string under one key and get it back. Swapping Cloud Save for another
    /// backend — or stubbing it in a test — is this file and nothing else.
    ///
    /// It is also the only file in the project that depends on <c>com.unity.services.cloudsave</c>.
    /// Until that package is added in the Unity Package Manager, this one file will not compile while
    /// the rest of the cloud-sync code does; that is the intended seam, not a mistake.
    ///
    /// Player-scoped data: Cloud Save keys everything to the signed-in player automatically, so the
    /// same code serves the anonymous id and a linked username account with no key juggling.
    /// </summary>
    internal static class CloudSaveBackend
    {
        /// <summary>Reads one key's value as a string, or null when the player has no such key yet
        /// (a first-ever save). Throws only on a genuine service/network failure, which
        /// <see cref="CloudSync"/> catches.</summary>
        internal static async Task<string> LoadAsync(string key)
        {
            var result = await CloudSaveService.Instance.Data.Player.LoadAsync(
                new HashSet<string> { key });

            return result.TryGetValue(key, out var item) ? item.Value.GetAs<string>() : null;
        }

        /// <summary>Writes one key's value. The whole save document is a single JSON string under a
        /// single key, so this is always exactly one write.</summary>
        internal static async Task SaveAsync(string key, string json)
        {
            await CloudSaveService.Instance.Data.Player.SaveAsync(
                new Dictionary<string, object> { { key, json } });
        }
    }
}
