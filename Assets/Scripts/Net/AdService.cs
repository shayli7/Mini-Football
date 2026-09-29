using System;

namespace TableFootball.Net
{
    /// <summary>
    /// Rewarded video ads. PLACEHOLDER: there is no ad SDK in the project yet, so every "ad" finishes
    /// at once and reports that it was watched to the end.
    ///
    /// Every ad call in the game goes through here and nowhere else, the same rule
    /// <see cref="CloudSaveBackend"/> follows for Cloud Save. When a real SDK (Unity LevelPlay, AdMob)
    /// lands, this file is the one that changes: <see cref="ShowRewarded"/> keeps its shape, and
    /// <see cref="ShopOffers"/> and the store never know the difference.
    /// </summary>
    public static class AdService
    {
        /// <summary>
        /// Shows a rewarded ad and calls <paramref name="onDone"/> with true only if the player watched
        /// it to the end. A skipped, failed or unavailable ad reports false, and the caller pays nothing.
        /// </summary>
        public static void ShowRewarded(Action<bool> onDone)
        {
            onDone?.Invoke(true);
        }
    }
}
