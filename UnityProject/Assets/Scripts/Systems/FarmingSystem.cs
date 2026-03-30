// FarmingSystem — manages Hydroponics Planter Tiles, crop growth, and NPC farming tasks.
//
// Each game tick (≈1 second) this system:
//   1. Updates per-device energisation state (GrowLights, Heater/Cooler/Vent) from wire adjacency.
//   2. Updates light levels on planter tiles from energised Grow Lights directly above.
//   3. Updates water status on planter tiles from adjacent pipe foundations.
//   4. Runs the growth tick on all Stage 1–2 planter tiles.
//   5. Scans for new SowTask / HarvestTask opportunities and queues them.
//   6. Assigns pending tasks to idle farming NPCs and advances in-progress tasks.
//
// Feature flag: FarmingSystem.Enabled = false pauses all scan and growth cycles
// without removing any code (safe for hotfix / rollback scenarios).
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class FarmingSystem
    {
        // ── Feature flag ──────────────────────────────────────────────────────
        /// <summary>Set to false to pause all farming scan/growth cycles.</summary>
        public static bool Enabled = true;

        // ── Task duration constants (ticks) ───────────────────────────────────
        private const int SowTaskTicks     = 30;   // simulated walk + seed placement
        private const int HarvestTaskTicks = 30;   // simulated walk + collect + deposit
        private const int TendTaskTicks    = 15;   // simulated walk + tending
        private const int TreatTaskTicks   = 20;   // simulated walk + crop treatment
        private const int JobRefreshTicks  = 5;    // jobTimer value refreshed each tick to
                                                    // prevent JobSystem reassignment

        // ── Growth modifier values ────────────────────────────────────────────
        private const float ModifierIdeal      = 1.0f;
        private const float ModifierAcceptable = 0.5f;
        private const float ModifierCritical   = 0.0f;

        // ── Neglect / blight / pests tuning ───────────────────────────────────
        public const int   TendFrequencyTicks              = 60;
        public const int   BlightNeglectThreshold          = 90;
        public const int   PestNeglectThreshold            = 70;
        public const float BlightTriggerChancePerTick      = 0.08f;
        public const float PestTriggerChancePerTick        = 0.10f;
        public const int   BlightDetectionBaseDelayTicks   = 18;
        public const int   PestDetectionBaseDelayTicks     = 14;
        public const int   PestDestroyThresholdTicks       = 160;
        public const int   PestYieldZeroTicks              = 120;
        public const float BlightSpreadBaseChancePerTick   = 0.10f;
        public const float BlightSpreadFirebreakMultiplier = 0.35f;

        // ── Grow Light output ─────────────────────────────────────────────────
        private const float GrowLightOutput = 1.0f;

        private readonly ContentRegistry _registry;
        private SkillSystem              _skillSystem;
        private MoodSystem               _mood;

        public FarmingSystem(ContentRegistry registry) => _registry = registry;

        /// <summary>Wire up SkillSystem after construction (called from GameManager).</summary>
        public void SetSkillSystem(SkillSystem skillSystem) => _skillSystem = skillSystem;

        /// <summary>Wire up MoodSystem after construction (called from GameManager).</summary>
        public void SetMoodSystem(MoodSystem mood) => _mood = mood;

        // ── Meal quality mood constants ───────────────────────────────────────
        // Harvesting under ideal conditions → fresh, high-quality produce → happy/sad boost.
        // Harvesting under degraded conditions → lower quality → neutral or minor boost.
        private const float MealQualityIdealDelta      = 6f;
        private const float MealQualityAcceptableDelta = 3f;
        private const int   MealQualityDurationTicks   = 120;  // ⅓ in-game day (120 of 360 ticks/day)

        // ── Condition tier (used for inspect display and growth logic) ────────
        public enum ConditionTier { Ideal, Acceptable, Critical }

        // ── Public API ────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            if (!Enabled) return;

            UpdateLightLevels(station);
            UpdateWaterStatus(station);
            UpdateNeglectAndNegativeEvents(station);
            RunGrowthTicks(station);
            GenerateFarmingTasks(station);
            ProcessFarmingTasks(station);
        }

        // ── Step 1 — light levels ─────────────────────────────────────────────

        /// <summary>
        /// Reset all planter light levels to 0, then apply GrowLightOutput from
        /// every energised Grow Light to the planter at the same grid position.
        /// A Grow Light above a non-planter tile has no effect and no error.
        /// </summary>
        private void UpdateLightLevels(StationState station)
        {
            // Reset
            foreach (var f in station.foundations.Values)
                if (f.buildableId == "buildable.hydroponics_planter")
                    f.lightLevel = 0f;

            // Apply from energised grow lights
            var plantersByPos = BuildPlanterLookup(station);
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.grow_light") continue;
                if (!f.isEnergised) continue;
                if (plantersByPos.TryGetValue((f.tileCol, f.tileRow), out var planter))
                    planter.lightLevel = GrowLightOutput;
                // No error when no planter below — graceful no-op.
            }
        }

        // ── Step 2 — water status ─────────────────────────────────────────────

        /// <summary>
        /// A planter is watered when any complete pipe conduit foundation (buildable.pipe)
        /// exists at the same tile position or at any directly adjacent tile.
        /// The planter itself is excluded from the pipe lookup to prevent self-watering.
        /// </summary>
        private void UpdateWaterStatus(StationState station)
        {
            var pipePositions = new HashSet<(int, int)>();
            foreach (var f in station.foundations.Values)
            {
                if (f.status != "complete") continue;
                if (f.buildableId == "buildable.hydroponics_planter") continue; // exclude self
                if (!_registry.Buildables.TryGetValue(f.buildableId, out var def)) continue;
                // Only count actual pipe conduits, not pipe consumers
                if (def.networkType == "pipe" && !def.requiresPower)
                    pipePositions.Add((f.tileCol, f.tileRow));
            }

            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.hydroponics_planter") continue;
                f.isWatered = IsAdjacentOrSame(pipePositions, f.tileCol, f.tileRow);
            }
        }

        // ── Step 3 — growth ticks ─────────────────────────────────────────────

        private void RunGrowthTicks(StationState station)
        {
            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.hydroponics_planter") continue;
                if (f.growthStage == 0 || f.growthStage == 3) continue;  // empty or mature
                if (f.cropId == null) continue;
                if (!_registry.Crops.TryGetValue(f.cropId, out var crop)) continue;
                if (FeatureFlags.FarmingNegativeEvents && f.hasBlight) continue;

                float modifier = EvaluateGrowthModifier(f, crop);

                if (modifier <= 0f)
                {
                    // Critical condition — accumulate damage
                    f.cropDamage += crop.damagePerSecond / 100f;
                    if (f.cropDamage >= 1f)
                    {
                        // Plant destroyed; crop assignment preserved but stage resets
                        f.growthStage    = 0;
                        f.growthProgress = 0f;
                        f.cropDamage     = 0f;
                        station.LogEvent($"Plant destroyed in planter at ({f.tileCol},{f.tileRow}) — critical conditions.");
                    }
                }
                else
                {
                    // Slowly recover damage when conditions are acceptable or better
                    if (f.cropDamage > 0f)
                        f.cropDamage = Mathf.Max(0f, f.cropDamage - 0.01f);

                    // Advance growth progress (1 tick ≈ 1 second)
                    float delta = modifier / crop.growthTimePerStage;
                    f.growthProgress += delta;

                    if (f.growthProgress >= 1f)
                    {
                        f.growthProgress = 0f;
                        f.growthStage    = Mathf.Min(f.growthStage + 1, 3);
                    }
                }
            }
        }

        // ── Step 4 — task generation ──────────────────────────────────────────

        private void GenerateFarmingTasks(StationState station)
        {
            // Build a lookup: planterUid → list of non-complete tasks for that planter
            var tasksByPlanter = new Dictionary<string, List<FarmingTaskInstance>>();
            foreach (var task in station.farmingTasks)
            {
                if (task.status == "complete") continue;
                if (!tasksByPlanter.ContainsKey(task.planterUid))
                    tasksByPlanter[task.planterUid] = new List<FarmingTaskInstance>();
                tasksByPlanter[task.planterUid].Add(task);
            }

            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.hydroponics_planter") continue;

                var tasks = tasksByPlanter.ContainsKey(f.uid)
                    ? tasksByPlanter[f.uid]
                    : new List<FarmingTaskInstance>();

                if (f.growthStage == 3)
                {
                    // Auto-generate HarvestTask; no duplicates
                    bool hasHarvest = tasks.Exists(t => t.taskType == "harvest");
                    if (!hasHarvest)
                        station.farmingTasks.Add(
                            FarmingTaskInstance.Create("harvest", f.uid, null, HarvestTaskTicks));
                }
                else if (f.growthStage == 0 && f.cropId != null)
                {
                    // Auto-generate SowTask only if seeds are in storage and no pending sow task
                    if (!_registry.Crops.TryGetValue(f.cropId, out var crop)) continue;
                    bool hasSow = tasks.Exists(t => t.taskType == "sow");
                    if (!hasSow && HasItemInStorage(station, crop.seedItemId))
                        station.farmingTasks.Add(
                            FarmingTaskInstance.Create("sow", f.uid, f.cropId, SowTaskTicks));
                    // When seeds are unavailable: no task generated, no error logged (per spec).
                }

                if (FeatureFlags.FarmingNegativeEvents && f.hasBlight && f.blightDetected)
                {
                    bool hasTreatBlight = tasks.Exists(t => t.taskType == "treat_blight");
                    if (!hasTreatBlight)
                        station.farmingTasks.Add(
                            FarmingTaskInstance.Create("treat_blight", f.uid, null, TreatTaskTicks));
                }

                if (FeatureFlags.FarmingNegativeEvents && f.hasPests && f.pestsDetected)
                {
                    bool hasTreatPests = tasks.Exists(t => t.taskType == "treat_pests");
                    if (!hasTreatPests)
                        station.farmingTasks.Add(
                            FarmingTaskInstance.Create("treat_pests", f.uid, null, TreatTaskTicks));
                }

                if (FeatureFlags.FarmingNegativeEvents
                    && f.cropId != null && f.growthStage > 0 && f.growthStage < 3)
                {
                    bool hasTend = tasks.Exists(t => t.taskType == "tend");
                    // `tasks` is built once before new treatment tasks are appended. We also gate on
                    // detected active conditions so tend is suppressed even in that first generation tick.
                    bool hasTreatment = tasks.Exists(t => t.taskType == "treat_blight" || t.taskType == "treat_pests")
                        || (f.hasBlight && f.blightDetected)
                        || (f.hasPests && f.pestsDetected);
                    if (!hasTend && !hasTreatment && IsTendOverdue(f, station.tick))
                        station.farmingTasks.Add(
                            FarmingTaskInstance.Create("tend", f.uid, null, TendTaskTicks));
                }
            }

            // Prune completed tasks to keep the list tidy
            station.farmingTasks.RemoveAll(t => t.status == "complete");
        }

        // ── Step 6 — NPC task processing ──────────────────────────────────────

        private void ProcessFarmingTasks(StationState station)
        {
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.classId != "class.farming") continue;

                // Only proceed when JobSystem has already assigned job.farming to this NPC.
                // This ensures sleep, hunger, night, and crisis overrides (rest/eat/recreate)
                // from JobSystem are respected — FarmingSystem never overrides them.
                if (npc.currentJobId != "job.farming") continue;

                // Find if this NPC already has an in-progress task
                FarmingTaskInstance activeTask = null;
                foreach (var task in station.farmingTasks)
                {
                    if (task.assignedNpcUid == npc.uid && task.status == "in_progress")
                    {
                        activeTask = task;
                        break;
                    }
                }

                if (activeTask != null)
                {
                    // Keep jobTimer alive so JobSystem does not reassign mid-task
                    npc.jobTimer = JobRefreshTicks;

                    activeTask.progressTicks--;
                    if (activeTask.progressTicks <= 0)
                        CompleteFarmingTask(station, activeTask, npc);
                }
                else
                {
                    // Look for a pending task to claim
                    bool assigned = false;
                    foreach (var task in station.farmingTasks)
                    {
                        if (task.status != "pending") continue;
                        task.assignedNpcUid = npc.uid;
                        task.status         = "in_progress";
                        npc.jobTimer        = JobRefreshTicks;
                        assigned            = true;
                        break;
                    }

                    // If no task was found, let jobTimer expire so JobSystem can reassign
                    if (!assigned && npc.jobTimer <= 1)
                        npc.jobTimer = 0;
                }
            }
        }

        // ── Task completion ───────────────────────────────────────────────────

        private void CompleteFarmingTask(StationState station, FarmingTaskInstance task,
                                          NPCInstance npc)
        {
            if (!station.foundations.TryGetValue(task.planterUid, out var planter))
            {
                task.status = "complete";
                return;
            }

            switch (task.taskType)
            {
                case "sow":
                    if (planter.growthStage == 0 && task.cropId != null
                        && _registry.Crops.TryGetValue(task.cropId, out var sowCrop))
                    {
                        bool consumed = TakeItemFromStorage(station, sowCrop.seedItemId, 1);
                        if (consumed)
                        {
                            planter.growthStage    = 1;
                            planter.growthProgress = 0f;
                            planter.cropDamage     = 0f;
                            planter.lastTendedTick = station.tick;
                            planter.neglectAccumulator = 0;
                            planter.pestAccumulator = 0;
                            station.LogEvent($"Planted {sowCrop.cropName} at ({planter.tileCol},{planter.tileRow}).");
                        }
                        else
                        {
                            // Seed no longer available — abort without error spam
                            station.LogEvent($"Sow task aborted: no {sowCrop.seedItemId} in storage.");
                        }
                    }
                    break;

                case "harvest":
                    if (planter.growthStage == 3 && planter.cropId != null
                        && _registry.Crops.TryGetValue(planter.cropId, out var hvCrop))
                    {
                        int baseQty = Random.Range(hvCrop.harvestQtyMin, hvCrop.harvestQtyMax + 1);
                        if (FeatureFlags.FarmingNegativeEvents && planter.hasBlight) baseQty = 0;
                        float pestYieldMult = (FeatureFlags.FarmingNegativeEvents && planter.hasPests)
                            ? ComputePestYieldMultiplier(planter.pestTicks) : 1f;
                        baseQty = Mathf.RoundToInt(baseQty * pestYieldMult);
                        // Apply expertise yield multiplier (Master Harvester +25%)
                        float yieldMult = _skillSystem != null
                            ? _skillSystem.GetYieldMultiplier(npc, "skill.farming") : 1.0f;
                        int qty = Mathf.RoundToInt(baseQty * yieldMult);
                        bool stored = true;
                        if (qty > 0)
                            stored = AddItemToStorage(station, hvCrop.harvestItemId, qty);
                        if (!stored)
                        {
                            // Storage full — drop at tile and warn (no softlock)
                            Debug.LogWarning($"[FarmingSystem] Storage full — dropping {qty}× " +
                                             $"{hvCrop.harvestItemId} at planter ({planter.tileCol},{planter.tileRow}).");
                            station.LogEvent($"Warning: storage full — {qty}× {hvCrop.harvestItemId} dropped.");
                        }

                        // Push meal quality mood modifier on the happy/sad axis.
                        // Quality is derived from grow conditions at the time of harvest.
                        float growthMod = EvaluateGrowthModifier(planter, hvCrop);
                        if (_mood != null)
                        {
                            float moodDelta = growthMod >= ModifierIdeal
                                ? MealQualityIdealDelta
                                : (growthMod >= ModifierAcceptable ? MealQualityAcceptableDelta : 0f);
                            if (moodDelta > 0f)
                                _mood.PushModifier(npc, "meal_quality_harvest", moodDelta,
                                                   MealQualityDurationTicks, station.tick,
                                                   MoodAxis.HappySad, "farming_system");
                        }

                        planter.growthStage    = 0;
                        planter.growthProgress = 0f;
                        planter.cropDamage     = 0f;
                        station.LogEvent($"Harvested {qty}× {hvCrop.harvestItemId} from ({planter.tileCol},{planter.tileRow}).");
                    }
                    break;

                case "tend":
                    if (!FeatureFlags.FarmingNegativeEvents)
                        break;
                    planter.lastTendedTick    = station.tick;
                    planter.neglectAccumulator = 0;
                    planter.pestAccumulator    = 0;
                    station.LogEvent($"Planter tended at ({planter.tileCol},{planter.tileRow}).");
                    break;

                case "treat_blight":
                    if (!FeatureFlags.FarmingNegativeEvents)
                        break;
                    planter.hasBlight          = false;
                    planter.blightDetected     = false;
                    planter.blightTicks        = 0;
                    planter.neglectAccumulator = 0;
                    station.LogEvent($"Blight treated at ({planter.tileCol},{planter.tileRow}).");
                    break;

                case "treat_pests":
                    if (!FeatureFlags.FarmingNegativeEvents)
                        break;
                    planter.hasPests        = false;
                    planter.pestsDetected   = false;
                    planter.pestTicks       = 0;
                    planter.pestAccumulator = 0;
                    station.LogEvent($"Pests treated at ({planter.tileCol},{planter.tileRow}).");
                    break;
            }

            // Award Farming skill XP for task completion
            _skillSystem?.AwardXP(npc, task.taskType, station);

            task.status  = "complete";
            npc.jobTimer = 0;   // let JobSystem reassign next tick
        }

        // ── Growth modifier evaluation (public for UI inspect) ────────────────

        /// <summary>
        /// Evaluate the GrowthRateModifier for a planter given its current conditions.
        /// Returns 1.0 (all ideal), 0.5 (any acceptable, none critical), or 0.0 (any critical).
        /// </summary>
        public float EvaluateGrowthModifier(FoundationInstance planter, CropDataDefinition crop)
        {
            bool anyCritical   = false;
            bool anyAcceptable = false;

            // Temperature
            var tempTier = EvalTemperature(planter.tileTemperature, crop);
            if (tempTier == ConditionTier.Critical)   anyCritical   = true;
            else if (tempTier == ConditionTier.Acceptable) anyAcceptable = true;

            // Light
            var lightTier = EvalLight(planter.lightLevel, crop);
            if (lightTier == ConditionTier.Critical)   anyCritical   = true;
            else if (lightTier == ConditionTier.Acceptable) anyAcceptable = true;

            // Water (binary: not watered = critical)
            if (crop.requiresWater && !planter.isWatered)
                anyCritical = true;

            if (anyCritical)   return ModifierCritical;
            if (anyAcceptable) return ModifierAcceptable;
            return ModifierIdeal;
        }

        public static ConditionTier EvalTemperature(float temp, CropDataDefinition crop)
        {
            if (temp >= crop.idealTempMin      && temp <= crop.idealTempMax)      return ConditionTier.Ideal;
            if (temp >= crop.acceptableTempMin && temp <= crop.acceptableTempMax) return ConditionTier.Acceptable;
            return ConditionTier.Critical;
        }

        public static ConditionTier EvalLight(float light, CropDataDefinition crop)
        {
            if (light >= crop.idealLightMin      && light <= crop.idealLightMax)      return ConditionTier.Ideal;
            if (light >= crop.acceptableLightMin && light <= crop.acceptableLightMax) return ConditionTier.Acceptable;
            return ConditionTier.Critical;
        }

        // ── Storage helpers ───────────────────────────────────────────────────

        /// <summary>Returns true when at least qty of itemId is available in any storage.</summary>
        public static bool HasItemInStorage(StationState station, string itemId)
        {
            foreach (var mod in station.modules.Values)
                if (mod.inventory.TryGetValue(itemId, out int q) && q > 0) return true;
            foreach (var f in station.foundations.Values)
                if (f.cargo.TryGetValue(itemId, out int q) && q > 0) return true;
            return false;
        }

        /// <summary>
        /// Remove qty units of itemId from station storage.
        /// Returns true if the full quantity was taken.
        /// </summary>
        private static bool TakeItemFromStorage(StationState station, string itemId, int qty)
        {
            int remaining = qty;

            foreach (var mod in station.modules.Values)
            {
                if (remaining <= 0) break;
                if (!mod.inventory.TryGetValue(itemId, out int q) || q <= 0) continue;
                int take = Mathf.Min(q, remaining);
                remaining -= take;
                int newQ = q - take;
                if (newQ <= 0) mod.inventory.Remove(itemId);
                else           mod.inventory[itemId] = newQ;
            }

            foreach (var f in station.foundations.Values)
            {
                if (remaining <= 0) break;
                if (!f.cargo.TryGetValue(itemId, out int q) || q <= 0) continue;
                int take = Mathf.Min(q, remaining);
                remaining -= take;
                int newQ = q - take;
                if (newQ <= 0) f.cargo.Remove(itemId);
                else           f.cargo[itemId] = newQ;
            }

            return remaining <= 0;
        }

        /// <summary>
        /// Add qty units of itemId to station storage.
        /// Prefers modules with cargoSettings (cargo holds); falls back to any module,
        /// then foundation cargo. Foundation cargo respects cargoCapacity limits.
        /// Returns true if all items were stored; returns false if no storage found
        /// (NPC drops items at tile — no softlock).
        /// </summary>
        private static bool AddItemToStorage(StationState station, string itemId, int qty)
        {
            // Prefer module inventories (modules with cargoSettings are cargo holds)
            foreach (var mod in station.modules.Values)
            {
                if (mod.cargoSettings == null) continue;
                mod.inventory[itemId] =
                    (mod.inventory.TryGetValue(itemId, out int cur) ? cur : 0) + qty;
                return true;
            }

            // Fallback: any module at all
            foreach (var mod in station.modules.Values)
            {
                mod.inventory[itemId] =
                    (mod.inventory.TryGetValue(itemId, out int cur) ? cur : 0) + qty;
                return true;
            }

            // Foundation cargo — respect capacity
            foreach (var f in station.foundations.Values)
            {
                if (f.cargoCapacity <= 0) continue;
                int totalStored = 0;
                foreach (var v in f.cargo.Values) totalStored += v;
                int space = f.cargoCapacity - totalStored;
                if (space <= 0) continue;
                int store = System.Math.Min(qty, space);
                f.cargo[itemId] =
                    (f.cargo.TryGetValue(itemId, out int cur2) ? cur2 : 0) + store;
                if (store >= qty) return true;
                qty -= store;
            }

            return false;   // no storage with capacity found
        }

        // ── Utility helpers ───────────────────────────────────────────────────


        private static bool IsAdjacentOrSame(HashSet<(int, int)> positions, int col, int row)
            => positions.Contains((col,     row    ))
            || positions.Contains((col + 1, row    ))
            || positions.Contains((col - 1, row    ))
            || positions.Contains((col,     row + 1))
            || positions.Contains((col,     row - 1));

        private static Dictionary<(int, int), FoundationInstance> BuildPlanterLookup(
            StationState station)
        {
            var lookup = new Dictionary<(int, int), FoundationInstance>();
            foreach (var f in station.foundations.Values)
                if (f.buildableId == "buildable.hydroponics_planter")
                    lookup[(f.tileCol, f.tileRow)] = f;
            return lookup;
        }

        public static float ComputeTemperatureSpreadModifier(float temperatureC)
        {
            if (temperatureC <= 10f) return 0.60f;
            if (temperatureC >= 30f) return 1.50f;
            return Mathf.Lerp(0.60f, 1.50f, (temperatureC - 10f) / 20f);
        }

        public static int ComputeDetectionDelayFromBotany(int botanySkill, int baseDelayTicks)
            => Mathf.Max(2, baseDelayTicks - Mathf.FloorToInt(botanySkill / 2f));

        public static float ComputePestYieldMultiplier(int pestTicks)
            => Mathf.Clamp01(1f - (float)pestTicks / PestYieldZeroTicks);

        public static float ComputeBlightSpreadChance(float temperatureC, bool throughFirebreak)
        {
            float chance = BlightSpreadBaseChancePerTick * ComputeTemperatureSpreadModifier(temperatureC);
            if (throughFirebreak) chance *= BlightSpreadFirebreakMultiplier;
            return chance;
        }

        public static bool ShouldTriggerNegativeEvent(int accumulator, int threshold, float chancePerTick, float roll01)
            => accumulator >= threshold && roll01 < chancePerTick;

        private static bool IsTendOverdue(FoundationInstance planter, int currentTick)
        {
            if (planter.lastTendedTick < 0)
                return currentTick >= TendFrequencyTicks;
            return currentTick - planter.lastTendedTick >= TendFrequencyTicks;
        }

        private void UpdateNeglectAndNegativeEvents(StationState station)
        {
            if (!FeatureFlags.FarmingNegativeEvents) return;

            int bestBotany = GetBestActiveBotanySkill(station);
            var plantersByPos = BuildPlanterLookup(station);
            var pendingBlight = new HashSet<string>();

            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.hydroponics_planter") continue;
                if (f.cropId == null || f.growthStage <= 0) continue;

                if (IsTendOverdue(f, station.tick))
                {
                    f.neglectAccumulator++;
                    f.pestAccumulator++;
                }

                if (!f.hasBlight && ShouldTriggerNegativeEvent(
                        f.neglectAccumulator, BlightNeglectThreshold, BlightTriggerChancePerTick, Random.value))
                {
                    f.hasBlight      = true;
                    f.blightDetected = false;
                    f.blightTicks    = 0;
                    station.LogEvent($"Blight developed at ({f.tileCol},{f.tileRow}).");
                }

                if (!f.hasPests && ShouldTriggerNegativeEvent(
                        f.pestAccumulator, PestNeglectThreshold, PestTriggerChancePerTick, Random.value))
                {
                    f.hasPests      = true;
                    f.pestsDetected = false;
                    f.pestTicks     = 0;
                    station.LogEvent($"Pest infestation developed at ({f.tileCol},{f.tileRow}).");
                }

                if (f.hasBlight)
                {
                    f.blightTicks++;
                    int detectDelay = ComputeDetectionDelayFromBotany(bestBotany, BlightDetectionBaseDelayTicks);
                    if (!f.blightDetected && f.blightTicks >= detectDelay)
                        f.blightDetected = true;
                }

                if (f.hasPests)
                {
                    f.pestTicks++;
                    int detectDelay = ComputeDetectionDelayFromBotany(bestBotany, PestDetectionBaseDelayTicks);
                    if (!f.pestsDetected && f.pestTicks >= detectDelay)
                        f.pestsDetected = true;

                    if (f.pestTicks >= PestDestroyThresholdTicks)
                    {
                        f.growthStage     = 0;
                        f.growthProgress  = 0f;
                        f.cropDamage      = 0f;
                        f.hasPests        = false;
                        f.pestsDetected   = false;
                        f.pestTicks       = 0;
                        f.pestAccumulator = 0;
                        f.hasBlight       = false;
                        f.blightDetected  = false;
                        f.blightTicks     = 0;
                        station.LogEvent($"Crop destroyed by pests at ({f.tileCol},{f.tileRow}).");
                        continue;
                    }
                }
            }

            foreach (var f in station.foundations.Values)
            {
                if (f.buildableId != "buildable.hydroponics_planter") continue;
                if (!f.hasBlight) continue;
                if (f.cropId == null || f.growthStage <= 0) continue;

                float temperature = TemperatureSystem.GetEffectiveTemperature(station, f.tileCol, f.tileRow);
                float directChance = ComputeBlightSpreadChance(temperature, throughFirebreak: false);
                float firebreakChance = ComputeBlightSpreadChance(temperature, throughFirebreak: true);

                TrySpreadTo(f.tileCol + 1, f.tileRow, directChance, plantersByPos, pendingBlight);
                TrySpreadTo(f.tileCol - 1, f.tileRow, directChance, plantersByPos, pendingBlight);
                TrySpreadTo(f.tileCol, f.tileRow + 1, directChance, plantersByPos, pendingBlight);
                TrySpreadTo(f.tileCol, f.tileRow - 1, directChance, plantersByPos, pendingBlight);

                TrySpreadAcrossFirebreak(f.tileCol, f.tileRow, 1, 0, firebreakChance, plantersByPos, pendingBlight);
                TrySpreadAcrossFirebreak(f.tileCol, f.tileRow, -1, 0, firebreakChance, plantersByPos, pendingBlight);
                TrySpreadAcrossFirebreak(f.tileCol, f.tileRow, 0, 1, firebreakChance, plantersByPos, pendingBlight);
                TrySpreadAcrossFirebreak(f.tileCol, f.tileRow, 0, -1, firebreakChance, plantersByPos, pendingBlight);
            }

            foreach (var uid in pendingBlight)
            {
                if (!station.foundations.TryGetValue(uid, out var target)) continue;
                if (target.hasBlight) continue;
                target.hasBlight      = true;
                target.blightDetected = false;
                target.blightTicks    = 0;
            }
        }

        private static void TrySpreadTo(int col, int row, float chance,
            Dictionary<(int, int), FoundationInstance> plantersByPos,
            HashSet<string> pendingBlight)
        {
            if (!plantersByPos.TryGetValue((col, row), out var target)) return;
            if (target.hasBlight) return;
            if (target.cropId == null || target.growthStage <= 0) return;
            if (Random.value < chance)
                pendingBlight.Add(target.uid);
        }

        private static void TrySpreadAcrossFirebreak(int sourceCol, int sourceRow, int dc, int dr, float chance,
            Dictionary<(int, int), FoundationInstance> plantersByPos, HashSet<string> pendingBlight)
        {
            int midCol = sourceCol + dc;
            int midRow = sourceRow + dr;
            if (plantersByPos.ContainsKey((midCol, midRow))) return;
            TrySpreadTo(sourceCol + dc * 2, sourceRow + dr * 2, chance, plantersByPos, pendingBlight);
        }

        private static int GetBestActiveBotanySkill(StationState station)
        {
            int best = 0;
            foreach (var npc in station.npcs.Values)
            {
                if (!npc.IsCrew()) continue;
                if (npc.classId != "class.farming") continue;
                if (npc.currentJobId != "job.farming") continue;
                int botany = npc.abilityScores.WIS + npc.abilityScores.INT / 2;
                best = Mathf.Max(best, botany);
            }
            return best;
        }
    }
}
