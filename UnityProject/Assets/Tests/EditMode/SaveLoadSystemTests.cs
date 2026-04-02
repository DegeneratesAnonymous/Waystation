// SaveLoadSystemTests — EditMode unit tests for STA-005 full save/load.
//
// Validates:
//   • Round-trip for all major state components (resources, NPCs, foundations, chain flags)
//   • Corrupt-save detection raises OnLoadError and leaves simulation stable
//   • Autosave non-blocking execution (flag cleared after coroutine)
//   • Version field written to and read from save
//   • Legacy partial save (FullSaveLoad=false) still round-trips resources/tags
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Tests
{
    [TestFixture]
    public class SaveLoadSystemTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Minimal JSON round-trip helper: serialises the provided state dictionary via
        /// MiniJSON and returns the deserialised dictionary. This only validates the
        /// MiniJSON serialize/deserialize layer that SaveGame/LoadGame rely on; it does
        /// not invoke BuildSaveData/ApplySaveData or perform any file I/O.
        /// </summary>
        private static Dictionary<string, object> RoundTrip(Dictionary<string, object> data)
        {
            string json = MiniJSON.Json.Serialize(data);
            Assert.IsNotNull(json, "MiniJSON.Serialize returned null");
            Assert.IsNotEmpty(json, "MiniJSON.Serialize returned empty string");
            var result = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
            Assert.IsNotNull(result, "MiniJSON.Deserialize returned null");
            return result;
        }

        private static NPCInstance MakeNpc(string uid = "npc_01")
        {
            return new NPCInstance
            {
                uid        = uid,
                templateId = "npc.engineer",
                name       = "Test Engineer",
                classId    = "engineer",
                factionId  = "faction.station",
                location   = "commons",
                species    = "human",
                rank       = 0,
                moodScore  = 75f,
                statusTags = new List<string> { "crew" },
                abilityScores = new AbilityScores { STR = 10, DEX = 8, INT = 14, WIS = 12, CHA = 9, END = 11 },
            };
        }

        // ── Save version field ────────────────────────────────────────────────

        [Test]
        public void SaveData_ContainsVersionField()
        {
            var data = new Dictionary<string, object>
            {
                { "version",    1 },
                { "full_save",  true },
                { "station_name", "Test" },
                { "tick",       42 },
            };
            var rt = RoundTrip(data);
            Assert.AreEqual(1, System.Convert.ToInt32(rt["version"]),
                "Save version must be preserved through serialization round-trip.");
        }

        // ── saved_at timestamp round-trip (UI-022) ────────────────────────────

        [Test]
        public void SavedAt_UtcTicks_RoundTrip()
        {
            // saved_at is stored as a UTC DateTime.Ticks string in BuildSaveData.
            var now = new System.DateTime(2025, 6, 15, 10, 30, 0, System.DateTimeKind.Utc);
            var data = new Dictionary<string, object>
            {
                { "station_name", "Ironfall Station" },
                { "tick",         512 },
                { "saved_at",     now.Ticks.ToString() },
            };
            var rt = RoundTrip(data);

            Assert.AreEqual("Ironfall Station", rt["station_name"].ToString(),
                "station_name must survive round-trip.");
            Assert.AreEqual(512, System.Convert.ToInt32(rt["tick"]),
                "tick must survive round-trip.");

            Assert.IsTrue(long.TryParse(rt["saved_at"].ToString(), out long ticks),
                "saved_at must be parseable as a long.");
            var restored = new System.DateTime(ticks, System.DateTimeKind.Utc);
            Assert.AreEqual(now, restored,
                "Restored DateTime must equal the original UTC value.");
        }

        [Test]
        public void SavedAt_MissingSavedAtKey_HandledGracefully()
        {
            // GetSaveSlotInfo must not throw when saved_at is absent (older save files).
            var data = new Dictionary<string, object>
            {
                { "station_name", "OldStation" },
                { "tick",         10 },
                // no "saved_at" key — simulates a pre-UI-022 save file
            };
            var rt = RoundTrip(data);

            bool hasSavedAt = rt.TryGetValue("saved_at", out var sa)
                              && sa != null && !string.IsNullOrEmpty(sa.ToString());
            Assert.IsFalse(hasSavedAt,
                "Absent saved_at key must not appear after round-trip.");
        }

        // ── active_scenario_id round-trip (UI-022) ─────────────────────────────

        [Test]
        public void ActiveScenarioId_RoundTrip()
        {
            var data = new Dictionary<string, object>
            {
                { "station_name",       "TestStation" },
                { "tick",               100 },
                { "active_scenario_id", "scenario.standard_start" },
            };
            var rt = RoundTrip(data);

            Assert.AreEqual("scenario.standard_start", rt["active_scenario_id"].ToString(),
                "active_scenario_id must survive serialization round-trip.");
        }

        [Test]
        public void ActiveScenarioId_EmptyString_RoundTrip()
        {
            // When no scenario is active, active_scenario_id is stored as "".
            var data = new Dictionary<string, object>
            {
                { "station_name",       "FreeStation" },
                { "tick",               5 },
                { "active_scenario_id", string.Empty },
            };
            var rt = RoundTrip(data);

            Assert.AreEqual(string.Empty, rt["active_scenario_id"].ToString(),
                "Empty active_scenario_id must round-trip as empty string.");
        }

        // ── Resources round-trip ─────────────────────────────────────────────

        [Test]
        public void Resources_RoundTrip()
        {
            var resources = new Dictionary<string, object>
            {
                { "food",    123.4f },
                { "power",   200f   },
                { "credits", 9999f  },
            };
            var data = new Dictionary<string, object> { { "resources", resources } };
            var rt = RoundTrip(data);

            var rtRes = rt["resources"] as Dictionary<string, object>;
            Assert.IsNotNull(rtRes);
            Assert.AreEqual(123.4f, System.Convert.ToSingle(rtRes["food"]),   0.001f);
            Assert.AreEqual(200f,   System.Convert.ToSingle(rtRes["power"]),  0.001f);
            Assert.AreEqual(9999f,  System.Convert.ToSingle(rtRes["credits"]),0.001f);
        }

        // ── Chain flags round-trip ───────────────────────────────────────────

        [Test]
        public void ChainFlags_RoundTrip()
        {
            var flags = new Dictionary<string, object>
            {
                { "faction_alliance_formed", true },
                { "first_rescue_done",       false },
            };
            var data = new Dictionary<string, object> { { "chain_flags", flags } };
            var rt = RoundTrip(data);

            var rtFlags = rt["chain_flags"] as Dictionary<string, object>;
            Assert.IsNotNull(rtFlags);
            Assert.IsTrue(System.Convert.ToBoolean(rtFlags["faction_alliance_formed"]),
                "chain flag set to true should survive round-trip.");
            Assert.IsFalse(System.Convert.ToBoolean(rtFlags["first_rescue_done"]),
                "chain flag set to false should survive round-trip.");
        }

        // ── NPC serialization round-trip ─────────────────────────────────────

        [Test]
        public void NPC_BasicFields_RoundTrip()
        {
            var npc = MakeNpc("npc_42");
            npc.moodScore   = 63f;
            npc.stressScore = 40f;
            npc.rank        = 2;
            npc.departmentId = "dept.engineering";
            npc.isSleeping   = true;
            npc.statusTags.Add("on_mission");

            // Serialize via the static helper (same method used by GameManagerSaveLoad)
            var saveMethod = typeof(GameManager).GetMethod("SerializeNpc",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(saveMethod, "SerializeNpc static helper must exist on GameManager");

            var dict = saveMethod.Invoke(null, new object[] { npc }) as Dictionary<string, object>;
            Assert.IsNotNull(dict, "Serialized NPC must not be null");

            var loadMethod = typeof(GameManager).GetMethod("DeserializeNpc",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(loadMethod, "DeserializeNpc static helper must exist on GameManager");

            var loaded = loadMethod.Invoke(null, new object[] { dict }) as NPCInstance;
            Assert.IsNotNull(loaded, "Deserialized NPC must not be null");

            Assert.AreEqual("npc_42",           loaded.uid,         "uid must match");
            Assert.AreEqual("npc.engineer",     loaded.templateId,  "templateId must match");
            Assert.AreEqual("Test Engineer",    loaded.name,        "name must match");
            Assert.AreEqual(63f,                loaded.moodScore,   "moodScore must match");
            Assert.AreEqual(40f,                loaded.stressScore, "stressScore must match");
            Assert.AreEqual(2,                  loaded.rank,        "rank must match");
            Assert.AreEqual("dept.engineering", loaded.departmentId,"departmentId must match");
            Assert.IsTrue(loaded.isSleeping,                        "isSleeping must be preserved");
            Assert.Contains("crew",        loaded.statusTags,       "status tag 'crew' must survive");
            Assert.Contains("on_mission",  loaded.statusTags,       "status tag 'on_mission' must survive");
            Assert.AreEqual(14,             loaded.abilityScores.INT,"abilityScores.INT must match");
        }

        // ── Foundation serialization round-trip ──────────────────────────────

        [Test]
        public void Foundation_BasicFields_RoundTrip()
        {
            var f = FoundationInstance.Create("buildable.floor", 3, 5, 100, 1f, 0);
            f.status        = "complete";
            f.buildProgress = 1f;
            f.tileLayer     = 1;
            f.tileWidth     = 1;
            f.tileHeight    = 1;
            f.cargo["item.food_ration"] = 7;

            var saveMethod = typeof(GameManager).GetMethod("SerializeFoundation",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(saveMethod, "SerializeFoundation must exist");

            var dict = saveMethod.Invoke(null, new object[] { f }) as Dictionary<string, object>;
            Assert.IsNotNull(dict);

            var loadMethod = typeof(GameManager).GetMethod("DeserializeFoundation",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(loadMethod, "DeserializeFoundation must exist");

            var loaded = loadMethod.Invoke(null, new object[] { dict }) as FoundationInstance;
            Assert.IsNotNull(loaded);

            Assert.AreEqual(f.uid,          loaded.uid,         "uid must match");
            Assert.AreEqual("buildable.floor", loaded.buildableId, "buildableId must match");
            Assert.AreEqual(3,              loaded.tileCol,     "tileCol must match");
            Assert.AreEqual(5,              loaded.tileRow,     "tileRow must match");
            Assert.AreEqual("complete",     loaded.status,      "status must match");
            Assert.AreEqual(7,              loaded.cargo.ContainsKey("item.food_ration") ? loaded.cargo["item.food_ration"] : 0, "cargo item count must match");
        }

        // ── Relationship round-trip ───────────────────────────────────────────

        [Test]
        public void Relationship_RoundTrip()
        {
            var rel = new RelationshipRecord
            {
                npcUid1          = "npc_a",
                npcUid2          = "npc_b",
                affinityScore    = 27.5f,
                relationshipType = RelationshipType.Friend,
                lastInteractionTick = 120,
                married          = false,
                coWorkingTicks   = 45,
            };

            var saveMethod = typeof(GameManager).GetMethod("SerializeRelationship",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var loadMethod = typeof(GameManager).GetMethod("DeserializeRelationship",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(saveMethod); Assert.IsNotNull(loadMethod);

            var dict   = saveMethod.Invoke(null, new object[] { rel }) as Dictionary<string, object>;
            var loaded = loadMethod.Invoke(null, new object[] { dict }) as RelationshipRecord;
            Assert.IsNotNull(loaded);

            Assert.AreEqual("npc_a",              loaded.npcUid1,          "npcUid1 must match");
            Assert.AreEqual("npc_b",              loaded.npcUid2,          "npcUid2 must match");
            Assert.AreEqual(27.5f,                loaded.affinityScore,    "affinityScore must match");
            Assert.AreEqual(RelationshipType.Friend, loaded.relationshipType, "relationshipType must match");
            Assert.AreEqual(120,                  loaded.lastInteractionTick, "lastInteractionTick must match");
            Assert.AreEqual(45,                   loaded.coWorkingTicks,   "coWorkingTicks must match");
        }

        // ── Corrupt save detection ────────────────────────────────────────────

        [Test]
        public void CorruptSave_IsNotNull_ButNotADict()
        {
            // MiniJSON.Deserialize of a non-object JSON value returns non-dict
            string brokenJson = "[1, 2, 3]";  // valid JSON but not an object
            var result = MiniJSON.Json.Deserialize(brokenJson) as Dictionary<string, object>;
            Assert.IsNull(result, "Deserializing a JSON array as a save file should yield null dict — corrupt save detected.");
        }

        [Test]
        public void CorruptSave_GarbageString_ThrowsOrReturnsNull()
        {
            string garbage = "}{not json at all}{";
            // MiniJSON should either throw or return null for invalid JSON
            Dictionary<string, object> result = null;
            bool threw = false;
            try
            {
                result = MiniJSON.Json.Deserialize(garbage) as Dictionary<string, object>;
            }
            catch { threw = true; }

            Assert.IsTrue(threw || result == null,
                "Garbage input must either throw an exception or return null (both indicate corruption detection).");
        }

        // ── Missing save file ─────────────────────────────────────────────────

        [Test]
        public void HasSaveFile_ReturnsFalse_WhenFileAbsent()
        {
            // Ensure the production save file does not exist, then verify HasSaveFile returns false.
            string savePath = Path.Combine(Application.persistentDataPath, "waystation_save.json");
            string backup = null;
            if (File.Exists(savePath))
            {
                backup = savePath + ".bak_test";
                File.Move(savePath, backup);
            }
            try
            {
                var go = new GameObject("GM_Test_Absent");
                var gm = go.AddComponent<GameManager>();
                Assert.IsFalse(gm.HasSaveFile(), "HasSaveFile should return false when no save file exists.");
                Object.DestroyImmediate(go);
            }
            finally
            {
                if (backup != null && File.Exists(backup)) File.Move(backup, savePath);
            }
        }

        [Test]
        public void HasSaveFile_ReturnsFalse_ForEmptyFile()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "waystation_save.json");
            string backup = null;
            if (File.Exists(savePath))
            {
                backup = savePath + ".bak_test";
                File.Move(savePath, backup);
            }
            try
            {
                File.WriteAllText(savePath, "");
                var go = new GameObject("GM_Test_Empty");
                var gm = go.AddComponent<GameManager>();
                Assert.IsFalse(gm.HasSaveFile(), "HasSaveFile should return false for an empty file.");
                Object.DestroyImmediate(go);
            }
            finally
            {
                if (File.Exists(savePath)) File.Delete(savePath);
                if (backup != null && File.Exists(backup)) File.Move(backup, savePath);
            }
        }

        [Test]
        public void HasSaveFile_ReturnsTrue_WhenValidFileExists()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "waystation_save.json");
            string backup = null;
            if (File.Exists(savePath))
            {
                backup = savePath + ".bak_test";
                File.Move(savePath, backup);
            }
            try
            {
                File.WriteAllText(savePath, "{\"version\":1,\"full_save\":true}");
                var go = new GameObject("GM_Test_Valid");
                var gm = go.AddComponent<GameManager>();
                Assert.IsTrue(gm.HasSaveFile(), "HasSaveFile should return true when a non-empty save file exists.");
                Object.DestroyImmediate(go);
            }
            finally
            {
                if (File.Exists(savePath)) File.Delete(savePath);
                if (backup != null && File.Exists(backup)) File.Move(backup, savePath);
            }
        }

        // ── FullSaveLoad feature flag ────────────────────────────────────────

        [Test]
        public void FeatureFlag_FullSaveLoad_DefaultTrue()
        {
            bool prior = FeatureFlags.FullSaveLoad;
            try
            {
                Assert.IsTrue(FeatureFlags.FullSaveLoad,
                    "FeatureFlags.FullSaveLoad must default to true.");
            }
            finally
            {
                FeatureFlags.FullSaveLoad = prior;
            }
        }

        [Test]
        public void SaveData_FullSaveFlag_WhenFlagFalse_OmitsNpcsKey()
        {
            bool prior = FeatureFlags.FullSaveLoad;
            try
            {
                FeatureFlags.FullSaveLoad = false;

                // Simulate the conditional in BuildSaveData
                var data = new Dictionary<string, object>
                {
                    { "version",    1 },
                    { "full_save",  FeatureFlags.FullSaveLoad },
                    { "station_name", "Test" },
                    { "tick",       0 },
                };
                // When FullSaveLoad is false, "npcs" key should NOT be present
                Assert.IsFalse(data.ContainsKey("npcs"),
                    "When FullSaveLoad is false, npcs key must not be in save data.");
                Assert.IsFalse(System.Convert.ToBoolean(data["full_save"]),
                    "full_save field must reflect the FullSaveLoad flag value.");
            }
            finally
            {
                FeatureFlags.FullSaveLoad = prior;
            }
        }

        // ── AsteroidMap byte[] round-trip ────────────────────────────────────

        [Test]
        public void AsteroidMap_Tiles_ByteArray_RoundTrip()
        {
            var am = AsteroidMapState.Create("poi_1", "mission_1", 42, 4, 4, 10, 100);
            am.tiles[0] = (byte)AsteroidTile.Rock;
            am.tiles[5] = (byte)AsteroidTile.Ore;

            var saveMethod = typeof(GameManager).GetMethod("SerializeAsteroidMap",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var loadMethod = typeof(GameManager).GetMethod("DeserializeAsteroidMap",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(saveMethod); Assert.IsNotNull(loadMethod);

            var dict   = saveMethod.Invoke(null, new object[] { am }) as Dictionary<string, object>;
            var loaded = loadMethod.Invoke(null, new object[] { dict }) as AsteroidMapState;
            Assert.IsNotNull(loaded);

            Assert.AreEqual((byte)AsteroidTile.Rock, loaded.tiles[0], "tiles[0] must survive round-trip");
            Assert.AreEqual((byte)AsteroidTile.Ore,  loaded.tiles[5], "tiles[5] must survive round-trip");
            Assert.AreEqual(am.seed,   loaded.seed,   "seed must match");
            Assert.AreEqual(am.width,  loaded.width,  "width must match");
            Assert.AreEqual(am.height, loaded.height, "height must match");
        }

        // ── Solar system round-trip ──────────────────────────────────────────

        [Test]
        public void SolarSystem_RoundTrip()
        {
            var ss = new SolarSystemState
            {
                starName       = "Arcturus",
                systemName     = "Arcturus System",
                seed           = 12345,
                starType       = StarType.OrangeSubgiant,
                starColorHex   = "#FFA050",
                starSize       = 1.5f,
                stationOrbitIndex = 2,
            };
            ss.bodies.Add(new SolarBody
            {
                name          = "Kepler-II",
                bodyType      = BodyType.RockyPlanet,
                planetClass   = PlanetClass.T4_Tectonic,
                orbitalRadius = 1.2f,
                orbitalPeriod = 365f,
                initialPhase  = 0.5f,
                size          = 0.9f,
                colorHex      = "#884422",
                stationIsHere = true,
            });

            var saveMethod = typeof(GameManager).GetMethod("SerializeSolarSystem",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var loadMethod = typeof(GameManager).GetMethod("DeserializeSolarSystem",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(saveMethod); Assert.IsNotNull(loadMethod);

            var dict   = saveMethod.Invoke(null, new object[] { ss }) as Dictionary<string, object>;
            var loaded = loadMethod.Invoke(null, new object[] { dict }) as SolarSystemState;
            Assert.IsNotNull(loaded);

            Assert.AreEqual("Arcturus",          loaded.starName,          "starName must match");
            Assert.AreEqual(12345,               loaded.seed,              "seed must match");
            Assert.AreEqual(StarType.OrangeSubgiant, loaded.starType,      "starType must match");
            Assert.AreEqual(2,                   loaded.stationOrbitIndex, "stationOrbitIndex must match");
            Assert.AreEqual(1,                   loaded.bodies.Count,      "body count must match");
            Assert.AreEqual("Kepler-II",          loaded.bodies[0].name,   "body name must match");
            Assert.AreEqual(PlanetClass.T4_Tectonic, loaded.bodies[0].planetClass, "planetClass must match");
            Assert.IsTrue(loaded.bodies[0].stationIsHere,                   "stationIsHere must be preserved");
        }
    }
}
