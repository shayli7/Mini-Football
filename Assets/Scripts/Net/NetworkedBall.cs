using Unity.Netcode;
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
    /// </summary>
    [RequireComponent(typeof(BallController))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class NetworkedBall : NetworkBehaviour
    {
        private BallController ball;
        private Rigidbody body;

        private void Awake()
        {
            ball = GetComponent<BallController>();
            body = GetComponent<Rigidbody>();
        }

        public override void OnNetworkSpawn()
        {
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
        }

        public override void OnNetworkDespawn()
        {
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
            ball.enabled = true;
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
                GameSfx.PlayBallHit(strength);
            }
            else
            {
                GameSfx.PlayWallThud(strength);
            }
        }
    }
}
