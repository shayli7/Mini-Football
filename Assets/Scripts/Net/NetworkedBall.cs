using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Keeps the ball in one simulation at a time and draws it everywhere else.
    ///
    /// The ball is the one object both players watch closely and the only one whose physics is
    /// genuinely chaotic, so exactly one machine simulates it at any moment — two Rigidbodies run against
    /// slightly different rod positions diverge within a second. That machine streams
    /// <see cref="BallSample"/>s (position, velocity, rotation, stamped on the shared
    /// <see cref="NetClock"/>) every physics step; the other draws them through a <see cref="BallStream"/>.
    ///
    /// <b>Lane authority.</b> WHICH machine simulates follows the ball down the table. The rows alternate
    /// by team, so the ball's position mostly decides whose figures can touch it (see
    /// <see cref="BallLanes"/>). Whoever owns the lane the ball is in simulates it, with the real
    /// <see cref="BallController"/> — so every player's OWN traps, blocks, passes and shots happen on
    /// their own phone, instantly, judged exactly as they saw them, however long the round trip. The
    /// other player draws that simulation projected forward to the present, and sees the opponent's
    /// touches arrive one trip late, as a forward jump.
    ///
    /// The hand over, per simulation "term" (<c>epoch</c>, which only ever grows):
    /// - The simulating machine hands off once its ball is a little way into the other team's lane and
    ///   nothing of its own has touched it for a moment — mid-gap, where no figure can reach the ball,
    ///   so its motion over the trip is predictable. It sends the state reliably and starts drawing.
    /// - The other machine does not wait for that: the moment the ball it DRAWS (projected to now)
    ///   enters its own lane, it starts simulating provisionally from what the player sees. When the
    ///   hand over arrives it keeps going if the two agree, rebases if they do not, and aborts if the
    ///   old simulator's samples show the ball never came.
    /// - The host is the referee: it takes the ball back at every goal and kick-off, when the guest goes
    ///   silent, and when the guest's samples fail <see cref="AuthorityValidator"/>; and it judges goals
    ///   in the guest's simulation from the guest's real path (<see cref="GoalJudge"/>).
    ///
    /// With lane authority switched off (the host's <c>laneAuthority</c> field) the host simulates
    /// throughout and the guest draws slightly in the past through an adaptive buffer.
    ///
    /// A machine not simulating runs no <see cref="BallController"/> at all; its contacts and sounds
    /// come from whichever machine is.
    /// </summary>
    [DefaultExecutionOrder(100)] // sample after BallController's own FixedUpdate has adjusted the velocity
    [RequireComponent(typeof(BallController))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class NetworkedBall : NetworkBehaviour
    {
        [Header("Drawing the mirrored ball")]
        [Tooltip("Longest the ball may be carried forward past the newest sample when samples run out, " +
                 "in seconds, when drawing in the past (lane authority off).")]
        [SerializeField] private float maxExtrapolation = 0.06f;
        // Renamed from errorSnapDistance (0.05) / errorDecay (0.06) so the scene's saved values cannot
        // shadow these. At 0.05 m nearly every correction of a ball drawn at the present snapped — an
        // opponent's pass or shot arrives 15–85 cm off the guess — which is what read as teleporting.
        [Tooltip("A correction smaller than this, in metres, runs into place; only a larger one snaps. " +
                 "Kick-offs and resets always snap.")]
        [SerializeField] private float correctionSnapDistance = 0.6f;
        // Replaced correctionBlendTime (an exponential decay, whose first frame moved a 40 cm correction
        // at 8 m/s — a lunge). A correction now closes at a bounded EXTRA speed instead.
        [Tooltip("Fastest a correction is closed at for a slow or resting ball, in m/s. The cap is the " +
                 "larger of this and the fraction of the ball's own speed below; the end of a correction " +
                 "always eases off regardless.")]
        [SerializeField] private float correctionMinSpeed = 0.6f;
        [Tooltip("A correction is closed at up to this fraction of the ball's own speed on top of it. " +
                 "Higher is snappier and lunges more; lower is smoother and trails the truth for longer.")]
        [SerializeField] private float correctionSpeedFraction = 0.6f;

        [Header("Delay buffer (lane authority off only)")]
        [Tooltip("Least buffer above the fastest arrival, in seconds.")]
        [SerializeField] private float bufferMin = 0.01f;
        [Tooltip("Most buffer above the fastest arrival, in seconds. Lower = less delay, more frames " +
                 "drawn by projection when updates arrive late.")]
        [SerializeField] private float bufferMax = 0.035f;
        [Tooltip("Fraction of updates the buffer waits for (0.5–0.99). The late remainder is projected " +
                 "rather than waited for. Lower = less delay, more small corrections.")]
        [Range(0.5f, 0.99f)]
        [SerializeField] private float bufferCoverage = 0.85f;

        [Header("Lane authority (the HOST's settings are the ones used)")]
        [Tooltip("Whoever owns the lane the ball is in simulates it, so each player's own touches are " +
                 "instant. Off: the host simulates throughout.")]
        [SerializeField] private bool laneAuthority = true;
        [Tooltip("How far into a lane the ball must be before that lane's owner takes it, in metres. Keeps " +
                 "a ball sitting on a boundary from changing hands back and forth.")]
        [SerializeField] private float laneHysteresis = 0.01f;
        [Tooltip("How long the ball must stay in the other team's lane before it is handed over, in seconds.")]
        [SerializeField] private float handoffDwell = 0.02f;
        [Tooltip("How long since this simulation's last figure touch before the ball may be handed over, in " +
                 "seconds — never mid-contact.")]
        [SerializeField] private float ownContactDwell = 0.03f;
        [Tooltip("A provisional start this close to the hand over's state is kept as is, in metres. Was " +
                 "0.04 — about 8 ms of travel at 5 m/s, so a quarter of all confirmations rebased, each " +
                 "one a visible snap of the simulated ball for a difference no one could have seen.")]
        [SerializeField] private float rebaseDistance = 0.08f;
        [Tooltip("…and this close in speed, in m/s. Was 0.5.")]
        [SerializeField] private float rebaseSpeed = 0.8f;
        [Tooltip("Grace after a provisional start before the old simulator's samples can cancel it, in seconds.")]
        [SerializeField] private float abortGrace = 0.08f;
        [Tooltip("Longest the drawn ball may be projected forward, in seconds. Past this the stream is " +
                 "treated as stale, and no provisional start is made from it.")]
        [SerializeField] private float maxPresentHorizon = 0.25f;
        [Tooltip("Host: how long the guest may go silent while simulating before the host takes the ball, " +
                 "in seconds.")]
        [SerializeField] private float silenceTakeover = 0.5f;
        [Tooltip("Host: how long the guest may keep simulating a ball that is deep in a Red lane, in seconds.")]
        [SerializeField] private float maxOverstay = 0.15f;
        [Tooltip("A provisional start on a ball whose latest REAL sample is still in the other lane needs " +
                 "it heading into this player's lane at least this fast, in m/s. Slower, the guess that " +
                 "carried it over the line is the one a trap or block at the boundary proves wrong — about " +
                 "half of all provisional starts were being cancelled — so it waits for the hand over.")]
        [SerializeField] private float provisionalMinSpeed = 0.8f;

        [Header("Touch claims (lane authority)")]
        [Tooltip("How close one of this player's own figures must come to a ball they are only DRAWING " +
                 "before they claim its simulation, in metres. Facing rows reach a few centimetres past " +
                 "the lane boundary, and without a claim a touch there waited a round trip.")]
        [SerializeField] private float claimSkin = 0.008f;
        [Tooltip("How long the simulating machine waits for its own figures to leave the ball before it " +
                 "lets a claim lapse, in seconds.")]
        [SerializeField] private float claimWindow = 0.12f;
        [Tooltip("After a claim that came to nothing, how long before this machine may claim again, in " +
                 "seconds — so two figures on one ball cannot pass it back and forth every frame.")]
        [SerializeField] private float claimCooldown = 0.3f;

        [Header("Temporary diagnostics")]
        [Tooltip("Log stream and authority health every two seconds.")]
        [SerializeField] private bool logStreamStats = true;

        private BallController ball;
        private Rigidbody body;
        private SphereCollider sphere;
        private MatchManager match;

        /// <summary>World radius of the ball.</summary>
        private float ballRadius = 0.02f;

        private BallStream stream;
        private BallLanes lanes;

        /// <summary>Whether THIS machine is running the ball's physics right now.</summary>
        private bool simulating = true;

        /// <summary>Set by <see cref="BallController.Teleported"/>; the next sample carries the flag.</summary>
        private bool pendingTeleport;

        // ── authority state ──
        private uint latestEpoch;
        private Team latestAuthority = Team.Red;
        private bool latestEpochSampleSeen;
        private bool provisional;
        private uint provisionalFromEpoch;
        private double provisionalStart;
        private float provisionalStartRealtime;
        private Vector3 provisionalPos, provisionalVel;
        private float inOtherLaneSince = -1f;
        private bool provisionalClaim;
        private bool claimPending;
        private float claimDeadline;
        private float claimCooldownUntil;

        /// <summary>Whether a claim may be made. Spent by a refused claim and re-armed only once this
        /// player's figures have left the ball — one claim per touch.</summary>
        private bool claimArmed = true;

        /// <summary>Refused claims in a row; each doubles the next wait.</summary>
        private int claimRefusals;

        private BallSampleFlags termFlags;
        private float termFlagsUntil;

        // ── a goal let in by this machine's own simulation (guest) ──
        /// <summary>Longest the ball is held on the goal line waiting for the host's verdict, in seconds.
        /// The host's answer is a round trip away; past this the hold lets go rather than strand a ball
        /// the host did not, after all, score.</summary>
        private const float GoalHoldMaxSeconds = 1.5f;

        private bool goalHeld;
        private float goalHeldAt;
        private Vector3 prevSimPos;
        private bool hasPrevSim;

        // ── host referee ──
        private GoalJudge goalJudge;
        private AuthorityValidator validator;
        private Bounds tableBounds;
        private BallSample prevGuestSample;
        private bool hasPrevGuestSample;
        private float lastGuestSampleRealtime;

        /// <summary>The host's lane-authority switch, so both machines run the same mode.</summary>
        private readonly NetworkVariable<bool> netLaneAuthority = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private bool LaneMode => netLaneAuthority.Value;

        /// <summary>Whether this machine is simulating on a provisional start that may yet be cancelled.
        /// Read by the temporary <see cref="BallSmoothnessProbe"/>.</summary>
        public bool IsProvisional => IsSpawned && provisional;

        /// <summary>How far past a lane boundary the other team's figures can reach — the only band in
        /// which a touch claim means anything. The measured reach is approximate, hence the margin.</summary>
        private float ClaimBand => (lanes != null ? lanes.WorstOverlap : 0f) + 0.02f;
        private static Team Me => OnlineMatchDirector.LocalTeam;
        private static Team Other => Me == Team.Red ? Team.Blue : Team.Red;

        // ── diagnostics ──
        private int seenClockSnaps = -1;
        private float nextStatsLog;
        private int statFrames, statHandoffs, statTakeovers, statProvisional, statConfirmed, statRebases,
                    statAborts, statForced, statGoalsJudged, statClaims, statGoalHolds;
        private string lastForceReason = string.Empty;

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

        /// <summary>How long a forced or reset term marks its samples, so a guest that missed the
        /// reliable message still learns of it from the stream.</summary>
        private const float TermFlagSeconds = 0.5f;

        /// <summary>Reused by the touch test so it never allocates.</summary>
        private readonly Collider[] touchResults = new Collider[8];

        /// <summary>
        /// When the guest predicted its own hits, newest last. Each entry is waiting to cancel one
        /// relayed impact — see <see cref="BallImpactRpc"/>.
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
        /// owns the table: it is the machine whose answer is already authoritative, so nothing has to
        /// be negotiated.
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

            StripLegacyTransformSync();
        }

        /// <summary>
        /// Removes a NetworkRigidbody and NetworkTransform left on the Ball from before the sample stream.
        ///
        /// They were never taken off in the scene, and they were the guest-only stutter: with the host as
        /// their authority, the NetworkTransform wrote the host's last-known pose onto the guest's ball
        /// every tick. While the guest only DREW the ball, this class's LateUpdate overwrote it and hid
        /// it; the moment the guest SIMULATED the ball, physics and the NetworkTransform fought over it,
        /// on every frame — while the host's own simulation stayed smooth. The NetworkRigidbody also
        /// forced the body kinematic from its own Awake and on ownership events, and every tick the host
        /// sent the ball's transform a second time over the same channel the rods use.
        ///
        /// Done here, at scene load, so both machines enter every spawn with the same list of network
        /// components — Netcode rebuilds that list at each spawn, so nothing has been indexed yet.
        /// NetworkRigidbody first: it requires NetworkTransform, and Unity refuses to remove a component
        /// another one still requires. Taking them off the Ball in the Inspector as well is still the
        /// tidy fix; until then this keeps them from doing anything.
        /// </summary>
        private void StripLegacyTransformSync()
        {
            bool removed = false;

            var rigidbodySync = GetComponent<Unity.Netcode.Components.NetworkRigidbody>();
            if (rigidbodySync != null)
            {
                DestroyImmediate(rigidbodySync);
                removed = true;
            }

            var transformSync = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (transformSync != null)
            {
                DestroyImmediate(transformSync);
                removed = true;
            }

            if (removed)
            {
                Debug.LogWarning($"{name}: removed a leftover NetworkRigidbody/NetworkTransform at startup — " +
                                 "the ball is synced by NetworkedBall's sample stream. Remove them from the " +
                                 "Ball in the Inspector too.", this);
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

            // Awake removes these; if one is still here, something added it back after load.
            if (GetComponent<Unity.Netcode.Components.NetworkTransform>() != null)
            {
                Debug.LogError($"{name}: carries a NetworkTransform at spawn. It drags the guest's simulated " +
                               "ball toward the host's stale pose every tick — remove it from the Ball.", this);
            }

            match = FindAnyObjectByType<MatchManager>();
            var table = FindAnyObjectByType<TableReferences>();
            lanes = BallLanes.Build(table, ballRadius, transform.position.y);

            stream = new BallStream(body, ballRadius, ball.WallRetention, maxExtrapolation,
                                    correctionSnapDistance, correctionMinSpeed, correctionSpeedFraction,
                                    bufferMin, bufferMax, bufferCoverage);

            latestEpoch = 0;
            latestAuthority = Team.Red;
            latestEpochSampleSeen = false;
            provisional = false;
            provisionalClaim = false;
            claimPending = false;
            claimCooldownUntil = 0f;
            claimArmed = true;
            claimRefusals = 0;
            inOtherLaneSince = -1f;
            seenClockSnaps = -1;

            // Both machines can simulate now, so both relay what their simulation hears and both mark
            // their placed balls; each handler checks whether this machine is the one simulating.
            ball.Impact += OnLocalImpact;
            ball.Teleported += OnTeleported;

            // Both machines: the guest holds its own simulated ball on a goal line it has just crossed,
            // and BOTH draw projections that stop at a goal volume instead of running out of its open back.
            goalJudge = new GoalJudge(ballRadius);
            stream.Goals = goalJudge;
            goalHeld = false;
            hasPrevSim = false;

            // Both machines: when MatchManager puts the ball back after a goal or restart, a ball that is
            // only DRAWN must stop being drawn from its stale projection, or the next LateUpdate puts it
            // straight back where the guess had it — behind the goal.
            if (match != null)
            {
                match.GoalScored += OnBallPlacedByMatch;
                match.MatchRestarted += OnBallPlacedByMatch;
            }

            if (IsServer)
            {
                netLaneAuthority.Value = laneAuthority && lanes.Valid;

                tableBounds = MeasureTable(table);
                // "Deep" in a Red lane starts past the band Blue's figures can reach: inside it the guest
                // may be simulating legitimately, off a touch claim.
                validator = new AuthorityValidator(ball.MaxSpeed, tableBounds, lanes,
                                                   laneHysteresis + ClaimBand, maxOverstay);
                hasPrevGuestSample = false;

                // The referee takes the ball back whenever play is reset, so every kick-off starts on
                // the host whatever was happening when the goal went in.
                if (match != null)
                {
                    match.GoalScored += OnHostGoal;
                    match.MatchRestarted += OnHostReset;
                    match.KickedOff += OnHostReset;
                }

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
            goalHeld = false;

            if (match != null)
            {
                match.GoalScored -= OnBallPlacedByMatch;
                match.MatchRestarted -= OnBallPlacedByMatch;
                match.GoalScored -= OnHostGoal;
                match.MatchRestarted -= OnHostReset;
                match.KickedOff -= OnHostReset;
            }

            // Hand the ball back to local physics on both machines, or the local match after an online
            // one starts with a ball nothing on the table can move.
            ball.EndSimulating();
            SetSimulating(true);

            provisional = false;
            provisionalClaim = false;
            claimPending = false;
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

        /// <summary>Starts simulating mid-play from a known state.</summary>
        private void BeginSimulatingAt(Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel, Team? lastTouch)
        {
            SetSimulating(true);
            transform.SetPositionAndRotation(pos, rot);
            body.position = pos;
            body.rotation = rot;
            body.linearVelocity = vel;
            body.angularVelocity = angVel;
            ball.BeginSimulatingFrom(vel, lastTouch);
            inOtherLaneSince = -1f;
            pendingTeleport = false;

            // A start is not a path: the segment from wherever the last simulation ended to here crossed
            // nothing, whatever the goal test would say about it.
            goalHeld = false;
            hasPrevSim = false;
        }

        /// <summary>Stops simulating; the ball is drawn from <paramref name="seed"/> on until the other
        /// machine's samples arrive, if one is given.</summary>
        private void StopSimulating(BallSample? seed)
        {
            ball.EndSimulating();
            SetSimulating(false);
            inOtherLaneSince = -1f;

            if (seed.HasValue)
            {
                stream.Seed(seed.Value);
            }
        }

        /// <summary>This machine's ball as a sample, before anything changes its body.</summary>
        private BallSample Snapshot(double time)
        {
            return new BallSample
            {
                Time = time,
                Pos = body.position,
                Vel = body.isKinematic ? Vector3.zero : body.linearVelocity,
                AngVel = body.isKinematic ? Vector3.zero : body.angularVelocity,
                Rot = body.rotation,
                Epoch = latestEpoch,
                AuthorityTeam = (byte)Me,
                LastTouch = BallSample.Encode(ball.LastTouchTeam),
            };
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

        /// <summary>Only a ball simulated here is "placed" here: the drawn copy is reset too when the
        /// mirrored MatchManager resets, and flagging that would make this machine's next real sample a
        /// spurious snap.</summary>
        private void OnTeleported()
        {
            if (simulating)
            {
                pendingTeleport = true;
            }
        }

        // ── sending, and handing off ───────────────────────────────────────────────────────────────

        /// <summary>
        /// The simulating machine streams the ball every physics step, stamped with the step's own
        /// time on the shared clock — so several steps run back to back at the start of one frame keep
        /// their true spacing on the other side — then checks whether the ball has left its lanes.
        /// </summary>
        private void FixedUpdate()
        {
            if (!IsSpawned || !simulating)
            {
                return;
            }

            // Until the shared clock is known this machine cannot say when anything happened.
            if (!NetClock.Synced)
            {
                return;
            }

            // A goal this machine's OWN simulation has just let in — the guest's, against Blue. The goal
            // triggers are off here and the goal back is not solid, so nothing stopped the ball: it ran
            // out of the open back and off the table for the whole round trip it takes the host to judge
            // it and answer, on both screens, which is the ball seen behind the goal. Held on the line
            // instead, and still sent, so the host judges the same crossing it always did.
            Vector3 here = body.position;
            if (!IsServer && LaneMode && !goalHeld && hasPrevSim && !pendingTeleport && !ball.IsBeingCarried &&
                match != null && match.PlayLive && goalJudge != null &&
                goalJudge.Crossed(prevSimPos, here, out _))
            {
                goalHeld = true;
                goalHeldAt = Time.unscaledTime;
                statGoalHolds++;
            }

            prevSimPos = here;
            hasPrevSim = true;

            if (goalHeld)
            {
                if (Time.unscaledTime - goalHeldAt > GoalHoldMaxSeconds)
                {
                    goalHeld = false;
                }
                else
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            var flags = BallSampleFlags.None;
            if (pendingTeleport) flags |= BallSampleFlags.Teleport;
            if (ball.IsBeingCarried) flags |= BallSampleFlags.Carried;
            if (provisional) flags |= BallSampleFlags.Provisional;
            if (goalHeld) flags |= BallSampleFlags.GoalHold;
            if (Time.unscaledTime < termFlagsUntil) flags |= termFlags;
            pendingTeleport = false;

            BallSample sample = Snapshot(NetClock.SharedFixedNow);
            sample.Flags = flags;
            BallSampleRpc(sample);

            // Never hand over a ball sitting in a goal the host has yet to rule on.
            if (LaneMode && !provisional && !goalHeld)
            {
                CheckHandoff(sample);
            }
        }

        /// <summary>
        /// MatchManager has just put the ball back (a goal, a restart). On a machine that is only DRAWING
        /// it, the drawn ball is cleared rather than left to be re-placed by the stale projection; the
        /// next sample of the new term then draws it where it really is. A simulating machine is left to
        /// the term's own start.
        /// </summary>
        private void OnBallPlacedByMatch(Team scorer) => ClearAfterPlacement();

        private void OnBallPlacedByMatch() => ClearAfterPlacement();

        private void ClearAfterPlacement()
        {
            goalHeld = false;
            hasPrevSim = false;

            if (!simulating && stream != null)
            {
                stream.Reset();
            }
        }

        /// <summary>
        /// Hands the ball to the other player once it is a little way into their lane, has stayed there
        /// for a moment, and nothing here has touched it for a moment — mid-gap, where no figure on
        /// either side can reach it, so the trip it takes to arrive changes nothing.
        /// </summary>
        private void CheckHandoff(BallSample current)
        {
            if (match == null || !match.PlayLive || ball.IsBeingCarried)
            {
                inOtherLaneSince = -1f;
                claimPending = false;
                return;
            }

            // A figure of this machine's own still on the ball counts as contact even before the
            // collision registers a touch — that touch is this player's to play, here. Wider than the
            // claim distance, so a ball just claimed is not handed straight back while the claiming
            // figure hovers at the edge of it, to be claimed again a moment later.
            bool ownContact = Time.time - ball.LastTouchTime < ownContactDwell
                              || RodWithin(body.position, claimSkin * 1.5f, Me) != null;

            // The other player's figure is on the ball: let it go the moment ours is off it.
            if (claimPending)
            {
                if (Time.unscaledTime > claimDeadline)
                {
                    claimPending = false;
                }
                else if (!ownContact)
                {
                    claimPending = false;
                    HandOff(Other, current);
                    return;
                }
            }

            Team owner = lanes.OwnerAt(body.position, out float depth);
            if (owner == Me || depth < laneHysteresis || ownContact)
            {
                inOtherLaneSince = -1f;
                return;
            }

            float now = Time.fixedUnscaledTime;
            if (inOtherLaneSince < 0f)
            {
                inOtherLaneSince = now;
                return;
            }

            if (now - inOtherLaneSince >= handoffDwell)
            {
                HandOff(owner, current);
            }
        }

        private void HandOff(Team to, BallSample current)
        {
            claimPending = false;

            BallSample handoff = current;
            handoff.Epoch = latestEpoch + 1;
            handoff.AuthorityTeam = (byte)to;
            handoff.Flags = BallSampleFlags.Handoff;

            latestEpoch = handoff.Epoch;
            latestAuthority = to;
            latestEpochSampleSeen = false;

            StopSimulating(handoff);

            if (IsServer)
            {
                // The guest's real path starts where the host's ended; the silence clock starts now.
                prevGuestSample = handoff;
                hasPrevGuestSample = true;
                lastGuestSampleRealtime = Time.unscaledTime;
                validator.Reset();
            }

            BallHandoffRpc(handoff);
            statHandoffs++;
        }

        /// <summary>
        /// The other player's figure is on the ball this machine is simulating, in the band where both
        /// teams' figures reach. Handed over as soon as this machine's own figures are off it (see
        /// <see cref="CheckHandoff"/>), through the ordinary hand over, so the claimant's provisional
        /// start is confirmed exactly as a lane crossing's would be. Reliable: one per claim.
        /// </summary>
        [Rpc(SendTo.NotMe)]
        private void ClaimRpc(uint epoch, RpcParams rpcParams = default)
        {
            if (!LaneMode || !FromOtherPlayer(rpcParams) || !simulating || provisional || epoch != latestEpoch + 1)
            {
                return;
            }

            // Only where the claimant's figures can actually reach. Anywhere deeper is a claim no rod
            // could make, so a modified client cannot pull the ball across the table with it.
            if (lanes.OwnerAt(body.position, out float depth) == Me && depth > ClaimBand)
            {
                return;
            }

            claimPending = true;
            claimDeadline = Time.unscaledTime + claimWindow;
        }

        /// <summary>
        /// Unreliable: a lost sample is simply skipped by the curve, whereas a reliable one would hold
        /// up every sample behind it until it was resent — the stall that used to show as a freeze
        /// followed by a jump. NotMe, so the same call works whichever machine is simulating.
        /// </summary>
        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        private void BallSampleRpc(BallSample s, RpcParams rpcParams = default)
        {
            // Until the shared clock is known, arrival times cannot be compared with sample times.
            if (stream == null || !NetClock.Synced || !FromOtherPlayer(rpcParams))
            {
                return;
            }

            double now = NetClock.SharedNow;

            if (!LaneMode)
            {
                if (!simulating && s.Epoch >= latestEpoch)
                {
                    stream.Add(s, now);
                }

                return;
            }

            // A term the host forced or reset outranks whatever the guest was doing, even if the
            // reliable message saying so has not landed yet.
            if (!IsServer && s.Epoch > latestEpoch &&
                (s.Has(BallSampleFlags.Forced) || s.Has(BallSampleFlags.Reset)))
            {
                ApplyHostTerm(s.Epoch, s.Has(BallSampleFlags.Reset));
            }

            // While starting provisionally, the old simulator's samples are still drawn from (in case
            // of an abort) — and are the evidence for one: a late sample showing the ball NOT in this
            // player's lane means it never came, so the provisional start is cancelled.
            if (provisional && s.Epoch == provisionalFromEpoch)
            {
                // Still proof of life: an abort hands the host straight back to watching the guest.
                if (IsServer) lastGuestSampleRealtime = Time.unscaledTime;

                stream.Add(s, now);

                // Not for a touch claim: the ball is MEANT to be in the other lane then, and the claim
                // is settled by the hand over arriving or by the timeout in Update.
                if (!provisionalClaim && !s.Has(BallSampleFlags.Handoff) && s.Time > provisionalStart + abortGrace &&
                    lanes.OwnerAt(s.Pos, out _) != Me)
                {
                    AbortProvisional();
                }

                return;
            }

            // Only the current term's simulator is drawn, and never while simulating here — a newer
            // term from the other machine is their provisional start, which counts once handed over.
            if (simulating || s.Epoch != latestEpoch)
            {
                return;
            }

            if (IsServer && !WatchGuestSample(s))
            {
                return;
            }

            if (stream.Add(s, now))
            {
                latestEpochSampleSeen = true;
            }
        }

        /// <summary>
        /// The hand over: reliable, because exactly one of them is sent per term and losing it would
        /// leave nobody simulating.
        /// </summary>
        [Rpc(SendTo.NotMe)]
        private void BallHandoffRpc(BallSample s, RpcParams rpcParams = default)
        {
            if (!LaneMode || stream == null || !FromOtherPlayer(rpcParams) || s.AuthorityTeam != (byte)Me)
            {
                return;
            }

            if (IsServer)
            {
                // Only the guest's own term can be handed back, and only to the very next one.
                bool expected = (latestAuthority == Team.Blue && s.Epoch == latestEpoch + 1)
                                || (provisional && s.Epoch == latestEpoch);
                if (!expected)
                {
                    return;
                }

                if (!tableBounds.Contains(s.Pos))
                {
                    HostTakeTerm(BallSampleFlags.Forced, "hand over placed the ball off the table");
                    return;
                }

                s.Vel = Vector3.ClampMagnitude(s.Vel, ball.MaxSpeed);
            }

            double now = NetClock.SharedNow;

            if (provisional && s.Epoch == latestEpoch)
            {
                ConfirmProvisional(s, now);
                return;
            }

            if (!simulating && s.Epoch > latestEpoch)
            {
                latestEpoch = s.Epoch;
                latestAuthority = Me;
                latestEpochSampleSeen = true;

                stream.ProjectState(s, (float)(now - s.Time), out Vector3 p, out Vector3 v);
                BeginSimulatingAt(p, s.Rot, v, s.AngVel, s.LastTouchTeam);
                statTakeovers++;
            }
        }

        // ── provisional start ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts simulating the moment the ball THIS player sees enters their own lane, rather than
        /// waiting a trip for the hand over. Without it, the first touch after the ball crosses into a
        /// player's lane — which is most touches, since the lane boundary is barely outside a figure's
        /// reach — would wait for the network.
        /// </summary>
        private void TryProvisional(double now, float horizon)
        {
            if (!CanStartProvisional(now, out BallSample newest))
            {
                return;
            }

            if (!stream.StateAt(now, horizon, out Vector3 p, out Vector3 v, out Quaternion r))
            {
                return;
            }

            if (lanes.OwnerAt(p, out float depth) != Me || depth < laneHysteresis)
            {
                return;
            }

            // Only on a crossing that is really happening. The drawn ball is a guess carried forward at
            // constant speed; a ball slowing at the boundary, or stopped there by the other player's
            // figure, crosses in the guess and never in fact. Either the real ball is already over the
            // line, or it is heading over it too fast for a slowdown to stop it.
            if (lanes.OwnerAt(newest.Pos, out _) != Me)
            {
                Vector3 axis = lanes.Axis;
                float towardMe = Vector3.Dot(newest.Vel, axis) * Mathf.Sign(Vector3.Dot(p - newest.Pos, axis));
                if (towardMe < provisionalMinSpeed)
                {
                    return;
                }
            }

            // An opponent's figure on the ball as drawn is about to act on it on their phone, and their
            // samples will show the ball never came: starting now is the start that gets aborted, and an
            // abort pulls the ball back after this player has watched it arrive. Wait for that touch to
            // finish — the hand over follows it.
            if (RodWithin(transform.position, claimSkin, Other) != null)
            {
                return;
            }

            StartProvisional(now, p, v, r, newest, false);
        }

        /// <summary>
        /// A touch claim: one of this player's own figures is on the ball they are only drawing, in the
        /// band where both teams' figures reach past the lane boundary. Without it that touch was judged
        /// on the other phone against a copy of the figure one trip old, and came back a full round trip
        /// late — the ball sat still under the player's own foot, then jumped. Instead this machine starts
        /// simulating at once, as for a lane crossing, and asks the other to hand over
        /// (<see cref="ClaimRpc"/>), which it does as soon as its own figures are off the ball.
        /// </summary>
        private void TryClaim(double now, float horizon)
        {
            // A refused claim means the other player's figure is holding this ball too. Claiming again
            // while this player's figure is still on it would only be refused again — and every refusal
            // shows as the ball running a little way on this screen, then gliding back. Re-arm once the
            // figure has clearly left the ball, so each touch gets one claim.
            if (!claimArmed)
            {
                if (RodWithin(transform.position, claimSkin * 2f, Me) != null)
                {
                    return;
                }

                claimArmed = true;
            }

            if (Time.unscaledTime < claimCooldownUntil ||
                !CanStartProvisional(now, out BallSample newest, allowUnseen: true))
            {
                return;
            }

            // In this player's own lane the ordinary provisional start decides; deep in the other
            // lane no figure of theirs can reach.
            if (lanes.OwnerAt(transform.position, out float depth) != Me && depth > ClaimBand)
            {
                return;
            }

            // Tested against the ball as drawn, which is the ball the player is aiming at. Not when an
            // opponent's figure is on it too: the simulating machine would keep the ball for its own
            // touch, the claim would time out, and the ball would snap back to where it was.
            if (RodWithin(transform.position, claimSkin, Me) == null ||
                RodWithin(transform.position, claimSkin, Other) != null)
            {
                return;
            }

            if (!stream.StateAt(now, horizon, out Vector3 p, out Vector3 v, out Quaternion r))
            {
                return;
            }

            StartProvisional(now, p, v, r, newest, true);
            ClaimRpc(latestEpoch);
            statClaims++;
        }

        /// <param name="allowUnseen">
        /// For a touch claim: also allow it before the other machine's first sample of this term has
        /// arrived. After this machine hands the ball over, that is a full round trip in which the stream
        /// is only this machine's OWN last state projected on, and the old rule blocked every touch in it.
        /// A claim is a figure actually on the ball, which is evidence enough; a lane crossing is only a
        /// prediction and still waits.
        /// </param>
        private bool CanStartProvisional(double now, out BallSample newest, bool allowUnseen = false)
        {
            newest = default;

            if (provisional || simulating || latestAuthority == Me || (!latestEpochSampleSeen && !allowUnseen))
            {
                return false;
            }

            if (match == null || !match.PlayLive || !stream.HasSamples)
            {
                return false;
            }

            // A scripted carry, or a stream gone quiet: nothing safe to start from.
            newest = stream.Newest;
            return !newest.Has(BallSampleFlags.Carried) && now - newest.Time <= maxPresentHorizon;
        }

        private void StartProvisional(double now, Vector3 p, Vector3 v, Quaternion r, in BallSample newest, bool claim)
        {
            provisionalFromEpoch = latestEpoch;
            latestEpoch++;
            latestAuthority = Me;
            provisional = true;
            provisionalClaim = claim;
            provisionalStart = now;
            provisionalStartRealtime = Time.unscaledTime;
            provisionalPos = p;
            provisionalVel = v;

            BeginSimulatingAt(p, r, v, newest.AngVel, newest.LastTouchTeam);
            statProvisional++;
        }

        /// <summary>
        /// The hand over arrived for a term this machine already started. If the old simulator's state
        /// agrees with where this one started, carry on — the player's touches since then stand.
        /// Otherwise something touched the ball on the other side just before it crossed, and this
        /// machine rebases onto the true state.
        /// </summary>
        private void ConfirmProvisional(BallSample handoff, double now)
        {
            if (provisionalClaim)
            {
                claimRefusals = 0;
            }

            provisional = false;
            provisionalClaim = false;
            statConfirmed++;

            // Compare at the same moment: whichever of the two is earlier is carried to the later.
            Vector3 theirs, theirsVel, mine, mineVel;
            if (handoff.Time <= provisionalStart)
            {
                stream.ProjectState(handoff, (float)(provisionalStart - handoff.Time), out theirs, out theirsVel);
                mine = provisionalPos;
                mineVel = provisionalVel;
            }
            else
            {
                var start = new BallSample { Time = provisionalStart, Pos = provisionalPos, Vel = provisionalVel };
                stream.ProjectState(start, (float)(handoff.Time - provisionalStart), out mine, out mineVel);
                theirs = handoff.Pos;
                theirsVel = handoff.Vel;
            }

            if ((theirs - mine).magnitude <= rebaseDistance && (theirsVel - mineVel).magnitude <= rebaseSpeed)
            {
                return;
            }

            stream.ProjectState(handoff, (float)(now - handoff.Time), out Vector3 p, out Vector3 v);
            BeginSimulatingAt(p, handoff.Rot, v, handoff.AngVel, handoff.LastTouchTeam);
            statRebases++;
        }

        /// <summary>The provisional start was wrong — the ball never came. Back to drawing.</summary>
        private void AbortProvisional()
        {
            if (provisionalClaim)
            {
                // One claim per touch, and a longer wait each time in a row the other side says no
                // (0.3, 0.6, 1.2 s, capped at 2 s), for the case where the figure never quite leaves.
                claimArmed = false;
                claimRefusals++;
                claimCooldownUntil = Time.unscaledTime +
                                     Mathf.Min(2f, claimCooldown * Mathf.Pow(2f, claimRefusals - 1));
            }

            provisional = false;
            provisionalClaim = false;
            latestEpoch = provisionalFromEpoch;
            latestAuthority = Other;
            latestEpochSampleSeen = true;

            Vector3 shown = body.position;
            StopSimulating(null); // the stream still holds the other machine's samples

            // Glide back to the other machine's ball from where this one had got to, instead of
            // snapping there — the snap was the "jumps back after a touch".
            stream.BlendFrom(shown, NetClock.SharedNow, PresentHorizon());
            if (IsServer) lastGuestSampleRealtime = Time.unscaledTime;
            statAborts++;
        }

        // ── the host as referee ────────────────────────────────────────────────────────────────────

        private void OnHostGoal(Team scorer) => HostTakeTerm(BallSampleFlags.Reset, null);

        private void OnHostReset() => HostTakeTerm(BallSampleFlags.Reset, null);

        /// <summary>
        /// The host takes the ball for a new term, overriding whatever the guest is doing: at every goal
        /// and kick-off, and when the guest goes silent or fails validation. Two epochs on, so the term
        /// outranks any provisional start the guest may have made in the meantime.
        /// </summary>
        private void HostTakeTerm(BallSampleFlags reason, string why)
        {
            if (!IsServer || !LaneMode || stream == null)
            {
                return;
            }

            latestEpoch += 2;
            latestAuthority = Team.Red;
            latestEpochSampleSeen = false;
            provisional = false;
            provisionalClaim = false;
            claimPending = false;
            termFlags = reason;
            termFlagsUntil = Time.unscaledTime + TermFlagSeconds;

            if (!simulating)
            {
                double now = NetClock.SharedNow;
                if (reason == BallSampleFlags.Reset)
                {
                    // A goal, restart or kick-off: MatchManager has ALREADY placed the ball (ResetBall)
                    // before raising the event that brought us here. Start from where it was put. The
                    // stream's guess is the guest's ball carried a quarter-second on — after a goal that
                    // is straight out through the open back of the goal, and the ball was simulated
                    // there, on both screens, until the next kick-off.
                    BeginSimulatingAt(body.position, body.rotation, Vector3.zero, Vector3.zero, null);
                    pendingTeleport = true; // the guest snaps to the placed ball rather than sliding to it
                }
                else if (stream.HasSamples && stream.StateAt(now, PresentHorizon(), out Vector3 p, out Vector3 v,
                                                             out Quaternion r))
                {
                    BeginSimulatingAt(p, r, v, stream.Newest.AngVel, stream.Newest.LastTouchTeam);
                }
                else
                {
                    BeginSimulatingAt(body.position, body.rotation, Vector3.zero, Vector3.zero, null);
                }
            }

            validator.Reset();
            hasPrevGuestSample = false;
            ForceAuthorityRpc(latestEpoch, reason == BallSampleFlags.Reset);

            if (reason == BallSampleFlags.Forced)
            {
                statForced++;
                lastForceReason = why ?? string.Empty;
                Debug.LogWarning($"[BallAuthority] host took the ball back: {why}");
            }
        }

        /// <param name="reset">The term is a goal, restart or kick-off: the ball has been PLACED, so there
        /// is nothing of this machine's to carry on from.</param>
        [Rpc(SendTo.NotServer)]
        private void ForceAuthorityRpc(uint epoch, bool reset)
        {
            ApplyHostTerm(epoch, reset);
        }

        /// <summary>Guest: the host has started a new term. Whatever this machine was simulating stops.</summary>
        private void ApplyHostTerm(uint epoch, bool reset)
        {
            // The host's terms always skip two epochs, so they outrank a provisional start here too.
            if (!LaneMode || epoch <= latestEpoch)
            {
                return;
            }

            bool wasSimulating = simulating;
            BallSample seed = Snapshot(NetClock.SharedNow);

            provisional = false;
            provisionalClaim = false;
            claimPending = false;
            goalHeld = false;
            latestEpoch = epoch;
            latestAuthority = Team.Red;
            latestEpochSampleSeen = false;

            if (reset)
            {
                // A placed ball: this machine's own picture of it — a simulation that may have run on out
                // of the goal, or a projection of the host's — is worth nothing now. Seeding the stream
                // with it was the guest-side ball-behind-the-goal: the seed was stamped "now", and the
                // stream drops any sample not newer than the one it holds, so the host's reset samples,
                // stamped a one-way trip earlier, were all thrown away and the guess was drawn until the
                // next kick-off. Cleared instead; the placed ball stays where MatchManager put it until
                // the first sample of the new term draws it.
                if (wasSimulating)
                {
                    StopSimulating(null);
                }

                stream.Reset();
                return;
            }

            if (wasSimulating)
            {
                // Forced rather than reset: the ball is still in play and the seed bridges to the host's
                // first samples. Stamped well in the past so that none of them can be older than it.
                seed.Time = NetClock.SharedNow - 1.0;
                StopSimulating(seed);
            }
        }

        /// <summary>
        /// Host: checks one sample of the guest's simulation and referees it. False when the sample
        /// ended the guest's term (a goal, or a failed check) and should not be drawn.
        /// </summary>
        private bool WatchGuestSample(in BallSample s)
        {
            lastGuestSampleRealtime = Time.unscaledTime;

            if (validator.Check(s, NetClock.SharedNow))
            {
                HostTakeTerm(BallSampleFlags.Forced, validator.LastReason);
                return false;
            }

            if (hasPrevGuestSample && !s.Has(BallSampleFlags.Teleport) && match != null && match.PlayLive &&
                goalJudge.Crossed(prevGuestSample.Pos, s.Pos, out Team scorer))
            {
                hasPrevGuestSample = false;
                statGoalsJudged++;

                // GoalScored takes the ball back for the kick-off (OnHostGoal) and the director relays
                // the goal to the guest, exactly as for a goal on the host's own trigger.
                match.ScoreGoal(scorer);
                return false;
            }

            prevGuestSample = s;
            hasPrevGuestSample = true;
            return true;
        }

        private bool FromOtherPlayer(RpcParams rpcParams)
        {
            bool fromHost = rpcParams.Receive.SenderClientId == NetworkManager.ServerClientId;
            return IsServer ? !fromHost : fromHost;
        }

        /// <summary>The playing area plus the goal mouths, generously — for spotting a ball claimed to be
        /// somewhere it cannot be, not for refereeing.</summary>
        private Bounds MeasureTable(TableReferences table)
        {
            var b = new Bounds(transform.position, Vector3.zero);
            if (table != null)
            {
                foreach (RodController rod in table.RedRods) Encapsulate(ref b, rod);
                foreach (RodController rod in table.BlueRods) Encapsulate(ref b, rod);
            }

            b.Encapsulate(transform.position + Vector3.down * 0.15f);
            b.Encapsulate(transform.position + Vector3.up * 0.3f);
            b.Expand(new Vector3(0.5f, 0f, 0.5f));
            return b;
        }

        private static void Encapsulate(ref Bounds b, RodController rod)
        {
            if (rod == null) return;
            foreach (Collider c in rod.GetComponentsInChildren<Collider>())
            {
                if (c != null) b.Encapsulate(c.bounds);
            }
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
            double renderTime;
            float horizon;

            if (LaneMode)
            {
                // The present: the ball where the other player's simulation has it now, as far as can
                // be told. A provisional start begins from exactly this picture.
                renderTime = now;
                horizon = PresentHorizon();
            }
            else
            {
                stream.UpdateDelay(now);
                renderTime = now - stream.RenderDelay;
                horizon = maxExtrapolation;
            }

            if (stream.Sample(renderTime, Time.unscaledDeltaTime, horizon, out Vector3 pos, out _,
                              out Quaternion rot))
            {
                // Both the transform (what is drawn) and the kinematic body (what physics queries such
                // as the own-kick touch test see) — physics does not pick up transform writes until its
                // next step, and the touch test runs this frame.
                transform.SetPositionAndRotation(pos, rot);
                body.position = pos;
                body.rotation = rot;
            }

            if (LaneMode)
            {
                TryProvisional(now, horizon);
                TryClaim(now, horizon);
            }

            if (!simulating)
            {
                PredictOwnImpact();
            }
        }

        private void Update()
        {
            if (!IsSpawned || stream == null)
            {
                return;
            }

            statFrames++;

            if (LaneMode)
            {
                // A claim is answered within one round trip plus however long the other player's own
                // figure stays on the ball; a lane crossing waits for the other side's hand over.
                float limit = provisionalClaim
                    ? Mathf.Max(0.15f, RttSeconds() + claimWindow + 0.05f)
                    : Mathf.Max(0.3f, RttSeconds() * 2f + 0.1f);
                if (simulating && provisional && Time.unscaledTime - provisionalStartRealtime > limit)
                {
                    AbortProvisional();
                }

                // The wait for the guest's FIRST sample after a hand over is a whole round trip — the
                // hand over there, the sample back — and one resend of the reliable hand over adds a
                // retransmission timeout on top. Counted from the hand over, a flat half-second took
                // the ball back from a perfectly healthy guest on a slow link; so the allowance grows
                // with the trip the link is really taking.
                if (IsServer && !simulating && latestAuthority == Team.Blue &&
                    Time.unscaledTime - lastGuestSampleRealtime > silenceTakeover + RttSeconds() * 1.5f)
                {
                    HostTakeTerm(BallSampleFlags.Forced, "guest went silent");
                }
            }

            LogStats();
        }

        /// <summary>
        /// How far ahead the drawn ball may be projected. It used to be a one-way trip plus 0.1 s, which
        /// is shorter than the gaps a phone link really has: Wi-Fi power saving and Relay deliver samples
        /// in bursts, and past the horizon the drawn ball FROZE until the next burst, then jumped — a fast
        /// ball showed as a few still positions instead of motion. The projection stops at rods and
        /// bounces off rails, so carrying it the whole way is the better guess.
        /// </summary>
        private float PresentHorizon() => maxPresentHorizon;

        private float RttSeconds()
        {
            if (NetClock.Rtt > 0) return (float)NetClock.Rtt;
            int ms = TransportRttMs();
            return ms > 0 ? ms / 1000f : 0f;
        }

        /// <summary>TEMPORARY: stream and authority health every two seconds, on both machines.</summary>
        private void LogStats()
        {
            if (!logStreamStats || Time.unscaledTime < nextStatsLog)
            {
                return;
            }

            nextStatsLog = Time.unscaledTime + 2f;

            // rtt = round trip measured by our own messages, which wait for a frame at each end;
            // netRtt = the transport's own measurement, without that. The gap is frame-rate cost.
            // fixDrift: how far the physics clock the samples are stamped with has fallen behind the
            // clock they are compared against. Should sit near zero; if it grows after pauses or hitches,
            // every sample reads as stale and takeovers silently stop.
            double fixDrift = (Time.unscaledTimeAsDouble - Time.fixedUnscaledTimeAsDouble) * 1000.0;
            string timing = $"fps={statFrames / 2f:F0} rtt={NetClock.Rtt * 1000.0:F0}ms netRtt={TransportRttMs()}ms " +
                            $"fixDrift={fixDrift:F0}ms " +
                            $"ver={OnlineSession.NetcodeVersion}/{OnlineMatchDirector.CardFor(Other).Protocol}";

            if (LaneMode)
            {
                Debug.Log($"[BallAuthority] me={Me} {(simulating ? (provisional ? "PROVISIONAL" : "SIMULATING") : "drawing")} " +
                          $"term={latestEpoch} handoffs={statHandoffs} takeovers={statTakeovers} " +
                          $"provisional={statProvisional} confirmed={statConfirmed} rebases={statRebases} " +
                          $"aborts={statAborts} forced={statForced} goalsJudged={statGoalsJudged} " +
                          $"claims={statClaims} goalHolds={statGoalHolds} frozen={stream.ClampedFrames} " +
                          $"maxGap={stream.MaxGap * 1000.0:F0}ms {timing}" +
                          (string.IsNullOrEmpty(lastForceReason) ? string.Empty : $" lastForce='{lastForceReason}'"));
            }
            else if (stream.Frames > 0)
            {
                // fastest = one-way trip (plus clock error); buffer = what the smoothing adds above it.
                Debug.Log($"[BallStream] starved={stream.StarvedFrames} " +
                          $"received={stream.Received} lateDrops={stream.LateDrops} " +
                          $"depth={stream.DepthSum / stream.Frames * 1000.0:F0}ms " +
                          $"delay={stream.RenderDelay * 1000.0:F0}ms " +
                          $"fastest={stream.FastestLateness * 1000.0:F0}ms " +
                          $"buffer={(stream.RenderDelay - stream.FastestLateness) * 1000.0:F0}ms {timing}");
            }
            else if (IsServer)
            {
                Debug.Log($"[BallHost] {timing}");
            }

            statFrames = 0;
            stream.Frames = stream.StarvedFrames = stream.Received = stream.LateDrops = stream.ClampedFrames = 0;
            stream.DepthSum = stream.MaxGap = 0;
        }

        /// <summary>The transport's own round-trip measurement to the other player, in ms, or -1.</summary>
        private int TransportRttMs()
        {
            if (NetworkManager == null ||
                !(NetworkManager.NetworkConfig.NetworkTransport is Unity.Netcode.Transports.UTP.UnityTransport transport))
            {
                return -1;
            }

            ulong other = NetworkManager.ServerClientId;
            if (IsServer)
            {
                other = ulong.MaxValue;
                foreach (ulong id in NetworkManager.ConnectedClientsIds)
                {
                    if (id != NetworkManager.ServerClientId)
                    {
                        other = id;
                        break;
                    }
                }

                if (other == ulong.MaxValue) return -1;
            }

            return (int)transport.GetCurrentRtt(other);
        }

        // ── sounds ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sounds this player's own kicks the moment they look like they happen while the OTHER machine
        /// is simulating, instead of a round trip later. With lane authority that is rare — a player's
        /// own touches are normally simulated here and sound for real — but it still covers the edge of
        /// a lane, and the whole match with lane authority off.
        ///
        /// It plays on the leading edge of this player's rod meeting the drawn ball, matching the
        /// simulating machine, which raises its impact from OnCollisionEnter and so also fires once per
        /// contact. Every guess is recorded, and <see cref="BallImpactRpc"/> cancels one relayed impact
        /// against it, so a correct prediction is heard exactly once and a wrong one never.
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
            // Only this player's rods. The opponent's hits are their action, not ours, and guessing at
            // them would just add wrong sounds to ones that already arrive correctly.
            RodController rod = RodWithin(transform.position, TouchSkin, Me);
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

        /// <summary>A rod of <paramref name="team"/> within <paramref name="skin"/> of a ball centred at
        /// <paramref name="centre"/>, or null.</summary>
        private RodController RodWithin(Vector3 centre, float skin, Team team)
        {
            int count = Physics.OverlapSphereNonAlloc(centre, ballRadius + skin, touchResults, ~0,
                                                      QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = touchResults[i];

                // The ball's own collider is always in this list and is never a contact.
                if (hit == null || hit.attachedRigidbody == body)
                {
                    continue;
                }

                RodController candidate = hit.GetComponentInParent<RodController>();
                if (candidate != null && candidate.Team == team)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Drops guesses never confirmed, so they cannot silence a later real hit.</summary>
        private void ExpirePredictions()
        {
            float lifetime = Mathf.Max(MinPredictionLifetime, RttSeconds() * PredictionLifetimeRtts);
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
            if (!IsSpawned || !simulating)
            {
                return;
            }

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
                // This may confirm a hit this player already sounded for themselves — see
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
