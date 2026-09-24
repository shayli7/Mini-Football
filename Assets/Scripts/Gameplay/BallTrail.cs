using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// The effects the top-tier ball skins carry: Ice Cube's frost trail, the Golden Trophy Ball's
    /// gold dust, and the Disco Ball's sparkle.
    ///
    /// Built in code like the rest of this project's presentation — no prefabs, no particle assets to
    /// keep in step with the catalogue. It follows <see cref="BallSkinner.OnSkinChanged"/> rather
    /// than reading the inventory itself, so it is right for the HOST'S ball in an online match
    /// without knowing that online exists.
    ///
    /// Two emitters, because "trail" and "dust" are different things:
    ///
    /// - a <see cref="TrailRenderer"/> for the continuous ribbon a sliding ball leaves, gated on
    ///   speed so a parked ball in the menu does not sit in a puddle of its own trail;
    /// - a <see cref="ParticleSystem"/> for motes, emitted PER METRE TRAVELLED for frost and dust
    ///   (a ball that is not moving sheds nothing) and per second for the disco sparkle, which is a
    ///   property of the ball rather than of its motion.
    ///
    /// Purely visual, and entirely optional: if no usable shader survives build stripping the
    /// effects are skipped and the ball is simply a ball.
    /// </summary>
    [RequireComponent(typeof(BallSkinner))]
    [DisallowMultipleComponent]
    public class BallTrail : MonoBehaviour
    {
        /// <summary>What a skin emits. Zero rate or zero trail time means "not this one".</summary>
        private readonly struct Effect
        {
            public readonly Color Color;
            public readonly float TrailSeconds;   // 0 = no ribbon
            public readonly float ParticlesPerMetre;
            public readonly float ParticlesPerSecond;
            public readonly float ParticleSize;

            public Effect(Color color, float trailSeconds, float perMetre, float perSecond, float size)
            {
                Color = color;
                TrailSeconds = trailSeconds;
                ParticlesPerMetre = perMetre;
                ParticlesPerSecond = perSecond;
                ParticleSize = size;
            }
        }

        private static Color Hex(string s)
        {
            ColorUtility.TryParseHtmlString(s, out Color c);
            return c;
        }

        private static bool EffectFor(string id, out Effect effect)
        {
            switch (id)
            {
                case "ball.ice":
                    // A frost ribbon plus a few flecks shaken loose as it travels.
                    effect = new Effect(Hex("#BBE6FF"), 0.34f, 90f, 0f, 0.0055f);
                    return true;
                case "ball.golden":
                    // Dust falls off the ball, so it is metered by distance and lingers briefly.
                    effect = new Effect(Hex("#FFD24A"), 0.26f, 70f, 0f, 0.0045f);
                    return true;
                case "ball.disco":
                    // No ribbon: a mirror ball glitters where it is, moving or not.
                    effect = new Effect(Hex("#E8F4FF"), 0f, 20f, 26f, 0.0040f);
                    return true;
                default:
                    effect = default;
                    return false;
            }
        }

        private BallSkinner skinner;
        private TrailRenderer trail;
        private ParticleSystem particles;
        private ParticleSystem.EmissionModule emission;

        /// <summary>The material both emitters share, kept so it can be replaced rather than leaked
        /// when the skin changes — browsing the store fires a change per tap.</summary>
        private Material trailMaterial;

        private float ballRadius = 0.018f;
        private bool built;
        private bool active;
        private float trailSpeedGate;

        /// <summary>Last frame's position, for measuring speed off the TRANSFORM.</summary>
        private Vector3 lastPosition;
        private bool hasLastPosition;

        private void Awake()
        {
            skinner = GetComponent<BallSkinner>();

            var sphere = GetComponent<SphereCollider>();
            if (sphere != null)
            {
                Vector3 s = transform.lossyScale;
                ballRadius = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }

            // A ball creeping at a few centimetres a second is settling, not travelling; a ribbon
            // there reads as a smear rather than as motion.
            trailSpeedGate = ballRadius * 12f;
        }

        private void OnEnable()
        {
            if (skinner == null) return;
            skinner.OnSkinChanged -= Configure;
            skinner.OnSkinChanged += Configure;
            Configure(skinner.CurrentId);
        }

        private void OnDisable()
        {
            if (skinner != null) skinner.OnSkinChanged -= Configure;
            Show(false);
        }

        private void LateUpdate()
        {
            if (!active || trail == null)
            {
                return;
            }

            // Speed is measured from the TRANSFORM, not the Rigidbody.
            //
            // On the guest in an online match the ball is kinematic and driven by NetworkTransform,
            // so its rigidbody velocity is permanently zero — reading that would have left the
            // guest's screen with no trail at all while the host's had one. The transform moves on
            // both machines, which is the only thing that is true of both.
            float dt = Time.deltaTime;
            Vector3 here = transform.position;
            float speed = hasLastPosition && dt > 0f
                ? (here - lastPosition).magnitude / dt
                : 0f;
            lastPosition = here;
            hasLastPosition = true;

            // Emitting is toggled rather than the component disabled: disabling a TrailRenderer
            // destroys the ribbon it has already laid, so a ball that slows and speeds up would
            // flicker instead of tapering.
            trail.emitting = speed > trailSpeedGate;
        }

        /// <summary>
        /// Rebuilds the emitters for a skin. Called on every real skin change, including the host's
        /// choice arriving in an online match.
        /// </summary>
        private void Configure(string id)
        {
            if (!EffectFor(id, out Effect effect))
            {
                Show(false);
                active = false;
                return;
            }

            if (!Build(effect))
            {
                active = false;
                return;
            }

            active = true;
            Show(true);
        }

        /// <summary>
        /// Creates or re-tunes the two emitters. Returns false when no usable material could be made,
        /// which is the one case where the effect is quietly skipped — a missing shader must cost the
        /// sparkle, not the match.
        /// </summary>
        private bool Build(Effect effect)
        {
            var mr = GetComponent<MeshRenderer>();
            Material mat = SkinMaterials.CreateTrailMaterial(
                effect.Color, mr != null ? mr.sharedMaterial : null);

            if (mat != null && trailMaterial != null && trailMaterial != mat)
            {
                // Replaced, not accumulated: every skin change builds a fresh material, and browsing
                // the store fires one per tap.
                Destroy(trailMaterial);
            }
            if (mat != null) trailMaterial = mat;

            if (mat == null)
            {
                if (!built)
                {
                    Debug.LogWarning("BallTrail: no usable shader for the trail material — " +
                                     "ball effects are disabled.");
                    built = true;
                }
                return false;
            }

            if (trail == null)
            {
                var go = new GameObject("BallTrail");
                go.transform.SetParent(transform, false);
                trail = go.AddComponent<TrailRenderer>();
                trail.alignment = LineAlignment.View;
                trail.numCapVertices = 4;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
            }

            trail.sharedMaterial = mat;
            trail.time = Mathf.Max(0.01f, effect.TrailSeconds);
            trail.enabled = effect.TrailSeconds > 0f;
            trail.startWidth = ballRadius * 1.6f;
            trail.endWidth = 0f;
            trail.minVertexDistance = ballRadius * 0.35f;

            // The gradient fades the ribbon out along its length. Unlit shaders honour it; the
            // fallback material does not, and there the width taper alone carries the effect.
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(effect.Color, 0f), new GradientColorKey(effect.Color, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = grad;

            if (particles == null)
            {
                var go = new GameObject("BallSparkle");
                go.transform.SetParent(transform, false);
                particles = go.AddComponent<ParticleSystem>();

                var r = particles.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                var shape = particles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;

                var colour = particles.colorOverLifetime;
                colour.enabled = true;
                var fade = new Gradient();
                fade.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                colour.color = new ParticleSystem.MinMaxGradient(fade);

                emission = particles.emission;
            }

            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;

            var main = particles.main;
            // World space, or the motes would ride along with the ball instead of being left behind.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.45f;
            main.startSpeed = 0.05f;
            main.startSize = effect.ParticleSize;
            main.startColor = effect.Color;
            main.maxParticles = 120;
            main.gravityModifier = 0f;

            var shapeMod = particles.shape;
            shapeMod.radius = ballRadius * 0.9f;

            emission = particles.emission;
            emission.rateOverDistance = effect.ParticlesPerMetre;
            emission.rateOverTime = effect.ParticlesPerSecond;

            built = true;
            return true;
        }

        private void Show(bool on)
        {
            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
                trail.gameObject.SetActive(on);
            }

            if (particles != null)
            {
                particles.gameObject.SetActive(on);
                if (on) particles.Play();
                else particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
