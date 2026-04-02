// ShipSystem tests — EditMode unit tests (EXP-003).
// Validates:
//   • Ship role eligibility flags for all five role types
//   • NPC need depletion during fleet mission ticks
//   • Ship destruction crew outcome resolution for all three outcomes
//   • Full ship acquisition pipeline (AddShipToFleet)
//   • Ship dispatch and mission lifecycle
//   • Damage state thresholds and repair logic
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Common helpers ────────────────────────────────────────────────────────

    internal static class ShipTestHelpers
    {
        public static StationState MakeStation(float credits = 1000f)
        {
            var s = new StationState("FleetTest");
            s.resources["credits"] = credits;
            s.resources["food"]    = 100f;
            s.resources["power"]   = 100f;
            s.resources["oxygen"]  = 100f;
            s.resources["parts"]   = 50f;
            s.resources["ice"]     = 200f;
            s.resources["fuel"]    = 50f;
            return s;
        }

        public static NPCInstance MakeCrew(string name = "Test NPC")
        {
            var npc = NPCInstance.Create("npc.test", name, "class.general");
            npc.statusTags.Add("crew");
            npc.hungerNeed     = new HungerNeedProfile   { value = 100f };
            npc.thirstNeed     = new ThirstNeedProfile   { value = 100f };
            npc.sleepNeed      = new SleepNeedProfile    { value = 100f };
            npc.socialNeed     = new SocialNeedProfile   { value = 100f };
            npc.recreationNeed = new RecreationNeedProfile { value = 100f };
            npc.hygieneNeed    = new HygieneNeedProfile  { value = 100f };
            return npc;
        }

        /// <summary>
        /// Create a ContentRegistry MonoBehaviour on a temporary GameObject
        /// and populate it with the supplied ship templates.
        /// Call Object.DestroyImmediate(go) in TearDown.
        /// </summary>
        public static (ContentRegistry registry, GameObject go) MakeRegistryWithShips(
            params ShipTemplate[] templates)
        {
            var go  = new GameObject("ShipTestRegistry");
            var reg = go.AddComponent<ContentRegistry>();
            foreach (var t in templates)
                reg.Ships[t.id] = t;
            return (reg, go);
        }

        public static ShipTemplate ScoutTemplate(int crewCapacity = 3) => new ShipTemplate
        {
            id                  = "ship.scout_vessel",
            role                = "scout",
            crewCapacity        = crewCapacity,
            eligibleMissionTypes = new List<string> { "scout", "exploration" },
        };

        public static ShipTemplate MiningTemplate(int crewCapacity = 6) => new ShipTemplate
        {
            id                  = "ship.mining_barge",
            role                = "mining",
            crewCapacity        = crewCapacity,
            eligibleMissionTypes = new List<string> { "mining", "asteroid" },
        };

        public static ShipTemplate CombatTemplate(int crewCapacity = 8) => new ShipTemplate
        {
            id                  = "ship.combat_frigate",
            role                = "combat",
            crewCapacity        = crewCapacity,
            eligibleMissionTypes = new List<string> { "combat", "defence", "patrol" },
        };

        public static ShipTemplate TransportTemplate(int crewCapacity = 4) => new ShipTemplate
        {
            id                  = "ship.transport_hauler",
            role                = "transport",
            crewCapacity        = crewCapacity,
            eligibleMissionTypes = new List<string> { "transport", "cargo", "hauling" },
        };

        public static ShipTemplate DiplomaticTemplate(int crewCapacity = 4) => new ShipTemplate
        {
            id                  = "ship.diplomatic_courier",
            role                = "diplomatic",
            crewCapacity        = crewCapacity,
            eligibleMissionTypes = new List<string> { "diplomatic", "courier", "trade" },
        };
    }

    // =========================================================================
    // Unit: ship role eligibility for all five roles
    // =========================================================================

    [TestFixture]
    public class ShipRoleEligibilityTests
    {
        private ContentRegistry _registry;
        private ShipSystem      _system;
        private StationState    _station;
        private GameObject      _registryGo;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;

            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                ShipTestHelpers.ScoutTemplate(),
                ShipTestHelpers.MiningTemplate(),
                ShipTestHelpers.CombatTemplate(),
                ShipTestHelpers.TransportTemplate(),
                ShipTestHelpers.DiplomaticTemplate());

            _system  = new ShipSystem(_registry);
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            Object.DestroyImmediate(_registryGo);
        }

        // Helper: place a ship in ownedShips with one crew member
        private OwnedShipInstance AddShipWithCrew(string templateId, string role)
        {
            var ship = OwnedShipInstance.Create(templateId, "Test " + role, role);
            _station.ownedShips[ship.uid] = ship;
            var npc = ShipTestHelpers.MakeCrew();
            _station.npcs[npc.uid] = npc;
            ship.crewUids.Add(npc.uid);
            npc.assignedShipUid = ship.uid;
            return ship;
        }

        // ── Scout ─────────────────────────────────────────────────────────────

        [Test]
        public void Scout_EligibleFor_ScoutMission()
        {
            var ship = AddShipWithCrew("ship.scout_vessel", "scout");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "scout", _station));
        }

        [Test]
        public void Scout_EligibleFor_ExplorationMission()
        {
            var ship = AddShipWithCrew("ship.scout_vessel", "scout");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "exploration", _station));
        }

        [Test]
        public void Scout_NotEligibleFor_CargoMission()
        {
            var ship = AddShipWithCrew("ship.scout_vessel", "scout");
            Assert.IsFalse(_system.IsEligibleForMission(ship.uid, "cargo", _station));
        }

        // ── Mining ────────────────────────────────────────────────────────────

        [Test]
        public void Mining_EligibleFor_MiningMission()
        {
            var ship = AddShipWithCrew("ship.mining_barge", "mining");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "mining", _station));
        }

        [Test]
        public void Mining_EligibleFor_AsteroidMission()
        {
            var ship = AddShipWithCrew("ship.mining_barge", "mining");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "asteroid", _station));
        }

        [Test]
        public void Mining_NotEligibleFor_ScoutMission()
        {
            var ship = AddShipWithCrew("ship.mining_barge", "mining");
            Assert.IsFalse(_system.IsEligibleForMission(ship.uid, "scout", _station));
        }

        // ── Combat ────────────────────────────────────────────────────────────

        [Test]
        public void Combat_EligibleFor_CombatMission()
        {
            var ship = AddShipWithCrew("ship.combat_frigate", "combat");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "combat", _station));
        }

        [Test]
        public void Combat_EligibleFor_DefenceMission()
        {
            var ship = AddShipWithCrew("ship.combat_frigate", "combat");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "defence", _station));
        }

        [Test]
        public void Combat_NotEligibleFor_TransportMission()
        {
            var ship = AddShipWithCrew("ship.combat_frigate", "combat");
            Assert.IsFalse(_system.IsEligibleForMission(ship.uid, "transport", _station));
        }

        // ── Transport ─────────────────────────────────────────────────────────

        [Test]
        public void Transport_EligibleFor_CargoMission()
        {
            var ship = AddShipWithCrew("ship.transport_hauler", "transport");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "cargo", _station));
        }

        [Test]
        public void Transport_EligibleFor_HaulingMission()
        {
            var ship = AddShipWithCrew("ship.transport_hauler", "transport");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "hauling", _station));
        }

        [Test]
        public void Transport_NotEligibleFor_CombatMission()
        {
            var ship = AddShipWithCrew("ship.transport_hauler", "transport");
            Assert.IsFalse(_system.IsEligibleForMission(ship.uid, "combat", _station));
        }

        // ── Diplomatic ────────────────────────────────────────────────────────

        [Test]
        public void Diplomatic_EligibleFor_DiplomaticMission()
        {
            var ship = AddShipWithCrew("ship.diplomatic_courier", "diplomatic");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "diplomatic", _station));
        }

        [Test]
        public void Diplomatic_EligibleFor_CourierMission()
        {
            var ship = AddShipWithCrew("ship.diplomatic_courier", "diplomatic");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "courier", _station));
        }

        [Test]
        public void Diplomatic_NotEligibleFor_MiningMission()
        {
            var ship = AddShipWithCrew("ship.diplomatic_courier", "diplomatic");
            Assert.IsFalse(_system.IsEligibleForMission(ship.uid, "mining", _station));
        }

        // ── CanDispatch ───────────────────────────────────────────────────────

        [Test]
        public void CanDispatch_Fails_WhenRoleIneligible()
        {
            var ship = AddShipWithCrew("ship.scout_vessel", "scout");
            bool ok = _system.CanDispatch(ship.uid, "cargo", _station, out string reason);
            Assert.IsFalse(ok);
            Assert.IsNotNull(reason);
        }

        [Test]
        public void CanDispatch_Fails_WhenNoCrew()
        {
            var ship = OwnedShipInstance.Create("ship.scout_vessel", "Scout", "scout");
            _station.ownedShips[ship.uid] = ship;   // no crew
            bool ok = _system.CanDispatch(ship.uid, "scout", _station, out string reason);
            Assert.IsFalse(ok);
            StringAssert.Contains("no crew", reason);
        }
    }

    // =========================================================================
    // Unit: NPC need depletion during fleet mission ticks
    // =========================================================================

    [TestFixture]
    public class FleetMissionNeedDepletionTests
    {
        private NeedSystem   _needs;
        private StationState _station;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            FeatureFlags.HygieneNeed     = true;
            _needs   = new NeedSystem();
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            FeatureFlags.HygieneNeed     = true;
        }

        [Test]
        public void CrewOnFleetMission_NeedsDeplete()
        {
            var npc = ShipTestHelpers.MakeCrew();
            npc.missionUid      = "fleet_abc123";
            npc.assignedShipUid = "ship_uid_001";
            _station.npcs[npc.uid] = npc;

            float hungerBefore = npc.hungerNeed.value;
            _needs.Tick(_station);

            Assert.Less(npc.hungerNeed.value, hungerBefore,
                "Hunger need should deplete for NPC on a fleet mission.");
        }

        [Test]
        public void CrewOnRegularMission_NeedsNotDepleted()
        {
            var npc = ShipTestHelpers.MakeCrew();
            npc.missionUid      = "regular_mission_uid";
            npc.assignedShipUid = null;
            _station.npcs[npc.uid] = npc;

            float hungerBefore = npc.hungerNeed.value;
            _needs.Tick(_station);

            Assert.AreEqual(hungerBefore, npc.hungerNeed.value, 0.001f,
                "Hunger need must NOT deplete for NPC on a regular (abstracted) mission.");
        }

        [Test]
        public void CrewNotOnMission_NeedsDeplete()
        {
            var npc = ShipTestHelpers.MakeCrew();
            npc.missionUid      = null;
            npc.assignedShipUid = null;
            _station.npcs[npc.uid] = npc;

            float hungerBefore = npc.hungerNeed.value;
            _needs.Tick(_station);

            Assert.Less(npc.hungerNeed.value, hungerBefore,
                "Hunger need should deplete for an idle crew NPC.");
        }

        [Test]
        public void CrewOnFleetMission_MultiTick_NeedsContinueToDeplete()
        {
            var npc = ShipTestHelpers.MakeCrew();
            npc.missionUid      = "fleet_abc456";
            npc.assignedShipUid = "ship_uid_002";
            _station.npcs[npc.uid] = npc;

            float initialHunger = npc.hungerNeed.value;

            for (int i = 0; i < 10; i++)
            {
                _station.tick++;
                _needs.Tick(_station);
            }

            Assert.Less(npc.hungerNeed.value, initialHunger,
                "After 10 ticks, fleet-mission NPC hunger should have depleted significantly.");
        }

        [Test]
        public void CrewOnFleetMission_HungerDepletionRateMatchesIdle()
        {
            // Fleet-mission NPC depletion rate should be the same as an idle NPC
            // (seeking is suppressed but the rate is identical).
            var idleNpc  = ShipTestHelpers.MakeCrew("Idle");
            idleNpc.missionUid = null;
            idleNpc.assignedShipUid = null;
            _station.npcs[idleNpc.uid] = idleNpc;

            var missionNpc = ShipTestHelpers.MakeCrew("OnMission");
            missionNpc.missionUid      = "fleet_test";
            missionNpc.assignedShipUid = "ship_x";
            _station.npcs[missionNpc.uid] = missionNpc;

            _needs.Tick(_station);

            Assert.AreEqual(idleNpc.hungerNeed.value, missionNpc.hungerNeed.value, 0.001f,
                "Hunger depletion rate for fleet-mission NPC should equal that of an idle NPC.");
        }
    }

    // =========================================================================
    // Unit: ship destruction and crew outcome resolution
    // =========================================================================

    [TestFixture]
    public class ShipDestructionTests
    {
        private ShipSystem   _system;
        private StationState _station;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            // Destruction tests don't call AddShipToFleet or IsEligibleForMission,
            // so a null registry is safe here.
            _system  = new ShipSystem(null);
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
        }

        private OwnedShipInstance AddShipWithCrew(
            int crewCount, ShipDamageState damageState = ShipDamageState.Moderate)
        {
            float conditionPct = damageState switch
            {
                ShipDamageState.Undamaged => 100f,
                ShipDamageState.Light     =>  85f,
                ShipDamageState.Moderate  =>  60f,
                ShipDamageState.Heavy     =>  35f,
                ShipDamageState.Critical  =>  10f,
                _                         =>  60f,
            };

            var ship = OwnedShipInstance.Create("ship.combat_frigate", "Warship Alpha", "combat");
            ship.damageState  = damageState;
            ship.conditionPct = conditionPct;
            _station.ownedShips[ship.uid] = ship;

            for (int i = 0; i < crewCount; i++)
            {
                var npc = ShipTestHelpers.MakeCrew($"Crew_{i}");
                npc.assignedShipUid = ship.uid;
                npc.missionUid      = "fleet_test";
                _station.npcs[npc.uid] = npc;
                ship.crewUids.Add(npc.uid);
            }

            return ship;
        }

        [Test]
        public void Destruction_ShipRemovedFromFleet()
        {
            var ship = AddShipWithCrew(2);
            string uid = ship.uid;
            _system.ResolveDestruction(uid, _station);
            Assert.IsFalse(_station.ownedShips.ContainsKey(uid));
        }

        [Test]
        public void Destruction_ReturnsOneResultPerCrew()
        {
            var ship    = AddShipWithCrew(4);
            var results = _system.ResolveDestruction(ship.uid, _station);
            Assert.AreEqual(4, results.Count);
        }

        [Test]
        public void Destruction_CrewMissionUidCleared()
        {
            var ship     = AddShipWithCrew(2);
            var crewUids = new List<string>(ship.crewUids);
            _system.ResolveDestruction(ship.uid, _station);

            foreach (var uid in crewUids)
            {
                if (_station.npcs.TryGetValue(uid, out var npc))
                    Assert.IsNull(npc.missionUid, $"{npc.name}: missionUid should be null after destruction.");
            }
        }

        [Test]
        public void Destruction_CrewAssignedShipUidCleared()
        {
            var ship     = AddShipWithCrew(2);
            var crewUids = new List<string>(ship.crewUids);
            _system.ResolveDestruction(ship.uid, _station);

            foreach (var uid in crewUids)
            {
                if (_station.npcs.TryGetValue(uid, out var npc))
                    Assert.IsNull(npc.assignedShipUid,
                        $"{npc.name}: assignedShipUid should be null after destruction.");
            }
        }

        [Test]
        public void Destruction_OutcomeIsOneOfThreeValues()
        {
            var ship    = AddShipWithCrew(6);
            var results = _system.ResolveDestruction(ship.uid, _station);

            foreach (var r in results)
            {
                Assert.IsTrue(
                    r.outcome == CrewDestructionOutcome.Killed   ||
                    r.outcome == CrewDestructionOutcome.Captured ||
                    r.outcome == CrewDestructionOutcome.Escaped,
                    $"Outcome must be one of the three valid values, got {r.outcome}.");
            }
        }

        [Test]
        public void Destruction_KilledCrew_HasDeadTag()
        {
            var savedRng = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(42);
                var ship    = AddShipWithCrew(20, ShipDamageState.Critical);
                var results = _system.ResolveDestruction(ship.uid, _station);

                bool anyKilled = false;
                foreach (var r in results)
                {
                    if (r.outcome != CrewDestructionOutcome.Killed) continue;
                    anyKilled = true;
                    if (_station.npcs.TryGetValue(r.npcUid, out var npc))
                        Assert.IsTrue(npc.statusTags.Contains("dead"),
                            "Killed NPC must have 'dead' status tag.");
                }

                Assert.IsTrue(anyKilled,
                    "With 20 crew and Critical damage at least one should be killed.");
            }
            finally
            {
                UnityEngine.Random.state = savedRng;
            }
        }

        [Test]
        public void Destruction_CapturedCrew_HasCapturedTag()
        {
            var savedRng = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(999);
                var ship    = AddShipWithCrew(20, ShipDamageState.Heavy);
                var results = _system.ResolveDestruction(ship.uid, _station);

                bool anyCaptured = false;
                foreach (var r in results)
                {
                    if (r.outcome != CrewDestructionOutcome.Captured) continue;
                    anyCaptured = true;
                    if (_station.npcs.TryGetValue(r.npcUid, out var npc))
                        Assert.IsTrue(npc.statusTags.Contains("captured"),
                            "Captured NPC must have 'captured' status tag.");
                }

                Assert.IsTrue(anyCaptured,
                    "With 20 crew and Heavy damage at least one should be captured.");
            }
            finally
            {
                UnityEngine.Random.state = savedRng;
            }
        }

        [Test]
        public void Destruction_EscapedCrew_MissionAndShipUidCleared()
        {
            var savedRng = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(1234);
                var ship    = AddShipWithCrew(20, ShipDamageState.Undamaged);
                var results = _system.ResolveDestruction(ship.uid, _station);

                bool anyEscaped = false;
                foreach (var r in results)
                {
                    if (r.outcome != CrewDestructionOutcome.Escaped) continue;
                    anyEscaped = true;
                    if (_station.npcs.TryGetValue(r.npcUid, out var npc))
                    {
                        Assert.IsNull(npc.missionUid,       "Escaped NPC missionUid must be null.");
                        Assert.IsNull(npc.assignedShipUid,  "Escaped NPC assignedShipUid must be null.");
                    }
                }

                Assert.IsTrue(anyEscaped,
                    "With 20 crew and Undamaged ship at least one should escape.");
            }
            finally
            {
                UnityEngine.Random.state = savedRng;
            }
        }
    }

    // =========================================================================
    // Integration: ship acquisition pipeline
    // =========================================================================

    [TestFixture]
    public class ShipAcquisitionTests
    {
        private ContentRegistry _registry;
        private ShipSystem      _system;
        private StationState    _station;
        private GameObject      _registryGo;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                ShipTestHelpers.ScoutTemplate());
            _system  = new ShipSystem(_registry);
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void AddShipToFleet_CreatesShipInOwnedShips()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            Assert.IsNotNull(ship);
            Assert.IsTrue(_station.ownedShips.ContainsKey(ship.uid));
        }

        [Test]
        public void AddShipToFleet_SetsCorrectRole()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            Assert.AreEqual("scout", ship.role);
        }

        [Test]
        public void AddShipToFleet_DefaultStatusIsDocked()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            Assert.AreEqual("docked", ship.status);
        }

        [Test]
        public void AddShipToFleet_UnknownTemplate_ReturnsNull()
        {
            var ship = _system.AddShipToFleet("ship.nonexistent", "Ghost", _station);
            Assert.IsNull(ship);
        }

        [Test]
        public void AddShipToFleet_DefaultConditionIsUndamaged()
        {
            var ship = _system.AddShipToFleet("ship.scout_vessel", "Pathfinder", _station);
            Assert.AreEqual(100f, ship.conditionPct, 0.001f);
            Assert.AreEqual(ShipDamageState.Undamaged, ship.damageState);
        }
    }

    // =========================================================================
    // Integration: dispatch and mission lifecycle
    // =========================================================================

    [TestFixture]
    public class ShipMissionLifecycleTests
    {
        private ContentRegistry _registry;
        private ShipSystem      _system;
        private StationState    _station;
        private GameObject      _registryGo;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                ShipTestHelpers.ScoutTemplate());
            _system  = new ShipSystem(_registry);
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            Object.DestroyImmediate(_registryGo);
        }

        private (OwnedShipInstance ship, NPCInstance npc) SetupDispatchableShip()
        {
            var ship = OwnedShipInstance.Create("ship.scout_vessel", "Scout Alpha", "scout");
            _station.ownedShips[ship.uid] = ship;
            var npc = ShipTestHelpers.MakeCrew();
            _station.npcs[npc.uid] = npc;
            ship.crewUids.Add(npc.uid);
            npc.assignedShipUid = ship.uid;
            return (ship, npc);
        }

        [Test]
        public void DispatchShipMission_SetsStatusToOnMission()
        {
            var (ship, _) = SetupDispatchableShip();
            var (ok, _, _) = _system.DispatchShipMission(ship.uid, "scout", 100, _station);
            Assert.IsTrue(ok);
            Assert.AreEqual("on_mission", ship.status);
        }

        [Test]
        public void DispatchShipMission_SetsMissionUidOnCrew()
        {
            var (ship, npc) = SetupDispatchableShip();
            _system.DispatchShipMission(ship.uid, "scout", 100, _station);
            Assert.IsNotNull(npc.missionUid);
        }

        [Test]
        public void DispatchShipMission_Fails_WhenRoleIneligible()
        {
            var (ship, _) = SetupDispatchableShip();
            var (ok, reason, _) = _system.DispatchShipMission(ship.uid, "cargo", 100, _station);
            Assert.IsFalse(ok);
            Assert.IsNotNull(reason);
        }

        [Test]
        public void Tick_ResolvesCompletedMission()
        {
            var (ship, npc) = SetupDispatchableShip();
            _system.DispatchShipMission(ship.uid, "scout", 50, _station);
            _station.tick = ship.missionEndTick + 1;
            _system.Tick(_station);
            Assert.AreEqual("docked", ship.status);
            Assert.IsNull(npc.missionUid);
        }

        [Test]
        public void Tick_DoesNotResolveBeforeEndTick()
        {
            var (ship, _) = SetupDispatchableShip();
            _system.DispatchShipMission(ship.uid, "scout", 50, _station);
            _station.tick = ship.missionEndTick - 1;
            _system.Tick(_station);
            Assert.AreEqual("on_mission", ship.status);
        }

        [Test]
        public void AssignCrew_ExceedCapacity_Fails()
        {
            var ship = OwnedShipInstance.Create("ship.scout_vessel", "Scout", "scout");
            _station.ownedShips[ship.uid] = ship;

            var crewUids = new List<string>();
            for (int i = 0; i < 4; i++)   // 4 > crewCapacity(3)
            {
                var npc = ShipTestHelpers.MakeCrew($"Crew_{i}");
                _station.npcs[npc.uid] = npc;
                crewUids.Add(npc.uid);
            }

            var (ok, _) = _system.AssignCrew(ship.uid, crewUids, _station);
            Assert.IsFalse(ok);
        }

        [Test]
        public void AssignCrew_WithinCapacity_Succeeds()
        {
            var ship = OwnedShipInstance.Create("ship.scout_vessel", "Scout", "scout");
            _station.ownedShips[ship.uid] = ship;

            var crewUids = new List<string>();
            for (int i = 0; i < 2; i++)   // 2 ≤ crewCapacity(3)
            {
                var npc = ShipTestHelpers.MakeCrew($"Crew_{i}");
                _station.npcs[npc.uid] = npc;
                crewUids.Add(npc.uid);
            }

            var (ok, _) = _system.AssignCrew(ship.uid, crewUids, _station);
            Assert.IsTrue(ok);
            Assert.AreEqual(2, ship.crewUids.Count);
        }
    }

    // =========================================================================
    // Unit: damage state thresholds and repair
    // =========================================================================

    [TestFixture]
    public class ShipDamageStateTests
    {
        // ShipSystem used with null registry — these tests never call registry methods.
        private ShipSystem   _system;
        private StationState _station;

        [SetUp]
        public void SetUp()
        {
            _system  = new ShipSystem(null);
            _station = ShipTestHelpers.MakeStation();
        }

        [Test]
        public void ComputeDamageState_100_IsUndamaged()
            => Assert.AreEqual(ShipDamageState.Undamaged, ShipSystem.ComputeDamageState(100f));

        [Test]
        public void ComputeDamageState_85_IsLight()
            => Assert.AreEqual(ShipDamageState.Light,     ShipSystem.ComputeDamageState(85f));

        [Test]
        public void ComputeDamageState_60_IsModerate()
            => Assert.AreEqual(ShipDamageState.Moderate,  ShipSystem.ComputeDamageState(60f));

        [Test]
        public void ComputeDamageState_35_IsHeavy()
            => Assert.AreEqual(ShipDamageState.Heavy,     ShipSystem.ComputeDamageState(35f));

        [Test]
        public void ComputeDamageState_10_IsCritical()
            => Assert.AreEqual(ShipDamageState.Critical,  ShipSystem.ComputeDamageState(10f));

        [Test]
        public void ComputeDamageState_0_IsDestroyed()
            => Assert.AreEqual(ShipDamageState.Destroyed, ShipSystem.ComputeDamageState(0f));

        [Test]
        public void ApplyDamage_ReducesCondition_AndUpdatesState()
        {
            var ship = OwnedShipInstance.Create("ship.combat_frigate", "Battlehammer", "combat");
            _station.ownedShips[ship.uid] = ship;

            _system.ApplyDamage(ship.uid, 15f, _station);

            Assert.AreEqual(85f, ship.conditionPct, 0.01f);
            Assert.AreEqual(ShipDamageState.Light, ship.damageState);
        }

        [Test]
        public void RepairShip_IncreasesCondition_AndUpdatesState()
        {
            var ship = OwnedShipInstance.Create("ship.combat_frigate", "Battlehammer", "combat");
            ship.conditionPct = 50f;
            ship.damageState  = ShipDamageState.Moderate;
            _station.ownedShips[ship.uid] = ship;

            _system.RepairShip(ship.uid, 40f, _station);

            Assert.AreEqual(90f, ship.conditionPct, 0.01f);
            Assert.AreEqual(ShipDamageState.Light, ship.damageState);
        }

        [Test]
        public void CriticalShip_CannotBeDispatched()
        {
            var ship = OwnedShipInstance.Create("ship.combat_frigate", "Crippled", "combat");
            ship.conditionPct = 10f;
            ship.damageState  = ShipDamageState.Critical;
            var npc = ShipTestHelpers.MakeCrew();
            ship.crewUids.Add(npc.uid);

            Assert.IsFalse(ship.CanDispatch());
        }

        [Test]
        public void UndamagedShipWithCrew_CanBeDispatched()
        {
            var ship = OwnedShipInstance.Create("ship.scout_vessel", "Pathfinder", "scout");
            ship.conditionPct = 100f;
            ship.damageState  = ShipDamageState.Undamaged;
            var npc = ShipTestHelpers.MakeCrew();
            ship.crewUids.Add(npc.uid);

            Assert.IsTrue(ship.CanDispatch());
        }
    }

    // =========================================================================
    // Unit: AssignCrew validation — null, duplicates, cross-ship reassignment
    // =========================================================================

    [TestFixture]
    public class AssignCrewValidationTests
    {
        private ContentRegistry _registry;
        private ShipSystem      _system;
        private StationState    _station;
        private GameObject      _registryGo;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                ShipTestHelpers.ScoutTemplate(),
                ShipTestHelpers.MiningTemplate());
            _system  = new ShipSystem(_registry);
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void AssignCrew_NullList_Fails()
        {
            var ship = OwnedShipInstance.Create("ship.scout_vessel", "Scout", "scout");
            _station.ownedShips[ship.uid] = ship;

            var (ok, reason) = _system.AssignCrew(ship.uid, null, _station);
            Assert.IsFalse(ok, "AssignCrew with null list should fail.");
            Assert.IsNotNull(reason);
        }

        [Test]
        public void AssignCrew_DuplicateUids_Fails()
        {
            var ship = OwnedShipInstance.Create("ship.scout_vessel", "Scout", "scout");
            _station.ownedShips[ship.uid] = ship;

            var npc = ShipTestHelpers.MakeCrew("Duplicate");
            _station.npcs[npc.uid] = npc;

            // Same UID twice
            var (ok, reason) = _system.AssignCrew(ship.uid, new List<string> { npc.uid, npc.uid }, _station);
            Assert.IsFalse(ok, "AssignCrew with duplicate UIDs should fail.");
            StringAssert.Contains("Duplicate", reason);
        }

        [Test]
        public void AssignCrew_CrossShipReassignment_RemovesFromPreviousShip()
        {
            var ship1 = OwnedShipInstance.Create("ship.scout_vessel",  "Scout 1", "scout");
            var ship2 = OwnedShipInstance.Create("ship.mining_barge",   "Miner 1", "mining");
            _station.ownedShips[ship1.uid] = ship1;
            _station.ownedShips[ship2.uid] = ship2;

            var npc = ShipTestHelpers.MakeCrew("Roaming");
            _station.npcs[npc.uid] = npc;

            // Assign NPC to ship1 first
            var (ok1, _) = _system.AssignCrew(ship1.uid, new List<string> { npc.uid }, _station);
            Assert.IsTrue(ok1, "First assignment should succeed.");
            Assert.IsTrue(ship1.crewUids.Contains(npc.uid));

            // Reassign NPC to ship2
            var (ok2, _) = _system.AssignCrew(ship2.uid, new List<string> { npc.uid }, _station);
            Assert.IsTrue(ok2, "Reassignment to second ship should succeed.");

            Assert.IsFalse(ship1.crewUids.Contains(npc.uid),
                "NPC should be removed from ship1 after reassignment to ship2.");
            Assert.IsTrue(ship2.crewUids.Contains(npc.uid),
                "NPC should be present in ship2 after reassignment.");
            Assert.AreEqual(ship2.uid, npc.assignedShipUid,
                "NPC assignedShipUid should point to ship2 after reassignment.");
        }
    }

    // =========================================================================
    // Unit: RemoveShipFromFleet clears both assignedShipUid and missionUid
    // =========================================================================

    [TestFixture]
    public class RemoveShipFromFleetTests
    {
        private ShipSystem   _system;
        private StationState _station;

        [SetUp]
        public void SetUp()
        {
            _system  = new ShipSystem(null);
            _station = ShipTestHelpers.MakeStation();
        }

        [Test]
        public void RemoveShipFromFleet_ClearsBothMissionAndShipUidOnCrew()
        {
            var ship = OwnedShipInstance.Create("ship.scout_vessel", "Scout", "scout");
            _station.ownedShips[ship.uid] = ship;

            var npc = ShipTestHelpers.MakeCrew();
            npc.assignedShipUid = ship.uid;
            npc.missionUid      = "fleet_test_mission";
            _station.npcs[npc.uid] = npc;
            ship.crewUids.Add(npc.uid);

            _system.RemoveShipFromFleet(ship.uid, _station);

            Assert.IsNull(npc.assignedShipUid, "assignedShipUid must be cleared after RemoveShipFromFleet.");
            Assert.IsNull(npc.missionUid,      "missionUid must also be cleared after RemoveShipFromFleet.");
            Assert.IsFalse(_station.ownedShips.ContainsKey(ship.uid), "Ship must be removed from ownedShips.");
        }
    }

    // =========================================================================
    // Unit: VisitorSystem fleet-only filter
    // =========================================================================

    [TestFixture]
    public class VisitorFleetOnlyFilterTests
    {
        private ContentRegistry _registry;
        private GameObject      _registryGo;

        [SetUp]
        public void SetUp()
        {
            _registryGo = new GameObject("FleetOnlyTestRegistry");
            _registry   = _registryGo.AddComponent<ContentRegistry>();

            // Add one normal visitor template and one fleet-only template
            _registry.Ships["ship.freighter"] = new ShipTemplate
            {
                id        = "ship.freighter",
                role      = "trader",
                fleetOnly = false,
            };
            _registry.Ships["ship.scout_vessel"] = new ShipTemplate
            {
                id        = "ship.scout_vessel",
                role      = "scout",
                fleetOnly = true,
                eligibleMissionTypes = new List<string> { "scout", "exploration" },
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void FleetOnlyTemplate_HasFleetOnlyFlag_True()
        {
            Assert.IsTrue(_registry.Ships["ship.scout_vessel"].fleetOnly,
                "Fleet ship template must have fleetOnly=true.");
        }

        [Test]
        public void NormalTemplate_HasFleetOnlyFlag_False()
        {
            Assert.IsFalse(_registry.Ships["ship.freighter"].fleetOnly,
                "Normal visitor template must have fleetOnly=false.");
        }
    }

    // =========================================================================
    // Unit: GetAvailableBlueprints — research gate and blueprint listing (UI-021)
    // =========================================================================

    [TestFixture]
    public class GetAvailableBlueprintsTests
    {
        private ContentRegistry _registry;
        private ShipSystem      _system;
        private StationState    _station;
        private GameObject      _registryGo;

        private static ShipTemplate BuildableScout(string researchPrereq = "") => new ShipTemplate
        {
            id                   = "ship.scout_vessel",
            role                 = "scout",
            fleetOnly            = true,
            buildTimeTicks       = 240,
            researchPrereq       = researchPrereq,
            eligibleMissionTypes = new List<string> { "scout", "exploration" },
        };

        private static ShipTemplate BuildableMining() => new ShipTemplate
        {
            id                   = "ship.mining_barge",
            role                 = "mining",
            fleetOnly            = true,
            buildTimeTicks       = 480,
            eligibleMissionTypes = new List<string> { "mining", "asteroid" },
        };

        private static ShipTemplate NonBuildableVisitor() => new ShipTemplate
        {
            id        = "ship.visitor",
            role      = "trader",
            fleetOnly = false,
            buildTimeTicks = 0,
        };

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                BuildableScout(),
                BuildableMining(),
                NonBuildableVisitor());
            _system  = new ShipSystem(_registry);
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void GetAvailableBlueprints_ExcludesNonFleetOnlyTemplates()
        {
            var blueprints = _system.GetAvailableBlueprints(_station);
            foreach (var (tmpl, _) in blueprints)
                Assert.AreNotEqual("ship.visitor", tmpl.id,
                    "Non-fleet-only templates must not appear in blueprints.");
        }

        [Test]
        public void GetAvailableBlueprints_ExcludesNonBuildableTemplates()
        {
            var blueprints = _system.GetAvailableBlueprints(_station);
            foreach (var (tmpl, _) in blueprints)
                Assert.Greater(tmpl.buildTimeTicks, 0,
                    "Blueprints with buildTimeTicks <= 0 must be excluded.");
        }

        [Test]
        public void GetAvailableBlueprints_UnlockedWhenNoResearchPrereq()
        {
            var blueprints = _system.GetAvailableBlueprints(_station);
            foreach (var (tmpl, locked) in blueprints)
                if (tmpl.id == "ship.scout_vessel")
                    Assert.IsFalse(locked, "Blueprint with no prereq must not be locked.");
        }

        [Test]
        public void GetAvailableBlueprints_LockedWhenResearchPrereqNotMet()
        {
            var (reg, go) = ShipTestHelpers.MakeRegistryWithShips(
                BuildableScout("tech.advanced_shipyard"));
            var sys = new ShipSystem(reg);

            // Station does not have the required tag.
            var blueprints = sys.GetAvailableBlueprints(_station);
            bool foundLocked = false;
            foreach (var (tmpl, locked) in blueprints)
                if (tmpl.id == "ship.scout_vessel") { foundLocked = locked; break; }

            Object.DestroyImmediate(go);
            Assert.IsTrue(foundLocked,
                "Blueprint must be locked when research prerequisite is not met.");
        }

        [Test]
        public void GetAvailableBlueprints_UnlockedWhenResearchPrereqMet()
        {
            var (reg, go) = ShipTestHelpers.MakeRegistryWithShips(
                BuildableScout("tech.advanced_shipyard"));
            var sys = new ShipSystem(reg);

            // Grant the required research tag.
            _station.SetTag("tech.advanced_shipyard");

            var blueprints = sys.GetAvailableBlueprints(_station);
            bool foundUnlocked = false;
            foreach (var (tmpl, locked) in blueprints)
                if (tmpl.id == "ship.scout_vessel") { foundUnlocked = !locked; break; }

            Object.DestroyImmediate(go);
            Assert.IsTrue(foundUnlocked,
                "Blueprint must be unlocked when research prerequisite is met.");
        }

        [Test]
        public void GetAvailableBlueprints_NullStation_AllUnlocked()
        {
            var (reg, go) = ShipTestHelpers.MakeRegistryWithShips(
                BuildableScout("tech.advanced_shipyard"));
            var sys = new ShipSystem(reg);

            // Passing null station must skip research gating entirely.
            var blueprints = sys.GetAvailableBlueprints(null);
            bool foundLocked = false;
            foreach (var (tmpl, locked) in blueprints)
                if (tmpl.id == "ship.scout_vessel") { foundLocked = locked; break; }

            Object.DestroyImmediate(go);
            Assert.IsFalse(foundLocked,
                "All blueprints must be unlocked when station is null.");
        }
    }

    // =========================================================================
    // Unit: BeginConstruction — Shipyard build queue (UI-021)
    // =========================================================================

    [TestFixture]
    public class BeginConstructionTests
    {
        private ContentRegistry _registry;
        private ShipSystem      _system;
        private StationState    _station;
        private GameObject      _registryGo;

        private static ShipTemplate ScoutBlueprint(string researchPrereq = "") => new ShipTemplate
        {
            id             = "ship.scout_vessel",
            role           = "scout",
            fleetOnly      = true,
            buildTimeTicks = 240,
            researchPrereq = researchPrereq,
            buildMaterials = new Dictionary<string, int>
            {
                { "parts", 10 },
            },
        };

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(ScoutBlueprint());
            _system  = new ShipSystem(_registry);
            _station = ShipTestHelpers.MakeStation(credits: 500f);
            _station.resources["parts"] = 50f;
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            Object.DestroyImmediate(_registryGo);
        }

        [Test]
        public void BeginConstruction_AddsEntryToShipConstructions()
        {
            var (ok, _, construction) = _system.BeginConstruction(
                "ship.scout_vessel", "Pathfinder", _station);

            Assert.IsTrue(ok, "BeginConstruction must succeed.");
            Assert.IsNotNull(construction, "Construction instance must be non-null.");
            Assert.IsTrue(_station.shipConstructions.ContainsKey(construction.uid),
                "Construction must be present in station.shipConstructions.");
        }

        [Test]
        public void BeginConstruction_DeductsMaterials()
        {
            float partsBefore = _station.resources["parts"];
            _system.BeginConstruction("ship.scout_vessel", "Scout", _station);
            float partsAfter = _station.resources["parts"];
            Assert.AreEqual(partsBefore - 10f, partsAfter, 0.001f,
                "Parts must be deducted on successful construction start.");
        }

        [Test]
        public void BeginConstruction_MaterialsReadyFalseWhenInsufficient()
        {
            _station.resources["parts"] = 5f; // less than required 10
            var (ok, _, construction) = _system.BeginConstruction(
                "ship.scout_vessel", "Scout", _station);

            Assert.IsTrue(ok, "BeginConstruction must still succeed (queued anyway).");
            Assert.IsFalse(construction.materialsReady,
                "materialsReady must be false when resources are insufficient.");
        }

        [Test]
        public void BeginConstruction_FailsWhenResearchPrereqNotMet()
        {
            var (reg, go) = ShipTestHelpers.MakeRegistryWithShips(
                ScoutBlueprint("tech.advanced_shipyard"));
            var sys = new ShipSystem(reg);

            var (ok, reason, _) = sys.BeginConstruction(
                "ship.scout_vessel", "Scout", _station);

            Object.DestroyImmediate(go);
            Assert.IsFalse(ok, "BeginConstruction must fail when prereq is not met.");
            Assert.IsNotNull(reason, "Failure reason must be provided.");
        }

        [Test]
        public void BeginConstruction_SucceedsWhenResearchPrereqMet()
        {
            var (reg, go) = ShipTestHelpers.MakeRegistryWithShips(
                ScoutBlueprint("tech.advanced_shipyard"));
            var sys = new ShipSystem(reg);
            _station.SetTag("tech.advanced_shipyard");

            var (ok, _, _) = sys.BeginConstruction(
                "ship.scout_vessel", "Scout", _station);

            Object.DestroyImmediate(go);
            Assert.IsTrue(ok, "BeginConstruction must succeed when research prereq is met.");
        }

        [Test]
        public void Tick_CompletesConstructionAndAddsShipToFleet()
        {
            var (ok, _, construction) = _system.BeginConstruction(
                "ship.scout_vessel", "Pathfinder", _station);
            Assert.IsTrue(ok);

            // Advance tick past endTick to trigger completion.
            _station.tick = construction.endTick + 1;
            _system.Tick(_station);

            Assert.IsFalse(_station.shipConstructions.ContainsKey(construction.uid),
                "Completed construction must be removed from shipConstructions.");
            bool shipAdded = false;
            foreach (var s in _station.ownedShips.Values)
                if (s.name == "Pathfinder") { shipAdded = true; break; }
            Assert.IsTrue(shipAdded,
                "Ship must be added to ownedShips after construction completes.");
        }
    }

    // =========================================================================
    // Unit: mission type filter — Scout eligible missions (UI-021)
    // =========================================================================

    [TestFixture]
    public class MissionTypeFilterTests
    {
        private ContentRegistry _registry;
        private ShipSystem      _system;
        private StationState    _station;
        private GameObject      _registryGo;

        [SetUp]
        public void SetUp()
        {
            FeatureFlags.FleetManagement = true;
            (_registry, _registryGo) = ShipTestHelpers.MakeRegistryWithShips(
                ShipTestHelpers.ScoutTemplate(),
                ShipTestHelpers.MiningTemplate());
            _system  = new ShipSystem(_registry);
            _station = ShipTestHelpers.MakeStation();
        }

        [TearDown]
        public void TearDown()
        {
            FeatureFlags.FleetManagement = true;
            Object.DestroyImmediate(_registryGo);
        }

        private OwnedShipInstance AddShipWithCrew(string templateId, string role)
        {
            var ship = OwnedShipInstance.Create(templateId, "Test Ship", role);
            _station.ownedShips[ship.uid] = ship;
            var npc = ShipTestHelpers.MakeCrew();
            _station.npcs[npc.uid] = npc;
            npc.assignedShipUid = ship.uid;
            ship.crewUids.Add(npc.uid);
            return ship;
        }

        [Test]
        public void Scout_IsEligibleForScoutMission()
        {
            var ship = AddShipWithCrew("ship.scout_vessel", "scout");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "scout", _station),
                "Scout ship must be eligible for 'scout' mission.");
        }

        [Test]
        public void Scout_IsEligibleForExplorationMission()
        {
            var ship = AddShipWithCrew("ship.scout_vessel", "scout");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "exploration", _station),
                "Scout ship must be eligible for 'exploration' mission.");
        }

        [Test]
        public void Scout_NotEligibleForMiningMission()
        {
            var ship = AddShipWithCrew("ship.scout_vessel", "scout");
            Assert.IsFalse(_system.IsEligibleForMission(ship.uid, "mining", _station),
                "Scout ship must not be eligible for 'mining' mission.");
        }

        [Test]
        public void Mining_EligibleForAsteroidMission()
        {
            var ship = AddShipWithCrew("ship.mining_barge", "mining");
            Assert.IsTrue(_system.IsEligibleForMission(ship.uid, "asteroid", _station),
                "Mining ship must be eligible for 'asteroid' mission.");
        }

        [Test]
        public void Mining_NotEligibleForScoutMission()
        {
            var ship = AddShipWithCrew("ship.mining_barge", "mining");
            Assert.IsFalse(_system.IsEligibleForMission(ship.uid, "scout", _station),
                "Mining ship must not be eligible for 'scout' mission.");
        }
    }
}
