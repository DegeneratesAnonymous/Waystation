// DrawerPanel.cs
// Custom UI Toolkit VisualElement that renders a sliding panel with
// open/close animation driven by USS transitions.
//
// The panel defaults to closed (hidden, no input). Calling Open() applies
// the .ws-drawer-panel--open modifier class which triggers the USS
// max-height and opacity transitions defined in WaystationComponents.uss.
//
// Usage in UXML:
//   <Waystation.UI.DrawerPanel direction="Vertical">
//     <!-- drawer content here -->
//   </Waystation.UI.DrawerPanel>
//
// Usage in C#:
//   var drawer = new DrawerPanel();
//   drawer.OnOpenChanged += isOpen => Debug.Log("Drawer open: " + isOpen);
//   drawer.Open();
//   drawer.Close();

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// A sliding panel that animates open/closed via USS transitions.
    /// Input is disabled while the panel is closed.
    /// </summary>
    [UxmlElement]
    public partial class DrawerPanel : VisualElement
    {
        // ── Slide direction enum ──────────────────────────────────────────
        public enum Direction { Vertical, Horizontal }

        private const string ClassOpen       = "ws-drawer-panel--open";
        private const string ClassHorizontal = "ws-drawer-panel--horizontal";

        // ── Fields ────────────────────────────────────────────────────────
        private bool _isOpen;
        private Direction _direction;

        // ── Events ────────────────────────────────────────────────────────
        /// <summary>Fired when the open/closed state changes.</summary>
        public event Action<bool> OnOpenChanged;

        // ── Properties ────────────────────────────────────────────────────
        public bool IsOpen => _isOpen;

        public Direction SlideDirection
        {
            get => _direction;
            set
            {
                _direction = value;
                EnableInClassList(ClassHorizontal, value == Direction.Horizontal);
            }
        }

        // ── Constructor ───────────────────────────────────────────────────
        public DrawerPanel() : this(Direction.Vertical) { }

        public DrawerPanel(Direction direction = Direction.Vertical)
        {
            AddToClassList("ws-drawer-panel");
            SlideDirection = direction;
            // Start with input blocked
            pickingMode = PickingMode.Ignore;

            // Inline closed-state styles — mirrors USS .ws-drawer-panel so the
            // drawer is hidden without the stylesheet loaded via Resources.
            style.overflow = Overflow.Hidden;
            style.visibility = Visibility.Hidden;
            style.display = DisplayStyle.Flex;
            if (direction == Direction.Horizontal)
            {
                style.maxWidth = 0;
                style.opacity = 0;
            }
            else
            {
                style.maxHeight = 0;
                style.opacity = 0;
            }
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Opens the drawer, enabling input and triggering the open animation.
        /// </summary>
        public void Open()
        {
            if (_isOpen) return;
            _isOpen = true;
            pickingMode = PickingMode.Position;
            AddToClassList(ClassOpen);

            // Inline open-state styles for when USS is not loaded.
            style.visibility = Visibility.Visible;
            style.opacity = 1;
            if (_direction == Direction.Horizontal)
                style.maxWidth = 300;
            else
                style.maxHeight = 600;

            OnOpenChanged?.Invoke(true);
        }

        /// <summary>
        /// Closes the drawer, disabling input and triggering the close animation.
        /// </summary>
        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            RemoveFromClassList(ClassOpen);
            pickingMode = PickingMode.Ignore;

            // Inline closed-state styles for when USS is not loaded.
            style.visibility = Visibility.Hidden;
            style.opacity = 0;
            if (_direction == Direction.Horizontal)
                style.maxWidth = 0;
            else
                style.maxHeight = 0;

            OnOpenChanged?.Invoke(false);
        }

        /// <summary>
        /// Toggles between open and closed states.
        /// </summary>
        public void Toggle()
        {
            if (_isOpen) Close();
            else Open();
        }
    }
}
