using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Cuts the four inside corners of the pitch with 45° ramps, so there is no square pocket for
    /// the ball to die in.
    ///
    /// This is a geometry problem, not a tuning one. Where a long rail meets an end rail the model
    /// leaves a 90° corner; a ball that rolls in sits against two walls with friction under it and
    /// no slope to carry it out, and a player figure sweeping past can only push it back into the
    /// same pocket. Real tables have moulded corner ramps for exactly this reason — no amount of
    /// bounciness or drag tuning replaces them.
    ///
    /// The ramps are colliders only: invisible, built at runtime from the pitch's own bounds, and
    /// parented under the table root. They carry their own slippery, lively material so a ball that
    /// reaches a corner is deflected back along the rail instead of stopping.
    ///
    /// Rods are kinematic and never collide with static colliders, so the figures pass through
    /// these freely — only the ball feels them.
    ///
    /// Put this on the table root. Select it in the scene to see the chamfers drawn as gizmos
    /// before you press Play.
    /// </summary>
    [DisallowMultipleComponent]
    public class CornerDeflectors : MonoBehaviour
    {
        [Header("Shape")]
        [Tooltip("How far the chamfer reaches along each rail, in metres. Roughly 2-3 ball " +
                 "diameters is enough to stop the ball settling; too large starts to eat the " +
                 "goal mouth (a warning is logged if it does).")]
        [SerializeField] private float cornerSize = 0.05f;
        [Tooltip("How thick the ramp is, measured outward from the corner. Only needs to be thick " +
                 "enough that a fast ball cannot tunnel through it.")]
        [SerializeField] private float thickness = 0.04f;
        [Tooltip("How far the ramp stands above the pitch. Keep it taller than the ball.")]
        [SerializeField] private float height = 0.05f;
        [Tooltip("Pulls all four ramps in from the pitch bounds. Use a small positive value if the " +
                 "rails sit slightly inside the pitch mesh and the ball still finds a lip.")]
        [SerializeField] private float inset = 0f;

        [Header("Feel")]
        [Tooltip("Lively, like a rail — the point is to send the ball back into play. Keep it at " +
                 "or above the rail value in TableSurfaces, and above the ball's own bounciness, " +
                 "or the ramp cannot claim the contact.")]
        [Range(0f, 1f)]
        [SerializeField] private float bounciness = 0.8f;
        [Tooltip("Keep low, and below the ball's own friction, so the ball slides along the ramp " +
                 "rather than gripping it.")]
        [Range(0f, 1f)]
        [SerializeField] private float friction = 0.02f;

        [Header("Object names in the model")]
        [SerializeField] private string fieldName = "Field";
        [Tooltip("Used only to check the chamfers are not reaching across a goal mouth.")]
        [SerializeField] private string[] goalNames = { "GoalBack", "GoalBack.001" };

        private const string ChildPrefix = "CornerDeflector_";

        private void Awake()
        {
            Build();
        }

        /// <summary>
        /// Builds (or re-fits) the four ramps. Named children are reused rather than duplicated, so
        /// calling this twice changes nothing.
        /// </summary>
        public void Build()
        {
            if (!TryGetPitch(out Bounds pitch))
            {
                Debug.LogWarning($"{name}: no renderer on '{fieldName}' — cannot place corner ramps.", this);
                return;
            }

            WarnIfReachingGoalMouth(pitch);

            // Maximum bounce / Minimum friction, matching the rails: both outrank the ball's own
            // Average material, so the ramp — not the ball — decides how a corner deflection feels.
            PhysicsMaterial material = new PhysicsMaterial("Corner ramp (runtime)")
            {
                bounciness = bounciness,
                dynamicFriction = friction,
                staticFriction = friction,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };

            int built = 0;
            for (int i = 0; i < 4; i++)
            {
                float signX = (i & 1) == 0 ? 1f : -1f;
                float signZ = (i & 2) == 0 ? 1f : -1f;

                BuildOne(pitch, signX, signZ, material);
                built++;
            }

            Debug.Log($"{name}: {built} corner ramps in place ({cornerSize:0.###} m chamfer). " +
                      "The ball can no longer settle in a square corner.", this);
        }

        private void BuildOne(Bounds pitch, float signX, float signZ, PhysicsMaterial material)
        {
            // The corner itself, pulled in by the inset.
            float cornerX = (signX > 0f ? pitch.max.x : pitch.min.x) - signX * inset;
            float cornerZ = (signZ > 0f ? pitch.max.z : pitch.min.z) - signZ * inset;

            // The chamfer runs from (cornerX - signX*size, cornerZ) to (cornerX, cornerZ - signZ*size):
            // its midpoint is half the chamfer in along both axes.
            Vector3 faceMidpoint = new Vector3(
                cornerX - signX * cornerSize * 0.5f,
                pitch.max.y + height * 0.5f,
                cornerZ - signZ * cornerSize * 0.5f);

            // Outward normal of that face — the diagonal pointing into the corner.
            Vector3 outward = new Vector3(signX, 0f, signZ).normalized;

            string childName = $"{ChildPrefix}{(signX > 0f ? "Xp" : "Xm")}{(signZ > 0f ? "Zp" : "Zm")}";
            Transform existing = transform.Find(childName);
            GameObject ramp = existing != null ? existing.gameObject : new GameObject(childName);
            ramp.transform.SetParent(transform, true);

            // Local Z faces out of the corner, so the box's flat side is the playing face.
            ramp.transform.SetPositionAndRotation(
                faceMidpoint + outward * (thickness * 0.5f),
                Quaternion.LookRotation(outward, Vector3.up));
            ramp.transform.localScale = Vector3.one;

            BoxCollider box = ramp.GetComponent<BoxCollider>() ?? ramp.AddComponent<BoxCollider>();
            box.center = Vector3.zero;

            // The face spans the hypotenuse of the chamfer triangle.
            Vector3 size = new Vector3(cornerSize * Mathf.Sqrt(2f), height, thickness);

            // Convert to the object's own units. Safe because the ramp is placed in world space and
            // only ever rotated about Y.
            Vector3 lossy = ramp.transform.lossyScale;
            box.size = new Vector3(
                size.x / Mathf.Max(Mathf.Abs(lossy.x), 1e-6f),
                size.y / Mathf.Max(Mathf.Abs(lossy.y), 1e-6f),
                size.z / Mathf.Max(Mathf.Abs(lossy.z), 1e-6f));

            box.sharedMaterial = material;
        }

        /// <summary>
        /// A chamfer wider than the gap between the goal post and the side rail would narrow the
        /// goal mouth. Worth saying out loud rather than leaving as a mystery "shots stopped going in".
        /// </summary>
        private void WarnIfReachingGoalMouth(Bounds pitch)
        {
            // Goals sit at the ends of the long axis, so the mouth is measured across the short one.
            int shortAxis = pitch.size.x >= pitch.size.z ? 2 : 0;

            foreach (string goalName in goalNames)
            {
                Transform goal = FindDescendant(goalName);
                Renderer goalRenderer = goal != null ? goal.GetComponent<Renderer>() : null;
                if (goalRenderer == null)
                {
                    continue;
                }

                float clearEachSide = (pitch.size[shortAxis] - goalRenderer.bounds.size[shortAxis]) * 0.5f;
                if (cornerSize > clearEachSide)
                {
                    Debug.LogWarning($"{name}: a {cornerSize:0.###} m chamfer reaches into the " +
                                     $"'{goalName}' mouth (only {clearEachSide:0.###} m of rail " +
                                     "beside it). Lower Corner Size or shots will be deflected out.", this);
                }
            }
        }

        private bool TryGetPitch(out Bounds pitch)
        {
            Transform field = FindDescendant(fieldName);
            Renderer fieldRenderer = field != null ? field.GetComponent<Renderer>() : null;
            pitch = fieldRenderer != null ? fieldRenderer.bounds : default;
            return fieldRenderer != null;
        }

        private Transform FindDescendant(string targetName)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }

            return null;
        }

        private void OnDrawGizmosSelected()
        {
            if (!TryGetPitch(out Bounds pitch))
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);

            for (int i = 0; i < 4; i++)
            {
                float signX = (i & 1) == 0 ? 1f : -1f;
                float signZ = (i & 2) == 0 ? 1f : -1f;

                float cornerX = (signX > 0f ? pitch.max.x : pitch.min.x) - signX * inset;
                float cornerZ = (signZ > 0f ? pitch.max.z : pitch.min.z) - signZ * inset;
                float y = pitch.max.y + height * 0.5f;

                Vector3 a = new Vector3(cornerX - signX * cornerSize, y, cornerZ);
                Vector3 b = new Vector3(cornerX, y, cornerZ - signZ * cornerSize);
                Vector3 corner = new Vector3(cornerX, y, cornerZ);

                Gizmos.DrawLine(a, b);
                Gizmos.DrawLine(a, corner);
                Gizmos.DrawLine(b, corner);
            }
        }
    }
}
