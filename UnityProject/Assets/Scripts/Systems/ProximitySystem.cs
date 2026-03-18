// ProximitySystem — applies mood modifiers when NPCs with bonds share a module.
//
// Friends within range receive +1 mood boost (proximity_friend).
// Enemies within range receive -2 mood penalty (proximity_enemy).
// The modifier is a time-limited PushModifier that refreshes every tick while
// in range; when NPCs separate it expires after ProximityModifierDurationTicks.
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

        // Feature flag
        public bool Enabled = true;

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station, MoodSystem mood, RelationshipRegistry rels)
        {
            if (!Enabled) return;

            var crew = station.GetCrew();
            if (crew.Count < 2) return;

            // Compare all unique NPC pairs
            for (int i = 0; i < crew.Count; i++)
            {
                var a = crew[i];
                if (a.missionUid != null) continue;   // away mission — skip

                for (int j = i + 1; j < crew.Count; j++)
                {
                    var b = crew[j];
                    if (b.missionUid != null) continue;

                    var rec = RelationshipRegistry.Get(station, a.uid, b.uid);
                    if (rec == null) continue;

                    // Check proximity: same module (non-empty matching location)
                    bool sameModule = !string.IsNullOrEmpty(a.location) &&
                                      a.location == b.location;

                    if (!sameModule) continue;

                    // Apply relationship-based proximity modifier to both NPCs
                    ApplyProximityEffect(a, b, rec.relationshipType, station.tick, mood);
                }
            }
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static void ApplyProximityEffect(NPCInstance a, NPCInstance b,
                                                   RelationshipType type, int tick,
                                                   MoodSystem mood)
        {
            switch (type)
            {
                case RelationshipType.Friend:
                case RelationshipType.Lover:
                case RelationshipType.Spouse:
                    mood?.PushModifier(a, "proximity_friend", FriendBonus,
                                       ProximityModifierDurationTicks, tick, "proximity");
                    mood?.PushModifier(b, "proximity_friend", FriendBonus,
                                       ProximityModifierDurationTicks, tick, "proximity");
                    break;

                case RelationshipType.Enemy:
                    mood?.PushModifier(a, "proximity_enemy", EnemyPenalty,
                                       ProximityModifierDurationTicks, tick, "proximity");
                    mood?.PushModifier(b, "proximity_enemy", EnemyPenalty,
                                       ProximityModifierDurationTicks, tick, "proximity");
                    break;

                // Acquaintance / None / unrecognised → no proximity effect
            }
        }
    }
}
