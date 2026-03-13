// Comms System — handles radio communications from passing ships.
//
// Every ~2 in-game days (48 ticks at 24 ticks/day) there is a 30% chance a
// trade ship will pass by and send a transmission offering to sell Ice.  The
// player can reply via the Comms tab in the HUD:
//   "Sure, let's trade"   → credits deducted, ice delivered to Hangar/cargo
//   "No thank you"        → ship departs, no penalty
//   "Come back later"     → ship departs with a polite acknowledgement
using System;
using System.Collections.Generic;
using UnityEngine;
using Waystation.Core;
using Waystation.Models;

namespace Waystation.Systems
{
    public class CommsSystem
    {
        // ── Constants ─────────────────────────────────────────────────────────
        private const int   TicksPerDay             = 24;
        private const int   PassingShipCheckInterval = TicksPerDay * 2;  // every 2 days
        private const float PassingShipChance        = 0.30f;

        private const int   IceQtyMin   = 50;
        private const int   IceQtyMax   = 200;
        private const float IcePriceMin = 1.5f;
        private const float IcePriceMax = 3.5f;

        private static readonly string[] TradePrefixes =
            { "ISV", "MCV", "RTV", "FSS", "CTV" };
        private static readonly string[] TradeNames =
        {
            "Ice Runner", "Frost Haul", "Cold Meridian", "Cryogenic Dawn",
            "Glacier Express", "Frozen Passage", "Arctic Reach", "Ice Break",
            "Crystal Wake", "Polar Drift"
        };

        // ── State ─────────────────────────────────────────────────────────────
        private int _lastCheckTick = 0;

        // ── Tick ──────────────────────────────────────────────────────────────

        public void Tick(StationState station)
        {
            PruneExpiredMessages(station);

            if (station.tick - _lastCheckTick < PassingShipCheckInterval) return;
            _lastCheckTick = station.tick;
            CheckPassingShips(station);
        }

        // ── Expiry ────────────────────────────────────────────────────────────

        /// <summary>
        /// Remove messages that have passed their expiry tick and have not been
        /// replied to. Also cleans up any transient passing-by ship associated
        /// with the expired message.
        /// </summary>
        private void PruneExpiredMessages(StationState station)
        {
            // Iterate backwards so we can remove by index safely
            for (int i = station.messages.Count - 1; i >= 0; i--)
            {
                var msg = station.messages[i];
                if (!msg.IsExpired(station.tick)) continue;

                // Remove the associated transient ship if it's still passing by
                if (!string.IsNullOrEmpty(msg.shipUid) &&
                    station.ships.TryGetValue(msg.shipUid, out var ship) &&
                    ship.behaviorTags.Contains("passing_by"))
                {
                    station.RemoveShip(msg.shipUid);
                }

                station.messages.RemoveAt(i);
                station.LogEvent($"Transmission from {msg.senderName} expired without reply.");
                Debug.Log($"[CommsSystem] Expired message '{msg.subject}' removed.");
            }
        }

        // ── Arrival generation ────────────────────────────────────────────────

        private void CheckPassingShips(StationState station)
        {
            if (UnityEngine.Random.value > PassingShipChance) return;

            string prefix   = TradePrefixes[UnityEngine.Random.Range(0, TradePrefixes.Length)];
            string shipName = $"{prefix} {TradeNames[UnityEngine.Random.Range(0, TradeNames.Length)]}";
            int    iceQty   = UnityEngine.Random.Range(IceQtyMin, IceQtyMax + 1);
            float  icePrice = Mathf.Round(UnityEngine.Random.Range(IcePriceMin, IcePriceMax) * 100f) / 100f;

            // Register a transient passing ship (status = "incoming", tagged as passing_by)
            var ship = ShipInstance.Create("ship.trade_vessel", shipName, "trader",
                                           "trade", factionId: null, threatLevel: 0);
            ship.cargo["item.ice"] = iceQty;
            ship.behaviorTags.Add("passing_by");
            station.AddShip(ship);

            // Build the comm message
            float total = iceQty * icePrice;
            string body =
                $"Greetings from {shipName}. We are a trade vessel on a transit route " +
                $"and have {iceQty} units of Ice available at {icePrice:F2} cr/unit " +
                $"(total {total:F0} credits). " +
                $"If interested, we can dispatch a shuttle immediately.";

            var msg = CommMessage.Create(
                subject:      $"Trade offer — Ice ×{iceQty} from {shipName}",
                body:         body,
                senderName:   shipName,
                senderType:   "trade_ship",
                shipUid:      ship.uid,
                tick:         station.tick,
                expiresAtTick: station.tick + TicksPerDay);  // expires after 1 in-game day

            msg.responseOptions.Add(new Dictionary<string, object>
            {
                { "label",    "Sure, let's trade" },
                { "action",   "accept_trade" },
                { "iceQty",   (object)iceQty   },
                { "icePrice", (object)icePrice  },
                { "shipUid",  (object)ship.uid  }
            });
            msg.responseOptions.Add(new Dictionary<string, object>
            {
                { "label",   "No thank you" },
                { "action",  "decline"      },
                { "shipUid", (object)ship.uid }
            });
            msg.responseOptions.Add(new Dictionary<string, object>
            {
                { "label",   "Come back later" },
                { "action",  "later"           },
                { "shipUid", (object)ship.uid  }
            });

            station.AddMessage(msg);
            station.LogEvent($"Incoming transmission from {shipName}");
            Debug.Log($"[CommsSystem] Passing trade ship {shipName} sent message (ice×{iceQty} @ {icePrice:F2})");
        }

        // ── Reply handling ────────────────────────────────────────────────────

        public void ReplyToMessage(CommMessage msg, Dictionary<string, object> option,
                                    StationState station)
        {
            msg.read    = true;
            msg.replied = option.ContainsKey("action") ? option["action"].ToString() : "";

            switch (msg.replied)
            {
                case "accept_trade":
                    AcceptTrade(msg, option, station);
                    break;
                case "decline":
                    DeclineTrade(msg, option, station);
                    break;
                case "later":
                    LaterTrade(msg, option, station);
                    break;
                default:
                    Debug.LogWarning($"[CommsSystem] Unknown comms action: {msg.replied}");
                    break;
            }
        }

        private void AcceptTrade(CommMessage msg, Dictionary<string, object> option,
                                  StationState station)
        {
            int   iceQty   = option.ContainsKey("iceQty")   ? Convert.ToInt32(option["iceQty"])   : 0;
            float icePrice = option.ContainsKey("icePrice")  ? Convert.ToSingle(option["icePrice"]) : 2f;
            float total    = iceQty * icePrice;

            if (station.GetResource("credits") < total)
            {
                station.LogEvent(
                    $"Insufficient credits to buy Ice×{iceQty} " +
                    $"(need {total:F0}, have {station.GetResource("credits"):F0})");
                msg.replied = null; // allow retry
                return;
            }

            station.ModifyResource("credits", -total);
            // Deliver ice as the canonical station resource so it is immediately
            // visible in the resource bars and usable by all other systems.
            station.ModifyResource("ice", iceQty);

            string shipUid = option.ContainsKey("shipUid") ? option["shipUid"].ToString() : msg.shipUid;
            if (shipUid != null) station.RemoveShip(shipUid);

            station.LogEvent($"Trade accepted: Ice×{iceQty} delivered for {total:F0} credits.");
        }

        private void DeclineTrade(CommMessage msg, Dictionary<string, object> option,
                                   StationState station)
        {
            string shipUid = option.ContainsKey("shipUid") ? option["shipUid"].ToString() : msg.shipUid;
            if (shipUid != null) station.RemoveShip(shipUid);
            station.LogEvent($"Declined trade offer from {msg.senderName}.");
        }

        private void LaterTrade(CommMessage msg, Dictionary<string, object> option,
                                 StationState station)
        {
            string shipUid = option.ContainsKey("shipUid") ? option["shipUid"].ToString() : msg.shipUid;
            if (shipUid != null) station.RemoveShip(shipUid);
            station.LogEvent($"Asked {msg.senderName} to come back later. They acknowledged.");
        }

    }
}
