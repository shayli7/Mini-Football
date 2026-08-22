using System.Collections.Generic;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Adds the physics bodies and colliders for Step 3. Sits on the table root; you drive it from
    /// the component's right-click menu, the same way as "Collect rods" on TableReferences.
    ///
    /// What it builds:
    ///   Ball  - dynamic Rigidbody + SphereCollider + BallController (continuous collision)
    ///   Rods  - kinematic Rigidbody, a capsule down the bar, and a box on every player figure
    ///   Walls - boxes on the long rails and goal backs, a mesh collider on the goal-mouth ends,
    ///           and a floor under the pitch thickened so the ball cannot tunnel through it
    ///
    /// Rods stay kinematic, so RodController automatically switches to MovePosition/MoveRotation
    /// and PhysX transfers their motion into the ball. Kinematic and static bodies never collide
    /// with each other, so no layer setup is needed — only the ball generates contacts.
    ///
    /// Safe to run again: existing colliders and bodies are updated, not duplicated.
    /// </summary>
    [DisallowMultipleComponent]
    public class TablePhysicsBuilder : MonoBehaviour
    {
        [Header("Object names in the model")]
        [SerializeField] private string ballName = "Ball";
        [Tooltip("Objects whose shape is close enough to a box (long side rails, goal backs).")]
        [SerializeField]
        private string[] boxWallNames = { "Rail_Lp", "Rail_Lm" };
        [Tooltip("Anything containing or surrounding the goal mouth. These need mesh colliders: a " +
                 "box drawn round a goal back reaches across the opening and narrows it, which " +
                 "wedges the ball in the mouth before it can ever reach the scoring volume.")]
        [SerializeField]
        private string[] meshWallNames = { "Rail_Sp", "Rail_Sm", "GoalBack", "GoalBack.001" };
        [SerializeField] private string fieldName = "Field";

        [Header("Floor")]
        [Tooltip("Minimum thickness for the pitch collider. A paper-thin floor gets tunnelled through.")]
        [SerializeField] private float minFloorThickness = 0.05f;

        [Header("Rods")]
        [Tooltip("Shrinks the bar collider's radius. The visual bar is thin; too fat a collider " +
                 "makes the ball bounce off invisible air above the pitch.")]
        [Range(0.2f, 1.5f)]
        [SerializeField] private float barRadiusScale = 0.9f;
        [Tooltip("Prefix identifying player figures under a rod.")]
        [SerializeField] private string figurePrefix = "Fig";

        [Tooltip("Clearance kept at each end of a rod's travel, so figures do not grind into the " +
                 "side rails at the extremes.")]
        [SerializeField] private float slideMargin = 0.005f;

        [Header("Figure height")]
        [Tooltip("Gap to leave between a figure's foot and the pitch. Keep it well under the ball's " +
                 "radius or shots will slide underneath the players.")]
        [SerializeField] private float figureClearance = 0.002f;
        [Tooltip("Reach each figure's COLLIDER down to the pitch, leaving the visible model exactly " +
                 "as it is. A box collider fits the mesh, so it inherits the model's gap above the " +
                 "pitch and the ball rolls under the players instead of being struck by them. " +
                 "Extending the collider fixes the gameplay without distorting the art.")]
        [SerializeField] private bool extendFigureCollidersToPitch = true;

        // ------------------------------------------------------------------ entry points

        [ContextMenu("Build all table physics")]
        public void BuildAll()
        {
            int ball = BuildBall();
            int rods = BuildRods();
            int walls = BuildWalls();
            Debug.Log($"{name}: physics built — ball: {ball}, rod colliders: {rods}, wall colliders: {walls}.", this);
        }

        [ContextMenu("Build ball only")]
        public int BuildBall()
        {
            Transform ball = FindDescendant(ballName);
            if (ball == null)
            {
                Debug.LogWarning($"{name}: no object named '{ballName}' found.", this);
                return 0;
            }

            SphereCollider sphere = GetOrAdd<SphereCollider>(ball.gameObject);

            // Size the sphere from the ball mesh so it matches whatever scale the model uses.
            MeshFilter filter = ball.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Bounds b = filter.sharedMesh.bounds;
                sphere.center = b.center;
                sphere.radius = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            }

            GetOrAdd<Rigidbody>(ball.gameObject);
            BallController controller = GetOrAdd<BallController>(ball.gameObject);
            controller.ApplyPhysics();

            Debug.Log($"Ball ready: radius {sphere.radius:0.####} (local units).", ball);
            return 1;
        }

        [ContextMenu("Build rods only")]
        public int BuildRods()
        {
            int added = 0;

            foreach (RodController rod in GetComponentsInChildren<RodController>(true))
            {
                // Kinematic body: RodController detects this and drives the rod through PhysX,
                // which is what lets a moving rod impart velocity to the ball.
                Rigidbody body = GetOrAdd<Rigidbody>(rod.gameObject);
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;

                if (BuildBarCollider(rod))
                {
                    added++;
                }

                foreach (Transform child in rod.GetComponentsInChildren<Transform>(true))
                {
                    if (child == rod.transform ||
                        !child.name.StartsWith(figurePrefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // AddComponent<BoxCollider> auto-fits to the renderer bounds, which is exactly
                    // the figure's shape — including the gap it leaves above the pitch.
                    if (child.GetComponent<MeshFilter>() != null)
                    {
                        BoxCollider box = GetOrAdd<BoxCollider>(child.gameObject);

                        if (extendFigureCollidersToPitch)
                        {
                            ExtendColliderToPitch(child, box);
                        }

                        added++;
                    }
                }
            }

            return added;
        }

        [ContextMenu("Build walls only")]
        public int BuildWalls()
        {
            int added = 0;

            foreach (string wallName in boxWallNames)
            {
                foreach (Transform t in FindAllDescendants(wallName))
                {
                    GetOrAdd<BoxCollider>(t.gameObject);
                    added++;
                }
            }

            // The end rails carry the goal opening — a box would seal it shut, so use the real mesh.
            foreach (string wallName in meshWallNames)
            {
                foreach (Transform t in FindAllDescendants(wallName))
                {
                    // Drop any box left over from an earlier build: it would still reach across the
                    // goal mouth and trap the ball even once the mesh collider is in place.
                    var stale = t.GetComponent<BoxCollider>();
                    if (stale != null)
                    {
                        if (Application.isPlaying)
                        {
                            Destroy(stale);
                        }
                        else
                        {
                            DestroyImmediate(stale);
                        }
                    }

                    MeshCollider mc = GetOrAdd<MeshCollider>(t.gameObject);
                    mc.convex = false; // concave is fine: these never move
                    added++;
                }
            }

            if (BuildFloor())
            {
                added++;
            }

            return added;
        }

        /// <summary>
        /// Drops every player figure until its foot almost touches the pitch, so the ball cannot
        /// pass underneath. Measures the real gap per figure and closes it, so running this twice
        /// changes nothing — it is a fit, not a nudge.
        ///
        /// Figures must be at rest (not spun) for the measurement to mean anything, so run it in
        /// edit mode rather than during play.
        /// </summary>
        /// <summary>
        /// Drops an invisible scoring volume into each goal, sized and placed from the goal backs
        /// already in the model. Creates new child objects only — it never alters the table itself,
        /// so deleting the two "GoalTrigger_*" objects undoes it completely.
        ///
        /// Which team scores where is worked out from where each team's rods sit: the volume in a
        /// team's own half awards their opponent.
        /// </summary>
        /// <summary>
        /// Prints the numbers that decide whether the players can actually hit the ball: how high
        /// each figure's collider sits above the pitch, against the ball's diameter. Any figure
        /// whose gap approaches the ball's size will let shots run underneath it.
        /// </summary>
        [ContextMenu("Report figure / ball clearances")]
        public void ReportClearances()
        {
            Transform field = FindDescendant(fieldName);
            Renderer fieldRenderer = field != null ? field.GetComponent<Renderer>() : null;
            if (fieldRenderer == null)
            {
                Debug.LogWarning($"{name}: no renderer on '{fieldName}'.", this);
                return;
            }

            float pitchTop = fieldRenderer.bounds.max.y;

            Transform ballTransform = FindDescendant(ballName);
            var ballCollider = ballTransform != null ? ballTransform.GetComponent<SphereCollider>() : null;
            float ballDiameter = ballCollider != null ? ballCollider.bounds.size.y : -1f;

            float worst = float.NegativeInfinity;
            string worstName = "none";
            int figures = 0, blocking = 0;

            foreach (RodController rod in GetComponentsInChildren<RodController>(true))
            {
                foreach (Transform child in rod.GetComponentsInChildren<Transform>(true))
                {
                    if (child == rod.transform ||
                        !child.name.StartsWith(figurePrefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var box = child.GetComponent<BoxCollider>();
                    if (box == null)
                    {
                        continue;
                    }

                    figures++;
                    float gap = box.bounds.min.y - pitchTop;

                    if (ballDiameter > 0f && gap < ballDiameter * 0.5f)
                    {
                        blocking++;
                    }

                    if (gap > worst)
                    {
                        worst = gap;
                        worstName = child.name;
                    }
                }
            }

            string verdict = ballDiameter <= 0f
                ? "no ball collider found — cannot judge"
                : blocking == figures
                    ? "OK: every figure sits low enough to block the ball"
                    : $"PROBLEM: {figures - blocking} of {figures} figures let the ball pass underneath";

            Debug.Log($"{name}: clearance report\n" +
                      $"  pitch top      {pitchTop:0.####}\n" +
                      $"  ball diameter  {(ballDiameter > 0f ? ballDiameter.ToString("0.####") : "?")}\n" +
                      $"  figures        {figures}\n" +
                      $"  largest gap    {worst:0.####} ({worstName})\n" +
                      $"  verdict        {verdict}", this);
        }

        /// <summary>
        /// Checks every link in the scoring chain and names the one that is broken: no manager, no
        /// trigger volumes, volumes the ball cannot reach, or a solid collider sealing the goal
        /// mouth so the ball can never get in.
        /// </summary>
        /// <summary>
        /// Gives every rod the travel its own figures allow: a rod can slide until its outermost
        /// player reaches the wall, so a one-man goalkeeper covers nearly the whole pitch while a
        /// five-man rod barely moves. Setting one range for all rods leaves the keeper unable to
        /// reach the corners of its own goal.
        ///
        /// Absolute, so re-running never compounds. Run in edit mode with the rods centred.
        /// </summary>
        [ContextMenu("Fit rod slide ranges")]
        public void FitRodSlideRanges()
        {
            Transform field = FindDescendant(fieldName);
            Renderer fieldRenderer = field != null ? field.GetComponent<Renderer>() : null;
            if (fieldRenderer == null)
            {
                Debug.LogWarning($"{name}: no renderer on '{fieldName}' — cannot measure the pitch.", this);
                return;
            }

            Bounds pitch = fieldRenderer.bounds;

#if UNITY_EDITOR
            int undoGroup = UnityEditor.Undo.GetCurrentGroup();
            UnityEditor.Undo.SetCurrentGroupName("Fit rod slide ranges");
#endif

            var summary = new System.Text.StringBuilder($"{name}: rod slide ranges\n");

            foreach (RodController rod in GetComponentsInChildren<RodController>(true))
            {
                // Rods run across the table, so travel is measured along the bar.
                Vector3 axis = rod.BarAxis;
                int axisIndex = Mathf.Abs(axis.x) >= Mathf.Abs(axis.z) ? 0 : 2;

                float figuresMin = float.PositiveInfinity;
                float figuresMax = float.NegativeInfinity;
                int figures = 0;

                foreach (Transform child in rod.GetComponentsInChildren<Transform>(true))
                {
                    if (child == rod.transform ||
                        !child.name.StartsWith(figurePrefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Renderer figure = child.GetComponent<Renderer>();
                    if (figure == null)
                    {
                        continue;
                    }

                    figuresMin = Mathf.Min(figuresMin, figure.bounds.min[axisIndex]);
                    figuresMax = Mathf.Max(figuresMax, figure.bounds.max[axisIndex]);
                    figures++;
                }

                if (figures == 0)
                {
                    continue;
                }

                float pitchWidth = pitch.size[axisIndex];
                float occupied = figuresMax - figuresMin;
                float travel = Mathf.Max(0f, pitchWidth - occupied - slideMargin * 2f);

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.Undo.RecordObject(rod, "Fit rod slide ranges");
                }
#endif
                rod.SetSlideRange(travel);

                summary.AppendLine($"  {rod.name,-8} {rod.Team,-4} {figures} figure(s), " +
                                   $"span {occupied:0.###} -> travel {travel:0.###} m");
            }

#if UNITY_EDITOR
            UnityEditor.Undo.CollapseUndoOperations(undoGroup);
#endif

            Debug.Log(summary.ToString(), this);
        }

        [ContextMenu("Report goal setup")]
        public void ReportGoalSetup()
        {
            var report = new System.Text.StringBuilder($"{name}: goal setup report\n");

            MatchManager match = FindAnyObjectByType<MatchManager>();
            report.AppendLine($"  MatchManager   {(match != null ? "found on " + match.name : "MISSING — nothing can score")}");

            GoalTrigger[] triggers = FindObjectsByType<GoalTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            report.AppendLine($"  GoalTriggers   {triggers.Length} (expected 2)");

            Transform ballTransform = FindDescendant(ballName);
            var ballCollider = ballTransform != null ? ballTransform.GetComponent<SphereCollider>() : null;
            float ballRadius = ballCollider != null ? ballCollider.bounds.extents.y : 0f;
            report.AppendLine($"  Ball           {(ballCollider != null ? $"radius {ballRadius:0.####} at {ballTransform.position}" : "MISSING collider")}");

            foreach (GoalTrigger trigger in triggers)
            {
                var box = trigger.GetComponent<BoxCollider>();
                if (box == null)
                {
                    report.AppendLine($"    {trigger.name}: NO BoxCollider");
                    continue;
                }

                report.AppendLine($"    {trigger.name}: scores for {trigger.ScoringTeam}, " +
                                  $"isTrigger={box.isTrigger}, size {box.size}, centre {box.bounds.center}");

                if (!box.isTrigger)
                {
                    report.AppendLine("      ^ NOT a trigger — the ball will bounce off instead of scoring");
                }

                if (trigger.Match == null && match == null)
                {
                    report.AppendLine("      ^ no MatchManager wired");
                }

                // Anything solid overlapping the volume can stop the ball before it arrives.
                Collider[] blockers = Physics.OverlapBox(box.bounds.center, box.bounds.extents,
                                                         Quaternion.identity, ~0,
                                                         QueryTriggerInteraction.Ignore);
                foreach (Collider blocker in blockers)
                {
                    if (blocker.transform.IsChildOf(trigger.transform) || blocker == ballCollider)
                    {
                        continue;
                    }

                    report.AppendLine($"      blocked by solid collider: {blocker.name} " +
                                      $"({blocker.GetType().Name}) — the ball may never reach this volume");
                }
            }

            Debug.Log(report.ToString(), this);
        }

        [ContextMenu("Build goal triggers")]
        public void BuildGoalTriggers()
        {
            TableReferences references = GetComponentInChildren<TableReferences>(true)
                                        ?? GetComponent<TableReferences>();
            MatchManager match = FindAnyObjectByType<MatchManager>();

            Transform field = FindDescendant(fieldName);
            Renderer fieldRenderer = field != null ? field.GetComponent<Renderer>() : null;
            if (fieldRenderer == null)
            {
                Debug.LogWarning($"{name}: no renderer on '{fieldName}' — cannot size the goals.", this);
                return;
            }

            Bounds pitch = fieldRenderer.bounds;
            Vector3 longAxis = pitch.size.x >= pitch.size.z ? Vector3.right : Vector3.forward;

            // Which end does each team defend? Compare their rods along the goal-to-goal axis.
            float redSide = AverageAlong(references != null ? references.RedRods : null, longAxis);

#if UNITY_EDITOR
            int undoGroup = UnityEditor.Undo.GetCurrentGroup();
            UnityEditor.Undo.SetCurrentGroupName("Build goal triggers");
#endif

            int built = 0;
            foreach (string goalName in new[] { "GoalBack", "GoalBack.001" })
            {
                Transform goal = FindDescendant(goalName);
                Renderer goalRenderer = goal != null ? goal.GetComponent<Renderer>() : null;
                if (goalRenderer == null)
                {
                    continue;
                }

                Bounds goalBounds = goalRenderer.bounds;
                float goalSide = Vector3.Dot(goalBounds.center, longAxis);

                // The goal on a team's own side is where the OTHER team scores.
                bool onRedSide = Mathf.Sign(goalSide) == Mathf.Sign(redSide) && Mathf.Abs(redSide) > 1e-4f;
                Team scorer = onRedSide ? Team.Blue : Team.Red;

                string triggerName = $"GoalTrigger_{scorer}";
                Transform existing = FindDescendant(triggerName);
                GameObject go = existing != null ? existing.gameObject : new GameObject(triggerName);

#if UNITY_EDITOR
                if (existing == null && !Application.isPlaying)
                {
                    UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Build goal triggers");
                }
#endif
                go.transform.SetParent(transform, true);

                // Span the whole mouth, from the goal line back to the goal back. Scoring the moment
                // the ball crosses the line means a ball that later wedges inside the goal has
                // already counted — and a deep volume cannot be stepped over between physics frames
                // at the ball's speed cap.
                int axisIndex = longAxis == Vector3.right ? 0 : 2;
                float goalLine = goalSide > 0f ? pitch.max[axisIndex] : pitch.min[axisIndex];
                float goalRear = Vector3.Dot(goalBounds.center, longAxis);
                float depth = Mathf.Abs(goalRear - goalLine) + 0.02f;

                Vector3 centre = goalBounds.center;
                centre[axisIndex] = (goalLine + goalRear) * 0.5f;

                go.transform.position = centre;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                Vector3 size = goalBounds.size;
                size[axisIndex] = depth;

                var box = go.GetComponent<BoxCollider>() ?? go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = Vector3.zero;

                // Convert the world size into the object's own units. Dividing by lossyScale is
                // safe here because the trigger is kept axis-aligned and unrotated; going through
                // InverseTransformVector would fold in the parent's rotation and mis-size the box.
                Vector3 lossy = go.transform.lossyScale;
                box.size = new Vector3(
                    size.x / Mathf.Max(Mathf.Abs(lossy.x), 1e-6f),
                    size.y / Mathf.Max(Mathf.Abs(lossy.y), 1e-6f),
                    size.z / Mathf.Max(Mathf.Abs(lossy.z), 1e-6f));

                var trigger = go.GetComponent<GoalTrigger>() ?? go.AddComponent<GoalTrigger>();
                trigger.ScoringTeam = scorer;
                trigger.Match = match;

                built++;
            }

#if UNITY_EDITOR
            UnityEditor.Undo.CollapseUndoOperations(undoGroup);
#endif

            Debug.Log($"{name}: built {built} goal trigger(s). Check each one's Scoring Team is the " +
                      "side that ATTACKS that goal, and swap them if they are the wrong way round.", this);
        }

        /// <summary>Mean position of a set of rods along an axis — tells which end a team defends.</summary>
        private static float AverageAlong(System.Collections.Generic.IReadOnlyList<RodController> rods,
                                          Vector3 axis)
        {
            if (rods == null || rods.Count == 0)
            {
                return 0f;
            }

            float total = 0f;
            int counted = 0;
            foreach (RodController rod in rods)
            {
                if (rod != null)
                {
                    total += Vector3.Dot(rod.transform.position, axis);
                    counted++;
                }
            }

            return counted > 0 ? total / counted : 0f;
        }

        [ContextMenu("Fit figures to the field")]
        public void FitFiguresToField()
        {
            Transform field = FindDescendant(fieldName);
            Renderer fieldRenderer = field != null ? field.GetComponent<Renderer>() : null;
            if (fieldRenderer == null)
            {
                Debug.LogWarning($"{name}: no renderer on '{fieldName}' — cannot measure the pitch height.", this);
                return;
            }

            float pitchTop = fieldRenderer.bounds.max.y;
            int moved = 0;
            float largestDrop = 0f;

#if UNITY_EDITOR
            // Group every figure into one undo step, so a single Ctrl+Z reverses the whole thing.
            int undoGroup = UnityEditor.Undo.GetCurrentGroup();
            UnityEditor.Undo.SetCurrentGroupName("Fit figures to field");
#endif

            foreach (RodController rod in GetComponentsInChildren<RodController>(true))
            {
                foreach (Transform child in rod.GetComponentsInChildren<Transform>(true))
                {
                    if (child == rod.transform ||
                        !child.name.StartsWith(figurePrefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Renderer figure = child.GetComponent<Renderer>();
                    if (figure == null)
                    {
                        continue;
                    }

                    float gap = figure.bounds.min.y - pitchTop;
                    float drop = gap - figureClearance;

                    if (Mathf.Abs(drop) < 1e-5f)
                    {
                        continue;
                    }

#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        UnityEditor.Undo.RecordObject(child, "Fit figures to field");
                    }
#endif
                    child.position += Vector3.down * drop;
                    moved++;
                    largestDrop = Mathf.Max(largestDrop, Mathf.Abs(drop));
                }
            }

#if UNITY_EDITOR
            UnityEditor.Undo.CollapseUndoOperations(undoGroup);
#endif

            Debug.Log($"{name}: fitted {moved} figure(s) to the pitch (largest move {largestDrop:0.####} m, " +
                      $"clearance {figureClearance:0.####} m). Ctrl+Z reverses this in one step.", this);
        }

        /// <summary>
        /// Lengthens each player figure so it spans from its rod's bar down to just above the pitch.
        /// Better than sliding figures downwards: sliding leaves a gap between the figure and the
        /// bar it is supposed to hang from, and pushes the foot further from the spin axis so it
        /// sweeps a wider arc. Stretching keeps the figure attached to the bar and its arc intact.
        ///
        /// Absolute, not incremental — it computes the length the figure should be, so clicking it
        /// repeatedly is harmless. Lower figureClearance to make the players reach further down.
        ///
        /// Run in edit mode, with the rods unspun.
        /// </summary>
        [ContextMenu("Stretch figures from bar to pitch")]
        public void StretchFiguresToPitch()
        {
            Transform field = FindDescendant(fieldName);
            Renderer fieldRenderer = field != null ? field.GetComponent<Renderer>() : null;
            if (fieldRenderer == null)
            {
                Debug.LogWarning($"{name}: no renderer on '{fieldName}' — cannot measure the pitch height.", this);
                return;
            }

            float pitchTop = fieldRenderer.bounds.max.y;
            float targetBottom = pitchTop + figureClearance;
            int stretched = 0;

#if UNITY_EDITOR
            // Group every figure into one undo step, so a single Ctrl+Z reverses the whole thing.
            int undoGroup = UnityEditor.Undo.GetCurrentGroup();
            UnityEditor.Undo.SetCurrentGroupName("Stretch figures to pitch");
#endif

            foreach (RodController rod in GetComponentsInChildren<RodController>(true))
            {
                float barY = rod.BarPivot.y;

                foreach (Transform child in rod.GetComponentsInChildren<Transform>(true))
                {
                    if (child == rod.transform ||
                        !child.name.StartsWith(figurePrefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Renderer figure = child.GetComponent<Renderer>();
                    if (figure == null)
                    {
                        continue;
                    }

                    float currentHeight = figure.bounds.size.y;
                    float targetHeight = barY - targetBottom;

                    if (currentHeight < 1e-5f || targetHeight < 1e-5f)
                    {
                        continue;
                    }

                    float factor = targetHeight / currentHeight;
                    if (factor < 0.1f || factor > 10f || Mathf.Abs(factor - 1f) < 1e-4f)
                    {
                        continue;
                    }

#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        UnityEditor.Undo.RecordObject(child, "Stretch figures to pitch");
                    }
#endif

                    // Stretch along whichever of the figure's own axes points up in the world.
                    Vector3 scale = child.localScale;
                    int axis = VerticalLocalAxis(child);
                    scale[axis] *= factor;
                    child.localScale = scale;

                    // Scaling moves the figure about its origin, so re-seat the foot afterwards.
                    float bottomAfter = figure.bounds.min.y;
                    child.position += Vector3.up * (targetBottom - bottomAfter);

                    stretched++;
                }
            }

#if UNITY_EDITOR
            UnityEditor.Undo.CollapseUndoOperations(undoGroup);
#endif

            Debug.Log($"{name}: stretched {stretched} figure(s) to span bar -> pitch " +
                      $"(clearance {figureClearance:0.####} m). Ctrl+Z reverses this in one step.", this);
        }

        /// <summary>
        /// Grows a figure's box collider downwards until it nearly touches the pitch, so the player
        /// can actually strike a ball that would otherwise pass beneath it. Only the collider moves;
        /// the mesh is never touched, so the figures look exactly as modelled.
        ///
        /// The box is rebuilt from the mesh first, so running this repeatedly cannot compound.
        /// </summary>
        private void ExtendColliderToPitch(Transform figure, BoxCollider box)
        {
            Transform field = FindDescendant(fieldName);
            Renderer fieldRenderer = field != null ? field.GetComponent<Renderer>() : null;
            MeshFilter mesh = figure.GetComponent<MeshFilter>();

            if (fieldRenderer == null || mesh == null || mesh.sharedMesh == null)
            {
                return;
            }

            // Reset to the plain mesh fit so a rebuild starts from the same place every time.
            box.center = mesh.sharedMesh.bounds.center;
            box.size = mesh.sharedMesh.bounds.size;

            float pitchTop = fieldRenderer.bounds.max.y;
            float targetBottom = pitchTop + figureClearance;
            float reach = box.bounds.min.y - targetBottom;

            if (reach <= 1e-5f)
            {
                return;
            }

            int axis = VerticalLocalAxis(figure);
            float scale = Mathf.Abs(figure.lossyScale[axis]);
            if (scale < 1e-6f)
            {
                return;
            }

            // Which way along that local axis points up in the world? The box has to grow the
            // other way.
            Vector3 unit = Vector3.zero;
            unit[axis] = 1f;
            float upSign = Mathf.Sign(Vector3.Dot(figure.TransformDirection(unit), Vector3.up));

            float local = reach / scale;

            Vector3 size = box.size;
            size[axis] += local;
            box.size = size;

            Vector3 centre = box.center;
            centre[axis] -= upSign * local * 0.5f;
            box.center = centre;
        }

        /// <summary>Index of the object's local axis that points most nearly straight up.</summary>
        private static int VerticalLocalAxis(Transform t)
        {
            float x = Mathf.Abs(Vector3.Dot(t.right, Vector3.up));
            float y = Mathf.Abs(Vector3.Dot(t.up, Vector3.up));
            float z = Mathf.Abs(Vector3.Dot(t.forward, Vector3.up));

            if (y >= x && y >= z) return 1;
            return x >= z ? 0 : 2;
        }

        // ------------------------------------------------------------------ pieces

        private bool BuildBarCollider(RodController rod)
        {
            MeshFilter filter = rod.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 axis = rod.LocalBarAxis.normalized;

            CapsuleCollider capsule = GetOrAdd<CapsuleCollider>(rod.gameObject);

            // Capsule direction: 0 = X, 1 = Y, 2 = Z, matching the bar's long axis.
            if (Mathf.Abs(axis.x) >= Mathf.Abs(axis.y) && Mathf.Abs(axis.x) >= Mathf.Abs(axis.z))
            {
                capsule.direction = 0;
                capsule.height = bounds.size.x;
                capsule.radius = Mathf.Min(bounds.extents.y, bounds.extents.z) * barRadiusScale;
            }
            else if (Mathf.Abs(axis.y) >= Mathf.Abs(axis.z))
            {
                capsule.direction = 1;
                capsule.height = bounds.size.y;
                capsule.radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * barRadiusScale;
            }
            else
            {
                capsule.direction = 2;
                capsule.height = bounds.size.z;
                capsule.radius = Mathf.Min(bounds.extents.x, bounds.extents.y) * barRadiusScale;
            }

            capsule.center = bounds.center;
            return true;
        }

        private bool BuildFloor()
        {
            Transform field = FindDescendant(fieldName);
            if (field == null)
            {
                Debug.LogWarning($"{name}: no object named '{fieldName}' found — the ball has no floor.", this);
                return false;
            }

            BoxCollider box = GetOrAdd<BoxCollider>(field.gameObject);

            MeshFilter filter = field.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return true;
            }

            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 size = bounds.size;
            Vector3 centre = bounds.center;

            // A flat pitch mesh gives a near-zero-thickness box that a fast ball drops straight
            // through. Thicken it downwards, keeping the playing surface exactly where it was.
            float lossyY = Mathf.Abs(field.lossyScale.y) < 1e-6f ? 1f : Mathf.Abs(field.lossyScale.y);
            float minLocalThickness = minFloorThickness / lossyY;

            if (size.y < minLocalThickness)
            {
                float top = centre.y + size.y * 0.5f;
                size.y = minLocalThickness;
                centre.y = top - size.y * 0.5f;
            }

            box.size = size;
            box.center = centre;
            return true;
        }

        // ------------------------------------------------------------------ helpers

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
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

        /// <summary>All descendants whose name matches, or starts with it (catches the ".001" copies).</summary>
        private List<Transform> FindAllDescendants(string targetName)
        {
            var found = new List<Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase) &&
                    t.GetComponent<MeshFilter>() != null)
                {
                    found.Add(t);
                }
            }

            return found;
        }
    }
}
