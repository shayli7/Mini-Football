using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Pins the frame rate at startup.
    ///
    /// Nothing in this project ever set <see cref="Application.targetFrameRate"/>, and
    /// <c>vSyncCount</c> is 0 in both quality levels — so on Android the game ran at Unity's mobile
    /// default of 30 fps. That is a 33 ms frame period, which is a longer input-to-photon delay than
    /// every other source in the game COMBINED: the rod's physics-step quantization is ~5 ms and its
    /// Rigidbody interpolation ~10 ms. Chasing milliseconds anywhere else while the frame rate is
    /// unpinned is measuring the wrong thing.
    ///
    /// It matters twice over online. Frame period is input-to-photon on both machines, so it is added
    /// to the guest's swing-to-reaction delay at both ends; and the ball's interpolation used to run
    /// on a frame-rate-dependent filter, which meant two players on different hardware did not even
    /// experience the same lag.
    ///
    /// Deliberately a <see cref="RuntimeInitializeOnLoadMethodAttribute"/> rather than a component.
    /// A MonoBehaviour has to be dropped on a GameObject to do anything, and this project already has
    /// one cautionary tale: <c>RodAutoLift</c> is fully written, documented and referenced from
    /// <c>BallController</c>'s comments, and has never been attached to anything — so the assist it
    /// provides has never run for any player in any mode. A setting that must not be forgotten should
    /// not be possible to forget.
    /// </summary>
    public static class DisplaySettings
    {
        /// <summary>
        /// Never aim below this. 60 fps halves the 30 fps default's frame period, and every device
        /// this game targets can hold it for a table with one dynamic body on it.
        /// </summary>
        private const int MinFrameRate = 60;

        /// <summary>
        /// Never aim above this, however fast the panel is. Past 120 fps the latency saved is under
        /// 2 ms a frame, which is not worth the heat on a phone — and a thermally throttled device
        /// delivers a WORSE and less even frame time than one that was never pushed.
        /// </summary>
        private const int MaxFrameRate = 120;

        /// <summary>Used when the panel does not report a refresh rate.</summary>
        private const int FallbackFrameRate = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            // targetFrameRate is only consulted when vSync is off. It is already 0 in both quality
            // levels, but a quality-level switch at runtime could bring it back and silently undo
            // everything below, so it is asserted here rather than assumed.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = PreferredFrameRate();

            Debug.Log($"Display: targeting {Application.targetFrameRate} fps " +
                      $"({1000f / Mathf.Max(1, Application.targetFrameRate):0.0} ms per frame).");
        }

        /// <summary>
        /// The panel's own refresh rate, clamped. Asking for more than the display can show buys
        /// nothing but heat, and asking for less leaves latency on the table.
        /// </summary>
        private static int PreferredFrameRate()
        {
            double refresh = Screen.currentResolution.refreshRateRatio.value;
            if (refresh < 1d || double.IsNaN(refresh))
            {
                return FallbackFrameRate;
            }

            return Mathf.Clamp(Mathf.RoundToInt((float)refresh), MinFrameRate, MaxFrameRate);
        }
    }
}
