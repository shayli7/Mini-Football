using System;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Turns a stream of <see cref="BallSample"/>s into smooth ball motion on the machine that is NOT
    /// simulating the ball.
    ///
    /// Why the ball used to stutter: samples leave the simulating machine every 10 ms but arrive
    /// unevenly over Relay — two at once, then nothing for 25 ms. Drawing "whatever arrived last"
    /// turns that unevenness straight into jerky motion. This class instead draws the ball where it
    /// was at a chosen moment on the shared clock, slightly behind the newest sample, so there is
    /// almost always one sample either side of the moment being drawn and a late packet is still in
    /// time to be used. The same trick video streaming uses, measured in milliseconds rather than
    /// seconds.
    ///
    /// How far behind is adaptive (<see cref="RenderDelay"/>). Every arrival records how late it was;
    /// the delay tracks the 95th percentile of that lateness plus one sample interval, bounded to a
    /// small buffer above the fastest path. A steady connection settles near the floor; a jittery one
    /// widens it — smoothly, never in a jump.
    ///
    /// Between two samples the path is a cubic Hermite curve through both positions AND both
    /// velocities, which is what keeps a bounce between samples sharp instead of cutting the corner.
    /// Past the newest sample (a starved buffer, or projection to the present later on) the ball is
    /// swept forward along its velocity: it bounces off walls and STOPS at any rod, because guessing a
    /// figure's reaction is exactly the guess that looks worst when it is wrong.
    ///
    /// When a new sample reveals that what was drawn a moment ago was off, the difference is not
    /// snapped away: it becomes an error offset that decays over <see cref="errorDecay"/>. Only a
    /// difference too large to smooth believably is snapped.
    ///
    /// Plain class, no networking: <see cref="NetworkedBall"/> feeds it and draws from it.
    /// </summary>
    public sealed class BallStream
    {
        private const int Capacity = 32;
        private const int LatenessCapacity = 200;

        /// <summary>The buffer above the fastest path is kept between these, in seconds.</summary>
        private const double MinBuffer = 0.015;
        private const double MaxBuffer = 0.06;

        /// <summary>How fast the delay may grow or shrink, in seconds per second. Growing is quicker,
        /// because a starving buffer is worse than a slightly late one.</summary>
        private const double GrowRate = 0.1;
        private const double ShrinkRate = 0.02;

        private const double RecalcInterval = 0.25;

        /// <summary>Ceiling for the starvation nudge, so a dead stream cannot push the delay off forever.</summary>
        private const double MaxStarvedDelay = 0.5;

        private readonly BallSample[] samples = new BallSample[Capacity];
        private int start;
        private int count;

        private readonly float[] lateness = new float[LatenessCapacity];
        private readonly float[] sortScratch = new float[LatenessCapacity];
        private int latenessNext;
        private int latenessCount;

        private double renderDelay = -1;
        private double targetDelay;
        private double nextRecalc;
        private double lastDelayUpdate;

        private readonly RaycastHit[] castHits = new RaycastHit[8];

        private readonly Rigidbody ignoreBody;
        private readonly float radius;
        private readonly float wallRetention;
        private readonly float maxExtrapolation;
        private readonly float errorSnapDistance;
        private readonly float errorDecay;

        private Vector3 errorOffset;
        private bool hasLast;
        private double lastRenderTime;
        private Vector3 lastRawPos;

        // ── diagnostics (read and cleared by NetworkedBall's temporary log) ──
        public int Received;
        public int LateDrops;
        public int StarvedFrames;
        public int Frames;
        public double DepthSum;

        public BallStream(Rigidbody ignoreBody, float radius, float wallRetention, float maxExtrapolation,
                          float errorSnapDistance, float errorDecay)
        {
            this.ignoreBody = ignoreBody;
            this.radius = radius;
            this.wallRetention = wallRetention;
            this.maxExtrapolation = maxExtrapolation;
            this.errorSnapDistance = errorSnapDistance;
            this.errorDecay = errorDecay;
        }

        public bool HasSamples => count > 0;

        /// <summary>How far behind the shared clock the ball is being drawn, in seconds.</summary>
        public double RenderDelay => renderDelay < 0 ? 0.1 : renderDelay;

        /// <summary>The velocity at the moment last drawn.</summary>
        public Vector3 LastVelocity { get; private set; }

        public BallSample Newest => samples[(start + count - 1) % Capacity];

        public void Reset()
        {
            start = count = 0;
            latenessNext = latenessCount = 0;
            renderDelay = -1;
            nextRecalc = 0;
            errorOffset = Vector3.zero;
            hasLast = false;
            LastVelocity = Vector3.zero;
        }

        /// <summary>
        /// Forgets the measured lateness (not the samples). For when the clock the lateness was
        /// measured against has just been re-estimated: old and new measurements would not agree.
        /// </summary>
        public void ResetTiming()
        {
            latenessNext = latenessCount = 0;
            renderDelay = -1;
            nextRecalc = 0;
        }

        /// <summary>
        /// Adds a sample. Returns false if it was dropped as out of date — unreliable delivery can
        /// reorder, and a sample older than one already held has nothing left to say.
        /// </summary>
        public bool Add(in BallSample sample, double arrivalShared)
        {
            if (count > 0 && sample.Time <= Newest.Time)
            {
                LateDrops++;
                return false;
            }

            Received++;

            lateness[latenessNext] = (float)(arrivalShared - sample.Time);
            latenessNext = (latenessNext + 1) % LatenessCapacity;
            latenessCount = Math.Min(latenessCount + 1, LatenessCapacity);

            // Were we drawing past the newest sample (a guess) a moment ago? Then this arrival may
            // move where that guess should have been — turn the difference into a decaying offset.
            bool wasProjecting = hasLast && count > 0 && lastRenderTime > Newest.Time;

            if (count == Capacity)
            {
                start = (start + 1) % Capacity;
                count--;
            }

            samples[(start + count) % Capacity] = sample;
            count++;

            if (sample.Has(BallSampleFlags.Teleport))
            {
                errorOffset = Vector3.zero;
                hasLast = false;
            }
            else if (wasProjecting)
            {
                EvaluateRaw(lastRenderTime, out Vector3 corrected, out _, out _, out _);
                Vector3 delta = lastRawPos - corrected;
                errorOffset = delta.magnitude <= errorSnapDistance ? errorOffset + delta : Vector3.zero;
            }

            return true;
        }

        /// <summary>
        /// Steps the adaptive delay toward its target. Call once per frame before drawing.
        /// </summary>
        public void UpdateDelay(double now)
        {
            double dt = lastDelayUpdate > 0 ? Math.Max(0, now - lastDelayUpdate) : 0;
            lastDelayUpdate = now;

            if (latenessCount >= 8 && now >= nextRecalc)
            {
                nextRecalc = now + RecalcInterval;

                Array.Copy(lateness, sortScratch, latenessCount);
                Array.Sort(sortScratch, 0, latenessCount);
                double fastest = sortScratch[0];
                double p95 = sortScratch[(int)(0.95f * (latenessCount - 1))];

                double interval = count > 1
                    ? (Newest.Time - samples[start].Time) / (count - 1)
                    : 0.01;

                targetDelay = Math.Clamp(p95 + interval, fastest + MinBuffer, fastest + MaxBuffer);

                if (renderDelay < 0)
                {
                    renderDelay = targetDelay; // first estimate: take it outright
                }
            }

            if (renderDelay < 0)
            {
                return;
            }

            double error = targetDelay - renderDelay;
            double limit = (error > 0 ? GrowRate : ShrinkRate) * dt;
            renderDelay += Math.Clamp(error, -limit, limit);
        }

        /// <summary>
        /// The ball at <paramref name="renderTime"/> on the shared clock, including the decaying
        /// correction offset. False if there is nothing to draw yet.
        /// </summary>
        public bool Sample(double renderTime, float dt, out Vector3 pos, out Vector3 vel, out Quaternion rot)
        {
            if (!EvaluateRaw(renderTime, out pos, out vel, out rot, out bool starved))
            {
                return false;
            }

            Frames++;
            DepthSum += Newest.Time - renderTime;
            if (starved)
            {
                StarvedFrames++;

                // Out of samples: this delay is too short for this connection right now. Nudge it
                // up at once rather than waiting for the next recalculation to notice.
                if (renderDelay >= 0)
                {
                    renderDelay = Math.Min(renderDelay + 0.002, MaxStarvedDelay);
                }
            }

            lastRenderTime = renderTime;
            lastRawPos = pos;
            hasLast = true;

            if (errorDecay > 0f && dt > 0f)
            {
                errorOffset *= Mathf.Exp(-dt / errorDecay);
            }

            pos += errorOffset;
            LastVelocity = vel;
            return true;
        }

        private bool EvaluateRaw(double t, out Vector3 pos, out Vector3 vel, out Quaternion rot, out bool starved)
        {
            starved = false;
            pos = vel = Vector3.zero;
            rot = Quaternion.identity;

            if (count == 0)
            {
                return false;
            }

            BallSample oldest = samples[start];
            BallSample newest = Newest;

            if (t <= oldest.Time)
            {
                pos = oldest.Pos;
                vel = oldest.Vel;
                rot = oldest.Rot;
                return true;
            }

            if (t >= newest.Time)
            {
                starved = t > newest.Time + 0.002;
                float ahead = Mathf.Min((float)(t - newest.Time), maxExtrapolation);
                Project(newest, ahead, out pos, out vel);
                rot = newest.Rot;
                return true;
            }

            // Newest first: the moment being drawn is almost always near the front.
            for (int i = count - 2; i >= 0; i--)
            {
                BallSample a = samples[(start + i) % Capacity];
                if (a.Time > t)
                {
                    continue;
                }

                BallSample b = samples[(start + i + 1) % Capacity];

                if (b.Has(BallSampleFlags.Teleport))
                {
                    // A placed ball did not travel from a to b; there is no curve to draw.
                    pos = b.Pos;
                    vel = b.Vel;
                    rot = b.Rot;
                    return true;
                }

                Hermite(a, b, t, out pos, out vel);
                rot = Quaternion.Slerp(a.Rot, b.Rot, (float)((t - a.Time) / (b.Time - a.Time)));
                return true;
            }

            pos = oldest.Pos;
            vel = oldest.Vel;
            rot = oldest.Rot;
            return true;
        }

        /// <summary>Cubic Hermite through both samples' positions, with their velocities as tangents.</summary>
        private static void Hermite(in BallSample a, in BallSample b, double t, out Vector3 pos, out Vector3 vel)
        {
            float h = (float)(b.Time - a.Time);
            float u = Mathf.Clamp01((float)((t - a.Time) / h));
            float u2 = u * u;
            float u3 = u2 * u;

            float h00 = 2f * u3 - 3f * u2 + 1f;
            float h10 = u3 - 2f * u2 + u;
            float h01 = -2f * u3 + 3f * u2;
            float h11 = u3 - u2;
            pos = h00 * a.Pos + h10 * h * a.Vel + h01 * b.Pos + h11 * h * b.Vel;

            float d00 = 6f * u2 - 6f * u;
            float d10 = 3f * u2 - 4f * u + 1f;
            float d01 = -6f * u2 + 6f * u;
            float d11 = 3f * u2 - 2f * u;
            vel = (d00 * a.Pos + d01 * b.Pos) / h + d10 * a.Vel + d11 * b.Vel;
        }

        /// <summary>
        /// Carries a sample forward in time along its velocity, flat on the pitch. Bounces off walls
        /// (keeping BallController's wall retention) and stops dead at any rod — a figure's reaction is
        /// the one thing that cannot be guessed, and drawing the ball through a foot is the guess that
        /// looks worst when it is wrong.
        /// </summary>
        private void Project(in BallSample from, float seconds, out Vector3 pos, out Vector3 vel)
        {
            pos = from.Pos;
            vel = new Vector3(from.Vel.x, 0f, from.Vel.z);

            float remaining = seconds;
            for (int bounce = 0; bounce < 3 && remaining > 0f; bounce++)
            {
                float speed = vel.magnitude;
                if (speed < 1e-4f)
                {
                    break;
                }

                Vector3 dir = vel / speed;
                float distance = speed * remaining;

                // Slightly under the true radius so the cast starts clear of the pitch it rests on.
                int hitCount = Physics.SphereCastNonAlloc(pos, radius * 0.95f, dir, castHits, distance,
                                                         ~0, QueryTriggerInteraction.Ignore);

                int nearest = -1;
                for (int i = 0; i < hitCount; i++)
                {
                    Collider c = castHits[i].collider;
                    if (c == null || castHits[i].distance <= 0f || (ignoreBody != null && c.attachedRigidbody == ignoreBody))
                    {
                        continue;
                    }

                    if (nearest < 0 || castHits[i].distance < castHits[nearest].distance)
                    {
                        nearest = i;
                    }
                }

                if (nearest < 0)
                {
                    pos += dir * distance;
                    break;
                }

                RaycastHit hit = castHits[nearest];
                pos += dir * hit.distance;

                if (hit.collider.GetComponentInParent<RodController>() != null)
                {
                    vel = Vector3.zero;
                    break;
                }

                Vector3 normal = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (normal.sqrMagnitude < 1e-6f)
                {
                    break;
                }

                remaining -= hit.distance / speed;
                vel = Vector3.Reflect(vel, normal.normalized) * wallRetention;
            }
        }
    }
}
