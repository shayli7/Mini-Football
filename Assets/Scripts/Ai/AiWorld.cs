using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// One opponent rod as the AI is allowed to see it: where its line sits and where its figures
    /// currently stand. This is the same information a human reads off the table, and it is what
    /// lets defense judge a passing lane and attack judge which corner is open.
    /// </summary>
    public class OpponentRodView
    {
        public RodController Rod;
        public float OrderedDepth;
        public int FigureCount;
        public float[] RestLaterals;

        /// <summary>Where the figures stand right now, rest position plus however far the rod has slid.</summary>
        public void CurrentLaterals(List<float> into)
        {
            into.Clear();
            if (Rod == null)
            {
                return;
            }

            float slide = Rod.CurrentSlideMeters;
            for (int i = 0; i < RestLaterals.Length; i++)
            {
                into.Add(RestLaterals[i] + slide);
            }
        }
    }

    /// <summary>
    /// The table as it actually is, rebuilt once per frame and shared by all four rods.
    ///
    /// This is ground truth and nothing else. It deliberately carries no lag, no error and no
    /// guessing: each rod holds its OWN delayed, imperfect read of this in <see cref="RodAgent"/>,
    /// because a team that shares one belief also shares one mistake, and a player learns to
    /// exploit a single team-wide bias in about a minute.
    ///
    /// Axis convention, inherited from the rods themselves:
    ///   BarAxis   - along a rod. "Lateral" everywhere below.
    ///   LongAxis  - down the table, goal to goal. "Depth".
    ///   Ordered depth is depth multiplied by AttackSign, so it always increases toward the
    ///   opponent's goal whichever end this team defends.
    /// </summary>
    public class AiWorld
    {
        public Vector3 BarAxis = Vector3.forward;
        public Vector3 LongAxis = Vector3.right;
        public float AttackSign = 1f;

        public float Now;
        public float DeltaTime;

        /// <summary>Where the ball really is. Rods lag this themselves.</summary>
        public Vector3 BallPosition;
        public Vector3 BallVelocity;

        public float BallLateral;
        public float BallOrderedDepth;
        public float BallLateralVelocity;
        /// <summary>Ball speed toward the opponent's goal. Negative means it is coming at us.</summary>
        public float BallAdvanceVelocity;

        public float OwnGoalOrderedDepth;
        public float OpponentGoalOrderedDepth;
        public float GoalCentreLateral;
        public float GoalHalfWidth;

        /// <summary>Half the playable width along a bar, derived from how far the rods can reach.</summary>
        public float LateralHalfExtent = 0.3f;

        public float MinRodOrderedDepth;
        public float MaxRodOrderedDepth;

        /// <summary>Where the ball sits between our goal line (0) and our most advanced rod (1).</summary>
        public float ZoneProgress;

        public readonly List<OpponentRodView> OpponentRods = new List<OpponentRodView>();

        /// <summary>Where the opponent's keeper stands right now. Attack aims away from it.</summary>
        public float OpponentKeeperLateral;

        /// <summary>Our own attack rod, so midfield knows where it is passing to.</summary>
        public float FriendlyAttackOrderedDepth;
        public float FriendlyAttackLateral;

        /// <summary>Longest lookahead any prediction will use, seconds. Keeps wild extrapolation out.</summary>
        public float MaxPredictTime = 1.2f;

        /// <summary>
        /// Is the ball the opponent's right now? Judged the way a player judges it — whose figure
        /// is nearest, and is it near enough to be in control.
        /// </summary>
        public bool OpponentHasBall;

        /// <summary>
        /// Increments each time the opponent takes possession, or the ball turns and starts coming
        /// at our goal while they have it. Defending rods roll their read once per attack against
        /// this, so a missed read is one clean missed block rather than a flicker.
        /// </summary>
        public int AttackId;

        /// <summary>Clamps a lateral aim to the part of the table that actually exists.</summary>
        public float ClampToTable(float lateral)
        {
            return Mathf.Clamp(lateral, -LateralHalfExtent, LateralHalfExtent);
        }

        /// <summary>
        /// Is the straight line from one point on the table to another free of opponent figures?
        ///
        /// Only rods strictly between the two depths can intercept, so rods behind the origin or
        /// past the target are ignored rather than counted as blockers.
        ///
        /// The origin is passed in rather than read from the ball, because the rod asking is
        /// working from its own belief about where the ball is — a rod with a slow read should
        /// judge the lane from what it thinks it sees, not from the truth.
        /// </summary>
        public bool IsLaneClear(float fromLateral, float fromOrderedDepth,
                                float targetLateral, float targetOrderedDepth,
                                float laneWidth, List<float> scratch)
        {
            float span = targetOrderedDepth - fromOrderedDepth;
            if (Mathf.Abs(span) < 1e-4f)
            {
                return true;
            }

            foreach (OpponentRodView view in OpponentRods)
            {
                if (view.Rod == null)
                {
                    continue;
                }

                float t = (view.OrderedDepth - fromOrderedDepth) / span;
                if (t <= 0.02f || t >= 0.98f)
                {
                    continue; // behind the origin or past the target — cannot block this pass
                }

                float laneLateral = Mathf.Lerp(fromLateral, targetLateral, t);
                view.CurrentLaterals(scratch);

                foreach (float figure in scratch)
                {
                    if (Mathf.Abs(figure - laneLateral) < laneWidth)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
