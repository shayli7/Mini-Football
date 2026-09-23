using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// Shows the real foosball table behind the menus, at a cinematic angle, so the front end looks
    /// out over the game itself instead of a flat backdrop.
    ///
    /// It creates its own perspective <see cref="Camera"/> at runtime and renders it <b>over</b> the
    /// gameplay camera by giving it a higher <see cref="Camera.depth"/>. The gameplay camera is never
    /// touched — disabling it would null <see cref="Camera.main"/>, which the rod input falls back to
    /// for its raycasts (`RodTouchInput`/`RodPointerInput`). For the same reason this camera is
    /// deliberately <b>not</b> tagged MainCamera and carries <b>no</b> AudioListener: the one on the
    /// Main Camera must stay the only listener in the scene.
    ///
    /// <see cref="GameFlow"/> switches it on for the menu, online, friends and profile screens and off
    /// the instant a match goes live, so the top-down gameplay view is what shows during play. It is
    /// found with <c>FindAnyObjectByType</c> and treated as optional — leave it out of the scene and
    /// the menus fall back to their flat backdrops with nothing broken.
    ///
    /// Manual setup: add this component to one empty GameObject anywhere in the scene. The framing
    /// fields have working defaults; nudge them in the Inspector to taste.
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuStageCamera : MonoBehaviour
    {
        [Header("Framing (world space) — a low angle looking across the pitch")]
        [Tooltip("Where the stage camera sits. The table is around the world origin.")]
        [SerializeField] private Vector3 position = new Vector3(0f, 0.55f, -1.15f);

        [Tooltip("Where it looks. Euler angles; the default tilts down onto the table.")]
        [SerializeField] private Vector3 eulerAngles = new Vector3(22f, 0f, 0f);

        [Tooltip("Field of view. Narrower reads as a longer lens and flattens the perspective.")]
        [SerializeField] private float fieldOfView = 34f;

        [Tooltip("Solid colour cleared behind the table, in case the skybox is bare. Kept dark so the " +
                 "menu UI stays readable over it.")]
        [SerializeField] private Color background = new Color(0.03f, 0.05f, 0.08f, 1f);

        [Tooltip("Rendered above the gameplay camera (its depth is normally 0 / -1). Higher wins.")]
        [SerializeField] private float renderDepth = 5f;

        [Header("Idle motion")]
        [Tooltip("Peak yaw sway in degrees. A slow drift so the table is never dead still. 0 disables.")]
        [SerializeField] private float swayDegrees = 2.2f;

        [Tooltip("Seconds for one full sway cycle.")]
        [SerializeField] private float swayPeriod = 26f;

        private Camera cam;
        private Quaternion baseRotation;
        private float t;

        private void Awake()
        {
            baseRotation = Quaternion.Euler(eulerAngles);

            var go = new GameObject("MenuStageCam");
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            go.transform.rotation = baseRotation;

            cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.fieldOfView = fieldOfView;
            cam.depth = renderDepth;           // draws over the gameplay camera
            cam.nearClipPlane = 0.05f;
            cam.allowHDR = false;              // a menu backdrop does not need it; cheaper on mobile
            cam.allowMSAA = false;
            // No AudioListener added, and the object is left untagged — see the class summary.

            SetActive(false); // GameFlow turns it on for the menu; off is the safe default.
        }

        /// <summary>
        /// Switches this camera between its flat backdrop and drawing the scene's skybox.
        ///
        /// The stage camera clears to a solid colour by default, which is what makes the front end
        /// look the way it always has — and which would also paint straight over any background skin,
        /// since this camera draws on top of the gameplay one. So a background skin has to ask for
        /// the skybox explicitly; see <see cref="TableFootball.BackgroundSkinner"/>.
        ///
        /// <paramref name="tint"/> is used only when <paramref name="on"/> is false: it lets a skin
        /// that has art but no shader to draw it with still colour the backdrop, rather than silently
        /// doing nothing. Passing null restores the configured backdrop colour.
        /// </summary>
        public void UseSkybox(bool on, Color? tint = null)
        {
            if (cam == null)
            {
                return;
            }

            cam.clearFlags = on ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            if (!on)
            {
                cam.backgroundColor = tint ?? background;
            }
        }

        /// <summary>
        /// Shows or hides the stage. Only the <see cref="Camera"/> component is toggled, never the
        /// GameObject, so nothing about the gameplay camera or the single AudioListener is disturbed.
        /// </summary>
        public void SetActive(bool on)
        {
            if (cam != null) cam.enabled = on;
            enabled = on; // stops the idle sway costing anything while hidden
        }

        private void Update()
        {
            if (cam == null || swayDegrees <= 0f || swayPeriod <= 0f) return;

            // Unscaled: the menu sits at timeScale 0, where a scaled clock would freeze this.
            t += Time.unscaledDeltaTime;
            float yaw = Mathf.Sin(t / swayPeriod * Mathf.PI * 2f) * swayDegrees;
            cam.transform.rotation = baseRotation * Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
