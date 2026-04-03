// TickSchedulerTests — EditMode unit tests for WO-SYS-001 TickScheduler.
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Core;

namespace Waystation.Tests
{
    [TestFixture]
    public class TickSchedulerTests
    {
        private TickScheduler _scheduler;

        [SetUp]
        public void SetUp()
        {
            _scheduler = new TickScheduler();
        }

        [Test]
        public void Register_AddsSystem()
        {
            _scheduler.Register(new TickRegistration
            {
                SystemId = "test_system",
                PreferredChannel = 0,
                MaxDelayTicks = 2,
                OnTick = () => { },
                EstimatedCostMs = 0.5f,
            });
            Assert.AreEqual(1, _scheduler.RegistrationCount);
        }

        [Test]
        public void Register_ReplacesDuplicate()
        {
            _scheduler.Register(new TickRegistration
            {
                SystemId = "dup",
                PreferredChannel = 0,
                MaxDelayTicks = 1,
                OnTick = () => { },
            });
            _scheduler.Register(new TickRegistration
            {
                SystemId = "dup",
                PreferredChannel = 1,
                MaxDelayTicks = 5,
                OnTick = () => { },
            });
            Assert.AreEqual(1, _scheduler.RegistrationCount, "Duplicate should replace");
        }

        [Test]
        public void Unregister_RemovesSystem()
        {
            _scheduler.Register(new TickRegistration
            {
                SystemId = "removable",
                PreferredChannel = 0,
                MaxDelayTicks = 1,
                OnTick = () => { },
            });
            _scheduler.Unregister("removable");
            Assert.AreEqual(0, _scheduler.RegistrationCount);
        }

        [Test]
        public void Tick_ExecutesImmediateChannelEveryTick()
        {
            int counter = 0;
            _scheduler.Register(new TickRegistration
            {
                SystemId = "immediate_test",
                PreferredChannel = 0,    // Immediate — cadence 1
                MaxDelayTicks = 1,
                OnTick = () => counter++,
                EstimatedCostMs = 0.1f,
            });

            _scheduler.Tick(1);
            _scheduler.Tick(2);
            _scheduler.Tick(3);

            Assert.AreEqual(3, counter, "Channel 0 (Immediate) should fire every tick");
        }

        [Test]
        public void Tick_FastChannelFiresEveryCadence()
        {
            int counter = 0;
            _scheduler.Register(new TickRegistration
            {
                SystemId = "fast_test",
                PreferredChannel = 1,    // Fast — cadence 4
                MaxDelayTicks = 2,
                OnTick = () => counter++,
                EstimatedCostMs = 0.1f,
            });

            // Tick 1-3: not due (cadence=4)
            for (int t = 1; t <= 3; t++) _scheduler.Tick(t);
            Assert.AreEqual(0, counter, "Should not fire before cadence");

            _scheduler.Tick(4);
            Assert.AreEqual(1, counter, "Should fire at tick 4 (cadence 4)");
        }

        [Test]
        public void Tick_MaxDelayForces_Execution()
        {
            int counter = 0;
            // Register on channel 2 (Medium, cadence 10) with MaxDelayTicks = 2
            _scheduler.Register(new TickRegistration
            {
                SystemId = "delay_test",
                PreferredChannel = 2,
                MaxDelayTicks = 2,
                OnTick = () => counter++,
                EstimatedCostMs = 0.1f,
            });

            // Skip to tick 12 (10 cadence + 2 max delay) — should force execute
            _scheduler.Tick(12);
            Assert.AreEqual(1, counter, "Should force execute after MaxDelayTicks exceeded");
        }

        [Test]
        public void GetBudgetSnapshot_ReturnsAllChannels()
        {
            _scheduler.Tick(1);
            var snapshot = _scheduler.GetBudgetSnapshot();
            Assert.AreEqual(5, snapshot.Length, "Should have 5 channel snapshots");
            Assert.AreEqual("Immediate", snapshot[0].Name);
            Assert.AreEqual("Fast", snapshot[1].Name);
            Assert.AreEqual("Medium", snapshot[2].Name);
            Assert.AreEqual("Slow", snapshot[3].Name);
            Assert.AreEqual("Weekly", snapshot[4].Name);
        }

        [Test]
        public void LoadConfig_OverridesDefaults()
        {
            string json = @"{
                ""target_frame_budget_ms"": 16.0,
                ""channels"": [
                    {""name"": ""Realtime"", ""default_cadence"": 1, ""budget_ms"": 8.0}
                ]
            }";
            _scheduler.LoadConfig(json);
            Assert.AreEqual(16f, _scheduler.TargetFrameBudgetMs, 0.01f);

            _scheduler.Tick(1);
            var snapshot = _scheduler.GetBudgetSnapshot();
            Assert.AreEqual("Realtime", snapshot[0].Name);
            Assert.AreEqual(8f, snapshot[0].BudgetAllocatedMs, 0.01f);
        }

        [Test]
        public void LegacyTickSubscriber_WrapsCallback()
        {
            var legacy = new LegacyTickSubscriber(_scheduler);
            int counter = 0;
            legacy.WrapSubscriber("old_system", () => counter++);

            Assert.AreEqual(1, legacy.WrappedCount);
            Assert.AreEqual(1, _scheduler.RegistrationCount);

            // The wrapped subscriber runs on Channel 0 (Immediate)
            _scheduler.Tick(1);
            Assert.AreEqual(1, counter, "Legacy subscriber should execute on tick");
        }

        [Test]
        public void ChannelBudgetState_UsagePercent()
        {
            var state = new ChannelBudgetState
            {
                BudgetAllocatedMs = 10f,
                BudgetUsedMs = 7f,
            };
            Assert.AreEqual(0.7f, state.UsagePercent, 0.001f);
        }

        [Test]
        public void ChannelBudgetState_ZeroBudget_SafeDivide()
        {
            var state = new ChannelBudgetState { BudgetAllocatedMs = 0f, BudgetUsedMs = 0f };
            Assert.AreEqual(0f, state.UsagePercent);
        }
    }
}
