using System;
using UnityEngine.UIElements;

namespace Waystation.Creator.UI
{
    public class UnsavedChangesModal
    {
        private readonly VisualElement _root;
        private VisualElement _modal;

        public UnsavedChangesModal(VisualElement root)
        {
            _root = root;
        }

        public void Show(Action onSave, Action onDiscard, Action onCancel)
        {
            _modal = new VisualElement();
            _modal.AddToClassList("modal-overlay");

            var dialog = new VisualElement();
            dialog.AddToClassList("modal-dialog");

            var title = new Label("UNSAVED CHANGES");
            title.AddToClassList("modal-title");

            var msg = new Label("You have unsaved changes. What would you like to do?");
            msg.AddToClassList("modal-message");

            var btnRow = new VisualElement();
            btnRow.AddToClassList("modal-btn-row");

            var saveBtn = new Button(() => { Dismiss(); onSave?.Invoke(); });
            saveBtn.text = "Save";
            saveBtn.AddToClassList("modal-btn");
            saveBtn.AddToClassList("modal-btn--primary");

            var discardBtn = new Button(() => { Dismiss(); onDiscard?.Invoke(); });
            discardBtn.text = "Discard";
            discardBtn.AddToClassList("modal-btn");
            discardBtn.AddToClassList("modal-btn--danger");

            var cancelBtn = new Button(() => { Dismiss(); onCancel?.Invoke(); });
            cancelBtn.text = "Cancel";
            cancelBtn.AddToClassList("modal-btn");

            btnRow.Add(cancelBtn);
            btnRow.Add(discardBtn);
            btnRow.Add(saveBtn);

            dialog.Add(title);
            dialog.Add(msg);
            dialog.Add(btnRow);
            _modal.Add(dialog);
            _root.Add(_modal);
        }

        public void Dismiss()
        {
            _modal?.RemoveFromHierarchy();
            _modal = null;
        }
    }
}
