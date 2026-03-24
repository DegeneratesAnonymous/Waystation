// MedicalTickSystem.cs — Main per-tick processor for all NPC MedicalProfiles.
//
// Each tick this system:
//   1. Advances bleed rates → reduces blood volume
//   2. Accumulates infection chance on untreated wounds
//   3. Rolls infection checks every 12 ticks per wound
//   4. Advances disease stage progression and regression
//   5. Advances wound healing progress on treated wounds
//   6. Derives pain value from all active wounds
//   7. Derives consciousness from pain, blood volume, brain health, disease
//   8. Applies blood volume natural recovery rate
//   9. Writes pain and consciousness back to MedicalProfile
//  10. Checks vital part death rules
//  11. Applies functional penalties from part damage
//  12. Evaluates scar chance when wounds fully heal
//  13. Routes mood modifiers (pain, blood loss, disease)
//  14. Routes sanity/trait lineage events (scars)
//  15. Suppresses need-seeking when unconscious (via MedicalProfile.isUnconscious as checked by NeedSystem)
//
// Sub-systems (BleedingSystem, PainSystem, ConsciousnessSystem) are encapsulated
// as private static helpers to keep this file self-contained but named appropriately
// for spec traceability.
//
// Feature gate: FEATURE_MEDICAL_SYSTEM (FeatureFlags.MedicalSystem)
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class MedicalTickSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>Ticks between infection roll attempts on untreated wounds.</summary>
        public const int InfectionRollInterval   = 12;

        /// <summary>Base infection accumulation per tick for untreated wounds.</summary>
        public const float BaseInfectionPerTick  = 0.5f;

        /// <summary>Half rate for treated (bandaged) wounds.</summary>
        public const float TreatedInfectionPerTick = 0.25f;

        /// <summary>Infection roll threshold: if accumulation ≥ this, a d20 roll is made.</summary>
        public const float InfectionRollThreshold = 6f;

        /// <summary>
        /// Base infection chance on the roll (out of 20).
        /// A roll ≤ this value triggers infection.
        /// </summary>
        public const int   BaseInfectionDC = 5;

        /// <summary>Base healing progress per tick for treated wounds.</summary>
        /// <remarks>At 0.005/tick × 96 ticks/day ≈ 0.48/day; full heal in ~2 in-game days (192 ticks).</remarks>
        public const float TreatedHealingPerTick   = 0.005f;

        /// <summary>Untreated wounds do not heal.</summary>
        public const float UntreatedHealingPerTick = 0f;

        /// <summary>
        /// Pain above this % adds a mood modifier.
        /// Pain tiers: Low=20–40%, Medium=40–60%, High=60–80%, Severe=80%+.
        /// </summary>
        public const float PainMoodThreshold = 20f;

        /// <summary>Blood volume below this % adds a mood modifier.</summary>
        public const float LowBloodMoodThreshold = 60f;

        // ── Cached part index ──────────────────────────────────────────────────

        private Dictionary<string, BodyPartDefinition> _humanPartIndex;
        private Dictionary<WoundType, WoundTypeDefinition> _woundTypes;
        private DiseaseDefinition _woundInfectionDef;

        // ── Dependencies ──────────────────────────────────────────────────────

        private MoodSystem   _mood;
        private SanitySystem _sanity;
        private TraitSystem  _traits;

        public void SetMoodSystem(MoodSystem m)     => _mood   = m;
        public void SetSanitySystem(SanitySystem s) => _sanity = s;
        public void SetTraitSystem(TraitSystem t)   => _traits = t;

        // ── Initialisation ────────────────────────────────────────────────────

        public void Initialise()
        {
            _humanPartIndex  = HumanBodyTree.Get().BuildIndex();
            _woundTypes      = HumanBodyTree.GetWoundTypes();
            _woundInfectionDef = DiseaseDefinition.WoundInfection();
        }

        /// <summary>
        /// Initialises a MedicalProfile on an NPC if one doesn't exist.
        /// Uses the species's body part tree (currently only Human is supported).
        /// </summary>
        public void EnsureProfile(NPCInstance npc)
        {
            if (npc.medicalProfile != null) return;
            var tree = GetTreeForSpecies(npc.species);
            npc.medicalProfile = MedicalProfile.Create(tree);
        }

        private BodyPartTreeDefinition GetTreeForSpecies(string species)
        {
            // TODO: expand for non-human species
            return HumanBodyTree.Get();
        }

        // ── Main tick ─────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (!FeatureFlags.MedicalSystem) return;
            if (station == null) return;

            foreach (var npc in station.npcs.Values)
            {
                if (npc.medicalProfile == null) continue;
                if (npc.statusTags.Contains("dead")) continue;
                TickNPC(npc, station);
            }
        }

        private void TickNPC(NPCInstance npc, StationState station)
        {
            var profile = npc.medicalProfile;

            // 1. BleedingSystem — advance bleed, apply to blood volume
            BleedingSystem.Tick(profile);

            // Blood volume 0% → immediate death from blood loss
            if (profile.bloodVolume <= 0f)
            {
                KillNPC(npc, station, "blood loss");
                return;
            }

            // 2–3. Infection accumulation and roll checks
            TickInfection(npc, profile, station);

            // 4. Disease progression (uses antibiotic state)
            TickDiseases(npc, profile, station);

            // 5. Wound healing
            TickHealing(npc, profile, station);

            // 5a. Analgesic duration countdown
            if (profile.analgesicDurationTicks > 0)
            {
                profile.analgesicDurationTicks--;
                if (profile.analgesicDurationTicks <= 0)
                    profile.analgesicStrength = 0f;
            }

            // 5b. Antibiotic duration countdown
            if (profile.antibioticsDurationTicks > 0)
            {
                profile.antibioticsDurationTicks--;
                if (profile.antibioticsDurationTicks <= 0)
                    profile.antibioticsStrength = 0f;
            }

            // 6. PainSystem — derive pain from wounds (applies analgesic suppression)
            PainSystem.Derive(npc, profile, _woundTypes, _woundInfectionDef);

            // 7. ConsciousnessSystem — derive consciousness
            ConsciousnessSystem.Derive(profile);

            // 8. Blood volume natural recovery
            if (profile.bloodVolume < 100f)
            {
                // Well-fed bonus (read from NeedsSystem)
                float recoveryBonus = 1f;
                if (npc.hungerNeed != null && npc.hungerNeed.value >= 70f) recoveryBonus += 0.5f;
                if (npc.sleepNeed  != null && npc.sleepNeed.wellRestedTicks > 0) recoveryBonus += 0.25f;
                profile.bloodVolume = Mathf.Min(100f,
                    profile.bloodVolume + MedicalProfile.BloodRecoveryPerTick * recoveryBonus);
            }

            // 9–10. Vital part death checks
            CheckVitalParts(npc, profile, station);
            TickPairedOrganTimers(npc, profile, station);

            // Early-return if NPC died during vital checks so we don't apply
            // mood/penalty/consciousness side-effects to an already-dead NPC.
            if (npc.statusTags.Contains("dead")) return;

            // 11. Functional penalties
            ApplyFunctionalPenalties(npc, profile);

            // 12. Mood modifiers (pain and blood volume)
            ApplyMoodModifiers(npc, profile, station);

            // 13. Consciousness state update — suppress need-seeking if unconscious
            bool wasUnconscious = profile.isUnconscious;
            profile.isUnconscious = profile.consciousness <= 0f;
            if (!wasUnconscious && profile.isUnconscious)
                station?.LogEvent($"{npc.name} has lost consciousness.");
            else if (wasUnconscious && !profile.isUnconscious)
                station?.LogEvent($"{npc.name} has regained consciousness.");
        }

        // ── BleedingSystem ────────────────────────────────────────────────────

        private static class BleedingSystem
        {
            public static void Tick(MedicalProfile profile)
            {
                if (profile.bloodVolume <= 0f) return;

                float totalBleed = 0f;
                foreach (var part in profile.parts.Values)
                    foreach (var w in part.wounds)
                        totalBleed += w.bleedRatePerTick;

                profile.bloodVolume = Mathf.Max(0f, profile.bloodVolume - totalBleed);
            }
        }

        // ── PainSystem ────────────────────────────────────────────────────────

        private static class PainSystem
        {
            public static void Derive(NPCInstance npc, MedicalProfile profile,
                                      Dictionary<WoundType, WoundTypeDefinition> woundTypes,
                                      DiseaseDefinition woundInfectionDef)
            {
                float rawPain = 0f;
                foreach (var part in profile.parts.Values)
                    foreach (var w in part.wounds)
                        rawPain += w.painContribution;

                // Include disease pain (uses pre-cached definition — no allocation per call)
                foreach (var part in profile.parts.Values)
                    foreach (var d in part.diseases)
                        rawPain += GetDiseasePain(d, woundInfectionDef);

                // END modifier reduces effective pain (Fortitude/Endurance)
                int endMod = npc.abilityScores.ENDMod; // -2 to +3
                float endReduction = Mathf.Clamp(endMod * 0.05f, -0.10f, 0.15f);
                rawPain *= Mathf.Max(0.1f, 1f - endReduction);

                // Analgesic suppression (from AdministerPainkiller)
                if (profile.analgesicDurationTicks > 0 && profile.analgesicStrength > 0f)
                    rawPain *= (1f - profile.analgesicStrength);

                // Fortitude trait: TODO — check for "trait.fortitude" and reduce by 0.10
                profile.pain = Mathf.Clamp(rawPain, 0f, 100f);
            }

            private static float GetDiseasePain(ActiveDisease d, DiseaseDefinition woundInfectionDef)
            {
                if (d.diseaseId == woundInfectionDef.diseaseId &&
                    d.currentStage < woundInfectionDef.stages.Count)
                    return woundInfectionDef.stages[d.currentStage].painPerTick;
                return 0f;
            }
        }

        // ── ConsciousnessSystem ───────────────────────────────────────────────

        private static class ConsciousnessSystem
        {
            public static void Derive(MedicalProfile profile)
            {
                // Consciousness = 100 - (pain_weight + blood_weight + brain_weight + disease_weight)
                // Pain contributes up to 50% (0–100 pain → 0–50 consciousness loss)
                float painLoss = profile.pain * 0.5f;

                // Blood volume contributes up to 40% (below 60% starts reducing consciousness)
                float bloodLoss = 0f;
                if (profile.bloodVolume < 60f)
                    bloodLoss = (60f - profile.bloodVolume) / 60f * 40f;

                // Brain health contributes up to 40%
                float brainHealth = 100f;
                if (profile.parts.TryGetValue("brain", out var brain))
                    brainHealth = brain.health;
                float brainLoss = (100f - brainHealth) * 0.4f;

                // Disease contributions (up to 20%)
                float diseaseLoss = 0f;
                foreach (var part in profile.parts.Values)
                    foreach (var d in part.diseases)
                        diseaseLoss += GetDiseaseConsciousnessLoss(d);
                diseaseLoss = Mathf.Min(diseaseLoss, 20f);

                profile.consciousness = Mathf.Clamp(
                    100f - painLoss - bloodLoss - brainLoss - diseaseLoss, 0f, 100f);
            }

            private static float GetDiseaseConsciousnessLoss(ActiveDisease d)
            {
                switch (d.diseaseId)
                {
                    case "disease.wound_infection":
                        return d.currentStage * 2f; // each stage adds 2% consciousness loss
                }
                return 0f;
            }
        }

        // ── Infection ─────────────────────────────────────────────────────────

        private void TickInfection(NPCInstance npc, MedicalProfile profile, StationState station)
        {
            foreach (var part in profile.parts.Values)
            {
                for (int i = 0; i < part.wounds.Count; i++)
                {
                    var w = part.wounds[i];
                    if (w.isInfected || w.healingProgress >= 1f) continue;

                    // Accumulate infection chance
                    float rate = w.isTreated ? TreatedInfectionPerTick : BaseInfectionPerTick;
                    if (_woundTypes.TryGetValue(w.type, out var wtd))
                        rate += wtd.infectionChanceModifier * 0.1f;
                    w.infectionAccumulation += rate;

                    // Roll every InfectionRollInterval ticks
                    if (station.tick % InfectionRollInterval == 0 &&
                        w.infectionAccumulation >= InfectionRollThreshold)
                    {
                        int dc = BaseInfectionDC;
                        // Better treatment quality → harder to infect
                        if (w.isTreated) dc = Mathf.Max(1, dc - Mathf.RoundToInt(w.treatmentQuality * 3));

                        int roll = UnityEngine.Random.Range(1, 21);
                        if (roll <= dc)
                        {
                            TriggerInfection(npc, part, w, station);
                        }
                        w.infectionAccumulation = 0f;
                    }
                }
            }
        }

        private void TriggerInfection(NPCInstance npc, BodyPart part, Wound wound, StationState station)
        {
            wound.isInfected = true;
            var disease = ActiveDisease.Create(_woundInfectionDef, station.tick);
            disease.affectedPartIds.Clear();
            disease.affectedPartIds.Add(part.partId);
            part.diseases.Add(disease);
            station?.LogEvent($"{npc.name}: wound on {part.partId} has become infected.");
        }

        // ── Disease progression ────────────────────────────────────────────────

        private void TickDiseases(NPCInstance npc, MedicalProfile profile, StationState station)
        {
            bool antibioticsActive = profile.antibioticsDurationTicks > 0 && profile.antibioticsStrength > 0f;

            foreach (var part in profile.parts.Values)
            {
                for (int i = part.diseases.Count - 1; i >= 0; i--)
                {
                    var disease = part.diseases[i];
                    var def     = _woundInfectionDef; // Only wound_infection defined for now

                    if (disease.diseaseId != def.diseaseId) continue;
                    if (def.stages.Count == 0) continue;

                    // Antibiotics slow progression: strong antibiotics can reverse stage advancement.
                    // Each tick, roll to determine whether the disease advances or regresses.
                    if (antibioticsActive)
                    {
                        // Probability that antibiotics suppress a stage tick this cycle
                        float suppressChance = profile.antibioticsStrength;
                        if (UnityEngine.Random.value < suppressChance)
                        {
                            // Antibiotic winning this tick: regress one stage if not already at stage 0.
                            // Reset ticksInStage so the patient spends a full stage duration at the
                            // regressed stage before further progression can occur.
                            if (disease.currentStage > 0)
                            {
                                disease.currentStage--;
                                disease.ticksInStage = 0;
                                station?.LogEvent(
                                    $"{npc.name}: {def.displayName} regressing to {def.stages[disease.currentStage].stageName} (antibiotics).");
                            }
                            // Skip normal disease progression for this tick
                            continue;
                        }
                    }

                    disease.ticksInStage++;

                    int stageDuration = def.stages[disease.currentStage].durationTicks;
                    if (disease.ticksInStage >= stageDuration)
                    {
                        disease.ticksInStage = 0;
                        int nextStage = disease.currentStage + 1;

                        if (nextStage >= def.stages.Count)
                        {
                            if (def.isChronic)
                            {
                                disease.currentStage = 0; // regress to start
                            }
                            else
                            {
                                // Disease cleared
                                part.diseases.RemoveAt(i);
                                station?.LogEvent($"{npc.name}: {def.displayName} on {part.partId} has cleared.");
                            }
                        }
                        else
                        {
                            disease.currentStage = nextStage;
                            station?.LogEvent(
                                $"{npc.name}: {def.displayName} on {part.partId} → {def.stages[nextStage].stageName}.");
                        }
                    }

                    // Apply blood drain from disease.
                    // Guard: if the disease was just removed above (non-chronic, last stage),
                    // i is no longer < part.diseases.Count so the drain is skipped correctly.
                    if (i >= 0 && i < part.diseases.Count && disease.currentStage < def.stages.Count)
                    {
                        float drain = def.stages[disease.currentStage].bloodDrainPerTick;
                        profile.bloodVolume = Mathf.Max(0f, profile.bloodVolume - drain);
                    }
                }
            }
        }

        // ── Wound healing ──────────────────────────────────────────────────────

        private void TickHealing(NPCInstance npc, MedicalProfile profile, StationState station)
        {
            foreach (var kv in profile.parts)
            {
                var part = kv.Value;
                _humanPartIndex.TryGetValue(kv.Key, out var partDef);

                for (int i = part.wounds.Count - 1; i >= 0; i--)
                {
                    var w = part.wounds[i];

                    float healRate = w.isTreated ? TreatedHealingPerTick : UntreatedHealingPerTick;

                    // Per-wound healing rate multiplier (e.g., fracture set by SetFracture)
                    healRate *= w.healingRateMultiplier;

                    // Well-fed + well-rested bonus
                    if (npc.hungerNeed != null && npc.hungerNeed.value >= 70f) healRate *= 1.25f;
                    if (npc.sleepNeed  != null && npc.sleepNeed.wellRestedTicks > 0) healRate *= 1.15f;

                    w.healingProgress = Mathf.Min(1f, w.healingProgress + healRate);

                    if (w.healingProgress >= 1f)
                    {
                        // Wound fully healed — evaluate scar chance
                        EvaluateScarChance(npc, part, w, partDef, station);
                        part.wounds.RemoveAt(i);
                    }
                }
            }
        }

        // ── Scar chance evaluation ────────────────────────────────────────────

        private void EvaluateScarChance(NPCInstance npc, BodyPart part, Wound wound,
                                        BodyPartDefinition partDef, StationState station)
        {
            if (!_woundTypes.TryGetValue(wound.type, out var wtd)) return;

            // scarChance = baseScarChance × (2 - treatmentQuality) × (1 / (1 + surgeonSkill × 0.05))
            // With no surgeon skill involved, just use treatmentQuality as the modifier.
            float qualityModifier = Mathf.Lerp(2f, 1f, wound.treatmentQuality);
            float scarChance      = wtd.baseScarChance * qualityModifier;

            if (UnityEngine.Random.value > scarChance) return;

            // Create and apply scar
            string coverageTag = partDef?.coverageTag ?? "unknown_region";
            var scar = Scar.Create(wound.type, wound.severity, part.partId, coverageTag);
            part.scars.Add(scar);
            station?.LogEvent($"{npc.name} has developed a scar on {part.partId}.");

            // Apply permanent functional penalty from scar
            part.health = Mathf.Max(0f, part.health - scar.functionalPenalty * 100f);
            ApplyScarPenaltyToProfile(npc.medicalProfile, scar);

            // Scar lineage pressure routing
            RouteScarLineagePressure(npc, npc.medicalProfile, partDef, station);
        }

        private static void ApplyScarPenaltyToProfile(MedicalProfile profile, Scar scar)
        {
            if (profile == null) return;
            profile.penalties.scarPenaltyAccumulator += scar.functionalPenalty;
        }

        private void RouteScarLineagePressure(NPCInstance npc, MedicalProfile profile,
                                              BodyPartDefinition partDef, StationState station)
        {
            if (_traits == null || profile == null || partDef == null) return;

            string region = partDef.coverageTag;

            // 3+ scars on same region → Resilience positive (or Fear negative if sanity < 0)
            int regionScarCount = profile.ScarCountInRegion(region, _humanPartIndex);
            if (regionScarCount >= 3)
            {
                var sanity = npc.sanity;
                bool negativeSanity = sanity != null && sanity.score < 0;
                if (negativeSanity)
                    _traits.ApplyLineageEvent(npc, "lineage.fear",       -1, station);
                else
                    _traits.ApplyLineageEvent(npc, "lineage.resilience", +1, station);
            }

            // 3+ facial scars → Withdrawn lineage pressure
            if (profile.FacialScarCount(_humanPartIndex) >= 3)
                _traits.ApplyLineageEvent(npc, "lineage.withdrawn", -1, station);
        }

        // ── Vital part death checks ────────────────────────────────────────────

        private void CheckVitalParts(NPCInstance npc, MedicalProfile profile, StationState station)
        {
            foreach (var kv in profile.parts)
            {
                var part = kv.Value;
                if (part.health > 0f || part.isAmputated) continue;

                if (!_humanPartIndex.TryGetValue(kv.Key, out var def)) continue;

                switch (def.vitalRule)
                {
                    case VitalRule.InstantDeath:
                        KillNPC(npc, station, $"{def.displayName} destroyed");
                        return;

                    case VitalRule.PairedOrgan5Ticks:
                        HandlePairedOrganDeath(npc, profile, def, 5, station);
                        break;

                    case VitalRule.PairedOrgan192Ticks:
                        HandlePairedOrganDeath(npc, profile, def, 192, station);
                        break;
                }
            }
        }

        private void HandlePairedOrganDeath(NPCInstance npc, MedicalProfile profile,
                                            BodyPartDefinition def, int deathTicks,
                                            StationState station)
        {
            if (string.IsNullOrEmpty(def.pairedPartId)) return;
            if (!profile.TryGetPart(def.pairedPartId, out var paired)) return;
            if (paired.health > 0f) return; // partner still alive

            // Both destroyed — start/continue death timer
            bool isLung   = def.partId == "left_lung" || def.partId == "right_lung";
            bool isKidney = def.partId == "left_kidney" || def.partId == "right_kidney";

            if (isLung)
            {
                if (profile.lungDeathTicksRemaining <= 0)
                    profile.lungDeathTicksRemaining = deathTicks;
            }
            else if (isKidney)
            {
                if (profile.kidneyDeathTicksRemaining <= 0)
                    profile.kidneyDeathTicksRemaining = deathTicks;
            }
        }

        private void TickPairedOrganTimers(NPCInstance npc, MedicalProfile profile, StationState station)
        {
            if (profile.lungDeathTicksRemaining > 0)
            {
                profile.lungDeathTicksRemaining--;
                if (profile.lungDeathTicksRemaining <= 0)
                    KillNPC(npc, station, "both lungs destroyed");
            }

            if (profile.kidneyDeathTicksRemaining > 0)
            {
                profile.kidneyDeathTicksRemaining--;
                if (profile.kidneyDeathTicksRemaining <= 0)
                    KillNPC(npc, station, "both kidneys destroyed");
            }
        }

        private static void KillNPC(NPCInstance npc, StationState station, string cause)
        {
            npc.statusTags.Remove("crew");
            npc.statusTags.Remove("visitor");
            if (!npc.statusTags.Contains("dead"))
            {
                npc.statusTags.Add("dead");
                station?.LogEvent($"{npc.name} has died ({cause}).");
            }
        }

        // ── Functional penalties ──────────────────────────────────────────────

        private void ApplyFunctionalPenalties(NPCInstance npc, MedicalProfile profile)
        {
            profile.penalties.Reset();

            foreach (var kv in profile.parts)
            {
                if (!_humanPartIndex.TryGetValue(kv.Key, out var def)) continue;
                var part   = kv.Value;
                float loss = (100f - part.health) / 100f;   // 0 = healthy, 1 = destroyed
                if (loss <= 0f || part.isAmputated) continue;

                float penalty = loss * def.healthWeight * 0.1f; // scaled contribution

                foreach (var tag in def.functionTags)
                {
                    switch (tag)
                    {
                        case "locomotion":   profile.penalties.locomotionModifier    = Mathf.Max(0f, profile.penalties.locomotionModifier    - penalty); break;
                        case "manipulation": profile.penalties.manipulationModifier  = Mathf.Max(0f, profile.penalties.manipulationModifier  - penalty); break;
                        case "sight":        profile.penalties.sightModifier         = Mathf.Max(0f, profile.penalties.sightModifier         - penalty); break;
                        case "hearing":      profile.penalties.hearingModifier       = Mathf.Max(0f, profile.penalties.hearingModifier       - penalty); break;
                        case "respiration":  profile.penalties.respirationModifier   = Mathf.Max(0f, profile.penalties.respirationModifier   - penalty); break;
                        case "circulation":  profile.penalties.circulationModifier   = Mathf.Max(0f, profile.penalties.circulationModifier   - penalty); break;
                        case "digestion":    profile.penalties.digestionModifier     = Mathf.Max(0f, profile.penalties.digestionModifier     - penalty); break;
                        case "excretion":    profile.penalties.organFunctionModifier = Mathf.Max(0f, profile.penalties.organFunctionModifier - penalty); break;
                    }
                }
            }

            // Re-apply permanent scar penalties (not reset by Reset())
            float scarPenalty = profile.penalties.scarPenaltyAccumulator;
            if (scarPenalty > 0f)
            {
                profile.penalties.locomotionModifier    = Mathf.Max(0f, profile.penalties.locomotionModifier    - scarPenalty * 0.5f);
                profile.penalties.manipulationModifier  = Mathf.Max(0f, profile.penalties.manipulationModifier - scarPenalty * 0.5f);
            }
        }

        // ── Mood modifiers ────────────────────────────────────────────────────

        private void ApplyMoodModifiers(NPCInstance npc, MedicalProfile profile, StationState station)
        {
            if (_mood == null) return;
            int tick = station.tick;

            // Pain mood tiers (0=none, 1=low, 2=med, 3=high, 4=severe).
            // Push the current tier's modifier every tick so it stays active while sustained.
            // MoodSystem.PushModifier dedupes by (eventId, source) and refreshes duration,
            // so re-pushing each tick does not stack the delta — it just resets the timer.
            // Old tiers expire naturally after their 3-tick duration when no longer refreshed.
            int painTier = GetPainTier(profile.pain);
            if (painTier > 0)
            {
                float painDelta = -2f * painTier;  // -2 / -4 / -6 / -8
                _mood.PushModifier(npc, $"pain_tier_{painTier}", painDelta, 3, tick, "medical");
            }

            // Blood volume below 60% mood modifier — push every tick while below threshold.
            if (profile.bloodVolume < LowBloodMoodThreshold)
                _mood.PushModifier(npc, "low_blood_volume", -8f, 3, tick, "medical");

            // Active disease mood modifiers — one modifier per active disease stage, pushed every tick.
            foreach (var part in profile.parts.Values)
            {
                foreach (var disease in part.diseases)
                {
                    var def = _woundInfectionDef;
                    if (disease.diseaseId != def.diseaseId) continue;
                    if (disease.currentStage >= def.stages.Count) continue;

                    float stageMoodMod = def.stages[disease.currentStage].moodModifier;
                    if (stageMoodMod != 0f)
                    {
                        string eventId = $"disease_{disease.diseaseId}_stage{disease.currentStage}";
                        _mood.PushModifier(npc, eventId, stageMoodMod, 3, tick, "medical");
                    }
                }
            }
        }

        private static int GetPainTier(float pain)
        {
            if (pain >= 80f) return 4; // severe
            if (pain >= 60f) return 3; // high
            if (pain >= 40f) return 2; // medium
            if (pain >= PainMoodThreshold) return 1; // low
            return 0;
        }

        // ── Public wound creation API ─────────────────────────────────────────

        /// <summary>
        /// Creates and adds a wound to a specific body part on an NPC.
        /// Initialises MedicalProfile if needed.
        /// Returns the created wound, or null if the part does not exist.
        /// </summary>
        public Wound AddWound(NPCInstance npc, string partId,
                              WoundType type, WoundSeverity severity, int currentTick)
        {
            if (!FeatureFlags.MedicalSystem) return null;

            EnsureProfile(npc);
            var profile = npc.medicalProfile;

            if (!profile.TryGetPart(partId, out var part)) return null;
            if (!_woundTypes.TryGetValue(type, out var wtd)) return null;

            float bleedRate  = wtd.GetBleedRate(severity);
            float painContrib = ((int)severity) * (1f + wtd.painModifier);

            var wound = Wound.Create(type, severity, bleedRate, painContrib, currentTick);
            part.wounds.Add(wound);

            // Part health decreases with wound severity (10% per severity level)
            float damagePercent = (int)severity * 10f;
            part.health = Mathf.Max(0f, part.health - damagePercent);

            return wound;
        }
    }
}
