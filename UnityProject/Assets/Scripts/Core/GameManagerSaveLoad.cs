// GameManagerSaveLoad.cs — partial class extension of GameManager.
// Contains BuildSaveData / ApplySaveData and all Serialize*/Deserialize* helpers
// for full game-state serialisation (STA-005).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Waystation.Models;

namespace Waystation.Core
{
    public partial class GameManager
    {
        // ─────────────────────────────────────────────────────────────────────
        // BuildSaveData / ApplySaveData
        // ─────────────────────────────────────────────────────────────────────

        private Dictionary<string, object> BuildSaveData()
        {
            // ── Shared / legacy state (always saved) ─────────────────────────
            var data = new Dictionary<string, object>
            {
                { "version",                          SaveVersion },
                { "full_save",                        FeatureFlags.FullSaveLoad },
                { "station_name",                     Station.stationName },
                { "tick",                             Station.tick },
                { "rank_names",                       ToObjList(Station.rankNames) },
                { "player_faction_color",             Station.playerFactionColor },
                { "player_faction_color_secondary",   Station.playerFactionColorSecondary },
                { "disposal_tile_designated",         Station.disposalTileDesignated },
                { "disposal_tile_col",                Station.disposalTileCol },
                { "disposal_tile_row",                Station.disposalTileRow },
            };

            // Primitives serialised via helper to keep BuildSaveData readable
            SaveDict(data, "resources",                    Station.resources);
            SaveDict(data, "faction_reputation",           Station.factionReputation);
            SaveSet(data,  "active_tags",                  Station.activeTags);
            SaveBoolDict(data, "chain_flags",              Station.chainFlags);
            SaveDict(data, "policy",                       Station.policy);
            SaveIntDict(data, "event_cooldowns",           Station.eventCooldowns);
            SaveStringList(data, "log",                    Station.log);
            SaveDict(data, "custom_room_names",            Station.customRoomNames);
            SaveDict(data, "player_room_type_assignments", Station.playerRoomTypeAssignments);

            // Research
            data["research"] = BuildResearchSaveData();

            // Galaxy
            data["galaxy"] = BuildGalaxySaveData();

            if (!FeatureFlags.FullSaveLoad)
                return data;

            // ── Full state ────────────────────────────────────────────────────
            data["npcs"]            = SerializeAll(Station.npcs.Values,            SerializeNpc);
            data["foundations"]     = SerializeAll(Station.foundations.Values,     SerializeFoundation);
            data["ships"]           = SerializeAll(Station.ships.Values,            SerializeShip);
            data["owned_ships"]     = SerializeAll(Station.ownedShips.Values,      SerializeOwnedShip);
            data["missions"]        = SerializeAll(Station.missions.Values,         SerializeMission);
            data["asteroid_maps"]   = SerializeAll(Station.asteroidMaps.Values,    SerializeAsteroidMap);
            data["points_of_interest"] = SerializeAll(Station.pointsOfInterest.Values, SerializePoi);
            data["relationships"]   = SerializeAll(Station.relationships.Values,   SerializeRelationship);
            data["messages"]        = SerializeAll(Station.messages,               SerializeMessage);
            data["departments"]     = SerializeAll(Station.departments,            SerializeDepartment);
            data["bodies"]          = SerializeAll(Station.bodies.Values,          SerializeBody);
            data["farming_tasks"]   = SerializeAll(Station.farmingTasks,           SerializeFarmingTask);
            data["standing_buy_orders"]  = SerializeAll(Station.standingBuyOrders,  SerializeStandingOrder);
            data["standing_sell_orders"] = SerializeAll(Station.standingSellOrders, SerializeStandingOrder);

            // Workbench queues: foundationUid → list
            var wqOut = new Dictionary<string, object>();
            foreach (var kv in Station.workbenchQueues)
                wqOut[kv.Key] = SerializeAll(kv.Value, SerializeWorkbenchQueueEntry);
            data["workbench_queues"] = wqOut;

            // Faction contracts: contractId → contract
            var fcOut = new Dictionary<string, object>();
            foreach (var kv in Station.factionContracts)
                fcOut[kv.Key] = SerializeFactionContract(kv.Value);
            data["faction_contracts"] = fcOut;

            // Work assignments: npcUid → list of jobIds
            var waOut = new Dictionary<string, object>();
            foreach (var kv in Station.workAssignments)
                waOut[kv.Key] = new List<object>(kv.Value.ConvertAll(j => (object)j));
            data["work_assignments"] = waOut;

            // Temperatures
            SaveFloatDict(data, "room_temperatures", Station.roomTemperatures);
            SaveFloatDict(data, "tile_temperatures", Station.tileTemperatures);
            SaveDict(data, "room_roles", Station.roomRoles);

            // Pending marriage events
            data["pending_marriage_events"] = ToObjList(Station.pendingMarriageEvents);

            // Regions
            var regOut = new Dictionary<string, object>();
            foreach (var kv in Station.regions)
                regOut[kv.Key] = SerializeRegion(kv.Value);
            data["regions"] = regOut;

            // Generated factions
            var gfOut = new Dictionary<string, object>();
            foreach (var kv in Station.generatedFactions)
                gfOut[kv.Key] = SerializeGeneratedFaction(kv.Value);
            data["generated_factions"] = gfOut;

            // Departed NPCs
            var dnOut = new Dictionary<string, object>();
            foreach (var kv in Station.departedNpcs)
                dnOut[kv.Key] = new Dictionary<string, object>
                {
                    { "departed_at_tick",         kv.Value.departedAtTick },
                    { "reason",                   kv.Value.reason },
                    { "eligible_for_reinjection", kv.Value.eligibleForReinjection },
                    { "npc",                      SerializeNpc(kv.Value.npc) },
                };
            data["departed_npcs"] = dnOut;

            // Captured NPCs
            var cnOut = new Dictionary<string, object>();
            foreach (var kv in Station.capturedNpcs)
                cnOut[kv.Key] = new Dictionary<string, object>
                {
                    { "captured_at_tick",    kv.Value.capturedAtTick },
                    { "captured_by",         kv.Value.capturedBy },
                    { "eligible_for_rescue", kv.Value.eligibleForRescue },
                    { "npc",                 SerializeNpc(kv.Value.npc) },
                };
            data["captured_npcs"] = cnOut;

            // Networks
            var netOut = new Dictionary<string, object>();
            foreach (var kv in Station.networks)
                netOut[kv.Key] = SerializeNetwork(kv.Value);
            data["networks"] = netOut;

            // Solar system
            if (Station.solarSystem != null)
                data["solar_system"] = SerializeSolarSystem(Station.solarSystem);

            return data;
        }

        private Dictionary<string, object> BuildResearchSaveData()
        {
            var d = new Dictionary<string, object>();
            if (Station.research == null) return d;
            foreach (var kv in Station.research.branches)
                d[kv.Key.ToString()] = new Dictionary<string, object>
                {
                    { "points",         kv.Value.points },
                    { "unlocked",       new List<string>(kv.Value.unlockedNodeIds) },
                    { "unlocked_order", new List<string>(kv.Value.unlockedNodeOrder) },
                };
            d["pending_datachips"]   = Station.research.pendingDatachips;
            d["applied_unlock_tags"] = new List<string>(Station.research.appliedUnlockTags);
            return d;
        }

        private Dictionary<string, object> BuildGalaxySaveData()
        {
            var d = new Dictionary<string, object>
            {
                { "galaxy_seed",        Station.galaxySeed },
                { "exploration_points", Station.explorationPoints },
            };
            var sectorList = new List<object>();
            foreach (var s in Station.sectors.Values)
                sectorList.Add(new Dictionary<string, object>
                {
                    { "uid",              s.uid },
                    { "proper_name",      s.properName },
                    { "is_renamed",       s.isRenamed },
                    { "discovery",        s.discoveryState.ToString() },
                    { "coordinates_x",    s.coordinates.x },
                    { "coordinates_y",    s.coordinates.y },
                    { "designation_code", s.designationCode },
                    { "prefix",           s.surveyPrefix.ToString() },
                });
            d["sectors"] = sectorList;
            d["charted_system_seeds"] = new List<int>(Station.chartedSystemSeeds);
            var chipList = new List<object>();
            foreach (var c in Station.explorationDatachips.Values)
                chipList.Add(new Dictionary<string, object>
                {
                    { "uid",                   c.uid },
                    { "system_seed",           c.systemSeed },
                    { "system_name",           c.systemName },
                    { "holder_foundation_uid", c.holderFoundationUid },
                    { "installed_in_server",   c.installedInServer },
                });
            d["exploration_datachips"] = chipList;
            return d;
        }

        private void ApplySaveData(Dictionary<string, object> data, bool isFullSave)
        {
            string stationName = data.TryGetValue("station_name", out var sn) ? sn?.ToString() : "Waystation";
            Station = new StationState(stationName);

            if (data.TryGetValue("tick", out var tick)) Station.tick = Convert.ToInt32(tick);

            ReadDict(data, "resources",             v => Station.resources[v.Key]            = Convert.ToSingle(v.Value));
            ReadDict(data, "faction_reputation",    v => Station.factionReputation[v.Key]    = Convert.ToSingle(v.Value));
            ReadList(data, "active_tags",           v => Station.activeTags.Add(v.ToString()));
            ReadDict(data, "chain_flags",           v => Station.chainFlags[v.Key]           = Convert.ToBoolean(v.Value));
            ReadDict(data, "policy",                v => Station.policy[v.Key]               = v.Value?.ToString());
            ReadDict(data, "event_cooldowns",       v => Station.eventCooldowns[v.Key]       = Convert.ToInt32(v.Value));
            ReadList(data, "log",                   v => Station.log.Add(v.ToString()));
            ReadDict(data, "custom_room_names",     v => Station.customRoomNames[v.Key]      = v.Value?.ToString());
            ReadDict(data, "player_room_type_assignments", v => Station.playerRoomTypeAssignments[v.Key] = v.Value?.ToString());

            if (data.TryGetValue("rank_names", out var rn) && rn is List<object> rnl)
            { Station.rankNames.Clear(); foreach (var r in rnl) Station.rankNames.Add(r.ToString()); }

            if (data.TryGetValue("player_faction_color",           out var pfc))  Station.playerFactionColor          = pfc?.ToString();
            if (data.TryGetValue("player_faction_color_secondary", out var pfcs)) Station.playerFactionColorSecondary = pfcs?.ToString();
            if (data.TryGetValue("disposal_tile_designated", out var dtd)) Station.disposalTileDesignated = Convert.ToBoolean(dtd);
            if (data.TryGetValue("disposal_tile_col",        out var dtc)) Station.disposalTileCol        = Convert.ToInt32(dtc);
            if (data.TryGetValue("disposal_tile_row",        out var dtr)) Station.disposalTileRow        = Convert.ToInt32(dtr);

            // Research
            if (data.TryGetValue("research", out var rsr) && rsr is Dictionary<string, object> rsrDict && Station.research != null)
            {
                if (rsrDict.TryGetValue("pending_datachips", out var pdv)) Station.research.pendingDatachips = Convert.ToInt32(pdv);
                ReadList(rsrDict, "applied_unlock_tags", v => Station.research.appliedUnlockTags.Add(v.ToString()));
                foreach (var kv in rsrDict)
                {
                    if (kv.Key == "pending_datachips" || kv.Key == "applied_unlock_tags") continue;
                    if (!(kv.Value is Dictionary<string, object> bd)) continue;
                    if (!Enum.TryParse(kv.Key, out ResearchBranch branch)) continue;
                    if (!Station.research.branches.TryGetValue(branch, out var bs)) continue;
                    if (bd.TryGetValue("points", out var pts)) bs.points = Convert.ToSingle(pts);
                    ReadList(bd, "unlocked",       v => bs.unlockedNodeIds.Add(v.ToString()));
                    ReadList(bd, "unlocked_order", v => bs.unlockedNodeOrder.Add(v.ToString()));
                }
            }

            // Galaxy
            ApplyGalaxySaveData(data);

            if (!isFullSave || !FeatureFlags.FullSaveLoad)
            {
                SetupStartingModules();
                SetupStartingCrew();
            }
            else
            {
                DeserializeIntoDict(data, "npcs",         Station.npcs,             DeserializeNpc,      n => n.uid);
                DeserializeIntoDict(data, "foundations",  Station.foundations,      DeserializeFoundation, f => f.uid);
                DeserializeIntoDict(data, "ships",        Station.ships,            DeserializeShip,     s => s.uid);
                DeserializeIntoDict(data, "owned_ships",  Station.ownedShips,       DeserializeOwnedShip, s => s.uid);
                DeserializeIntoDict(data, "missions",     Station.missions,         DeserializeMission,  m => m.uid);
                DeserializeIntoDict(data, "asteroid_maps", Station.asteroidMaps,    DeserializeAsteroidMap, a => a.uid);
                DeserializeIntoDict(data, "points_of_interest", Station.pointsOfInterest, DeserializePoi, p => p.uid);
                DeserializeIntoDict(data, "bodies",       Station.bodies,           DeserializeBody,     b => b.uid);

                // Relationships keyed by pair key
                ReadObjList(data, "relationships", d2 => {
                    var r = DeserializeRelationship(d2);
                    if (r != null) Station.relationships[RelationshipRecord.MakeKey(r.npcUid1, r.npcUid2)] = r;
                });

                // Messages (ordered list)
                ReadObjList(data, "messages",      d2 => { var m = DeserializeMessage(d2);     if (m != null) Station.messages.Add(m); });
                ReadObjList(data, "farming_tasks", d2 => { var t = DeserializeFarmingTask(d2); if (t != null) Station.farmingTasks.Add(t); });
                ReadObjList(data, "standing_buy_orders",  d2 => { var o = DeserializeStandingOrder(d2); if (o != null) Station.standingBuyOrders.Add(o); });
                ReadObjList(data, "standing_sell_orders", d2 => { var o = DeserializeStandingOrder(d2); if (o != null) Station.standingSellOrders.Add(o); });

                // Departments: override the defaults created by the constructor
                if (data.TryGetValue("departments", out var deptsRaw) && deptsRaw is List<object> deptsList)
                {
                    Station.departments.Clear();
                    foreach (var d2 in deptsList)
                        if (d2 is Dictionary<string, object> dd) { var dept = DeserializeDepartment(dd); if (dept != null) Station.departments.Add(dept); }
                }

                // Workbench queues
                if (data.TryGetValue("workbench_queues", out var wqRaw) && wqRaw is Dictionary<string, object> wqDict)
                    foreach (var kv in wqDict)
                        if (kv.Value is List<object> qList)
                        {
                            var entries = new List<WorkbenchQueueEntry>();
                            foreach (var e in qList)
                                if (e is Dictionary<string, object> ed) { var entry = DeserializeWorkbenchQueueEntry(ed); if (entry != null) entries.Add(entry); }
                            Station.workbenchQueues[kv.Key] = entries;
                        }

                // Faction contracts
                if (data.TryGetValue("faction_contracts", out var fcRaw) && fcRaw is Dictionary<string, object> fcDict)
                    foreach (var kv in fcDict)
                        if (kv.Value is Dictionary<string, object> fcd) { var c = DeserializeFactionContract(fcd); if (c != null) Station.factionContracts[kv.Key] = c; }

                // Work assignments
                if (data.TryGetValue("work_assignments", out var waRaw) && waRaw is Dictionary<string, object> waDict)
                    foreach (var kv in waDict)
                        if (kv.Value is List<object> jl) { var jobs = new List<string>(); foreach (var j in jl) jobs.Add(j.ToString()); Station.workAssignments[kv.Key] = jobs; }

                // Temperatures / room roles
                ReadDict(data, "room_temperatures", v => Station.roomTemperatures[v.Key] = Convert.ToSingle(v.Value));
                ReadDict(data, "tile_temperatures", v => Station.tileTemperatures[v.Key] = Convert.ToSingle(v.Value));
                ReadDict(data, "room_roles",        v => Station.roomRoles[v.Key]        = v.Value?.ToString());

                ReadList(data, "pending_marriage_events", v => Station.pendingMarriageEvents.Add(v.ToString()));

                // Regions
                if (data.TryGetValue("regions", out var regRaw) && regRaw is Dictionary<string, object> regDict)
                    foreach (var kv in regDict)
                        if (kv.Value is Dictionary<string, object> rd) Station.regions[kv.Key] = DeserializeRegion(rd);

                // Generated factions
                if (data.TryGetValue("generated_factions", out var gfRaw) && gfRaw is Dictionary<string, object> gfDict)
                    foreach (var kv in gfDict)
                        if (kv.Value is Dictionary<string, object> gfd) { var f = DeserializeGeneratedFaction(gfd); if (f != null) Station.generatedFactions[kv.Key] = f; }

                // Departed NPCs
                if (data.TryGetValue("departed_npcs", out var dnRaw) && dnRaw is Dictionary<string, object> dnDict)
                    foreach (var kv in dnDict)
                        if (kv.Value is Dictionary<string, object> dnd)
                        {
                            var npcRec = new DepartedNpcRecord
                            {
                                departedAtTick         = dnd.TryGetValue("departed_at_tick",         out var dat)  ? Convert.ToInt32(dat)  : 0,
                                reason                 = dnd.TryGetValue("reason",                   out var dr)   ? dr?.ToString()        : null,
                                eligibleForReinjection = dnd.TryGetValue("eligible_for_reinjection", out var efr)  && Convert.ToBoolean(efr),
                                npc                    = dnd.TryGetValue("npc", out var dn) && dn is Dictionary<string, object> npcd ? DeserializeNpc(npcd) : null,
                            };
                            if (npcRec.npc != null) Station.departedNpcs[kv.Key] = npcRec;
                        }

                // Captured NPCs
                if (data.TryGetValue("captured_npcs", out var cnRaw) && cnRaw is Dictionary<string, object> cnDict)
                    foreach (var kv in cnDict)
                        if (kv.Value is Dictionary<string, object> cnd)
                        {
                            var npcRec = new CapturedNpcRecord
                            {
                                capturedAtTick    = cnd.TryGetValue("captured_at_tick",    out var cat)  ? Convert.ToInt32(cat) : 0,
                                capturedBy        = cnd.TryGetValue("captured_by",         out var cb)   ? cb?.ToString()       : null,
                                eligibleForRescue = cnd.TryGetValue("eligible_for_rescue", out var efr) && Convert.ToBoolean(efr),
                                npc               = cnd.TryGetValue("npc", out var cn) && cn is Dictionary<string, object> npcd ? DeserializeNpc(npcd) : null,
                            };
                            if (npcRec.npc != null) Station.capturedNpcs[kv.Key] = npcRec;
                        }

                // Networks
                if (data.TryGetValue("networks", out var netRaw) && netRaw is Dictionary<string, object> netDict)
                    foreach (var kv in netDict)
                        if (kv.Value is Dictionary<string, object> nd2) { var net = DeserializeNetwork(nd2); if (net != null) Station.networks[kv.Key] = net; }

                // Solar system
                if (data.TryGetValue("solar_system", out var ssRaw) && ssRaw is Dictionary<string, object> ssd)
                    Station.solarSystem = DeserializeSolarSystem(ssd);
            }

            // Post-load initialisation
            Factions.Initialize(Station);
            Skills.InitialiseNpcSkills(Station);
            DeptRegistry.Init(Station.departments);
            Rooms.RebuildBonusCache(Station);
            UtilityNetworks.RebuildAll(Station);
            Resources.ResetWarningState();
        }

        private void ApplyGalaxySaveData(Dictionary<string, object> data)
        {
            if (!data.TryGetValue("galaxy", out var gal) || !(gal is Dictionary<string, object> gd)) return;
            if (gd.TryGetValue("galaxy_seed",        out var gs))  Station.galaxySeed        = Convert.ToInt32(gs);
            if (gd.TryGetValue("exploration_points", out var ep))  Station.explorationPoints = Convert.ToInt32(ep);
            ReadList(gd, "charted_system_seeds", v => Station.chartedSystemSeeds.Add(Convert.ToInt32(v)));
            if (gd.TryGetValue("sectors", out var secRaw) && secRaw is List<object> secList)
                foreach (var sr in secList)
                    if (sr is Dictionary<string, object> sd)
                    {
                        string uid = sd.TryGetValue("uid", out var uv) ? uv?.ToString() : null;
                        if (uid == null || !Station.sectors.TryGetValue(uid, out var sec)) continue;
                        if (sd.TryGetValue("proper_name", out var pn))  sec.properName = pn?.ToString();
                        if (sd.TryGetValue("is_renamed",  out var ir))  sec.isRenamed  = Convert.ToBoolean(ir);
                        if (sd.TryGetValue("discovery",   out var dv) && Enum.TryParse(dv?.ToString(), out SectorDiscoveryState disc)) sec.discoveryState = disc;
                    }
            ReadObjList(gd, "exploration_datachips", cd => {
                var chip = new ExplorationDatachipInstance
                {
                    uid                 = cd.TryGetValue("uid",                   out var uv)  ? uv?.ToString()        : null,
                    systemName          = cd.TryGetValue("system_name",           out var snv) ? snv?.ToString()       : null,
                    systemSeed          = cd.TryGetValue("system_seed",           out var ssv) ? Convert.ToInt32(ssv)  : 0,
                    holderFoundationUid = cd.TryGetValue("holder_foundation_uid", out var hfv) ? hfv?.ToString()       : null,
                    installedInServer   = cd.TryGetValue("installed_in_server",   out var isv) && Convert.ToBoolean(isv),
                };
                if (!string.IsNullOrEmpty(chip.uid)) Station.explorationDatachips[chip.uid] = chip;
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // NPC serialization
        // ─────────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> SerializeNpc(NPCInstance n)
        {
            var d = new Dictionary<string, object>
            {
                { "uid",          n.uid },
                { "template_id",  n.templateId },
                { "name",         n.name },
                { "class_id",     n.classId },
                { "subclass_id",  n.subclassId },
                { "faction_id",   n.factionId },
                { "location",     n.location },
                { "species",      n.species },
                { "rank",         n.rank },
                { "mood",         n.mood },
                { "mood_score",   n.moodScore },
                { "stress_score", n.stressScore },
                { "work_modifier",n.workModifier },
                { "in_crisis",    n.inCrisis },
                { "injuries",     n.injuries },
                { "social_skill", n.socialSkill },
                { "department_id",n.departmentId },
                { "sleep_bed_uid",n.sleepBedUid },
                { "is_sleeping",  n.isSleeping },
                { "mission_uid",  n.missionUid },
                { "assigned_ship_uid", n.assignedShipUid },
                { "current_job_id",    n.currentJobId },
                { "job_module_uid",    n.jobModuleUid },
                { "job_timer",         n.jobTimer },
                { "job_interrupted",   n.jobInterrupted },
                { "current_task_id",   n.currentTaskId },
                { "path_target_col",   n.pathTargetCol },
                { "path_target_row",   n.pathTargetRow },
                { "is_pathing",        n.isPathing },
                { "trait_slots",       n.traitSlots },
                { "age_days",          n.ageDays },
                { "mother_id",         n.motherId },
                { "father_id",         n.fatherId },
                { "last_conversation_tick", n.lastConversationTick },
                { "trait_work_modifier",    n.traitWorkModifier },
                { "tension_work_modifier",  n.tensionWorkModifier },
                { "proximity_work_modifier",              n.proximityWorkModifier },
                { "proximity_work_modifier_expires_at",   n.proximityWorkModifierExpiresAtTick },
                { "expertise_modifier",                   n.expertiseModifier },
                { "ability_score_points",                 n.abilityScorePoints },
            };

            // Collections
            SaveIntDictToField(d, "skills",        n.skills);
            d["traits"]               = ToObjList(n.traits);
            d["status_tags"]          = ToObjList(n.statusTags);
            d["sibling_ids"]          = ToObjList(n.siblingIds);
            d["chosen_expertise"]     = ToObjList(n.chosenExpertise);
            d["pending_expertise_skill_ids"] = ToObjList(n.pendingExpertiseSkillIds);
            d["life_stage"]           = n.lifeStage.ToString();

            // Needs legacy dict
            var needsOut = new Dictionary<string, object>();
            foreach (var kv in n.needs) needsOut[kv.Key] = kv.Value;
            d["needs"] = needsOut;

            // Need profiles
            d["sleep_need"]       = SerializeSleepNeed(n.sleepNeed);
            d["hunger_need"]      = SerializeHungerNeed(n.hungerNeed);
            d["thirst_need"]      = SerializeThirstNeed(n.thirstNeed);
            d["recreation_need"]  = SerializeRecreationNeed(n.recreationNeed);
            d["social_need"]      = SerializeSocialNeed(n.socialNeed);
            d["hygiene_need"]     = SerializeHygieneNeed(n.hygieneNeed);

            // Depletion rates
            if (n.needDepletionRates != null)
            {
                var ndr = new Dictionary<string, object>();
                foreach (var kv in n.needDepletionRates) ndr[kv.Key] = kv.Value;
                d["need_depletion_rates"] = ndr;
            }

            d["sanity"]        = n.sanity        != null ? SerializeSanity(n.sanity)       : null;
            d["trait_profile"] = n.traitProfile  != null ? SerializeTraitProfile(n.traitProfile) : null;
            d["ability_scores"] = SerializeAbilityScores(n.abilityScores);

            // Skill instances
            var siOut = new List<object>();
            foreach (var si in n.skillInstances) siOut.Add(SerializeSkillInstance(si));
            d["skill_instances"] = siOut;

            // Mood modifiers
            var mmOut = new List<object>();
            foreach (var mm in n.moodModifiers) mmOut.Add(SerializeMoodModifier(mm));
            d["mood_modifiers"] = mmOut;
            var smOut = new List<object>();
            foreach (var mm in n.stressModifiers) smOut.Add(SerializeMoodModifier(mm));
            d["stress_modifiers"] = smOut;

            // Schedule
            if (n.npcSchedule != null)
            {
                var schedOut = new List<object>();
                foreach (var slot in n.npcSchedule) schedOut.Add((int)slot);
                d["npc_schedule"] = schedOut;
            }

            // Equipped / pocket items
            var eqOut = new Dictionary<string, object>();
            foreach (var kv in n.equippedSlots) eqOut[kv.Key] = kv.Value;
            d["equipped_slots"] = eqOut;
            SaveIntDictToField(d, "pocket_items", n.pocketItems);

            // Medical profile
            d["medical_profile"] = n.medicalProfile != null ? SerializeMedicalProfile(n.medicalProfile) : null;

            // Memory hooks (only string/int/float/bool values survive round-trip via MiniJSON)
            var memOut = new Dictionary<string, object>();
            foreach (var kv in n.memory)
                if (kv.Value == null || kv.Value is string || kv.Value is bool || kv.Value is int || kv.Value is float || kv.Value is double || kv.Value is long)
                    memOut[kv.Key] = kv.Value;
            d["memory"] = memOut;

            // Skill XP legacy dict
            var xpOut = new Dictionary<string, object>();
            foreach (var kv in n.skillXp) xpOut[kv.Key] = kv.Value;
            d["skill_xp"] = xpOut;

            return d;
        }

        private static NPCInstance DeserializeNpc(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var n = new NPCInstance
            {
                uid          = Str(d, "uid"),
                templateId   = Str(d, "template_id"),
                name         = Str(d, "name"),
                classId      = Str(d, "class_id"),
                subclassId   = Str(d, "subclass_id"),
                factionId    = Str(d, "faction_id"),
                location     = Str(d, "location") ?? "commons",
                species      = Str(d, "species") ?? "human",
                rank         = Int(d, "rank"),
                mood         = Flt(d, "mood"),
                moodScore    = Flt(d, "mood_score", 50f),
                stressScore  = Flt(d, "stress_score", 50f),
                workModifier = Flt(d, "work_modifier", 1f),
                inCrisis     = Bool(d, "in_crisis"),
                injuries     = Int(d, "injuries"),
                socialSkill  = Int(d, "social_skill", 1),
                departmentId = Str(d, "department_id"),
                sleepBedUid  = Str(d, "sleep_bed_uid"),
                isSleeping   = Bool(d, "is_sleeping"),
                missionUid   = Str(d, "mission_uid"),
                assignedShipUid   = Str(d, "assigned_ship_uid"),
                currentJobId      = Str(d, "current_job_id"),
                jobModuleUid      = Str(d, "job_module_uid"),
                jobTimer          = Int(d, "job_timer"),
                jobInterrupted    = Bool(d, "job_interrupted"),
                currentTaskId     = Str(d, "current_task_id"),
                pathTargetCol     = Int(d, "path_target_col", -1),
                pathTargetRow     = Int(d, "path_target_row", -1),
                isPathing         = Bool(d, "is_pathing"),
                traitSlots        = Int(d, "trait_slots", 3),
                ageDays           = Int(d, "age_days"),
                motherId          = Str(d, "mother_id"),
                fatherId          = Str(d, "father_id"),
                lastConversationTick = Int(d, "last_conversation_tick", -99),
                traitWorkModifier   = Flt(d, "trait_work_modifier", 1f),
                tensionWorkModifier = Flt(d, "tension_work_modifier", 1f),
                proximityWorkModifier             = Flt(d, "proximity_work_modifier", 1f),
                proximityWorkModifierExpiresAtTick = Int(d, "proximity_work_modifier_expires_at", -1),
                expertiseModifier  = Flt(d, "expertise_modifier", 1f),
                abilityScorePoints = Int(d, "ability_score_points"),
            };

            if (Enum.TryParse(Str(d, "life_stage") ?? "Adult", out LifeStage ls)) n.lifeStage = ls;

            ReadStrings(d, "traits",                    n.traits);
            ReadStrings(d, "status_tags",               n.statusTags);
            ReadStrings(d, "sibling_ids",               n.siblingIds);
            ReadStrings(d, "chosen_expertise",          n.chosenExpertise);
            ReadStrings(d, "pending_expertise_skill_ids", n.pendingExpertiseSkillIds);

            // Needs legacy dict
            if (d.TryGetValue("needs", out var ndv) && ndv is Dictionary<string, object> ndDict)
                foreach (var kv in ndDict) n.needs[kv.Key] = Convert.ToSingle(kv.Value);

            // Need profiles
            if (d.TryGetValue("sleep_need",      out var snp) && snp is Dictionary<string, object> snpd) n.sleepNeed      = DeserializeSleepNeed(snpd);
            if (d.TryGetValue("hunger_need",     out var hnp) && hnp is Dictionary<string, object> hnpd) n.hungerNeed     = DeserializeHungerNeed(hnpd);
            if (d.TryGetValue("thirst_need",     out var tnp) && tnp is Dictionary<string, object> tnpd) n.thirstNeed     = DeserializeThirstNeed(tnpd);
            if (d.TryGetValue("recreation_need", out var rnp) && rnp is Dictionary<string, object> rnpd) n.recreationNeed = DeserializeRecreationNeed(rnpd);
            if (d.TryGetValue("social_need",     out var snp2) && snp2 is Dictionary<string, object> snp2d) n.socialNeed   = DeserializeSocialNeed(snp2d);
            if (d.TryGetValue("hygiene_need",    out var hyp) && hyp is Dictionary<string, object> hypd) n.hygieneNeed    = DeserializeHygieneNeed(hypd);

            if (d.TryGetValue("need_depletion_rates", out var ndr) && ndr is Dictionary<string, object> ndrDict)
            {
                n.needDepletionRates = new Dictionary<string, float>();
                foreach (var kv in ndrDict) n.needDepletionRates[kv.Key] = Convert.ToSingle(kv.Value);
            }

            if (d.TryGetValue("sanity",        out var san) && san is Dictionary<string, object> sand) n.sanity       = DeserializeSanity(sand);
            if (d.TryGetValue("trait_profile", out var tp)  && tp  is Dictionary<string, object> tpd)  n.traitProfile = DeserializeTraitProfile(tpd);
            if (d.TryGetValue("ability_scores",out var ab)  && ab  is Dictionary<string, object> abd)  n.abilityScores = DeserializeAbilityScores(abd);

            if (d.TryGetValue("skill_instances", out var siRaw) && siRaw is List<object> siList)
                foreach (var si in siList)
                    if (si is Dictionary<string, object> sid) { var s = DeserializeSkillInstance(sid); if (s != null) n.skillInstances.Add(s); }

            if (d.TryGetValue("mood_modifiers", out var mmRaw) && mmRaw is List<object> mmList)
                foreach (var mm in mmList)
                    if (mm is Dictionary<string, object> mmd) { var m = DeserializeMoodModifier(mmd); if (m != null) n.moodModifiers.Add(m); }

            if (d.TryGetValue("stress_modifiers", out var smRaw) && smRaw is List<object> smList)
                foreach (var sm in smList)
                    if (sm is Dictionary<string, object> smd) { var m = DeserializeMoodModifier(smd); if (m != null) n.stressModifiers.Add(m); }

            if (d.TryGetValue("npc_schedule", out var schedRaw) && schedRaw is List<object> schedList)
            {
                n.npcSchedule = new ScheduleSlot[schedList.Count];
                for (int i = 0; i < schedList.Count; i++)
                    n.npcSchedule[i] = (ScheduleSlot)Convert.ToInt32(schedList[i]);
            }

            if (d.TryGetValue("equipped_slots", out var eqRaw) && eqRaw is Dictionary<string, object> eqDict)
                foreach (var kv in eqDict) n.equippedSlots[kv.Key] = kv.Value?.ToString();

            if (d.TryGetValue("pocket_items", out var piRaw) && piRaw is Dictionary<string, object> piDict)
                foreach (var kv in piDict) n.pocketItems[kv.Key] = Convert.ToInt32(kv.Value);

            if (d.TryGetValue("medical_profile", out var mp) && mp is Dictionary<string, object> mpd)
                n.medicalProfile = DeserializeMedicalProfile(mpd);

            if (d.TryGetValue("memory", out var memRaw) && memRaw is Dictionary<string, object> memDict)
                foreach (var kv in memDict) n.memory[kv.Key] = kv.Value;

            if (d.TryGetValue("skill_xp", out var xpRaw) && xpRaw is Dictionary<string, object> xpDict)
                foreach (var kv in xpDict) n.skillXp[kv.Key] = Convert.ToSingle(kv.Value);

            if (d.TryGetValue("skills", out var skRaw) && skRaw is Dictionary<string, object> skDict)
                foreach (var kv in skDict) n.skills[kv.Key] = Convert.ToInt32(kv.Value);

            return n;
        }

        private static Dictionary<string, object> SerializeSleepNeed(SleepNeedProfile p) => p == null ? null : new Dictionary<string, object>
            { { "value", p.value }, { "is_seeking", p.isSeeking }, { "assigned_bed_id", p.assignedBedId }, { "well_rested_ticks", p.wellRestedTicks } };
        private static SleepNeedProfile DeserializeSleepNeed(Dictionary<string, object> d) =>
            d == null ? new SleepNeedProfile() : new SleepNeedProfile { value = Flt(d,"value",100f), isSeeking = Bool(d,"is_seeking"), assignedBedId = Str(d,"assigned_bed_id"), wellRestedTicks = Int(d,"well_rested_ticks") };

        private static Dictionary<string, object> SerializeHungerNeed(HungerNeedProfile p) => p == null ? null : new Dictionary<string, object>
            { { "value", p.value }, { "is_seeking", p.isSeeking }, { "nourishment_debt_ticks", p.nourishmentDebtTicks }, { "nourishment_recovery_ticks", p.nourishmentRecoveryTicks }, { "is_malnourished", p.isMalnourished }, { "starvation_day_count", p.starvationDayCount } };
        private static HungerNeedProfile DeserializeHungerNeed(Dictionary<string, object> d) =>
            d == null ? new HungerNeedProfile() : new HungerNeedProfile { value = Flt(d,"value",100f), isSeeking = Bool(d,"is_seeking"), nourishmentDebtTicks = Int(d,"nourishment_debt_ticks"), nourishmentRecoveryTicks = Int(d,"nourishment_recovery_ticks"), isMalnourished = Bool(d,"is_malnourished"), starvationDayCount = Int(d,"starvation_day_count") };

        private static Dictionary<string, object> SerializeThirstNeed(ThirstNeedProfile p) => p == null ? null : new Dictionary<string, object>
            { { "value", p.value }, { "is_seeking", p.isSeeking }, { "dehydration_day_count", p.dehydrationDayCount } };
        private static ThirstNeedProfile DeserializeThirstNeed(Dictionary<string, object> d) =>
            d == null ? new ThirstNeedProfile() : new ThirstNeedProfile { value = Flt(d,"value",100f), isSeeking = Bool(d,"is_seeking"), dehydrationDayCount = Int(d,"dehydration_day_count") };

        private static Dictionary<string, object> SerializeRecreationNeed(RecreationNeedProfile p) => p == null ? null : new Dictionary<string, object>
            { { "value", p.value }, { "is_burnt_out", p.isBurntOut } };
        private static RecreationNeedProfile DeserializeRecreationNeed(Dictionary<string, object> d) =>
            d == null ? new RecreationNeedProfile() : new RecreationNeedProfile { value = Flt(d,"value",100f), isBurntOut = Bool(d,"is_burnt_out") };

        private static Dictionary<string, object> SerializeSocialNeed(SocialNeedProfile p) => p == null ? null : new Dictionary<string, object>
            { { "value", p.value }, { "is_reclusive", p.isReclusive }, { "last_interaction_tick", p.lastInteractionTick } };
        private static SocialNeedProfile DeserializeSocialNeed(Dictionary<string, object> d) =>
            d == null ? new SocialNeedProfile() : new SocialNeedProfile { value = Flt(d,"value",50f), isReclusive = Bool(d,"is_reclusive"), lastInteractionTick = Int(d,"last_interaction_tick",-1) };

        private static Dictionary<string, object> SerializeHygieneNeed(HygieneNeedProfile p) => p == null ? null : new Dictionary<string, object>
            { { "value", p.value }, { "is_seeking", p.isSeeking }, { "in_crisis", p.inCrisis } };
        private static HygieneNeedProfile DeserializeHygieneNeed(Dictionary<string, object> d) =>
            d == null ? new HygieneNeedProfile() : new HygieneNeedProfile { value = Flt(d,"value",100f), isSeeking = Bool(d,"is_seeking"), inCrisis = Bool(d,"in_crisis") };

        private static Dictionary<string, object> SerializeSanity(SanityProfile p) => p == null ? null : new Dictionary<string, object>
            { { "score", p.score }, { "ceiling", p.ceiling }, { "daily_mood_accumulator", p.dailyMoodAccumulator }, { "daily_mood_sample_count", p.dailyMoodSampleCount }, { "need_depleted_this_cycle", p.needDepletedThisCycle }, { "needs_above_50_count", p.needsAbove50Count }, { "is_in_breakdown", p.isInBreakdown }, { "requires_intervention", p.requiresIntervention } };
        private static SanityProfile DeserializeSanity(Dictionary<string, object> d) => d == null ? null : new SanityProfile
            { score = Int(d,"score"), ceiling = Int(d,"ceiling"), dailyMoodAccumulator = Flt(d,"daily_mood_accumulator"), dailyMoodSampleCount = Int(d,"daily_mood_sample_count"), needDepletedThisCycle = Bool(d,"need_depleted_this_cycle"), needsAbove50Count = Int(d,"needs_above_50_count"), isInBreakdown = Bool(d,"is_in_breakdown"), requiresIntervention = Bool(d,"requires_intervention") };

        private static Dictionary<string, object> SerializeAbilityScores(AbilityScores a) => a == null ? null : new Dictionary<string, object>
            { { "STR", a.STR }, { "DEX", a.DEX }, { "INT", a.INT }, { "WIS", a.WIS }, { "CHA", a.CHA }, { "END", a.END } };
        private static AbilityScores DeserializeAbilityScores(Dictionary<string, object> d) => d == null ? new AbilityScores() : new AbilityScores
            { STR = Int(d,"STR",8), DEX = Int(d,"DEX",8), INT = Int(d,"INT",8), WIS = Int(d,"WIS",8), CHA = Int(d,"CHA",8), END = Int(d,"END",8) };

        private static Dictionary<string, object> SerializeSkillInstance(SkillInstance s) => s == null ? null : new Dictionary<string, object>
            { { "skill_id", s.skillId }, { "current_xp", s.currentXP }, { "daily_xp_accumulated", s.dailyXPAccumulated }, { "daily_xp_day", s.dailyXPDay } };
        private static SkillInstance DeserializeSkillInstance(Dictionary<string, object> d) => d == null ? null : new SkillInstance
            { skillId = Str(d,"skill_id"), currentXP = Flt(d,"current_xp"), dailyXPAccumulated = Flt(d,"daily_xp_accumulated"), dailyXPDay = Int(d,"daily_xp_day",-1) };

        private static Dictionary<string, object> SerializeMoodModifier(MoodModifierRecord m) => m == null ? null : new Dictionary<string, object>
            { { "event_id", m.eventId }, { "delta", m.delta }, { "expires_at_tick", m.expiresAtTick }, { "source", m.source } };
        private static MoodModifierRecord DeserializeMoodModifier(Dictionary<string, object> d) => d == null ? null : new MoodModifierRecord
            { eventId = Str(d,"event_id"), delta = Flt(d,"delta"), expiresAtTick = Int(d,"expires_at_tick",-1), source = Str(d,"source") };

        private static Dictionary<string, object> SerializeTraitProfile(NpcTraitProfile p)
        {
            if (p == null) return null;
            var traitsOut = new List<object>();
            foreach (var t in p.traits) traitsOut.Add(new Dictionary<string, object> { { "trait_id", t.traitId }, { "strength", t.strength }, { "acquisition_tick", t.acquisitionTick } });
            var cpOut = new Dictionary<string, object>(); foreach (var kv in p.conditionPressure) cpOut[kv.Key] = kv.Value;
            var lpOut = new Dictionary<string, object>(); foreach (var kv in p.lineagePositions)   lpOut[kv.Key] = kv.Value;
            var lcOut = new Dictionary<string, object>(); foreach (var kv in p.lineageCooldownEndTick) lcOut[kv.Key] = kv.Value;
            var d = new Dictionary<string, object>
            {
                { "traits", traitsOut }, { "condition_pressure", cpOut }, { "tension_score", p.tensionScore },
                { "tension_stage", p.tensionStage.ToString() }, { "lineage_positions", lpOut }, { "lineage_cooldown_end_tick", lcOut },
            };
            if (p.departure != null)
                d["departure"] = new Dictionary<string, object> { { "announced", p.departure.announced }, { "announced_at_tick", p.departure.announcedAtTick }, { "intervention_deadline_tick", p.departure.interventionDeadlineTick } };
            return d;
        }

        private static NpcTraitProfile DeserializeTraitProfile(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var p = new NpcTraitProfile();
            if (Enum.TryParse(Str(d,"tension_stage") ?? "Normal", out TensionStage ts)) p.tensionStage = ts;
            p.tensionScore = Flt(d, "tension_score");
            if (d.TryGetValue("traits", out var tr) && tr is List<object> tl)
                foreach (var t in tl) if (t is Dictionary<string, object> td) p.traits.Add(new ActiveTrait { traitId = Str(td,"trait_id"), strength = Flt(td,"strength",1f), acquisitionTick = Int(td,"acquisition_tick") });
            if (d.TryGetValue("condition_pressure", out var cp) && cp is Dictionary<string, object> cpd) foreach (var kv in cpd) p.conditionPressure[kv.Key] = Convert.ToSingle(kv.Value);
            if (d.TryGetValue("lineage_positions",  out var lp) && lp is Dictionary<string, object> lpd) foreach (var kv in lpd) p.lineagePositions[kv.Key]   = Convert.ToInt32(kv.Value);
            if (d.TryGetValue("lineage_cooldown_end_tick", out var lc) && lc is Dictionary<string, object> lcd) foreach (var kv in lcd) p.lineageCooldownEndTick[kv.Key] = Convert.ToInt32(kv.Value);
            if (d.TryGetValue("departure", out var dep) && dep is Dictionary<string, object> depd)
                p.departure = new DepartureAnnouncementState { announced = Bool(depd,"announced"), announcedAtTick = Int(depd,"announced_at_tick"), interventionDeadlineTick = Int(depd,"intervention_deadline_tick") };
            return p;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Medical profile serialization
        // ─────────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> SerializeMedicalProfile(MedicalProfile p)
        {
            if (p == null) return null;
            var partsOut = new Dictionary<string, object>();
            foreach (var kv in p.parts) partsOut[kv.Key] = SerializeBodyPart(kv.Value);
            return new Dictionary<string, object>
            {
                { "blood_volume",                p.bloodVolume },
                { "pain",                        p.pain },
                { "consciousness",               p.consciousness },
                { "is_unconscious",              p.isUnconscious },
                { "lung_death_ticks_remaining",  p.lungDeathTicksRemaining },
                { "kidney_death_ticks_remaining",p.kidneyDeathTicksRemaining },
                { "species_id",                  p.speciesId },
                { "analgesic_strength",          p.analgesicStrength },
                { "analgesic_duration_ticks",    p.analgesicDurationTicks },
                { "antibiotics_strength",        p.antibioticsStrength },
                { "antibiotics_duration_ticks",  p.antibioticsDurationTicks },
                { "parts", partsOut },
                { "penalties", SerializePenalties(p.penalties) },
            };
        }

        private static MedicalProfile DeserializeMedicalProfile(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var p = new MedicalProfile
            {
                bloodVolume               = Flt(d, "blood_volume", 100f),
                pain                      = Flt(d, "pain"),
                consciousness             = Flt(d, "consciousness", 100f),
                isUnconscious             = Bool(d, "is_unconscious"),
                lungDeathTicksRemaining   = Int(d, "lung_death_ticks_remaining"),
                kidneyDeathTicksRemaining = Int(d, "kidney_death_ticks_remaining"),
                speciesId                 = Str(d, "species_id") ?? "human",
                analgesicStrength         = Flt(d, "analgesic_strength"),
                analgesicDurationTicks    = Int(d, "analgesic_duration_ticks"),
                antibioticsStrength       = Flt(d, "antibiotics_strength"),
                antibioticsDurationTicks  = Int(d, "antibiotics_duration_ticks"),
            };
            if (d.TryGetValue("parts", out var prtsRaw) && prtsRaw is Dictionary<string, object> prtsDict)
                foreach (var kv in prtsDict)
                    if (kv.Value is Dictionary<string, object> pd) p.parts[kv.Key] = DeserializeBodyPart(pd);
            if (d.TryGetValue("penalties", out var penRaw) && penRaw is Dictionary<string, object> pend)
                p.penalties = DeserializePenalties(pend);
            return p;
        }

        private static Dictionary<string, object> SerializeBodyPart(BodyPart bp)
        {
            if (bp == null) return null;
            var woundsOut = new List<object>();
            foreach (var w in bp.wounds) woundsOut.Add(SerializeWound(w));
            var scarsOut = new List<object>();
            foreach (var s in bp.scars) scarsOut.Add(SerializeScar(s));
            var diseasesOut = new List<object>();
            foreach (var dd in bp.diseases) diseasesOut.Add(SerializeActiveDisease(dd));
            return new Dictionary<string, object>
            {
                { "part_id",     bp.partId },
                { "health",      bp.health },
                { "is_amputated",bp.isAmputated },
                { "wounds",      woundsOut },
                { "scars",       scarsOut },
                { "diseases",    diseasesOut },
            };
        }

        private static BodyPart DeserializeBodyPart(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var bp = new BodyPart { partId = Str(d,"part_id"), health = Flt(d,"health",100f), isAmputated = Bool(d,"is_amputated") };
            if (d.TryGetValue("wounds",   out var wr) && wr is List<object> wl)   foreach (var w in wl)   if (w is Dictionary<string,object> wd)   { var wound   = DeserializeWound(wd);       if (wound   != null) bp.wounds.Add(wound); }
            if (d.TryGetValue("scars",    out var sr) && sr is List<object> sl)    foreach (var s in sl)   if (s is Dictionary<string,object> sd)   { var scar    = DeserializeScar(sd);        if (scar    != null) bp.scars.Add(scar); }
            if (d.TryGetValue("diseases", out var dr) && dr is List<object> dl)   foreach (var dd in dl)  if (dd is Dictionary<string,object> ddd) { var disease = DeserializeActiveDisease(ddd); if (disease != null) bp.diseases.Add(disease); }
            return bp;
        }

        private static Dictionary<string, object> SerializeWound(Wound w) => w == null ? null : new Dictionary<string, object>
            { { "uid", w.uid }, { "type", w.type.ToString() }, { "severity", w.severity.ToString() }, { "is_treated", w.isTreated }, { "bleed_rate", w.bleedRatePerTick }, { "pain_contribution", w.painContribution }, { "healing_progress", w.healingProgress }, { "infection_accumulation", w.infectionAccumulation }, { "is_infected", w.isInfected }, { "created_at_tick", w.createdAtTick }, { "treatment_quality", w.treatmentQuality }, { "healing_rate_multiplier", w.healingRateMultiplier } };
        private static Wound DeserializeWound(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var w = new Wound { uid = Str(d,"uid"), isTreated = Bool(d,"is_treated"), bleedRatePerTick = Flt(d,"bleed_rate"), painContribution = Flt(d,"pain_contribution"), healingProgress = Flt(d,"healing_progress"), infectionAccumulation = Flt(d,"infection_accumulation"), isInfected = Bool(d,"is_infected"), createdAtTick = Int(d,"created_at_tick"), treatmentQuality = Flt(d,"treatment_quality"), healingRateMultiplier = Flt(d,"healing_rate_multiplier",1f) };
            if (Enum.TryParse(Str(d,"type")     ?? "Laceration",  out WoundType wt))    w.type     = wt;
            if (Enum.TryParse(Str(d,"severity") ?? "Minor",       out WoundSeverity ws)) w.severity = ws;
            return w;
        }

        private static Dictionary<string, object> SerializeScar(Scar s) => s == null ? null : new Dictionary<string, object>
            { { "uid", s.uid }, { "source_wound_type", s.sourceWoundType.ToString() }, { "source_severity", s.sourceSeverity.ToString() }, { "part_id", s.partId }, { "coverage_tag", s.coverageTag }, { "functional_penalty", s.functionalPenalty } };
        private static Scar DeserializeScar(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var s = new Scar { uid = Str(d,"uid"), partId = Str(d,"part_id"), coverageTag = Str(d,"coverage_tag"), functionalPenalty = Flt(d,"functional_penalty") };
            if (Enum.TryParse(Str(d,"source_wound_type") ?? "Laceration", out WoundType wt))    s.sourceWoundType = wt;
            if (Enum.TryParse(Str(d,"source_severity")   ?? "Minor",      out WoundSeverity ws)) s.sourceSeverity  = ws;
            return s;
        }

        private static Dictionary<string, object> SerializeActiveDisease(ActiveDisease ad) => ad == null ? null : new Dictionary<string, object>
            { { "uid", ad.uid }, { "disease_id", ad.diseaseId }, { "display_name", ad.displayName }, { "current_stage", ad.currentStage }, { "ticks_in_stage", ad.ticksInStage }, { "affected_part_ids", ToObjList(ad.affectedPartIds) }, { "is_chronic", ad.isChronic }, { "immunity_expires_at_tick", ad.immunityExpiresAtTick } };
        private static ActiveDisease DeserializeActiveDisease(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var ad = new ActiveDisease { uid = Str(d,"uid"), diseaseId = Str(d,"disease_id"), displayName = Str(d,"display_name"), currentStage = Int(d,"current_stage"), ticksInStage = Int(d,"ticks_in_stage"), isChronic = Bool(d,"is_chronic"), immunityExpiresAtTick = Int(d,"immunity_expires_at_tick") };
            ReadStrings(d, "affected_part_ids", ad.affectedPartIds);
            return ad;
        }

        private static Dictionary<string, object> SerializePenalties(FunctionalPenaltyProfile p) => p == null ? null : new Dictionary<string, object>
            { { "locomotion", p.locomotionModifier }, { "manipulation", p.manipulationModifier }, { "sight", p.sightModifier }, { "hearing", p.hearingModifier }, { "respiration", p.respirationModifier }, { "circulation", p.circulationModifier }, { "digestion", p.digestionModifier }, { "organ_function", p.organFunctionModifier }, { "scar_penalty_accumulator", p.scarPenaltyAccumulator } };
        private static FunctionalPenaltyProfile DeserializePenalties(Dictionary<string, object> d) => d == null ? new FunctionalPenaltyProfile() : new FunctionalPenaltyProfile
            { locomotionModifier = Flt(d,"locomotion",1f), manipulationModifier = Flt(d,"manipulation",1f), sightModifier = Flt(d,"sight",1f), hearingModifier = Flt(d,"hearing",1f), respirationModifier = Flt(d,"respiration",1f), circulationModifier = Flt(d,"circulation",1f), digestionModifier = Flt(d,"digestion",1f), organFunctionModifier = Flt(d,"organ_function",1f), scarPenaltyAccumulator = Flt(d,"scar_penalty_accumulator") };

        // ─────────────────────────────────────────────────────────────────────
        // Foundation serialization
        // ─────────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> SerializeFoundation(FoundationInstance f)
        {
            if (f == null) return null;
            var cargoOut = new Dictionary<string, object>(); foreach (var kv in f.cargo) cargoOut[kv.Key] = kv.Value;
            var hauledOut = new Dictionary<string, object>(); foreach (var kv in f.hauledMaterials) hauledOut[kv.Key] = kv.Value;
            var repHauledOut = new Dictionary<string, object>(); foreach (var kv in f.repairHauledMaterials) repHauledOut[kv.Key] = kv.Value;
            return new Dictionary<string, object>
            {
                { "uid",             f.uid }, { "buildable_id", f.buildableId }, { "tile_col", f.tileCol }, { "tile_row", f.tileRow },
                { "tile_rotation",   f.tileRotation }, { "status", f.status }, { "build_progress", f.buildProgress },
                { "max_health",      f.maxHealth }, { "health", f.health }, { "quality", f.quality },
                { "door_status",     f.doorStatus }, { "door_hold_open", f.doorHoldOpen },
                { "assigned_npc_uid", f.assignedNpcUid }, { "pending_repair", f.pendingRepair },
                { "repair_assigned_npc_uid", f.repairAssignedNpcUid }, { "repair_progress", f.repairProgress },
                { "cargo_capacity",  f.cargoCapacity }, { "tile_layer", f.tileLayer }, { "tile_width", f.tileWidth }, { "tile_height", f.tileHeight },
                { "network_id",      f.networkId }, { "is_under_wall", f.isUnderWall }, { "operating_state", f.operatingState },
                { "stored_energy",   f.storedEnergy }, { "stored_fluid", f.storedFluid }, { "stored_gas", f.storedGas }, { "stored_fuel", f.storedFuel },
                { "isolator_open",   f.isolatorOpen },
                { "crop_id",         f.cropId }, { "growth_stage", f.growthStage }, { "growth_progress", f.growthProgress },
                { "crop_damage",     f.cropDamage }, { "last_tended_tick", f.lastTendedTick }, { "neglect_accumulator", f.neglectAccumulator },
                { "pest_accumulator",f.pestAccumulator }, { "has_blight", f.hasBlight }, { "blight_detected", f.blightDetected }, { "blight_ticks", f.blightTicks },
                { "has_pests",       f.hasPests }, { "pests_detected", f.pestsDetected }, { "pest_ticks", f.pestTicks },
                { "target_temperature", f.targetTemperature },
                { "relay_branch_filter", ToObjList(f.relayBranchFilter) },
                { "cargo",           cargoOut }, { "hauled_materials", hauledOut }, { "repair_hauled_materials", repHauledOut },
                { "access_policy",   f.accessPolicy != null ? SerializeDoorAccessPolicy(f.accessPolicy) : null },
                { "cargo_settings",  f.cargoSettings != null ? SerializeCargoHoldSettings(f.cargoSettings) : null },
            };
        }

        private static FoundationInstance DeserializeFoundation(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var f = new FoundationInstance
            {
                uid          = Str(d,"uid"), buildableId = Str(d,"buildable_id"),
                tileCol      = Int(d,"tile_col"), tileRow = Int(d,"tile_row"), tileRotation = Int(d,"tile_rotation"),
                status       = Str(d,"status") ?? "awaiting_haul", buildProgress = Flt(d,"build_progress"),
                maxHealth    = Int(d,"max_health",100), health = Int(d,"health",100), quality = Flt(d,"quality",1f),
                doorStatus   = Str(d,"door_status") ?? "powered", doorHoldOpen = Bool(d,"door_hold_open"),
                assignedNpcUid = Str(d,"assigned_npc_uid"), pendingRepair = Bool(d,"pending_repair"),
                repairAssignedNpcUid = Str(d,"repair_assigned_npc_uid"), repairProgress = Flt(d,"repair_progress"),
                cargoCapacity = Int(d,"cargo_capacity"), tileLayer = Int(d,"tile_layer",1), tileWidth = Int(d,"tile_width",1), tileHeight = Int(d,"tile_height",1),
                networkId    = Str(d,"network_id"), isUnderWall = Bool(d,"is_under_wall"), operatingState = Str(d,"operating_state") ?? "standby",
                storedEnergy = Flt(d,"stored_energy"), storedFluid = Flt(d,"stored_fluid"), storedGas = Flt(d,"stored_gas"), storedFuel = Flt(d,"stored_fuel"),
                isolatorOpen = d.TryGetValue("isolator_open", out var io) ? Convert.ToBoolean(io) : true,
                cropId       = Str(d,"crop_id"), growthStage = Int(d,"growth_stage"), growthProgress = Flt(d,"growth_progress"),
                cropDamage   = Flt(d,"crop_damage"), lastTendedTick = Int(d,"last_tended_tick",-1), neglectAccumulator = Int(d,"neglect_accumulator"),
                pestAccumulator = Int(d,"pest_accumulator"), hasBlight = Bool(d,"has_blight"), blightDetected = Bool(d,"blight_detected"), blightTicks = Int(d,"blight_ticks"),
                hasPests     = Bool(d,"has_pests"), pestsDetected = Bool(d,"pests_detected"), pestTicks = Int(d,"pest_ticks"),
                targetTemperature = Flt(d,"target_temperature",20f),
            };
            ReadStrings(d, "relay_branch_filter", f.relayBranchFilter);
            if (d.TryGetValue("cargo",           out var cg)  && cg  is Dictionary<string,object> cgd)  foreach (var kv in cgd)  f.cargo[kv.Key]               = Convert.ToInt32(kv.Value);
            if (d.TryGetValue("hauled_materials", out var hm)  && hm  is Dictionary<string,object> hmd)  foreach (var kv in hmd)  f.hauledMaterials[kv.Key]     = Convert.ToInt32(kv.Value);
            if (d.TryGetValue("repair_hauled_materials", out var rhm) && rhm is Dictionary<string,object> rhmd) foreach (var kv in rhmd) f.repairHauledMaterials[kv.Key] = Convert.ToInt32(kv.Value);
            if (d.TryGetValue("access_policy",   out var ap)  && ap  is Dictionary<string,object> apd)  f.accessPolicy   = DeserializeDoorAccessPolicy(apd);
            if (d.TryGetValue("cargo_settings",  out var cs)  && cs  is Dictionary<string,object> csd)  f.cargoSettings  = DeserializeCargoHoldSettings(csd);
            return f;
        }

        private static Dictionary<string, object> SerializeDoorAccessPolicy(DoorAccessPolicy p) => p == null ? null : new Dictionary<string, object>
            { { "allow_all", p.allowAll }, { "allowed_species", ToObjList(p.allowedSpecies) }, { "allowed_department_ids", ToObjList(p.allowedDepartmentIds) }, { "min_rank", p.minRank }, { "required_faction_id", p.requiredFactionId }, { "min_faction_rep", p.minFactionRep } };
        private static DoorAccessPolicy DeserializeDoorAccessPolicy(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var p = new DoorAccessPolicy { allowAll = Bool(d,"allow_all",true), minRank = Int(d,"min_rank"), requiredFactionId = Str(d,"required_faction_id"), minFactionRep = Flt(d,"min_faction_rep") };
            ReadStrings(d, "allowed_species",        p.allowedSpecies);
            ReadStrings(d, "allowed_department_ids", p.allowedDepartmentIds);
            return p;
        }

        private static Dictionary<string, object> SerializeCargoHoldSettings(CargoHoldSettings s)
        {
            if (s == null) return null;
            var rbtOut = new Dictionary<string, object>();
            foreach (var kv in s.reservedByType) rbtOut[kv.Key] = kv.Value;
            return new Dictionary<string, object>
                { { "allowed_types", ToObjList(s.allowedTypes) }, { "priority", s.priority }, { "reserved_by_type", rbtOut } };
        }
        private static CargoHoldSettings DeserializeCargoHoldSettings(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var s = new CargoHoldSettings { priority = Int(d,"priority") };
            ReadStrings(d, "allowed_types", s.allowedTypes);
            if (d.TryGetValue("reserved_by_type", out var rbt) && rbt is Dictionary<string,object> rbtd) foreach (var kv in rbtd) s.reservedByType[kv.Key] = Convert.ToSingle(kv.Value);
            return s;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Ship / OwnedShip / Mission / AsteroidMap / POI serialization
        // ─────────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> SerializeShip(ShipInstance s)
        {
            if (s == null) return null;
            var cargoOut = new Dictionary<string, object>(); foreach (var kv in s.cargo) cargoOut[kv.Key] = kv.Value;
            return new Dictionary<string, object>
            {
                { "uid", s.uid }, { "template_id", s.templateId }, { "name", s.name }, { "role", s.role }, { "faction_id", s.factionId },
                { "intent", s.intent }, { "threat_level", s.threatLevel }, { "status", s.status }, { "docked_at", s.dockedAt },
                { "ticks_docked", s.ticksDocked }, { "planned_departure_tick", s.plannedDepartureTick },
                { "visit_state", s.visitState.ToString() }, { "world_x", s.worldX }, { "world_y", s.worldY },
                { "drift_target_x", s.driftTargetX }, { "drift_target_y", s.driftTargetY }, { "in_range_since_tick", s.inRangeSinceTick }, { "shuttle_uid", s.shuttleUid },
                { "cargo", cargoOut }, { "passenger_uids", ToObjList(s.passengerUids) }, { "behavior_tags", ToObjList(s.behaviorTags) },
            };
        }
        private static ShipInstance DeserializeShip(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var s = new ShipInstance { uid = Str(d,"uid"), templateId = Str(d,"template_id"), name = Str(d,"name"), role = Str(d,"role"), factionId = Str(d,"faction_id"),
                intent = Str(d,"intent") ?? "unknown", threatLevel = Int(d,"threat_level"), status = Str(d,"status") ?? "incoming", dockedAt = Str(d,"docked_at"),
                ticksDocked = Int(d,"ticks_docked"), plannedDepartureTick = Int(d,"planned_departure_tick",-1),
                worldX = Flt(d,"world_x"), worldY = Flt(d,"world_y"), driftTargetX = Flt(d,"drift_target_x"), driftTargetY = Flt(d,"drift_target_y"),
                inRangeSinceTick = Int(d,"in_range_since_tick",-1), shuttleUid = Str(d,"shuttle_uid") };
            if (Enum.TryParse(Str(d,"visit_state") ?? "OutOfRange", out ShipVisitState vs)) s.visitState = vs;
            if (d.TryGetValue("cargo", out var cg) && cg is Dictionary<string,object> cgd) foreach (var kv in cgd) s.cargo[kv.Key] = Convert.ToInt32(kv.Value);
            ReadStrings(d, "passenger_uids", s.passengerUids);
            ReadStrings(d, "behavior_tags",  s.behaviorTags);
            return s;
        }

        private static Dictionary<string, object> SerializeOwnedShip(OwnedShipInstance s) => s == null ? null : new Dictionary<string, object>
            { { "uid", s.uid }, { "template_id", s.templateId }, { "name", s.name }, { "role", s.role }, { "status", s.status }, { "condition_pct", s.conditionPct }, { "damage_state", s.damageState.ToString() }, { "mission_uid", s.missionUid }, { "mission_type", s.missionType }, { "mission_start_tick", s.missionStartTick }, { "mission_end_tick", s.missionEndTick }, { "crew_uids", ToObjList(s.crewUids) } };
        private static OwnedShipInstance DeserializeOwnedShip(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var s = new OwnedShipInstance { uid = Str(d,"uid"), templateId = Str(d,"template_id"), name = Str(d,"name"), role = Str(d,"role"), status = Str(d,"status") ?? "docked", conditionPct = Flt(d,"condition_pct",100f), missionUid = Str(d,"mission_uid"), missionType = Str(d,"mission_type"), missionStartTick = Int(d,"mission_start_tick"), missionEndTick = Int(d,"mission_end_tick") };
            if (Enum.TryParse(Str(d,"damage_state") ?? "Undamaged", out ShipDamageState ds)) s.damageState = ds;
            ReadStrings(d, "crew_uids", s.crewUids);
            return s;
        }

        private static Dictionary<string, object> SerializeMission(MissionInstance m)
        {
            if (m == null) return null;
            var rewardsOut = new Dictionary<string, object>(); foreach (var kv in m.rewards) rewardsOut[kv.Key] = kv.Value;
            return new Dictionary<string, object>
            { { "uid", m.uid }, { "mission_type", m.missionType }, { "display_name", m.displayName }, { "definition_id", m.definitionId }, { "start_tick", m.startTick }, { "end_tick", m.endTick }, { "status", m.status }, { "target_system_seed", m.targetSystemSeed }, { "target_system_name", m.targetSystemName }, { "crew_uids", ToObjList(m.crewUids) }, { "rewards", rewardsOut } };
        }
        private static MissionInstance DeserializeMission(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var m = new MissionInstance { uid = Str(d,"uid"), missionType = Str(d,"mission_type"), displayName = Str(d,"display_name"), definitionId = Str(d,"definition_id"), startTick = Int(d,"start_tick"), endTick = Int(d,"end_tick"), status = Str(d,"status") ?? "active", targetSystemSeed = Int(d,"target_system_seed"), targetSystemName = Str(d,"target_system_name") };
            ReadStrings(d, "crew_uids", m.crewUids);
            if (d.TryGetValue("rewards", out var rw) && rw is Dictionary<string,object> rwd) foreach (var kv in rwd) m.rewards[kv.Key] = Convert.ToSingle(kv.Value);
            return m;
        }

        private static Dictionary<string, object> SerializeAsteroidMap(AsteroidMapState am)
        {
            if (am == null) return null;
            var extractedOut = new Dictionary<string, object>(); foreach (var kv in am.extractedResources) extractedOut[kv.Key] = kv.Value;
            return new Dictionary<string, object>
            { { "uid", am.uid }, { "poi_uid", am.poiUid }, { "width", am.width }, { "height", am.height }, { "seed", am.seed },
              { "tiles", Convert.ToBase64String(am.tiles) }, { "mission_uid", am.missionUid }, { "status", am.status }, { "start_tick", am.startTick }, { "end_tick", am.endTick },
              { "retreat_ordered", am.retreatOrdered }, { "distress_signal_active", am.distressSignalActive }, { "distress_window_expiry_tick", am.distressWindowExpiryTick }, { "rescue_dispatched", am.rescueDispatched }, { "threat_level", am.threatLevel },
              { "extracted_resources", extractedOut }, { "assigned_npc_uids", ToObjList(am.assignedNpcUids) } };
        }
        private static AsteroidMapState DeserializeAsteroidMap(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var tilesStr = Str(d,"tiles");
            var am = new AsteroidMapState { uid = Str(d,"uid"), poiUid = Str(d,"poi_uid"), width = Int(d,"width"), height = Int(d,"height"), seed = Int(d,"seed"), tiles = tilesStr != null ? Convert.FromBase64String(tilesStr) : new byte[0], missionUid = Str(d,"mission_uid"), status = Str(d,"status") ?? "active", startTick = Int(d,"start_tick"), endTick = Int(d,"end_tick"), retreatOrdered = Bool(d,"retreat_ordered"), distressSignalActive = Bool(d,"distress_signal_active"), distressWindowExpiryTick = Int(d,"distress_window_expiry_tick",-1), rescueDispatched = Bool(d,"rescue_dispatched"), threatLevel = Flt(d,"threat_level") };
            if (d.TryGetValue("extracted_resources", out var er) && er is Dictionary<string,object> erd) foreach (var kv in erd) am.extractedResources[kv.Key] = Convert.ToInt32(kv.Value);
            ReadStrings(d, "assigned_npc_uids", am.assignedNpcUids);
            return am;
        }

        private static Dictionary<string, object> SerializePoi(PointOfInterest p)
        {
            if (p == null) return null;
            var yieldOut = new Dictionary<string, object>(); foreach (var kv in p.resourceYield) yieldOut[kv.Key] = kv.Value;
            return new Dictionary<string, object> { { "uid", p.uid }, { "poi_type", p.poiType }, { "display_name", p.displayName }, { "pos_x", p.posX }, { "pos_y", p.posY }, { "discovered", p.discovered }, { "visited", p.visited }, { "seed", p.seed }, { "resource_yield", yieldOut } };
        }
        private static PointOfInterest DeserializePoi(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var p = new PointOfInterest { uid = Str(d,"uid"), poiType = Str(d,"poi_type"), displayName = Str(d,"display_name"), posX = Flt(d,"pos_x"), posY = Flt(d,"pos_y"), discovered = Bool(d,"discovered"), visited = Bool(d,"visited"), seed = Int(d,"seed") };
            if (d.TryGetValue("resource_yield", out var ry) && ry is Dictionary<string,object> ryd) foreach (var kv in ryd) p.resourceYield[kv.Key] = Convert.ToInt32(kv.Value);
            return p;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Relationship / Message / Department / Body / FarmingTask / Crafting
        // ─────────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> SerializeRelationship(RelationshipRecord r) => r == null ? null : new Dictionary<string, object>
            { { "npc_uid1", r.npcUid1 }, { "npc_uid2", r.npcUid2 }, { "affinity_score", r.affinityScore }, { "relationship_type", r.relationshipType.ToString() }, { "last_interaction_tick", r.lastInteractionTick }, { "married", r.married }, { "marriage_event_pending", r.marriageEventPending }, { "last_marriage_event_tick", r.lastMarriageEventTick }, { "mentor_uid", r.mentorUid }, { "co_working_ticks", r.coWorkingTicks }, { "last_co_working_tick", r.lastCoWorkingTick } };
        private static RelationshipRecord DeserializeRelationship(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var r = new RelationshipRecord { npcUid1 = Str(d,"npc_uid1"), npcUid2 = Str(d,"npc_uid2"), affinityScore = Flt(d,"affinity_score"), lastInteractionTick = Int(d,"last_interaction_tick"), married = Bool(d,"married"), marriageEventPending = Bool(d,"marriage_event_pending"), lastMarriageEventTick = Int(d,"last_marriage_event_tick",-1), mentorUid = Str(d,"mentor_uid"), coWorkingTicks = Int(d,"co_working_ticks"), lastCoWorkingTick = Int(d,"last_co_working_tick",-1) };
            if (Enum.TryParse(Str(d,"relationship_type") ?? "None", out RelationshipType rt)) r.relationshipType = rt;
            return r;
        }

        private static Dictionary<string, object> SerializeMessage(CommMessage m)
        {
            if (m == null) return null;
            var opts = new List<object>();
            foreach (var opt in m.responseOptions)
            {
                var o = new Dictionary<string, object>();
                foreach (var kv in opt) o[kv.Key] = kv.Value;
                opts.Add(o);
            }
            return new Dictionary<string, object> { { "uid", m.uid }, { "subject", m.subject }, { "body", m.body }, { "sender_name", m.senderName }, { "sender_type", m.senderType }, { "ship_uid", m.shipUid }, { "read", m.read }, { "tick", m.tick }, { "replied", m.replied }, { "expires_at_tick", m.expiresAtTick }, { "response_options", opts } };
        }
        private static CommMessage DeserializeMessage(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var m = new CommMessage { uid = Str(d,"uid"), subject = Str(d,"subject"), body = Str(d,"body"), senderName = Str(d,"sender_name"), senderType = Str(d,"sender_type"), shipUid = Str(d,"ship_uid"), read = Bool(d,"read"), tick = Int(d,"tick"), replied = Str(d,"replied"), expiresAtTick = Int(d,"expires_at_tick",-1) };
            if (d.TryGetValue("response_options", out var roRaw) && roRaw is List<object> roList)
                foreach (var ro in roList)
                    if (ro is Dictionary<string,object> rod) { var opt = new Dictionary<string,object>(); foreach (var kv in rod) opt[kv.Key] = kv.Value; m.responseOptions.Add(opt); }
            return m;
        }

        private static Dictionary<string, object> SerializeDepartment(Department dept) => dept == null ? null : new Dictionary<string, object>
            { { "uid", dept.uid }, { "name", dept.name }, { "colour_hex", dept.colourHex }, { "secondary_colour_hex", dept.secondaryColourHex }, { "head_npc_uid", dept.headNpcUid }, { "allowed_jobs", ToObjList(dept.allowedJobs) } };
        private static Department DeserializeDepartment(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var dept = new Department { uid = Str(d,"uid"), name = Str(d,"name"), colourHex = Str(d,"colour_hex"), secondaryColourHex = Str(d,"secondary_colour_hex"), headNpcUid = Str(d,"head_npc_uid") };
            ReadStrings(d, "allowed_jobs", dept.allowedJobs);
            return dept;
        }

        private static Dictionary<string, object> SerializeBody(BodyInstance b) => b == null ? null : new Dictionary<string, object>
            { { "uid", b.uid }, { "npc_uid", b.npcUid }, { "npc_name", b.npcName }, { "tile_col", b.tileCol }, { "tile_row", b.tileRow }, { "location", b.location }, { "spawned_at_tick", b.spawnedAtTick }, { "haul_task_generated", b.haulTaskGenerated }, { "haul_blocked", b.haulBlocked }, { "hauler_npc_uid", b.haulerNpcUid }, { "haul_job_timer", b.haulJobTimer }, { "escalation_step", b.escalationStep } };
        private static BodyInstance DeserializeBody(Dictionary<string, object> d) => d == null ? null : new BodyInstance { uid = Str(d,"uid"), npcUid = Str(d,"npc_uid"), npcName = Str(d,"npc_name"), tileCol = Int(d,"tile_col"), tileRow = Int(d,"tile_row"), location = Str(d,"location"), spawnedAtTick = Int(d,"spawned_at_tick"), haulTaskGenerated = Bool(d,"haul_task_generated"), haulBlocked = Bool(d,"haul_blocked"), haulerNpcUid = Str(d,"hauler_npc_uid"), haulJobTimer = Int(d,"haul_job_timer"), escalationStep = Int(d,"escalation_step") };

        private static Dictionary<string, object> SerializeFarmingTask(FarmingTaskInstance t) => t == null ? null : new Dictionary<string, object>
            { { "uid", t.uid }, { "task_type", t.taskType }, { "planter_uid", t.planterUid }, { "crop_id", t.cropId }, { "assigned_npc_uid", t.assignedNpcUid }, { "status", t.status }, { "progress_ticks", t.progressTicks } };
        private static FarmingTaskInstance DeserializeFarmingTask(Dictionary<string, object> d) => d == null ? null : new FarmingTaskInstance { uid = Str(d,"uid"), taskType = Str(d,"task_type"), planterUid = Str(d,"planter_uid"), cropId = Str(d,"crop_id"), assignedNpcUid = Str(d,"assigned_npc_uid"), status = Str(d,"status") ?? "pending", progressTicks = Int(d,"progress_ticks") };

        private static Dictionary<string, object> SerializeWorkbenchQueueEntry(WorkbenchQueueEntry e)
        {
            if (e == null) return null;
            var hmOut = new Dictionary<string, object>(); foreach (var kv in e.hauledMaterials) hmOut[kv.Key] = kv.Value;
            return new Dictionary<string, object> { { "uid", e.uid }, { "recipe_id", e.recipeId }, { "status", e.status }, { "assigned_npc_uid", e.assignedNpcUid }, { "execution_progress", e.executionProgress }, { "output_quality_tier", e.outputQualityTier }, { "hauled_materials", hmOut } };
        }
        private static WorkbenchQueueEntry DeserializeWorkbenchQueueEntry(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var e = new WorkbenchQueueEntry { uid = Str(d,"uid"), recipeId = Str(d,"recipe_id"), status = Str(d,"status") ?? "queued", assignedNpcUid = Str(d,"assigned_npc_uid"), executionProgress = Flt(d,"execution_progress"), outputQualityTier = Str(d,"output_quality_tier") ?? "standard" };
            if (d.TryGetValue("hauled_materials", out var hm) && hm is Dictionary<string,object> hmd) foreach (var kv in hmd) e.hauledMaterials[kv.Key] = Convert.ToInt32(kv.Value);
            return e;
        }

        private static Dictionary<string, object> SerializeStandingOrder(StandingOrder so) => so == null ? null : new Dictionary<string, object>
            { { "resource", so.resource }, { "limit_price", so.limitPrice }, { "amount", so.amount } };
        private static StandingOrder DeserializeStandingOrder(Dictionary<string, object> d) => d == null ? null : new StandingOrder { resource = Str(d,"resource"), limitPrice = Flt(d,"limit_price"), amount = Flt(d,"amount") };

        private static Dictionary<string, object> SerializeFactionContract(FactionContract fc) => fc == null ? null : new Dictionary<string, object>
            { { "contract_id", fc.contractId }, { "faction_id", fc.factionId }, { "credit_per_payment", fc.creditPerPayment }, { "payment_interval_ticks", fc.paymentIntervalTicks }, { "last_payment_tick", fc.lastPaymentTick }, { "description", fc.description } };
        private static FactionContract DeserializeFactionContract(Dictionary<string, object> d) => d == null ? null : new FactionContract { contractId = Str(d,"contract_id"), factionId = Str(d,"faction_id"), creditPerPayment = Flt(d,"credit_per_payment"), paymentIntervalTicks = Int(d,"payment_interval_ticks"), lastPaymentTick = Int(d,"last_payment_tick"), description = Str(d,"description") };

        // ─────────────────────────────────────────────────────────────────────
        // Region / Network / SolarSystem / GeneratedFaction serialization
        // ─────────────────────────────────────────────────────────────────────

        private static Dictionary<string, object> SerializeRegion(RegionData r)
        {
            if (r == null) return null;
            var rh = r.resourceHistory;
            var dailyAmountsOut = new Dictionary<string, object>();
            if (rh != null) foreach (var kv in rh.dailyAmounts) { var vals = new List<object>(); foreach (var v in kv.Value) vals.Add(v); dailyAmountsOut[kv.Key] = vals; }
            var baselinesOut = new Dictionary<string, object>();
            if (rh != null) foreach (var kv in rh.baselines) baselinesOut[kv.Key] = kv.Value;
            return new Dictionary<string, object>
            { { "region_id", r.regionId }, { "display_name", r.displayName }, { "simulation_state", r.simulationState.ToString() }, { "conflict_level", r.conflictLevel }, { "population_density", r.populationDensity },
              { "faction_ids", ToObjList(r.factionIds) }, { "daily_amounts", dailyAmountsOut }, { "baselines", baselinesOut } };
        }
        private static RegionData DeserializeRegion(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var r = new RegionData { regionId = Str(d,"region_id"), displayName = Str(d,"display_name"), conflictLevel = Flt(d,"conflict_level"), populationDensity = Flt(d,"population_density",0.5f) };
            if (Enum.TryParse(Str(d,"simulation_state") ?? "Undiscovered", out RegionSimulationState ss)) r.simulationState = ss;
            ReadStrings(d, "faction_ids", r.factionIds);
            if (d.TryGetValue("daily_amounts", out var daRaw) && daRaw is Dictionary<string,object> daDict)
                foreach (var kv in daDict) if (kv.Value is List<object> vals) { var list = new List<float>(); foreach (var v in vals) list.Add(Convert.ToSingle(v)); r.resourceHistory.dailyAmounts[kv.Key] = list; }
            if (d.TryGetValue("baselines", out var blRaw) && blRaw is Dictionary<string,object> blDict)
                foreach (var kv in blDict) r.resourceHistory.baselines[kv.Key] = Convert.ToSingle(kv.Value);
            return r;
        }

        private static Dictionary<string, object> SerializeNetwork(NetworkInstance n) => n == null ? null : new Dictionary<string, object>
            { { "uid", n.uid }, { "network_type", n.networkType }, { "content_type", n.contentType }, { "content_amount", n.contentAmount }, { "content_capacity", n.contentCapacity }, { "member_uids", ToObjList(n.memberUids) } };
        private static NetworkInstance DeserializeNetwork(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var n = new NetworkInstance { uid = Str(d,"uid"), networkType = Str(d,"network_type"), contentType = Str(d,"content_type"), contentAmount = Flt(d,"content_amount"), contentCapacity = Flt(d,"content_capacity") };
            ReadStrings(d, "member_uids", n.memberUids);
            return n;
        }

        private static Dictionary<string, object> SerializeGeneratedFaction(FactionDefinition f)
        {
            if (f == null) return null;
            var relOut = new Dictionary<string, object>(); foreach (var kv in f.relationships) relOut[kv.Key] = kv.Value;
            return new Dictionary<string, object>
            { { "id", f.id }, { "display_name", f.displayName }, { "type", f.type }, { "description", f.description }, { "ideology_tags", ToObjList(f.ideologyTags) }, { "behavior_tags", ToObjList(f.behaviorTags) }, { "government_type", f.governmentType.ToString() }, { "succession_state", f.successionState.ToString() }, { "stability_score", f.stabilityScore }, { "government_tenure_ticks", f.governmentTenureTicks }, { "member_npc_ids", ToObjList(f.memberNpcIds) }, { "leader_npc_ids", ToObjList(f.leaderNpcIds) }, { "vassal_parent_faction_id", f.vassalParentFactionId }, { "relationships", relOut } };
        }
        private static FactionDefinition DeserializeGeneratedFaction(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var f = new FactionDefinition { id = Str(d,"id"), displayName = Str(d,"display_name"), type = Str(d,"type") ?? "minor", description = Str(d,"description"), stabilityScore = Flt(d,"stability_score",50f), governmentTenureTicks = Int(d,"government_tenure_ticks"), vassalParentFactionId = Str(d,"vassal_parent_faction_id") };
            if (Enum.TryParse(Str(d,"government_type") ?? "Democracy",   out GovernmentType gt)) f.governmentType   = gt;
            if (Enum.TryParse(Str(d,"succession_state") ?? "Stable",     out SuccessionState ss)) f.successionState = ss;
            ReadStrings(d, "ideology_tags",  f.ideologyTags);
            ReadStrings(d, "behavior_tags",  f.behaviorTags);
            ReadStrings(d, "member_npc_ids", f.memberNpcIds);
            ReadStrings(d, "leader_npc_ids", f.leaderNpcIds);
            if (d.TryGetValue("relationships", out var relRaw) && relRaw is Dictionary<string,object> relDict) foreach (var kv in relDict) f.relationships[kv.Key] = Convert.ToSingle(kv.Value);
            return f;
        }

        private static Dictionary<string, object> SerializeSolarSystem(SolarSystemState ss)
        {
            if (ss == null) return null;
            var bodiesOut = new List<object>(); foreach (var b in ss.bodies) bodiesOut.Add(SerializeSolarBody(b));
            return new Dictionary<string, object> { { "star_name", ss.starName }, { "system_name", ss.systemName }, { "seed", ss.seed }, { "star_type", ss.starType.ToString() }, { "star_color_hex", ss.starColorHex }, { "star_size", ss.starSize }, { "station_orbit_index", ss.stationOrbitIndex }, { "bodies", bodiesOut } };
        }
        private static SolarSystemState DeserializeSolarSystem(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var ss = new SolarSystemState { starName = Str(d,"star_name"), systemName = Str(d,"system_name"), seed = Int(d,"seed"), starColorHex = Str(d,"star_color_hex"), starSize = Flt(d,"star_size"), stationOrbitIndex = Int(d,"station_orbit_index",-1) };
            if (Enum.TryParse(Str(d,"star_type") ?? "YellowDwarf", out StarType st)) ss.starType = st;
            if (d.TryGetValue("bodies", out var bRaw) && bRaw is List<object> bList) foreach (var b in bList) if (b is Dictionary<string,object> bd) { var body = DeserializeSolarBody(bd); if (body != null) ss.bodies.Add(body); }
            return ss;
        }
        private static Dictionary<string, object> SerializeSolarBody(SolarBody b)
        {
            if (b == null) return null;
            var moonsOut = new List<object>(); foreach (var m in b.moons) moonsOut.Add(SerializeSolarBody(m));
            return new Dictionary<string, object> { { "name", b.name }, { "body_type", b.bodyType.ToString() }, { "planet_class", b.planetClass.ToString() }, { "orbital_radius", b.orbitalRadius }, { "orbital_period", b.orbitalPeriod }, { "initial_phase", b.initialPhase }, { "size", b.size }, { "color_hex", b.colorHex }, { "has_rings", b.hasRings }, { "station_is_here", b.stationIsHere }, { "tags", ToObjList(b.tags) }, { "moons", moonsOut } };
        }
        private static SolarBody DeserializeSolarBody(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var b = new SolarBody { name = Str(d,"name"), orbitalRadius = Flt(d,"orbital_radius"), orbitalPeriod = Flt(d,"orbital_period"), initialPhase = Flt(d,"initial_phase"), size = Flt(d,"size"), colorHex = Str(d,"color_hex"), hasRings = Bool(d,"has_rings"), stationIsHere = Bool(d,"station_is_here") };
            if (Enum.TryParse(Str(d,"body_type")    ?? "RockyPlanet", out BodyType     bt)) b.bodyType    = bt;
            if (Enum.TryParse(Str(d,"planet_class") ?? "None",        out PlanetClass pc)) b.planetClass = pc;
            ReadStrings(d, "tags", b.tags);
            if (d.TryGetValue("moons", out var mRaw) && mRaw is List<object> mList) foreach (var m in mList) if (m is Dictionary<string,object> md) { var moon = DeserializeSolarBody(md); if (moon != null) b.moons.Add(moon); }
            return b;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Utility helpers — save dict writers and type-safe readers
        // ─────────────────────────────────────────────────────────────────────

        private static List<object> SerializeAll<T>(IEnumerable<T> src, Func<T, Dictionary<string, object>> fn)
        {
            var out_ = new List<object>();
            foreach (var item in src) { var d = fn(item); if (d != null) out_.Add(d); }
            return out_;
        }

        private static void DeserializeIntoDict<TVal>(Dictionary<string, object> data, string key,
            Dictionary<string, TVal> target, Func<Dictionary<string, object>, TVal> fn, Func<TVal, string> getKey)
        {
            if (!data.TryGetValue(key, out var raw) || !(raw is List<object> list)) return;
            foreach (var item in list)
                if (item is Dictionary<string, object> d) { var v = fn(d); if (v != null) target[getKey(v)] = v; }
        }

        private static void SaveDict(Dictionary<string, object> data, string key, Dictionary<string, string> src)
        { var out_ = new Dictionary<string, object>(); foreach (var kv in src) out_[kv.Key] = kv.Value; data[key] = out_; }

        private static void SaveDict(Dictionary<string, object> data, string key, Dictionary<string, float> src)
        { var out_ = new Dictionary<string, object>(); foreach (var kv in src) out_[kv.Key] = kv.Value; data[key] = out_; }

        private static void SaveFloatDict(Dictionary<string, object> data, string key, Dictionary<string, float> src)
        { var out_ = new Dictionary<string, object>(); foreach (var kv in src) out_[kv.Key] = kv.Value; data[key] = out_; }

        private static void SaveIntDict(Dictionary<string, object> data, string key, Dictionary<string, int> src)
        { var out_ = new Dictionary<string, object>(); foreach (var kv in src) out_[kv.Key] = kv.Value; data[key] = out_; }

        private static void SaveBoolDict(Dictionary<string, object> data, string key, Dictionary<string, bool> src)
        { var out_ = new Dictionary<string, object>(); foreach (var kv in src) out_[kv.Key] = kv.Value; data[key] = out_; }

        private static void SaveSet(Dictionary<string, object> data, string key, HashSet<string> src)
        { var out_ = new List<object>(); foreach (var v in src) out_.Add(v); data[key] = out_; }

        private static void SaveStringList(Dictionary<string, object> data, string key, List<string> src)
        { var out_ = new List<object>(); foreach (var v in src) out_.Add(v); data[key] = out_; }

        // Converts a List<string> (or any IEnumerable<string>) to List<object> for MiniJSON serialization.
        private static List<object> ToObjList(System.Collections.Generic.IEnumerable<string> src)
        { var out_ = new List<object>(); foreach (var v in src) out_.Add(v); return out_; }

        private static void SaveIntDictToField(Dictionary<string, object> d, string key, Dictionary<string, int> src)
        { var out_ = new Dictionary<string, object>(); foreach (var kv in src) out_[kv.Key] = kv.Value; d[key] = out_; }

        private static void ReadDict(Dictionary<string, object> data, string key, Action<KeyValuePair<string, object>> action)
        {
            if (data.TryGetValue(key, out var raw) && raw is Dictionary<string, object> dict)
                foreach (var kv in dict) action(kv);
        }

        private static void ReadList(Dictionary<string, object> data, string key, Action<object> action)
        {
            if (data.TryGetValue(key, out var raw) && raw is List<object> list)
                foreach (var v in list) action(v);
        }

        private static void ReadObjList(Dictionary<string, object> data, string key, Action<Dictionary<string, object>> action)
        {
            if (data.TryGetValue(key, out var raw) && raw is List<object> list)
                foreach (var v in list) if (v is Dictionary<string, object> d) action(d);
        }

        private static void ReadStrings(Dictionary<string, object> data, string key, List<string> target)
        {
            if (data.TryGetValue(key, out var raw) && raw is List<object> list)
                foreach (var v in list) if (v != null) target.Add(v.ToString());
        }

        // ── Primitive getters ────────────────────────────────────────────────
        private static string Str(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) ? v?.ToString() : null;
        private static int Int(Dictionary<string, object> d, string k, int def = 0) =>
            d.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v) : def;
        private static float Flt(Dictionary<string, object> d, string k, float def = 0f) =>
            d.TryGetValue(k, out var v) && v != null ? Convert.ToSingle(v) : def;
        private static bool Bool(Dictionary<string, object> d, string k, bool def = false) =>
            d.TryGetValue(k, out var v) && v != null ? Convert.ToBoolean(v) : def;
    }
}
