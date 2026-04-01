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

        private const float SkillXpPerTick           = 0.008f;
        private const float XpPerLevel               = 1.0f;
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

        // ── Sleep / Wake events ────────────────────────────────────────────────
        // Subscribed by MoodSystem in GameManager.InitSystems()
#pragma warning disable CS0067
        public event System.Action<NPCInstance> OnNPCSleeps;
        public event System.Action<NPCInstance> OnNPCWakes;
#pragma warning restore CS0067

        // ── Registry query ────────────────────────────────────────────────────

        /// <summary>All NPC template IDs currently registered in the content registry.</summary>
        public IEnumerable<string> AvailableTemplateIds => _registry.Npcs.Keys;

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
            return BuildNpcFromTemplate(template, templateId,
                PickTraits(template), statusTags, overrides);
        }

        /// <summary>
        /// Spawns an NPC from a template with trait selection biased toward the trait
        /// categories that align with the given government type (trait gravity mechanic).
        /// When <paramref name="governmentType"/> is null the behaviour is identical to
        /// <see cref="SpawnFromTemplate"/>.
        /// </summary>
        public NPCInstance SpawnWithGovernmentBias(string templateId,
                                                    GovernmentType? governmentType,
                                                    List<string> statusTags = null,
                                                    Dictionary<string, object> overrides = null)
        {
            if (!_registry.Npcs.TryGetValue(templateId, out var template))
            {
                Debug.LogError($"[NPCSystem] Unknown NPC template '{templateId}'");
                return null;
            }
            return BuildNpcFromTemplate(template, templateId,
                PickTraitsWithGovernmentBias(template, governmentType), statusTags, overrides);
        }

        /// <summary>
        /// Shared NPC construction core.  Callers are responsible for supplying the
        /// trait list (e.g., plain <see cref="PickTraits"/> or biased selection).
        /// All other construction steps — ability scores, skill rolls, faction assignment,
        /// need depletion rates, and mood initialisation — live here so the two public
        /// factory methods can never drift apart.
        /// </summary>
        /// <param name="template">The resolved NPC template.</param>
        /// <param name="templateId">The template ID string (used for ability-score archetype lookup).</param>
        /// <param name="traits">Pre-selected trait IDs to assign.</param>
        /// <param name="statusTags">Optional initial status tags (e.g. "crew", "visitor").</param>
        /// <param name="overrides">Optional dictionary overriding "faction_id" or "name".</param>
        private NPCInstance BuildNpcFromTemplate(NPCTemplate template, string templateId,
                                                  List<string> traits,
                                                  List<string> statusTags,
                                                  Dictionary<string, object> overrides)
        {
            var npc = NPCInstance.Create(
                templateId: templateId,
                name:       GenerateName(template),
                classId:    template.baseClass,
                subclassId: PickSubclass(template)
            );

            npc.skills     = RollSkills(template);
            npc.traits     = traits;
            npc.factionId  = PickFaction(template);
            npc.statusTags = statusTags != null ? new List<string>(statusTags) : new List<string>();

            if (overrides != null)
            {
                if (overrides.ContainsKey("faction_id")) npc.factionId = overrides["faction_id"]?.ToString();
                if (overrides.ContainsKey("name"))        npc.name      = overrides["name"]?.ToString();
            }

            AssignAbilityScores(npc, templateId);

            if (template.needDepletionRates != null && template.needDepletionRates.Count > 0)
                npc.needDepletionRates = new System.Collections.Generic.Dictionary<string, float>(
                    template.needDepletionRates);

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
            // NPCs on away missions are off-station — skip all local routing.
            if (npc.missionUid != null) return;

            // Needs, sleep routing, and mood recalculation are now handled by NeedSystem.
            // MoodSystem.TickMood handles drift and modifier expiry.

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

            // Skill progression + rank promotion
            if (npc.IsCrew() && npc.currentJobId != null)
            {
                TryAdvanceSkill(npc);
                TryPromoteRank(npc, station);
            }

            // Daily aging and life stage update
            if (station.tick % 96 == 0)
            {
                npc.ageDays++;
                UpdateLifeStageAndSlots(npc);
            }

            // Idle wandering: move to a random oxygenated module periodically.
            if (npc.IsCrew() && string.IsNullOrEmpty(npc.currentJobId) &&
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
            npc.location     = chosen.definitionId;
            npc.jobModuleUid = chosen.uid;
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

        // ── Rank progression ──────────────────────────────────────────────────
        // Rank is driven by total skill points across all skills.
        //   Rank 0 (Crew)           :  0 – 29
        //   Rank 1 (Officer)        : 30 – 59
        //   Rank 2 (Senior Officer) : 60 – 99
        //   Rank 3 (Command)        : 100+
        // Promotion awards one trait drawn from a rank-specific pool.

        private const int RankOfficerThreshold = 30;
        private const int RankSeniorThreshold  = 60;
        private const int RankCommandThreshold = 100;

        private static readonly Dictionary<int, string[]> RankTraitPool =
            new Dictionary<int, string[]>
        {
            { 1, new[] { "experienced", "disciplined", "reliable"  } },
            { 2, new[] { "tactical",    "composed",    "inspiring" } },
            { 3, new[] { "legendary",   "commanding",  "veteran"   } },
        };

        private static int ComputeTargetRank(int totalSkill)
        {
            if (totalSkill >= RankCommandThreshold) return 3;
            if (totalSkill >= RankSeniorThreshold)  return 2;
            if (totalSkill >= RankOfficerThreshold) return 1;
            return 0;
        }

        private static string RankLabel(int rank) => rank switch
        {
            1 => "Officer",
            2 => "Senior Officer",
            3 => "Command",
            _ => "Crew",
        };

        private void TryPromoteRank(NPCInstance npc, StationState station)
        {
            int total = 0;
            foreach (var v in npc.skills.Values) total += v;

            int target = ComputeTargetRank(total);
            if (target <= npc.rank) return;   // no promotion (also covers demotion safety)

            npc.rank = target;

            // Award a trait from the rank pool (skip if already possessed).
            if (RankTraitPool.TryGetValue(target, out var pool))
            {
                // Use uid hash for deterministic-but-varied selection.
                int pick = (System.Math.Abs(npc.uid.GetHashCode()) + target) % pool.Length;
                string trait = pool[pick];
                if (!npc.traits.Contains(trait)) npc.traits.Add(trait);
            }

            station.LogEvent($"{npc.name} promoted to {RankLabel(npc.rank)}.");
        }

        // ── Bed routing ───────────────────────────────────────────────────────

        private static void TryClaimBed(NPCInstance npc, StationState station)
        {
            // Already has a valid bed assigned — just start sleeping there.
            if (npc.sleepBedUid != null &&
                station.foundations.TryGetValue(npc.sleepBedUid, out var existing) &&
                existing.status == "complete")
            {
                npc.isSleeping = true;
                return;
            }

            // Find any unoccupied, completed bed foundation.
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.bed") continue;
                if (f.status != "complete") continue;
                bool takenByOther = false;
                foreach (var other in station.npcs.Values)
                {
                    if (other.uid != npc.uid && other.sleepBedUid == f.uid)
                    { takenByOther = true; break; }
                }
                if (takenByOther) continue;

                npc.sleepBedUid = f.uid;
                npc.isSleeping  = true;
                return;
            }
            // No bed available — can't sleep yet
        }

        // ── Department skill bonus ─────────────────────────────────────────────

        /// <summary>
        /// Returns the effective skill value for an NPC on a given job, applying
        /// a +5 % department bonus when the NPC is the sole holder of a job within
        /// a matching department.
        /// </summary>
        public int GetEffectiveSkill(NPCInstance npc, string jobId, StationState station)
        {
            string skillKey = JobSkillMap.ContainsKey(jobId) ? JobSkillMap[jobId] : null;
            int baseSkill = (skillKey != null && npc.skills.ContainsKey(skillKey))
                            ? npc.skills[skillKey] : 0;

            if (npc.departmentId == null) return baseSkill;  // Crewman — no bonus

            // Find the department this NPC belongs to
            Department dept = null;
            foreach (var d in station.departments)
                if (d.uid == npc.departmentId) { dept = d; break; }
            if (dept == null || !dept.allowedJobs.Contains(jobId)) return baseSkill;

            // Count how many NPCs OTHER than this one hold this job in the same dept
            int holderCount = 0;
            foreach (var other in station.npcs.Values)
            {
                if (other.uid == npc.uid) continue;
                if (other.departmentId == npc.departmentId && other.currentJobId == jobId)
                    holderCount++;
            }
            // sole holder → 5 % bonus (round is fine for skill values 0-10)
            if (holderCount == 0)
                return Mathf.RoundToInt(baseSkill * 1.05f);

            return baseSkill;
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

        /// <summary>
        /// Assigns ability scores using the standard array [14, 12, 11, 10, 9, 8]
        /// distributed by archetype (templateId prefix).
        /// STR DEX INT WIS CHA END order.
        /// </summary>
        private static void AssignAbilityScores(NPCInstance npc, string templateId)
        {
            // Standard array: STR, DEX, INT, WIS, CHA, END
            (int str, int dex, int @int, int wis, int cha, int end) scores = templateId switch
            {
                var t when t.Contains("engineer")         => (9,  10, 14, 12, 8,  11),
                var t when t.Contains("security")         => (14, 12, 8,  9,  10, 11),
                var t when t.Contains("scientist")        => (8,  10, 14, 12, 11, 9 ),
                var t when t.Contains("medic")            => (8,  10, 12, 14, 11, 9 ),
                var t when t.Contains("merchant")         => (8,  10, 10, 10, 14, 11),
                var t when t.Contains("pilot")            => (9,  14, 12, 10, 8,  11),
                _                                         => (8,  10, 14, 12, 9,  11),
            };
            npc.abilityScores.STR = scores.str;
            npc.abilityScores.DEX = scores.dex;
            npc.abilityScores.INT = scores.@int;
            npc.abilityScores.WIS = scores.wis;
            npc.abilityScores.CHA = scores.cha;
            npc.abilityScores.END = scores.end;
        }

        /// <summary>
        /// Updates life stage and trait slot count based on ageDays.
        /// Baby → Child at 3 in-game years (1095 days).
        /// Child → Adult at 18 in-game years (6570 days).
        /// </summary>
        private static void UpdateLifeStageAndSlots(NPCInstance npc)
        {
            LifeStage newStage = npc.ageDays >= 6570 ? LifeStage.Adult
                               : npc.ageDays >= 1095 ? LifeStage.Child
                               :                       LifeStage.Baby;
            npc.lifeStage = newStage;

            npc.traitSlots = newStage switch
            {
                LifeStage.Baby  => 1,
                LifeStage.Child => 2,
                _ =>               3, // Adult base; expertise adds slots via SkillSystem
            };
        }

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

        /// <summary>
        /// Picks traits from the template's trait pool, giving double weight to traits
        /// whose category aligns with the preferred categories for <paramref name="governmentType"/>.
        /// Falls back to the unbiased selection when no trait category information is
        /// available in the registry or when <paramref name="governmentType"/> is null.
        /// </summary>
        private List<string> PickTraitsWithGovernmentBias(NPCTemplate template,
                                                           GovernmentType? governmentType,
                                                           int count = 2)
        {
            if (governmentType == null || template.traitPool.Count == 0)
                return PickTraits(template, count);

            var preferredCategories = FactionProceduralGenerator.GetGovTraitAffinity(governmentType.Value);
            if (preferredCategories.Count == 0)
                return PickTraits(template, count);

            // Build a weighted list: biased traits have weight 2.0, others weight 1.0
            var weighted = new List<(string traitId, float weight)>();
            float totalWeight = 0f;
            foreach (var traitId in template.traitPool)
            {
                // Try to get the trait definition from the registry to check its category.
                // If the trait isn't in the registry (e.g. id mismatch) give it base weight.
                float weight = 1.0f;
                if (_registry.Traits.TryGetValue(traitId, out var def) &&
                    preferredCategories.Contains(def.category))
                    weight = 2.0f;

                weighted.Add((traitId, weight));
                totalWeight += weight;
            }

            var result = new List<string>();
            int n = Mathf.Min(count, weighted.Count);
            for (int i = 0; i < n; i++)
            {
                float roll = UnityEngine.Random.Range(0f, totalWeight);
                float acc  = 0f;
                int   pick = weighted.Count - 1;
                for (int j = 0; j < weighted.Count; j++)
                {
                    acc += weighted[j].weight;
                    if (roll <= acc) { pick = j; break; }
                }
                result.Add(weighted[pick].traitId);
                totalWeight -= weighted[pick].weight;
                weighted.RemoveAt(pick);
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
