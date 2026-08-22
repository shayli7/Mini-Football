#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TableFootball.Tools
{
    /// <summary>
    /// Editor-only helper for shooting the four main-menu card images.
    ///
    /// The cards read as a set only if the camera never moves between shots, and a camera nudged by
    /// hand between takes will not hold still — that is exactly how the first set ended up with one
    /// 3/4 angle and one top-down crop. So the transform lives here as data and is re-applied every
    /// frame; framing is identical by construction rather than by care.
    ///
    /// What differs between the four shots is the table: where the ball sits, which rods are turned.
    /// The mode each card stands for is carried by its badge in the UI, not by the photograph.
    ///
    ///   F8   freeze / unfreeze (line up a moment mid-rally)
    ///   F9   capture the current slot, then advance to the next
    ///   F10  skip to the next slot without capturing
    ///   F7   toggle the camera lock, to explore a new framing
    ///
    /// Shots are written straight over the existing sprites in Assets/Images, so their import
    /// settings and GUIDs survive and anything already referencing them keeps working.
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuArtCapture : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("Camera used for the shots. Leave empty to use Camera.main. Keep it disabled — " +
                 "this script renders it directly, it should not be drawing every frame.")]
        [SerializeField] private Camera captureCamera;

        [Tooltip("Re-applies the framing below every frame, so the four shots cannot drift apart.")]
        [SerializeField] private bool lockCamera = true;

        [SerializeField] private Vector3 position = new Vector3(0.7f, 1.1f, -0.9f);
        [SerializeField] private Vector3 euler = new Vector3(35f, -30f, 0f);
        [SerializeField] private float fieldOfView = 35f;

        [Header("Output")]
        [Tooltip("Matches the card's picture area (398x302), so nothing is stretched or letterboxed.")]
        [SerializeField] private int width = 1024;
        [SerializeField] private int height = 768;
        [SerializeField] private string outputFolder = "Assets/Images";

        [Tooltip("File names, in capture order. These match the sprites already assigned on " +
                 "TableFootballUI, so re-shooting updates the menu in place.")]
        [SerializeField] private string[] slots = { "Online", "Local", "P vs P", "AI vs Player" };

        private int slot;

        private void OnEnable()
        {
            Debug.Log($"[MenuArtCapture] Ready. F9 captures \"{Current}\". F8 freeze, F10 skip, F7 unlock camera.");
        }

        private string Current => slots != null && slots.Length > 0 ? slots[slot % slots.Length] : "shot";

        private void LateUpdate()
        {
            var cam = captureCamera != null ? captureCamera : Camera.main;

            // Late, so anything else driving the camera this frame has already had its turn.
            //
            // Only ever locks a camera assigned here on purpose. Falling back to Camera.main is fine
            // for a one-off render, but holding it at the framing below would drag the gameplay
            // camera off the table for as long as this component is enabled.
            if (lockCamera && captureCamera != null)
            {
                cam = captureCamera;
                cam.transform.position = position;
                cam.transform.rotation = Quaternion.Euler(euler);
                cam.orthographic = false;
                cam.fieldOfView = fieldOfView;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.F8].wasPressedThisFrame)
            {
                Time.timeScale = Time.timeScale > 0f ? 0f : 1f;
                Debug.Log($"[MenuArtCapture] {(Time.timeScale > 0f ? "running" : "frozen")}");
            }

            if (kb[Key.F7].wasPressedThisFrame)
            {
                lockCamera = !lockCamera;
                string state = lockCamera
                    ? "ON"
                    : "OFF — move the camera, then copy its Transform values into Position/Euler to make that the new lock";
                Debug.Log("[MenuArtCapture] camera lock " + state);
            }

            if (kb[Key.F10].wasPressedThisFrame)
            {
                Advance();
            }

            if (kb[Key.F9].wasPressedThisFrame)
            {
                if (Capture(cam)) Advance();
            }
        }

        private void Advance()
        {
            if (slots == null || slots.Length == 0) return;
            slot = (slot + 1) % slots.Length;
            Debug.Log($"[MenuArtCapture] next up: \"{Current}\"");
        }

        private bool Capture(Camera cam)
        {
            if (cam == null)
            {
                Debug.LogWarning("[MenuArtCapture] No capture camera assigned and no Camera.main in scene.");
                return false;
            }

            Directory.CreateDirectory(outputFolder);

            var rt = new RenderTexture(width, height, 24);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D tex = null;

            try
            {
                cam.targetTexture = rt;
                RenderTexture.active = rt;
                cam.Render();

                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                var path = Path.Combine(outputFolder, Current + ".png").Replace('\\', '/');
                File.WriteAllBytes(path, tex.EncodeToPNG());

                AssetDatabase.Refresh();
                Debug.Log($"[MenuArtCapture] saved {path}");
                return true;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                if (tex != null) DestroyImmediate(tex);
                rt.Release();
                DestroyImmediate(rt);
            }
        }
    }
}
#endif
