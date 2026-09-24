using UnityEngine;
using UnityEngine.UIElements;

namespace TableFootball.UI.Toolkit
{
    /// <summary>
    /// The one UI Toolkit panel the game draws its rebuilt screens on.
    ///
    /// Created in code at runtime, like the rest of the UI: a <see cref="PanelSettings"/> and a
    /// <see cref="UIDocument"/> on a GameObject of their own, so nothing has to be set up in the scene.
    /// The screens being moved over from the uGUI canvas one at a time each hang a full-screen element
    /// off <see cref="Root"/> and show or hide it; this class knows nothing about any of them.
    ///
    /// Two things every screen relies on:
    /// - <see cref="Root"/> is inset to <see cref="Screen.safeArea"/>, so screens clear notches and
    ///   gesture bars without knowing they exist. Backgrounds that must reach the screen edge go on
    ///   <see cref="FullRoot"/> instead, or bleed out with negative offsets.
    /// - The panel's root and the safe-area root ignore the pointer. <c>RodTouchInput</c> asks the
    ///   EventSystem whether a touch is over UI, and a UI Toolkit panel answers that too — a root that
    ///   picked would report every touch as "over UI" and the rods would never move. A screen blocks
    ///   touches only while it is displayed.
    /// </summary>
    public class UiToolkitHost : MonoBehaviour
    {
        /// <summary>
        /// The size every screen is laid out against, in panel units. Height is fixed (match = 1): a
        /// landscape phone always gets 760 units of height, and the width is whatever the aspect gives
        /// — about 1690 at 20:9, 1350 at 16:9 — so layouts flex horizontally and never vertically.
        /// </summary>
        public const int ReferenceWidth = 1280;
        public const int ReferenceHeight = 760;

        /// <summary>Shared with the uGUI canvas's sort order. See GameFlow.OpenSettings for why the
        /// relative order of the two systems never has to be relied on.</summary>
        private const int SortingOrder = 100;

        private static UiToolkitHost instance;

        private UIDocument document;
        private VisualElement safeRoot;
        private Rect lastSafeArea;
        private Vector2Int lastScreen;

        /// <summary>The safe-area-inset container screens are added to.</summary>
        public static VisualElement Root => Ensure().safeRoot;

        /// <summary>The panel's own root, edge to edge — for layers that must cover the whole screen.</summary>
        public static VisualElement FullRoot => Ensure().document.rootVisualElement;

        public static UiToolkitHost Ensure()
        {
            if (instance != null) return instance;

            // Built inactive and enabled once configured: UIDocument creates its visual tree in
            // OnEnable, and it has to have its PanelSettings by then.
            var go = new GameObject("UiToolkitHost");
            go.SetActive(false);
            instance = go.AddComponent<UiToolkitHost>();
            instance.CreateDocument(go);
            go.SetActive(true);
            instance.CreateRoot();
            return instance;
        }

        private void CreateDocument(GameObject go)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "RuntimePanelSettings";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/Theme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(ReferenceWidth, ReferenceHeight);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.sortingOrder = SortingOrder;
            settings.clearColor = false;

            if (settings.themeStyleSheet == null)
            {
                Debug.LogWarning("UiToolkitHost: Resources/UI/Theme.tss is missing — UI Toolkit controls " +
                                 "will draw without the default runtime theme.");
            }

            document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
        }

        private void CreateRoot()
        {
            var root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;

            var sheet = Resources.Load<StyleSheet>("UI/Styles/Base");
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogWarning("UiToolkitHost: Resources/UI/Styles/Base.uss is missing.");

            root.AddToClassList("app");
            if (ArcadeTheme.ReducedMotion) root.AddToClassList("reduced-motion");
            UiFonts.ApplyBody(root);

            safeRoot = new VisualElement { name = "SafeArea", pickingMode = PickingMode.Ignore };
            safeRoot.style.position = Position.Absolute;
            root.Add(safeRoot);

            ApplySafeArea();
        }

        private void Update()
        {
            if (Screen.safeArea != lastSafeArea || Screen.width != lastScreen.x || Screen.height != lastScreen.y)
            {
                ApplySafeArea();
            }
        }

        private void ApplySafeArea()
        {
            if (safeRoot == null || document == null || document.rootVisualElement.panel == null) return;

            lastSafeArea = Screen.safeArea;
            lastScreen = new Vector2Int(Screen.width, Screen.height);

            // Screen coordinates start at the bottom left; panel coordinates at the top left.
            var panel = document.rootVisualElement.panel;
            Rect safe = Screen.safeArea;
            Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(safe.xMin, Screen.height - safe.yMax));
            Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(safe.xMax, Screen.height - safe.yMin));
            Vector2 full = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height));

            safeRoot.style.left = topLeft.x;
            safeRoot.style.top = topLeft.y;
            safeRoot.style.right = Mathf.Max(0f, full.x - bottomRight.x);
            safeRoot.style.bottom = Mathf.Max(0f, full.y - bottomRight.y);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
