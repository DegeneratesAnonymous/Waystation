// OverlayModeController — MonoBehaviour that listens for the Tab key and cycles
// the station view through utility overlay modes via UtilityNetworkManager.
//
// Tab cycle: Off → Electrical → Plumbing → Ducting → Off
//
// Auto-installs after scene load. When the overlay changes, this controller
// also syncs StationRoomView so the correct tile tinting is applied.
using UnityEngine;
using Waystation.Core;
using Waystation.Systems;
using Waystation.View;

namespace Waystation.UI
{
    public class OverlayModeController : MonoBehaviour
    {
        [Tooltip("Key that cycles through overlay modes. Default: Tab.")]
        [SerializeField] private KeyCode cycleKey = KeyCode.Tab;

        private UtilityNetworkManager _utilityManager;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<OverlayModeController>() != null) return;
            new GameObject("OverlayModeController").AddComponent<OverlayModeController>();
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                _utilityManager = GameManager.Instance.UtilityNetworks;
                if (_utilityManager != null)
                    _utilityManager.OnOverlayChanged += SyncViewMode;
            }
        }

        private void OnDestroy()
        {
            if (_utilityManager != null)
                _utilityManager.OnOverlayChanged -= SyncViewMode;
        }

        private void Update()
        {
            if (_utilityManager == null) return;
            if (Input.GetKeyDown(cycleKey))
                _utilityManager.CycleOverlay();
        }

        /// <summary>
        /// Returns the current overlay mode for external callers (e.g. render passes).
        /// </summary>
        public OverlayMode CurrentOverlay
            => _utilityManager?.CurrentOverlay ?? OverlayMode.Off;

        private void SyncViewMode(OverlayMode mode)
        {
            var srv = StationRoomView.Instance;
            if (srv == null) return;
            srv.SetViewMode(mode switch
            {
                OverlayMode.Electrical => StationRoomView.ViewMode.Electricity,
                OverlayMode.Plumbing   => StationRoomView.ViewMode.Pipes,
                OverlayMode.Ducting    => StationRoomView.ViewMode.Ducts,
                _                      => StationRoomView.ViewMode.Normal,
            });
        }
    }
}
