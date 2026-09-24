using System;
using Unity.Netcode;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// Sends one rod's pose to the other player, and drives it from theirs.
    ///
    /// Deliberately NOT a NetworkTransform. A rod has exactly two degrees of freedom — how far it has
    /// slid and how far it has spun — and <see cref="RodController"/> rebuilds the whole transform
    /// from those two numbers plus a rest pose baked from the scene. Both machines load the same
    /// scene, so both hold the same rest pose, and sending the two floats reproduces the rod exactly.
    /// A NetworkTransform would instead ship a position and a rotation that are already derived from
    /// them: three times the bandwidth, and its interpolation would fight
    /// <see cref="RodController"/>'s own ApplyPose every frame for control of the same transform.
    ///
    /// A remote rod carries no spin velocity and no input, so its Tick does nothing but re-apply the
    /// pose we just set. How hard it is turning is a third number, sent with the pose rather than
    /// worked back out of it — the host's strike boost is keyed on
    /// <see cref="RodController.MeasuredSpinSpeed"/>, and letting the other machine derive that from
    /// a wrapped angle arriving in jumps got its SIGN wrong on exactly the hardest shots, which sent
    /// them back down the table at the player who took them.
    /// </summary>
    /// <remarks>
    /// Ordered ahead of everything else so Follow lands the incoming pose BEFORE
    /// <see cref="RodController"/>'s own FixedUpdate reads it and pushes it to PhysX. Both run on
    /// FixedUpdate now, and Unity does not order two components' FixedUpdate calls on its own — left
    /// to chance, half the physics steps would apply a pose one step stale, which is a jitter the
    /// ball feels as an inconsistent sweep.
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(RodController))]
    [DisallowMultipleComponent]
    public class NetworkedRod : NetworkBehaviour
    {
        /// <summary>Slide movement smaller than this is not worth a packet.</summary>
        private const float SlideEpsilon = 0.001f;

        /// <summary>Spin movement smaller than this, in degrees, is not worth a packet.</summary>
        private const float SpinEpsilon = 0.35f;

        /// <summary>
        /// How hard a remote rod chases the pose it was sent, per second. Exponential rather than a
        /// fixed speed on purpose: a rod can be whipped at 2200°/s, and any constant follow rate low
        /// enough to look smooth on a slow drag would be left behind by a flick.
        /// </summary>
        [Tooltip("Higher snaps the other player's rods to their true pose sooner, at the cost of " +
                 "showing more of the network's jitter. Lower is smoother but laggier.")]
        [SerializeField] private float followSharpness = 28f;

        private struct Pose : INetworkSerializable, IEquatable<Pose>
        {
            public float Slide01;
            public float SpinAngle;

            /// <summary>
            /// How fast the owner measured this rod turning, in degrees/second.
            ///
            /// Sent rather than worked out on the other side, and it is the third float that stops
            /// hard shots flying backwards. A wrapped angle cannot tell "forward past half a turn"
            /// from "backward" — see <see cref="RodController.SetMeasuredSpin"/> — and the ball takes
            /// the direction of its strike impulse from the sign of this number.
            /// </summary>
            public float SpinSpeed;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Slide01);
                serializer.SerializeValue(ref SpinAngle);
                serializer.SerializeValue(ref SpinSpeed);
            }

            public bool Equals(Pose other) =>
                Slide01.Equals(other.Slide01)
                && SpinAngle.Equals(other.SpinAngle)
                && SpinSpeed.Equals(other.SpinSpeed);
        }

        private readonly NetworkVariable<Pose> pose = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private RodController rod;

        private void Awake()
        {
            rod = GetComponent<RodController>();

            // Subscribed for the rod's whole lifetime rather than around spawn, so publishing can
            // never be left unhooked by an ownership change or a despawn/respawn. OnPoseApplied
            // checks IsSpawned and IsOwner itself, so an idle subscription costs nothing.
            rod.PoseApplied += OnPoseApplied;

            // A rod outlives whoever was holding it.
            //
            // Netcode's default is that objects a client owns are DESTROYED when that client
            // disconnects — it despawns them with destroy set, which for an in-scene object means the
            // GameObject is gone for the rest of the session. The guest owns the four Blue rods, so
            // the moment they left, half the table was deleted on the host's machine: no blue team in
            // the win screen behind the banner, none in the next online match, and none in a local
            // match against the AI afterwards either, because nothing puts a destroyed scene object
            // back. Set here rather than in the Inspector so no rod can be missed.
            var netObject = GetComponent<NetworkObject>();
            if (netObject != null)
            {
                netObject.DontDestroyWithOwner = true;
            }
        }

        /// <summary>
        /// Override rather than a plain OnDestroy: NetworkBehaviour does its own teardown here, and
        /// declaring a new method would hide it and quietly skip that.
        /// </summary>
        public override void OnDestroy()
        {
            if (rod != null)
            {
                rod.PoseApplied -= OnPoseApplied;
            }

            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            // Seed from where the rod actually is, so a rod that spawns mid-match does not lurch
            // from a default pose to its real one on the first update.
            if (IsOwner)
            {
                pose.Value = new Pose { Slide01 = rod.CurrentSlide01, SpinAngle = rod.CurrentSpinAngle };
            }
        }

        /// <summary>
        /// Follows on FixedUpdate rather than Update, because that is the clock RodController runs on
        /// once the rod has its kinematic Rigidbody. Follow used to run on Update: at 60 fps against a
        /// 100 Hz physics step, some physics steps saw a Follow call and some saw none, so the sweep a
        /// remote rod handed the ball varied step to step for no gameplay reason. On FixedUpdate, and
        /// with this component ordered ahead of RodController, an arriving pose reaches PhysX in the
        /// same step it landed.
        ///
        /// Publishing does NOT belong here — see <see cref="OnPoseApplied"/>.
        /// </summary>
        private void FixedUpdate()
        {
            if (IsSpawned && !IsOwner)
            {
                Follow(Time.fixedDeltaTime);
            }
        }

        /// <summary>
        /// Publishes off <see cref="RodController.PoseApplied"/> rather than this component's own
        /// FixedUpdate, because the two halves of this class need OPPOSITE orderings and the
        /// execution-order attribute can only grant one.
        ///
        /// Follow has to run before RodController's Tick. Publish has to run after it: ordered early,
        /// it samples CurrentSpinAngle before Tick has advanced it, so every packet leaves carrying
        /// the pose from one step ago. That is a free 10 ms of latency on the owner-to-opponent leg at
        /// 100 Hz, paid on every rod on every step, and it is invisible — the rod looks right on both
        /// screens, it is just consistently late.
        /// </summary>
        private void OnPoseApplied()
        {
            if (IsSpawned && IsOwner)
            {
                Publish();
            }
        }

        private void Publish()
        {
            float slide = rod.CurrentSlide01;
            float spin = rod.CurrentSpinAngle;
            float speed = rod.MeasuredSpinSpeed;
            Pose last = pose.Value;

            // A rod at rest is the common case — most rods, most of the time — and without this it
            // would still resend float noise on every tick of the match.
            bool moved = Mathf.Abs(slide - last.Slide01) >= SlideEpsilon
                         || Mathf.Abs(Mathf.DeltaAngle(last.SpinAngle, spin)) >= SpinEpsilon;

            // A rod that has come to a stop is worth one more packet even though it has not moved.
            // Without it the last pose sent stays on the wire carrying the speed of the swing that
            // ended, and the other machine goes on believing the rod is still turning at full tilt.
            bool stopping = last.SpinSpeed != 0f && Mathf.Abs(speed) < 1f;

            if (moved || stopping)
            {
                pose.Value = new Pose
                {
                    Slide01 = slide,
                    SpinAngle = spin,
                    SpinSpeed = Mathf.Abs(speed) < 1f ? 0f : speed
                };
            }
        }

        private void Follow(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            Pose target = pose.Value;
            float k = 1f - Mathf.Exp(-followSharpness * dt);

            rod.SetSlide01Immediate(Mathf.Lerp(rod.CurrentSlide01, target.Slide01, k));

            // Turn the rod at the reported rate first, then fold in whatever's left toward the exact
            // reported angle. Advancing by the signed speed (rather than only easing toward a wrapped
            // angle) keeps the host's rod moving the same way its owner turned it — LerpAngle alone
            // takes the shorter way round, which points a whip past half a turn backwards.
            float previousAngle = rod.CurrentSpinAngle;
            float extrapolated = previousAngle + target.SpinSpeed * dt;
            float newAngle = Mathf.LerpAngle(extrapolated, target.SpinAngle, k);
            rod.SetSpinAngle(newAngle);

            // The rod is being positioned outright, so any leftover velocity would be integrated on
            // top of it and push the rod past where its owner actually is.
            rod.SetSpinVelocity(0f);

            // Report the spin the rod ACTUALLY turned this step, not the raw number the last packet
            // carried. That distinction is the whole fix for the ball being struck by a rod that is
            // standing still: when a rod stops, its owner's measured spin decays over ~10 steps
            // (991 → 495 → 248 → …), and through that decay the rod moves too little to send a fresh
            // packet yet stays above the "stopped" packet's threshold — so the host holds the last
            // speed it heard and keeps the rod ARMED at a swing it is no longer making. A ball
            // rolling into it then gets shot by a motionless figure. Measuring the magnitude from the
            // rod's real motion here makes a still rod read as still (~0°/s, not armed), exactly as a
            // local rod does. The SIGN still comes from the packet: a whip past half a turn lands as
            // a short backwards step under LerpAngle, and the shot's direction keys off this sign.
            float appliedStep = Mathf.DeltaAngle(previousAngle, newAngle);
            float sign = Mathf.Approximately(target.SpinSpeed, 0f)
                ? Mathf.Sign(appliedStep)
                : Mathf.Sign(target.SpinSpeed);
            rod.SetMeasuredSpin(sign * Mathf.Abs(appliedStep) / dt);
        }

        /// <summary>
        /// Clears momentum when a rod changes hands, which happens at kick-off when the guest is
        /// handed their team. Without this, a rod that was mid-flick keeps coasting under its new
        /// owner and publishes that as genuine input.
        /// </summary>
        public override void OnGainedOwnership()
        {
            rod.SetSpinVelocity(0f);

            // Back to working its own spin out: this machine moves the angle itself now, in steps
            // small enough for the measurement to be sound.
            rod.ClearMeasuredSpin();

            pose.Value = new Pose
            {
                Slide01 = rod.CurrentSlide01,
                SpinAngle = rod.CurrentSpinAngle,
                SpinSpeed = 0f
            };
        }

        public override void OnLostOwnership()
        {
            rod.SetSpinVelocity(0f);
        }

        /// <summary>
        /// Hands the rod back to itself. Nobody is reporting its spin any more, so it has to go back
        /// to measuring its own — otherwise the local match after an online one plays with rods
        /// frozen at whatever the last packet said, and no shot the player takes has any power.
        /// </summary>
        public override void OnNetworkDespawn()
        {
            rod.SetSpinVelocity(0f);
            rod.ClearMeasuredSpin();
        }
    }
}
