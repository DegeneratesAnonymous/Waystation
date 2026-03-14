// NPC System — procedural generation and runtime management of NPC instances.
// Generates NPCInstance objects from NPCTemplate definitions.
// Handles per-tick needs decay and mood recalculation.
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class NPCSystem
    {
        private readonly ContentRegistry _registry;

        // ── Name generation pools ──────────────────────────────────────────────
        private static readonly string[] FirstNames =
        {
            "Aiko","Brecht","Cael","Dara","Ezra","Fenn","Gira","Holt",
            "Idris","Jura","Kael","Lira","Moro","Naya","Oren","Petra",
            "Quinn","Rael","Siva","Thorn","Ulva","Vena","Wren","Xan",
            "Yola","Zeph","Asha","Brix","Cova","Deln"
        };
        private static readonly string[] Surnames =
        {
            "Vance","Orel","Dusk","Harlow","Mira","Crane","Forde","Sable",
            "Thane","Nori","Crest","Weld","Pell","Strix","Vael","Oryn"
        };

        // Needs decay rates per tick
        public static readonly Dictionary<string, float> NeedsDecayRate = new Dictionary<string, float>
        {
            { "hunger", -0.04f }, { "rest", -0.03f }, { "social", -0.01f }, { "safety", 0f }
        };

        private const float SkillXpPerTick           = 0.008f;
        private const float XpPerLevel               = 1.0f;
        private const float MaxPassiveSocialRecovery  = 0.02f;
        private const float SocialRecoveryPerPerson   = 0.005f;
        private const float InjuryRecoveryChance      = 0.05f;

        // Maps job IDs → the skill they train
        private static readonly Dictionary<string, string> JobSkillMap = new Dictionary<string, string>
        {
            { "job.guard_post",            "combat"       },
            { "job.patrol",                "perception"   },
            { "job.contraband_inspection", "investigation"},
            { "job.module_maintenance",    "repair"       },
            { "job.power_management",      "technical"    },
            { "job.life_support",          "technical"    },
            { "job.dock_control",          "coordination" },
            { "job.resource_management",   "logistics"    },
            { "job.visitor_intake",        "negotiation"  }
        };

        public NPCSystem(ContentRegistry registry) => _registry = registry;

        // ── Factory methods ────────────────────────────────────────────────────

        public NPCInstance SpawnFromTemplate(string templateId,
                                              List<string> statusTags = null,
                                              Dictionary<string, object> overrides = null)
        {
            if (!_registry.Npcs.TryGetValue(templateId, out var template))
            {
                Debug.LogError($"[NPCSystem] Unknown NPC template '{templateId}'");
                return null;
            }

            var npc = NPCInstance.Create(
                templateId: templateId,
                name:       GenerateName(template),
                classId:    template.baseClass,
                subclassId: PickSubclass(template)
            );

            npc.skills     = RollSkills(template);
            npc.traits     = PickTraits(template);
            npc.factionId  = PickFaction(template);
            npc.statusTags = statusTags != null ? new List<string>(statusTags) : new List<string>();

            if (overrides != null)
            {
                if (overrides.ContainsKey("faction_id"))  npc.factionId = overrides["faction_id"]?.ToString();
                if (overrides.ContainsKey("name"))         npc.name      = overrides["name"]?.ToString();
            }

            npc.RecalculateMood();
            return npc;
        }

        public NPCInstance SpawnCrewMember(string templateId)
            => SpawnFromTemplate(templateId, new List<string> { "crew" });

        public NPCInstance SpawnVisitor(string templateId, string factionId = null)
            => SpawnFromTemplate(templateId, new List<string> { "visitor" },
               factionId != null ? new Dictionary<string, object> { { "faction_id", factionId } } : null);

        // ── Per-tick update ────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            int population = station.npcs.Count;
            foreach (var npc in station.npcs.Values)
                TickNpc(npc, station, population);
        }

        private void TickNpc(NPCInstance npc, StationState station, int population)
        {
            npc.UpdateNeeds(NeedsDecayRate);

            // Food consumption
            if ((npc.IsCrew() || npc.IsVisitor()) && station.GetResource("food") > 0f)
            {
                npc.UpdateNeeds(new Dictionary<string, float> { { "hunger", 0.06f } });
                station.ModifyResource("food", -0.5f);
            }

            // Social recovery from population density
            float socialRecovery = Mathf.Min(MaxPassiveSocialRecovery, (population - 1) * SocialRecoveryPerPerson);
            if (socialRecovery > 0f)
                npc.UpdateNeeds(new Dictionary<string, float> { { "social", socialRecovery } });

            // Visitor lounge social boost
            foreach (var module in station.modules.Values)
            {
                if (module.active && module.definitionId.Contains("visitor_lounge"))
                {
                    npc.UpdateNeeds(new Dictionary<string, float> { { "social", 0.01f } });
                    break;
                }
            }

            // Injury recovery in med bay
            if (npc.injuries > 0)
            {
                foreach (var module in station.modules.Values)
                {
                    if (module.active && module.category == "utility" &&
                        module.definitionId.Contains("med_bay"))
                    {
                        if (UnityEngine.Random.value < InjuryRecoveryChance)
                            npc.injuries = Mathf.Max(0, npc.injuries - 1);
                        break;
                    }
                }
            }

            npc.RecalculateMood();

            // Skill progression
            if (npc.IsCrew() && npc.currentJobId != null)
                TryAdvanceSkill(npc);

            // Distress logging (rate-limited)
            if (npc.needs.ContainsKey("hunger") && npc.needs["hunger"] < 0.2f &&
                UnityEngine.Random.value < 0.1f)
                station.LogEvent($"{npc.name} is starving.");
            if (npc.needs.ContainsKey("rest") && npc.needs["rest"] < 0.1f &&
                UnityEngine.Random.value < 0.1f)
                station.LogEvent($"{npc.name} is exhausted.");

            // Location wandering: occasionally update npc.location so the data
            // model reflects movement.  Skip active builders so they aren't
            // relocated mid-construction.  Visual tile movement is handled
            // independently in StationRoomView.
            if (npc.IsCrew() && npc.currentJobId != "job.build" &&
                UnityEngine.Random.value < 0.05f)
            {
                WanderToActiveModule(npc, station);
            }
        }

        /// <summary>
        /// Move an idle crew NPC to a random active module so the visual dot
        /// wanders.  Prefers modules with "utility" or "commons" category, which
        /// are most likely to be pressurised and occupied areas.
        /// </summary>
        private static void WanderToActiveModule(NPCInstance npc, StationState station)
        {
            var candidates = new List<ModuleInstance>();
            foreach (var mod in station.modules.Values)
                if (mod.active && mod.category != "dock")
                    candidates.Add(mod);

            if (candidates.Count == 0) return;

            var chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            npc.location = chosen.definitionId;
            // Note: jobModuleUid is managed exclusively by JobSystem — do not set here.
        }

        private void TryAdvanceSkill(NPCInstance npc)
        {
            if (!JobSkillMap.TryGetValue(npc.currentJobId ?? "", out var skill) || skill == "") return;
            int current = npc.skills.ContainsKey(skill) ? npc.skills[skill] : 0;
            if (current >= 10) return;

            float xp = (npc.skillXp.ContainsKey(skill) ? npc.skillXp[skill] : 0f) + SkillXpPerTick;
            if (xp >= XpPerLevel)
            {
                npc.skills[skill] = current + 1;
                xp -= XpPerLevel;
            }
            npc.skillXp[skill] = xp;
        }

        // ── Convenience queries ────────────────────────────────────────────────

        public List<NPCInstance> GetCrewWithSkill(StationState station, string skill, int minLevel = 1)
        {
            var result = new List<NPCInstance>();
            foreach (var n in station.GetCrew())
                if ((n.skills.ContainsKey(skill) ? n.skills[skill] : 0) >= minLevel)
                    result.Add(n);
            return result;
        }

        public float AverageCrewMood(StationState station)
        {
            var crew = station.GetCrew();
            if (crew.Count == 0) return 0f;
            float total = 0f;
            foreach (var n in crew) total += n.mood;
            return total / crew.Count;
        }

        // ── Private generation helpers ──────────────────────────────────────────

        private string GenerateName(NPCTemplate template)
        {
            if (template.namePool.Count > 0)
                return template.namePool[UnityEngine.Random.Range(0, template.namePool.Count)];
            string first = FirstNames[UnityEngine.Random.Range(0, FirstNames.Length)];
            string last  = Surnames[UnityEngine.Random.Range(0, Surnames.Length)];
            return $"{first} {last}";
        }

        private Dictionary<string, int> RollSkills(NPCTemplate template)
        {
            var skills = new Dictionary<string, int>();
            foreach (var kv in template.skillRanges)
                skills[kv.Key] = UnityEngine.Random.Range(kv.Value.min, kv.Value.max + 1);
            return skills;
        }

        private List<string> PickTraits(NPCTemplate template, int count = 2)
        {
            var result = new List<string>();
            if (template.traitPool.Count == 0) return result;
            var pool = new List<string>(template.traitPool);
            int n = Mathf.Min(count, pool.Count);
            for (int i = 0; i < n; i++)
            {
                int idx = UnityEngine.Random.Range(0, pool.Count);
                result.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return result;
        }

        private string PickSubclass(NPCTemplate template)
        {
            if (template.allowedSubclasses.Count == 0) return null;
            return template.allowedSubclasses[UnityEngine.Random.Range(0, template.allowedSubclasses.Count)];
        }

        private string PickFaction(NPCTemplate template)
        {
            if (template.factionBias.Count == 0) return null;
            var factions = new List<string>(template.factionBias.Keys);
            float total  = 0f;
            foreach (var kv in template.factionBias) total += kv.Value;
            float roll = UnityEngine.Random.Range(0f, total);
            float acc  = 0f;
            foreach (var f in factions)
            {
                acc += template.factionBias[f];
                if (roll <= acc) return f;
            }
            return factions[factions.Count - 1];
        }
    }
}
