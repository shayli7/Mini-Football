using System;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Whether the phone can reach the internet at all — the light behind the "OFFLINE" chip and the
    /// locked Online card.
    ///
    /// This is the device's own idea of connectivity (<see cref="Application.internetReachability"/>),
    /// not a probe of Unity's services: it says "no signal" instantly and for free, but it cannot say
    /// the network is actually delivering. <see cref="GameServices"/> still reports its own failures, so
    /// a phone that is connected to a dead router looks online here and fails there, and neither is
    /// asked to know about the other. Like them it never throws.
    ///
    /// Nothing polls on its own: whichever screen shows the state calls <see cref="Poll"/> each frame
    /// it is visible, and that is rate-limited to once a second here.
    /// </summary>
    public static class Connectivity
    {
        private const float PollSeconds = 1f;

        private static bool online = true;
        private static bool probed;
        private static float lastPoll = float.NegativeInfinity;

        /// <summary>True when the device reports any route to the internet. Reads the device once on
        /// first use, so it is right before anything has polled.</summary>
        public static bool IsOnline
        {
            get
            {
                if (!probed) Probe();
                return online;
            }
        }

        /// <summary>Raised when <see cref="IsOnline"/> flips, either way.</summary>
        public static event Action OnChanged;

        /// <summary>Re-reads the device, at most once a second (unscaled — menus run at timeScale 0).</summary>
        public static void Poll()
        {
            if (Time.unscaledTime - lastPoll < PollSeconds) return;
            lastPoll = Time.unscaledTime;
            Probe();
        }

        private static void Probe()
        {
            bool now = Application.internetReachability != NetworkReachability.NotReachable;
            bool first = !probed;
            probed = true;

            if (now == online && !first) return;

            bool changed = now != online;
            online = now;
            if (changed) OnChanged?.Invoke();
        }
    }
}
