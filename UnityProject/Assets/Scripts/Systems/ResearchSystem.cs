// ResearchSystem — accumulates research points from crew working at research
// terminals and automatically unlocks nodes when prerequisites are met.
// On each unlock a physical Datachip (item.datachip) is produced and stored in
// the first available complete Data Storage Server foundation.  If no storage
// space is available the chip is tallied as "pending" and stored as soon as
// capacity appears.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class ResearchSystem
    {
        private readonly ContentRegistry _registry;
        private SkillSystem              _skillSystem;
        private float                    _secondsPerTick = 0.5f;
        private float                    _pointsPerNpcPerTickBase = 0.04f;

        private const string DatachipItemId      = "item.datachip";
        private const string DataStorageBuildable = "buildable.data_storage_server";
        private const string RelayNodeBuildable   = "buildable.relay_node";

        // Terminal buildable-id → branch mapping
        private static readonly Dictionary<string, ResearchBranch> TerminalBranch =
            new Dictionary<string, ResearchBranch>
            {
                { "buildable.industry_terminal",    ResearchBranch.Industry    },
                { "buildable.exploration_terminal", ResearchBranch.Exploration },
                { "buildable.diplomacy_terminal",   ResearchBranch.Diplomacy   },
                { "buildable.security_terminal",    ResearchBranch.Security    },
                { "buildable.science_terminal",     ResearchBranch.Science     },
            };

        // Research job-id → branch + skill mapping
        private static readonly Dictionary<string, ResearchBranch> JobBranch =
            new Dictionary<string, ResearchBranch>
            {
                { "job.research_industry",    ResearchBranch.Industry    },
                { "job.research_exploration", ResearchBranch.Exploration },
                { "job.research_diplomacy",   ResearchBranch.Diplomacy   },
                { "job.research_security",    ResearchBranch.Security    },
                { "job.research_science",     ResearchBranch.Science     },
            };

        // Research job-id → skill.id for XP awards
        private static readonly Dictionary<string, string> JobSkill =
            new Dictionary<string, string>
            {
                { "job.research_military",  "skill.combat"     },
                { "job.research_economic",  "skill.economics"  },
                { "job.research_science",   "skill.science"    },
            };

        public ResearchSystem(ContentRegistry registry) => _registry = registry;

        /// <summary>Wire up SkillSystem after construction (called from GameManager).</summary>
        public void SetSkillSystem(SkillSystem skillSystem) => _skillSystem = skillSystem;

        /// <summary>Set the real-time seconds per game tick so XP rates stay consistent when game speed changes.</summary>
        public void SetSecondsPerTick(float secondsPerTick) => _secondsPerTick = secondsPerTick;
        /// <summary>Set base research points generated per actively assigned NPC per tick.</summary>
        public void SetPointsPerNpcPerTick(float pointsPerNpcPerTick) => _pointsPerNpcPerTickBase = Mathf.Max(0f, pointsPerNpcPerTick);

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (station == null) return;
            if (station.research == null) station.research = ResearchState.Create();

            // Accumulate points: one entry per NPC doing a research job.
            // If a complete research terminal of the matching branch is present in
            // a fully-qualified research_lab room, its room bonus multiplier is applied.
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.currentJobId == null) continue;
                if (!JobBranch.TryGetValue(npc.currentJobId, out var branch)) continue;
                if (!TryGetTerminalMultiplier(branch, station, out float terminalMult)) continue;

                int researchSkill = npc.skills.ContainsKey("science") ? npc.skills["science"]
                                  : npc.skills.ContainsKey("research") ? npc.skills["research"] : 5;
                float pts = _pointsPerNpcPerTickBase * (0.5f + researchSkill / 10f) * npc.workModifier;

                // Apply room bonus from any complete terminal of matching branch.
                pts *= terminalMult;

                // Apply expertise ResearchOutput bonus (e.g. Research Prodigy +20%)
                if (_skillSystem != null && JobSkill.TryGetValue(npc.currentJobId, out var skillId))
                    pts *= _skillSystem.GetResearchOutputMultiplier(npc, skillId);

                station.research.branches[branch].points += pts;

                // Award skill XP for time at terminal (1 tick ≈ 1 second)
                if (_skillSystem != null && JobSkill.TryGetValue(npc.currentJobId, out var xpSkillId))
                {
                    if (_registry.Skills.TryGetValue(xpSkillId, out var skillDef))
                        _skillSystem.AwardXPOverTime(npc, xpSkillId,
                            skillDef.xpPerActiveSecond, _secondsPerTick, station);
                }
            }

            // Auto-unlock nodes whose prerequisites are satisfied and cost is met.
            foreach (var kv in _registry.ResearchNodes)
            {
                var node    = kv.Value;
                var bState  = station.research.branches[node.branch];
                if (bState.unlockedNodeIds.Contains(node.id)) continue;

                // Prerequisites: all must already be unlocked.
                bool prereqsMet = true;
                foreach (var p in node.prerequisites)
                {
                    if (!station.research.IsUnlocked(p)) { prereqsMet = false; break; }
                }
                if (!prereqsMet) continue;
                if (bState.points < node.pointCost) continue;

                // Unlock.
                bState.unlockedNodeIds.Add(node.id);
                if (!bState.unlockedNodeOrder.Contains(node.id))
                    bState.unlockedNodeOrder.Add(node.id);
                bState.points -= node.pointCost;   // consume the spent points

                // Produce a datachip for the completed research.
                StoreDatachip(station);

                station.LogEvent($"Research unlocked: {node.displayName}.");
                Debug.Log($"[ResearchSystem] Unlocked node '{node.id}'.");
            }

            // Attempt to place any pending chips whenever new storage appears.
            if (station.research.pendingDatachips > 0)
                FlushPendingDatachips(station);

            // Re-evaluate active research unlock tags based on physically stored chips.
            RefreshActiveUnlockTags(station);
        }

        // ── Datachip storage helpers ──────────────────────────────────────────

        /// <summary>
        /// Returns the best effective terminal multiplier across all complete terminals
        /// of the given branch.  For each terminal the effective value is:
        ///   (hasRoomBonus ? roomBonusMultiplier : 1.0) × Functionality()
        /// Non-functional terminals (Functionality == 0, i.e. destroyed) are skipped.
        /// The maximum across all qualifying terminals is returned so that a damaged
        /// terminal never reduces output when a healthier one is present.
        /// Returns true and outputs the multiplier when a qualifying terminal exists;
        /// otherwise returns false and research generation is gated off for that branch.
        /// </summary>
        private static bool TryGetTerminalMultiplier(ResearchBranch branch, StationState station, out float multiplier)
        {
            multiplier = 1.0f;
            float best = 1.0f;
            bool  found = false;
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete") continue;
                if (!TerminalBranch.TryGetValue(f.buildableId, out var fb) || fb != branch) continue;
                float func = f.Functionality();
                if (func <= 0f) continue;  // destroyed terminal — no contribution
                float effective = (f.hasRoomBonus ? f.roomBonusMultiplier : 1.0f) * func;
                if (!found || effective > best)
                {
                    best  = effective;
                    found = true;
                }
            }
            if (!found) return false;
            multiplier = best;
            return true;
        }

        /// <summary>
        /// Try to add one datachip to a complete Data Storage Server foundation.
        /// If no Data Storage Server capacity exists, the chip is recorded as pending.
        /// </summary>
        private void StoreDatachip(StationState station)
        {
            if (TryAddDatachipToStorage(station)) return;
            // No room anywhere — track as pending.
            station.research.pendingDatachips++;
            station.LogEvent("Warning: No Data Storage Server capacity. Research Datachip is pending storage.");
        }

        /// <summary>Flush pending chips into newly-available storage each tick.</summary>
        private void FlushPendingDatachips(StationState station)
        {
            while (station.research.pendingDatachips > 0)
            {
                if (!TryAddDatachipToStorage(station)) break;
                station.research.pendingDatachips--;
                station.LogEvent("Pending Research Datachip stored successfully.");
            }
        }

        /// <summary>
        /// Try to store one datachip in a complete Data Storage Server foundation.
        /// Returns true if the chip was stored.
        /// </summary>
        private bool TryAddDatachipToStorage(StationState station)
        {
            // Pass 1 — prefer dedicated Data Storage Servers.
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != DataStorageBuildable) continue;
                if (f.status != "complete") continue;
                if (f.cargoCapacity <= 0) continue;
                if (f.CargoItemCount() < f.cargoCapacity)
                {
                    IncrementCargo(f.cargo, DatachipItemId);
                    return true;
                }
            }

            return false;
        }

        private static void IncrementCargo(Dictionary<string, int> cargo, string itemId)
        {
            cargo[itemId] = (cargo.TryGetValue(itemId, out var existing) ? existing : 0) + 1;
        }

        private List<ResearchNodeDefinition> GetChipBackedUnlockedNodes(StationState station)
        {
            var ordered = new List<ResearchNodeDefinition>();
            foreach (ResearchBranch branch in Enum.GetValues(typeof(ResearchBranch)))
            {
                if (!station.research.branches.TryGetValue(branch, out var bState)) continue;
                var processedNodeIds = new HashSet<string>();
                var inOrder = new HashSet<string>(bState.unlockedNodeOrder);

                // Preserve historical unlock order when available.
                foreach (var nodeId in bState.unlockedNodeOrder)
                {
                    if (!bState.unlockedNodeIds.Contains(nodeId)) continue;
                    if (!processedNodeIds.Add(nodeId)) continue;
                    if (!_registry.ResearchNodes.TryGetValue(nodeId, out var n)) continue;
                    if (n.branch != branch) continue;
                    ordered.Add(n);
                }

                // Backfill old saves where order was not tracked, deterministically.
                var missingNodeIds = new List<string>();
                foreach (var nodeId in bState.unlockedNodeIds)
                {
                    if (inOrder.Contains(nodeId)) continue;
                    missingNodeIds.Add(nodeId);
                }
                missingNodeIds.Sort(StringComparer.Ordinal);
                foreach (var nodeId in missingNodeIds)
                {
                    if (!processedNodeIds.Add(nodeId)) continue;
                    bState.unlockedNodeOrder.Add(nodeId);
                    inOrder.Add(nodeId);
                    if (!_registry.ResearchNodes.TryGetValue(nodeId, out var n)) continue;
                    if (n.branch != branch) continue;
                    ordered.Add(n);
                }
            }

            int activeChipCount = Mathf.Min(GetStoredDatachipCount(station), ordered.Count);
            return ordered.GetRange(0, activeChipCount);
        }

        private void RefreshActiveUnlockTags(StationState station)
        {
            if (station?.research == null) return;

            var desired = new HashSet<string>();
            var allResearchTags = new HashSet<string>();
            foreach (var n in _registry.ResearchNodes.Values)
                foreach (var t in n.unlockTags)
                    allResearchTags.Add(t);

            foreach (var node in GetChipBackedUnlockedNodes(station))
                foreach (var tag in node.unlockTags)
                    desired.Add(tag);

            var clearCandidates = new HashSet<string>(allResearchTags);
            foreach (var t in station.research.appliedUnlockTags)
                clearCandidates.Add(t);

            foreach (var tag in clearCandidates)
                if (!desired.Contains(tag))
                    station.ClearTag(tag);

            foreach (var tag in desired)
            {
                station.SetTag(tag);
                station.research.appliedUnlockTags.Add(tag);
            }

            station.research.appliedUnlockTags.RemoveWhere(t => !desired.Contains(t));
        }

        /// <summary>Set per-branch relay filter for a relay node foundation.
        /// Empty filter means share all branches.</summary>
        public void SetRelayBranchFilter(FoundationInstance relayNode, IEnumerable<ResearchBranch> branches)
        {
            if (relayNode == null || relayNode.buildableId != RelayNodeBuildable) return;
            relayNode.relayBranchFilter.Clear();
            if (branches == null) return;
            foreach (var b in branches)
                relayNode.relayBranchFilter.Add(b.ToString());
        }

        /// <summary>
        /// Copies chip-backed unlocks from source to destination via configured relay nodes.
        /// Copy semantics: source chips are retained; destination receives copies.
        /// Returns number of datachip copies successfully stored at destination.
        /// </summary>
        public int CopyDatachipsViaRelayNodes(StationState source, StationState destination)
        {
            if (source == null || destination == null) return 0;

            var sourceRelayFilters = new List<HashSet<ResearchBranch>>();
            foreach (var f in source.foundations.Values)
            {
                if (f.status != "complete") continue;
                if (f.buildableId != RelayNodeBuildable) continue;

                var filter = new HashSet<ResearchBranch>();
                foreach (var raw in f.relayBranchFilter)
                    if (Enum.TryParse(raw, out ResearchBranch b))
                        filter.Add(b);
                sourceRelayFilters.Add(filter);
            }
            if (sourceRelayFilters.Count == 0) return 0;

            var sourceNodes = GetChipBackedUnlockedNodes(source);
            int copied = 0;

            foreach (var node in sourceNodes)
            {
                bool allowed = false;
                foreach (var f in sourceRelayFilters)
                {
                    if (f.Count == 0 || f.Contains(node.branch)) { allowed = true; break; }
                }
                if (!allowed) continue;
                if (!_registry.ResearchNodes.ContainsKey(node.id)) continue;

                var dstBranch = destination.research.branches[node.branch];
                if (dstBranch.unlockedNodeIds.Contains(node.id)) continue;

                bool prereqsMet = true;
                foreach (var p in node.prerequisites)
                {
                    if (!destination.research.IsUnlocked(p)) { prereqsMet = false; break; }
                }
                if (!prereqsMet) continue;

                dstBranch.unlockedNodeIds.Add(node.id);
                if (!dstBranch.unlockedNodeOrder.Contains(node.id))
                    dstBranch.unlockedNodeOrder.Add(node.id);

                if (TryAddDatachipToStorage(destination))
                    copied++;
                else
                    destination.research.pendingDatachips++;
            }

            RefreshActiveUnlockTags(destination);
            return copied;
        }

        // ── Queries ───────────────────────────────────────────────────────────

        /// <summary>Nodes in <paramref name="branch"/> that are not yet unlocked but whose
        /// prerequisites are all met (i.e. ready to research).</summary>
        public ResearchNodeDefinition[] GetAvailableNodes(ResearchBranch branch, StationState station)
        {
            if (station?.research == null) return Array.Empty<ResearchNodeDefinition>();
            var list = new List<ResearchNodeDefinition>();
            foreach (var node in _registry.ResearchNodes.Values)
            {
                if (node.branch != branch) continue;
                if (station.research.IsUnlocked(node.id)) continue;
                bool prereqsMet = true;
                foreach (var p in node.prerequisites)
                    if (!station.research.IsUnlocked(p)) { prereqsMet = false; break; }
                if (prereqsMet) list.Add(node);
            }
            return list.ToArray();
        }

        /// <summary>Nodes in <paramref name="branch"/> that have already been unlocked.</summary>
        public ResearchNodeDefinition[] GetUnlockedNodes(ResearchBranch branch, StationState station)
        {
            if (station?.research == null) return Array.Empty<ResearchNodeDefinition>();
            var bState = station.research.branches[branch];
            var list   = new List<ResearchNodeDefinition>();
            foreach (var node in _registry.ResearchNodes.Values)
            {
                if (node.branch != branch) continue;
                if (bState.unlockedNodeIds.Contains(node.id)) list.Add(node);
            }
            return list.ToArray();
        }

        /// <summary>
        /// 0-1 fraction of points toward the cheapest available (prereqs-met) node,
        /// or 0 if none are available.
        /// </summary>
        public float GetProgressToNext(ResearchBranch branch, StationState station)
        {
            if (station?.research == null) return 0f;
            var available = GetAvailableNodes(branch, station);
            if (available.Length == 0) return 0f;

            float pts = station.research.branches[branch].points;
            int   cheapest = int.MaxValue;
            foreach (var n in available)
                if (n.pointCost < cheapest) cheapest = n.pointCost;

            return cheapest > 0 ? Mathf.Clamp01(pts / cheapest) : 1f;
        }

        /// <summary>
        /// Total datachips currently stored across all Data Storage Server foundations.
        /// </summary>
        public int GetStoredDatachipCount(StationState station)
        {
            int total = 0;
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != DataStorageBuildable) continue;
                if (f.status != "complete") continue;
                total += f.cargo.TryGetValue(DatachipItemId, out var n) ? n : 0;
            }
            return total;
        }

        /// <summary>
        /// Total datachip capacity across all complete Data Storage Server foundations.
        /// </summary>
        public int GetDatachipCapacity(StationState station)
        {
            int total = 0;
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != DataStorageBuildable) continue;
                if (f.status != "complete") continue;
                total += f.cargoCapacity;
            }
            return total;
        }
    }
}
