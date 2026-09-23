using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// A short device vibration on ball contact, for phones — the tactile half of an impact, next to
    /// the sound GameSfx plays.
    ///
    /// Deliberately NOT Handheld.Vibrate: that is one blunt ~500 ms buzz with no strength, far too
    /// heavy to fire on every touch of the ball. Instead this drives the Android Vibrator directly
    /// for a few-millisecond tick whose length and (on API 26+) amplitude scale with how hard the
    /// hit was, so a firm strike thumps and a glancing touch barely ticks.
    ///
    /// Everything is guarded and wrapped: off Android (and in the Editor) it is a silent no-op, and a
    /// device with no vibrator, or with the VIBRATE permission missing, simply does nothing rather
    /// than throwing. Requires <uses-permission android:name="android.permission.VIBRATE"/> in the
    /// Android manifest to actually buzz.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Master switch, so a settings toggle can turn rumble off.</summary>
        public static bool Enabled = true;

        [Tooltip("Hits softer than this (0..1) do not buzz, so the ball settling against a figure " +
                 "does not chatter the phone.")]
        private const float QuietFloor = 0.06f;

        private const long MinMs = 8;   // a barely-there tick for the lightest hit that still counts
        private const long MaxMs = 34;  // a firm thump at full strength — still short enough to spam
        private const int MinAmplitude = 70;   // 1..255, API 26+
        private const int MaxAmplitude = 255;

        private static bool probed;
        private static bool available;
        private static int sdkInt;
        private static AndroidJavaObject vibrator;

        /// <summary>Fire a one-shot vibration scaled by impact strength 0..1.</summary>
        public static void Play(float strength01)
        {
            if (!Enabled) return;
            if (Application.platform != RuntimePlatform.Android) return;

            float s = Mathf.Clamp01(strength01);
            if (s < QuietFloor) return;

            if (!probed) Probe();
            if (!available) return;

            long ms = (long)Mathf.Lerp(MinMs, MaxMs, s);

            try
            {
                if (sdkInt >= 26)
                {
                    int amplitude = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(MinAmplitude, MaxAmplitude, s)), 1, 255);
                    using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                    {
                        AndroidJavaObject effect =
                            effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude);
                        vibrator.Call("vibrate", effect);
                    }
                }
                else
                {
                    vibrator.Call("vibrate", ms);
                }
            }
            catch (System.Exception)
            {
                // A flaky device or a revoked permission should never take the game down — just stop
                // trying rather than throwing on every hit.
                available = false;
            }
        }

        /// <summary>Resolves the platform Vibrator once, on the first real hit.</summary>
        private static void Probe()
        {
            probed = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    sdkInt = version.GetStatic<int>("SDK_INT");
                }

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }

                available = vibrator != null && vibrator.Call<bool>("hasVibrator");
            }
            catch (System.Exception)
            {
                available = false;
            }
        }
    }
}
