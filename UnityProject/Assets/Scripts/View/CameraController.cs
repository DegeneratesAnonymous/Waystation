// CameraController — scroll-wheel zoom and right-click drag-to-pan for the
// orthographic station camera. Self-installs via RuntimeInitializeOnLoadMethod.
using UnityEngine;

namespace Waystation.View
{
    public class CameraController : MonoBehaviour
    {
        // ── Configuration ─────────────────────────────────────────────────────
        private const float ZoomSpeed    = 0.12f;  // fraction of current size per scroll unit
        private const float ZoomMin      = 2f;
        private const float ZoomMax      = 16f;

        // ── Auto-install ──────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<CameraController>() != null) return;
            new GameObject("CameraController").AddComponent<CameraController>();
        }

        // ── State ─────────────────────────────────────────────────────────────
        private Camera  _cam;
        private Vector3 _panOriginScreen;   // screen-space mouse pos when pan started
        private Vector3 _panOriginWorld;    // camera world pos when pan started

        private void Start() => _cam = Camera.main;

        private void Update()
        {
            if (_cam == null || !_cam.orthographic) return;

            HandleZoom();
            HandlePan();
        }

        // ── Zoom ──────────────────────────────────────────────────────────────
        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) < 0.001f) return;

            // Zoom toward the mouse cursor by shifting the camera position
            Vector3 mouseWorld = _cam.ScreenToWorldPoint(Input.mousePosition);

            float newSize = Mathf.Clamp(
                _cam.orthographicSize * (1f - scroll * ZoomSpeed * 10f),
                ZoomMin, ZoomMax);

            float ratio = newSize / _cam.orthographicSize;
            _cam.orthographicSize = newSize;

            // Shift camera so world point under cursor stays fixed
            Vector3 newMouseWorld = _cam.ScreenToWorldPoint(Input.mousePosition);
            _cam.transform.position += mouseWorld - newMouseWorld;
        }

        // ── Pan ───────────────────────────────────────────────────────────────
        private void HandlePan()
        {
            if (Input.GetMouseButtonDown(1))
            {
                _panOriginScreen = Input.mousePosition;
                _panOriginWorld  = _cam.transform.position;
            }

            if (!Input.GetMouseButton(1)) return;

            // Convert pixel delta to world units
            Vector3 delta      = Input.mousePosition - _panOriginScreen;
            float   worldScale = _cam.orthographicSize * 2f / Screen.height;
            Vector3 worldDelta = new Vector3(delta.x, delta.y, 0f) * worldScale;

            _cam.transform.position = _panOriginWorld - worldDelta;
        }
    }
}
