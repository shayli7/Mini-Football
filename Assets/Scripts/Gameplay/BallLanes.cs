using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Which team's figures can reach the ball, by where the ball is.
    ///
    /// The rows alternate by team down the table — Red goalie and defence, Blue attack, Red midfield,
    /// Blue midfield, Red attack, Blue defence and goalie — and a figure can only reach a ball within
    /// about half the gap to the next row. So the table divides into LANES along the goal-to-goal axis:
    /// neighbouring rows of one team merge into one lane, and the boundary between two lanes sits midway
    /// between the facing rows of opposite teams. Online, the ball is simulated by whichever player owns
    /// the lane it is in, so each player's own touches happen on their own phone.
    ///
    /// Built from the rods' resting pivots, never from list order or team names, so it is right whichever
    /// way round the table sits. Build it in Start or later: <see cref="RodController.BarPivot"/> only
    /// exists once each rod's Awake has run.
    /// </summary>
    public sealed class BallLanes
    {
        private struct Lane
        {
            public float Min, Max;
            public Team Team;
        }

        private readonly List<Lane> lanes = new List<Lane>();
        private Vector3 axis;

        public bool Valid => lanes.Count > 1;

        /// <summary>The goal-to-goal direction lanes are measured along.</summary>
        public Vector3 Axis => axis;

        /// <summary>How far a figure's reach crosses a lane boundary, at worst, in metres (measured once
        /// at build; 0 when no figure reaches past it). See <see cref="LogReach"/>.</summary>
        public float WorstOverlap { get; private set; }

        public static BallLanes Build(TableReferences table, float ballRadius, float ballCentreHeight)
        {
            var result = new BallLanes();
            if (table == null) return result;

            var rods = new List<RodController>();
            foreach (RodController rod in table.RedRods) if (rod != null) rods.Add(rod);
            foreach (RodController rod in table.BlueRods) if (rod != null) rods.Add(rod);
            if (rods.Count < 2 || rods[0].BarAxis.sqrMagnitude < 1e-6f) return result;

            result.axis = Vector3.Cross(rods[0].BarAxis, Vector3.up).normalized;
            Vector3 a = result.axis;
            rods.Sort((x, y) => Vector3.Dot(x.BarPivot, a).CompareTo(Vector3.Dot(y.BarPivot, a)));

            float start = float.NegativeInfinity;
            for (int i = 0; i < rods.Count; i++)
            {
                bool last = i == rods.Count - 1;
                if (!last && rods[i + 1].Team == rods[i].Team) continue; // same team: one lane

                float end = last
                    ? float.PositiveInfinity
                    : (Vector3.Dot(rods[i].BarPivot, a) + Vector3.Dot(rods[i + 1].BarPivot, a)) * 0.5f;
                result.lanes.Add(new Lane { Min = start, Max = end, Team = rods[i].Team });
                start = end;
            }

            result.LogReach(rods, ballRadius, ballCentreHeight);
            return result;
        }

        /// <summary>
        /// The team whose lane <paramref name="position"/> is in, and how deep inside it — the distance to
        /// the nearest boundary with another lane (large at the open ends behind the goalies).
        /// </summary>
        public Team OwnerAt(Vector3 position, out float depth)
        {
            float s = Vector3.Dot(position, axis);
            for (int i = 0; i < lanes.Count; i++)
            {
                Lane lane = lanes[i];
                if (s < lane.Max || i == lanes.Count - 1)
                {
                    float toMin = float.IsNegativeInfinity(lane.Min) ? float.PositiveInfinity : s - lane.Min;
                    float toMax = float.IsPositiveInfinity(lane.Max) ? float.PositiveInfinity : lane.Max - s;
                    depth = Mathf.Min(toMin, toMax);
                    return lane.Team;
                }
            }

            depth = 0f;
            return Team.Red;
        }

        /// <summary>
        /// Measures, once, how far each rod's figures can reach toward its neighbours at ball height,
        /// against half the gap to the nearest opposite-team row. Lane authority assumes the two do not
        /// overlap by more than about a centimetre: where they do, a touch by the player who is NOT
        /// simulating is judged against a delayed copy of their figure, as every touch was before lanes.
        ///
        /// Approximate by design — a figure is treated as a stick pivoting on the bar, reaching as far as
        /// its farthest collider corner, thickened by its foot — and only ever logged, never acted on.
        /// </summary>
        private void LogReach(List<RodController> rods, float ballRadius, float ballCentreHeight)
        {
            Vector3 a = axis;
            float worst = float.NegativeInfinity;
            string worstPair = string.Empty;

            for (int i = 0; i < rods.Count - 1; i++)
            {
                if (rods[i].Team == rods[i + 1].Team) continue;

                float halfGap = (Vector3.Dot(rods[i + 1].BarPivot, a) - Vector3.Dot(rods[i].BarPivot, a)) * 0.5f;
                float reach = Mathf.Max(Reach(rods[i], ballRadius, ballCentreHeight),
                                        Reach(rods[i + 1], ballRadius, ballCentreHeight));
                float overlap = reach - halfGap;
                if (overlap > worst)
                {
                    worst = overlap;
                    worstPair = $"{rods[i].name}/{rods[i + 1].name}";
                }
            }

            WorstOverlap = Mathf.Max(0f, worst);
            Debug.Log($"[BallLanes] {lanes.Count} lanes. Worst figure reach past a lane boundary: " +
                      $"{worst * 100f:F1} cm ({worstPair}). Lane authority assumes this stays under ~1 cm.");
        }

        /// <summary>Horizontal distance from the bar at which a figure on this rod can still touch the
        /// ball's centre, at ball height.</summary>
        private float Reach(RodController rod, float ballRadius, float ballCentreHeight)
        {
            Vector3 bar = rod.BarAxis.normalized;
            Vector3 pivot = rod.BarPivot;
            float barHeight = pivot.y - ballCentreHeight;

            float length = 0f, thickness = 0f;
            foreach (Collider c in rod.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger) continue;
                Bounds b = c.bounds;
                Vector3 e = b.extents;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = b.center + new Vector3((k & 1) == 0 ? -e.x : e.x,
                                                            (k & 2) == 0 ? -e.y : e.y,
                                                            (k & 4) == 0 ? -e.z : e.z);
                    Vector3 rel = corner - pivot;
                    length = Mathf.Max(length, (rel - Vector3.Project(rel, bar)).magnitude);
                }

                thickness = Mathf.Max(thickness,
                    Mathf.Abs(axis.x) * e.x + Mathf.Abs(axis.y) * e.y + Mathf.Abs(axis.z) * e.z);
            }

            float swept = (length + ballRadius) * (length + ballRadius) - barHeight * barHeight;
            return (swept > 0f ? Mathf.Sqrt(swept) : 0f) + thickness;
        }
    }
}
