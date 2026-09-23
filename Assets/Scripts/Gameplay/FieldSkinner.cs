using TableFootball.Net;
using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Puts the equipped field skin on the playing surface, and keeps it there.
    ///
    /// The same shape as <see cref="BallSkinner"/>: input-agnostic, knowing how to wear a skin and
    /// nothing about who chose it. Offline that is the local <see cref="Inventory"/>; online it is
    /// the host, pushed in through <see cref="SetOverride"/>. New sources should be new callers of
    /// <see cref="SetOverride"/>, not new branches here.
    ///
    /// Goes on the Field object. Purely visual — the table's collision is built separately by
    /// <see cref="TablePhysicsBuilder"/>, so a skin cannot change play.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    [DisallowMultipleComponent]
    public class FieldSkinner : MonoBehaviour
    {
        private MeshRenderer meshRenderer;

        /// <summary>The material the field arrived with. <c>field.classic</c> resolves to this: the
        /// default skin is not a texture, it is the absence of one.</summary>
        private Material original;

        private string overrideId;

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
            // Re-applied on the way back in, not just subscribed: while disabled this is not
            // listening, so any skin equipped in that window was missed.
            Apply();
        }

        private void OnDisable()
        {
            Inventory.OnChanged -= Apply;
        }

        /// <summary>Wears the skin somebody else chose — online, the host's — until
        /// <see cref="ClearOverride"/>.</summary>
        public void SetOverride(string id)
        {
            overrideId = id;
            Apply();
        }

        /// <summary>Hands the choice back to the local inventory. The end of an online match.</summary>
        public void ClearOverride()
        {
            overrideId = null;
            Apply();
        }

        public string CurrentId =>
            string.IsNullOrEmpty(overrideId) ? Inventory.EquippedId(CosmeticKind.FieldSkin) : overrideId;

        private void Apply()
        {
            if (meshRenderer == null)
            {
                return;
            }

            Material resolved = FieldSkins.Resolve(CurrentId, original);
            // sharedMaterial, not material: reading `material` clones per assignment and leaks one
            // per skin change for the life of the session. FieldSkins already caches one instance.
            if (resolved != null && meshRenderer.sharedMaterial != resolved)
            {
                meshRenderer.sharedMaterial = resolved;
            }
        }
    }
}
