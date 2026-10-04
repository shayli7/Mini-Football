using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The host's sanity checks on the ball while the GUEST simulates it.
    ///
    /// Lane authority hands the guest the real physics of the ball whenever it is in a Blue lane, and
    /// the host draws whatever the guest sends — which makes the guest's samples untrusted input, the
    /// same shape as the guest's reported spin speed (see AGENTS.md). These checks bound what a modified
    /// guest can claim: the ball cannot outrun the speed cap, leave the table, or stay simulated by the
    /// guest after it has gone deep into a Red lane. Two violations within a second and the host takes
    /// the ball back.
    ///
    /// What they do NOT prove is that a plausible-looking trap or save inside a Blue lane really
    /// happened. Doing that needs a server that re-simulates the guest's figures, which this
    /// relay-hosted design does not have — an accepted residual, not a bug to patch here.
    /// </summary>
    public sealed class AuthorityValidator
    {
        private const float SpeedTolerance = 0.5f;
        private const double FutureTolerance = 0.05;
        private const double PastTolerance = 1.0;
        private const float ViolationWindow = 1f;

        private readonly float maxSpeed;
        private readonly Bounds table;
        private readonly BallLanes lanes;
        private readonly float hysteresis;
        private readonly float maxOverstay;

        private float overstaySince = -1f;
        private float firstViolationAt = -1f;
        private int violations;

        /// <summary>The last violation, for the log.</summary>
        public string LastReason { get; private set; } = string.Empty;

        public AuthorityValidator(float maxSpeed, Bounds table, BallLanes lanes, float hysteresis, float maxOverstay)
        {
            this.maxSpeed = maxSpeed;
            this.table = table;
            this.lanes = lanes;
            this.hysteresis = hysteresis;
            this.maxOverstay = maxOverstay;
        }

        public void Reset()
        {
            overstaySince = -1f;
            firstViolationAt = -1f;
            violations = 0;
        }

        /// <summary>
        /// Checks one sample from the guest's simulation. Returns true when the host should take the
        /// ball back.
        /// </summary>
        public bool Check(in BallSample s, double hostNow)
        {
            float now = Time.unscaledTime;

            if (s.Time > hostNow + FutureTolerance || s.Time < hostNow - PastTolerance)
            {
                return Violate("sample time off the shared clock", now);
            }

            if (new Vector3(s.Vel.x, 0f, s.Vel.z).magnitude > maxSpeed + SpeedTolerance)
            {
                return Violate($"speed {s.Vel.magnitude:F1} m/s over the cap", now);
            }

            if (!table.Contains(s.Pos))
            {
                return Violate("ball outside the table", now);
            }

            // The guest should hand the ball back once it is deep in a Red lane. A little overstay is
            // normal (the handoff takes a moment); a long one is either a bug or a guest keeping the
            // ball where it has no figures and so no business simulating it.
            if (!s.Has(BallSampleFlags.Carried) && lanes.Valid
                && lanes.OwnerAt(s.Pos, out float depth) == Team.Red && depth >= hysteresis)
            {
                if (overstaySince < 0f) overstaySince = now;
                else if (now - overstaySince > maxOverstay)
                {
                    overstaySince = -1f;
                    LastReason = "guest kept the ball in a Red lane";
                    return true;
                }
            }
            else
            {
                overstaySince = -1f;
            }

            return false;
        }

        private bool Violate(string reason, float now)
        {
            LastReason = reason;

            if (firstViolationAt < 0f || now - firstViolationAt > ViolationWindow)
            {
                firstViolationAt = now;
                violations = 0;
            }

            violations++;
            return violations >= 2;
        }
    }
}
