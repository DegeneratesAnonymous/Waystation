// DepartmentSystemTests — EditMode unit tests for DepartmentSystem (INF-005).
//
// Validates:
//   • Department creation, rename, and deletion with correct cascade to assignments
//   • Duplicate-name guard on create and rename
//   • Department Head eligibility check at rank boundary (rank 0 fails, rank 1 succeeds)
//   • AppointHead fails when NPC is not in the department
//   • RemoveNpcFromDepartment clears Head role when the NPC is the Head
//   • DeleteDepartment cascade sets all NPC departmentIds to null
//   • AssignJobToDepartment / RemoveJobFromDepartment mutate allowedJobs correctly
//   • OnDeptColourChanged event fires on SetDeptColour
//   • NotifyColourChanged fires OnNpcsNeedColourResolve with the correct NPC uid list
//   • DepartmentManagement feature flag gates Tick behaviour
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static class DeptTestHelpers
    {
        public static StationState MakeStation()
        {
            // StationState constructor calls InitDefaultDepartments, so clear them
            // to give each test a clean slate.
            var s = new StationState("DeptTestStation");
            s.departments.Clear();
            return s;
        }

        public static NPCInstance MakeCrewNpc(string uid = null, int rank = 0)
        {
            var npc = new NPCInstance
            {
                uid  = uid ?? System.Guid.NewGuid().ToString("N").Substring(0, 8),
                name = "TestNPC_" + (uid ?? "x"),
                rank = rank,
            };
            npc.statusTags.Add("crew");
            return npc;
        }

        public static (DepartmentRegistry registry, DepartmentSystem system) MakeSystems()
        {
            var registry = new DepartmentRegistry();
            var system   = new DepartmentSystem(registry);
            return (registry, system);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Department CRUD
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class DepartmentCreationTests
    {
        [Test]
        public void CreateDepartment_ValidName_AppearsInStationList()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();

            var dept = system.CreateDepartment("Engineering", station);

            Assert.IsNotNull(dept);
            Assert.AreEqual("Engineering", dept.name);
            Assert.IsTrue(station.departments.Contains(dept));
        }

        [Test]
        public void CreateDepartment_DuplicateName_ReturnsNull()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();

            system.CreateDepartment("Engineering", station);
            var second = system.CreateDepartment("engineering", station); // case-insensitive

            Assert.IsNull(second);
            Assert.AreEqual(1, station.departments.Count);
        }

        [Test]
        public void CreateDepartment_EmptyName_ReturnsNull()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();

            var dept = system.CreateDepartment("  ", station);

            Assert.IsNull(dept);
            Assert.AreEqual(0, station.departments.Count);
        }

        [Test]
        public void RenameDepartment_Success()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("OldName", station);

            var (ok, reason) = system.RenameDepartment(dept.uid, "NewName", station);

            Assert.IsTrue(ok, reason);
            Assert.AreEqual("NewName", dept.name);
        }

        [Test]
        public void RenameDepartment_DuplicateNameConflict_ReturnsFalse()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            system.CreateDepartment("Alpha", station);
            var beta = system.CreateDepartment("Beta", station);

            var (ok, _) = system.RenameDepartment(beta.uid, "Alpha", station);

            Assert.IsFalse(ok);
            Assert.AreEqual("Beta", beta.name);
        }

        [Test]
        public void RenameDepartment_SameName_Succeeds()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Alpha", station);

            // Renaming to the same name should succeed (same uid, no conflict)
            var (ok, _) = system.RenameDepartment(dept.uid, "Alpha", station);

            Assert.IsTrue(ok);
        }
    }

    [TestFixture]
    internal class DepartmentDeletionTests
    {
        [Test]
        public void DeleteDepartment_RemovesFromList()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Security", station);

            var (ok, reason) = system.DeleteDepartment(dept.uid, station);

            Assert.IsTrue(ok, reason);
            Assert.IsFalse(station.departments.Contains(dept));
        }

        [Test]
        public void DeleteDepartment_CascadesNpcDepartmentIdToNull()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Security", station);

            var npc1 = DeptTestHelpers.MakeCrewNpc("npc1");
            var npc2 = DeptTestHelpers.MakeCrewNpc("npc2");
            station.AddNpc(npc1);
            station.AddNpc(npc2);
            npc1.departmentId = dept.uid;
            npc2.departmentId = dept.uid;

            system.DeleteDepartment(dept.uid, station);

            Assert.IsNull(npc1.departmentId);
            Assert.IsNull(npc2.departmentId);
        }

        [Test]
        public void DeleteDepartment_UnknownUid_ReturnsFalse()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();

            var (ok, _) = system.DeleteDepartment("dept.nonexistent", station);

            Assert.IsFalse(ok);
        }

        [Test]
        public void DeleteDepartment_DoesNotAffectNpcsInOtherDepartments()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var deptA = system.CreateDepartment("Alpha", station);
            var deptB = system.CreateDepartment("Beta",  station);

            var npc = DeptTestHelpers.MakeCrewNpc("npc1");
            station.AddNpc(npc);
            npc.departmentId = deptB.uid;

            system.DeleteDepartment(deptA.uid, station);

            Assert.AreEqual(deptB.uid, npc.departmentId);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NPC and Job assignment
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class DepartmentAssignmentTests
    {
        [Test]
        public void AssignNpcToDepartment_SetsNpcDepartmentId()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Science", station);
            var npc  = DeptTestHelpers.MakeCrewNpc("npc1");
            station.AddNpc(npc);

            var (ok, reason) = system.AssignNpcToDepartment(npc.uid, dept.uid, station);

            Assert.IsTrue(ok, reason);
            Assert.AreEqual(dept.uid, npc.departmentId);
        }

        [Test]
        public void RemoveNpcFromDepartment_ClearsNpcDepartmentId()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Science", station);
            var npc  = DeptTestHelpers.MakeCrewNpc("npc1");
            station.AddNpc(npc);
            npc.departmentId = dept.uid;

            system.RemoveNpcFromDepartment(npc.uid, station);

            Assert.IsNull(npc.departmentId);
        }

        [Test]
        public void RemoveNpcFromDepartment_ClearsHeadRoleWhenNpcIsHead()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Science", station);
            var npc  = DeptTestHelpers.MakeCrewNpc("npc1", rank: 1);
            station.AddNpc(npc);
            npc.departmentId = dept.uid;
            dept.headNpcUid  = npc.uid;

            system.RemoveNpcFromDepartment(npc.uid, station);

            Assert.IsNull(dept.headNpcUid);
        }

        [Test]
        public void AssignJobToDepartment_AddsJobToAllowedList()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Engineering", station);

            var (ok, reason) = system.AssignJobToDepartment("job.build", dept.uid, station);

            Assert.IsTrue(ok, reason);
            Assert.IsTrue(dept.allowedJobs.Contains("job.build"));
        }

        [Test]
        public void AssignJobToDepartment_NoDuplicate()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Engineering", station);
            system.AssignJobToDepartment("job.build", dept.uid, station);

            system.AssignJobToDepartment("job.build", dept.uid, station); // second call

            Assert.AreEqual(1, dept.allowedJobs.Count);
        }

        [Test]
        public void RemoveJobFromDepartment_RemovesFromAllowedList()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Engineering", station);
            dept.allowedJobs.Add("job.build");

            system.RemoveJobFromDepartment("job.build", dept.uid, station);

            Assert.IsFalse(dept.allowedJobs.Contains("job.build"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Department Head
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class DepartmentHeadTests
    {
        [Test]
        public void AppointHead_EligibleNpc_Succeeds()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Command", station);
            var npc  = DeptTestHelpers.MakeCrewNpc("officer1", rank: DepartmentSystem.MinHeadRank);
            station.AddNpc(npc);
            npc.departmentId = dept.uid;

            var (ok, reason) = system.AppointHead(dept.uid, npc.uid, station);

            Assert.IsTrue(ok, reason);
            Assert.AreEqual(npc.uid, dept.headNpcUid);
        }

        [Test]
        public void AppointHead_BelowMinRank_ReturnsFalse()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Command", station);
            var npc  = DeptTestHelpers.MakeCrewNpc("crew1", rank: DepartmentSystem.MinHeadRank - 1);
            station.AddNpc(npc);
            npc.departmentId = dept.uid;

            var (ok, _) = system.AppointHead(dept.uid, npc.uid, station);

            Assert.IsFalse(ok);
            Assert.IsNull(dept.headNpcUid);
        }

        [Test]
        public void AppointHead_NpcNotInDepartment_ReturnsFalse()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Command", station);
            var npc  = DeptTestHelpers.MakeCrewNpc("officer1", rank: 2);
            station.AddNpc(npc);
            // npc.departmentId is null — NPC is unassigned

            var (ok, _) = system.AppointHead(dept.uid, npc.uid, station);

            Assert.IsFalse(ok);
            Assert.IsNull(dept.headNpcUid);
        }

        [Test]
        public void AppointHead_AtExactMinRank_Succeeds()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Science", station);
            var npc  = DeptTestHelpers.MakeCrewNpc("officer2", rank: DepartmentSystem.MinHeadRank);
            station.AddNpc(npc);
            npc.departmentId = dept.uid;

            var (ok, reason) = system.AppointHead(dept.uid, npc.uid, station);

            Assert.IsTrue(ok, reason);
        }

        [Test]
        public void RemoveHead_ClearsHeadNpcUid()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Science", station);
            var npc  = DeptTestHelpers.MakeCrewNpc("officer2", rank: 1);
            station.AddNpc(npc);
            npc.departmentId = dept.uid;
            dept.headNpcUid  = npc.uid;

            system.RemoveHead(dept.uid, station);

            Assert.IsNull(dept.headNpcUid);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Colour wiring
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class DepartmentColourTests
    {
        [Test]
        public void SetDeptColour_FiresOnDeptColourChangedEvent()
        {
            var station = DeptTestHelpers.MakeStation();
            var (registry, _) = DeptTestHelpers.MakeSystems();
            registry.Init(station.departments);

            var dept = Department.Create("dept.test", "TestDept");
            station.departments.Add(dept);

            string firedUid = null;
            registry.OnDeptColourChanged += uid => firedUid = uid;

            registry.SetDeptColour("dept.test", UnityEngine.Color.red);

            Assert.AreEqual("dept.test", firedUid);
        }

        [Test]
        public void NotifyColourChanged_FiresOnNpcsNeedColourResolveWithCorrectUids()
        {
            var station = DeptTestHelpers.MakeStation();
            var (registry, system) = DeptTestHelpers.MakeSystems();

            var dept = system.CreateDepartment("Colour Dept", station);

            var npc1 = DeptTestHelpers.MakeCrewNpc("npc_c1");
            var npc2 = DeptTestHelpers.MakeCrewNpc("npc_c2");
            var npc3 = DeptTestHelpers.MakeCrewNpc("npc_c3"); // different department
            station.AddNpc(npc1);
            station.AddNpc(npc2);
            station.AddNpc(npc3);
            npc1.departmentId = dept.uid;
            npc2.departmentId = dept.uid;
            // npc3.departmentId is null

            List<string> receivedUids = null;
            system.OnNpcsNeedColourResolve += uids => receivedUids = uids;

            system.NotifyColourChanged(dept.uid, station);

            Assert.IsNotNull(receivedUids);
            Assert.AreEqual(2, receivedUids.Count);
            Assert.IsTrue(receivedUids.Contains(npc1.uid));
            Assert.IsTrue(receivedUids.Contains(npc2.uid));
            Assert.IsFalse(receivedUids.Contains(npc3.uid));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Feature flag
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class DepartmentFeatureFlagTests
    {
        [Test]
        public void Tick_WithFeatureFlagFalse_DoesNotRunHeadLogic()
        {
            bool wasEnabled = FeatureFlags.DepartmentManagement;
            try
            {
                FeatureFlags.DepartmentManagement = false;

                var station = DeptTestHelpers.MakeStation();
                var (_, system) = DeptTestHelpers.MakeSystems();
                var dept = system.CreateDepartment("Ops", station);

                var npc = DeptTestHelpers.MakeCrewNpc("head1", rank: 1);
                station.AddNpc(npc);
                npc.departmentId = dept.uid;
                dept.headNpcUid  = npc.uid;

                int initialLogCount = station.log.Count;

                // Tick should be a no-op when flag is false
                system.Tick(station, null);

                // No escalation entries should have been added
                Assert.AreEqual(initialLogCount, station.log.Count);
            }
            finally
            {
                FeatureFlags.DepartmentManagement = wasEnabled;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetNpcsInDepartment
    // ─────────────────────────────────────────────────────────────────────────

    [TestFixture]
    internal class GetNpcsInDepartmentTests
    {
        [Test]
        public void GetNpcsInDepartment_ReturnsOnlyMembersOfThatDepartment()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();

            var deptA = system.CreateDepartment("Alpha", station);
            var deptB = system.CreateDepartment("Beta",  station);

            var npc1 = DeptTestHelpers.MakeCrewNpc("n1");
            var npc2 = DeptTestHelpers.MakeCrewNpc("n2");
            var npc3 = DeptTestHelpers.MakeCrewNpc("n3");
            station.AddNpc(npc1);
            station.AddNpc(npc2);
            station.AddNpc(npc3);

            npc1.departmentId = deptA.uid;
            npc2.departmentId = deptA.uid;
            npc3.departmentId = deptB.uid;

            var result = system.GetNpcsInDepartment(deptA.uid, station);

            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.Contains(npc1.uid));
            Assert.IsTrue(result.Contains(npc2.uid));
        }

        [Test]
        public void GetNpcsInDepartment_UnassignedNpcsNotIncluded()
        {
            var station = DeptTestHelpers.MakeStation();
            var (_, system) = DeptTestHelpers.MakeSystems();
            var dept = system.CreateDepartment("Alpha", station);

            var npc = DeptTestHelpers.MakeCrewNpc("n1");
            station.AddNpc(npc);
            // npc.departmentId is null

            var result = system.GetNpcsInDepartment(dept.uid, station);

            Assert.AreEqual(0, result.Count);
        }
    }
}
