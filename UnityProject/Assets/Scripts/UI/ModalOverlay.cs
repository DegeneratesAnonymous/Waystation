// ModalOverlay.cs
// Custom UI Toolkit VisualElement that renders a darkened full-screen
// backdrop with a centred content panel. Blocks input to underlying UI
// while visible.
//
// The overlay is hidden by default. Call Show() to display it and Hide()
// to dismiss it. Clicking the backdrop (outside the content panel) calls
// Hide() unless BackdropCloseEnabled is set to false.
//
// Usage in UXML:
//   <Waystation.UI.ModalOverlay>
//     <!-- content panel children added in C# or UXML -->
//   </Waystation.UI.ModalOverlay>
//
// Usage in C#:
//   var modal = new ModalOverlay();
//   modal.Title = "CONFIRM ACTION";
//   modal.BodyContent.Add(new Label("Are you sure?"));
//   modal.AddFooterButton("CONFIRM", () => { Confirm(); modal.Hide(); });
//   modal.AddFooterButton("CANCEL",  () => modal.Hide());
//   root.Add(modal);
//   modal.Show();

using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Waystation.UI
{
    /// <summary>
    /// Full-screen modal overlay with centred content panel.
    /// Blocks pointer input to underlying elements while visible.
    /// </summary>
    [UxmlElement]
    public partial class ModalOverlay : VisualElement
    {
        private const string ClassVisible = "ws-modal-overlay--visible";
        private static readonly HashSet<ModalOverlay> s_liveOverlays = new HashSet<ModalOverlay>();

        // ── Child elements ────────────────────────────────────────────────
        private readonly VisualElement _panel;
        private readonly Label _titleLabel;
        private readonly VisualElement _body;
        private readonly VisualElement _footer;
        private readonly Button _closeBtn;

        // ── Events ────────────────────────────────────────────────────────
        /// <summary>Fired when the overlay is shown.</summary>
        public event Action OnShown;
        /// <summary>Fired when the overlay is hidden.</summary>
        public event Action OnHidden;

        // ── Properties ────────────────────────────────────────────────────

        /// <summary>Title displayed in the modal header.</summary>
        public string Title
        {
            get => _titleLabel.text;
            set => _titleLabel.text = value;
        }

        /// <summary>Content area for the modal body.</summary>
        public VisualElement BodyContent => _body;

        /// <summary>
        /// Ensures that UXML-declared children are added to the modal body
        /// instead of to the overlay root element.
        /// </summary>
        public override VisualElement contentContainer => _body ?? base.contentContainer;

        /// <summary>
        /// When true (default), clicking outside the content panel closes the modal.
        /// </summary>
        public bool BackdropCloseEnabled { get; set; } = true;

        /// <summary>
        /// When false, hides the top-right close button.
        /// </summary>
        public bool CloseButtonVisible
        {
            get => _closeBtn.style.display != DisplayStyle.None;
            set => _closeBtn.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ── Constructor ───────────────────────────────────────────────────
        public ModalOverlay()
        {
            AddToClassList("ws-modal-overlay");

            // Hidden by default so the overlay is safe even if USS fails to load.
            style.display = DisplayStyle.None;
            style.visibility = Visibility.Hidden;
            style.opacity = 0f;
            pickingMode = PickingMode.Ignore;

            RegisterCallback<AttachToPanelEvent>(_ => s_liveOverlays.Add(this));
            RegisterCallback<DetachFromPanelEvent>(_ => s_liveOverlays.Remove(this));

            // Backdrop click-to-close
            RegisterCallback<ClickEvent>(OnBackdropClick);

            // Content panel — clicks inside stop propagation to prevent backdrop close
            _panel = new VisualElement();
            _panel.AddToClassList("ws-modal-overlay__panel");
            _panel.RegisterCallback<ClickEvent>(e => e.StopPropagation());

            // Header
            var header = new VisualElement();
            header.AddToClassList("ws-modal-overlay__header");

            _titleLabel = new Label();
            _titleLabel.AddToClassList("ws-modal-overlay__title");
            header.Add(_titleLabel);

            _closeBtn = new Button(() => Hide());
            _closeBtn.AddToClassList("ws-btn");
            _closeBtn.AddToClassList("ws-btn--danger");
            _closeBtn.AddToClassList("close-btn");
            _closeBtn.text = "✕";
            header.Add(_closeBtn);

            // Body
            _body = new VisualElement();
            _body.AddToClassList("ws-modal-overlay__body");

            // Footer
            _footer = new VisualElement();
            _footer.AddToClassList("ws-modal-overlay__footer");

            _panel.Add(header);
            _panel.Add(_body);
            _panel.Add(_footer);
            hierarchy.Add(_panel);
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Shows the modal overlay.</summary>
        public void Show()
        {
            AddToClassList(ClassVisible);
            style.display = DisplayStyle.Flex;
            style.visibility = Visibility.Visible;
            style.opacity = 1f;
            pickingMode = PickingMode.Position;
            OnShown?.Invoke();
        }

        /// <summary>Hides the modal overlay.</summary>
        public void Hide()
        {
            RemoveFromClassList(ClassVisible);
            style.display = DisplayStyle.None;
            style.visibility = Visibility.Hidden;
            style.opacity = 0f;
            pickingMode = PickingMode.Ignore;
            OnHidden?.Invoke();
        }

        /// <summary>Adds a labelled button to the modal footer.</summary>
        public Button AddFooterButton(string label, Action onClick, bool isPrimary = false)
        {
            var btn = new Button(onClick);
            btn.AddToClassList("ws-btn");
            if (isPrimary) btn.AddToClassList("ws-btn--accent");
            btn.text = label;
            _footer.Add(btn);
            return btn;
        }

        /// <summary>Removes all buttons from the footer.</summary>
        public void ClearFooter() => _footer.Clear();

        /// <summary>
        /// Defensive cleanup for stale dim backdrops. Hides every live modal overlay.
        /// </summary>
        public static void ForceHideAllVisible()
        {
            foreach (var overlay in s_liveOverlays)
            {
                if (overlay == null) continue;
                overlay.Hide();
            }
        }

        // ── Private ───────────────────────────────────────────────────────
        private void OnBackdropClick(ClickEvent evt)
        {
            if (BackdropCloseEnabled)
                Hide();
        }
    }
}
