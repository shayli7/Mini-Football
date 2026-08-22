using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// The one dynamic object on the table. Physics moves it; the kinematic rods push it.
    ///
    /// The ball owns its own physics material, built at runtime from the values below, so there is
    /// no material asset to wire up. Its combine modes are Average — the LOWEST priority PhysX
    /// has — so the ball never overrules what it hits and every contact takes its character from
    /// the surface instead. That is deliberate and it is the whole basis of ball control: a figure
    /// can be dead and grippy while a rail two centimetres away is lively and slippery. See
    /// TableSurfaces for how the surfaces claim that authority, and for the one rule the values
    /// here have to respect.
    ///
    /// Where power comes from: not from bounciness, and not from Max Speed. A figure's foot is only
    /// a few centimetres from the bar, so even a full-speed swing carries little momentum, and a
    /// ball made bouncy enough to fly off it would also fly off every accidental touch — no
    /// control. Instead the figure stays dead, and a rod that is genuinely SWINGING adds an
    /// explicit strike impulse (see OnCollisionEnter). Power and control end up on separate dials.
    ///
    /// ResetBall is what the goal reset in Step 4 will call.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class BallController : MonoBehaviour
    {
        [Header("Feel (arcade)")]
        [Tooltip("Lower = livelier, easier to send flying.")]
        [SerializeField] private float mass = 0.05f;
        [Tooltip("BASELINE bounciness only. Each surface on the table overrides this — see " +
                 "TableSurfaces. Keep it between the surfaces' low values (figures, pitch) and " +
                 "their high one (rails), or those surfaces stop having any effect.")]
        [Range(0f, 1f)]
        [SerializeField] private float bounciness = 0.5f;
        [Tooltip("BASELINE grip only, overridden per surface by TableSurfaces. Keep it above the " +
                 "rails' friction and below the figures' and the pitch's.")]
        [Range(0f, 1f)]
        [SerializeField] private float friction = 0.05f;
        [Tooltip("Air resistance. Near zero on purpose: the pitch's own friction should be what " +
                 "slows the ball, so it decelerates like a ball on a table rather than fading out " +
                 "in mid-flight.")]
        [SerializeField] private float linearDrag = 0.02f;
        [SerializeField] private float angularDrag = 0.05f;
        [Tooltip("Speed cap in m/s, so a hard flick can't fire the ball through the table. Note " +
                 "this is headroom, not the thing that governs how hard shots are: a figure's foot " +
                 "moves a few m/s at most, so real shots land far below this. Power comes from " +
                 "Strike Boost below and from the rod's Max Spin Speed.")]
        [SerializeField] private float maxSpeed = 12f;
        [Tooltip("Contact precision for this ball. Must be small next to its radius or bounces " +
                 "feel mushy. Set here as well as globally, since the global value only applies " +
                 "to colliders created after it changes.")]
        [SerializeField] private float contactOffset = 0.002f;
        [Tooltip("Max spin in rad/s. The default clamps a struck ball's spin quite low.")]
        [SerializeField] private float maxAngularSpeed = 100f;

        [Header("Safety")]
        [Tooltip("If the ball drops this far below its start height it is treated as lost and reset. " +
                 "0 disables the safety net.")]
        [SerializeField] private float fallResetDistance = 0.3f;

        [Header("Dead ball rescue")]
        [Tooltip("Below this speed the ball counts as standing still, in m/s.")]
        [SerializeField] private float stuckSpeed = 0.05f;
        [Tooltip("How long it may stand still before it is nudged back into play. 0 disables the " +
                 "rescue. Raise it if you want players to be able to trap and hold the ball longer.")]
        [SerializeField] private float stuckSeconds = 10f;
        [Tooltip("Speed of the first nudge toward the centre spot, in m/s. Each further nudge is " +
                 "this much stronger again.")]
        [SerializeField] private float nudgeSpeed = 0.45f;
        [Tooltip("Random spread on the nudge direction, in degrees, so a repeatedly stuck ball " +
                 "does not retrace the same path back into the same pocket.")]
        [SerializeField] private float nudgeSpread = 30f;
        [Tooltip("After this many nudges fail to free it, the ball is returned to the centre spot.")]
        [SerializeField] private int nudgesBeforeReset = 3;
        [SerializeField] private bool logRescues = true;

        [Header("Striking — where shot power comes from")]
        [Tooltip("Extra speed in m/s added by a full-power swing, on top of what the figure's own " +
                 "momentum imparts. This is the main power dial.")]
        [SerializeField] private float strikeBoost = 3f;
        [Tooltip("Rod spin below this, in degrees/second, adds nothing at all. This is the line " +
                 "between trapping and kicking: a still or slowly-moving figure just deadens the " +
                 "ball, which is what keeps close control possible.")]
        [SerializeField] private float strikeMinSpin = 200f;
        [Tooltip("Rod spin treated as a full-power swing, in degrees/second.")]
        [SerializeField] private float strikeFullSpin = 1800f;
        [Tooltip("Minimum seconds between boosts from the same rod, so one swing that brushes the " +
                 "ball twice does not count twice.")]
        [SerializeField] private float strikeCooldown = 0.05f;

        [Header("Sliding — positioning, not shooting")]
        [Tooltip("The most speed, in m/s, that sliding a rod sideways may give the ball along the " +
                 "bar. Sliding is how you walk the ball across to another player or line up a " +
                 "shot, so it should move the ball, not fire it. Shots are unaffected: a swing " +
                 "throws the ball ACROSS the bar, and only the along-the-bar part is capped here.")]
        [SerializeField] private float maxSlidePush = 0.8f;
        [Tooltip("How fast the rod must be sliding, in m/s, before the cap applies at all. Below " +
                 "this the rod is barely moving and physics is left alone.")]
        [SerializeField] private float minSlideSpeed = 0.15f;

        [Header("Wall rebound")]
        [Tooltip("Guarantees a ball leaves a rail at the angle it arrived. Turn off to run on " +
                 "PhysX's own restitution alone.")]
        [SerializeField] private bool wallAssist = true;
        [Tooltip("Fraction of its speed the ball keeps through an assisted rebound.")]
        [Range(0f, 1f)]
        [SerializeField] private float wallBounceRetention = 0.85f;

        [Header("Audio")]
        [Tooltip("Impacts slower than this make no sound, in m/s. Without a floor the ball chatters " +
                 "constantly as it settles against a figure or rail.")]
        [SerializeField] private float quietImpactSpeed = 0.35f;
        [Tooltip("Impact speed treated as a full-strength hit, in m/s.")]
        [SerializeField] private float loudImpactSpeed = 3f;

        private Rigidbody body;
        private SphereCollider ball;
        private Vector3 homePosition;

        /// <summary>Goal-to-goal direction, used to steer the dead-ball rescue off the halfway line.</summary>
        private Vector3 longAxis;

        /// <summary>How much of a rescue nudge must run goal-to-goal rather than across the table.</summary>
        private const float MinRescueAlongness = 0.5f;
        private Quaternion homeRotation;

        private float stillTime;
        private int nudges;
        private bool rescueActive = true;

        // How hard the ball must be driving INTO a wall before the rebound assist will touch it.
        // A ball running along a rail, or resting against one, keeps re-establishing contact with
        // almost no approach speed; without this floor those grazes get "rebounded" too, and each
        // one shaves speed off a ball that never actually hit anything.
        private const float MinAssistedApproach = 0.05f;

        // The ball's velocity going INTO a contact. OnCollisionEnter runs after the solver, so by
        // then the velocity has already been changed by the very collision being handled — both the
        // strike boost and the wall rebound need the incoming vector, not the outgoing one.
        private Vector3 lastVelocity;
        private RodController lastStriker;
        private float lastStrikeTime;

        public Rigidbody Body => body;

        /// <summary>
        /// Raised on every audible impact, with its strength 0..1 and whether a rod was struck rather
        /// than the table. Exists for the online relay — see the call site in OnCollisionEnter.
        /// </summary>
        public event System.Action<float, bool> Impact;

        /// <summary>The ball's own bounciness, before any surface overrides it. TableSurfaces
        /// checks its values against this to catch settings that can never take effect.</summary>
        public float BaselineBounciness => bounciness;

        /// <summary>The ball's own friction, before any surface overrides it.</summary>
        public float BaselineFriction => friction;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            ball = GetComponent<SphereCollider>();

            homePosition = transform.position;
            homeRotation = transform.rotation;

            ApplyPhysics();
        }

        /// <summary>
        /// Re-asserts the ball as a dynamic body, once every Awake on this object has run.
        ///
        /// Netcode's NetworkRigidbody parks the body kinematic from its own Awake, unconditionally
        /// and whether or not a session is ever started, and it sits after this component on the
        /// GameObject — so ApplyPhysics cannot win that race from Awake, and a purely local match
        /// gets a kinematic ball. Nothing on the table can then move it: the figures sweep straight
        /// through, which reads as broken colliders rather than as a physics-state problem, because
        /// the ball still sits correctly on the pitch and every collider is present and enabled.
        ///
        /// Start is the right place precisely because it runs after all Awakes and long before any
        /// OnNetworkSpawn. The guest's kinematic ball is set in NetworkedBall on spawn, so it is
        /// still applied after this and is not disturbed.
        /// </summary>
        private void Start()
        {
            body.isKinematic = false;

            // Measured here rather than in Awake because BarAxis only exists once each rod has
            // cached its rest pose in its own Awake, and Awake order between components is not
            // guaranteed. Taken from a rod rather than configured so it is right whichever way
            // round the table sits.
            foreach (RodController rod in FindObjectsByType<RodController>(FindObjectsSortMode.None))
            {
                if (rod != null && rod.BarAxis.sqrMagnitude > 1e-6f)
                {
                    longAxis = Vector3.Cross(rod.BarAxis, Vector3.up).normalized;
                    break;
                }
            }
        }

        /// <summary>Pushes the inspector values onto the Rigidbody and the ball's material.</summary>
        [ContextMenu("Apply physics settings")]
        public void ApplyPhysics()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (ball == null) ball = GetComponent<SphereCollider>();

            body.mass = mass;
            body.linearDamping = linearDrag;
            body.angularDamping = angularDrag;
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxAngularVelocity = maxAngularSpeed;

            // The ball is small and fast and the figures are thin — without continuous detection it
            // punches straight through a spinning rod instead of being hit by it.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Never let the ball fall asleep. A slow roll that sleeps stops dead mid-pitch, and a
            // sleeping ball wedged in a corner reports zero velocity forever — the rescue below
            // would keep firing at a body PhysX has already stopped simulating.
            body.sleepThreshold = 0f;

            var material = new PhysicsMaterial("Ball (runtime)")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                // Average is the lowest-priority combine mode, so any surface that declares
                // Minimum or Maximum outranks the ball and decides the contact itself. Setting
                // Maximum/Minimum here instead — which looks like a safe "the ball always wins"
                // default — silently disables every per-surface material on the table.
                bounceCombine = PhysicsMaterialCombine.Average,
                frictionCombine = PhysicsMaterialCombine.Average
            };

            ball.material = material;
            ball.contactOffset = contactOffset;
        }

        private void FixedUpdate()
        {
            // Cap the speed: a hard enough flick could otherwise beat even continuous detection.
            if (maxSpeed > 0f && body.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                body.linearVelocity = body.linearVelocity.normalized * maxSpeed;
            }

            // Safety net: if the ball escapes the table, put it back rather than lose it forever.
            if (fallResetDistance > 0f && transform.position.y < homePosition.y - fallResetDistance)
            {
                ResetBall();
                return;
            }

            TickRescue(Time.fixedDeltaTime);

            // Last thing in the step: this is the velocity the ball carries into whatever it hits
            // during the next one.
            lastVelocity = body.linearVelocity;
        }

        /// <summary>
        /// Frees a ball that has stopped moving. The corner ramps stop the common case, but a ball
        /// can still die anywhere the players cannot reach it — pinned under a bar, resting against
        /// a figure's foot, or wedged in the goal mouth — and there is no way for a player to
        /// recover it. Rather than a single hard reset, this escalates: a gentle nudge toward the
        /// centre spot first, harder each time, and only a full reset once nudging has clearly
        /// failed. Most stalls end after the first nudge and the player barely notices.
        ///
        /// The nudge aims at the ball's home position, which is the centre spot, so no reference to
        /// the table is needed.
        /// </summary>
        private void TickRescue(float dt)
        {
            if (!rescueActive || stuckSeconds <= 0f)
            {
                stillTime = 0f;
                return;
            }

            float speed = body.linearVelocity.magnitude;
            if (speed > stuckSpeed)
            {
                stillTime = 0f;

                // Properly away and moving again — forget the earlier attempts, so a later stall
                // starts gently instead of jumping straight to a reset.
                if (speed > stuckSpeed * 4f)
                {
                    nudges = 0;
                }

                return;
            }

            stillTime += dt;
            if (stillTime < stuckSeconds)
            {
                return;
            }

            stillTime = 0f;
            nudges++;

            if (nudgesBeforeReset > 0 && nudges > nudgesBeforeReset)
            {
                if (logRescues)
                {
                    Debug.Log($"{name}: stuck at {transform.position} after {nudgesBeforeReset} " +
                              "nudges — returning it to the centre spot.", this);
                }

                ResetBall();
                return;
            }

            Vector3 toCentre = homePosition - transform.position;
            toCentre.y = 0f;

            // Already on the centre spot (so there is no "toward the centre"): pick any direction.
            Vector2 fallback = Random.insideUnitCircle.normalized;
            Vector3 direction = toCentre.sqrMagnitude > 1e-6f
                ? toCentre.normalized
                : new Vector3(fallback.x, 0f, fallback.y);

            direction = Quaternion.AngleAxis(Random.Range(-nudgeSpread, nudgeSpread), Vector3.up) * direction;

            // Make sure the nudge actually crosses the table rather than running along the halfway
            // line. The centre spot is a dead lane where no figure can reach the ball, and the two
            // instincts above both aim straight at it: "toward the centre" IS the dead spot, and a
            // random direction from it is as likely as not to run parallel to the line and stay
            // there. Either way the rescue would drop the ball back where nothing can play it and
            // fire again a few seconds later.
            if (longAxis.sqrMagnitude > 1e-6f)
            {
                float alongness = Vector3.Dot(direction, longAxis);

                if (Mathf.Abs(alongness) < MinRescueAlongness)
                {
                    Vector3 escape = longAxis * (alongness >= 0f ? 1f : -1f);
                    direction = Vector3.Lerp(direction, escape, MinRescueAlongness).normalized;
                }
            }

            // Each attempt is stronger, but capped — with resets disabled (0) this would otherwise
            // keep escalating until the ball is fired off the table.
            float strength = nudgeSpeed * Mathf.Min(nudges, 4);

            // VelocityChange so the nudge is a speed in m/s regardless of the ball's mass.
            body.AddForce(direction * strength, ForceMode.VelocityChange);

            if (logRescues)
            {
                Debug.Log($"{name}: dead ball at {transform.position} — nudge {nudges} " +
                          $"at {strength:0.##} m/s.", this);
            }
        }

        /// <summary>
        /// Turns the dead-ball rescue on and off. The match parks the ball, motionless and on
        /// purpose, between a goal and the next kick-off — and after the winning goal it stays
        /// parked indefinitely. Without this the rescue would read that as a stuck ball and fire
        /// the parked ball across the table.
        /// </summary>
        public void SetRescueActive(bool active)
        {
            rescueActive = active;
            stillTime = 0f;
            nudges = 0;
        }

        /// <summary>
        /// Sounds each impact, scaled by how hard it was, and split by what was struck: a figure
        /// gives the sharp knock of a kick, everything else the duller thud of the table.
        ///
        /// Driving this from the actual collision — rather than from the input that caused it — means
        /// a ball deflected onto a rail or nudged by a rod it merely brushed sounds right, with no
        /// separate bookkeeping about who hit what.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            // Figures and bars hang off a rod; anything else is the table itself.
            RodController rod = collision.collider != null
                ? collision.collider.GetComponentInParent<RodController>()
                : null;

            if (rod != null)
            {
                ApplyStrikeBoost(rod);
                CapSlidePush(rod);
            }
            else
            {
                ApplyWallRebound(collision);
            }

            float speed = collision.relativeVelocity.magnitude;
            if (speed < quietImpactSpeed)
            {
                return;
            }

            float strength = Mathf.InverseLerp(quietImpactSpeed, loudImpactSpeed, speed);

            if (rod != null)
            {
                GameSfx.PlayBallHit(strength);
            }
            else
            {
                GameSfx.PlayWallThud(strength);
            }

            // Online, only the host simulates the ball, so this is the only machine where a contact
            // ever happens. NetworkedBall listens here and relays the hit, or the guest would watch
            // the whole match in silence.
            Impact?.Invoke(strength, rod != null);
        }

        /// <summary>
        /// The same two rod behaviours, for contact that is already established.
        ///
        /// Both need this, for opposite reasons. Trapping the ball and then shooting is the core
        /// move of the game, and in that case the figure is ALREADY touching the ball when the
        /// swing starts — no new collision is generated, so an Enter-only strike boost would miss
        /// every shot taken from a trap, which is most of them. Sliding is the mirror image: a rod
        /// dragged sideways keeps pushing the ball for as long as the contact lasts, so capping it
        /// once at first touch would leave the rest of the drag free to launch it.
        /// </summary>
        private void OnCollisionStay(Collision collision)
        {
            RodController rod = collision.collider != null
                ? collision.collider.GetComponentInParent<RodController>()
                : null;

            if (rod == null)
            {
                return;
            }

            ApplyStrikeBoost(rod);
            CapSlidePush(rod);
        }

        /// <summary>
        /// Adds the power of a swing to the ball, scaled by how fast the rod is really turning.
        ///
        /// This exists because the two things the game wants are in direct conflict under plain
        /// physics. Power would normally come from making the figures bouncy — but a bouncy figure
        /// fires the ball away on every accidental brush, so close control disappears. Keeping the
        /// figures dead restores control and leaves shots feeble.
        ///
        /// Splitting them fixes both at once: the figure's material stays dead and grippy, so a
        /// still figure traps and drags the ball exactly as before, and the power arrives here
        /// instead — only when the rod is actually being swung. Below strikeMinSpin nothing is
        /// added at all, which is what makes a slow, deliberate touch a genuine touch.
        ///
        /// Reads MeasuredSpinSpeed, not CurrentSpinVelocity: the latter is pinned at zero for the
        /// whole of a finger drag, so a boost keyed on it would fire for the AI and never once for
        /// the player.
        /// </summary>
        private void ApplyStrikeBoost(RodController rod)
        {
            if (strikeBoost <= 0f)
            {
                return;
            }

            // A rod standing back upright or lifting out of the way is not swinging AT the
            // ball, however fast it happens to be turning. Treated as a swing, the two
            // behaviours that exist to stop the ball being blocked would kick it instead.
            if (rod.StrikeSuppressed)
            {
                if (rod == lastStriker)
                {
                    lastStriker = null;
                }

                return;
            }

            float spin = rod.MeasuredSpinSpeed;

            if (Mathf.Abs(spin) < strikeMinSpin)
            {
                // Resting or merely drifting: a trap, not a kick. This is also where a rod earns
                // the right to strike again — see below.
                if (rod == lastStriker)
                {
                    lastStriker = null;
                }

                return;
            }

            // One swing must add its power once. Contact with a trapped ball persists across the
            // whole swing, so a rod stays spent until its spin has dropped back below the strike
            // threshold; the cooldown is a floor for the case where another rod strikes in between
            // and takes the slot.
            if (rod == lastStriker || Time.time - lastStrikeTime < strikeCooldown)
            {
                return;
            }

            float power = Mathf.InverseLerp(strikeMinSpin, strikeFullSpin, Mathf.Abs(spin));

            // ForwardKickDirection is the way a foot travels under POSITIVE spin, with the rod's
            // own invertSpin already folded in, so it is correct for either team and either way
            // round the figures were modelled. Flip it for a backwards swing.
            Vector3 direction = rod.ForwardKickDirection * Mathf.Sign(spin);
            direction.y = 0f;

            if (direction.sqrMagnitude < 1e-6f)
            {
                return;
            }

            body.AddForce(direction.normalized * (strikeBoost * power), ForceMode.VelocityChange);

            lastStriker = rod;
            lastStrikeTime = Time.time;
        }

        /// <summary>
        /// Keeps sliding a rod sideways a way of POSITIONING the ball rather than hitting it.
        ///
        /// A rod being dragged is a kinematic body teleporting to wherever the finger is —
        /// SetSlide01Immediate applies the finger's position with no easing — so PhysX sees an
        /// enormous implied velocity and fires the ball along the bar. Walking the ball across to
        /// another player, or shifting a rod to line a shot up, ends up hitting it harder than an
        /// actual shot does, which is both wrong and the opposite of control.
        ///
        /// The fix leans on the geometry: a swing throws the ball ACROSS the bar
        /// (ForwardKickDirection is perpendicular to the bar axis) while a slide pushes it ALONG
        /// the bar. Capping only the along-the-bar component therefore costs shots nothing at all.
        ///
        /// It is a cap, never a brake: a ball that was already travelling that fast along the bar
        /// before the contact keeps its speed, so this can only ever remove push the rod has just
        /// added.
        /// </summary>
        private void CapSlidePush(RodController rod)
        {
            if (maxSlidePush <= 0f || Mathf.Abs(rod.MeasuredSlideSpeed) < minSlideSpeed)
            {
                return; // the rod is not really sliding — leave the physics alone
            }

            Vector3 axis = rod.BarAxis;
            axis.y = 0f;

            if (axis.sqrMagnitude < 1e-6f)
            {
                return;
            }

            axis.Normalize();

            float before = Vector3.Dot(lastVelocity, axis);
            float after = Vector3.Dot(body.linearVelocity, axis);

            // Whatever the ball already had along the bar is its own and is never taken away.
            float allowed = Mathf.Max(Mathf.Abs(before), maxSlidePush);
            if (Mathf.Abs(after) <= allowed)
            {
                return;
            }

            body.linearVelocity += axis * (Mathf.Sign(after) * allowed - after);
        }

        /// <summary>
        /// Makes a ball leave a rail at the angle it arrived.
        ///
        /// PhysX only applies restitution to the part of the impact along the contact normal, and
        /// an angled hit puts very little there — a ball meeting a rail at 8° drives barely a tenth
        /// of its speed into the wall. Anything under Physics.bounceThreshold is dropped entirely,
        /// and near the solver's precision floor even what survives comes out mushy. The ball keeps
        /// its sideways motion, loses the rest, and slides along the rail instead of rebounding.
        ///
        /// The threshold is now low enough that most hits bounce properly on their own, so this
        /// only steps in when a rebound really was swallowed: it compares what came back along the
        /// normal against what should have. Honest bounces are left exactly as PhysX computed them.
        /// </summary>
        private void ApplyWallRebound(Collision collision)
        {
            if (!wallAssist || collision.contactCount == 0)
            {
                return;
            }

            Vector3 normal = collision.GetContact(0).normal;

            // Only walls. A near-vertical normal is the pitch underneath the ball or the top of a
            // corner ramp, and "reflecting" off those would fire the ball into the air.
            if (Mathf.Abs(normal.y) > 0.5f)
            {
                return;
            }

            Vector3 incoming = lastVelocity;
            incoming.y = 0f;

            float approach = Vector3.Dot(incoming, normal); // negative: heading into the wall
            if (-approach < MinAssistedApproach)
            {
                return;
            }

            float expected = -approach * wallBounceRetention;
            float actual = Vector3.Dot(body.linearVelocity, normal);

            if (actual >= expected * 0.5f)
            {
                return; // PhysX handled it well enough; leave the bounce alone
            }

            Vector3 reflected = Vector3.Reflect(incoming, normal);
            reflected.y = 0f;

            if (reflected.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector3 rebound = reflected.normalized * (incoming.magnitude * wallBounceRetention);
            body.linearVelocity = new Vector3(rebound.x, body.linearVelocity.y, rebound.z);
        }

        /// <summary>Stops the ball dead and returns it to its start position.</summary>
        public void ResetBall()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(homePosition, homeRotation);
            body.position = homePosition;
            body.rotation = homeRotation;

            stillTime = 0f;
            nudges = 0;
        }

        /// <summary>Re-captures the current position as the reset point.</summary>
        [ContextMenu("Set current position as home")]
        public void SetHomeToCurrent()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }
    }
}
