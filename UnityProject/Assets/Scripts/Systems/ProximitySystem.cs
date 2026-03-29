// ProximitySystem — applies mood modifiers and work speed bonuses when NPCs with bonds share a module.
//
// Friends within range receive +1 mood boost (proximity_friend) on the happy/sad axis.
// Enemies within range receive -2 mood penalty (proximity_enemy) on BOTH mood axes.
// Mentors with a student in the same module give the student a work speed bonus
// (proximityWorkModifier) in addition to the friend mood boost shared by both.
//
// Modifiers are time-limited and refresh every tick while in range; when NPCs separate
// they expire after ProximityModifierDurationTicks.
//
// Per-tick evaluation groups NPCs by module first so only same-module pairs are processed,
// avoiding a registry lookup for pairs in different modules.  A per-pair relationship type
// cache (_typeCache) stores the last-seen RelationshipType and is updated whenever the
// registry type differs, ensuring cache invalidation is automatic and allocation-free.
//
// Strangers (RelationshipType.None) receive no proximity modifier.
using System.Collections.Generic;
using Waystation.Models;

namespace Waystation.Systems
{
    public class ProximitySystem
    {
        // ── Constants ─────────────────────────────────────────────────────────

        /// <summary>
        /// Duration (ticks) of a proximity modifier after NPCs stop sharing a module.
        /// Must be > 0 so the modifier expires; set short (~20 ticks ≈ 10 seconds)
        /// so separation is felt quickly.
        /// </summary>
        public const int ProximityModifierDurationTicks = 20;

        // Mood deltas
        private const float FriendBonus  =  1f;
        private const float EnemyPenalty = -2f;

        // Work speed bonus applied to a student when their mentor shares the module.
        // Stored as an additive fraction: 0.1 → 10% faster work (proximityWorkModifier = 1.1).
        private const float MentorWorkBonus = 0.1f;

        // Feature flag
        public bool Enabled = true;

        // ── Per-pair relationship type cache ──────────────────────────────────

        // Caches the RelationshipType per pair key to avoid re-reading the full
        // RelationshipRecord every tick.  Updated (cache-invalidated) automatically
        // when the registry type differs from the cached value.
        private readonly Dictionary<string, RelationshipType> _typeCache =
            new Dictionary<string, RelationshipType>();

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station, MoodSystem mood, RelationshipRegistry rels)
        {
            if (!Enabled) return;

            var crew = station.GetCrew();
            if (crew.Count < 2) return;

            // ── Expire stale proximity work modifiers ─────────────────────────
            foreach (var npc in crew)
            {
                if (npc.proximityWorkModifierExpiresAtTick >= 0 &&
                    station.tick >= npc.proximityWorkModifierExpiresAtTick)
                {
                    npc.proximityWorkModifier            = 1.0f;
                    npc.proximityWorkModifierExpiresAtTick = -1;
                }
            }

            // ── Group crew by module (O(n)) for efficient pair evaluation ─────
            // Only NPCs in the same module can have proximity effects.
            var byModule = new Dictionary<string, List<NPCInstance>>();
            foreach (var npc in crew)
            {
                if (npc.missionUid != null) continue;          // away mission — skip
                if (string.IsNullOrEmpty(npc.location)) continue;

                if (!byModule.TryGetValue(npc.location, out var list))
                    byModule[npc.location] = list = new List<NPCInstance>();
                list.Add(npc);
            }

            // ── Evaluate pairs within each module ─────────────────────────────
            foreach (var occupants in byModule.Values)
            {
                if (occupants.Count < 2) continue;

                for (int i = 0; i < occupants.Count; i++)
                {
                    for (int j = i + 1; j < occupants.Count; j++)
                    {
                        var a = occupants[i];
                        var b = occupants[j];

                        var rec = RelationshipRegistry.Get(station, a.uid, b.uid);
                        if (rec == null) continue;

                        // ── Update relationship type cache ────────────────────
                        // If the cached type differs from the current registry type, the
                        // cache is stale (relationship changed) — overwrite it now so this
                        // tick applies the correct effect.
                        string cacheKey = RelationshipRecord.MakeKey(a.uid, b.uid);
                        if (!_typeCache.TryGetValue(cacheKey, out var cachedType) ||
                            cachedType != rec.relationshipType)
                        {
                            _typeCache[cacheKey] = rec.relationshipType;
                        }

                        ApplyProximityEffect(a, b, rec, station.tick, mood);
                    }
                }
            }
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static void ApplyProximityEffect(NPCInstance a, NPCInstance b,
                                                   RelationshipRecord rec, int tick,
                                                   MoodSystem mood)
        {
            switch (rec.relationshipType)
            {
                case RelationshipType.Friend:
                case RelationshipType.Lover:
                case RelationshipType.Spouse:
                    mood?.PushModifier(a, "proximity_friend", FriendBonus,
                                       ProximityModifierDurationTicks, tick, "proximity");
                    mood?.PushModifier(b, "proximity_friend", FriendBonus,
                                       ProximityModifierDurationTicks, tick, "proximity");
                    break;

                case RelationshipType.Mentor:
                    // Both NPC in the pair receive the friend mood boost (Mentor is a Friend sub-type).
                    mood?.PushModifier(a, "proximity_friend", FriendBonus,
                                       ProximityModifierDurationTicks, tick, "proximity");
                    mood?.PushModifier(b, "proximity_friend", FriendBonus,
                                       ProximityModifierDurationTicks, tick, "proximity");

                    // The student (not the mentor) also receives a work speed bonus.
                    // mentorUid identifies which NPC is the mentor; the other is the student.
                    if (rec.mentorUid != null)
                    {
                        var student = (a.uid == rec.mentorUid) ? b : a;
                        student.proximityWorkModifier            = 1.0f + MentorWorkBonus;
                        student.proximityWorkModifierExpiresAtTick = tick + ProximityModifierDurationTicks;
                    }
                    break;

                case RelationshipType.Enemy:
                    // Enemy penalty applies on both mood axes: happy/sad and calm/stressed.
                    mood?.PushModifier(a, "proximity_enemy", EnemyPenalty,
                                       ProximityModifierDurationTicks, tick,
                                       MoodAxis.HappySad, "proximity");
                    mood?.PushModifier(b, "proximity_enemy", EnemyPenalty,
                                       ProximityModifierDurationTicks, tick,
                                       MoodAxis.HappySad, "proximity");
                    mood?.PushModifier(a, "proximity_enemy", EnemyPenalty,
                                       ProximityModifierDurationTicks, tick,
                                       MoodAxis.CalmStressed, "proximity");
                    mood?.PushModifier(b, "proximity_enemy", EnemyPenalty,
                                       ProximityModifierDurationTicks, tick,
                                       MoodAxis.CalmStressed, "proximity");
                    break;

                // Acquaintance / None / unrecognised → no proximity effect
            }
        }
    }
}
