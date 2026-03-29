// EventSystemTests — EditMode unit tests for EventSystem (FAC-004).
//
// Validates:
//   • Chain flag set, check, and clear operations on StationState
//   • chain_flag_set condition type evaluated correctly by ConditionEvaluator
//   • set_chain_flag / clear_chain_flag effect types applied by EffectResolver
//   • Cooldown enforcement: same event cannot fire again within its cooldown window
//   • Weighted selection: distribution over many samples matches defined weights
//   • FireReactiveTrigger selects a matching eligible event and queues it
//   • FireReactiveTrigger respects cooldowns and eligibility conditions
//   • Chain flag survives in-memory save/load round-trip (serialization structure)
using System.Collections.Generic;
using NUnit.Framework;
using Waystation.Core;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Tests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IRegistryAccess for EventSystem tests — no resources or modules needed.
    /// </summary>
    internal class EventStubRegistry : IRegistryAccess
    {
        public Dictionary<string, ModuleDefinition>   Modules   { get; } = new();
        public Dictionary<string, ResourceDefinition> Resources { get; } = new();
        public Dictionary<string, EventDefinition>    Events    { get; } = new();

        public void AddEvent(EventDefinition ev) => Events[ev.id] = ev;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static class EventTestHelpers
    {
        public static StationState MakeStation()
            => new StationState("EventTestStation");

        /// <summary>
        /// Builds a minimal EventDefinition with the given id, weight, and optional
        /// reactive triggers and cooldown.
        /// </summary>
        public static EventDefinition MakeEvent(string id, float weight = 1f,
            int cooldown = 0, string reactiveTrigger = null,
            List<ConditionBlock> triggerConditions = null)
        {
            var ev = new EventDefinition
            {
                id       = id,
                title    = id,
                weight   = weight,
                cooldown = cooldown,
                hostile  = false,
            };
            if (reactiveTrigger != null)
                ev.reactiveTriggers.Add(reactiveTrigger);
            if (triggerConditions != null)
                ev.triggerConditions.AddRange(triggerConditions);
            return ev;
        }

        /// <summary>Creates an EventSystem backed by the given stub registry.</summary>
        public static EventSystem MakeEventSystem(EventStubRegistry registry)
            => new EventSystem(registry, "normal");
    }

    // ── Chain flag tests ──────────────────────────────────────────────────────

    [TestFixture]
    public class ChainFlagTests
    {
        private StationState _station;

        [SetUp]
        public void SetUp()
        {
            _station = EventTestHelpers.MakeStation();
        }

        [Test]
        public void SetChainFlag_FlagIsPresent()
        {
            _station.SetChainFlag("my_flag");

            Assert.IsTrue(_station.HasChainFlag("my_flag"),
                "Flag should be present after SetChainFlag.");
        }

        [Test]
        public void HasChainFlag_ReturnsFalse_WhenNotSet()
        {
            Assert.IsFalse(_station.HasChainFlag("nonexistent_flag"),
                "HasChainFlag should return false for a flag that was never set.");
        }

        [Test]
        public void ClearChainFlag_RemovesFlag()
        {
            _station.SetChainFlag("to_remove");
            _station.ClearChainFlag("to_remove");

            Assert.IsFalse(_station.HasChainFlag("to_remove"),
                "Flag should be absent after ClearChainFlag.");
        }

        [Test]
        public void ClearChainFlag_OnUnsetFlag_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _station.ClearChainFlag("not_set"),
                "Clearing an unset flag must not throw.");
        }

        [Test]
        public void MultipleFlags_AreIndependent()
        {
            _station.SetChainFlag("flag_a");
            _station.SetChainFlag("flag_b");
            _station.ClearChainFlag("flag_a");

            Assert.IsFalse(_station.HasChainFlag("flag_a"), "flag_a should be cleared.");
            Assert.IsTrue(_station.HasChainFlag("flag_b"),  "flag_b should still be set.");
        }

        [Test]
        public void ChainFlags_DefaultEmpty()
        {
            Assert.IsNotNull(_station.chainFlags, "chainFlags dictionary must be initialised.");
            Assert.AreEqual(0, _station.chainFlags.Count, "No flags set on a fresh station.");
        }
    }

    // ── ConditionEvaluator — chain_flag_set condition ─────────────────────────

    [TestFixture]
    public class ChainFlagConditionTests
    {
        private StationState  _station;
        private EventStubRegistry _registry;
        private EventSystem   _events;

        [SetUp]
        public void SetUp()
        {
            _station  = EventTestHelpers.MakeStation();
            _registry = new EventStubRegistry();
            _events   = EventTestHelpers.MakeEventSystem(_registry);
        }

        [Test]
        public void ChainFlagCondition_Pass_WhenFlagSet()
        {
            _station.SetChainFlag("quest_started");

            // Event requires chain flag "quest_started"
            var ev = EventTestHelpers.MakeEvent("ev_chain",
                triggerConditions: new List<ConditionBlock>
                {
                    new ConditionBlock { type = "chain_flag_set", target = "quest_started" }
                });
            _registry.AddEvent(ev);

            // Set tick past the initial random nextEventTick gap (worst case: 10 ticks max)
            _station.tick = 15;
            var fired = _events.Tick(_station);

            var foundEvChain = false;
            foreach (var instance in fired)
            {
                if (instance.definition.id == "ev_chain")
                {
                    foundEvChain = true;
                    break;
                }
            }

            if (!foundEvChain)
            {
                foreach (var pending in _events.GetPending())
                {
                    if (pending.definition.id == "ev_chain")
                    {
                        foundEvChain = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(foundEvChain,
                "When flag is set the 'ev_chain' event should be eligible (fired or pending).");
        }

        [Test]
        public void ChainFlagCondition_EventIneligible_WhenFlagAbsent()
        {
            // Do NOT set any flag
            var ev = EventTestHelpers.MakeEvent("ev_flag_required",
                triggerConditions: new List<ConditionBlock>
                {
                    new ConditionBlock { type = "chain_flag_set", target = "missing_flag" }
                });
            _registry.AddEvent(ev);

            // Advance far past scheduling window
            for (int i = 0; i < 50; i++)
            {
                _station.tick = i + 1;
                _events.Tick(_station);
            }

            // Check the event is never in pending list
            foreach (var p in _events.GetPending())
                Assert.AreNotEqual("ev_flag_required", p.definition.id,
                    "Event requiring an absent flag must not fire.");
        }

        [Test]
        public void ChainFlagCondition_Negated_TrueWhenFlagAbsent()
        {
            // Event requires that flag is NOT set (negate: true)
            var ev = EventTestHelpers.MakeEvent("ev_no_flag",
                triggerConditions: new List<ConditionBlock>
                {
                    new ConditionBlock { type = "chain_flag_set", target = "blocking_flag", negate = true }
                });
            _registry.AddEvent(ev);

            // With no flag, the negated condition should pass → event should eventually become pending/fired.
            bool seen = false;
            for (int i = 0; i < 50 && !seen; i++)
            {
                _station.tick = i + 1;
                var newEvts = _events.Tick(_station);
                foreach (var p in newEvts)
                    if (p.definition.id == "ev_no_flag") seen = true;

                if (!seen)
                {
                    foreach (var p in _events.GetPending())
                    {
                        if (p.definition.id == "ev_no_flag")
                        {
                            seen = true;
                            break;
                        }
                    }
                }
            }

            Assert.IsTrue(seen,
                "When the blocking flag is absent, a negated chain_flag_set condition should allow the event to become pending or fire.");
        }

        [Test]
        public void ChainFlagCondition_Negated_EventIneligible_WhenFlagPresent()
        {
            _station.SetChainFlag("blocking_flag");

            var ev = EventTestHelpers.MakeEvent("ev_no_flag",
                triggerConditions: new List<ConditionBlock>
                {
                    new ConditionBlock { type = "chain_flag_set", target = "blocking_flag", negate = true }
                });
            _registry.AddEvent(ev);

            bool seen = false;
            for (int i = 0; i < 50 && !seen; i++)
            {
                _station.tick = i + 1;
                var newEvts = _events.Tick(_station);

                foreach (var p in newEvts)
                {
                    if (p.definition.id == "ev_no_flag")
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                {
                    foreach (var p in _events.GetPending())
                    {
                        if (p.definition.id == "ev_no_flag")
                        {
                            seen = true;
                            break;
                        }
                    }
                }
            }

            Assert.IsFalse(seen,
                "When the blocking flag is present, a negated chain_flag_set condition must prevent the event from becoming pending or firing.");
        }
    }

    // ── EffectResolver — set/clear chain flag effects ─────────────────────────

    [TestFixture]
    public class ChainFlagEffectTests
    {
        private StationState  _station;
        private EventStubRegistry _registry;
        private EventSystem   _events;

        [SetUp]
        public void SetUp()
        {
            _station  = EventTestHelpers.MakeStation();
            _registry = new EventStubRegistry();
            _events   = EventTestHelpers.MakeEventSystem(_registry);
        }

        /// <summary>Auto-resolves an event with the given outcome effects immediately.</summary>
        private void FireAutoEvent(List<OutcomeEffect> autoOutcomes)
        {
            var ev = new EventDefinition
            {
                id          = "auto_ev",
                title       = "auto",
                weight      = 1f,
                autoOutcomes = autoOutcomes,
            };
            _events.QueueEvent(ev.id);
            _registry.AddEvent(ev);
            _station.tick = 1;
            _events.Tick(_station);  // dequeues follow-up into pending + auto-resolves
        }

        [Test]
        public void SetChainFlagEffect_SetsFlag()
        {
            FireAutoEvent(new List<OutcomeEffect>
            {
                new OutcomeEffect { type = "set_chain_flag", target = "effect_flag" }
            });

            Assert.IsTrue(_station.HasChainFlag("effect_flag"),
                "set_chain_flag effect must set the flag on the station.");
        }

        [Test]
        public void ClearChainFlagEffect_ClearsFlag()
        {
            _station.SetChainFlag("to_clear");

            FireAutoEvent(new List<OutcomeEffect>
            {
                new OutcomeEffect { type = "clear_chain_flag", target = "to_clear" }
            });

            Assert.IsFalse(_station.HasChainFlag("to_clear"),
                "clear_chain_flag effect must remove the flag from the station.");
        }

        [Test]
        public void SetThenClear_ViaEffects_LeavesFlagAbsent()
        {
            // Set via effect, then clear via another effect in the same autoOutcomes list
            FireAutoEvent(new List<OutcomeEffect>
            {
                new OutcomeEffect { type = "set_chain_flag",   target = "transient_flag" },
                new OutcomeEffect { type = "clear_chain_flag", target = "transient_flag" },
            });

            Assert.IsFalse(_station.HasChainFlag("transient_flag"),
                "Flag cleared after being set in the same outcome list should be absent.");
        }
    }

    // ── Cooldown enforcement tests ────────────────────────────────────────────

    [TestFixture]
    public class EventCooldownTests
    {
        private StationState      _station;
        private EventStubRegistry _registry;
        private EventSystem       _events;

        [SetUp]
        public void SetUp()
        {
            _station  = EventTestHelpers.MakeStation();
            _registry = new EventStubRegistry();
            _events   = EventTestHelpers.MakeEventSystem(_registry);
        }

        [Test]
        public void CooledDownEvent_DoesNotFireWithinCooldownWindow()
        {
            const int Cooldown = 20;
            var ev = EventTestHelpers.MakeEvent("cd_event", cooldown: Cooldown);
            _registry.AddEvent(ev);

            // Manually set cooldown as if event just fired at tick 5
            _station.tick = 5;
            _station.eventCooldowns["cd_event"] = 5 + Cooldown;  // ready at tick 25

            // Advance to tick 20 (still within cooldown)
            int fireCount = 0;
            for (int t = 6; t <= 24; t++)
            {
                _station.tick = t;
                var fired = _events.Tick(_station);
                foreach (var p in fired)
                    if (p.definition.id == "cd_event") fireCount++;
            }

            Assert.AreEqual(0, fireCount,
                "Event must not fire while still within its cooldown window.");
        }

        [Test]
        public void Event_CanFireAfterCooldownExpires()
        {
            const int Cooldown = 5;
            var ev = EventTestHelpers.MakeEvent("cd_event_expire", cooldown: Cooldown);
            _registry.AddEvent(ev);

            // Set cooldown to expire at tick 10
            _station.eventCooldowns["cd_event_expire"] = 10;

            bool fired = false;
            for (int t = 10; t <= 50 && !fired; t++)
            {
                _station.tick = t;
                var newEvts = _events.Tick(_station);
                foreach (var p in newEvts)
                    if (p.definition.id == "cd_event_expire") fired = true;
            }

            Assert.IsTrue(fired, "Event should be able to fire once its cooldown expires.");
        }

        [Test]
        public void FinishEvent_SetsCooldownCorrectly()
        {
            const int Cooldown = 15;
            var ev = EventTestHelpers.MakeEvent("cd_set_test", cooldown: Cooldown);
            _registry.AddEvent(ev);

            // Use reactive trigger to fire the event at a known tick
            ev.reactiveTriggers.Add("test_trigger");
            _station.tick = 10;
            _events.FireReactiveTrigger("test_trigger", _station);
            _events.Tick(_station);   // surfaces queued follow-up

            // Check cooldown was recorded
            Assert.IsTrue(_station.eventCooldowns.ContainsKey("cd_set_test"),
                "eventCooldowns should have an entry for the fired event.");
            Assert.AreEqual(10 + Cooldown, _station.eventCooldowns["cd_set_test"],
                "Cooldown should be set to current tick + event.cooldown.");
        }
    }

    // ── Weighted selection distribution tests ─────────────────────────────────

    [TestFixture]
    public class WeightedSelectionTests
    {
        private StationState      _station;
        private EventStubRegistry _registry;
        private EventSystem       _events;

        [SetUp]
        public void SetUp()
        {
            _station  = EventTestHelpers.MakeStation();
            _registry = new EventStubRegistry();
            _events   = EventTestHelpers.MakeEventSystem(_registry);
        }

        [Test]
        public void WeightedDistribution_MatchesExpectedRatio()
        {
            // Event A has weight 3, Event B has weight 1 → expect ~75% / 25% split
            const int Samples = 1200;
            const float AcceptableVariance = 0.08f;  // ±8 % tolerance

            var evA = EventTestHelpers.MakeEvent("ev_heavy", weight: 3f);
            var evB = EventTestHelpers.MakeEvent("ev_light", weight: 1f);
            evA.reactiveTriggers.Add("weight_test");
            evB.reactiveTriggers.Add("weight_test");
            _registry.AddEvent(evA);
            _registry.AddEvent(evB);

            int countA = 0, countB = 0;
            for (int i = 0; i < Samples; i++)
            {
                // Reset cooldowns each sample so the same event can fire repeatedly
                _station.eventCooldowns.Clear();
                _station.tick = i + 1;

                // Fire reactive trigger and capture the queued event id
                _events.FireReactiveTrigger("weight_test", _station);
                var newEvts = _events.Tick(_station);
                foreach (var p in newEvts)
                {
                    if (p.definition.id == "ev_heavy") countA++;
                    else if (p.definition.id == "ev_light") countB++;
                }
            }

            int total = countA + countB;
            Assert.Greater(total, Samples / 2,
                "At least half the samples should have fired an event.");

            if (total > 0)
            {
                float ratioA = (float)countA / total;
                Assert.AreEqual(0.75f, ratioA, AcceptableVariance,
                    $"ev_heavy (weight 3) should fire ~75 % of the time. Actual: {ratioA:P1}");
            }
        }
    }

    // ── Reactive trigger tests ────────────────────────────────────────────────

    [TestFixture]
    public class ReactiveTriggerTests
    {
        private StationState      _station;
        private EventStubRegistry _registry;
        private EventSystem       _events;

        [SetUp]
        public void SetUp()
        {
            _station  = EventTestHelpers.MakeStation();
            _registry = new EventStubRegistry();
            _events   = EventTestHelpers.MakeEventSystem(_registry);
        }

        [Test]
        public void FireReactiveTrigger_QueuesMatchingEvent()
        {
            var ev = EventTestHelpers.MakeEvent("reactive_ev", reactiveTrigger: "mood_crisis_entry");
            _registry.AddEvent(ev);

            _station.tick = 1;
            _events.FireReactiveTrigger("mood_crisis_entry", _station);
            var fired = _events.Tick(_station);

            bool found = false;
            foreach (var p in fired)
                if (p.definition.id == "reactive_ev") found = true;

            Assert.IsTrue(found, "FireReactiveTrigger should queue the matching event.");
        }

        [Test]
        public void FireReactiveTrigger_DoesNotFireUnmatchedEvent()
        {
            var ev = EventTestHelpers.MakeEvent("wrong_trigger_ev", reactiveTrigger: "faction_rep_threshold_crossed");
            _registry.AddEvent(ev);

            _station.tick = 1;
            _events.FireReactiveTrigger("mood_crisis_entry", _station);
            var fired = _events.Tick(_station);

            foreach (var p in fired)
                Assert.AreNotEqual("wrong_trigger_ev", p.definition.id,
                    "Event with a different reactive trigger must not fire.");
        }

        [Test]
        public void FireReactiveTrigger_RespectsEventCooldown()
        {
            const int Cooldown = 50;
            var ev = EventTestHelpers.MakeEvent("cd_reactive", cooldown: Cooldown,
                reactiveTrigger: "npc_died");
            _registry.AddEvent(ev);

            // Mark event as in cooldown
            _station.tick = 10;
            _station.eventCooldowns["cd_reactive"] = 10 + Cooldown;

            _events.FireReactiveTrigger("npc_died", _station);
            var fired = _events.Tick(_station);

            foreach (var p in fired)
                Assert.AreNotEqual("cd_reactive", p.definition.id,
                    "Reactive trigger must not fire an event still within cooldown.");
        }

        [Test]
        public void FireReactiveTrigger_RespectsEligibilityConditions()
        {
            // Event requires a chain flag that is NOT set
            var ev = EventTestHelpers.MakeEvent("cond_reactive",
                reactiveTrigger: "resource_depleted",
                triggerConditions: new List<ConditionBlock>
                {
                    new ConditionBlock { type = "chain_flag_set", target = "prerequisite_flag" }
                });
            _registry.AddEvent(ev);

            _station.tick = 1;
            _events.FireReactiveTrigger("resource_depleted", _station);
            var fired = _events.Tick(_station);

            foreach (var p in fired)
                Assert.AreNotEqual("cond_reactive", p.definition.id,
                    "Reactive trigger must not fire an event whose conditions are not met.");
        }

        [Test]
        public void FireReactiveTrigger_WithNoMatchingEvent_DoesNotThrow()
        {
            _station.tick = 1;
            Assert.DoesNotThrow(
                () => _events.FireReactiveTrigger("unknown_trigger", _station),
                "FireReactiveTrigger with no matching events must not throw.");
        }

        [Test]
        public void FireReactiveTrigger_ContextPassedToPendingEvent()
        {
            var ev = EventTestHelpers.MakeEvent("ctx_ev", reactiveTrigger: "test_ctx");
            _registry.AddEvent(ev);

            var ctx = new Dictionary<string, object> { { "npc_uid", "abc123" } };
            _station.tick = 1;
            _events.FireReactiveTrigger("test_ctx", _station, ctx);
            var fired = _events.Tick(_station);

            bool found = false;
            foreach (var p in fired)
            {
                if (p.definition.id != "ctx_ev") continue;
                found = true;
                Assert.IsTrue(p.context.ContainsKey("npc_uid"),
                    "Context dictionary should be forwarded to the PendingEvent.");
                Assert.AreEqual("abc123", p.context["npc_uid"]);
            }
            Assert.IsTrue(found, "Event should have been queued with context.");
        }
    }

    // ── Chain flag save/load round-trip (structural) ──────────────────────────

    [TestFixture]
    public class ChainFlagPersistenceTests
    {
        [Test]
        public void ChainFlags_PresentInStationState_SurviveRoundTrip()
        {
            // This test verifies the structural requirement: chainFlags lives on StationState
            // and is therefore part of whatever serialisation the GameManager persists.
            // (Actual JSON serialisation is integration-tested in the save/load system.)

            var original = new StationState("PersistStation");
            original.SetChainFlag("completed_quest_1");
            original.SetChainFlag("met_stranger");

            // Simulate shallow copy (stand-in for save/restore cycle)
            var restored = new StationState("PersistStation");
            foreach (var kv in original.chainFlags)
                restored.chainFlags[kv.Key] = kv.Value;

            Assert.IsTrue(restored.HasChainFlag("completed_quest_1"),
                "'completed_quest_1' flag must survive save/restore.");
            Assert.IsTrue(restored.HasChainFlag("met_stranger"),
                "'met_stranger' flag must survive save/restore.");
            Assert.IsFalse(restored.HasChainFlag("never_set"),
                "Flags that were never set must not appear after restore.");
        }
    }
}
