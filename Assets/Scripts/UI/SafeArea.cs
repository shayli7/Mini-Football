using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// Insets a full-screen RectTransform to <see cref="Screen.safeArea"/>, keeping the UI clear of
    /// notches, punch-holes and gesture bars.
    ///
    /// The player settings deliberately render outside the safe area (androidRenderOutsideSafeArea),
    /// so the game fills the whole panel — which is right for the table, and wrong for a Quit button
    /// that would otherwise sit under a camera cutout. Applying the inset to the UI root alone gets
    /// both: edge-to-edge world, safely-placed controls.
    ///
    /// One instance sits between the Canvas and every screen, so screens inherit the inset without
    /// knowing it exists.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SafeArea : MonoBehaviour
    {
        private RectTransform rt;
        private Rect applied = Rect.zero;
        private ScreenOrientation appliedOrientation;
        private Vector2Int appliedResolution;

        private void Awake()
        {
            rt = GetComponent<RectTransform>();
            Apply();
        }

        // Cheap enough to poll: three comparisons against cached values, and it is the only way to
        // catch a rotation or a resize. Unity raises no event for either.
        private void Update()
        {
            if (Screen.safeArea != applied ||
                Screen.orientation != appliedOrientation ||
                Screen.width != appliedResolution.x ||
                Screen.height != appliedResolution.y)
            {
                Apply();
            }
        }

        private void Apply()
        {
            if (rt == null) return;

            Rect safe = Screen.safeArea;
            applied = safe;
            appliedOrientation = Screen.orientation;
            appliedResolution = new Vector2Int(Screen.width, Screen.height);

            // A zero-sized screen happens for a frame during some startup and rotation paths;
            // dividing by it would push the anchors to NaN and blank the entire UI.
            if (Screen.width <= 0 || Screen.height <= 0) return;

            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            if (min.x < 0f || min.y < 0f || max.x > 1f || max.y > 1f) return;

            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
