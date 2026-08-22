using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Retunes PhysX for tabletop scale. Unity's defaults assume human-sized objects, and on a
    /// 1.3 m table with a 35 mm ball several of them quietly break the feel:
    ///
    ///   Bounce Threshold 2 m/s - collisions slower than this get NO bounce at all. A ball
    ///       crossing the whole table in a second is only doing ~1.3 m/s, so nearly every hit
    ///       lands under the threshold and the ball goes dead however bouncy its material is.
    ///       This is the single biggest cause of "my bouncy ball won't bounce".
    ///
    ///       The threshold is measured against the NORMAL component of the impact, not its speed,
    ///       and that is what makes it so destructive here: a ball meeting a rail at an angle only
    ///       drives (speed x sin(angle)) into the wall. At 1.5 m/s a 10° hit presents 0.26 m/s and
    ///       an 8° hit presents 0.21 m/s — so glancing hits fail the test first, lose all
    ///       restitution, keep only their sideways motion and slide along the rail instead of
    ///       rebounding. Keeping this near zero is what makes a bank shot leave at the angle it
    ///       arrived.
    ///   Contact Offset 1 cm  - more than half the ball's radius. The ball hovers and resolves
    ///       contacts early, making bounces mushy and imprecise.
    ///   Sleep Threshold      - a slowly rolling ball falls asleep and stops dead mid-pitch.
    ///   Fixed Timestep 0.02  - at 50 Hz a fast ball moves a long way between physics steps.
    ///
    /// Applied from code rather than Project Settings so the values live in version control and
    /// hold on device. Put this on the table root (or any object in the scene).
    /// </summary>
    [DisallowMultipleComponent]
    public class TablePhysicsSettings : MonoBehaviour
    {
        [Header("Bounce")]
        [Tooltip("Impacts whose NORMAL component is slower than this produce no bounce. Unity's " +
                 "default of 2 is far too high for a tabletop, and even a few tenths silently " +
                 "kills glancing hits off a rail — keep it near zero.")]
        [SerializeField] private float bounceThreshold = 0.02f;

        [Header("Precision")]
        [Tooltip("How far apart contacts are generated. Should be small next to the ball's radius.")]
        [SerializeField] private float contactOffset = 0.002f;
        [Tooltip("Below this energy a body sleeps. Low keeps a slow ball rolling instead of freezing.")]
        [SerializeField] private float sleepThreshold = 0.0005f;
        [Tooltip("More velocity iterations means restitution is resolved more accurately — which is " +
                 "what keeps a fast rebound clean rather than approximate.")]
        [Range(1, 8)]
        [SerializeField] private int solverVelocityIterations = 6;
        [Range(1, 16)]
        [SerializeField] private int solverIterations = 8;

        [Header("Rate")]
        [Tooltip("Physics steps per second as a timestep. 0.01 = 100 Hz, which handles a fast " +
                 "small ball far better than the default 0.02. Costs little in this scene.")]
        [SerializeField] private float fixedTimestep = 0.01f;

        private void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply physics settings")]
        public void Apply()
        {
            Physics.bounceThreshold = bounceThreshold;
            Physics.defaultContactOffset = contactOffset;
            Physics.sleepThreshold = sleepThreshold;
            Physics.defaultSolverVelocityIterations = solverVelocityIterations;
            Physics.defaultSolverIterations = solverIterations;

            if (fixedTimestep > 0f)
            {
                Time.fixedDeltaTime = fixedTimestep;
            }

            Debug.Log($"Physics tuned for tabletop scale: bounceThreshold {bounceThreshold}, " +
                      $"contactOffset {contactOffset}, {1f / Mathf.Max(fixedTimestep, 1e-4f):0} Hz.", this);
        }
    }
}
