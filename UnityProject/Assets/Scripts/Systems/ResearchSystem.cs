// ResearchSystem — accumulates research points from crew working at research
// terminals and automatically unlocks nodes when prerequisites are met.
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

        // Terminal buildable-id → branch mapping
        private static readonly Dictionary<string, ResearchBranch> TerminalBranch =
            new Dictionary<string, ResearchBranch>
            {
                { "buildable.military_terminal",  ResearchBranch.Military   },
                { "buildable.economic_terminal",  ResearchBranch.Economics  },
                { "buildable.science_terminal",   ResearchBranch.Sciences   },
            };

        // Research job-id → branch mapping
        private static readonly Dictionary<string, ResearchBranch> JobBranch =
            new Dictionary<string, ResearchBranch>
            {
                { "job.research_military",  ResearchBranch.Military   },
                { "job.research_economic",  ResearchBranch.Economics  },
                { "job.research_science",   ResearchBranch.Sciences   },
            };

        public ResearchSystem(ContentRegistry registry) => _registry = registry;

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (station == null) return;
            if (station.research == null) station.research = ResearchState.Create();

            // Accumulate points: one entry per NPC doing a research job.
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.currentJobId == null) continue;
                if (!JobBranch.TryGetValue(npc.currentJobId, out var branch)) continue;

                int researchSkill = npc.skills.ContainsKey("research") ? npc.skills["research"] : 5;
                float pts = 0.04f * (0.5f + researchSkill / 10f);
                station.research.branches[branch].points += pts;
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
                bState.points -= node.pointCost;   // consume the spent points
                foreach (var tag in node.unlockTags)
                    station.SetTag(tag);
                station.LogEvent($"Research unlocked: {node.displayName}.");
                Debug.Log($"[ResearchSystem] Unlocked node '{node.id}'.");
            }
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
    }
}
