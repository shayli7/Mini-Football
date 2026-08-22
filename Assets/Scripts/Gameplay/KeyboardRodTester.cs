using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TableFootball
{
    /// <summary>
    /// TEMPORARY — a keyboard harness so the rods can be tried out in the Editor during Step 1.
    /// Step 2 replaces this with touch swipes; delete this component then. Nothing else depends on
    /// it: it only calls the public RodController API, exactly as the touch and AI drivers will.
    ///
    /// Red team:   1-4 pick a rod, A/D slide, Q/E spin (tap = flick, hold = wind up).
    /// Blue team:  7-0 pick a rod, arrows slide, PageUp/PageDown spin.
    /// R resets every rod.
    /// </summary>
    public class KeyboardRodTester : MonoBehaviour
    {
        [SerializeField] private TableReferences table;

        [Header("Feel")]
        [Tooltip("How fast the slide target moves while a slide key is held, in 0..1 per second.")]
        [SerializeField] private float slideRate = 1.2f;
        [Tooltip("Spin velocity added by a single tap, in degrees/second.")]
        [SerializeField] private float flickStrength = 900f;
        [Tooltip("A key held longer than this winds the rod up instead of flicking it.")]
        [SerializeField] private float holdThreshold = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool logSelection = true;

        /// <summary>Per-team scratch state for rod selection and spin timing.</summary>
        private struct SideState
        {
            public int rodIndex;
            public float spinHeldFor;
            public float lastSpinDirection;
        }

        private SideState red;
        private SideState blue;

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

            if (table == null)
            {
                Debug.LogError($"{name}: no TableReferences assigned or found — rods cannot be driven.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                table.ResetAllRods();
            }

            SelectRod(Team.Red, ref red,
                keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key);
            SelectRod(Team.Blue, ref blue,
                keyboard.digit7Key, keyboard.digit8Key, keyboard.digit9Key, keyboard.digit0Key);

            DriveRod(
                Get(Team.Red, red.rodIndex),
                keyboard.aKey, keyboard.dKey,
                keyboard.qKey, keyboard.eKey,
                ref red);

            DriveRod(
                Get(Team.Blue, blue.rodIndex),
                keyboard.leftArrowKey, keyboard.rightArrowKey,
                keyboard.pageUpKey, keyboard.pageDownKey,
                ref blue);
        }

        private void SelectRod(Team team, ref SideState state,
            KeyControl a, KeyControl b, KeyControl c, KeyControl d)
        {
            int picked = -1;
            if (a.wasPressedThisFrame) picked = 0;
            else if (b.wasPressedThisFrame) picked = 1;
            else if (c.wasPressedThisFrame) picked = 2;
            else if (d.wasPressedThisFrame) picked = 3;

            if (picked < 0)
            {
                return;
            }

            var rods = table.RodsFor(team);
            if (picked >= rods.Count)
            {
                return;
            }

            state.rodIndex = picked;
            if (logSelection)
            {
                Debug.Log($"{team} rod {picked} selected ({rods[picked].name}).", rods[picked]);
            }
        }

        private void DriveRod(RodController rod,
            KeyControl slideNegative, KeyControl slidePositive,
            KeyControl spinNegative, KeyControl spinPositive,
            ref SideState state)
        {
            if (rod == null)
            {
                return;
            }

            float slide = (slidePositive.isPressed ? 1f : 0f) - (slideNegative.isPressed ? 1f : 0f);
            if (!Mathf.Approximately(slide, 0f))
            {
                rod.Slide(slide * slideRate * Time.deltaTime);
            }

            float spin = (spinPositive.isPressed ? 1f : 0f) - (spinNegative.isPressed ? 1f : 0f);

            if (!Mathf.Approximately(spin, 0f))
            {
                state.spinHeldFor += Time.deltaTime;
                state.lastSpinDirection = spin;

                // Held past the threshold? Wind the rod up continuously instead of flicking it.
                if (state.spinHeldFor >= holdThreshold)
                {
                    rod.ApplySpinInput(spin);
                }
            }
            else
            {
                // Let go before the threshold — that was a tap, so flick the rod.
                if (state.spinHeldFor > 0f && state.spinHeldFor < holdThreshold)
                {
                    rod.FlickSpin(state.lastSpinDirection * flickStrength);
                }

                state.spinHeldFor = 0f;
            }
        }

        private RodController Get(Team team, int index)
        {
            var rods = table.RodsFor(team);
            return index >= 0 && index < rods.Count ? rods[index] : null;
        }
    }
}
