// ResearchSubPanelController.cs
// Research tab panel (UI-018).
//
// Displays five branch sub-tabs (Industry, Exploration, Diplomacy, Security, Science),
// each containing:
//   • A scrollable, zoomable node graph:
//       – Nodes positioned at data-driven 2-D grid coordinates.
//       – Prerequisite connection lines routed as L-shaped paths drawn with
//         generateVisualContent.
//       – Four visually distinct node states: Locked, Available, In Progress,
//         Complete.
//       – Completed nodes display a DataChipIndicator (Filled when the chip
//         is active in server storage, Empty when the chip was removed).
//       – Active assignment indicators: NPC avatar (initials) icons shown at
//         the top of the graph when NPCs are currently doing research in the
//         branch.
//   • A node detail sub-panel (slides in when a node is selected) showing:
//       – Name, description, research cost, current branch progress,
//         prerequisites (with completion state), unlock rewards.
//
// Pan: left-drag on the graph background.
// Zoom: mouse wheel on the graph area.
//
// Data is pushed via Refresh(StationState, ResearchSystem).
// Call on mount and again on every OnTick while the panel is active (throttled
// to every 5 ticks in WaystationHUDController).
//
// Feature-flagged under FeatureFlags.UseUIToolkitHUD (panel mounts inside
// WaystationHUDController, which is itself gated by that flag).

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.UI
{
    /// <summary>
    /// Research tab panel.  Extends <see cref="VisualElement"/> so it can be
    /// added directly to the side-panel drawer.
    /// </summary>
    public class ResearchSubPanelController : VisualElement
    {
        // ── Layout constants ──────────────────────────────────────────────────

        /// <summary>Pixel width of a grid cell (horizontal step between columns).</summary>
        private const float NodeStepX = 160f;
        /// <summary>Pixel height of a grid cell (vertical step between rows).</summary>
        private const float NodeStepY = 110f;
        /// <summary>Width of a node card.</summary>
        private const float NodeW = 130f;
        /// <summary>Height of a node card.</summary>
        private const float NodeH = 64f;
        /// <summary>Padding around the node grid on the canvas.</summary>
        private const float CanvasPad = 60f;
        /// <summary>Width of the detail side-panel (px).</summary>
        private const float DetailPanelWidth = 240f;

        private const float ZoomMin = 0.4f;
        private const float ZoomMax = 2.0f;

        // ── USS class names ───────────────────────────────────────────────────

        private const string PanelClass         = "ws-research-panel";
        private const string BranchContentClass = "ws-research-panel__branch-content";
        private const string GraphAreaClass     = "ws-research-panel__graph-area";
        private const string NodeClass          = "ws-research-node";
        private const string NodeLockedClass    = "ws-research-node--locked";
        private const string NodeAvailableClass = "ws-research-node--available";
        private const string NodeInProgressClass = "ws-research-node--inprogress";
        private const string NodeCompleteClass  = "ws-research-node--complete";
        private const string NodeSelectedClass  = "ws-research-node--selected";
        private const string NodeNameClass      = "ws-research-node__name";
        private const string NodeCostClass      = "ws-research-node__cost";
        private const string NodeChipClass      = "ws-research-node__chip";
        private const string AssignmentRowClass = "ws-research-panel__assignments";
        private const string AvatarClass        = "ws-research-panel__avatar";
        private const string DetailPanelClass   = "ws-research-panel__detail";
        private const string DetailTitleClass   = "ws-research-panel__detail-title";
        private const string DetailBodyClass    = "ws-research-panel__detail-body";
        private const string DetailLabelClass   = "ws-research-panel__detail-label";
        private const string DetailValueClass   = "ws-research-panel__detail-value";
        private const string DetailSectionClass = "ws-research-panel__detail-section";
        private const string PrereqRowClass     = "ws-research-panel__prereq-row";
        private const string PrereqDoneClass    = "ws-research-panel__prereq--done";
        private const string CloseButtonClass   = "ws-research-panel__close-btn";

        // ── Node state enum ───────────────────────────────────────────────────

        /// <summary>Visual state of a research node.</summary>
        public enum NodeState { Locked, Available, InProgress, Complete }

        // ── Internal state ────────────────────────────────────────────────────

        private readonly TabStrip      _branchTabs;
        private readonly VisualElement _branchContent;

        // Per-branch cached data (built once per branch activation).
        private ResearchBranch _activeBranch = ResearchBranch.Industry;

        // Graph pan / zoom state per branch.
        private readonly Dictionary<ResearchBranch, Vector2> _panOffset =
            new Dictionary<ResearchBranch, Vector2>();
        private readonly Dictionary<ResearchBranch, float> _zoomScale =
            new Dictionary<ResearchBranch, float>();

        // Live game references — updated by Refresh().
        private StationState   _station;
        private ResearchSystem _research;

        // Currently selected node id (across all branches).
        private string _selectedNodeId;

        // ── Constructor ───────────────────────────────────────────────────────

        public ResearchSubPanelController()
        {
            AddToClassList(PanelClass);
            style.flexDirection = FlexDirection.Column;
            style.flexGrow      = 1;
            style.overflow      = Overflow.Hidden;

            // Initialise per-branch pan/zoom defaults.
            foreach (ResearchBranch branch in Enum.GetValues(typeof(ResearchBranch)))
            {
                _panOffset[branch]  = Vector2.zero;
                _zoomScale[branch]  = 1f;
            }

            // Branch sub-tab strip (horizontal).
            _branchTabs = new TabStrip(TabStrip.Orientation.Horizontal);
            _branchTabs.OnTabSelected += OnBranchTabSelected;
            _branchTabs.AddTab("INDUSTRY",    ResearchBranch.Industry.ToString());
            _branchTabs.AddTab("EXPLORATION", ResearchBranch.Exploration.ToString());
            _branchTabs.AddTab("DIPLOMACY",   ResearchBranch.Diplomacy.ToString());
            _branchTabs.AddTab("SECURITY",    ResearchBranch.Security.ToString());
            _branchTabs.AddTab("SCIENCE",     ResearchBranch.Science.ToString());
            Add(_branchTabs);

            // Content area for the currently-active branch graph.
            _branchContent = new VisualElement();
            _branchContent.AddToClassList(BranchContentClass);
            _branchContent.style.flexGrow = 1;
            _branchContent.style.overflow = Overflow.Hidden;
            Add(_branchContent);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Refresh the panel with current game state.</summary>
        public void Refresh(StationState station, ResearchSystem research)
        {
            _station  = station;
            _research = research;
            RebuildBranchView(_activeBranch);
        }

        // ── Branch tab selection ──────────────────────────────────────────────

        private void OnBranchTabSelected(string tabId)
        {
            if (Enum.TryParse<ResearchBranch>(tabId, out var branch))
            {
                _activeBranch    = branch;
                _selectedNodeId  = null;
                RebuildBranchView(branch);
            }
        }

        // ── Graph construction ────────────────────────────────────────────────

        private void RebuildBranchView(ResearchBranch branch)
        {
            _branchContent.Clear();

            if (_station == null || _research == null) return;

            var nodes = _research.GetBranchData(branch);
            var assignments = _research.GetActiveAssignments(branch, _station);

            // Build the outer row: [graphArea | detailPanel].
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexGrow      = 1;
            row.style.overflow      = Overflow.Hidden;
            _branchContent.Add(row);

            // ── Graph area ──────────────────────────────────────────────────

            var graphArea = new VisualElement();
            graphArea.AddToClassList(GraphAreaClass);
            graphArea.style.flexGrow     = 1;
            graphArea.style.overflow     = Overflow.Hidden;
            graphArea.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            row.Add(graphArea);

            // The panned/zoomed canvas lives inside graphArea.
            var canvas = new VisualElement();
            canvas.style.position        = Position.Absolute;
            canvas.style.transformOrigin = new TransformOrigin(0, 0, 0);
            graphArea.Add(canvas);

            // Apply stored pan/zoom.
            ApplyTransform(canvas, branch);

            // Build node position map first so we can route connections.
            var nodePosMap = BuildNodePositions(nodes);

            // Canvas size = content + padding.
            float canvasW = 0f, canvasH = 0f;
            foreach (var pos in nodePosMap.Values)
            {
                if (pos.x + NodeW + CanvasPad > canvasW) canvasW = pos.x + NodeW + CanvasPad;
                if (pos.y + NodeH + CanvasPad > canvasH) canvasH = pos.y + NodeH + CanvasPad;
            }
            canvas.style.width  = Mathf.Max(canvasW, 400f);
            canvas.style.height = Mathf.Max(canvasH, 400f);

            // Connection layer — draws L-shaped prerequisite lines.
            if (nodes.Length > 0)
            {
                var connLayer = BuildConnectionLayer(nodes, nodePosMap);
                canvas.Add(connLayer);
            }

            // Node cards.
            foreach (var node in nodes)
            {
                if (!nodePosMap.TryGetValue(node.id, out var pos)) continue;
                var nodeState = GetNodeState(node, _station, _research, branch);
                var card      = BuildNodeCard(node, nodeState, pos);
                canvas.Add(card);
            }

            // Assignment indicator strip (NPC avatars above graph).
            if (assignments.Length > 0)
            {
                var assignRow = BuildAssignmentRow(assignments);
                graphArea.Add(assignRow);
            }

            // ── Pan and zoom event handling ─────────────────────────────────

            Vector2 dragStart    = Vector2.zero;
            Vector2 panAtDragStart = Vector2.zero;
            bool    dragging     = false;

            graphArea.RegisterCallback<WheelEvent>(evt =>
            {
                float delta = -evt.delta.y * 0.05f;
                float newZoom = Mathf.Clamp(_zoomScale[branch] + delta, ZoomMin, ZoomMax);
                _zoomScale[branch] = newZoom;
                ApplyTransform(canvas, branch);
                evt.StopPropagation();
            });

            graphArea.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                dragging        = true;
                dragStart       = evt.position;
                panAtDragStart  = _panOffset[branch];
                graphArea.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            graphArea.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                var delta          = evt.position - dragStart;
                _panOffset[branch] = panAtDragStart + new Vector2(delta.x, delta.y);
                ApplyTransform(canvas, branch);
                evt.StopPropagation();
            });

            graphArea.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                graphArea.ReleasePointer(evt.pointerId);
            });

            // ── Detail panel ────────────────────────────────────────────────

            var detailPanel = BuildDetailPanel();
            row.Add(detailPanel);

            // Restore selection if a node was previously selected and still exists.
            if (_selectedNodeId != null)
            {
                var selNode = FindNode(nodes, _selectedNodeId);
                if (selNode != null)
                {
                    var selState = GetNodeState(selNode, _station, _research, branch);
                    PopulateDetailPanel(detailPanel, selNode, selState, nodes, _station, _research);
                    detailPanel.style.display = DisplayStyle.Flex;
                    // Re-highlight the selected card.
                    if (nodePosMap.TryGetValue(_selectedNodeId, out var selPos))
                    {
                        var selCard = canvas.Q<VisualElement>($"node_{_selectedNodeId}");
                        selCard?.AddToClassList(NodeSelectedClass);
                    }
                }
                else
                {
                    _selectedNodeId = null;
                    detailPanel.style.display = DisplayStyle.None;
                }
            }
            else
            {
                detailPanel.style.display = DisplayStyle.None;
            }

            // Wire node card click → detail panel.
            // We rebuild here after all cards are added so we can reference detailPanel.
            foreach (var node in nodes)
            {
                if (!nodePosMap.TryGetValue(node.id, out _)) continue;
                var nodeState = GetNodeState(node, _station, _research, branch);
                if (nodeState == NodeState.Locked) continue;   // locked nodes are not clickable

                var capturedNode  = node;
                var capturedState = nodeState;
                var card          = canvas.Q<VisualElement>($"node_{node.id}");
                if (card == null) continue;

                card.RegisterCallback<ClickEvent>(_ =>
                {
                    // Deselect previous.
                    if (_selectedNodeId != null)
                    {
                        var prev = canvas.Q<VisualElement>($"node_{_selectedNodeId}");
                        prev?.RemoveFromClassList(NodeSelectedClass);
                    }

                    if (_selectedNodeId == capturedNode.id)
                    {
                        // Toggle off.
                        _selectedNodeId = null;
                        detailPanel.style.display = DisplayStyle.None;
                    }
                    else
                    {
                        _selectedNodeId = capturedNode.id;
                        card.AddToClassList(NodeSelectedClass);
                        PopulateDetailPanel(detailPanel, capturedNode, capturedState,
                            nodes, _station, _research);
                        detailPanel.style.display = DisplayStyle.Flex;
                    }
                });
            }
        }

        // ── Node position map ─────────────────────────────────────────────────

        private Dictionary<string, Vector2> BuildNodePositions(ResearchNodeDefinition[] nodes)
        {
            var map = new Dictionary<string, Vector2>();
            // Track used grid cells so we can fall back gracefully for nodes with
            // duplicate or default (0,0) grid positions.
            var usedCells = new HashSet<(int, int)>();
            int fallbackCol = 0, fallbackRow = 0;

            foreach (var node in nodes)
            {
                int col = node.gridX;
                int row = node.gridY;

                // If this cell is already taken (two nodes share the same grid coord),
                // find the next free cell scanning rows then columns.
                if (usedCells.Contains((col, row)))
                {
                    while (usedCells.Contains((fallbackCol, fallbackRow)))
                    {
                        fallbackRow++;
                        if (fallbackRow > 20) { fallbackRow = 0; fallbackCol++; }
                    }
                    col = fallbackCol;
                    row = fallbackRow;
                }
                usedCells.Add((col, row));

                map[node.id] = new Vector2(
                    CanvasPad + col * NodeStepX,
                    CanvasPad + row * NodeStepY);
            }
            return map;
        }

        // ── Connection lines ──────────────────────────────────────────────────

        private VisualElement BuildConnectionLayer(
            ResearchNodeDefinition[] nodes,
            Dictionary<string, Vector2> nodePosMap)
        {
            // We need a snapshot of pos/prereq data for the generateVisualContent callback.
            var connections = new List<(Vector2 from, Vector2 to)>();

            foreach (var node in nodes)
            {
                if (!nodePosMap.TryGetValue(node.id, out var toPos)) continue;
                foreach (var prereqId in node.prerequisites)
                {
                    if (!nodePosMap.TryGetValue(prereqId, out var fromPos)) continue;
                    // Connect: bottom-center of prereq → top-center of dependent.
                    var fromPt = new Vector2(fromPos.x + NodeW * 0.5f, fromPos.y + NodeH);
                    var toPt   = new Vector2(toPos.x  + NodeW * 0.5f, toPos.y);
                    connections.Add((fromPt, toPt));
                }
            }

            var layer = new VisualElement();
            layer.style.position = Position.Absolute;
            layer.style.left     = 0; layer.style.top    = 0;
            layer.style.right    = 0; layer.style.bottom = 0;
            layer.pickingMode    = PickingMode.Ignore;

            // Snapshot for closure.
            var connSnapshot = new List<(Vector2 from, Vector2 to)>(connections);

            layer.generateVisualContent += ctx =>
            {
                var painter = ctx.painter2D;
                painter.lineWidth    = 1.5f;
                painter.strokeColor  = new Color(0.35f, 0.5f, 0.7f, 0.7f);
                painter.lineCap      = LineCap.Round;

                foreach (var (from, to) in connSnapshot)
                {
                    // L-shaped path: from→midpoint-y (vertical), then horizontal, then to (vertical).
                    float midY = (from.y + to.y) * 0.5f;

                    painter.BeginPath();
                    painter.MoveTo(from);
                    painter.LineTo(new Vector2(from.x, midY));
                    painter.LineTo(new Vector2(to.x, midY));
                    painter.LineTo(to);
                    painter.Stroke();
                }
            };

            return layer;
        }

        // ── Node card ─────────────────────────────────────────────────────────

        private VisualElement BuildNodeCard(
            ResearchNodeDefinition node,
            NodeState state,
            Vector2 pos)
        {
            var card = new VisualElement();
            card.name = $"node_{node.id}";
            card.AddToClassList(NodeClass);

            // Apply state-specific class.
            card.AddToClassList(StateClass(state));

            // Position absolutely within the canvas.
            card.style.position = Position.Absolute;
            card.style.left     = pos.x;
            card.style.top      = pos.y;
            card.style.width    = NodeW;
            card.style.height   = NodeH;
            card.style.overflow = Overflow.Hidden;

            // Inline styles to supplement USS (so the panel works even without a stylesheet).
            card.style.borderTopLeftRadius     = 4;
            card.style.borderTopRightRadius    = 4;
            card.style.borderBottomLeftRadius  = 4;
            card.style.borderBottomRightRadius = 4;
            card.style.paddingLeft   = 6;
            card.style.paddingRight  = 6;
            card.style.paddingTop    = 5;
            card.style.paddingBottom = 5;
            card.style.flexDirection = FlexDirection.Column;

            ApplyNodeStateStyle(card, state);

            // Name label.
            var nameLabel = new Label(node.displayName);
            nameLabel.AddToClassList(NodeNameClass);
            nameLabel.style.fontSize      = 10;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.whiteSpace    = WhiteSpace.Normal;
            card.Add(nameLabel);

            // Cost row.
            var costRow = new VisualElement();
            costRow.style.flexDirection = FlexDirection.Row;
            costRow.style.marginTop     = 2;
            card.Add(costRow);

            var costLabel = new Label($"{node.pointCost} pts");
            costLabel.AddToClassList(NodeCostClass);
            costLabel.style.fontSize = 9;
            costLabel.style.opacity  = 0.7f;
            costRow.Add(costLabel);

            // DataChipIndicator for complete nodes.
            if (state == NodeState.Complete)
            {
                var chipRow = new VisualElement();
                chipRow.AddToClassList(NodeChipClass);
                chipRow.style.flexDirection  = FlexDirection.Row;
                chipRow.style.marginTop      = 2;
                card.Add(chipRow);

                bool chipActive = _research != null &&
                    _station  != null &&
                    _research.IsNodeChipActive(node.id, _station);
                var chipIndicator = new DataChipIndicator(
                    chipActive ? DataChipIndicator.ChipState.Filled
                               : DataChipIndicator.ChipState.Empty);
                chipRow.Add(chipIndicator);
            }

            // Locked nodes are not interactive.
            if (state == NodeState.Locked)
                card.pickingMode = PickingMode.Ignore;

            return card;
        }

        // ── Node state helpers ────────────────────────────────────────────────

        /// <summary>
        /// Determines the visual state of a node given the current station research state.
        /// </summary>
        public static NodeState GetNodeState(
            ResearchNodeDefinition node,
            StationState station,
            ResearchSystem research,
            ResearchBranch branch)
        {
            if (station?.research == null) return NodeState.Locked;

            if (station.research.IsUnlocked(node.id))
                return NodeState.Complete;

            // Check prerequisites.
            bool prereqsMet = true;
            foreach (var p in node.prerequisites)
                if (!station.research.IsUnlocked(p)) { prereqsMet = false; break; }

            if (!prereqsMet) return NodeState.Locked;

            // Prereqs met — Available or In Progress depending on accumulated points.
            float progress = research?.GetProgressToNext(branch, station) ?? 0f;
            return progress > 0f ? NodeState.InProgress : NodeState.Available;
        }

        private static string StateClass(NodeState state) => state switch
        {
            NodeState.Locked     => NodeLockedClass,
            NodeState.Available  => NodeAvailableClass,
            NodeState.InProgress => NodeInProgressClass,
            NodeState.Complete   => NodeCompleteClass,
            _                   => NodeLockedClass,
        };

        private static void ApplyNodeStateStyle(VisualElement card, NodeState state)
        {
            switch (state)
            {
                case NodeState.Locked:
                    card.style.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 1f);
                    card.style.opacity         = 0.45f;
                    card.style.borderTopColor  = card.style.borderRightColor =
                    card.style.borderBottomColor = card.style.borderLeftColor =
                        new StyleColor(new Color(0.2f, 0.22f, 0.28f, 1f));
                    card.style.borderTopWidth = card.style.borderRightWidth =
                    card.style.borderBottomWidth = card.style.borderLeftWidth = 1f;
                    break;

                case NodeState.Available:
                    card.style.backgroundColor = new Color(0.12f, 0.14f, 0.20f, 1f);
                    card.style.opacity         = 1f;
                    card.style.borderTopColor  = card.style.borderRightColor =
                    card.style.borderBottomColor = card.style.borderLeftColor =
                        new StyleColor(new Color(0.3f, 0.6f, 0.9f, 1f));
                    card.style.borderTopWidth = card.style.borderRightWidth =
                    card.style.borderBottomWidth = card.style.borderLeftWidth = 1.5f;
                    break;

                case NodeState.InProgress:
                    card.style.backgroundColor = new Color(0.10f, 0.16f, 0.22f, 1f);
                    card.style.opacity         = 1f;
                    card.style.borderTopColor  = card.style.borderRightColor =
                    card.style.borderBottomColor = card.style.borderLeftColor =
                        new StyleColor(new Color(0.2f, 0.8f, 1.0f, 1f));
                    card.style.borderTopWidth = card.style.borderRightWidth =
                    card.style.borderBottomWidth = card.style.borderLeftWidth = 2f;
                    break;

                case NodeState.Complete:
                    card.style.backgroundColor = new Color(0.08f, 0.18f, 0.12f, 1f);
                    card.style.opacity         = 0.75f;
                    card.style.borderTopColor  = card.style.borderRightColor =
                    card.style.borderBottomColor = card.style.borderLeftColor =
                        new StyleColor(new Color(0.2f, 0.7f, 0.35f, 1f));
                    card.style.borderTopWidth = card.style.borderRightWidth =
                    card.style.borderBottomWidth = card.style.borderLeftWidth = 1f;
                    break;
            }
        }

        // ── Assignment row ────────────────────────────────────────────────────

        private VisualElement BuildAssignmentRow(NPCInstance[] assignments)
        {
            var row = new VisualElement();
            row.AddToClassList(AssignmentRowClass);
            row.style.flexDirection  = FlexDirection.Row;
            row.style.position       = Position.Absolute;
            row.style.top            = 4;
            row.style.right          = 4;
            row.style.paddingLeft    = 4;
            row.style.paddingRight   = 4;
            row.style.paddingTop     = 2;
            row.style.paddingBottom  = 2;
            row.style.backgroundColor = new Color(0.05f, 0.07f, 0.10f, 0.85f);
            row.style.borderTopLeftRadius     = 4;
            row.style.borderTopRightRadius    = 4;
            row.style.borderBottomLeftRadius  = 4;
            row.style.borderBottomRightRadius = 4;
            row.pickingMode = PickingMode.Ignore;

            foreach (var npc in assignments)
            {
                var avatar = new Label(GetInitials(npc.name));
                avatar.AddToClassList(AvatarClass);
                avatar.style.width  = 20;
                avatar.style.height = 20;
                avatar.style.borderTopLeftRadius     = 10;
                avatar.style.borderTopRightRadius    = 10;
                avatar.style.borderBottomLeftRadius  = 10;
                avatar.style.borderBottomRightRadius = 10;
                avatar.style.backgroundColor  = new Color(0.15f, 0.35f, 0.55f, 1f);
                avatar.style.unityTextAlign   = TextAnchor.MiddleCenter;
                avatar.style.fontSize         = 8;
                avatar.style.color            = new Color(0.9f, 0.9f, 0.95f, 1f);
                avatar.style.marginRight      = 2;
                row.Add(avatar);
            }
            return row;
        }

        // ── Detail panel ──────────────────────────────────────────────────────

        private VisualElement BuildDetailPanel()
        {
            var panel = new VisualElement();
            panel.AddToClassList(DetailPanelClass);
            panel.style.width           = DetailPanelWidth;
            panel.style.flexShrink      = 0;
            panel.style.flexDirection   = FlexDirection.Column;
            panel.style.overflow        = Overflow.Hidden;
            panel.style.backgroundColor = new Color(0.07f, 0.08f, 0.11f, 1f);
            panel.style.borderLeftWidth = 1;
            panel.style.borderLeftColor = new StyleColor(new Color(0.2f, 0.25f, 0.35f, 1f));

            // Close button.
            var closeBtn = new Button { text = "✕" };
            closeBtn.AddToClassList(CloseButtonClass);
            closeBtn.style.alignSelf  = Align.FlexEnd;
            closeBtn.style.marginTop  = 4;
            closeBtn.style.marginRight = 6;
            closeBtn.style.fontSize   = 11;
            closeBtn.style.paddingLeft  = 5;
            closeBtn.style.paddingRight = 5;
            closeBtn.style.backgroundColor = StyleKeyword.None;
            panel.Add(closeBtn);

            // Scroll view for body content.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            panel.Add(scroll);

            var body = new VisualElement();
            body.AddToClassList(DetailBodyClass);
            body.style.paddingLeft   = 8;
            body.style.paddingRight  = 8;
            body.style.paddingBottom = 8;
            body.style.flexDirection = FlexDirection.Column;
            scroll.Add(body);

            // The panel starts empty; PopulateDetailPanel fills it in.
            panel.userData = (closeBtn, body);
            return panel;
        }

        private void PopulateDetailPanel(
            VisualElement panel,
            ResearchNodeDefinition node,
            NodeState state,
            ResearchNodeDefinition[] allNodes,
            StationState station,
            ResearchSystem research)
        {
            if (panel.userData is not (Button closeBtn, VisualElement body)) return;

            // Wire close button.
            closeBtn.clicked -= OnDetailClose;
            closeBtn.clicked += OnDetailClose;

            body.Clear();

            // Title.
            var title = new Label(node.displayName);
            title.AddToClassList(DetailTitleClass);
            title.style.fontSize             = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom         = 6;
            title.style.whiteSpace           = WhiteSpace.Normal;
            body.Add(title);

            // State badge.
            var stateLbl = new Label(StateBadgeText(state));
            stateLbl.style.fontSize      = 9;
            stateLbl.style.marginBottom  = 6;
            stateLbl.style.color         = StateBadgeColor(state);
            body.Add(stateLbl);

            // Description.
            if (!string.IsNullOrEmpty(node.description))
            {
                var desc = new Label(node.description);
                desc.style.fontSize      = 10;
                desc.style.whiteSpace    = WhiteSpace.Normal;
                desc.style.marginBottom  = 8;
                desc.style.opacity       = 0.8f;
                body.Add(desc);
            }

            // Cost + progress.
            body.Add(DetailRow("COST", $"{node.pointCost} pts"));
            if (state == NodeState.InProgress && research != null && station != null)
            {
                float pct = research.GetProgressToNext(node.branch, station);
                body.Add(DetailRow("PROGRESS", $"{(int)(pct * 100)}%"));
            }

            // Prerequisites.
            if (node.prerequisites.Count > 0)
            {
                body.Add(SectionHeader("PREREQUISITES"));
                foreach (var prereqId in node.prerequisites)
                {
                    var prereqDef = FindNode(allNodes, prereqId);
                    bool done     = station?.research?.IsUnlocked(prereqId) ?? false;
                    string label  = prereqDef?.displayName ?? prereqId;
                    var prereqRow = new VisualElement();
                    prereqRow.AddToClassList(PrereqRowClass);
                    prereqRow.style.flexDirection = FlexDirection.Row;
                    prereqRow.style.marginBottom  = 2;

                    var tick = new Label(done ? "✓" : "○");
                    tick.style.fontSize   = 10;
                    tick.style.width      = 14;
                    tick.style.color      = done ? new Color(0.2f, 0.8f, 0.3f, 1f)
                                                 : new Color(0.6f, 0.6f, 0.7f, 1f);
                    tick.style.marginRight = 4;
                    prereqRow.Add(tick);

                    var prereqName = new Label(label);
                    prereqName.style.fontSize  = 10;
                    prereqName.style.whiteSpace = WhiteSpace.Normal;
                    prereqName.style.flexGrow   = 1;
                    if (done) prereqName.AddToClassList(PrereqDoneClass);
                    prereqRow.Add(prereqName);

                    body.Add(prereqRow);
                }
            }

            // Unlock rewards.
            if (node.unlockTags.Count > 0)
            {
                body.Add(SectionHeader("UNLOCKS"));
                foreach (var tag in node.unlockTags)
                {
                    var tagLabel = new Label($"• {tag}");
                    tagLabel.style.fontSize    = 9;
                    tagLabel.style.marginBottom = 1;
                    tagLabel.style.opacity     = 0.75f;
                    body.Add(tagLabel);
                }
            }

            // DataChip status for complete nodes.
            if (state == NodeState.Complete && research != null && station != null)
            {
                body.Add(SectionHeader("DATACHIP"));
                bool active = research.IsNodeChipActive(node.id, station);
                var chipRow = new VisualElement();
                chipRow.style.flexDirection = FlexDirection.Row;
                chipRow.style.marginTop     = 2;
                var chipInd = new DataChipIndicator(
                    active ? DataChipIndicator.ChipState.Filled
                           : DataChipIndicator.ChipState.Empty);
                chipRow.Add(chipInd);
                var chipLbl = new Label(active ? "Active" : "Removed");
                chipLbl.style.fontSize    = 9;
                chipLbl.style.marginLeft  = 4;
                chipLbl.style.opacity     = 0.8f;
                chipLbl.style.alignSelf   = Align.Center;
                chipRow.Add(chipLbl);
                body.Add(chipRow);
            }
        }

        private void OnDetailClose()
        {
            _selectedNodeId = null;
            // Rebuild the active branch view to clear selection highlight.
            if (_station != null && _research != null)
                RebuildBranchView(_activeBranch);
        }

        // ── Detail panel helpers ──────────────────────────────────────────────

        private VisualElement DetailRow(string labelText, string valueText)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.marginBottom   = 3;

            var lbl = new Label(labelText);
            lbl.AddToClassList(DetailLabelClass);
            lbl.style.fontSize  = 9;
            lbl.style.width     = 72;
            lbl.style.opacity   = 0.6f;
            row.Add(lbl);

            var val = new Label(valueText);
            val.AddToClassList(DetailValueClass);
            val.style.fontSize  = 10;
            val.style.flexGrow  = 1;
            row.Add(val);

            return row;
        }

        private VisualElement SectionHeader(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList(DetailSectionClass);
            lbl.style.fontSize    = 9;
            lbl.style.opacity     = 0.55f;
            lbl.style.marginTop   = 8;
            lbl.style.marginBottom = 3;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            return lbl;
        }

        private static string StateBadgeText(NodeState state) => state switch
        {
            NodeState.Locked     => "LOCKED",
            NodeState.Available  => "AVAILABLE",
            NodeState.InProgress => "IN PROGRESS",
            NodeState.Complete   => "COMPLETE",
            _                   => string.Empty,
        };

        private static Color StateBadgeColor(NodeState state) => state switch
        {
            NodeState.Locked     => new Color(0.5f, 0.5f, 0.6f, 1f),
            NodeState.Available  => new Color(0.3f, 0.6f, 0.9f, 1f),
            NodeState.InProgress => new Color(0.2f, 0.8f, 1.0f, 1f),
            NodeState.Complete   => new Color(0.2f, 0.75f, 0.35f, 1f),
            _                   => Color.white,
        };

        // ── Transform helpers ─────────────────────────────────────────────────

        private void ApplyTransform(VisualElement canvas, ResearchBranch branch)
        {
            float zoom    = _zoomScale[branch];
            var   pan     = _panOffset[branch];
            canvas.style.translate = new StyleTranslate(new Translate(pan.x, pan.y));
            canvas.style.scale     = new StyleScale(new Scale(new Vector3(zoom, zoom, 1f)));
        }

        // ── Utility ───────────────────────────────────────────────────────────

        private static ResearchNodeDefinition FindNode(ResearchNodeDefinition[] nodes, string id)
        {
            foreach (var n in nodes)
                if (n.id == id) return n;
            return null;
        }

        private static string GetInitials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            int space = name.LastIndexOf(' ');
            return space > 0 && space < name.Length - 1
                ? $"{name[0]}{name[space + 1]}"
                : name.Length > 0 ? name[..1] : "?";
        }
    }
}
