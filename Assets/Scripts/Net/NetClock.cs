using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The one clock both machines agree on during an online match: the HOST's unscaled time.
    ///
    /// The host reads it directly. The guest estimates the offset between its own clock and the host's
    /// from ping/pong pairs sent by <see cref="OnlineMatchDirector"/>, NTP-style: a pong reports the
    /// host's time when the ping arrived, and half the measured round trip is assumed spent on the way
    /// back. Of the recent pairs it trusts the one with the SHORTEST round trip, because the delay a
    /// packet can pick up is one-sided — a slow round trip is mostly queueing on one leg, and the
    /// half-and-half assumption is least wrong when there was the least queueing to split.
    ///
    /// The estimate is slewed rather than jumped, so a better sample arriving never makes the ball
    /// lurch; only an error too large to be jitter (a resumed app, a clock hiccup) snaps it.
    ///
    /// Everything uses unscaled time: the pause menu and the win banner stop <c>timeScale</c>, and the
    /// shared clock has to keep running through both.
    /// </summary>
    public static class NetClock
    {
        /// <summary>How long a round-trip measurement stays eligible to be "the best recent one".</summary>
        private const double WindowSeconds = 3.0;

        /// <summary>Largest correction applied per second of real time — the clock never visibly jumps.</summary>
        private const double SlewPerSecond = 0.005;

        /// <summary>An error this large is not jitter; the estimate snaps instead of slewing.</summary>
        private const double SnapError = 0.1;

        private static bool isReference;
        private static bool synced;
        private static double offset;        // shared = local + offset
        private static double targetOffset;
        private static double lastSlew;

        private static double bestRtt = double.MaxValue;
        private static double bestAt;

        /// <summary>Whether this machine can map its clock to the shared one yet. Always true on the host.</summary>
        public static bool Synced => isReference || synced;

        /// <summary>Counts every time the estimate snapped rather than slewed (the first sync included).
        /// Anything that measured intervals against the old estimate must start measuring again.</summary>
        public static int SnapCount { get; private set; }

        /// <summary>The latest round trip measured, in seconds — for the HUD's ping readout and for the
        /// projection horizon. Zero on the host and before the first pong.</summary>
        public static double Rtt { get; private set; }

        /// <summary>Now, on the shared clock.</summary>
        public static double SharedNow => ToShared(Time.unscaledTimeAsDouble);

        /// <summary>This physics step's time on the shared clock. Samples are stamped with it, so steps
        /// that run in a burst at the start of one frame keep their true spacing.</summary>
        public static double SharedFixedNow => ToShared(Time.fixedUnscaledTimeAsDouble);

        public static double ToShared(double local)
        {
            Slew();
            return local + offset;
        }

        /// <summary>Called on the host: its own clock IS the shared clock.</summary>
        public static void BecomeReference()
        {
            Reset();
            isReference = true;
        }

        /// <summary>Called on the guest, and on the way out of a session.</summary>
        public static void Reset()
        {
            isReference = false;
            synced = false;
            offset = targetOffset = 0;
            bestRtt = double.MaxValue;
            bestAt = 0;
            lastSlew = Time.unscaledTimeAsDouble;
            Rtt = 0;
        }

        /// <summary>
        /// A pong arrived. <paramref name="guestSent"/> is this machine's clock when the ping left,
        /// echoed back; <paramref name="hostTime"/> is the host's clock when the ping arrived there.
        /// </summary>
        public static void OnPong(double guestSent, double hostTime)
        {
            if (isReference)
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            double rtt = now - guestSent;
            if (rtt < 0 || rtt > 5.0)
            {
                return; // a stale or nonsensical echo
            }

            Rtt = rtt;

            // A better (faster) measurement replaces the best one; an old best one is allowed to age out,
            // so a route that has genuinely become slower is eventually believed.
            if (rtt <= bestRtt || now - bestAt > WindowSeconds)
            {
                bestRtt = rtt;
                bestAt = now;
                targetOffset = hostTime + rtt * 0.5 - now;

                if (!synced || System.Math.Abs(targetOffset - offset) > SnapError)
                {
                    offset = targetOffset;
                    synced = true;
                    SnapCount++;
                }
            }
        }

        private static void Slew()
        {
            double now = Time.unscaledTimeAsDouble;
            double dt = now - lastSlew;
            lastSlew = now;

            if (isReference || !synced || dt <= 0)
            {
                return;
            }

            double step = SlewPerSecond * dt;
            double error = targetOffset - offset;
            offset += System.Math.Clamp(error, -step, step);
        }
    }
}
