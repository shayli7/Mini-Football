using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Keeps the ball in one simulation and draws it smoothly everywhere else.
    ///
    /// The ball is the one object both players watch closely and the only one whose physics is
    /// genuinely chaotic, so exactly one machine simulates it at a time — two Rigidbodies run against
    /// slightly different rod positions diverge within a second. That machine streams
    /// <see cref="BallSample"/>s (position, velocity, rotation, stamped on the shared
    /// <see cref="NetClock"/>) every physics step; the other draws them through a
    /// <see cref="BallStream"/>, which curves smoothly between samples and absorbs uneven arrival.
    ///
    /// This replaced NetworkTransform + NetworkRigidbody + a velocity-lead extrapolation. That stack
    /// drew "whatever arrived last" with no real jitter buffer, so packets bunching up over Relay
    /// showed as stutter, and the lead — driven by velocity arriving on a separate channel — lurched
    /// the wrong way at every bounce. It also could not be told to stop writing the transform, which a
    /// machine that sometimes simulates the ball itself needs.
    ///
    /// For now the host always simulates (epoch 0, Red). The sample format, the NotMe routing and
    /// <see cref="SetSimulating"/> already allow the simulating machine to change, which is what
    /// lane authority and the away rules build on.
    ///
    /// The machine that is not simulating runs no <see cref="BallController"/> at all — the speed cap,
    /// fall-through net and dead-ball rescue are decisions about a simulation it is not running — so it
    /// has no contacts and no impact sounds of its own. Those are relayed, and the guest predicts the
    /// sound of its OWN kicks so they are not a round trip late.
    /// </summary>
    [DefaultExecutionOrder(100)] // sample after BallController's own FixedUpdate has adjusted the velocity
    [RequireComponent(typeof(BallController))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class NetworkedBall : NetworkBehaviour
    {
        [Header("Drawing the mirrored ball")]
        [Tooltip("Longest the ball may be carried forward past the newest sample when samples run out, " +
                 "in seconds. Past this it holds still rather than guess further.")]
        [SerializeField] private float maxExtrapolation = 0.06f;
        [Tooltip("A correction smaller than this, in metres, is smoothed out; a larger one snaps.")]
        [SerializeField] private float errorSnapDistance = 0.05f;
        [Tooltip("How long a smoothed correction takes to fade, in seconds.")]
        [SerializeField] private float errorDecay = 0.06f;

        [Header("Temporary diagnostics")]
        [Tooltip("Log stream health every two seconds on the machine drawing the mirrored ball.")]
        [SerializeField] private bool logStreamStats = true;

        private BallController ball;
        private Rigidbody body;
        private SphereCollider sphere;

        /// <summary>World radius of the ball.</summary>
        private float ballRadius = 0.02f;

        private BallStream stream;

        /// <summary>Whether THIS machine is running the ball's physics right now.</summary>
        private bool simulating = true;

        /// <summary>Set by <see cref="BallController.Teleported"/>; the next sample carries the flag.</summary>
        private bool pendingTeleport;

        /// <summary>The simulation term. Constant until authority can change hands.</summary>
        private uint epoch;

        private int seenClockSnaps = -1;
        private float nextStatsLog;

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

        /// <summary>A relayed impact older than this, in seconds, is dropped: a sound that late would
        /// land on a moment the player has already watched go by.</summary>
        private const float StaleImpactSeconds = 0.35f;

        /// <summary>Reused by the touch test so it never allocates.</summary>
        private readonly Collider[] touchResults = new Collider[8];

        /// <summary>
        /// When the guest predicted its own hits, newest last. Each entry is waiting to cancel one
        /// relayed impact from the host — see <see cref="BallImpactRpc"/>.
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

            // A leftover NetworkTransform would go on writing this transform every frame on the guest.
            // The sample stream still wins (it draws later in the frame), but the two are doing one job
            // twice and the stale one costs bandwidth — say so rather than leave it silently.
            if (GetComponent<Unity.Netcode.Components.NetworkTransform>() != null)
            {
                Debug.LogWarning($"{name}: still carries a NetworkTransform. The ball is synced by " +
                                 "NetworkedBall's sample stream now — remove NetworkRigidbody, then " +
                                 "NetworkTransform, from the Ball.", this);
            }

            stream = new BallStream(body, ballRadius, ball.WallRetention, maxExtrapolation,
                                    errorSnapDistance, errorDecay);
            epoch = 0;
            seenClockSnaps = -1;

            if (IsServer)
            {
                ball.Impact += OnLocalImpact;
                ball.Teleported += OnTeleported;
                SetSimulating(true);
            }
            else
            {
                SetSimulating(false);
            }
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

            ball.Impact -= OnLocalImpact;
            ball.Teleported -= OnTeleported;

            // Hand the ball back to local physics on both machines, or the local match after an online
            // one starts with a ball nothing on the table can move.
            SetSimulating(true);

            stream?.Reset();
            predictedImpacts.Clear();
            touchingLocalRod = false;
        }

        /// <summary>
        /// Switches this machine between simulating the ball and drawing someone else's simulation of
        /// it. The ONLY place that changes the body's kinematic flag, its interpolation and
        /// <see cref="BallController"/>'s enabled state — always together, because they are not
        /// independent: Unity delivers collision callbacks to disabled scripts too, so a dynamic ball
        /// with BallController switched off would still run its strike logic.
        /// </summary>
        private void SetSimulating(bool on)
        {
            simulating = on;

            if (on)
            {
                body.isKinematic = false;
                ball.enabled = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                return;
            }

            // Velocities first, while the body is still dynamic — a kinematic body has none to set.
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            ball.enabled = false;
            body.isKinematic = true;

            // Nothing for PhysX to interpolate between on a body it does not move: the stream draws it.
            body.interpolation = RigidbodyInterpolation.None;
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

        private void OnTeleported()
        {
            pendingTeleport = true;
        }

        // ── sending ────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The simulating machine streams the ball every physics step. Stamped with the step's own time
        /// on the shared clock, so several steps run back to back at the start of one frame keep their
        /// true spacing on the other side.
        /// </summary>
        private void FixedUpdate()
        {
            if (!IsSpawned || !simulating)
            {
                return;
            }

            var flags = BallSampleFlags.None;
            if (pendingTeleport) flags |= BallSampleFlags.Teleport;
            if (ball.IsBeingCarried) flags |= BallSampleFlags.Carried;
            pendingTeleport = false;

            var sample = new BallSample
            {
                Time = NetClock.SharedFixedNow,
                Pos = body.position,
                Vel = body.isKinematic ? Vector3.zero : body.linearVelocity,
                AngVel = body.isKinematic ? Vector3.zero : body.angularVelocity,
                Rot = body.rotation,
                Epoch = epoch,
                AuthorityTeam = (byte)OnlineMatchDirector.LocalTeam,
                Flags = flags,
            };

            BallSampleRpc(sample);
        }

        /// <summary>
        /// Unreliable: a lost sample is simply skipped by the curve, whereas a reliable one would hold
        /// up every sample behind it until it was resent — the stall that used to show as a freeze
        /// followed by a jump. NotMe, so the same call works whichever machine is simulating.
        /// </summary>
        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        private void BallSampleRpc(BallSample sample, RpcParams rpcParams = default)
        {
            // Only the simulating machine may move the ball. Today that is always the host.
            if (simulating || rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                return;
            }

            if (sample.Epoch < epoch)
            {
                return;
            }

            // Until the shared clock is known, arrival times cannot be compared with sample times —
            // and lateness measured against a clock that is about to jump would poison the buffer.
            if (!NetClock.Synced)
            {
                return;
            }

            stream.Add(sample, NetClock.SharedNow);
        }

        // ── drawing ────────────────────────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!IsSpawned || simulating || stream == null)
            {
                return;
            }

            if (NetClock.SnapCount != seenClockSnaps)
            {
                seenClockSnaps = NetClock.SnapCount;
                stream.ResetTiming();
            }

            if (!stream.HasSamples)
            {
                return;
            }

            double now = NetClock.SharedNow;
            stream.UpdateDelay(now);

            if (stream.Sample(now - stream.RenderDelay, Time.unscaledDeltaTime,
                              out Vector3 pos, out _, out Quaternion rot))
            {
                // Both the transform (what is drawn) and the kinematic body (what physics queries such
                // as the own-kick touch test see) — physics does not pick up transform writes until its
                // next step, and the touch test runs this frame.
                transform.SetPositionAndRotation(pos, rot);
                body.position = pos;
                body.rotation = rot;
            }

            PredictOwnImpact();
            LogStreamStats();
        }

        /// <summary>TEMPORARY: stream health every two seconds, to confirm the stutter is gone and to
        /// tune the buffer. Remove once Phase A is confirmed on device.</summary>
        private void LogStreamStats()
        {
            if (!logStreamStats || Time.unscaledTime < nextStatsLog)
            {
                return;
            }

            nextStatsLog = Time.unscaledTime + 2f;

            if (stream.Frames > 0)
            {
                Debug.Log($"[BallStream] frames={stream.Frames} starved={stream.StarvedFrames} " +
                          $"received={stream.Received} lateDrops={stream.LateDrops} " +
                          $"depth={stream.DepthSum / stream.Frames * 1000.0:F0}ms " +
                          $"delay={stream.RenderDelay * 1000.0:F0}ms rtt={NetClock.Rtt * 1000.0:F0}ms");
            }

            stream.Frames = stream.StarvedFrames = stream.Received = stream.LateDrops = 0;
            stream.DepthSum = 0;
        }

        // ── sounds ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sounds the guest's own kicks the moment they look like they happen, instead of a round trip
        /// later.
        ///
        /// The guest never simulates the ball, so no contact is generated on this machine and every
        /// impact arrives relayed — a full round trip after the swing that caused it. For the
        /// opponent's hits that is fine: they are somebody else's action and there is nothing to
        /// compare the delay against. For the player's OWN kick it reads as input lag even when the
        /// picture is right, because the ear is far better at spotting a late confirmation of your own
        /// action than the eye is at spotting a late ball.
        ///
        /// So the guest guesses, but only about itself: it plays on the leading edge of its own rod
        /// meeting the ball, matching the simulating machine, which raises its impact from
        /// OnCollisionEnter and so also fires once per contact. Every guess is recorded, and
        /// <see cref="BallImpactRpc"/> cancels one relayed impact against it, so a correct prediction
        /// is heard exactly once and a wrong one never — it just expires unmatched.
        /// </summary>
        private void PredictOwnImpact()
        {
            bool touching = TryFindOwnRodTouch(out float speed);

            // Leading edge only. A ball resting against a foot is one contact, not one per frame.
            if (touching && !touchingLocalRod && speed >= ball.QuietImpactSpeed)
            {
                float strength = Mathf.InverseLerp(ball.QuietImpactSpeed, ball.LoudImpactSpeed, speed);
                GameSfx.PlayBallHit(strength);
                Haptics.Play(strength);
                predictedImpacts.Add(Time.unscaledTime);
            }

            touchingLocalRod = touching;
            ExpirePredictions();
        }

        /// <summary>
        /// Looks for one of THIS player's own rods against the ball, and estimates how hard the
        /// meeting is: the ball's own pace plus the foot's, because either alone gets a common case
        /// wrong — a still figure meeting a driven ball is loud, and so is a whipped figure meeting a
        /// still ball. The foot's speed comes from how far the ball sits off the bar's centreline,
        /// which IS the radius the foot sweeps at the contact.
        /// </summary>
        private bool TryFindOwnRodTouch(out float speed)
        {
            speed = 0f;
            RodController rod = null;

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
                if (candidate != null && candidate.Team == OnlineMatchDirector.LocalTeam)
                {
                    rod = candidate;
                    break;
                }
            }

            if (rod == null)
            {
                return false;
            }

            float footSpeed = 0f;
            Vector3 axis = rod.BarAxis;
            if (axis.sqrMagnitude > 1e-6f)
            {
                axis.Normalize();
                Vector3 relative = transform.position - rod.BarPivot;
                float radius = (relative - Vector3.Project(relative, axis)).magnitude;
                footSpeed = Mathf.Abs(rod.MeasuredSpinSpeed) * Mathf.Deg2Rad * radius;
            }

            speed = stream.LastVelocity.magnitude + footSpeed;
            return true;
        }

        /// <summary>Drops guesses never confirmed, so they cannot silence a later real hit.</summary>
        private void ExpirePredictions()
        {
            float lifetime = Mathf.Max(MinPredictionLifetime, (float)NetClock.Rtt * PredictionLifetimeRtts);
            float cutoff = Time.unscaledTime - lifetime;

            // Oldest first, so stopping at the first live entry is safe.
            while (predictedImpacts.Count > 0 && predictedImpacts[0] < cutoff)
            {
                predictedImpacts.RemoveAt(0);
            }
        }

        /// <summary>The simulating machine heard a contact itself; pass it on.</summary>
        private void OnLocalImpact(float strength, bool struckRod)
        {
            BallImpactRpc(NetClock.SharedNow, strength, struckRod);
        }

        /// <summary>
        /// NotMe, because the simulating machine already played this sound the moment the contact
        /// happened. Unreliable and timestamped: a lost thump costs nothing, and a late one is dropped
        /// rather than played over a moment the player has already watched go by.
        /// </summary>
        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        private void BallImpactRpc(double time, float strength, bool struckRod)
        {
            if (NetClock.Synced && NetClock.SharedNow - time > StaleImpactSeconds)
            {
                return;
            }

            if (struckRod)
            {
                // This may confirm a hit the guest already sounded for itself — see
                // PredictOwnImpact. Cancel it against the oldest outstanding guess rather than thumping
                // twice for one kick. Only rod hits are ever predicted, so wall thuds always play.
                ExpirePredictions();
                if (predictedImpacts.Count > 0)
                {
                    predictedImpacts.RemoveAt(0);
                    return;
                }

                GameSfx.PlayBallHit(strength);
                Haptics.Play(strength);
            }
            else
            {
                GameSfx.PlayWallThud(strength);
            }
        }
    }
}
