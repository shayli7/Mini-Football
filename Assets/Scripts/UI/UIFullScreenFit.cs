using UnityEngine;

namespace TableFootball.UI
{
    /// <summary>
    /// Sizes a child of a safe-area-inset screen to cover the WHOLE screen, edge to edge.
    ///
    /// Screens live inside the safe area so their controls clear notches and gesture bars, and until
    /// now backdrops reached the screen edge by over-extending a fixed 200 px past it. That is fine for
    /// a flat colour and wrong for the Arena, whose rim light is painted at the true screen edge: 200 px
    /// of over-extension puts the rim off screen. This measures the root canvas instead and fits to it
    /// exactly, whatever the inset is.
    ///
    /// Re-fits when the screen size or the parent's size changes (rotation, a resized window), and for
    /// the first frames after the screen opens, before the canvas has laid itself out.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class UIFullScreenFit : MonoBehaviour
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        private RectTransform rt;
        private Vector2Int lastScreen;
        private Vector2 lastParentSize;

        private void OnEnable()
        {
            lastScreen = Vector2Int.zero; // force a fit on the next LateUpdate
            Fit();
        }

        private void LateUpdate()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;

            var screen = new Vector2Int(Screen.width, Screen.height);
            Vector2 parentSize = parent.rect.size;
            if (screen == lastScreen && parentSize == lastParentSize) return;

            Fit();
        }

        private void Fit()
        {
            if (rt == null) rt = (RectTransform)transform;
            var parent = transform.parent as RectTransform;
            var canvas = GetComponentInParent<Canvas>();
            if (parent == null || canvas == null) return;

            var rootRect = (RectTransform)canvas.rootCanvas.transform;
            rootRect.GetWorldCorners(Corners);

            // The root canvas's bottom-left and top-right corners, in the parent's own space.
            Vector2 min = parent.InverseTransformPoint(Corners[0]);
            Vector2 max = parent.InverseTransformPoint(Corners[2]);
            Rect pr = parent.rect;

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = min - pr.min;
            rt.offsetMax = max - pr.max;

            lastScreen = new Vector2Int(Screen.width, Screen.height);
            lastParentSize = pr.size;
        }
    }
}
