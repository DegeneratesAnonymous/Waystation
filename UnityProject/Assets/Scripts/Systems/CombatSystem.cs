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
using System.Collections.Generic;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Systems
{
    public class CombatOutcome
    {
        public string tier;         // repelled_clean / repelled_damaged / partial_defeat / overrun
        public string narrative;
        public float  creditsLost  = 0f;
        public float  partsLost    = 0f;
        public float  foodLost     = 0f;
        public float  moduleDamage = 0f;   // fractional damage applied to a random dock module
        public float  crewTrauma   = 0f;   // safety need reduction applied to all crew
        public int    crewInjuries = 0;    // number of crew injury points inflicted
    }

    public class CombatSystem
    {
        // Gaussian jitter standard deviation
        public const float CombatVariance             = 8f;
        public const int   SecurityDefaultSkill       = 2;
        public const float SecurityPowerMultiplier    = 2.5f;
        public const int   NonSecurityDefaultSkill    = 1;
        public const float NonSecurityPowerMultiplier = 1f;
        public const float GuardPostBonus             = 10f;
        public const float SecurityModuleBonus        = 5f;

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

        // ── Main resolution ───────────────────────────────────────────────────

        public CombatOutcome ResolveBoardingAttempt(StationState station, ShipInstance ship)
        {
            float defenderPower = CalculateDefenderPower(station);
            float attackerPower = ship.threatLevel * 10f;

            // Gaussian jitter
            float jitter = SampleGaussian(0f, CombatVariance);
            float net    = defenderPower - attackerPower + jitter;

            string tier    = NetToTier(net);
            var    outcome = BuildOutcome(tier, ship, station);
            ApplyOutcome(outcome, station);

            Debug.Log($"[CombatSystem] ship={ship.name} threat={ship.threatLevel} " +
                      $"def={defenderPower:F1} net={net:F1} tier={tier}");
            return outcome;
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

        private static void ApplyOutcome(CombatOutcome outcome, StationState station)
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

            // Crew trauma + injuries
            var crew = station.GetCrew();
            foreach (var npc in crew)
            {
                if (outcome.crewTrauma > 0f)
                {
                    npc.UpdateNeeds(new Dictionary<string, float> { { "safety", -outcome.crewTrauma } });
                    npc.RecalculateMood();
                }
            }
            if (outcome.crewInjuries > 0 && crew.Count > 0)
            {
                int toInjure = Mathf.Min(outcome.crewInjuries, crew.Count);
                // Fisher-Yates shuffle to pick random victims
                for (int i = crew.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    (crew[i], crew[j]) = (crew[j], crew[i]);
                }
                for (int i = 0; i < toInjure; i++)
                {
                    crew[i].injuries++;
                    station.LogEvent($"{crew[i].name} was injured in the boarding action.");
                }
            }
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
