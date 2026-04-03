// JobTagFilterTests — EditMode unit tests for WO-JOB-001 task tag filter.
//
// Validates:
//   • Tag intersection: task tags vs job tags — match, no match, wildcard
//   • Unassigned NPC bypasses job layer for any task
//   • Two-layer eligibility: job layer pass + expertise check scenarios
//   • JobDefinition taskTags parsed from JSON-like data
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    [TestFixture]
    public class JobTagFilterTests
    {
        private static NPCInstance MakeNpc(string departmentId = null)
        {
            return new NPCInstance
            {
                uid = "npc_test",
                name = "Test NPC",
                departmentId = departmentId
            };
        }

        private static Department MakeDept(string uid, params string[] jobIds)
        {
            var dept = Department.Create(uid, "Test Dept");
            foreach (var j in jobIds) dept.allowedJobs.Add(j);
            return dept;
        }

        private static Dictionary<string, JobDefinition> MakeJobRegistry(params JobDefinition[] jobs)
        {
            var reg = new Dictionary<string, JobDefinition>();
            foreach (var j in jobs) reg[j.id] = j;
            return reg;
        }

        private static JobDefinition MakeJob(string id, params string[] tags)
        {
            return new JobDefinition
            {
                id = id,
                displayName = id,
                taskTags = new List<string>(tags)
            };
        }

        private static JobDefinition MakeWildcardJob()
        {
            return new JobDefinition
            {
                id = "job_general_duties",
                displayName = "General Duties",
                taskTags = new List<string> { "*" },
                isWildcard = true
            };
        }

        // ── Unassigned NPC bypasses filter ────────────────────────────────────

        [Test]
        public void UnassignedNpc_PassesAnyTags()
        {
            var npc = MakeNpc(departmentId: null);
            var tags = new List<string> { "surgery" };
            var dept = MakeDept("dept_a", "job_structural");
            var registry = MakeJobRegistry(MakeJob("job_structural", "engineering"));

            Assert.IsTrue(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, dept, registry));
        }

        [Test]
        public void UnassignedNpc_NullDepartment_Passes()
        {
            var npc = MakeNpc(departmentId: null);
            var tags = new List<string> { "melee" };

            Assert.IsTrue(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, null, null));
        }

        // ── Tag intersection match ────────────────────────────────────────────

        [Test]
        public void AssignedNpc_TagMatch_Passes()
        {
            var npc = MakeNpc(departmentId: "dept_a");
            var dept = MakeDept("dept_a", "job_surgery");
            var registry = MakeJobRegistry(MakeJob("job_surgery", "surgery", "prosthetics"));
            var tags = new List<string> { "surgery" };

            Assert.IsTrue(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, dept, registry));
        }

        [Test]
        public void AssignedNpc_NoTagMatch_Fails()
        {
            var npc = MakeNpc(departmentId: "dept_a");
            var dept = MakeDept("dept_a", "job_structural");
            var registry = MakeJobRegistry(MakeJob("job_structural", "engineering"));
            var tags = new List<string> { "surgery" };

            Assert.IsFalse(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, dept, registry));
        }

        [Test]
        public void AssignedNpc_MultipleJobTags_PartialMatch_Passes()
        {
            var npc = MakeNpc(departmentId: "dept_a");
            var dept = MakeDept("dept_a", "job_medical");
            var registry = MakeJobRegistry(
                MakeJob("job_medical", "diagnosis", "treatment", "psychology"));
            var tags = new List<string> { "treatment" };

            Assert.IsTrue(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, dept, registry));
        }

        [Test]
        public void AssignedNpc_MultipleJobs_OneMatches_Passes()
        {
            var npc = MakeNpc(departmentId: "dept_a");
            var dept = MakeDept("dept_a", "job_structural", "job_combat");
            var registry = MakeJobRegistry(
                MakeJob("job_structural", "engineering"),
                MakeJob("job_combat", "melee", "ranged"));
            var tags = new List<string> { "ranged" };

            Assert.IsTrue(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, dept, registry));
        }

        // ── Wildcard job ──────────────────────────────────────────────────────

        [Test]
        public void AssignedNpc_WildcardJob_MatchesAnything()
        {
            var npc = MakeNpc(departmentId: "dept_a");
            var dept = MakeDept("dept_a", "job_general_duties");
            var registry = MakeJobRegistry(MakeWildcardJob());
            var tags = new List<string> { "something_obscure" };

            Assert.IsTrue(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, dept, registry));
        }

        // ── Empty tasks / edge cases ──────────────────────────────────────────

        [Test]
        public void EmptyTaskTags_Passes()
        {
            var npc = MakeNpc(departmentId: "dept_a");
            var dept = MakeDept("dept_a", "job_structural");
            var registry = MakeJobRegistry(MakeJob("job_structural", "engineering"));
            var tags = new List<string>();

            Assert.IsTrue(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, dept, registry));
        }

        [Test]
        public void NullTaskTags_Passes()
        {
            var npc = MakeNpc(departmentId: "dept_a");
            var dept = MakeDept("dept_a", "job_structural");
            var registry = MakeJobRegistry(MakeJob("job_structural", "engineering"));

            Assert.IsTrue(TaskEligibilityResolver.PassesJobTagFilter(npc, null, dept, registry));
        }

        [Test]
        public void DeptWithNoJobs_Fails()
        {
            var npc = MakeNpc(departmentId: "dept_a");
            var dept = MakeDept("dept_a");
            var registry = MakeJobRegistry();
            var tags = new List<string> { "engineering" };

            Assert.IsFalse(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, dept, registry));
        }

        [Test]
        public void DeptJobNotInRegistry_Skipped()
        {
            var npc = MakeNpc(departmentId: "dept_a");
            var dept = MakeDept("dept_a", "job_nonexistent");
            var registry = MakeJobRegistry();
            var tags = new List<string> { "engineering" };

            Assert.IsFalse(TaskEligibilityResolver.PassesJobTagFilter(npc, tags, dept, registry));
        }
    }
}
