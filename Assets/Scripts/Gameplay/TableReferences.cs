using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// One place to look up the things on the table. Sits on the table root.
    ///
    /// The keyboard tester uses it now; touch input (Step 2), scoring (Step 4) and the AI (Step 5)
    /// will use the same lists rather than hunting through the hierarchy themselves.
    /// </summary>
    [DisallowMultipleComponent]
    public class TableReferences : MonoBehaviour
    {
        [Tooltip("Red team's rods, goalie first. Fill with the 'Collect rods' context menu.")]
        [SerializeField] private List<RodController> redRods = new List<RodController>();

        [Tooltip("Blue team's rods, goalie first. Fill with the 'Collect rods' context menu.")]
        [SerializeField] private List<RodController> blueRods = new List<RodController>();

        [SerializeField] private Transform ball;

        public IReadOnlyList<RodController> RedRods => redRods;
        public IReadOnlyList<RodController> BlueRods => blueRods;
        public Transform Ball => ball;

        public IReadOnlyList<RodController> RodsFor(Team team)
        {
            return team == Team.Red ? redRods : blueRods;
        }

        /// <summary>Centres every rod and stops it spinning. Used by the round reset in Step 4.</summary>
        public void ResetAllRods()
        {
            foreach (RodController rod in redRods)
            {
                if (rod != null) rod.ResetRod();
            }

            foreach (RodController rod in blueRods)
            {
                if (rod != null) rod.ResetRod();
            }
        }

        /// <summary>
        /// Finds every RodController underneath this object, splits them by team and orders them by
        /// rod index. Also picks up an object named "Ball" if the field is still empty.
        /// </summary>
        [ContextMenu("Collect rods")]
        public void CollectRods()
        {
            redRods.Clear();
            blueRods.Clear();

            foreach (RodController rod in GetComponentsInChildren<RodController>(true))
            {
                if (rod.Team == Team.Red)
                {
                    redRods.Add(rod);
                }
                else
                {
                    blueRods.Add(rod);
                }
            }

            redRods.Sort((a, b) => a.RodIndex.CompareTo(b.RodIndex));
            blueRods.Sort((a, b) => a.RodIndex.CompareTo(b.RodIndex));

            if (ball == null)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.StartsWith("Ball", System.StringComparison.OrdinalIgnoreCase))
                    {
                        ball = child;
                        break;
                    }
                }
            }

            Debug.Log($"{name}: collected {redRods.Count} red and {blueRods.Count} blue rod(s)" +
                      (ball != null ? $", ball = {ball.name}." : ", no ball found."), this);
        }
    }
}
