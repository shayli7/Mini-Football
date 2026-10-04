using UnityEngine;

namespace TableFootball
{
    /// <summary>
    /// Sizes the top-down gameplay camera so the table fills the screen, whatever the screen is.
    ///
    /// The camera's orthographic size is a height, and the table is wider than it is tall, so one
    /// hand-typed size can only ever be right for one aspect ratio: 0.5 frames a 16:9 window but
    /// leaves a long phone (20:9) with the table at barely 60% of the width. This measures the table
    /// instead, in the camera's own axes, and picks the smaller size that still shows all of it —
    /// fitting the width on a wide screen and the height on a narrow one.
    ///
    /// Re-fits whenever the screen size changes (a rotation, a window resize), since Unity raises no
    /// event for either. Touch input raycasts through this camera, so the rods follow the zoom with
    /// nothing else to change.
    ///
    /// Manual setup: add this component to the Main Camera. It overrides that camera's saved
    /// Orthographic Size, so there is no value there left to edit.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class GameplayCameraFit : MonoBehaviour
    {
        [Tooltip("Spare room left either side of the table, as a fraction of its width. Raise it if " +
                 "the rod handles or the pause button feel crowded against the screen edge.")]
        [SerializeField] private float sideMargin = 0.03f;

        [Tooltip("Spare room left above and below the table, as a fraction of its depth. The score " +
                 "strip sits over the top of the screen, so this is what keeps it off the goals.")]
        [SerializeField] private float verticalMargin = 0.16f;

        [Tooltip("Final multiplier on the fitted size. Below 1 zooms in and crops the edges, above 1 " +
                 "zooms out. Leave at 1 unless you want to nudge the framing by eye.")]
        [SerializeField] private float sizeScale = 1f;

        private Camera cam;
        private Bounds table;
        private bool measured;
        private Vector2Int fittedResolution;

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }

        // Start, not Awake: the table's renderers must all exist and sit where they will play.
        private void Start()
        {
            Measure();
            Fit();
        }

        private void Update()
        {
            if (Screen.width != fittedResolution.x || Screen.height != fittedResolution.y)
            {
                Fit();
            }
        }

        /// <summary>
        /// The playing surface's world bounds: the pitch and the rails and goal backs around it.
        ///
        /// Not every mesh under the table. The rods are longer than the table is wide — their handles
        /// stick out past both side rails — so measuring everything frames the handles, which is
        /// exactly as far out as the camera already was. The names are the ones
        /// <see cref="TableSurfaces"/> already keys the ball's materials on. The handles are still
        /// reachable when they fall off the screen edge: rods are grabbed by screen position.
        /// </summary>
        private void Measure()
        {
            var refs = FindAnyObjectByType<TableReferences>();
            if (refs == null) return;

            bool any = false;
            foreach (MeshRenderer r in refs.GetComponentsInChildren<MeshRenderer>())
            {
                if (r == null || !r.enabled) continue;
                if (r.name != "Field" && !r.name.StartsWith("Rail_") && !r.name.StartsWith("GoalBack")) continue;

                if (!any) { table = r.bounds; any = true; }
                else table.Encapsulate(r.bounds);
            }

            measured = any;
            if (!any)
            {
                Debug.LogWarning($"{name}: found no Field / Rail_* / GoalBack meshes under the table, so " +
                                 "the camera keeps its saved Orthographic Size.", this);
            }
        }

        private void Fit()
        {
            fittedResolution = new Vector2Int(Screen.width, Screen.height);
            if (cam == null || !cam.orthographic || !measured || Screen.height <= 0) return;

            // How far the table reaches from the camera's centre line along the camera's own right
            // and up, taken over all eight corners. Measured from the camera rather than from the
            // table's middle, so a rig that sits a little off-centre still gets the whole table in.
            Transform t = cam.transform;
            float halfRight = 0f, halfUp = 0f;
            Vector3 c = table.center, e = table.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x),
                                         c.y + ((i & 2) == 0 ? -e.y : e.y),
                                         c.z + ((i & 4) == 0 ? -e.z : e.z));
                Vector3 offset = corner - t.position;
                halfRight = Mathf.Max(halfRight, Mathf.Abs(Vector3.Dot(offset, t.right)));
                halfUp = Mathf.Max(halfUp, Mathf.Abs(Vector3.Dot(offset, t.up)));
            }

            float aspect = (float)Screen.width / Screen.height;
            float forHeight = halfUp * (1f + verticalMargin);
            float forWidth = halfRight * (1f + sideMargin) / aspect;

            cam.orthographicSize = Mathf.Max(forHeight, forWidth) * sizeScale;
        }
    }
}
