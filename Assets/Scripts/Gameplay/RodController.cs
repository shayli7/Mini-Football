using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TableFootball
{
    public enum Team
    {
        Red,
        Blue
    }

    /// <summary>
    /// Slides a foosball rod along its bar and spins it about the same axis.
    ///
    /// Step 1 needs no physics: the rod is driven straight through its Transform. If a kinematic
    /// Rigidbody is later added (Step 3, for ball interaction) this switches to MovePosition /
    /// MoveRotation automatically, so no code has to change.
    ///
    /// Nothing here knows where input comes from. Keyboard (Step 1), touch swipes (Step 2) and the
    /// AI (Step 5) all drive the same public API.
    /// </summary>
    [DisallowMultipleComponent]
    public class RodController : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private Team team = Team.Red;
        [Tooltip("Index from the model (Rod_0..Rod_7). Orders the rods within a team.")]
        [SerializeField] private int rodIndex;

        [Header("Slide (along the bar)")]
        [Tooltip("Total travel in metres, centred on the rod's starting position.")]
        [SerializeField] private float slideRangeMeters = 0.15f;
        [Tooltip("How fast the rod chases its slide target, in metres/second.")]
        [SerializeField] private float slideSpeed = 1.5f;

        [Header("Spin (about the bar)")]
        [Tooltip("Ceiling on spin from a flick, in degrees/second. This is a real power limit: a " +
                 "figure's foot sits only ~5 cm from the bar, so 1440°/s is barely 1.2 m/s of foot " +
                 "speed and the ball can never leave hard however high the ball's speed cap is.")]
        [SerializeField] private float maxSpinSpeed = 2200f;
        [Tooltip("Degrees/second² while a spin input is held.")]
        [SerializeField] private float spinAccel = 4400f;
        [Tooltip("Higher settles the spin faster after a flick.")]
        [SerializeField] private float spinDamping = 3f;
        [SerializeField] private bool invertSpin = false;

        [Header("Bar axis (auto-detected from the bar mesh)")]
        [SerializeField] private bool autoDetectAxis = true;
        [Tooltip("Local-space direction the bar runs along. Auto-filled when Auto Detect Axis is on.")]
        [SerializeField] private Vector3 localBarAxis = Vector3.forward;
        [Tooltip("Local-space point on the bar's centreline that the rod spins about.")]
        [SerializeField] private Vector3 localPivot = Vector3.zero;

        [Header("Debug")]
        [Tooltip("Prints this rod's live settings on Play — which rod it is, how many figures it " +
                 "carries and the slide range it actually received. Turn on when an inspector value " +
                 "appears to have no effect.")]
        [SerializeField] private bool logSetupOnStart = false;

        // Cached rest pose — every frame's transform is rebuilt from this, so nothing can drift.
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector3 worldBarAxis;
        private Vector3 worldPivot;

        private Rigidbody kinematicBody;

        /// <summary>Interpolation suspended by a teleport, restored on the next physics step.</summary>
        private RigidbodyInterpolation interpolationToRestore;
        private bool restoreInterpolation;

        private float targetSlide01 = 0.5f;
        private float currentSlide01 = 0.5f;
        private float spinAngleDeg;
        private float spinVelocity;
        private float spinInput;

        private float previousSpinAngleDeg;
        private float measuredSpinSpeed;

        /// <summary>True while another machine is telling us this rod's spin — see SetMeasuredSpin.</summary>
        private bool spinReportedExternally;
        private float previousSlideMeters;
        private float measuredSlideSpeed;

        public Team Team => team;
        public int RodIndex => rodIndex;

        /// <summary>Slide position as 0..1, where 0.5 is the rod's rest position.</summary>
        public float CurrentSlide01 => currentSlide01;
        public float TargetSlide01 => targetSlide01;

        /// <summary>Slide offset from rest, in metres (negative = toward the bar's -axis end).</summary>
        public float CurrentSlideMeters => (currentSlide01 - 0.5f) * slideRangeMeters;

        /// <summary>Total travel in metres. Pointer input needs this to track the finger 1:1.</summary>
        public float SlideRangeMeters => slideRangeMeters;

        /// <summary>
        /// Sets how far this rod may travel. Rods are not alike: a one-man goalkeeper can cover
        /// nearly the full width of the pitch, while a five-man rod runs out of room almost at once.
        /// </summary>
        public void SetSlideRange(float meters)
        {
            slideRangeMeters = Mathf.Max(0f, meters);
        }

        /// <summary>
        /// Sets how fast this rod chases its slide target, in metres/second.
        ///
        /// Only affects <see cref="SetSlide01"/>, which is what the AI drives. A finger uses
        /// SetSlide01Immediate and owns the position outright, so changing this can never slow a
        /// human player's rod down — it is purely the AI's "hand speed" dial.
        /// </summary>
        public void SetSlideSpeed(float metersPerSecond)
        {
            slideSpeed = Mathf.Max(0f, metersPerSecond);
        }

        /// <summary>A point on the bar's centreline, in world space.</summary>
        public Vector3 BarPivot => Application.isPlaying ? worldPivot : transform.TransformPoint(localPivot);

        /// <summary>
        /// World direction a figure's foot travels when this rod is given a POSITIVE spin.
        ///
        /// The sign convention — which way round the bar a positive angle turns, and whether
        /// invertSpin flips it — lives here rather than being re-derived by callers. An AI can then
        /// work out which way to flick towards a given goal by testing this against that goal's
        /// direction, instead of guessing a sign and being wrong for one of the two teams.
        ///
        /// Figures hang below the bar, so the foot sits at roughly -up from the pivot and its
        /// velocity under angular velocity w is w x r.
        /// </summary>
        public Vector3 ForwardKickDirection
        {
            get
            {
                Vector3 axis = BarAxis * (invertSpin ? -1f : 1f);
                return Vector3.Cross(axis, Vector3.down).normalized;
            }
        }

        /// <summary>Local-space bar direction. The physics builder needs it to orient the bar collider.</summary>
        public Vector3 LocalBarAxis => localBarAxis;

        /// <summary>Local-space point on the bar's centreline.</summary>
        public Vector3 LocalPivot => localPivot;

        public float CurrentSpinAngle => spinAngleDeg;

        /// <summary>
        /// The rod's INTENDED spin velocity — the number the physics model carries between frames.
        /// Note this is zero throughout a finger drag: the input scripts own the angle directly
        /// while a finger is down and hold this at zero. Use <see cref="MeasuredSpinSpeed"/> to ask
        /// how fast the rod is really turning.
        /// </summary>
        public float CurrentSpinVelocity => spinVelocity;

        /// <summary>
        /// How fast the rod is ACTUALLY turning right now, in degrees/second, signed the same way
        /// as the spin angle.
        ///
        /// This exists because there are two ways a rod turns and only one of them shows up in
        /// <see cref="CurrentSpinVelocity"/>. A flick — from the AI, or from releasing a drag —
        /// sets a spin velocity and lets it decay. A finger drag does not: RodTouchInput and
        /// RodPointerInput call SetSpinVelocity(0) and then SetSpinAngle() every single frame, so
        /// the rod can be whipped through a full turn while its spin velocity reads exactly zero.
        ///
        /// Anything reacting to how hard a rod swung — a strike impulse on the ball, a sound, a
        /// visual effect — must read this instead, or it will fire for the AI and never once for
        /// the player.
        /// </summary>
        public float MeasuredSpinSpeed => measuredSpinSpeed;

        /// <summary>
        /// Raised at the end of every Tick, once this step's pose has been written to the transform.
        /// For observers that must read a settled pose rather than a half-updated one — see the call
        /// site at the bottom of <see cref="Tick"/>.
        /// </summary>
        public event System.Action PoseApplied;

        /// <summary>
        /// Set by <see cref="RodAutoLift"/> while it is turning this rod out of the ball's way.
        /// </summary>
        public bool LiftingClear { get; set; }

        /// <summary>
        /// True while this rod is being turned out of the ball's way rather than swung at it.
        ///
        /// Lifting clear turns the rod fast enough to read as a real swing, so without this a rod
        /// getting OUT of the way would kick the ball on its way past — the very thing the auto-lift
        /// exists to stop. Read by <see cref="BallController.ApplyStrikeBoost"/>.
        /// </summary>
        public bool StrikeSuppressed => LiftingClear;

        /// <summary>
        /// How fast the rod is really travelling ALONG its bar right now, in metres/second, signed
        /// the same way as the bar axis.
        ///
        /// Measured rather than derived from slideSpeed for the same reason as the spin: a finger
        /// drag calls SetSlide01Immediate, which puts the rod exactly where the finger is with no
        /// easing at all, so the configured slideSpeed says nothing about how fast the rod actually
        /// moved. A quick drag across the table can shift a rod far in a single frame.
        /// </summary>
        public float MeasuredSlideSpeed => measuredSlideSpeed;

        /// <summary>World-space direction the bar runs along. Slides move along it, spins rotate about it.</summary>
        public Vector3 BarAxis => Application.isPlaying
            ? worldBarAxis
            : transform.TransformDirection(localBarAxis.normalized).normalized;

        private void Awake()
        {
            if (autoDetectAxis)
            {
                DetectBarAxisAndPivot();
            }

            CacheRestPose();

            kinematicBody = GetComponent<Rigidbody>();
            if (kinematicBody != null && !kinematicBody.isKinematic)
            {
                // A dynamic body would fight the transform writes below.
                Debug.LogWarning($"{name}: Rigidbody should be kinematic for rod control.", this);
                kinematicBody = null;
            }

            if (logSetupOnStart)
            {
                LogSetup();
            }
        }

        /// <summary>
        /// Reports what this rod actually starts play with. Useful when an inspector value seems to
        /// have no effect: it shows the value the component really received, which rod carries which
        /// figures, and how far it can travel — so the rod being edited can be told apart from the
        /// rod being watched.
        /// </summary>
        private void LogSetup()
        {
            int figures = 0;
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child != transform && child.name.StartsWith("Fig", System.StringComparison.OrdinalIgnoreCase))
                {
                    figures++;
                }
            }

            string role = figures switch
            {
                1 => "goalkeeper",
                2 => "defence",
                3 => "attack",
                5 => "midfield",
                _ => "?"
            };

            float half = slideRangeMeters * 0.5f;

            Debug.Log($"{name}: {team}, index {rodIndex}, {figures} figure(s) ({role})\n" +
                      $"   slide range {slideRangeMeters:0.###} m  (±{half:0.###} m from centre)\n" +
                      $"   bar axis {BarAxis}  pivot {BarPivot}", this);
        }

        private void CacheRestPose()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
            worldBarAxis = transform.TransformDirection(localBarAxis.normalized).normalized;
            worldPivot = transform.TransformPoint(localPivot);
        }

        // ---------------------------------------------------------------- input API

        /// <summary>Sets where the rod should slide to, 0..1. It eases there at slideSpeed.</summary>
        public void SetSlide01(float t)
        {
            targetSlide01 = Mathf.Clamp01(t);
        }

        /// <summary>Sets the slide position with no easing — for direct finger-follow or AI snapping.</summary>
        public void SetSlide01Immediate(float t)
        {
            targetSlide01 = currentSlide01 = Mathf.Clamp01(t);
        }

        /// <summary>Nudges the slide target by a 0..1-space delta.</summary>
        public void Slide(float delta01)
        {
            SetSlide01(targetSlide01 + delta01);
        }

        /// <summary>Nudges the slide target by a distance in metres.</summary>
        public void SlideMeters(float deltaMeters)
        {
            if (slideRangeMeters > Mathf.Epsilon)
            {
                SetSlide01(targetSlide01 + deltaMeters / slideRangeMeters);
            }
        }

        /// <summary>Held spin input, -1..1. Accelerates the spin while applied.</summary>
        public void ApplySpinInput(float direction)
        {
            spinInput = Mathf.Clamp(direction, -1f, 1f);
        }

        /// <summary>A flick — instantly adds to the spin velocity, in degrees/second.</summary>
        public void FlickSpin(float degreesPerSecond)
        {
            spinVelocity = Mathf.Clamp(spinVelocity + degreesPerSecond, -maxSpinSpeed, maxSpinSpeed);
        }

        /// <summary>Rotates the rod by a fixed amount right now.</summary>
        public void AddSpin(float degrees)
        {
            spinAngleDeg = Mathf.Repeat(spinAngleDeg + degrees, 360f);
        }

        /// <summary>Sets the spin angle outright — for dragging, where the finger owns the angle.</summary>
        public void SetSpinAngle(float degrees)
        {
            spinAngleDeg = Mathf.Repeat(degrees, 360f);
        }

        public void SetSpinVelocity(float degreesPerSecond)
        {
            spinVelocity = Mathf.Clamp(degreesPerSecond, -maxSpinSpeed, maxSpinSpeed);
        }

        /// <summary>
        /// Takes the spin measurement from elsewhere instead of deriving it, for a rod being driven
        /// from another machine.
        ///
        /// <see cref="MeasureSpin"/> works out how fast a rod is turning from how far its angle moved
        /// since last frame, and that is only sound while the angle moves in small steps. A rod
        /// following a remote pose does not: it is chasing updates that arrive at the network's tick
        /// rate, and a whipped rod covers more than half a turn between two of them. The angle is
        /// wrapped to 0..360, so at that point "forward by 200°" and "backward by 160°" are the same
        /// number, and the shorter reading wins — the rod is measured as spinning BACKWARDS.
        ///
        /// Nothing about the rod looks wrong when that happens. What breaks is the ball: the strike
        /// impulse takes its direction from the sign of this measurement, so every hard shot went
        /// back down the table towards the player who took it.
        /// </summary>
        public void SetMeasuredSpin(float degreesPerSecond)
        {
            measuredSpinSpeed = degreesPerSecond;
            spinReportedExternally = true;
        }

        /// <summary>
        /// Goes back to working the spin out locally. For a rod that has just been handed to this
        /// machine, whose angle it is now moving itself in small steps again.
        /// </summary>
        public void ClearMeasuredSpin()
        {
            spinReportedExternally = false;
            previousSpinAngleDeg = spinAngleDeg;
            measuredSpinSpeed = 0f;
        }

        /// <summary>Returns the rod to upright and centred. Used by the reset in Step 4.</summary>
        public void ResetRod()
        {
            targetSlide01 = currentSlide01 = 0.5f;
            spinAngleDeg = 0f;
            spinVelocity = 0f;
            spinInput = 0f;
            previousSpinAngleDeg = 0f;
            measuredSpinSpeed = 0f;
            previousSlideMeters = 0f;
            measuredSlideSpeed = 0f;
            ApplyPose(teleport: true);
        }

        // ---------------------------------------------------------------- driving

        // Without a Rigidbody, run in Update so motion is as smooth as the framerate allows.
        // With one, run in FixedUpdate so PhysX sees the movement and can push the ball.
        private void Update()
        {
            if (kinematicBody == null)
            {
                Tick(Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (kinematicBody != null)
            {
                Tick(Time.fixedDeltaTime);
            }
        }

        private void Tick(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            // Physics is running again, so the interpolator has a live pose to work from and a
            // teleport's suspension of it can be lifted. See ApplyPose.
            if (restoreInterpolation && kinematicBody != null)
            {
                kinematicBody.interpolation = interpolationToRestore;
                restoreInterpolation = false;
            }

            currentSlide01 = slideRangeMeters > Mathf.Epsilon
                ? Mathf.MoveTowards(currentSlide01, targetSlide01, slideSpeed / slideRangeMeters * dt)
                : targetSlide01;

            if (!Mathf.Approximately(spinInput, 0f))
            {
                spinVelocity += spinInput * spinAccel * dt;
            }
            else if (spinDamping > 0f)
            {
                spinVelocity *= Mathf.Exp(-spinDamping * dt);
                if (Mathf.Abs(spinVelocity) < 1f)
                {
                    spinVelocity = 0f;
                }
            }

            spinVelocity = Mathf.Clamp(spinVelocity, -maxSpinSpeed, maxSpinSpeed);
            spinAngleDeg = Mathf.Repeat(spinAngleDeg + spinVelocity * dt, 360f);
            spinInput = 0f; // consumed — input must be re-applied each frame it is held

            MeasureSpin(dt);
            ApplyPose();

            // Fired after the pose for this step is final, so anyone REPORTING the rod reads what it
            // actually became rather than what it was a step ago. NetworkedRod publishes from here:
            // it has to run its Follow before this Tick (so a remote pose reaches PhysX in the same
            // step it arrived) but its Publish after it, and one execution-order attribute cannot say
            // both. An event settles the ordering for the half that needs to be late, and leaves the
            // attribute free to serve the half that needs to be early.
            PoseApplied?.Invoke();
        }

        /// <summary>
        /// Works out how fast the rod really turned this step, whatever moved it.
        ///
        /// Uses DeltaAngle rather than a plain subtraction because the angle is wrapped to 0..360:
        /// a rod passing through zero would otherwise report a 359° jump, which reads as a swing
        /// hundreds of times harder than it was and would fire a full-power strike on a rod barely
        /// moving.
        /// </summary>
        private void MeasureSpin(float dt)
        {
            float slide = CurrentSlideMeters;

            // A rod driven from the network reports its own spin and is believed — see
            // SetMeasuredSpin for why the derivation below cannot be trusted for one. The slide is
            // still measured here: it is a position along a bar, not a wrapped angle, so it has no
            // shorter way round to be mistaken for.
            if (!spinReportedExternally)
            {
                float instant = Mathf.DeltaAngle(previousSpinAngleDeg, spinAngleDeg) / dt;

                // Light smoothing: enough to swallow a single noisy input frame, fast enough that
                // the peak of a swing is still there at the moment of contact.
                measuredSpinSpeed = Mathf.Lerp(measuredSpinSpeed, instant, 0.5f);
            }

            previousSpinAngleDeg = spinAngleDeg;

            measuredSlideSpeed = Mathf.Lerp(measuredSlideSpeed,
                                            (slide - previousSlideMeters) / dt, 0.5f);
            previousSlideMeters = slide;
        }

        /// <summary>
        /// Writes the rod's pose from its rest pose, spin angle and slide.
        ///
        /// <paramref name="teleport"/> decides HOW it lands, and that matters more than it looks. A
        /// kinematic body normally moves through MovePosition/MoveRotation, which is what lets a
        /// swinging rod hand its momentum to the ball - but those are requests PhysX carries out on
        /// the next PHYSICS step, and no physics step runs while the world sits at timeScale 0. A
        /// reset issued to a frozen table therefore sits in the queue, invisible, until play resumes:
        /// the rods appear to snap back to centre AFTER the countdown instead of before it.
        ///
        /// Teleporting writes the transform and PhysX's own copy of the pose together, so a reset
        /// lands immediately whether the clock is running or not.
        /// </summary>
        private void ApplyPose(bool teleport = false)
        {
            float angle = invertSpin ? -spinAngleDeg : spinAngleDeg;
            Quaternion spin = Quaternion.AngleAxis(angle, worldBarAxis);
            Vector3 slide = worldBarAxis * ((currentSlide01 - 0.5f) * slideRangeMeters);

            // Rebuild from the rest pose each time: rotate about the bar's centreline, then slide.
            Quaternion rotation = spin * homeRotation;
            Vector3 position = worldPivot + spin * (homePosition - worldPivot) + slide;

            if (kinematicBody != null && !teleport)
            {
                kinematicBody.MovePosition(position);
                kinematicBody.MoveRotation(rotation);
                return;
            }

            if (kinematicBody == null)
            {
                transform.SetPositionAndRotation(position, rotation);
                return;
            }

            // Two separate things have to be defeated to move an interpolated kinematic body while
            // the world is stopped, and fixing either one alone changes nothing on screen:
            //
            //   1. MovePosition/MoveRotation are carried out on the next PHYSICS step, and a frozen
            //      world runs none - so the move sits queued and invisible. Hence writing the pose
            //      directly rather than requesting it.
            //   2. Interpolation rewrites the Transform every FRAME from PhysX's buffered poses, so
            //      a direct write is overwritten before it is ever drawn. That buffer is only
            //      refreshed by a physics step either, so at timeScale 0 it holds the stale pose
            //      indefinitely and the rod springs into place only once play resumes.
            //
            // Interpolation stays OFF until physics actually runs again, rather than being
            // restored on the spot. Restoring it here would put the interpolator straight back
            // to work rewriting this transform every frame, and while the world is frozen it has
            // no fresh physics pose to work from - which is the whole reason the rod appeared to
            // reset late. Tick only runs off FixedUpdate, so it fires on the first step after
            // play resumes, which is exactly when interpolation becomes meaningful again.
            if (!restoreInterpolation)
            {
                interpolationToRestore = kinematicBody.interpolation;
                restoreInterpolation = true;
            }

            kinematicBody.interpolation = RigidbodyInterpolation.None;
            kinematicBody.position = position;
            kinematicBody.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        // ---------------------------------------------------------------- axis detection

        /// <summary>
        /// Works out which way the bar runs and where its centreline sits, from the bar mesh's own
        /// bounds. Uses only this object's mesh where possible — the player figures hang off one
        /// side, so including them would drag the centreline off the bar.
        /// </summary>
        [ContextMenu("Detect bar axis")]
        public void DetectBarAxisAndPivot()
        {
            if (!TryGetLocalBounds(out Bounds bounds))
            {
                Debug.LogWarning($"{name}: no mesh found, keeping the axis as it is.", this);
                return;
            }

            Vector3 size = bounds.size;
            if (size.x >= size.y && size.x >= size.z)
            {
                localBarAxis = Vector3.right;
            }
            else if (size.y >= size.x && size.y >= size.z)
            {
                localBarAxis = Vector3.up;
            }
            else
            {
                localBarAxis = Vector3.forward;
            }

            // Any point on the centreline works, so keep the origin's own position along the bar
            // and only correct the two perpendicular directions.
            Vector3 pivot = bounds.center;
            pivot = Vector3.Scale(pivot, Vector3.one - Abs(localBarAxis));
            localPivot = pivot;

            CacheRestPose();
        }

        private bool TryGetLocalBounds(out Bounds bounds)
        {
            // The bar's own mesh is the reliable source — a cylinder whose bounds centre is on axis.
            MeshFilter own = GetComponent<MeshFilter>();
            if (own != null && own.sharedMesh != null)
            {
                bounds = own.sharedMesh.bounds;
                return true;
            }

            // Empty pivot object: fall back to whatever meshes hang under it.
            bool found = false;
            bounds = new Bounds();
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                Bounds meshBounds = filter.sharedMesh.bounds;
                foreach (Vector3 corner in Corners(meshBounds))
                {
                    Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!found)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return found;
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            Vector3 min = b.min;
            Vector3 max = b.max;
            yield return new Vector3(min.x, min.y, min.z);
            yield return new Vector3(max.x, min.y, min.z);
            yield return new Vector3(min.x, max.y, min.z);
            yield return new Vector3(max.x, max.y, min.z);
            yield return new Vector3(min.x, min.y, max.z);
            yield return new Vector3(max.x, min.y, max.z);
            yield return new Vector3(min.x, max.y, max.z);
            yield return new Vector3(max.x, max.y, max.z);
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        // ---------------------------------------------------------------- editor helpers

        /// <summary>
        /// Pulls this rod's handles and its nearest player figures in as children, so that sliding
        /// and spinning the rod carries them along. Figures go to whichever rod's bar they sit
        /// closest to. Also fills in the rod index and team from what it finds.
        /// </summary>
        [ContextMenu("Auto-parent nearby figures & handles")]
        public void AutoParentFiguresAndHandles()
        {
            Transform tableRoot = transform.parent != null ? transform.parent : transform;

            if (autoDetectAxis)
            {
                DetectBarAxisAndPivot();
            }
            else
            {
                // Awake has not run in edit mode, so the world axis would still be zero.
                CacheRestPose();
            }

            int parsedIndex = ParseTrailingIndex(name);
            var rods = new List<Transform>();
            foreach (Transform candidate in tableRoot)
            {
                if (candidate.name.StartsWith("Rod", System.StringComparison.OrdinalIgnoreCase))
                {
                    rods.Add(candidate);
                }
            }

            int adopted = 0;
            int red = 0;
            int blue = 0;

            // Copy first: reparenting mutates the child list we would otherwise be iterating.
            var children = new List<Transform>();
            foreach (Transform child in tableRoot)
            {
                children.Add(child);
            }

            foreach (Transform child in children)
            {
                if (child == transform)
                {
                    continue;
                }

                bool isFigure = child.name.StartsWith("Fig", System.StringComparison.OrdinalIgnoreCase);
                bool isHandle = child.name.StartsWith("Handle", System.StringComparison.OrdinalIgnoreCase);
                if (!isFigure && !isHandle)
                {
                    continue;
                }

                bool mine;
                if (isHandle)
                {
                    // Handle_3_1 / Handle_3_-1 belong to Rod_3.
                    mine = parsedIndex >= 0 && ParseHandleRodIndex(child.name) == parsedIndex;
                }
                else
                {
                    mine = NearestRod(child.position, rods) == transform;
                }

                if (!mine)
                {
                    continue;
                }

                Reparent(child, transform);
                adopted++;

                if (isFigure)
                {
                    if (child.name.StartsWith("FigR", System.StringComparison.OrdinalIgnoreCase)) red++;
                    else if (child.name.StartsWith("FigB", System.StringComparison.OrdinalIgnoreCase)) blue++;
                }
            }

            if (parsedIndex >= 0)
            {
                rodIndex = Mathf.Clamp(parsedIndex, 0, 7);
            }

            if (red > 0 || blue > 0)
            {
                team = red >= blue ? Team.Red : Team.Blue;
            }

            CacheRestPose();
            Debug.Log($"{name}: adopted {adopted} object(s) — {red} red, {blue} blue figures. Team = {team}.", this);
        }

        private Transform NearestRod(Vector3 point, List<Transform> rods)
        {
            Transform best = null;
            float bestDistance = float.MaxValue;

            foreach (Transform rod in rods)
            {
                // Distance from the figure to the rod's bar line, ignoring position along the bar.
                Vector3 axis = rod == transform
                    ? worldBarAxis
                    : rod.TransformDirection(localBarAxis.normalized).normalized;
                Vector3 offset = point - rod.position;
                float distance = Vector3.ProjectOnPlane(offset, axis).magnitude;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = rod;
                }
            }

            return best;
        }

        private static void Reparent(Transform child, Transform parent)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.SetTransformParent(child, parent, "Auto-parent rod children");
                return;
            }
#endif
            child.SetParent(parent, true);
        }

        private static int ParseTrailingIndex(string objectName)
        {
            int i = objectName.Length - 1;
            while (i >= 0 && char.IsDigit(objectName[i]))
            {
                i--;
            }

            string digits = objectName.Substring(i + 1);
            return int.TryParse(digits, out int value) ? value : -1;
        }

        private static int ParseHandleRodIndex(string handleName)
        {
            // Handle_<rod>_<side>
            string[] parts = handleName.Split('_');
            return parts.Length >= 2 && int.TryParse(parts[1], out int value) ? value : -1;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 axis = Application.isPlaying
                ? worldBarAxis
                : transform.TransformDirection(localBarAxis.normalized).normalized;
            Vector3 pivot = Application.isPlaying ? worldPivot : transform.TransformPoint(localPivot);
            Vector3 centre = Application.isPlaying
                ? pivot + axis * ((currentSlide01 - 0.5f) * slideRangeMeters)
                : pivot;
            float half = slideRangeMeters * 0.5f;

            // The bar line.
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(pivot - axis * 0.5f, pivot + axis * 0.5f);

            // The travel limits.
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pivot - axis * half, pivot + axis * half);
            Gizmos.DrawSphere(pivot - axis * half, 0.008f);
            Gizmos.DrawSphere(pivot + axis * half, 0.008f);

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(centre, 0.01f);
        }
    }
}
