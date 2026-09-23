using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The one MonoBehaviour <see cref="CloudSync"/> needs: it turns app lifecycle and the passage of
    /// time — neither of which a static class can see — into flushes.
    ///
    /// Kept deliberately thin. All the policy (what is dirty, how long to wait, what to merge) lives
    /// in <see cref="CloudSync"/>; this only asks "should we flush this frame?" and, on the way out,
    /// "flush now, there may be no more frames."
    ///
    /// <see cref="OnApplicationPause"/> is the reliable save point on mobile — Android and iOS raise
    /// it before backgrounding, where <see cref="OnApplicationQuit"/> is not guaranteed to run to
    /// completion. Both are wired so a desktop quit is covered too.
    /// </summary>
    [DisallowMultipleComponent]
    public class CloudSyncRunner : MonoBehaviour
    {
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (CloudSync.ShouldFlush())
            {
                _ = CloudSync.FlushAsync();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                CloudSync.FlushNow();
            }
        }

        private void OnApplicationQuit()
        {
            CloudSync.FlushNow();
        }
    }
}
