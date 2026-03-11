// Event System — tag-driven, data-authored event pipeline.
//
// Responsibilities:
//   - Evaluate which events are eligible given current station state
//   - Select events by weighted random on a difficulty-scaled schedule
//   - Apply outcome effects to the station
//   - Manage cooldowns and event expiry
//   - Queue follow-up events
//   - Surface pending events as player choices
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    // ── Difficulty configuration ──────────────────────────────────────────────

    public class DifficultyConfig
    {
        public int   minGap;              // minimum ticks between random events
        public int   maxGap;              // maximum ticks between random events
        public float hostileMultiplier;   // weight multiplier applied to hostile events
    }

    // ── Pending event ─────────────────────────────────────────────────────────

    public class PendingEvent
    {
        public EventDefinition definition;
        public Dictionary<string, object> context = new Dictionary<string, object>();
        public bool   resolved         = false;
        public string chosenChoiceId;
        public int    expiresAt        = 0;    // 0 = never expires
    }

    // ── Effect Resolver ───────────────────────────────────────────────────────

    public class EffectResolver
    {
        private readonly Dictionary<string, Action<OutcomeEffect, StationState, Dictionary<string, object>>>
            _handlers = new Dictionary<string, Action<OutcomeEffect, StationState, Dictionary<string, object>>>();

        public EffectResolver() => RegisterDefaults();

        public void Register(string type,
            Action<OutcomeEffect, StationState, Dictionary<string, object>> handler)
            => _handlers[type] = handler;

        public void Apply(OutcomeEffect effect, StationState station,
                          Dictionary<string, object> context)
        {
            if (_handlers.TryGetValue(effect.type, out var handler))
            {
                try { handler(effect, station, context); }
                catch (Exception ex)
                { Debug.LogError($"[EffectResolver] Error applying '{effect.type}': {ex.Message}"); }
            }
            else Debug.LogWarning($"[EffectResolver] Unknown effect type '{effect.type}' — skipping.");
        }

        private void RegisterDefaults()
        {
            Register("add_resource",    (e, s, _) =>
            {
                float amount = Convert.ToSingle(e.value ?? 0);
                s.ModifyResource(e.target, amount);
                s.LogEvent($"Resources: +{amount:F0} {e.target}");
            });
            Register("remove_resource", (e, s, _) =>
            {
                float amount = Convert.ToSingle(e.value ?? 0);
                s.ModifyResource(e.target, -amount);
                s.LogEvent($"Resources: -{amount:F0} {e.target}");
            });
            Register("set_tag",     (e, s, _) => s.SetTag(e.target));
            Register("clear_tag",   (e, s, _) => s.ClearTag(e.target));
            Register("modify_rep",  (e, s, _) =>
            {
                float delta  = Convert.ToSingle(e.value ?? 0);
                float newRep = s.ModifyFactionRep(e.target, delta);
                s.LogEvent($"Reputation with {e.target}: {delta:+0;-0} (now {newRep:F0})");
            });
            Register("log_message", (e, s, _) => s.LogEvent((e.value ?? "").ToString()));
            Register("trigger_event", (_, _, _) => { });  // handled by EventSystem
            Register("spawn_npc",     (_, _, _) => { });  // handled externally
            Register("spawn_ship",    (_, _, _) => { });  // handled externally
        }
    }

    // ── Condition Evaluator ───────────────────────────────────────────────────

    public class ConditionEvaluator
    {
        public bool CheckAll(List<ConditionBlock> conditions, StationState station,
                             Dictionary<string, object> context)
        {
            foreach (var c in conditions)
                if (!Check(c, station, context)) return false;
            return true;
        }

        private bool Check(ConditionBlock c, StationState station,
                           Dictionary<string, object> context)
        {
            bool result = Evaluate(c, station, context);
            return c.negate ? !result : result;
        }

        private bool Evaluate(ConditionBlock c, StationState station,
                               Dictionary<string, object> context)
        {
            switch (c.type)
            {
                case "tag_present":          return station.HasTag(c.target);
                case "resource_above":       return station.GetResource(c.target) > Convert.ToSingle(c.value ?? 0);
                case "resource_below":       return station.GetResource(c.target) < Convert.ToSingle(c.value ?? 0);
                case "faction_rep_above":    return station.GetFactionRep(c.target) > Convert.ToSingle(c.value ?? 0);
                case "faction_rep_below":    return station.GetFactionRep(c.target) < Convert.ToSingle(c.value ?? 0);
                case "crew_count_above":     return station.GetCrew().Count > Convert.ToInt32(c.value ?? 0);
                case "visitor_count_above":  return station.GetVisitors().Count > Convert.ToInt32(c.value ?? 0);
                case "docked_ships_above":   return station.GetDockedShips().Count > Convert.ToInt32(c.value ?? 0);
                case "tick_above":           return station.tick > Convert.ToInt32(c.value ?? 0);
                case "policy_is":            return station.policy.TryGetValue(c.target, out var v) && v == (c.value?.ToString() ?? "");
                case "always":               return true;
                default:
                    Debug.LogWarning($"[ConditionEvaluator] Unknown condition type '{c.type}'");
                    return false;
            }
        }
    }

    // ── Event System ─────────────────────────────────────────────────────────

    public class EventSystem
    {
        public static readonly Dictionary<string, DifficultyConfig> DifficultySettings =
            new Dictionary<string, DifficultyConfig>
        {
            { "easy",    new DifficultyConfig { minGap = 10, maxGap = 14, hostileMultiplier = 0.3f } },
            { "normal",  new DifficultyConfig { minGap =  6, maxGap = 10, hostileMultiplier = 1.0f } },
            { "hard",    new DifficultyConfig { minGap =  4, maxGap =  6, hostileMultiplier = 1.5f } },
            { "intense", new DifficultyConfig { minGap =  2, maxGap =  4, hostileMultiplier = 2.0f } }
        };

        private readonly ContentRegistry   _registry;
        private readonly EffectResolver    _resolver;
        private readonly ConditionEvaluator _evaluator;
        private string                     _difficulty;

        // Pending events awaiting player interaction
        private readonly List<PendingEvent>              _pending       = new List<PendingEvent>();
        // Follow-up events to fire next tick
        private readonly Queue<(string eventId, Dictionary<string, object> context)> _followupQueue =
            new Queue<(string, Dictionary<string, object>)>();

        private int _nextEventTick;

        public EventSystem(ContentRegistry registry, string difficulty = "normal")
        {
            _registry   = registry;
            _resolver   = new EffectResolver();
            _evaluator  = new ConditionEvaluator();
            _difficulty = DifficultySettings.ContainsKey(difficulty) ? difficulty : "normal";
            var cfg = DifficultySettings[_difficulty];
            _nextEventTick = UnityEngine.Random.Range(cfg.minGap, cfg.maxGap + 1);
        }

        // ── Registration ─────────────────────────────────────────────────────

        public void RegisterEffectHandler(string type,
            Action<OutcomeEffect, StationState, Dictionary<string, object>> handler)
            => _resolver.Register(type, handler);

        /// <summary>
        /// Change the difficulty in-place without rebuilding the EventSystem.
        /// Safe to call at any point; recalculates the next-event gap immediately.
        /// </summary>
        public void SetDifficulty(string newDifficulty, int currentTick = 0)
        {
            _difficulty = DifficultySettings.ContainsKey(newDifficulty) ? newDifficulty : "normal";
            var cfg = DifficultySettings[_difficulty];
            _nextEventTick = currentTick + UnityEngine.Random.Range(cfg.minGap, cfg.maxGap + 1);
        }

        // ── Tick ──────────────────────────────────────────────────────────────

        public List<PendingEvent> Tick(StationState station)
        {
            var newEvents = new List<PendingEvent>();

            // Fire queued follow-ups
            while (_followupQueue.Count > 0)
            {
                var (eventId, ctx) = _followupQueue.Dequeue();
                if (_registry.Events.TryGetValue(eventId, out var ev))
                {
                    var pending = new PendingEvent
                    {
                        definition = ev, context = ctx,
                        expiresAt  = ev.expiresIn > 0 ? station.tick + ev.expiresIn : 0
                    };
                    _pending.Add(pending);
                    newEvents.Add(pending);
                }
            }

            // Expire timed-out non-hostile events
            foreach (var p in new List<PendingEvent>(_pending))
            {
                if (!p.resolved && p.expiresAt > 0 && station.tick >= p.expiresAt
                    && !p.definition.hostile)
                {
                    station.LogEvent($"EVENT MISSED: {p.definition.title}");
                    FinishEvent(p.definition, p, station);
                }
            }

            // Don't schedule new events while a hostile event is pending
            bool hostilePending = false;
            foreach (var p in _pending)
                if (!p.resolved && p.definition.hostile) { hostilePending = true; break; }
            if (hostilePending) return newEvents;

            if (station.tick < _nextEventTick) return newEvents;

            // Try to fire a random eligible event
            var candidate = SelectEvent(station);
            if (candidate != null)
            {
                var pending = new PendingEvent
                {
                    definition = candidate,
                    expiresAt  = candidate.expiresIn > 0 ? station.tick + candidate.expiresIn : 0
                };
                _pending.Add(pending);
                newEvents.Add(pending);

                if (candidate.choices.Count == 0)
                    AutoResolve(pending, station);
            }

            var cfg2 = DifficultySettings[_difficulty];
            _nextEventTick = station.tick + UnityEngine.Random.Range(cfg2.minGap, cfg2.maxGap + 1);

            return newEvents;
        }

        // ── Selection ────────────────────────────────────────────────────────

        private EventDefinition SelectEvent(StationState station)
        {
            var cfg = DifficultySettings[_difficulty];
            var eligible = new List<EventDefinition>();
            var weights  = new List<float>();

            foreach (var ev in _registry.Events.Values)
            {
                if (ev.weight <= 0f)              continue;
                if (!IsEligible(ev, station))     continue;
                float w = ev.weight * (ev.hostile ? cfg.hostileMultiplier : 1f);
                eligible.Add(ev);
                weights.Add(w);
            }

            if (eligible.Count == 0) return null;

            float total = 0f;
            foreach (var w in weights) total += w;
            float roll = UnityEngine.Random.Range(0f, total);
            float acc  = 0f;
            for (int i = 0; i < eligible.Count; i++)
            {
                acc += weights[i];
                if (roll <= acc) return eligible[i];
            }
            return eligible[eligible.Count - 1];
        }

        private bool IsEligible(EventDefinition ev, StationState station)
        {
            if (station.eventCooldowns.TryGetValue(ev.id, out int ready) && station.tick < ready)
                return false;
            foreach (var tag in ev.requiredTags)
                if (!station.HasTag(tag)) return false;
            foreach (var tag in ev.excludedTags)
                if (station.HasTag(tag)) return false;
            return _evaluator.CheckAll(ev.triggerConditions, station, new Dictionary<string, object>());
        }

        // ── Resolution ───────────────────────────────────────────────────────

        public void ResolveChoice(PendingEvent pending, string choiceId, StationState station)
        {
            var ev = pending.definition;
            EventChoice choice = null;
            foreach (var c in ev.choices)
                if (c.id == choiceId) { choice = c; break; }
            if (choice == null)
            {
                Debug.LogWarning($"[EventSystem] Choice '{choiceId}' not found in event '{ev.id}'");
                return;
            }

            if (!_evaluator.CheckAll(choice.conditions, station, pending.context))
            {
                station.LogEvent($"Choice '{choice.label}' is not available.");
                return;
            }

            ApplyOutcomes(choice.outcomes, station, pending.context);

            if (choice.followupEvent != null)
                _followupQueue.Enqueue((choice.followupEvent, pending.context));

            FinishEvent(ev, pending, station);
        }

        private void AutoResolve(PendingEvent pending, StationState station)
        {
            var ev = pending.definition;
            ApplyOutcomes(ev.autoOutcomes, station, pending.context);
            foreach (var followup in ev.followupEvents)
                _followupQueue.Enqueue((followup, pending.context));
            FinishEvent(ev, pending, station);
        }

        private void ApplyOutcomes(List<OutcomeEffect> outcomes, StationState station,
                                    Dictionary<string, object> context)
        {
            foreach (var effect in outcomes)
            {
                if (effect.type == "trigger_event")
                {
                    string eventId = (effect.value ?? effect.target)?.ToString() ?? "";
                    _followupQueue.Enqueue((eventId, context));
                }
                else _resolver.Apply(effect, station, context);
            }
        }

        private void FinishEvent(EventDefinition ev, PendingEvent pending, StationState station)
        {
            pending.resolved = true;
            if (ev.cooldown > 0)
                station.eventCooldowns[ev.id] = station.tick + ev.cooldown;
        }

        // ── Queue inspection ──────────────────────────────────────────────────

        public List<PendingEvent> GetPending()
        {
            var r = new List<PendingEvent>();
            foreach (var p in _pending)
                if (!p.resolved) r.Add(p);
            return r;
        }

        public void QueueEvent(string eventId, Dictionary<string, object> context = null)
            => _followupQueue.Enqueue((eventId, context ?? new Dictionary<string, object>()));
    }
}
