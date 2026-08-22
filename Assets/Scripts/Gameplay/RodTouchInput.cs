using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace TableFootball
{
    /// <summary>
    /// Multi-touch rod control. Every finger grabs its own rod and plays it independently, so both
    /// hands — and both players on a shared screen — work at the same time.
    ///
    ///   press on a bar          -> that finger takes the rod
    ///   drag along the bar      -> slides it, tracking the finger 1:1 in world metres
    ///   drag across the bar     -> spins it
    ///   flick across, release   -> it keeps spinning, then settles
    ///
    /// On a touchscreen this reads every active finger. With no touchscreen it falls back to the
    /// mouse as a single finger, so the Editor still plays. Same code either way.
    ///
    /// Replaces RodPointerInput (single pointer) from Step 1.
    /// </summary>
    public class RodTouchInput : MonoBehaviour
    {
        [SerializeField] private TableReferences table;
        [Tooltip("Defaults to Camera.main.")]
        [SerializeField] private Camera view;

        [Header("Grabbing")]
        [Tooltip("How close (in metres) a press must be to a bar to grab it.")]
        [SerializeField] private float grabRadius = 0.12f;
        [SerializeField] private bool restrictToOneTeam = false;
        [SerializeField] private Team controllableTeam = Team.Red;

        [Header("Feel")]
        [Tooltip("Spin from dragging the full height of the screen across the bar. Screen-relative, " +
                 "so it feels the same on every device. High values whip the figures round from a " +
                 "small finger movement, which launches the ball off every touch — lower this if " +
                 "you cannot nudge the ball gently.")]
        [SerializeField] private float spinDegreesPerScreenHeight = 540f;
        [Tooltip("Scales the flick given on release. Lower it for finer control, 0 disables flicking.")]
        [SerializeField] private float flickStrength = 0.9f;
        [Tooltip("Ceiling on the release flick, in degrees/second. Keep it near the rod's own Max " +
                 "Spin Speed — clamping well below that is a quiet power cap on every shot.")]
        [SerializeField] private float maxFlick = 1600f;

        [Header("Debug")]
        [SerializeField] private bool logGrabs = false;

        /// <summary>One finger's grip on one rod. Each drag is independent of the others.</summary>
        private class Grip
        {
            public RodController Rod;
            public Vector2 PressPosition;
            public float PressSlide01;
            public float PressSpinAngle;
            public float AcrossNormalised;
            public float AcrossVelocity;
        }

        private readonly Dictionary<int, Grip> grips = new Dictionary<int, Grip>();
        private readonly List<int> seenThisFrame = new List<int>();
        private readonly List<int> toRelease = new List<int>();
        private static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

        private const int MousePointerId = -1;

        /// <summary>
        /// Limits which team this input may grab, for the mode select: restricted when playing the
        /// AI so the player cannot reach their opponent's rods, unrestricted head to head so two
        /// people can share one screen.
        ///
        /// Any grips currently held are dropped — a rod grabbed under the previous rule would
        /// otherwise stay attached to a finger that is no longer allowed to play it.
        /// </summary>
        public void SetTeamRestriction(bool restrict, Team team)
        {
            restrictToOneTeam = restrict;
            controllableTeam = team;
            ReleaseAll();
        }

        /// <summary>
        /// Does a human play this team on this device?
        ///
        /// Head to head both teams are human; against the AI only the restricted team is. Asked
        /// by <see cref="RodAutoLift"/>, which must never move the AI's rods - the AI already
        /// manages its own figures through RodAgent, and a second hand on them would fight it.
        /// </summary>
        public bool HumanControls(Team team)
        {
            return !restrictToOneTeam || team == controllableTeam;
        }

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
                               "MainCamera?). Touch control disabled.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
            ReleaseAll();
        }

        private void Update()
        {
            // The menu pauses the game with timeScale 0. Without this the rods would still follow
            // fingers behind the menu, and letting go of a grip on resume would fling one.
            if (Time.timeScale <= 0f)
            {
                ReleaseAll();
                return;
            }

            seenThisFrame.Clear();

            if (Touchscreen.current != null)
            {
                ReadTouches();
            }
            else
            {
                ReadMouse();
            }

            DropStaleGrips();
        }

        private void ReadTouches()
        {
            foreach (Touch touch in Touch.activeTouches)
            {
                int id = touch.touchId;
                seenThisFrame.Add(id);

                switch (touch.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        Grab(id, touch.screenPosition);
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        Drag(id, touch.screenPosition);
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        Release(id);
                        seenThisFrame.Remove(id);
                        break;
                }
            }
        }

        private void ReadMouse()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null)
            {
                return;
            }

            Vector2 position = pointer.position.ReadValue();

            if (pointer.press.wasPressedThisFrame)
            {
                Grab(MousePointerId, position);
                seenThisFrame.Add(MousePointerId);
            }
            else if (pointer.press.wasReleasedThisFrame)
            {
                Release(MousePointerId);
            }
            else if (pointer.press.isPressed)
            {
                Drag(MousePointerId, position);
                seenThisFrame.Add(MousePointerId);
            }
        }

        /// <summary>Cleans up fingers that vanished without an Ended phase (app pause, focus loss).</summary>
        private void DropStaleGrips()
        {
            toRelease.Clear();
            foreach (int id in grips.Keys)
            {
                if (!seenThisFrame.Contains(id))
                {
                    toRelease.Add(id);
                }
            }

            foreach (int id in toRelease)
            {
                Release(id);
            }
        }

        private void Grab(int pointerId, Vector2 position)
        {
            if (grips.ContainsKey(pointerId))
            {
                return;
            }

            // This reads pointers straight from the device rather than through the EventSystem, so
            // without an explicit check a tap on a menu button would also grab the rod behind it.
            // Only grabbing is blocked: a drag that began on the table is allowed to continue even
            // if the finger later passes under a HUD element.
            if (IsPointerOverUI(position))
            {
                return;
            }

            RodController rod = FindFreeRodUnder(position);
            if (rod == null)
            {
                return;
            }

            rod.SetSpinVelocity(0f);

            grips[pointerId] = new Grip
            {
                Rod = rod,
                PressPosition = position,
                PressSlide01 = rod.CurrentSlide01,
                PressSpinAngle = rod.CurrentSpinAngle,
                AcrossNormalised = 0f,
                AcrossVelocity = 0f
            };

            if (logGrabs)
            {
                Debug.Log($"Finger {pointerId} grabbed {rod.Team} rod {rod.RodIndex} ({rod.name}).", rod);
            }
        }

        private void Drag(int pointerId, Vector2 position)
        {
            if (!grips.TryGetValue(pointerId, out Grip grip) || grip.Rod == null)
            {
                return;
            }

            GetScreenFrame(grip.Rod, out Vector2 barOnScreen, out float pixelsPerMetre);

            Vector2 drag = position - grip.PressPosition;
            float along = Vector2.Dot(drag, barOnScreen);
            float across = drag.x * barOnScreen.y - drag.y * barOnScreen.x;

            // Slide: back into world metres, so the rod stays under the finger at any zoom.
            if (pixelsPerMetre > 1f && grip.Rod.SlideRangeMeters > Mathf.Epsilon)
            {
                float metres = along / pixelsPerMetre;
                grip.Rod.SetSlide01Immediate(grip.PressSlide01 + metres / grip.Rod.SlideRangeMeters);
            }

            // Spin: measured as a fraction of screen height so it feels identical on any device.
            float acrossNormalised = across / Mathf.Max(Screen.height, 1);
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            grip.AcrossVelocity = (acrossNormalised - grip.AcrossNormalised) / dt;
            grip.AcrossNormalised = acrossNormalised;

            grip.Rod.SetSpinVelocity(0f);
            grip.Rod.SetSpinAngle(grip.PressSpinAngle + acrossNormalised * spinDegreesPerScreenHeight);
        }

        private void Release(int pointerId)
        {
            if (!grips.TryGetValue(pointerId, out Grip grip))
            {
                return;
            }

            grips.Remove(pointerId);

            if (grip.Rod == null || flickStrength <= 0f)
            {
                return;
            }

            float flick = grip.AcrossVelocity * spinDegreesPerScreenHeight * flickStrength;
            grip.Rod.FlickSpin(Mathf.Clamp(flick, -maxFlick, maxFlick));
        }

        private void ReleaseAll()
        {
            toRelease.Clear();
            toRelease.AddRange(grips.Keys);
            foreach (int id in toRelease)
            {
                Release(id);
            }
        }

        /// <summary>Where the bar points on screen, and how many pixels one world metre spans.</summary>
        private void GetScreenFrame(RodController rod, out Vector2 barOnScreen, out float pixelsPerMetre)
        {
            Vector3 origin = rod.BarPivot;
            Vector3 screenOrigin = view.WorldToScreenPoint(origin);
            Vector3 screenAlong = view.WorldToScreenPoint(origin + rod.BarAxis);

            Vector2 delta = (Vector2)(screenAlong - screenOrigin);
            pixelsPerMetre = delta.magnitude;
            barOnScreen = pixelsPerMetre > 1e-3f ? delta / pixelsPerMetre : Vector2.right;
        }

        /// <summary>
        /// Nearest bar to the press that no other finger already holds. Needs no colliders: it
        /// measures how close the pointer ray passes to each bar's centreline.
        /// </summary>
        private RodController FindFreeRodUnder(Vector2 position)
        {
            Ray ray = view.ScreenPointToRay(position);

            RodController best = null;
            float bestDistance = grabRadius;

            Evaluate(table.RedRods);
            Evaluate(table.BlueRods);
            return best;

            void Evaluate(IReadOnlyList<RodController> rods)
            {
                foreach (RodController rod in rods)
                {
                    if (rod == null || (restrictToOneTeam && rod.Team != controllableTeam) || IsHeld(rod))
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

        /// <summary>True while a finger is on this rod. Public so RodAutoLift can leave held rods alone.</summary>
        public bool IsHeld(RodController rod)
        {
            foreach (Grip grip in grips.Values)
            {
                if (grip.Rod == rod)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Is there UI under this screen position? Tested by raycasting the canvas at the position
        /// rather than by pointer id: the id EnhancedTouch reports is not the id the EventSystem
        /// assigns, so an id-based check quietly fails to match on a touchscreen.
        /// </summary>
        private static bool IsPointerOverUI(Vector2 screenPosition)
        {
            EventSystem events = EventSystem.current;
            if (events == null)
            {
                return false;
            }

            var pointer = new PointerEventData(events) { position = screenPosition };
            uiHits.Clear();
            events.RaycastAll(pointer, uiHits);
            return uiHits.Count > 0;
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
