using System;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// TEMPORARY DIAGNOSTIC — delete once the online ball is behaving.
    ///
    /// Measures what the player SEES rather than what the network sends: how evenly the frames come, and
    /// how evenly the ball moves from one frame to the next. Every two seconds it logs one
    /// <c>[BallSmooth]</c> line, split by what this machine was doing with the ball, so an offline match
    /// and an online one can be compared number for number.
    ///
    /// - <c>dt</c>: median and worst frame time, and <c>hitches</c> — frames over twice the median.
    /// - <c>jerk</c>: the median change in the ball's on-screen speed between consecutive frames, as a
    ///   fraction of that speed. A smoothly moving ball changes speed a little each frame (a few
    ///   percent); a stuttering one jumps between big and small steps.
    /// - <c>stalls</c>: frames where a moving ball barely moved on screen, between frames where it did.
    /// - <c>sim/prov/draw</c>: simulating, simulating on a provisional start, or drawing the other
    ///   machine's ball. <c>switch</c>: the first <see cref="SwitchSeconds"/> after changing between
    ///   them, kept apart so a hand over's own frames cannot colour the steady numbers.
    /// - <c>steps=same%/changed%</c> (simulating only): jerk on frames that ran the same number of
    ///   physics steps as the frame before, against frames that ran a different number. With working
    ///   Rigidbody interpolation the two match; if "changed" is far higher, the ball is being drawn at
    ///   raw physics positions, hopping one or two steps a frame.
    ///
    /// Creates itself at startup, so nothing has to be added to the scene.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after NetworkedBall's LateUpdate has placed a drawn ball
    public sealed class BallSmoothnessProbe : MonoBehaviour
    {
        private const float WindowSeconds = 2f;
        private const float SwitchSeconds = 0.1f;
        private const float MovingSpeed = 0.3f;
        private const int Capacity = 512;

        private BallController ball;
        private Rigidbody body;
        private Net.NetworkedBall networked;
        private Vector3 lastPos;
        private float lastSpeed = -1f;
        private bool hasLast;
        private float windowEnd;

        private Stats lastBucket;
        private float switchedAt = -1f;
        private int stepsThisFrame;
        private int lastSteps = -1;

        private readonly float[] frameTimes = new float[Capacity];
        private int frameCount;

        private readonly Stats simulating = new Stats();
        private readonly Stats provisional = new Stats();
        private readonly Stats drawing = new Stats();
        private readonly Stats switching = new Stats();
        private readonly Stats stepsSame = new Stats();
        private readonly Stats stepsChanged = new Stats();

        private sealed class Stats
        {
            public readonly float[] Jerk = new float[Capacity];
            public int Count;
            public int Stalls;
            public int Frames;

            public void Clear() => Count = Stalls = Frames = 0;

            public void Add(float jerk)
            {
                if (Count < Capacity) Jerk[Count++] = jerk;
            }

            public float Median()
            {
                if (Count == 0) return 0f;
                Array.Sort(Jerk, 0, Count);
                return Jerk[Count / 2];
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("BallSmoothnessProbe");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            go.AddComponent<BallSmoothnessProbe>();
        }

        private void FixedUpdate() => stepsThisFrame++;

        private void LateUpdate()
        {
            int steps = stepsThisFrame;
            stepsThisFrame = 0;

            if (ball == null)
            {
                ball = FindAnyObjectByType<BallController>();
                hasLast = false;
                if (ball == null) return;
                body = ball.GetComponent<Rigidbody>();
                networked = ball.GetComponent<Net.NetworkedBall>();
            }

            float dt = Time.unscaledDeltaTime;
            if (frameCount < Capacity) frameTimes[frameCount++] = dt;

            // Online, a disabled BallController means this machine is only drawing the ball; a
            // provisional start is simulation that may still be cancelled, counted on its own.
            Stats bucket = !ball.enabled ? drawing
                         : networked != null && networked.IsProvisional ? provisional
                         : simulating;

            if (bucket != lastBucket)
            {
                if (lastBucket != null) switchedAt = Time.unscaledTime;
                lastBucket = bucket;
            }

            Stats stats = switchedAt >= 0f && Time.unscaledTime - switchedAt < SwitchSeconds ? switching : bucket;
            stats.Frames++;

            Vector3 pos = ball.transform.position;
            if (hasLast && dt > 0f && Time.timeScale > 0f)
            {
                float speed = new Vector3(pos.x - lastPos.x, 0f, pos.z - lastPos.z).magnitude / dt;

                if (lastSpeed >= MovingSpeed)
                {
                    float jerk = Mathf.Abs(speed - lastSpeed) / ((speed + lastSpeed) * 0.5f);
                    stats.Add(jerk);
                    if (speed < lastSpeed * 0.3f) stats.Stalls++;

                    if (stats == simulating && lastSteps >= 0)
                    {
                        (steps == lastSteps ? stepsSame : stepsChanged).Add(jerk);
                    }
                }

                lastSpeed = speed;
            }
            else
            {
                lastSpeed = -1f;
            }

            lastPos = pos;
            lastSteps = steps;
            hasLast = true;

            if (Time.unscaledTime >= windowEnd)
            {
                Report();
                windowEnd = Time.unscaledTime + WindowSeconds;
            }
        }

        private void Report()
        {
            if (frameCount == 0) return;

            Array.Sort(frameTimes, 0, frameCount);
            float median = frameTimes[frameCount / 2];
            float worst = frameTimes[frameCount - 1];
            int hitches = 0;
            for (int i = 0; i < frameCount; i++)
            {
                if (frameTimes[i] > median * 2f) hitches++;
            }

            string interp = body != null ? body.interpolation.ToString() : "?";
            string line = $"[BallSmooth] fps={frameCount / WindowSeconds:F0} dt={median * 1000f:F1}/{worst * 1000f:F0}ms " +
                          $"hitches={hitches} sim[{Describe(simulating)} " +
                          $"steps={stepsSame.Median() * 100f:F0}%/{stepsChanged.Median() * 100f:F0}% " +
                          $"n={stepsSame.Count}/{stepsChanged.Count} interp={interp}] " +
                          $"prov[{Describe(provisional)}] draw[{Describe(drawing)}] switch[{Describe(switching)}]";

            // Only worth a line while the ball is in play somewhere.
            if (simulating.Count + provisional.Count + drawing.Count + switching.Count > 0)
            {
                Debug.Log(line);
            }

            frameCount = 0;
            simulating.Clear();
            provisional.Clear();
            drawing.Clear();
            switching.Clear();
            stepsSame.Clear();
            stepsChanged.Clear();
        }

        private static string Describe(Stats s)
        {
            if (s.Count == 0) return $"frames={s.Frames} still";

            float median = s.Median(); // sorts
            float p90 = s.Jerk[(int)((s.Count - 1) * 0.9f)];
            return $"frames={s.Frames} moving={s.Count} jerk={median * 100f:F0}%/{p90 * 100f:F0}% stalls={s.Stalls}";
        }
    }
}
