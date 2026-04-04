using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.TileEditor
{
    public struct PixelChange
    {
        public int index;
        public Color32 before;
        public Color32 after;
    }

    public class UndoAction
    {
        public List<PixelChange> changes = new List<PixelChange>();
    }

    public class CanvasUndoManager
    {
        public const int MaxStackDepth = 200;

        private readonly Stack<UndoAction> _undoStack = new Stack<UndoAction>();
        private readonly Stack<UndoAction> _redoStack = new Stack<UndoAction>();
        private UndoAction _pendingAction;
        private Color32[] _snapshot;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void BeginAction(Color32[] currentPixels)
        {
            _pendingAction = new UndoAction();
            _snapshot = new Color32[currentPixels.Length];
            Array.Copy(currentPixels, _snapshot, currentPixels.Length);
        }

        public void CommitAction(Color32[] currentPixels)
        {
            if (_pendingAction == null || _snapshot == null) return;

            for (int i = 0; i < currentPixels.Length; i++)
            {
                if (!ColorsEqual(_snapshot[i], currentPixels[i]))
                {
                    _pendingAction.changes.Add(new PixelChange
                    {
                        index = i,
                        before = _snapshot[i],
                        after = currentPixels[i]
                    });
                }
            }

            if (_pendingAction.changes.Count > 0)
            {
                _undoStack.Push(_pendingAction);
                _redoStack.Clear();

                // Evict oldest if over limit
                if (_undoStack.Count > MaxStackDepth)
                {
                    var temp = new UndoAction[MaxStackDepth];
                    int i = 0;
                    foreach (var action in _undoStack)
                    {
                        if (i >= MaxStackDepth) break;
                        temp[i++] = action;
                    }
                    _undoStack.Clear();
                    for (int j = MaxStackDepth - 1; j >= 0; j--)
                        _undoStack.Push(temp[j]);
                }
            }

            _pendingAction = null;
            _snapshot = null;
        }

        public bool Undo(Color32[] pixels)
        {
            if (_undoStack.Count == 0) return false;
            var action = _undoStack.Pop();
            foreach (var change in action.changes)
                pixels[change.index] = change.before;
            _redoStack.Push(action);
            return true;
        }

        public bool Redo(Color32[] pixels)
        {
            if (_redoStack.Count == 0) return false;
            var action = _redoStack.Pop();
            foreach (var change in action.changes)
                pixels[change.index] = change.after;
            _undoStack.Push(action);
            return true;
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _pendingAction = null;
            _snapshot = null;
        }

        private static bool ColorsEqual(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }
    }
}
