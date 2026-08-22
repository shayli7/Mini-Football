using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Turns a player's own idle rods out of the path of their own shot.
    ///
    /// The problem it solves is specific to this table. A team's rods are interleaved with the
    /// opponent's, so every shot from the five-man row has to pass the SAME team's three-man row on
    /// its way to goal. At a real table you hold that rod up with your other hand; on a phone you
    /// have two thumbs and four rods, so in practice the shot rebounds off your own team and the
    /// game reads as though attacking is impossible.
    ///
    /// Ninety degrees is the angle that clears the ball, and it is not obvious. A figure's collider
    /// straddles the bar rather than hanging below it, so a 180-degree flip still reaches back down
    /// into the ball. Turned side-on the collider presents only its thickness to the vertical, which
    /// lifts its lowest point clear of the top of the ball.
    ///
    /// Deliberately narrow. A rod lifts only while a shot of the player's own is actually about to
    /// reach it — never for a ball travelling toward their own goal, which is what keeps a defence
    /// on the pitch, and never for a rod a finger is on, which is what keeps the three-man row
    /// usable as a shooting rod.
    /// </summary>
    [DisallowMultipleComponent]
    public class RodAutoLift : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [SerializeField] private TableReferences table;
        [SerializeField] private BallController ball;
        [SerializeField] private MatchManager match;
        [SerializeField] private RodTouchInput input;

        [Header("Assist")]
        [Tooltip("Turn the whole assist off to play it straight, as a real table does.")]
        [SerializeField] private bool assistEnabled = true;

        [Tooltip("How fast the ball must be travelling toward the opponent's goal, in metres/second, " +
                 "before rods get out of its way. Below this it is a dribble, not a shot, and the " +
                 "player is still using their rods to control it.")]
        [SerializeField] private float minShotSpeed = 0.6f;

        [Tooltip("How far ahead a rod looks, in seconds. A rod lifts only once the ball would reach " +
                 "it within this time, so rods far up the table stay down until the shot is real.")]
        [SerializeField] private float lookAhead = 0.3f;

        [Tooltip("Degrees to turn to. 90 puts the figures side-on, which is the smallest turn that " +
                 "lifts their collider clear of the top of the ball.")]
        [SerializeField] private float liftAngle = 90f;

        [Tooltip("How fast a rod turns out of the way, in degrees/second. Well above the upright " +
                 "return: this one is racing a shot.")]
        [SerializeField] private float liftSpeed = 900f;

        [Header("Debug")]
        [SerializeField] private bool logSetupOnStart = false;

        private Vector3 longAxis = Vector3.right;

        /// <summary>Which way along the long axis each team attacks. Keyed by team.</summary>
        private readonly Dictionary<Team, float> attackSign = new Dictionary<Team, float>();

        private readonly List<RodController> allRods = new List<RodController>();

        private bool ready;

        private void Awake()
        {
            if (table == null)
            {
                table = GetComponentInParent<TableReferences>() ?? FindAnyObjectByType<TableReferences>();
            }

            if (ball == null) ball = FindAnyObjectByType<BallController>();
            if (match == null) match = FindAnyObjectByType<MatchManager>();
            if (input == null) input = FindAnyObjectByType<RodTouchInput>();
        }

        /// <summary>
        /// Built in Start, not Awake. Everything here is measured from <see cref="RodController.BarAxis"/>
        /// and <see cref="RodController.BarPivot"/>, which only exist once each rod has cached its
        /// rest pose in its own Awake — and Awake order between components is not guaranteed.
        /// </summary>
        private void Start()
        {
            if (table == null || ball == null)
            {
                Debug.LogWarning($"{name}: needs a TableReferences and a BallController. Auto-lift off.", this);
                enabled = false;
                return;
            }

            allRods.Clear();
            allRods.AddRange(table.RedRods);
            allRods.AddRange(table.BlueRods);

            RodController reference = null;
            foreach (RodController rod in allRods)
            {
                if (rod != null)
                {
                    reference = rod;
                    break;
                }
            }

            if (reference == null || reference.BarAxis.sqrMagnitude < 1e-6f)
            {
                Debug.LogWarning($"{name}: rods have no bar axis yet. Auto-lift off.", this);
                enabled = false;
                return;
            }

            longAxis = Vector3.Cross(reference.BarAxis, Vector3.up).normalized;

            attackSign[Team.Red] = AttackSignFor(table.RedRods);
            attackSign[Team.Blue] = AttackSignFor(table.BlueRods);

            ready = true;

            if (logSetupOnStart)
            {
                Debug.Log($"{name}: long axis {longAxis}, red attacks {attackSign[Team.Red]:+0;-0}, " +
                          $"blue attacks {attackSign[Team.Blue]:+0;-0}.", this);
            }
        }

        /// <summary>
        /// Which way this team attacks: away from the half its own rods sit in. Measured rather than
        /// configured, so it is right for either team and whichever way round the table is placed.
        /// </summary>
        private float AttackSignFor(IReadOnlyList<RodController> rods)
        {
            float ownHalf = 0f;
            int counted = 0;

            foreach (RodController rod in rods)
            {
                if (rod != null)
                {
                    ownHalf += Vector3.Dot(rod.BarPivot, longAxis);
                    counted++;
                }
            }

            if (counted > 0)
            {
                ownHalf /= counted;
            }

            return ownHalf > 0f ? -1f : 1f;
        }

        /// <summary>
        /// Runs in FixedUpdate to sit in step with <see cref="RodController"/>, which ticks there
        /// while its body is kinematic.
        /// </summary>
        private void FixedUpdate()
        {
            if (!ready)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;

            foreach (RodController rod in allRods)
            {
                if (rod == null)
                {
                    continue;
                }

                bool lift = assistEnabled && ShouldLift(rod);

                // Cleared as well as set, every frame: a rod that stops qualifying has to hand its
                // spin straight back to the upright return, and a stale flag would also leave it
                // permanently unable to strike.
                rod.LiftingClear = lift;

                if (!lift)
                {
                    continue;
                }

                float target = NearestLiftAngle(rod.CurrentSpinAngle);
                rod.SetSpinAngle(Mathf.MoveTowardsAngle(rod.CurrentSpinAngle, target, liftSpeed * dt));
            }
        }

        /// <summary>Whichever side-on angle is the shorter turn from here.</summary>
        private float NearestLiftAngle(float current)
        {
            float up = Mathf.Abs(Mathf.DeltaAngle(current, liftAngle));
            float down = Mathf.Abs(Mathf.DeltaAngle(current, -liftAngle));
            return up <= down ? liftAngle : -liftAngle;
        }

        private bool ShouldLift(RodController rod)
        {
            if (match != null && !match.PlayLive)
            {
                return false;
            }

            // Never the AI's rods: RodAgent already settles its own figures, and a second hand on
            // them would fight it. Never a rod a finger is on either — that one is being aimed.
            if (input != null && (!input.HumanControls(rod.Team) || input.IsHeld(rod)))
            {
                return false;
            }

            Rigidbody body = ball.Body;
            if (body == null)
            {
                return false;
            }

            if (!attackSign.TryGetValue(rod.Team, out float sign))
            {
                return false;
            }

            // Everything below is measured along the long axis in this team's attacking direction,
            // so "ahead" and "toward goal" are both simply positive.
            float towardGoal = Vector3.Dot(body.linearVelocity, longAxis) * sign;

            // A ball coming back at this team is exactly when its rods must NOT be out of the way.
            if (towardGoal < minShotSpeed)
            {
                return false;
            }

            float ballAt = Vector3.Dot(ball.transform.position, longAxis) * sign;
            float rodAt = Vector3.Dot(rod.BarPivot, longAxis) * sign;

            float ahead = rodAt - ballAt;
            if (ahead <= 0f)
            {
                // The shot is already past this rod; standing up again is the useful thing to do.
                return false;
            }

            return ahead / towardGoal <= lookAhead;
        }
    }
}
