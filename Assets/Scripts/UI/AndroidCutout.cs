using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// Asks Android to let the game draw into the display cutout (the camera notch) in landscape.
    ///
    /// The player setting <c>androidRenderOutsideSafeArea</c> is meant to do this, but on some phones the
    /// activity still comes up in the system's default cutout mode, which in landscape letterboxes the
    /// window: the notch's strip stays outside the game and the system fills it with a blurred copy of the
    /// wallpaper. Setting the window's cutout mode here guarantees the game covers the whole display; the
    /// UI then keeps clear of the notch through <see cref="SafeArea.Effective"/>, the same as everywhere.
    ///
    /// Android only, and it never throws — a phone that refuses simply keeps the letterbox.
    /// </summary>
    public static class AndroidCutout
    {
        /// <summary>Android's LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES (API 28) and _ALWAYS (API 30).</summary>
        private const int ShortEdges = 1, Always = 3;

        public static void Extend()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    int sdk = version.GetStatic<int>("SDK_INT");
                    if (sdk < 28 || activity == null) return;

                    int mode = sdk >= 30 ? Always : ShortEdges;

                    // Window attributes can only be changed on the UI thread.
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        try
                        {
                            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                            using (var attributes = window.Call<AndroidJavaObject>("getAttributes"))
                            {
                                attributes.Set("layoutInDisplayCutoutMode", mode);
                                window.Call("setAttributes", attributes);
                            }
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogWarning("AndroidCutout: could not set the cutout mode — " + e.Message);
                        }
                    }));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("AndroidCutout: could not set the cutout mode — " + e.Message);
            }
#endif
        }
    }
}
