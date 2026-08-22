using UnityEngine;
using UnityEngine.InputSystem;

namespace TableFootball
{
    /// <summary>
    /// Grab a rod by pressing on it, then drag to play it:
    ///   drag along the bar        -> slides the rod, tracking the finger 1:1
    ///   drag across the bar       -> spins the rod
    ///   flick across and release  -> the rod keeps spinning, then settles
    ///
    /// Driven by <see cref="Pointer"/>, so the same code is the mouse in the Editor and a finger on
    /// the phone — the playtest controls and the shipping controls are the same thing.
    ///
    /// Rod picking needs no colliders: it measures how close the pointer ray passes to each bar's
    /// centreline and takes the nearest. That keeps Step 1 free of physics.
    ///
    /// One pointer drives one rod at a time. Step 2 extends this to multi-touch so both hands can
    /// play at once; the per-rod maths below is already written to be per-pointer.
    /// </summary>
    public class RodPointerInput : MonoBehaviour
    {
        [SerializeField] private TableReferences table;
        [Tooltip("Defaults to Camera.main.")]
        [SerializeField] private Camera view;

        [Header("Selection")]
        [Tooltip("How close (in metres) the press must be to a bar to grab it.")]
        [SerializeField] private float grabRadius = 0.12f;
        [Tooltip("Keep hold of the last rod when a press misses, instead of dropping it.")]
        [SerializeField] private bool keepSelectionOnMiss = true;
        [SerializeField] private bool restrictToOneTeam = false;
        [SerializeField] private Team controllableTeam = Team.Red;
        [SerializeField] private bool logSelection = true;

        [Header("Feel")]
        [Tooltip("Degrees of spin per pixel dragged across the bar.")]
        [SerializeField] private float spinDegreesPerPixel = 1.2f;
        [Tooltip("Scales the release flick. 0 disables flicking.")]
        [SerializeField] private float flickStrength = 1f;
        [SerializeField] private float maxFlick = 1440f;

        private RodController selected;
        private bool dragging;

        // Pose captured when the drag started — the drag is applied as an absolute offset from it,
        // so the rod tracks the finger exactly and cannot accumulate drift.
        private Vector2 pressPosition;
        private float pressSlide01;
        private float pressSpinAngle;

        // Screen-space frame of the grabbed bar, refreshed each frame as the rod moves.
        private Vector2 barDirectionOnScreen;
        private float pixelsPerMetre;

        private float perpendicularPixels;
        private float perpendicularVelocity;

        public RodController Selected => selected;

        private void Reset()
        {
            table = GetComponentInParent<TableReferences>();
            if (table == null)
            {
                table = FindAnyObjectByType<TableReferences>();
            }
        }

        private void Awake()
        {
            if (table == null)
            {
                table = FindAnyObjectByType<TableReferences>();
            }

            if (view == null)
            {
                view = Camera.main;
            }

            if (table == null || view == null)
            {
                Debug.LogError($"{name}: needs a TableReferences and a camera (is the camera tagged " +
                               "MainCamera?). Pointer control disabled.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null)
            {
                return;
            }

            Vector2 position = pointer.position.ReadValue();

            if (pointer.press.wasPressedThisFrame)
            {
                BeginDrag(position);
            }
            else if (dragging && pointer.press.isPressed)
            {
                ContinueDrag(position);
            }
            else if (dragging && pointer.press.wasReleasedThisFrame)
            {
                EndDrag();
            }
        }

        private void BeginDrag(Vector2 position)
        {
            RodController rod = FindRodUnder(position);

            if (rod == null)
            {
                if (!keepSelectionOnMiss)
                {
                    selected = null;
                }

                if (selected == null)
                {
                    return;
                }

                rod = selected;
            }
            else if (rod != selected)
            {
                selected = rod;
                if (logSelection)
                {
                    Debug.Log($"Controlling {rod.Team} rod {rod.RodIndex} ({rod.name}).", rod);
                }
            }

            dragging = true;
            pressPosition = position;
            pressSlide01 = rod.CurrentSlide01;
            pressSpinAngle = rod.CurrentSpinAngle;
            perpendicularPixels = 0f;
            perpendicularVelocity = 0f;

            rod.SetSpinVelocity(0f);
            CacheScreenFrame(rod);
        }

        private void ContinueDrag(Vector2 position)
        {
            if (selected == null)
            {
                dragging = false;
                return;
            }

            CacheScreenFrame(selected);

            Vector2 drag = position - pressPosition;
            float along = Vector2.Dot(drag, barDirectionOnScreen);
            float across = drag.x * barDirectionOnScreen.y - drag.y * barDirectionOnScreen.x;

            // Slide: convert the on-screen drag back into metres so the rod stays under the finger.
            if (pixelsPerMetre > 1f && selected.SlideRangeMeters > Mathf.Epsilon)
            {
                float metres = along / pixelsPerMetre;
                selected.SetSlide01Immediate(pressSlide01 + metres / selected.SlideRangeMeters);
            }

            // Spin: the finger owns the angle while the drag lasts.
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            perpendicularVelocity = (across - perpendicularPixels) / dt;
            perpendicularPixels = across;

            selected.SetSpinVelocity(0f);
            selected.SetSpinAngle(pressSpinAngle + across * spinDegreesPerPixel);
        }

        private void EndDrag()
        {
            dragging = false;

            if (selected == null || flickStrength <= 0f)
            {
                return;
            }

            float flick = perpendicularVelocity * spinDegreesPerPixel * flickStrength;
            selected.FlickSpin(Mathf.Clamp(flick, -maxFlick, maxFlick));
        }

        /// <summary>Refreshes where the grabbed bar points on screen, and its pixels-per-metre scale.</summary>
        private void CacheScreenFrame(RodController rod)
        {
            Vector3 origin = rod.BarPivot;
            Vector3 screenOrigin = view.WorldToScreenPoint(origin);
            Vector3 screenAlong = view.WorldToScreenPoint(origin + rod.BarAxis);

            Vector2 delta = (Vector2)(screenAlong - screenOrigin);
            pixelsPerMetre = delta.magnitude;
            barDirectionOnScreen = pixelsPerMetre > 1e-3f ? delta / pixelsPerMetre : Vector2.right;
        }

        private RodController FindRodUnder(Vector2 position)
        {
            Ray ray = view.ScreenPointToRay(position);

            RodController best = null;
            float bestDistance = grabRadius;

            Evaluate(table.RedRods);
            Evaluate(table.BlueRods);
            return best;

            void Evaluate(System.Collections.Generic.IReadOnlyList<RodController> rods)
            {
                foreach (RodController rod in rods)
                {
                    if (rod == null || (restrictToOneTeam && rod.Team != controllableTeam))
                    {
                        continue;
                    }

                    float distance = RayToLineDistance(ray, rod.BarPivot, rod.BarAxis);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = rod;
                    }
                }
            }
        }

        /// <summary>Shortest distance between a ray and an infinite line — used to pick a bar.</summary>
        private static float RayToLineDistance(Ray ray, Vector3 linePoint, Vector3 lineDirection)
        {
            Vector3 cross = Vector3.Cross(ray.direction, lineDirection);
            float denominator = cross.magnitude;

            if (denominator < 1e-6f)
            {
                // Looking straight down the bar: fall back to point-to-ray distance.
                return Vector3.Cross(linePoint - ray.origin, ray.direction.normalized).magnitude;
            }

            return Mathf.Abs(Vector3.Dot(linePoint - ray.origin, cross / denominator));
        }
    }
}
