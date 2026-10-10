using System.Collections.Generic;
using UnityEngine;

namespace TableFootball.Net
{
    /// <summary>
    /// The host's referee for goals that happen inside the GUEST's simulation.
    ///
    /// With lane authority the guest simulates the ball while it is in a Blue lane — including Blue's
    /// own goal mouth — so a goal against Blue can happen where the host's goal trigger never sees it:
    /// the host's ball is then only a drawing of the guest's, and a drawing must never score (it is
    /// projected ahead and could cross the line before the guest's real save arrives). So the host
    /// judges the guest's REAL path instead: every segment between two consecutive guest samples is
    /// tested against the same goal volumes the triggers use.
    ///
    /// Testing segments rather than points means a lost sample cannot carry the ball over the line
    /// unseen. Judging on the host rather than trusting a report from the guest means a guest cannot
    /// decline to report a goal against itself.
    /// </summary>
    public sealed class GoalJudge
    {
        private readonly List<GoalTrigger> goals = new List<GoalTrigger>();
        private readonly List<BoxCollider> boxes = new List<BoxCollider>();
        private readonly float radius;

        public GoalJudge(float ballRadius)
        {
            radius = ballRadius;
            foreach (GoalTrigger goal in Object.FindObjectsByType<GoalTrigger>(FindObjectsInactive.Include,
                                                                              FindObjectsSortMode.None))
            {
                var box = goal != null ? goal.GetComponent<BoxCollider>() : null;
                if (box == null) continue;
                goals.Add(goal);
                boxes.Add(box);
            }
        }

        /// <summary>
        /// Whether the ball, moving from <paramref name="from"/> to <paramref name="to"/>, touched a goal
        /// volume — the same moment the volume's own trigger would fire for a ball simulated here.
        /// </summary>
        public bool Crossed(Vector3 from, Vector3 to, out Team scorer)
        {
            float length = Vector3.Distance(from, to);
            int steps = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(radius * 0.5f, 0.002f)), 1, 200);

            for (int g = 0; g < boxes.Count; g++)
            {
                for (int i = 0; i <= steps; i++)
                {
                    if (Touches(boxes[g], Vector3.Lerp(from, to, i / (float)steps)))
                    {
                        scorer = goals[g].ScoringTeam;
                        return true;
                    }
                }
            }

            scorer = Team.Red;
            return false;
        }

        /// <summary>
        /// Where along <paramref name="from"/> → <paramref name="to"/> the ball FIRST touches a goal
        /// volume, for a projection that has to stop there: the goal backs are deliberately not solid, so
        /// without it a guess carried past the line runs on out of the open back of the goal.
        /// </summary>
        public bool FirstContact(Vector3 from, Vector3 to, out Vector3 point)
        {
            float length = Vector3.Distance(from, to);
            int steps = Mathf.Clamp(Mathf.CeilToInt(length / Mathf.Max(radius * 0.5f, 0.002f)), 1, 200);

            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)steps);
                for (int g = 0; g < boxes.Count; g++)
                {
                    if (Touches(boxes[g], p))
                    {
                        point = p;
                        return true;
                    }
                }
            }

            point = to;
            return false;
        }

        /// <summary>Sphere of the ball's radius against the box, in the box's own space.</summary>
        private bool Touches(BoxCollider box, Vector3 point)
        {
            Transform t = box.transform;
            Vector3 local = t.InverseTransformPoint(point) - box.center;
            Vector3 half = box.size * 0.5f;
            Vector3 scale = t.lossyScale;

            Vector3 outside = new Vector3(
                Mathf.Max(0f, Mathf.Abs(local.x) - half.x) * Mathf.Abs(scale.x),
                Mathf.Max(0f, Mathf.Abs(local.y) - half.y) * Mathf.Abs(scale.y),
                Mathf.Max(0f, Mathf.Abs(local.z) - half.z) * Mathf.Abs(scale.z));

            return outside.sqrMagnitude <= radius * radius;
        }
    }
}
