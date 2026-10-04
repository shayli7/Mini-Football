using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// The Solar Crown's living effects — everything the Legendary ball does beyond its texture and
    /// the gold trail <see cref="BallTrail"/> already lays:
    ///
    /// - SPEED: the gold seams (the skin's emission map) brighten with speed, and the trail grows
    ///   longer and wider. The glow rises with movement, so the ball feels alive, not painted.
    /// - IMPACT: a sharp change of direction or pace — a rail, a figure, a block — throws a gold
    ///   shockwave ring out from the contact and a burst of sparks.
    /// - CROWN AURA: a soft gold halo round the ball, and the occasional tiny crown that drifts off
    ///   it and fades.
    /// - IDLE: at rest, a soft gold pulse every few seconds, with small motes floating round it.
    /// - GOAL: a large gold crown flashes over the ball, then a burst of sparks scatters and fades.
    ///
    /// Everything is measured from the TRANSFORM, never the Rigidbody, for the reason BallTrail
    /// documents: on the online guest the ball is kinematic and its rigidbody velocity is always
    /// zero, so physics-keyed effects would play for the host and never for the guest. Impacts are
    /// therefore detected as a jump in the transform's velocity between frames — which works on both
    /// machines — rather than from collision callbacks, which a kinematic ball does not get.
    ///
    /// Restraint is a requirement, not a nicety: the ball is the one thing on screen that must never
    /// be hard to see. So the halo sits BEHIND the ball's silhouette (the ball occludes its middle,
    /// leaving a rim), nothing here ever covers the ball for longer than a flash, and the brightest
    /// moments — impact and goal — are under a second.
    ///
    /// Added and enabled by <see cref="BallTrail"/> only while the Solar Crown is worn; disabling it
    /// hides every object and puts the seams back to their resting glow. Purely visual.
    /// </summary>
    [DisallowMultipleComponent]
    public class SolarCrownFx : MonoBehaviour
    {
        // ── Tuning. Sizes are in ball radii, so they hold if the ball is ever rescaled. ──────────
        private const float RestGlow = 0.7f;        // emission multiplier at rest
        private const float MaxGlow = 2.6f;         // ...at full speed
        private const float FullSpeed = 3.0f;       // m/s at which speed effects peak
        private const float IdleAfter = 0.6f;       // seconds still before the idle effects begin
        private const float PulseEvery = 3.0f;      // seconds between idle pulses
        private const float PulseLength = 0.9f;
        private const float ImpactDeltaV = 0.8f;    // m/s change in one frame that counts as a hit
        private const float ImpactMinSpeed = 0.3f;  // ignore nudges of a ball that was barely moving
        private const float ImpactCooldown = 0.12f;
        private const float TeleportStep = 0.25f;   // metres in one frame: a reset, not a hit
        private const float RingSeconds = 0.32f;
        private const float GoalSeconds = 1.1f;
        private const float CrownsPerSecond = 0.7f;
        private const float IdleMotesPerSecond = 5f;

        private static readonly Color Gold = new Color(1f, 0.78f, 0.25f, 1f);

        private MeshRenderer ballRenderer;
        private TrailRenderer trail;
        private float r = 0.018f;

        private GameObject root;                    // scene-root holder, so nothing inherits ball scale
        private Transform emitter;
        private Quad halo;
        private Quad goalCrown;
        private readonly Quad[] rings = new Quad[3];
        private ParticleSystem crowns, sparks, motes;

        private MatchManager match;

        private Vector3 lastPos, lastVel;
        private bool hasLast;
        private float smoothSpeed, stillFor, sinceImpact = 99f, goalAge = -1f;
        private Vector3 goalPos;

        private static Texture2D glowTex, ringTex, crownTex;

        /// <summary>A camera-facing quad and its own material, so each one fades independently.</summary>
        private sealed class Quad
        {
            public Transform T;
            public Material M;
            public float Age = -1f;
        }

        // ── lifecycle ──────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            ballRenderer = GetComponent<MeshRenderer>();
            var sphere = GetComponent<SphereCollider>();
            if (sphere != null)
            {
                Vector3 s = transform.lossyScale;
                r = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }
        }

        /// <summary>BallTrail hands over its ribbon, so the speed effect stretches the same trail
        /// rather than laying a second one on top of it.</summary>
        public void UseTrail(TrailRenderer t) => trail = t;

        private void OnEnable()
        {
            if (!Build())
            {
                enabled = false;
                return;
            }

            root.SetActive(true);
            hasLast = false;
            stillFor = 0f;
            goalAge = -1f;

            match = FindAnyObjectByType<MatchManager>();
            if (match != null)
            {
                match.GoalScored -= OnGoal;
                match.GoalScored += OnGoal;
            }
        }

        private void OnDisable()
        {
            if (match != null) match.GoalScored -= OnGoal;
            if (root != null) root.SetActive(false);
            SetGlow(1f);
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root);
            foreach (Quad q in AllQuads()) if (q?.M != null) Destroy(q.M);
        }

        // ── per frame ──────────────────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || root == null)
            {
                return; // a frozen world (menus, pause) holds every effect where it is
            }

            Vector3 pos = transform.position;
            Vector3 vel = Vector3.zero;
            bool teleported = false;

            if (hasLast)
            {
                Vector3 step = pos - lastPos;
                if (step.magnitude > TeleportStep)
                {
                    teleported = true;  // a kick-off reset: no speed, and certainly no impact
                }
                else
                {
                    vel = step / dt;
                }
            }

            float speed = vel.magnitude;
            smoothSpeed = Mathf.Lerp(smoothSpeed, speed, 1f - Mathf.Exp(-dt * 10f));
            float speed01 = Mathf.Clamp01(smoothSpeed / FullSpeed);

            // Impact: the velocity jumped this frame, on a ball that was genuinely moving.
            sinceImpact += dt;
            if (hasLast && !teleported && lastVel.magnitude > ImpactMinSpeed && sinceImpact > ImpactCooldown)
            {
                float dv = (vel - lastVel).magnitude;
                if (dv > ImpactDeltaV)
                {
                    Impact(pos, Mathf.Clamp01(dv / 4f));
                    sinceImpact = 0f;
                }
            }

            lastPos = pos;
            lastVel = teleported ? Vector3.zero : vel;
            hasLast = true;

            // Idle: still long enough, then a soft pulse every few seconds.
            stillFor = speed < 0.05f ? stillFor + dt : 0f;
            bool idle = stillFor > IdleAfter;
            float pulse = 0f;
            if (idle)
            {
                float phase = (stillFor - IdleAfter) % PulseEvery;
                if (phase < PulseLength) pulse = Mathf.Sin(Mathf.PI * phase / PulseLength);
            }

            // Speed: seams brighten, trail lengthens and widens.
            SetGlow(Mathf.Lerp(RestGlow, MaxGlow, speed01) + pulse * 1.2f);
            if (trail != null)
            {
                trail.time = Mathf.Lerp(0.16f, 0.5f, speed01);
                trail.startWidth = r * Mathf.Lerp(1.3f, 2.1f, speed01);
            }

            Camera cam = Camera.main;
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.LookRotation(Vector3.down);
            Vector3 towardCam = cam != null ? -cam.transform.forward : Vector3.up;

            // Aura: a halo in the ball's own centre plane, so the ball hides its middle and only a
            // rim shows — a glow ROUND the ball, never a film over it.
            Place(halo, pos, face, r * (3.0f + 0.6f * pulse + 0.4f * speed01),
                  0.22f + 0.18f * speed01 + 0.25f * pulse);

            emitter.position = pos;
            var crownEmission = crowns.emission;
            crownEmission.rateOverTime = CrownsPerSecond;
            var moteEmission = motes.emission;
            moteEmission.rateOverTime = idle ? IdleMotesPerSecond : 0f;

            // Shockwave rings, each in the plane of the hit it marks.
            foreach (Quad ring in rings)
            {
                if (ring.Age < 0f) continue;
                ring.Age += dt;
                float t = ring.Age / RingSeconds;
                if (t >= 1f)
                {
                    ring.Age = -1f;
                    ring.T.gameObject.SetActive(false);
                    continue;
                }
                float ease = 1f - (1f - t) * (1f - t);
                Place(ring, ring.T.position, face, r * Mathf.Lerp(1.5f, 7f, ease), (1f - t) * 0.8f);
            }

            // Goal: a large crown over the ball, swelling and fading.
            if (goalAge >= 0f)
            {
                goalAge += dt;
                float t = goalAge / GoalSeconds;
                if (t >= 1f)
                {
                    goalAge = -1f;
                    goalCrown.T.gameObject.SetActive(false);
                }
                else
                {
                    float ease = 1f - (1f - t) * (1f - t);
                    float alpha = Mathf.Min(1f, t / 0.1f) * (1f - t);
                    Place(goalCrown, goalPos + towardCam * (r * 4f), face, r * Mathf.Lerp(6f, 13f, ease), alpha);
                }
            }
        }

        // ── events ─────────────────────────────────────────────────────────────────────────────

        private void Impact(Vector3 at, float strength)
        {
            Quad ring = rings[0];
            foreach (Quad q in rings)
            {
                if (q.Age < 0f) { ring = q; break; }
                if (q.Age > ring.Age) ring = q;  // all busy: recycle the oldest
            }
            ring.Age = 0f;
            ring.T.position = at;
            ring.T.gameObject.SetActive(true);

            Burst(at, Mathf.RoundToInt(Mathf.Lerp(8f, 18f, strength)), Mathf.Lerp(0.35f, 0.9f, strength));
        }

        private void OnGoal(Team scorer)
        {
            if (!isActiveAndEnabled) return;
            goalPos = transform.position;
            goalAge = 0f;
            goalCrown.T.gameObject.SetActive(true);
            Burst(goalPos, 40, 1.2f);
        }

        private void Burst(Vector3 at, int count, float maxSpeed)
        {
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                // Spread in the plane the camera looks at, so a top-down view sees them scatter
                // rather than fly straight at it.
                Vector2 d = Random.insideUnitCircle.normalized;
                p.position = at;
                p.velocity = new Vector3(d.x, 0.15f, d.y) * Random.Range(maxSpeed * 0.35f, maxSpeed);
                sparks.Emit(p, 1);
            }
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────

        private void SetGlow(float multiplier)
        {
            if (ballRenderer == null) return;
            Material m = ballRenderer.sharedMaterial;
            if (m != null && m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", BallSkins.GlowColor * multiplier);
            }
        }

        private static void Place(Quad q, Vector3 at, Quaternion face, float size, float alpha)
        {
            q.T.position = at;
            q.T.rotation = face;
            q.T.localScale = new Vector3(size, size, 1f);
            SkinMaterials.SetTint(q.M, new Color(Gold.r, Gold.g, Gold.b, Mathf.Clamp01(alpha)));
        }

        private System.Collections.Generic.IEnumerable<Quad> AllQuads()
        {
            yield return halo;
            yield return goalCrown;
            foreach (Quad q in rings) yield return q;
        }

        // ── building ───────────────────────────────────────────────────────────────────────────

        /// <summary>Creates every object once. False when no effect material can be made at all —
        /// the case where a stripped build costs the sparkle, never the match.</summary>
        private bool Build()
        {
            if (root != null) return true;

            glowTex ??= MakeTexture(Radial);
            ringTex ??= MakeTexture(Ring);
            crownTex ??= MakeTexture(Crown);

            Material probe = Effect(Gold, glowTex);
            if (probe == null)
            {
                Debug.LogWarning("SolarCrownFx: no usable effect shader — Solar Crown effects are off.");
                return false;
            }
            Destroy(probe);

            root = new GameObject("SolarCrownFx");
            emitter = new GameObject("Emitter").transform;
            emitter.SetParent(root.transform, false);

            halo = MakeQuad("Halo", glowTex);
            goalCrown = MakeQuad("GoalCrown", crownTex);
            for (int i = 0; i < rings.Length; i++) rings[i] = MakeQuad("Shockwave" + i, ringTex);
            goalCrown.T.gameObject.SetActive(false);
            foreach (Quad ring in rings) ring.T.gameObject.SetActive(false);

            crowns = MakeParticles("Crowns", crownTex, 0.9f, r * 1.3f, 0.03f, r * 1.4f, 20);
            sparks = MakeParticles("Sparks", glowTex, 0.4f, r * 0.4f, 0f, r, 160);
            motes = MakeParticles("Motes", glowTex, 1.6f, r * 0.2f, 0.012f, r * 1.7f, 40);
            var sparkEmission = sparks.emission;
            sparkEmission.rateOverTime = 0f;      // bursts only
            return true;
        }

        private Material Effect(Color tint, Texture tex)
        {
            Material m = SkinMaterials.CreateTrailMaterial(tint, ballRenderer != null ? ballRenderer.sharedMaterial : null);
            if (m == null) return null;
            SkinMaterials.SetBaseMap(m, tex);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);   // seen from either side
            return m;
        }

        private Quad MakeQuad(string name, Texture tex)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());   // decoration only: never in the ball's physics
            go.transform.SetParent(root.transform, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var q = new Quad { T = go.transform, M = Effect(Gold, tex) };
            mr.sharedMaterial = q.M;
            return q;
        }

        private ParticleSystem MakeParticles(string name, Texture tex, float life, float size, float speed,
                                             float shapeRadius, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(emitter, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = life;
            main.startSize = size;
            main.startSpeed = speed;
            main.startColor = Gold;
            main.maxParticles = max;
            main.gravityModifier = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;   // in the plane the camera looks at
            shape.radius = shapeRadius;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(fade);

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sharedMaterial = Effect(Color.white, tex);   // colour comes from the particle

            ps.Play();
            return ps;
        }

        // ── procedural textures: no imported art ───────────────────────────────────────────────

        private delegate float Shape(float x, float y);

        private static float Radial(float x, float y)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            return d >= 1f ? 0f : (1f - d) * (1f - d);
        }

        private static float Ring(float x, float y)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            float k = (d - 0.78f) / 0.09f;
            return Mathf.Exp(-k * k);
        }

        /// <summary>The same three-point crown as the ball's emblem (Art/Blender/make_ball_textures.py).</summary>
        private static float Crown(float x, float y)
        {
            x *= 1.25f;
            y *= 1.25f;
            float[] xs = { -0.62f, -0.31f, 0f, 0.31f, 0.62f };
            float[] ys = { 0.42f, 0.02f, 0.62f, 0.02f, 0.42f };
            float top = ys[0];
            for (int i = 0; i < xs.Length - 1; i++)
            {
                if (x >= xs[i] && x <= xs[i + 1])
                {
                    top = Mathf.Lerp(ys[i], ys[i + 1], (x - xs[i]) / (xs[i + 1] - xs[i]));
                }
            }
            bool body = Mathf.Abs(x) <= 0.62f && y >= -0.22f && y <= top;
            bool band = Mathf.Abs(x) <= 0.66f && y >= -0.52f && y <= -0.30f;
            bool tip = false;
            foreach (var t in new[] { new Vector2(-0.62f, 0.42f), new Vector2(0f, 0.62f), new Vector2(0.62f, 0.42f) })
            {
                if ((new Vector2(x, y - 0.07f) - t).sqrMagnitude <= 0.01f) tip = true;
            }
            return body || band || tip ? 1f : 0f;
        }

        /// <summary>A 64x64 white-with-alpha texture of <paramref name="shape"/>, 4x supersampled so
        /// the crown's edges stay smooth at the small size it is drawn.</summary>
        private static Texture2D MakeTexture(Shape shape)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var px = new Color32[n * n];
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float a = 0f;
                    for (int s = 0; s < 4; s++)
                    {
                        float x = ((i + (s % 2) * 0.5f + 0.25f) / n) * 2f - 1f;
                        float y = ((j + (s / 2) * 0.5f + 0.25f) / n) * 2f - 1f;
                        a += shape(x, y);
                    }
                    px[j * n + i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a / 4f) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
