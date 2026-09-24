using TableFootball.Net;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Puts the equipped ball skin on the ball, and keeps it there.
    ///
    /// Deliberately input-agnostic, in the manner of <see cref="RodController"/>: it knows how to
    /// wear a skin and nothing about who chose it. Offline that is the local
    /// <see cref="Inventory"/>; online it is the host, pushed in through <see cref="SetOverride"/> by
    /// <see cref="TableFootball.Net.NetworkedBall"/>. New sources of a skin should be new callers of
    /// <see cref="SetOverride"/>, not new branches in here.
    ///
    /// Purely visual — it assigns a material and touches nothing else. The SphereCollider and the
    /// Rigidbody are untouched, so no skin can change how the ball plays.
    ///
    /// It writes <see cref="Renderer.sharedMaterial"/> rather than <c>material</c>. Reading
    /// <c>material</c> would clone the material on every assignment and leak one per skin change for
    /// the life of the session; the library above already hands back one shared instance per skin.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public class BallSkinner : MonoBehaviour
    {
        private MeshRenderer meshRenderer;

        /// <summary>
        /// The material the ball arrived with — the model's own. Captured before anything is applied,
        /// and it is what <c>ball.classic</c> resolves to: the default skin is not a texture, it is
        /// the absence of one.
        /// </summary>
        private Material original;

        /// <summary>The host's choice while an online match is running, or null when the local
        /// player's own inventory decides. Empty and null both mean "no override".</summary>
        private string overrideId;

        /// <summary>
        /// Raised with the id actually being worn, whenever it changes.
        ///
        /// This component is the single source of truth for what the ball looks like — it already
        /// resolves the local inventory against the host's override — so anything that has to follow
        /// the skin (the trail, see <see cref="BallTrail"/>) listens here rather than working the
        /// answer out a second time and risking a different one.
        /// </summary>
        public event System.Action<string> OnSkinChanged;

        /// <summary>The id last handed to <see cref="OnSkinChanged"/>, so the event fires on real
        /// changes rather than on every inventory notification.</summary>
        private string announced;

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                original = meshRenderer.sharedMaterial;
            }
        }

        private void OnEnable()
        {
            Inventory.OnChanged -= Apply;
            Inventory.OnChanged += Apply;

            // Re-applied on the way back in, not just subscribed. While this component is disabled it
            // is not listening, so any skin equipped in that window was missed — and re-enabling would
            // otherwise leave the ball wearing whatever it had before, with the inventory and the
            // table disagreeing and nothing to correct it. Harmless when nothing changed: Apply only
            // writes the renderer if the material actually differs.
            Apply();
        }

        private void OnDisable()
        {
            Inventory.OnChanged -= Apply;
        }

        /// <summary>
        /// Wears the skin somebody else chose, ignoring the local inventory until
        /// <see cref="ClearOverride"/>.
        ///
        /// This is how the online rule — the ball everyone sees is the HOST's — is expressed: the
        /// host publishes its own equipped id and every machine, its own included, wears that. Going
        /// through the same one call on both sides means the host cannot end up looking at a
        /// different ball from its guest.
        /// </summary>
        public void SetOverride(string id)
        {
            overrideId = id;
            Apply();
        }

        /// <summary>Hands the choice back to the local inventory — the end of an online match.</summary>
        public void ClearOverride()
        {
            overrideId = null;
            Apply();
        }

        /// <summary>The id currently being worn: the override if there is one, else what the local
        /// player has equipped.</summary>
        public string CurrentId =>
            string.IsNullOrEmpty(overrideId) ? Inventory.EquippedId(CosmeticKind.BallSkin) : overrideId;

        private void Apply()
        {
            if (meshRenderer == null)
            {
                return;
            }

            string id = CurrentId;
            Material resolved = BallSkins.Resolve(id, original);
            if (resolved != null && meshRenderer.sharedMaterial != resolved)
            {
                meshRenderer.sharedMaterial = resolved;
            }

            if (id != announced)
            {
                announced = id;
                OnSkinChanged?.Invoke(id);
            }
        }
    }
}
