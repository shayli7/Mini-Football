using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// TEMPORARY DIAGNOSTIC — delete once the online ball is behaving.
    ///
    /// Answers one question: when the ball's velocity changes, what changed it? It watches the ball
    /// every physics step, and whenever the velocity jumps by more than <see cref="reportDelta"/> it
    /// logs the jump alongside every rod that was in contact that step and what that rod's state was.
    /// A jump with no rod in the contact list is the "nothing was touching it" case, and the log says
    /// which of the ball's own systems (rescue nudge, control window, wall assist, speed cap) or which
    /// unexpected collider was responsible.
    ///
    /// Put it on the Ball GameObject alongside BallController. It only reports on the machine that
    /// actually simulates the ball, so in an online match it is the host that produces the log.
    /// </summary>
    [RequireComponent(typeof(BallController))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class BallMotionProbe : MonoBehaviour
    {
        [Tooltip("Velocity change in m/s within one physics step that counts as worth reporting. " +
                 "Gravity and rolling friction sit well under 0.1; a genuine kick is over 1.")]
        [SerializeField] private float reportDelta = 0.25f;

        [Tooltip("Also log every rod whose spin says it could strike, even on steps where the ball " +
                 "did not jump. Noisy — turn on only when hunting a rod that moves without input.")]
        [SerializeField] private bool logArmedRods = false;

        [Tooltip("Rod spin, in degrees/second, above which a rod is reported as armed to strike. " +
                 "Match BallController's Strike Min Spin.")]
        [SerializeField] private float armedSpin = 200f;

        private Rigidbody body;
        private BallController ball;
        private RodController[] rods;

        private Vector3 previousVelocity;
        private readonly List<Collider> touchingThisStep = new List<Collider>();
        private readonly Dictionary<RodController, float> previousAngles = new Dictionary<RodController, float>();

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            ball = GetComponent<BallController>();
        }

        private void Start()
        {
            rods = FindObjectsByType<RodController>(FindObjectsSortMode.None);
            foreach (RodController rod in rods)
            {
                previousAngles[rod] = rod.CurrentSpinAngle;
            }

            Debug.Log($"[BallProbe] watching {rods.Length} rods. Reporting velocity jumps over " +
                      $"{reportDelta} m/s.", this);
        }

        private void OnCollisionEnter(Collision collision) => touchingThisStep.Add(collision.collider);

        private void OnCollisionStay(Collision collision) => touchingThisStep.Add(collision.collider);

        private void FixedUpdate()
        {
            // A kinematic ball is not being simulated here — this is the guest, and its ball is just
            // a replicated pose. Nothing this probe measures would mean anything.
            if (body.isKinematic)
            {
                touchingThisStep.Clear();
                return;
            }

            Vector3 velocity = body.linearVelocity;
            float delta = (velocity - previousVelocity).magnitude;

            if (delta >= reportDelta)
            {
                Report(delta, velocity);
            }
            else if (logArmedRods)
            {
                ReportArmedRods();
            }

            foreach (RodController rod in rods)
            {
                if (rod != null) previousAngles[rod] = rod.CurrentSpinAngle;
            }

            previousVelocity = velocity;
            touchingThisStep.Clear();
        }

        private void Report(float delta, Vector3 velocity)
        {
            var line = new StringBuilder();
            line.Append($"[BallProbe] dV={delta:F2} m/s  v={velocity.magnitude:F2}  pos={body.position}");

            if (touchingThisStep.Count == 0)
            {
                line.Append("  CONTACTS: none  <-- nothing was touching the ball");
            }
            else
            {
                line.Append("  CONTACTS:");
                foreach (Collider c in touchingThisStep)
                {
                    if (c == null) continue;
                    RodController rod = c.GetComponentInParent<RodController>();
                    line.Append(rod != null
                        ? $" [rod {rod.name} team={rod.Team} {DescribeRod(rod)}]"
                        : $" [table {c.name}]");
                }
            }

            // Any rod turning fast enough to strike, whether or not it registered a contact — a rod
            // that swept THROUGH the ball inside one step leaves no contact behind but still moved it.
            string armed = ArmedRodSummary();
            if (armed.Length > 0)
            {
                line.Append($"  ARMED:{armed}");
            }

            Debug.Log(line.ToString(), this);
        }

        private void ReportArmedRods()
        {
            string armed = ArmedRodSummary();
            if (armed.Length > 0)
            {
                Debug.Log($"[BallProbe] armed:{armed}", this);
            }
        }

        private string ArmedRodSummary()
        {
            var line = new StringBuilder();
            foreach (RodController rod in rods)
            {
                if (rod == null) continue;
                if (Mathf.Abs(rod.MeasuredSpinSpeed) < armedSpin && AngleStep(rod) < 5f) continue;
                line.Append($" [{rod.name} {DescribeRod(rod)}]");
            }

            return line.ToString();
        }

        /// <summary>
        /// The reported spin next to the angle the rod ACTUALLY moved this step. Online those two
        /// disagreeing is the whole problem: the strike model reads the first, the ball is hit by
        /// the second.
        /// </summary>
        private string DescribeRod(RodController rod)
        {
            float stepped = AngleStep(rod);
            float impliedSpeed = stepped / Time.fixedDeltaTime;

            // The bug's signature: MeasuredSpinSpeed says the rod is swinging hard while the rod has
            // barely moved. A clean reading has the two agree.
            bool stale = Mathf.Abs(rod.MeasuredSpinSpeed) >= armedSpin && impliedSpeed < armedSpin * 0.5f;
            string flag = stale ? " <-- STALE (armed but not moving)" : string.Empty;

            return $"spin={rod.MeasuredSpinSpeed:F0}deg/s moved={stepped:F1}deg " +
                   $"(implies {impliedSpeed:F0}deg/s) lifted={rod.LiftingClear}{flag}";
        }

        private float AngleStep(RodController rod)
        {
            if (!previousAngles.TryGetValue(rod, out float previous)) return 0f;
            return Mathf.Abs(Mathf.DeltaAngle(previous, rod.CurrentSpinAngle));
        }
    }
}
