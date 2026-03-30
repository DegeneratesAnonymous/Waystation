// NeedSystem — drives Sleep, Hunger, Thirst, Recreation, Social, and Hygiene needs.
//
// Tick rates (0-100 scale, 1 tick = 15 in-game minutes, 96 ticks = 1 in-game day):
//   Sleep depletion (awake):  100/64  = ~1.5625 / tick  → empty in 16h waking
//   Sleep recovery (asleep):  100/32  = ~3.125  / tick  → full in 8h sleeping
//   Hunger depletion:         100/96  = ~1.0417 / tick  → empty in 1 in-game day
//   Thirst depletion:         100/48  = ~2.0833 / tick  → empty in 12h
//   Recreation (work tick):   -1 pt  / tick of work     → 8h work = -32
//   Recreation (rec tick):    +4 pts / tick of rec      → 2h rec = +32 (8:2 restore ratio)
//   Social decay:             100/192 = ~0.5208 / tick  → empty in 2 in-game days
//   Hygiene depletion:        100/192 = ~0.5208 / tick  → empty in 2 in-game days
//
// Mood and Health are read-only aggregates sourced from MoodSystem and
// MedicalTickSystem respectively — they have no independent depletion in NeedSystem.
//
// Backward compatibility: mirrors new float profiles back to the legacy npc.needs dict
// so existing code reading npc.needs["hunger"] etc. continues to work.
//
// Species and trait depletion rate modifiers: each need tick applies a combined
// multiplier from npc.needDepletionRates (species) and trait checks (individual).
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class NeedSystem
    {
        // ── Tick rate constants ────────────────────────────────────────────────
        public const float TicksPerDay             = 96f;

        // Sleep
        public const float SleepDepletionPerTick   = 100f / 64f;   // ~1.5625  per awake tick
        public const float SleepRecoveryPerTick    = 100f / 32f;   // ~3.125   per asleep tick
        public const float SleepSeekThreshold      = 35f;

        // Hunger
        public const float HungerDepletionPerTick  = 100f / 96f;   // ~1.0417  per tick
        public const float HungerSeekThreshold     = 35f;
        public const float HungerMalnourishThr     = 10f;
        public const float MalnourishDebtRequired  = 2f * TicksPerDay; // 48 in-game hours
        public const float MalnourishClearRequired = 3f * TicksPerDay; // 72 in-game hours

        // Thirst
        public const float ThirstDepletionPerTick  = 100f / 48f;   // ~2.0833  per tick
        public const float ThirstSeekThreshold     = 35f;

        // Recreation
        public const float RecWorkCostPerTick      = 1f;
        public const float RecRecoveryPerTick      = 4f;
        public const float RecSeekThreshold        = 35f;
        public const float RecBurnoutClearThreshold = 40f;

        // Social
        public const float SocialDepletionPerTick  = 100f / 192f;  // ~0.5208  per tick
        public const float SocialCompatibleRestoration   = 12f;
        public const float SocialIncompatibleRestoration =  2f;
        public const float SocialNeutralRestoration      =  5f;

        // Hygiene
        public const float HygieneDepletionPerTick = 100f / 192f;  // ~0.5208  per tick → empty in 2 in-game days
        public const float HygieneSeekThreshold    = 35f;
        public const float HygieneCrisisThreshold  = 10f;
        // Mood penalty applied when Hygiene is in crisis
        public const float HygieneCrisisMoodPenalty   = -15f;
        // Social interaction quality penalty applied while in hygiene crisis (multiplier on restoration)
        public const float HygieneCrisisSocialPenalty = 0.5f;
        // Hygiene restoration amounts from facilities
        public const float ShowerRestorationAmount    = 80f;
        public const float SinkRestorationAmount      = 40f;
        // Threshold above which a need counts as "satisfied" for the sanity check
        public const float NeedSatisfactionThreshold  = 50f;

        // ── Dependencies ──────────────────────────────────────────────────────
        private MoodSystem    _mood;
        private SanitySystem  _sanity;
        private TraitSystem   _traits;

        public void SetMoodSystem(MoodSystem m)       => _mood    = m;
        public void SetSanitySystem(SanitySystem s)   => _sanity  = s;
        public void SetTraitSystem(TraitSystem t)      => _traits  = t;

        // ── Main tick ─────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            bool isDayBoundary = station.tick % (int)TicksPerDay == 0;
            int  population    = station.npcs.Count;

            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;

                // NPCs on regular away missions (no fleet ship assigned) skip need processing:
                // their state is abstracted until they return.
                // NPCs on fleet missions (assignedShipUid set) continue to have needs depleted
                // because their full simulation carries through during travel (EXP-003 design constraint).
                bool onFleetMission = npc.missionUid != null && npc.assignedShipUid != null;
                bool onRegularMission = npc.missionUid != null && npc.assignedShipUid == null;
                if (onRegularMission) continue;

                // FEATURE_MEDICAL_SYSTEM: when the medical system is active and the NPC is
                // unconscious, need decay still happens (hunger/thirst/sleep continue depleting)
                // but NPCs cannot actively seek or consume resources.
                // NPCs on fleet missions also have seeking suppressed — station facilities are
                // unavailable during travel, so needs deplete but NPCs cannot claim beds,
                // dispensers, or recreational equipment.
                bool suppressSeeking = (FeatureFlags.MedicalSystem &&
                                        npc.medicalProfile != null && npc.medicalProfile.isUnconscious)
                                       || onFleetMission;

                EnsureProfiles(npc, station);

                TickSleep     (npc, station, suppressSeeking);
                TickHunger    (npc, station, suppressSeeking);
                TickThirst    (npc, station, suppressSeeking);
                TickRecreation(npc);
                TickSocial    (npc, population);
                if (FeatureFlags.HygieneNeed)
                    TickHygiene(npc, station, suppressSeeking);
                ApplyMoodModifiers(npc, station);
                MirrorToLegacyNeeds(npc);
                npc.RecalculateMood();          // keep legacy -1..1 mood float in sync

                if (isDayBoundary)
                    TickDailyChecks(npc, station);
            }
        }

        // ── Sleep ─────────────────────────────────────────────────────────────

        private void TickSleep(NPCInstance npc, StationState station, bool suppressSeeking = false)
        {
            var s = npc.sleepNeed;

            if (npc.isSleeping)
            {
                float rate = SleepRecoveryPerTick;
                if (HasTrait(npc, "trait.quick_sleeper")) rate = 100f / 24f; // 6h
                if (HasTrait(npc, "trait.heavy_sleeper")) rate = 100f / 40f; // 10h

                s.value = Mathf.Min(100f, s.value + rate);
                s.wellRestedTicks++;

                if (s.value >= 100f)
                {
                    npc.isSleeping = false;
                    s.isSeeking    = false;
                    // Apply well-rested bonus when waking fully restored
                    float bonus = HasTrait(npc, "trait.well_rested_expert") ? 15f : 8f;
                    _mood?.PushModifier(npc, "well_rested", bonus,
                                        (int)(4 * TicksPerDay), station.tick, "need_system");
                    _mood?.OnNPCWakes(npc);
                }
                return;
            }

            // Awake — deplete
            float decay = SleepDepletionPerTick;
            if (HasTrait(npc, "trait.fatigued"))            decay *= 1.10f;
            if (HasTrait(npc, "trait.exhausted_disposition")) decay *= 1.20f;
            decay *= GetSpeciesMultiplier(npc, "sleep");

            s.value     = Mathf.Max(0f, s.value - decay);
            s.isSeeking = s.value <= SleepSeekThreshold;
            s.wellRestedTicks = 0;

            if (s.value <= 0f)
                station.LogEvent($"{npc.name} has collapsed from exhaustion.");
            else if (s.isSeeking && !suppressSeeking)
                TryClaimBedAndSleep(npc, station);
        }

        private static void TryClaimBedAndSleep(NPCInstance npc, StationState station)
        {
            // Honour assigned bed first
            if (npc.sleepBedUid != null &&
                station.foundations.TryGetValue(npc.sleepBedUid, out var assigned) &&
                assigned.status == "complete")
            {
                npc.isSleeping = true;
                return;
            }
            // Find any unoccupied, completed bed
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.bed" || f.status != "complete") continue;
                bool taken = false;
                foreach (var other in station.npcs.Values)
                    if (other.uid != npc.uid && other.sleepBedUid == f.uid) { taken = true; break; }
                if (taken) continue;
                npc.sleepBedUid = f.uid;
                npc.isSleeping  = true;
                return;
            }
        }

        // ── Hunger ────────────────────────────────────────────────────────────

        private void TickHunger(NPCInstance npc, StationState station, bool suppressSeeking = false)
        {
            var h = npc.hungerNeed;

            if (npc.isSleeping) return; // sleep priority: NPC does not eat while asleep

            float decay = HungerDepletionPerTick;
            if (HasTrait(npc, "trait.iron_stomach")) decay *= 0.80f;
            if (HasTrait(npc, "trait.hardy"))        decay *= 0.90f;
            if (HasTrait(npc, "trait.hungry"))       decay *= 1.10f; // debuff threshold shifts earlier
            decay *= GetSpeciesMultiplier(npc, "hunger");

            h.value     = Mathf.Max(0f, h.value - decay);
            h.isSeeking = h.value <= HungerSeekThreshold;

            // Attempt food consumption when seeking and able to do so
            if (h.isSeeking && !suppressSeeking && station.GetResource("food") > 0f)
            {
                h.value = Mathf.Min(100f, h.value + 45f); // standard meal: 45%
                station.ModifyResource("food", -1f);
                h.isSeeking = h.value <= HungerSeekThreshold;
                _mood?.PushModifier(npc, "meal_bonus", 3f,
                                    (int)(4 * TicksPerDay), station.tick, "need_system");
            }

            // Malnourishment tracker
            if (h.value < HungerMalnourishThr)
            {
                h.nourishmentDebtTicks++;
                if (!h.isMalnourished && h.nourishmentDebtTicks >= (int)MalnourishDebtRequired)
                {
                    h.isMalnourished = true;
                    station.LogEvent($"{npc.name} is now malnourished.");
                }
            }
            else if (h.isMalnourished && h.value >= 60f)
            {
                h.nourishmentRecoveryTicks++;
                if (h.nourishmentRecoveryTicks >= (int)MalnourishClearRequired)
                {
                    h.isMalnourished           = false;
                    h.nourishmentDebtTicks      = 0;
                    h.nourishmentRecoveryTicks  = 0;
                    station.LogEvent($"{npc.name} has recovered from malnourishment.");
                }
            }
            else
            {
                h.nourishmentRecoveryTicks = 0;
            }
        }

        // ── Thirst ────────────────────────────────────────────────────────────

        private void TickThirst(NPCInstance npc, StationState station, bool suppressSeeking = false)
        {
            var t = npc.thirstNeed;

            if (npc.isSleeping) return;

            float thirstDecay = ThirstDepletionPerTick * GetSpeciesMultiplier(npc, "thirst");
            t.value     = Mathf.Max(0f, t.value - thirstDecay);
            t.isSeeking = t.value <= ThirstSeekThreshold;

            if (t.isSeeking && !suppressSeeking && station.GetResource("water") > 0f)
            {
                t.value = Mathf.Min(100f, t.value + 40f); // basic water: 40%
                station.ModifyResource("water", -0.5f);
                t.isSeeking = t.value <= ThirstSeekThreshold;
            }
        }

        // ── Recreation ────────────────────────────────────────────────────────

        private static void TickRecreation(NPCInstance npc)
        {
            var r = npc.recreationNeed;

            // While working → spend; while idle → recover
            // Apply species modifier to work cost only (how fast this archetype fatigues during work)
            if (!string.IsNullOrEmpty(npc.currentJobId))
                r.value = Mathf.Max(0f, r.value - RecWorkCostPerTick * GetSpeciesMultiplier(npc, "recreation"));
            else
                r.value = Mathf.Min(100f, r.value + RecRecoveryPerTick);

            // Burnout state
            if (!r.isBurntOut && r.value <= 0f)
                r.isBurntOut = true;
            else if (r.isBurntOut && r.value >= RecBurnoutClearThreshold)
                r.isBurntOut = false;
        }

        /// <summary>External call from JobSystem: one tick of active work.</summary>
        public static void RegisterWorkTick(NPCInstance npc)
        {
            if (npc?.recreationNeed == null) return;
            npc.recreationNeed.value = Mathf.Max(0f, npc.recreationNeed.value - RecWorkCostPerTick);
        }

        /// <summary>External call from JobSystem: one tick of active recreation activity.</summary>
        public static void RegisterRecreationTick(NPCInstance npc)
        {
            if (npc?.recreationNeed == null) return;
            npc.recreationNeed.value = Mathf.Min(100f, npc.recreationNeed.value + RecRecoveryPerTick);
        }

        // ── Social ────────────────────────────────────────────────────────────

        private static void TickSocial(NPCInstance npc, int stationPopulation)
        {
            var s = npc.socialNeed;
            if (s.isReclusive) return; // Reclusive need is Solitude — managed externally

            float decay = SocialDepletionPerTick;
            if (HasTrait(npc, "trait.gregarious"))         decay *= 0.70f;
            if (HasTrait(npc, "trait.social_comfortable")) decay *= 0.85f;
            if (HasTrait(npc, "trait.withdrawn"))          decay *= 1.00f; // no change but debuff threshold shifts
            decay *= GetSpeciesMultiplier(npc, "social");

            s.value = Mathf.Max(0f, s.value - decay);

            // Passive recovery from population presence (approximates proximity bonus)
            if (stationPopulation >= 2)
            {
                float recovery = Mathf.Min(1.5f, (stationPopulation - 1) * 0.3f);
                s.value = Mathf.Min(100f, s.value + recovery);
            }
        }

        /// <summary>
        /// Called by ConversationSystem / ProximitySystem when an NPC has a social interaction.
        /// quality: SocialCompatibleRestoration (12), SocialNeutralRestoration (5), or SocialIncompatibleRestoration (2).
        /// </summary>
        public static void RegisterSocialInteraction(NPCInstance npc, float quality)
        {
            if (npc?.socialNeed == null || npc.socialNeed.isReclusive) return;
            // Apply hygiene crisis social penalty: poor hygiene reduces social restoration
            float effectiveQuality = quality;
            if (npc.hygieneNeed != null && npc.hygieneNeed.inCrisis)
                effectiveQuality *= HygieneCrisisSocialPenalty;
            npc.socialNeed.value = Mathf.Min(100f, npc.socialNeed.value + effectiveQuality);
        }

        // ── Hygiene ───────────────────────────────────────────────────────────

        private void TickHygiene(NPCInstance npc, StationState station, bool suppressSeeking = false)
        {
            var h = npc.hygieneNeed;

            if (npc.isSleeping) return;

            float decay = HygieneDepletionPerTick;
            if (HasTrait(npc, "trait.clean"))   decay *= 0.80f; // fastidious cleanliness
            if (HasTrait(npc, "trait.slovenly")) decay *= 1.20f; // hygiene degrades faster
            decay *= GetSpeciesMultiplier(npc, "hygiene");

            h.value     = Mathf.Max(0f, h.value - decay);
            h.isSeeking = h.value <= HygieneSeekThreshold;

            // Crisis state: sustained low hygiene applies mood and social penalties
            bool wasInCrisis = h.inCrisis;
            h.inCrisis = h.value <= HygieneCrisisThreshold;

            if (h.inCrisis && !wasInCrisis)
                station.LogEvent($"{npc.name} is in dire need of a wash.");

            // Seek hygiene facility when below threshold and able
            if (h.isSeeking && !suppressSeeking)
                TryUseHygieneFacility(npc, station);
        }

        private static void TryUseHygieneFacility(NPCInstance npc, StationState station)
        {
            // Prefer showers over sinks: collect the best candidate and use it.
            // Showers give a larger restore (ShowerRestorationAmount); sinks give a smaller one.
            FoundationInstance best = null;
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete") continue;
                if (f.buildableId != "buildable.shower" && f.buildableId != "buildable.sink") continue;

                // Always prefer a shower over a sink
                if (best == null) { best = f; continue; }
                if (f.buildableId == "buildable.shower" && best.buildableId != "buildable.shower")
                    best = f;
            }

            if (best == null) return;

            // Use facility: restore hygiene
            // Shower gives a large hygiene restore; sink gives a smaller partial wash
            float restoration = best.buildableId == "buildable.shower" ? ShowerRestorationAmount : SinkRestorationAmount;
            npc.hygieneNeed.value     = Mathf.Min(100f, npc.hygieneNeed.value + restoration);
            npc.hygieneNeed.isSeeking = npc.hygieneNeed.value <= HygieneSeekThreshold;
            npc.hygieneNeed.inCrisis  = npc.hygieneNeed.value <= HygieneCrisisThreshold;
        }

        // ── Daily boundary checks ─────────────────────────────────────────────

        private void TickDailyChecks(NPCInstance npc, StationState station)
        {
            var h = npc.hungerNeed;
            var t = npc.thirstNeed;
            var s = npc.sleepNeed;

            // Starvation timeline
            if (h.value <= 0f)
            {
                h.starvationDayCount++;
                npc.injuries = Mathf.Min(npc.injuries + 1, 10);
                if      (h.starvationDayCount >= 21) station.LogEvent($"⚠ {npc.name} is dying of starvation.");
                else if (h.starvationDayCount >= 7)  station.LogEvent($"⚠ {npc.name} is starving (day {h.starvationDayCount}).");

                // Sustained starvation (≥ 3 days) registers trait condition pressure:
                // repeated pressure can eventually trigger a desperation or resourcefulness trait.
                if (h.starvationDayCount >= 3)
                    _traits?.RegisterConditionPressure(npc, TraitConditionCategory.SustainedStarvation, 2f);
            }
            else if (h.starvationDayCount > 0)
            {
                h.starvationDayCount = 0;
            }

            // Dehydration timeline
            if (t.value <= 0f)
            {
                t.dehydrationDayCount++;
                npc.injuries = Mathf.Min(npc.injuries + 1, 10);
                if (t.dehydrationDayCount >= 3) station.LogEvent($"⚠ {npc.name} has died of dehydration.");
                else                            station.LogEvent($"⚠ {npc.name} is dehydrated (day {t.dehydrationDayCount}).");
            }
            else if (t.dehydrationDayCount > 0)
            {
                t.dehydrationDayCount = 0;
            }

            // Sanity pipeline
            if (_sanity != null)
            {
                bool anyDepleted = h.value <= 0f || t.value <= 0f || s.value <= 0f ||
                                   (FeatureFlags.HygieneNeed && npc.hygieneNeed != null && npc.hygieneNeed.value <= 0f);
                if (anyDepleted)
                    _sanity.RegisterNeedDepleted(npc, station);

                int above50 = 0;
                if (h.value > NeedSatisfactionThreshold) above50++;
                if (t.value > NeedSatisfactionThreshold) above50++;
                if (s.value > NeedSatisfactionThreshold) above50++;
                if (FeatureFlags.HygieneNeed && npc.hygieneNeed != null && npc.hygieneNeed.value > NeedSatisfactionThreshold) above50++;
                if (above50 > 0)
                    _sanity.RegisterNeedsSatisfied(npc, above50, station);
            }

            // Lineage pressure
            if (_traits != null)
            {
                if (h.value < 30f) _traits.ApplyLineageEvent(npc, "lineage.hunger", -1, station);
                else if (h.value > 70f) _traits.ApplyLineageEvent(npc, "lineage.hunger", +1, station);

                if (s.value < 20f) _traits.ApplyLineageEvent(npc, "lineage.rest", -1, station);
                else if (s.value > 70f) _traits.ApplyLineageEvent(npc, "lineage.rest", +1, station);

                if (npc.recreationNeed.isBurntOut)
                    _traits.ApplyLineageEvent(npc, "lineage.focus", -1, station);
                else if (npc.recreationNeed.value > 70f)
                    _traits.ApplyLineageEvent(npc, "lineage.focus", +1, station);

                if (npc.socialNeed.value < 20f)
                    _traits.ApplyLineageEvent(npc, "lineage.social_comfort", -1, station);
                else if (npc.socialNeed.value > 70f)
                    _traits.ApplyLineageEvent(npc, "lineage.social_comfort", +1, station);
            }
        }

        // ── Mood modifiers from need debuffs ──────────────────────────────────
        // We use a single aggregate "needs_penalty" modifier that is replaced each tick.

        private void ApplyMoodModifiers(NPCInstance npc, StationState station)
        {
            float penalty = CalculateMoodPenalty(npc);
            ReplaceModifier(npc, "needs_penalty", penalty, station);
        }

        private static float CalculateMoodPenalty(NPCInstance npc)
        {
            float p = 0f;

            float sleep = npc.sleepNeed.value;
            if      (sleep <= 5f)  p -= 40f;
            else if (sleep <= 10f) p -= 30f;
            else if (sleep <= 15f) p -= 20f;
            else if (sleep <= 20f) p -= 10f;
            else if (sleep <= 25f) p -=  5f;

            float hunger = npc.hungerNeed.value;
            if      (hunger <= 0f)  p -= 30f;
            else if (hunger <= 10f) p -= 20f;
            else if (hunger <= 20f) p -= 10f;
            else if (hunger <= 25f) p -=  5f;
            if (npc.hungerNeed.isMalnourished) p -= 10f;

            float thirst = npc.thirstNeed.value;
            if      (thirst <= 0f)  p -= 30f;
            else if (thirst <= 10f) p -= 20f;
            else if (thirst <= 20f) p -= 10f;
            else if (thirst <= 25f) p -=  5f;

            if (npc.recreationNeed.isBurntOut)       p -= 25f;
            else if (npc.recreationNeed.value <= 10f) p -= 15f;
            else if (npc.recreationNeed.value <= 20f) p -=  5f;

            float social = npc.socialNeed.value;
            if      (social <= 0f)  p -= 30f; // should never reach here normally
            else if (social <= 10f) p -= 20f;
            else if (social <= 20f) p -= 10f;
            else if (social <= 30f) p -=  5f; // debuff starts at 30%

            if (FeatureFlags.HygieneNeed && npc.hygieneNeed != null)
            {
                float hygiene = npc.hygieneNeed.value;
                if      (hygiene <= HygieneCrisisThreshold)  p += HygieneCrisisMoodPenalty; // -15
                else if (hygiene <= 20f)                     p -=  8f;
                else if (hygiene <= HygieneSeekThreshold)    p -=  4f;
            }

            return p;
        }

        private void ReplaceModifier(NPCInstance npc, string eventId, float delta, StationState station)
        {
            if (npc.moodModifiers == null) npc.moodModifiers = new List<MoodModifierRecord>();

            // Remove existing entry and reverse its delta
            for (int i = npc.moodModifiers.Count - 1; i >= 0; i--)
            {
                var m = npc.moodModifiers[i];
                if (m.eventId == eventId && m.source == "need_system")
                {
                    npc.moodScore -= m.delta;
                    npc.moodModifiers.RemoveAt(i);
                }
            }

            // Push new value if non-zero
            if (!Mathf.Approximately(delta, 0f))
                _mood?.PushModifier(npc, eventId, delta, -1, station.tick, "need_system");
        }

        // ── Work speed / skill check query helpers ────────────────────────────

        /// <summary>Returns a combined work speed multiplier from need states (floored at 0.35).</summary>
        public static float GetWorkSpeedModifier(NPCInstance npc)
        {
            if (npc?.sleepNeed == null) return 1f;
            float mod = 1f;
            float sleep = npc.sleepNeed.value;
            if      (sleep <= 10f) mod *= 0.70f;
            else if (sleep <= 15f) mod *= 0.80f;
            else if (sleep <= 20f) mod *= 0.90f;
            if (npc.recreationNeed.isBurntOut)        mod *= 0.65f;
            else if (npc.recreationNeed.value <= 10f)  mod *= 0.80f;
            if (npc.hungerNeed.isMalnourished)         mod *= 0.90f;
            if (npc.thirstNeed.value <= 20f)           mod *= 0.80f;
            if (npc.socialNeed.value <= 10f)           mod *= 0.90f;
            if (FeatureFlags.HygieneNeed && npc.hygieneNeed != null && npc.hygieneNeed.inCrisis)
                mod *= 0.85f;
            return Mathf.Max(0.35f, mod);
        }

        /// <summary>Returns the combined skill check modifier from need states (-4 to 0).</summary>
        public static int GetSkillCheckModifier(NPCInstance npc)
        {
            if (npc?.sleepNeed == null) return 0;
            int mod = 0;
            if      (npc.sleepNeed.value <= 15f) mod -= 2;
            else if (npc.sleepNeed.value <= 20f) mod -= 1;
            if (npc.thirstNeed.value <= 20f)     mod -= 1;
            if (npc.hungerNeed.isMalnourished)   mod -= 1;
            if (FeatureFlags.HygieneNeed && npc.hygieneNeed != null && npc.hygieneNeed.inCrisis) mod -= 1;
            return Mathf.Max(-4, mod);
        }

        // ── Legacy dict mirror ────────────────────────────────────────────────

        private static void MirrorToLegacyNeeds(NPCInstance npc)
        {
            npc.needs["sleep"]   = npc.sleepNeed.value   / 100f;
            npc.needs["hunger"]  = npc.hungerNeed.value  / 100f;
            npc.needs["rest"]    = npc.sleepNeed.value   / 100f;   // rest = sleep alias
            npc.needs["social"]  = npc.socialNeed.value  / 100f;
            if (FeatureFlags.HygieneNeed && npc.hygieneNeed != null)
                npc.needs["hygiene"] = npc.hygieneNeed.value / 100f;
            if (!npc.needs.ContainsKey("safety")) npc.needs["safety"] = 1f;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the species depletion rate multiplier for the given need.
        /// Reads from npc.needDepletionRates (populated at spawn from NPCTemplate).
        /// Returns 1.0 if no species modifier is defined.
        /// </summary>
        private static float GetSpeciesMultiplier(NPCInstance npc, string needId)
        {
            if (npc.needDepletionRates == null) return 1f;
            return npc.needDepletionRates.TryGetValue(needId, out float m) ? m : 1f;
        }

        private static bool HasTrait(NPCInstance npc, string traitId)
        {
            if (npc.traitProfile == null) return false;
            foreach (var t in npc.traitProfile.traits)
                if (t.traitId == traitId) return true;
            return false;
        }

        private static void EnsureProfiles(NPCInstance npc, StationState station)
        {
            if (npc.sleepNeed      == null) npc.sleepNeed      = new SleepNeedProfile();
            if (npc.hungerNeed     == null) npc.hungerNeed     = new HungerNeedProfile();
            if (npc.thirstNeed     == null) npc.thirstNeed     = new ThirstNeedProfile();
            if (npc.recreationNeed == null) npc.recreationNeed = new RecreationNeedProfile();
            if (npc.socialNeed     == null) npc.socialNeed     = new SocialNeedProfile();
            if (FeatureFlags.HygieneNeed && npc.hygieneNeed == null) npc.hygieneNeed = new HygieneNeedProfile();

            // One-time migration: seed new profiles from legacy needs dict values
            if (npc.sleepNeed.value >= 100f && npc.needs.TryGetValue("sleep", out var ls) && ls < 1f)
                npc.sleepNeed.value = ls * 100f;
            if (npc.hungerNeed.value >= 100f && npc.needs.TryGetValue("hunger", out var lh) && lh < 1f)
                npc.hungerNeed.value = lh * 100f;
            if (npc.socialNeed.value >= 100f && npc.needs.TryGetValue("social", out var lso) && lso > 0f)
                npc.socialNeed.value = lso * 100f;

            // Initialise sanity profile if missing
            npc.GetOrCreateSanity();
        }
    }
}
