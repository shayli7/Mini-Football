using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Makes the ball the host's alone.
    ///
    /// The ball is the one object both players watch closely and the only one whose physics is
    /// genuinely chaotic, so it is simulated in exactly one place and mirrored everywhere else. A
    /// NetworkTransform on the same GameObject carries the pose; this component's job is to make sure
    /// the guest's copy does not also try to simulate it. Two machines running the same Rigidbody
    /// against slightly different rod positions diverge within a second or two, and the ball ends up
    /// in a different half of the table on each screen.
    ///
    /// So on the guest the Rigidbody goes kinematic and <see cref="BallController"/> switches off
    /// wholesale — the speed cap, the fall-through safety net and the dead-ball rescue are all
    /// decisions about a simulation the guest is not running, and a second opinion on any of them is
    /// worse than none.
    ///
    /// This is also the one place where a ball being physically absent from the guest's simulation
    /// shows through: no contacts happen there, so no impact sounds do either. They are relayed.
    ///
    /// One more thing the guest lacks is any ball motion of its own. Its ball is a pose the host
    /// recorded a round-trip ago, played back by NetworkTransform's interpolation — which also
    /// deliberately renders a little in the past to stay smooth. The two together read as a laggy,
    /// floaty ball. Since the host also ships the ball's velocity here, the guest dead-reckons its
    /// ball forward along that velocity — capped hard, and smoothed on and off — to hide most of the
    /// delay without ever running a second, divergent simulation. See <see cref="LateUpdate"/>.
    /// </summary>
    [RequireComponent(typeof(BallController))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class NetworkedBall : NetworkBehaviour
    {
        [Header("Guest extrapolation (hides network lag on the mirrored ball)")]
        [Tooltip("How far ahead, in seconds, the guest leads the ball on top of the measured " +
                 "half round-trip. Set it near the NetworkTransform interpolation buffer so the two " +
                 "roughly cancel. 0 disables the fixed part and leans on RTT alone.")]
        [SerializeField] private float interpolationLeadSeconds = 0.04f;
        [Tooltip("Ceiling on the total lead, in seconds, so a spike in ping can't fling the ball " +
                 "far ahead of where the host actually has it.")]
        [SerializeField] private float maxLeadSeconds = 0.12f;
        [Tooltip("Hard cap on how far ahead the ball may be drawn, in metres. This is the real " +
                 "safety net against rubber-banding: a fast shot changes direction on a bounce, and " +
                 "whatever we led it by has to be small enough to snap back invisibly. Keep it a few " +
                 "ball-widths at most.")]
        [SerializeField] private float maxLeadDistance = 0.12f;
        [Tooltip("How sharply the lead eases in and out, per second. Stops the ball popping the " +
                 "instant it starts or stops moving. Higher = snappier, lower = softer.")]
        [SerializeField] private float leadSharpness = 25f;

        private BallController ball;
        private Rigidbody body;
        private SphereCollider sphere;

        /// <summary>World radius of the ball, so a lead can be stopped a ball's width short of a wall.</summary>
        private float ballRadius = 0.02f;

        /// <summary>Reused by the lead's clearance check so it never allocates. Eight is far more
        /// than the ball can have in front of it along one line.</summary>
        private readonly RaycastHit[] leadHits = new RaycastHit[8];

        /// <summary>
        /// The host's live ball velocity, in m/s, so the guest can lead its mirrored ball along it.
        /// Server-written, everyone-read: only the host simulates the ball, so only the host knows.
        /// </summary>
        private readonly NetworkVariable<Vector3> netVelocity = new(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>The lead currently applied to the guest's transform, eased toward its target and
        /// re-based on NetworkTransform's fresh pose every frame, so it never accumulates.</summary>
        private Vector3 appliedLead;

        /// <summary>Below this change, in m/s, the host does not bother re-publishing its velocity.</summary>
        private const float VelocityEpsilon = 0.01f;

        /// <summary>
        /// How far outside the ball's own surface a local rod counts as touching it, in metres. The
        /// guest is guessing at a contact the host will resolve properly, so a little generosity here
        /// buys a few milliseconds of earliness at the cost of the occasional prediction that the
        /// host does not confirm — which costs nothing, because an unconfirmed guess simply expires.
        /// </summary>
        private const float TouchSkin = 0.004f;

        /// <summary>
        /// How long a prediction waits to be matched by the host's relayed impact before it is
        /// forgotten, as a multiple of the round trip. Generous on purpose: a prediction that expires
        /// early un-silences a relayed sound the guest has already heard, which is a double thump.
        /// </summary>
        private const float PredictionLifetimeRtts = 1.5f;

        /// <summary>Floor for the above, so a 0 ms LAN still gives predictions time to be matched.</summary>
        private const float MinPredictionLifetime = 0.25f;

        /// <summary>Reused by the touch test so it never allocates.</summary>
        private readonly Collider[] touchResults = new Collider[8];

        /// <summary>
        /// When the guest predicted its own hits, newest last. Each entry is waiting to cancel one
        /// relayed impact from the host — see <see cref="PlayImpactRpc"/>.
        /// </summary>
        private readonly List<float> predictedImpacts = new List<float>();

        /// <summary>Whether a local rod was already touching the ball last frame, so a sound plays on
        /// the leading edge of a contact and not once per frame throughout it.</summary>
        private bool touchingLocalRod;

        /// <summary>
        /// The ball skin everyone in this match sees, chosen by the HOST.
        ///
        /// A cosmetic both players look at the whole match has to be one ball, not two — each player
        /// seeing their own would make every shot look different on the two screens, and a skin with
        /// a trail would disagree about where the trail is. The host owns it for the same reason it
        /// owns the ball's physics: it is the machine whose answer is already authoritative, so
        /// nothing has to be negotiated.
        ///
        /// Server-written, everyone-read, and a fixed string rather than a free one because Netcode
        /// needs a bounded size on the wire. Catalogue ids are short by design ("ball.golden"); an id
        /// that ever outgrew this would be truncated and simply resolve to the plain ball.
        /// </summary>
        private readonly NetworkVariable<FixedString64Bytes> netSkin = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        /// <summary>The ball's own skin component, if the Ball carries one. Optional: an online match
        /// on a table with no BallSkinner simply keeps whatever material the model has.</summary>
        private BallSkinner skinner;

        private void Awake()
        {
            ball = GetComponent<BallController>();
            body = GetComponent<Rigidbody>();
            sphere = GetComponent<SphereCollider>();
            skinner = GetComponent<BallSkinner>();

            if (sphere != null)
            {
                Vector3 s = transform.lossyScale;
                ballRadius = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }
        }

        public override void OnNetworkSpawn()
        {
            // Both sides watch the skin, and both sides APPLY it — the host included. Wearing its own
            // published value rather than its local inventory is what guarantees the two machines
            // cannot drift apart: there is one value and one code path that reads it.
            netSkin.OnValueChanged += OnSkinChanged;

            if (IsServer)
            {
                // The host's own equipped skin becomes the match's ball. Written before the guest can
                // possibly ask: a NetworkVariable's current value is delivered to a late joiner as
                // part of the spawn, so a guest arriving later still gets it without an RPC.
                netSkin.Value = new FixedString64Bytes(
                    Inventory.EquippedId(CosmeticKind.BallSkin) ?? string.Empty);
            }

            ApplyNetSkin();

            if (IsServer)
            {
                // The host simulates as it always has. Relay each contact to the guest, who has no
                // way of knowing one happened.
                ball.Impact += OnHostImpact;
                return;
            }

            // Kinematic first: NetworkTransform writes this transform every tick, and PhysX would
            // otherwise treat each of those writes as a teleport and fight it with its own solve.
            body.isKinematic = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            ball.enabled = false;

            // BallController.ApplyPhysics turned this on for a Rigidbody that PhysX drives. Left on
            // here, it is a second smoother fighting NetworkTransform's own interpolation over the
            // same transform, with no fresh physics pose of its own to interpolate toward on a
            // kinematic body — the two disagreeing is what reads as the ball moving unpredictably on
            // the guest's screen.
            body.interpolation = RigidbodyInterpolation.None;
        }

        public override void OnNetworkDespawn()
        {
            netSkin.OnValueChanged -= OnSkinChanged;

            // The local player's own choice comes back the moment the online match ends. Without
            // this, the next local match would still be wearing whatever the last host happened to
            // have equipped — a skin this player may not even own.
            if (skinner != null)
            {
                skinner.ClearOverride();
            }

            if (IsServer)
            {
                ball.Impact -= OnHostImpact;
            }

            // Hand the ball back to local physics, on both machines. The guest left it kinematic
            // with BallController switched off, and Netcode's own AutoSetKinematicOnDespawn parks it
            // kinematic again on the way out — for the host too, which never went kinematic itself.
            // Without this, the local match after an online one starts with a ball that nothing on
            // the table can move. This component sits after NetworkRigidbody on the GameObject, so
            // this runs after that and is the state the ball keeps.
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            ball.enabled = true;

            appliedLead = Vector3.zero;
        }

        private void OnSkinChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            ApplyNetSkin();
        }

        /// <summary>
        /// Wears whatever the host published. An empty value means the host had nothing equipped, in
        /// which case the override is cleared rather than set to "" — <see cref="BallSkinner"/> reads
        /// empty as "no override", and setting it explicitly would be the same thing said twice.
        /// </summary>
        private void ApplyNetSkin()
        {
            if (skinner == null)
            {
                return;
            }

            string id = netSkin.Value.ToString();
            if (string.IsNullOrEmpty(id))
            {
                skinner.ClearOverride();
            }
            else
            {
                skinner.SetOverride(id);
            }
        }

        /// <summary>Host only: publish the ball's velocity so the guest can lead its mirror by it.
        /// Runs on the physics clock, which is also the network tick rate here, so this is at most one
        /// packet per tick and only when the velocity has actually changed.</summary>
        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            Vector3 v = body.linearVelocity;
            if ((v - netVelocity.Value).sqrMagnitude >= VelocityEpsilon * VelocityEpsilon)
            {
                netVelocity.Value = v;
            }
        }

        /// <summary>
        /// Guest only: lead the mirrored ball along the host's velocity to hide the round-trip.
        ///
        /// Runs in LateUpdate, after NetworkTransform has written this frame's interpolated (and
        /// deliberately slightly stale) pose to the transform. Because the lead is re-derived from
        /// that fresh pose every frame — read the transform, add the offset — it can never drift or
        /// accumulate: when the ball is at rest the host's velocity is ~0, so the offset eases to zero
        /// and the ball sits exactly where the host says it is.
        ///
        /// The offset is capped in both time and distance, and eased on and off, because this is NOT a
        /// simulation: it is a straight-line guess. A bounce reverses the true velocity in one tick,
        /// so whatever we led the ball by has to be small enough to correct itself invisibly the
        /// moment the host's next velocity arrives.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsSpawned || IsServer)
            {
                return;
            }

            float lead = Mathf.Min(maxLeadSeconds, interpolationLeadSeconds + HalfRttSeconds());
            Vector3 targetLead = Vector3.ClampMagnitude(netVelocity.Value * lead, maxLeadDistance);

            // Never lead the ball past something it is about to hit. This is what makes the guess
            // safe at a contact, which is the one place a straight line is certainly WRONG: led
            // blindly, the ball is drawn into the figure it is about to bounce off, and when the
            // host's reversed velocity arrives the lead flips sign — a correction of twice the lead,
            // landing exactly on the touch. Stopping the lead at the surface means there is no
            // overshoot left to take back, so the bounce simply happens.
            float clearance = ClearanceAhead(targetLead);
            targetLead = Vector3.ClampMagnitude(targetLead, clearance);

            float k = leadSharpness > 0f ? 1f - Mathf.Exp(-leadSharpness * Time.deltaTime) : 1f;
            appliedLead = Vector3.Lerp(appliedLead, targetLead, k);

            transform.position += appliedLead;

            // After the lead, so the guess is made against the ball the player can actually SEE. The
            // lead exists to put the drawn ball where the host already has it; predicting against the
            // un-led pose would fire the sound behind the picture.
            PredictOwnImpact();
        }

        /// <summary>
        /// Sounds the guest's own kicks the moment they look like they happen, instead of a round trip
        /// later.
        ///
        /// The guest never simulates the ball, so no contact is generated on this machine and every
        /// impact arrives relayed from the host — a full RTT after the swing that caused it. For the
        /// opponent's hits that is fine: they are somebody else's action and there is nothing to
        /// compare the delay against. For the player's OWN kick it is the single most damning thing in
        /// the build, because the ear is far better at spotting a late confirmation of your own action
        /// than the eye is at spotting a late ball. It reads as input lag even when the picture is
        /// right.
        ///
        /// So the guest guesses, but only about itself. It watches for its own rods meeting the ball
        /// and plays on the leading edge of that — matching the host, which raises its own impact from
        /// OnCollisionEnter and so also fires once per contact rather than throughout it. The host
        /// remains the authority: this only decides WHEN a sound is heard, never what the ball does.
        ///
        /// Every guess is recorded, and <see cref="PlayImpactRpc"/> cancels one relayed impact against
        /// it, so a correct prediction is heard exactly once. A wrong one is never heard at all — it
        /// just expires unmatched.
        /// </summary>
        private void PredictOwnImpact()
        {
            if (ball == null)
            {
                return;
            }

            bool touching = TryFindOwnRodTouch(out RodController rod, out float speed);

            // Leading edge only. A ball resting against a foot is one contact, not one per frame.
            if (touching && !touchingLocalRod && speed >= ball.QuietImpactSpeed)
            {
                float strength = Mathf.InverseLerp(ball.QuietImpactSpeed, ball.LoudImpactSpeed, speed);
                GameSfx.PlayBallHit(strength);
                Haptics.Play(strength);
                predictedImpacts.Add(Time.time);
            }

            touchingLocalRod = touching;
            ExpirePredictions();
        }

        /// <summary>
        /// Looks for one of THIS player's own rods against the ball, and estimates how hard the
        /// meeting is.
        ///
        /// The speed estimate is the ball's own pace plus the foot's, because either alone gets a
        /// common case wrong: a still figure meeting a driven ball is loud, and a whipped figure
        /// meeting a still ball is loud, and only their sum describes both. The foot's speed is worked
        /// out from how far the ball sits off the bar's centreline — that distance IS the radius the
        /// foot is sweeping at the contact, so it needs no configuring and stays right for any figure
        /// on any rod.
        /// </summary>
        private bool TryFindOwnRodTouch(out RodController rod, out float speed)
        {
            rod = null;
            speed = 0f;

            int count = Physics.OverlapSphereNonAlloc(transform.position, ballRadius + TouchSkin,
                                                      touchResults, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = touchResults[i];

                // The ball's own collider is always in this list and is never a contact.
                if (hit == null || hit.attachedRigidbody == body)
                {
                    continue;
                }

                RodController candidate = hit.GetComponentInParent<RodController>();

                // Only this player's rods. The opponent's hits are their action, not ours, and
                // guessing at them would just add wrong sounds to ones that already arrive correctly.
                if (candidate == null || candidate.Team != OnlineMatchDirector.LocalTeam)
                {
                    continue;
                }

                rod = candidate;
                break;
            }

            if (rod == null)
            {
                return false;
            }

            float ballSpeed = netVelocity.Value.magnitude;

            // Distance from the bar's centreline out to the ball — the radius the foot sweeps at.
            Vector3 axis = rod.BarAxis;
            float footSpeed = 0f;
            if (axis.sqrMagnitude > 1e-6f)
            {
                axis.Normalize();
                Vector3 relative = transform.position - rod.BarPivot;
                float radius = (relative - Vector3.Project(relative, axis)).magnitude;
                footSpeed = Mathf.Abs(rod.MeasuredSpinSpeed) * Mathf.Deg2Rad * radius;
            }

            speed = ballSpeed + footSpeed;
            return true;
        }

        /// <summary>Drops guesses the host never confirmed, so they cannot silence a later real hit.</summary>
        private void ExpirePredictions()
        {
            float lifetime = Mathf.Max(MinPredictionLifetime,
                                       HalfRttSeconds() * 2f * PredictionLifetimeRtts);
            float cutoff = Time.time - lifetime;

            // Oldest first, so stopping at the first live entry is safe.
            while (predictedImpacts.Count > 0 && predictedImpacts[0] < cutoff)
            {
                predictedImpacts.RemoveAt(0);
            }
        }

        /// <summary>
        /// How far the ball may be carried along <paramref name="lead"/> before it would reach a
        /// surface, measured from the authoritative pose the transform currently holds. Everything on
        /// the table — figures, bars, rails — exists on the guest as an ordinary collider, so this is
        /// a plain query against the local scene and needs nothing from the host.
        /// </summary>
        private float ClearanceAhead(Vector3 lead)
        {
            float distance = lead.magnitude;
            if (distance < 1e-4f)
            {
                return 0f;
            }

            Vector3 direction = lead / distance;
            int count = Physics.RaycastNonAlloc(transform.position, direction, leadHits,
                                                distance + ballRadius, ~0, QueryTriggerInteraction.Ignore);

            float nearest = distance;
            for (int i = 0; i < count; i++)
            {
                Collider hit = leadHits[i].collider;

                // The ray starts at the ball's own centre, so its own collider is the one thing in
                // front of it that must never count.
                if (hit == null || hit.attachedRigidbody == body)
                {
                    continue;
                }

                nearest = Mathf.Min(nearest, Mathf.Max(0f, leadHits[i].distance - ballRadius));
            }

            return nearest;
        }

        /// <summary>Half the measured round-trip to the host, in seconds — the part of the lag that
        /// varies with the connection. Zero if the transport can't report it.</summary>
        private float HalfRttSeconds()
        {
            if (NetworkManager == null)
            {
                return 0f;
            }

            if (NetworkManager.NetworkConfig.NetworkTransport is UnityTransport transport)
            {
                return transport.GetCurrentRtt(NetworkManager.ServerClientId) * 0.0005f; // ms → s, halved
            }

            return 0f;
        }

        private void OnHostImpact(float strength, bool struckRod)
        {
            PlayImpactRpc(strength, struckRod);
        }

        /// <summary>
        /// NotServer, because the host already played this sound locally the moment the contact
        /// happened — <see cref="BallController"/> does that itself, and the event that got us here
        /// fires immediately after it.
        /// </summary>
        [Rpc(SendTo.NotServer)]
        private void PlayImpactRpc(float strength, bool struckRod)
        {
            if (struckRod)
            {
                // This may be the host confirming a hit the guest already sounded for itself, a full
                // round trip ago — see PredictOwnImpact. Cancel it against the oldest outstanding
                // guess rather than thumping twice for one kick. Only rod hits are ever predicted, so
                // wall thuds below are always played as they arrive.
                ExpirePredictions();
                if (predictedImpacts.Count > 0)
                {
                    predictedImpacts.RemoveAt(0);
                    return;
                }

                GameSfx.PlayBallHit(strength);
                Haptics.Play(strength); // the guest feels its own hits too
            }
            else
            {
                GameSfx.PlayWallThud(strength);
            }
        }
    }
}
