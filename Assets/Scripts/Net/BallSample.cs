using System;
using Unity.Netcode;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>What a <see cref="BallSample"/> is telling the receiver beyond the ball's pose.</summary>
    [Flags]
    public enum BallSampleFlags : byte
    {
        None = 0,

        /// <summary>The ball was placed, not moved (a kick-off or reset). Snap to it; never draw a
        /// curve from where it was to where it is.</summary>
        Teleport = 1 << 0,

        /// <summary>BallController is carrying the ball back after a fall-through.</summary>
        Carried = 1 << 1,

        // Reserved for lane authority (Phase C) and the away rules (Phase B). Declared now so the
        // wire format does not change underneath them.
        Handoff = 1 << 2,
        Provisional = 1 << 3,
        Forced = 1 << 4,
        Reset = 1 << 5,
        GoalHold = 1 << 6,
    }

    /// <summary>
    /// One moment of the ball, as simulated by whichever machine is simulating it.
    ///
    /// Carries velocity as well as position because the receiver draws a CURVE between samples, not a
    /// straight line: with both ends' velocities known, a bounce off a rail between two samples is
    /// drawn as the sharp turn it was rather than a corner cut across the pitch.
    ///
    /// <see cref="Time"/> is on the shared clock (see <see cref="NetClock"/>), stamped with the physics
    /// step the state belongs to — so the receiver places the ball where it was at that moment, not
    /// whenever the packet happened to land, and uneven arrival over Relay stops showing as uneven
    /// motion.
    /// </summary>
    public struct BallSample : INetworkSerializable
    {
        public double Time;
        public Vector3 Pos;
        public Vector3 Vel;
        public Vector3 AngVel;
        public Quaternion Rot;

        /// <summary>Which simulation term this sample belongs to. Only ever grows; a receiver ignores
        /// samples from a term it has already moved past. Always 0 until authority can change hands.</summary>
        public uint Epoch;

        /// <summary>The team whose machine produced this sample (a <see cref="Team"/> as a byte).</summary>
        public byte AuthorityTeam;

        public BallSampleFlags Flags;

        public bool Has(BallSampleFlags flag) => (Flags & flag) != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Time);
            serializer.SerializeValue(ref Pos);
            serializer.SerializeValue(ref Vel);
            serializer.SerializeValue(ref AngVel);
            serializer.SerializeValue(ref Rot);
            serializer.SerializeValue(ref Epoch);
            serializer.SerializeValue(ref AuthorityTeam);

            byte flags = (byte)Flags;
            serializer.SerializeValue(ref flags);
            Flags = (BallSampleFlags)flags;
        }
    }
}
