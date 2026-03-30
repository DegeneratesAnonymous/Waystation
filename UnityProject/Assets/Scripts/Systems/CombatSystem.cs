// Combat System — resolves violent engagements aboard and around the station.
//
// Resolution model:
//   Defender power = sum of security crew combat skills + armed modules
//   Attacker power = ship.threatLevel × 10
//   Net power + gaussian jitter → outcome tier
//
// Outcome tiers:
//   repelled_clean    — attackers driven off, no casualties, minor parts cost
//   repelled_damaged  — attackers driven off, station damage, crew injuries
//   partial_defeat    — station looted, crew traumatised, attackers eventually leave
//   overrun           — station critically damaged, major resource loss
//
// Six combat scenario types (STA-003):
//   Boarding         — enemy NPCs breach the hull and fight on station tile map
//   Raid             — aggressive assault variant of boarding, higher attacker power
//   ShipToStation    — weapons fire hits hull/equipment tiles before potential boarding
//   StationToStation — a hostile station fires on this station (same as ShipToStation but no boarding)
//   Sabotage         — an internal actor disables critical foundations
//   MentalBreak      — a crew member in breakdown enters a hostile state
//
// NPC Combat AI:
//   NpcCombatState tracks per-NPC HP and AI flags.
//   EvaluateRetreat() returns true when HP falls strictly below RetreatThreshold (30% of max).
//   SelectWeaponForRange() picks the best weapon class for the given enemy distance.
//   ShouldSeekCover()  returns true when incoming burst damage is high relative to HP.
//
// Crew outcome resolution:
//   Killed   — death consequences fire via DeathHandlingSystem (if wired)
//   Injured  — wound added to MedicalTickSystem (if wired)
//   Captured — NPC removed from active roster and added to StationState.capturedNpcs
//
// Feature gate: FeatureFlags.CombatSystem (false → abstract resolution only)
//               FeatureFlags.MentalBreakCombat (false → mental-break NPCs stay non-combat)
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // ── Scenario type ─────────────────────────────────────────────────────────

    /// <summary>Which of the six defined combat scenarios is being resolved.</summary>
    public enum CombatScenarioType
    {
        Boarding,
        Raid,
        ShipToStation,
        StationToStation,
        Sabotage,
        MentalBreak,
    }

    // ── Per-NPC outcome ───────────────────────────────────────────────────────

    /// <summary>Individual outcome for a crew NPC involved in combat.</summary>
    public enum NpcCombatOutcome { Survived, Injured, Killed, Captured }

    // ── NPC Combat AI state ───────────────────────────────────────────────────

    /// <summary>
    /// Transient per-NPC state during a real-time combat engagement.
    /// Instantiated by <see cref="CombatSystem"/> for each participant.
    /// </summary>
    public class NpcCombatState
    {
        /// <summary>Retreat when HP/maxHp falls strictly below this fraction (exclusive).</summary>
        public const float RetreatThreshold = 0.3f;

        public string npcUid;
        public float  hp;
        public float  maxHp;
        public bool   isRetreating;
        public bool   isIncapacitated;
        /// <summary>"attacker" or "defender"</summary>
        public string faction;

        /// <summary>True when HP is below the retreat threshold.</summary>
        public bool ShouldRetreat() => maxHp > 0f && (hp / maxHp) < RetreatThreshold;
    }

    // ── Outcome data ──────────────────────────────────────────────────────────

    public class CombatOutcome
    {
        public CombatScenarioType scenario;
        public string tier;         // repelled_clean / repelled_damaged / partial_defeat / overrun
        public string narrative;
        public float  creditsLost  = 0f;
        public float  partsLost    = 0f;
        public float  foodLost     = 0f;
        public float  moduleDamage = 0f;   // fractional damage applied to a random dock module
        public float  crewTrauma   = 0f;   // safety need reduction applied to all crew
        public int    crewInjuries = 0;    // number of crew injury points inflicted

        // Per-NPC outcome tracking (STA-003)
        public List<string> killedNpcUids   = new List<string>();
        public List<string> injuredNpcUids  = new List<string>();
        public List<string> capturedNpcUids = new List<string>();

        /// <summary>Total HP of hull damage applied to station foundations.</summary>
        public int hullDamageApplied = 0;
    }

    // ── Main system ───────────────────────────────────────────────────────────

    public class CombatSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        public const float CombatVariance             = 8f;
        public const int   SecurityDefaultSkill       = 2;
        public const float SecurityPowerMultiplier    = 2.5f;
        public const int   NonSecurityDefaultSkill    = 1;
        public const float NonSecurityPowerMultiplier = 1f;
        public const float GuardPostBonus             = 10f;
        public const float SecurityModuleBonus        = 5f;

        /// <summary>
        /// Attacker power multiplier for a Raid scenario (more aggressive than a Boarding).
        /// </summary>
        public const float RaidPowerMultiplier        = 1.5f;

        /// <summary>Hull HP damage applied per weapon salvo during ShipToStation fire.</summary>
        public const int ShipToStationSalvoDamage     = 20;

        // ── Optional system dependencies ──────────────────────────────────────

        private DeathHandlingSystem _death;
        private MedicalTickSystem   _medical;
        private BuildingSystem      _building;

        public void SetDeathHandlingSystem(DeathHandlingSystem d) => _death    = d;
        public void SetMedicalSystem(MedicalTickSystem m)         => _medical  = m;
        public void SetBuildingSystem(BuildingSystem b)           => _building = b;

        // ── Narratives ────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string[]> OutcomeNarratives =
            new Dictionary<string, string[]>
        {
            { "repelled_clean", new[]
              {
                  "Security holds the breach — raiders driven back to their ship. Hull integrity intact.",
                  "A sharp, professional defence. The boarding party retreats with heavy losses.",
                  "Your security crew closes the breach and locks the airlock. No blood on the decks."
              }
            },
            { "repelled_damaged", new[]
              {
                  "Boarders repelled after a brutal corridor fight. Two crew injured, docking bay scorched.",
                  "The breach is sealed — but not before the docking bay takes hits. Repair crews moving in.",
                  "Attackers fall back. The fight leaves blast scarring on the bay walls and one crew member limping."
              }
            },
            { "partial_defeat", new[]
              {
                  "Raiders push through briefly, grab what they can from storage, then withdraw. Expensive.",
                  "The corridor fight goes badly — boarders loot the outer hold before a bulkhead seal forces them off.",
                  "Breach contained to Docking Bay A. Raiders strip the cargo bay. Security holds the core."
              }
            },
            { "overrun", new[]
              {
                  "Overwhelming force — the station is boarded from multiple points. Crew falls back to the command center.",
                  "Catastrophic breach. Raiders ransack half the station before departing. The damage will take days to repair.",
                  "Security is overwhelmed. The station survives, barely. Everything of value near the docking ring is gone."
              }
            }
        };

        // ── Scenario 1: Boarding ──────────────────────────────────────────────

        /// <summary>
        /// Resolves a standard boarding attempt.  Enemy NPCs breach the hull and
        /// fight on the station tile map (real-time gated by FeatureFlags.CombatSystem).
        /// </summary>
        public CombatOutcome ResolveBoardingAttempt(StationState station, ShipInstance ship)
        {
            float defenderPower = CalculateDefenderPower(station);
            float attackerPower = ship.threatLevel * 10f;

            float jitter = SampleGaussian(0f, CombatVariance);
            float net    = defenderPower - attackerPower + jitter;

            string tier    = NetToTier(net);
            var    outcome = BuildOutcome(tier, ship, station);
            outcome.scenario = CombatScenarioType.Boarding;
            ApplyOutcome(outcome, station, ship.name);

            Debug.Log($"[CombatSystem] Boarding ship={ship.name} threat={ship.threatLevel} " +
                      $"def={defenderPower:F1} net={net:F1} tier={tier}");
            return outcome;
        }

        // ── Scenario 2: Raid ─────────────────────────────────────────────────

        /// <summary>
        /// Resolves a raid — a more aggressive boarding variant where attacker power
        /// is multiplied by <see cref="RaidPowerMultiplier"/>.
        /// </summary>
        public CombatOutcome ResolveRaid(StationState station, ShipInstance ship)
        {
            float defenderPower = CalculateDefenderPower(station);
            float attackerPower = ship.threatLevel * 10f * RaidPowerMultiplier;

            float jitter = SampleGaussian(0f, CombatVariance);
            float net    = defenderPower - attackerPower + jitter;

            string tier    = NetToTier(net);
            var    outcome = BuildOutcome(tier, ship, station);
            outcome.scenario = CombatScenarioType.Raid;
            ApplyOutcome(outcome, station, ship.name);

            Debug.Log($"[CombatSystem] Raid ship={ship.name} threat={ship.threatLevel} " +
                      $"def={defenderPower:F1} net={net:F1} tier={tier}");
            return outcome;
        }

        // ── Scenario 3: Ship-to-Station weapons fire ─────────────────────────

        /// <summary>
        /// Resolves weapons fire from a hostile ship on the station.
        /// Applies hull damage to foundations in the target area via BuildingSystem,
        /// then resolves the combat outcome (potential boarding).
        /// </summary>
        public CombatOutcome ResolveShipToStation(StationState station, ShipInstance ship)
        {
            int hullDamage = Mathf.RoundToInt(ShipToStationSalvoDamage * Mathf.Max(1f, ship.threatLevel / 5f));

            // Apply damage to hull/equipment foundations via BuildingSystem
            int damageApplied = ApplyHullDamage(station, hullDamage);

            float defenderPower = CalculateDefenderPower(station);
            float attackerPower = ship.threatLevel * 10f;

            float jitter = SampleGaussian(0f, CombatVariance);
            float net    = defenderPower - attackerPower + jitter;

            string tier    = NetToTier(net);
            var    outcome = BuildOutcome(tier, ship, station);
            outcome.scenario          = CombatScenarioType.ShipToStation;
            outcome.hullDamageApplied = damageApplied;
            ApplyOutcome(outcome, station, ship.name);

            station.LogEvent($"Weapons fire from {ship.name} strikes the station hull ({hullDamage} HP damage).");
            Debug.Log($"[CombatSystem] ShipToStation ship={ship.name} hull={hullDamage} tier={tier}");
            return outcome;
        }

        // ── Scenario 4: Station-to-Station fire ──────────────────────────────

        /// <summary>
        /// Resolves weapons fire from a hostile station or fixed emplacement.
        /// Applies hull damage but does not trigger boarding.
        /// </summary>
        public CombatOutcome ResolveStationToStation(StationState station, string attackerName,
                                                      int threatLevel)
        {
            int hullDamage    = Mathf.RoundToInt(ShipToStationSalvoDamage * Mathf.Max(1f, threatLevel / 5f));
            int damageApplied = ApplyHullDamage(station, hullDamage);

            // No boarding for station-to-station — just hull damage + crew trauma
            float defenderPower = CalculateDefenderPower(station);
            float attackerPower = threatLevel * 10f;
            float jitter        = SampleGaussian(0f, CombatVariance);
            float net           = defenderPower - attackerPower + jitter;
            string tier         = NetToTier(net);

            var narratives = OutcomeNarratives[tier];
            string narrative = narratives[UnityEngine.Random.Range(0, narratives.Length)];

            var outcome = new CombatOutcome
            {
                scenario          = CombatScenarioType.StationToStation,
                tier              = tier,
                narrative         = $"Station weapons fire from {attackerName}. {narrative}",
                partsLost         = 10f * Mathf.Max(1f, threatLevel / 5f),
                moduleDamage      = tier == "overrun" ? 0.3f : (tier == "partial_defeat" ? 0.2f : 0.1f),
                crewTrauma        = 0.2f,
                crewInjuries      = tier == "overrun" ? 2 : (tier == "partial_defeat" ? 1 : 0),
                hullDamageApplied = damageApplied,
            };

            ApplyOutcome(outcome, station, attackerName);
            station.LogEvent($"Station artillery from {attackerName} strikes the hull ({hullDamage} HP damage).");
            Debug.Log($"[CombatSystem] StationToStation attacker={attackerName} hull={hullDamage} tier={tier}");
            return outcome;
        }

        // ── Scenario 5: Sabotage ──────────────────────────────────────────────

        /// <summary>
        /// Resolves an internal sabotage event.  A saboteur disables one or more
        /// critical foundations, causing resource loss and crew trauma.
        /// </summary>
        public CombatOutcome ResolveSabotage(StationState station, string saboteurName = "Unknown saboteur")
        {
            // Sabotage targets foundations by disabling them via BuildingSystem
            int targetsHit = 0;
            if (_building != null && FeatureFlags.CombatSystem)
            {
                var candidates = new List<FoundationInstance>();
                foreach (var f in station.foundations.Values)
                    if (f.status == "complete" && f.operatingState != "broken")
                        candidates.Add(f);

                int count = Mathf.Min(2, candidates.Count);
                // Fisher-Yates shuffle for random selection
                for (int i = candidates.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }
                for (int i = 0; i < count; i++)
                {
                    _building.DamageFoundation(station, candidates[i].uid, candidates[i].health);
                    targetsHit++;
                }
            }

            float crewTraumaVal = 0.3f;
            string narrative = targetsHit > 0
                ? $"Saboteur ({saboteurName}) has disabled {targetsHit} critical system(s). Security is investigating."
                : $"Sabotage detected — damage contained. Saboteur ({saboteurName}) apprehended.";

            var outcome = new CombatOutcome
            {
                scenario     = CombatScenarioType.Sabotage,
                tier         = targetsHit > 0 ? "repelled_damaged" : "repelled_clean",
                narrative    = narrative,
                creditsLost  = 50f * targetsHit,
                partsLost    = 10f * targetsHit,
                crewTrauma   = crewTraumaVal,
                crewInjuries = 0,
            };

            ApplyOutcome(outcome, station, saboteurName);
            station.LogEvent($"Sabotage event resolved. Systems hit: {targetsHit}.");
            Debug.Log($"[CombatSystem] Sabotage saboteur={saboteurName} targets={targetsHit}");
            return outcome;
        }

        // ── Scenario 6: Mental Break Combat ──────────────────────────────────

        /// <summary>
        /// Resolves a mental break combat event.  The breakdown NPC enters a hostile
        /// state.  Resolution prefers non-lethal incapacitation (via counsellor) over
        /// lethal force.
        ///
        /// When <paramref name="counsellorAvailable"/> is true the breakdown NPC is
        /// incapacitated non-lethally; the outcome tier is "repelled_clean" with no
        /// crew killed.  When false the NPC may be killed or injured.
        ///
        /// Gated by <see cref="FeatureFlags.MentalBreakCombat"/>.
        /// </summary>
        public CombatOutcome ResolveMentalBreakCombat(StationState station,
                                                       NPCInstance   breakdown,
                                                       bool          counsellorAvailable)
        {
            if (!FeatureFlags.MentalBreakCombat)
            {
                // Fallback: breakdown stays non-combat; no outcome
                return new CombatOutcome
                {
                    scenario  = CombatScenarioType.MentalBreak,
                    tier      = "repelled_clean",
                    narrative = $"{breakdown.name} is in distress but has not become violent.",
                };
            }

            if (counsellorAvailable)
            {
                // Non-lethal: incapacitate the breakdown NPC via counselling
                var san = breakdown.GetOrCreateSanity();
                san.requiresIntervention = true;

                string narrative =
                    $"Counsellor intervenes as {breakdown.name} enters a breakdown rage. " +
                    "Non-lethal de-escalation successful — no crew injured.";

                station.LogEvent(narrative);
                Debug.Log($"[CombatSystem] MentalBreak npc={breakdown.name} non-lethal via counsellor");

                return new CombatOutcome
                {
                    scenario   = CombatScenarioType.MentalBreak,
                    tier       = "repelled_clean",
                    narrative  = narrative,
                    crewTrauma = 0.1f,
                };
            }
            else
            {
                // Lethal path: security must physically subdue the NPC
                // Roll whether the NPC is incapacitated, injured, or lethal
                float roll = UnityEngine.Random.value;

                string tier;
                string narrative;
                bool breakdownNpcKilled = false;

                if (roll < 0.5f)
                {
                    tier = "repelled_damaged";
                    narrative = $"{breakdown.name}'s breakdown escalated to violence. Security subdued them after a struggle — injuries on both sides.";
                }
                else if (roll < 0.8f)
                {
                    tier = "partial_defeat";
                    narrative = $"{breakdown.name} attacked crew before being restrained. Multiple injuries; medical assistance required.";
                }
                else
                {
                    tier = "overrun";
                    narrative = $"{breakdown.name}'s breakdown turned lethal. Security had no choice but to use lethal force.";
                    breakdownNpcKilled = true;
                }

                var outcome = new CombatOutcome
                {
                    scenario     = CombatScenarioType.MentalBreak,
                    tier         = tier,
                    narrative    = narrative,
                    crewTrauma   = tier == "overrun" ? 0.4f : 0.25f,
                    crewInjuries = tier == "repelled_damaged" ? 1 : (tier == "partial_defeat" ? 2 : 1),
                };

                // Apply crew trauma/injuries to the rest of crew (not the breakdown NPC)
                var crew = station.GetCrew();
                foreach (var npc in crew)
                {
                    if (npc.uid == breakdown.uid) continue;
                    if (outcome.crewTrauma > 0f)
                    {
                        npc.UpdateNeeds(new Dictionary<string, float> { { "safety", -outcome.crewTrauma } });
                        npc.RecalculateMood();
                    }
                }

                if (breakdownNpcKilled)
                {
                    // Mark breakdown NPC dead and fire death consequences
                    breakdown.statusTags.Add("dead");
                    outcome.killedNpcUids.Add(breakdown.uid);
                    station.RemoveNpc(breakdown.uid);
                    _death?.OnNPCDied(breakdown, station);
                    station.LogEvent($"{breakdown.name} was killed during a mental break incident.");
                }
                else
                {
                    // Non-lethal but injured — apply wound data if MedicalSystem is available
                    outcome.injuredNpcUids.Add(breakdown.uid);
                    ApplyInjuryWound(breakdown, station);
                    station.LogEvent($"{breakdown.name} was subdued during a mental break incident.");
                }

                Debug.Log($"[CombatSystem] MentalBreak npc={breakdown.name} tier={tier} killed={breakdownNpcKilled}");
                return outcome;
            }
        }

        // ── NPC Combat AI helpers ─────────────────────────────────────────────

        /// <summary>
        /// Evaluates whether a combatant should retreat based on their current HP
        /// relative to the <see cref="NpcCombatState.RetreatThreshold"/>.
        /// Called each AI tick to determine movement intent.
        /// </summary>
        public static bool EvaluateRetreat(NpcCombatState state)
        {
            if (state == null) return false;
            if (state.isIncapacitated) return false;
            return state.ShouldRetreat();
        }

        /// <summary>
        /// Returns the preferred weapon class for the given distance to the nearest
        /// enemy.  Called by NPC AI to select the best available attack.
        /// </summary>
        /// <param name="distanceTiles">Manhattan distance to nearest enemy in tiles.</param>
        public static string SelectWeaponForRange(float distanceTiles)
        {
            if (distanceTiles >= 6f) return "ranged_long";   // sniper / rifle
            if (distanceTiles >= 3f) return "ranged_short";  // pistol / shotgun
            return "melee";                                   // knife / baton / fists
        }

        /// <summary>
        /// Returns true when a combatant should seek cover based on incoming burst
        /// damage relative to their remaining HP.  Called by NPC AI each turn.
        /// </summary>
        /// <param name="incomingBurstDamage">Estimated incoming damage this turn.</param>
        public static bool ShouldSeekCover(NpcCombatState state, float incomingBurstDamage)
        {
            if (state == null || state.isIncapacitated) return false;
            // Seek cover if burst damage would drop HP below the retreat threshold
            return state.hp > 0f && incomingBurstDamage >= state.hp * NpcCombatState.RetreatThreshold;
        }

        // ── Power calculation ─────────────────────────────────────────────────

        private float CalculateDefenderPower(StationState station)
        {
            var crew = station.GetCrew();
            if (crew.Count == 0) return 0f;

            float power = 0f;
            foreach (var n in crew)
            {
                bool isSecurity = n.classId == "class.security";
                if (isSecurity)
                    power += (n.skills.ContainsKey("combat") ? n.skills["combat"] : SecurityDefaultSkill)
                             * SecurityPowerMultiplier;
                else
                    power += (n.skills.ContainsKey("combat") ? n.skills["combat"] : NonSecurityDefaultSkill)
                             * NonSecurityPowerMultiplier;
            }

            if (station.HasTag("station_guarded"))
                power += GuardPostBonus;

            foreach (var module in station.modules.Values)
                if (module.active && module.category == "security" && module.damage < 0.5f)
                    power += SecurityModuleBonus;

            return power;
        }

        // ── Tier mapping ──────────────────────────────────────────────────────

        private static string NetToTier(float net)
        {
            if (net >= 12f) return "repelled_clean";
            if (net >= 2f)  return "repelled_damaged";
            if (net >= -8f) return "partial_defeat";
            return "overrun";
        }

        // ── Outcome construction ──────────────────────────────────────────────

        private static CombatOutcome BuildOutcome(string tier, ShipInstance ship, StationState station)
        {
            var narratives = OutcomeNarratives[tier];
            string narrative = narratives[UnityEngine.Random.Range(0, narratives.Length)];
            float threatScale = Mathf.Max(1f, ship.threatLevel / 5f);

            switch (tier)
            {
                case "repelled_clean":
                    return new CombatOutcome { tier = tier, narrative = narrative,
                        partsLost = 5f * threatScale, crewTrauma = 0.05f, crewInjuries = 0 };
                case "repelled_damaged":
                    return new CombatOutcome { tier = tier, narrative = narrative,
                        partsLost = 15f * threatScale, moduleDamage = 0.15f,
                        crewTrauma = 0.15f, crewInjuries = 1 };
                case "partial_defeat":
                    return new CombatOutcome { tier = tier, narrative = narrative,
                        creditsLost = 100f * threatScale, foodLost = 20f * threatScale,
                        partsLost = 10f * threatScale, moduleDamage = 0.25f,
                        crewTrauma = 0.25f, crewInjuries = 2 };
                default: // overrun
                    return new CombatOutcome { tier = tier, narrative = narrative,
                        creditsLost = 250f * threatScale, foodLost = 50f * threatScale,
                        partsLost = 25f * threatScale, moduleDamage = 0.45f,
                        crewTrauma = 0.4f, crewInjuries = 3 };
            }
        }

        // ── Apply effects ─────────────────────────────────────────────────────

        private void ApplyOutcome(CombatOutcome outcome, StationState station, string attackerName = "")
        {
            if (outcome.creditsLost > 0f) station.ModifyResource("credits", -outcome.creditsLost);
            if (outcome.partsLost   > 0f) station.ModifyResource("parts",   -outcome.partsLost);
            if (outcome.foodLost    > 0f) station.ModifyResource("food",    -outcome.foodLost);

            // Module damage — random dock module
            if (outcome.moduleDamage > 0f)
            {
                var dockModules = new List<ModuleInstance>();
                foreach (var m in station.modules.Values)
                    if (m.category == "dock" && m.active) dockModules.Add(m);
                if (dockModules.Count > 0)
                {
                    var target = dockModules[UnityEngine.Random.Range(0, dockModules.Count)];
                    target.damage = Mathf.Min(1f, target.damage + outcome.moduleDamage);
                    if (target.damage >= 1f)
                    {
                        target.active = false;
                        station.LogEvent($"{target.displayName} destroyed in combat!");
                    }
                }
            }

            // Crew trauma + injuries → crew outcome resolution
            var crew = station.GetCrew();
            foreach (var npc in crew)
            {
                if (outcome.crewTrauma > 0f)
                {
                    npc.UpdateNeeds(new Dictionary<string, float> { { "safety", -outcome.crewTrauma } });
                    npc.RecalculateMood();
                }
            }

            ApplyCrewOutcomes(outcome, station, crew, attackerName);
        }

        /// <summary>
        /// Distributes crew outcome resolution (killed / injured / captured) for the
        /// number of casualties specified in <paramref name="outcome"/>.
        /// Fires death consequences, MedicalSystem wounds, and captured pool updates.
        /// </summary>
        private void ApplyCrewOutcomes(CombatOutcome outcome, StationState station,
                                        List<NPCInstance> crew, string attackerName)
        {
            if (outcome.crewInjuries <= 0 || crew.Count == 0) return;

            int toAffect = Mathf.Min(outcome.crewInjuries, crew.Count);

            // Fisher-Yates shuffle to pick random victims
            for (int i = crew.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (crew[i], crew[j]) = (crew[j], crew[i]);
            }

            for (int i = 0; i < toAffect; i++)
            {
                var npc    = crew[i];
                // For overrun: some crew may be killed or captured; otherwise injured
                float roll = UnityEngine.Random.value;
                NpcCombatOutcome npcOutcome;

                if (outcome.tier == "overrun")
                    npcOutcome = roll < 0.3f ? NpcCombatOutcome.Killed
                               : roll < 0.5f ? NpcCombatOutcome.Captured
                               :               NpcCombatOutcome.Injured;
                else if (outcome.tier == "partial_defeat")
                    npcOutcome = roll < 0.1f ? NpcCombatOutcome.Killed
                               :               NpcCombatOutcome.Injured;
                else
                    npcOutcome = NpcCombatOutcome.Injured;

                switch (npcOutcome)
                {
                    case NpcCombatOutcome.Killed:
                        KillNpc(npc, station);
                        outcome.killedNpcUids.Add(npc.uid);
                        break;

                    case NpcCombatOutcome.Captured:
                        CaptureNpc(npc, station, attackerName);
                        outcome.capturedNpcUids.Add(npc.uid);
                        break;

                    default: // Injured
                        npc.injuries++;
                        outcome.injuredNpcUids.Add(npc.uid);
                        ApplyInjuryWound(npc, station);
                        station.LogEvent($"{npc.name} was injured in combat.");
                        break;
                }
            }
        }

        /// <summary>Marks an NPC dead and fires death consequences.</summary>
        private void KillNpc(NPCInstance npc, StationState station)
        {
            npc.statusTags.Add("dead");
            station.RemoveNpc(npc.uid);
            _death?.OnNPCDied(npc, station);
            station.LogEvent($"{npc.name} was killed in combat.");
        }

        /// <summary>
        /// Removes an NPC from the active roster and adds them to the captured pool
        /// with full state retained.
        /// </summary>
        private static void CaptureNpc(NPCInstance npc, StationState station, string capturedBy)
        {
            station.RemoveNpc(npc.uid);
            var record = new CapturedNpcRecord
            {
                capturedAtTick    = station.tick,
                capturedBy        = capturedBy,
                eligibleForRescue = true,
                npc               = npc,
            };
            station.capturedNpcs[npc.uid] = record;
            station.LogEvent($"{npc.name} was captured by {capturedBy}.");
        }

        /// <summary>
        /// Adds a combat wound to an NPC's medical profile via MedicalTickSystem
        /// (if wired).  Falls back to incrementing the legacy injuries counter.
        /// </summary>
        private void ApplyInjuryWound(NPCInstance npc, StationState station)
        {
            if (_medical != null && FeatureFlags.MedicalSystem)
            {
                _medical.EnsureProfile(npc);
                _medical.AddWound(npc, "torso", WoundType.Gunshot, WoundSeverity.Moderate, station.tick);
            }
        }

        // ── Hull damage (ShipToStation / StationToStation) ────────────────────

        /// <summary>
        /// Distributes <paramref name="totalDamage"/> HP across complete foundations
        /// on the station via BuildingSystem.  Returns the total HP actually applied.
        /// Falls back gracefully when BuildingSystem is not wired.
        /// </summary>
        private int ApplyHullDamage(StationState station, int totalDamage)
        {
            if (_building == null || !FeatureFlags.CombatSystem) return 0;

            var candidates = new List<FoundationInstance>();
            foreach (var f in station.foundations.Values)
                if (f.status == "complete") candidates.Add(f);

            if (candidates.Count == 0) return 0;

            // Spread damage across up to three foundations, biased toward the first
            int applied        = 0;
            int foundationsHit = Mathf.Min(3, candidates.Count);

            // Fisher-Yates shuffle for random targeting
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            int remaining = totalDamage;
            for (int i = 0; i < foundationsHit && remaining > 0; i++)
            {
                int share = (i == foundationsHit - 1) ? remaining : remaining / 2;
                _building.DamageFoundation(station, candidates[i].uid, share);
                applied   += share;
                remaining -= share;
            }

            return applied;
        }

        // ── Convenience ───────────────────────────────────────────────────────

        public string SecurityStrengthLabel(StationState station)
        {
            float power = CalculateDefenderPower(station);
            if (power >= 50f) return "formidable";
            if (power >= 25f) return "capable";
            if (power >= 10f) return "limited";
            return "minimal";
        }

        // ── Box-Muller Gaussian sample ────────────────────────────────────────

        private static float SampleGaussian(float mean, float stdDev)
        {
            float u1 = 1f - UnityEngine.Random.value;
            float u2 = 1f - UnityEngine.Random.value;
            float randStdNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) *
                                  Mathf.Sin(2f * Mathf.PI * u2);
            return mean + stdDev * randStdNormal;
        }
    }
}
