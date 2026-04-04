// VisitorModels — data structures for the extended visitor lifecycle (WO-FAC-007).
using System.Collections.Generic;

namespace Waystation.Systems
{
    // ── Approach Stages ───────────────────────────────────────────────────
    public enum ApproachStage
    {
        Contact,     // Enters sensor range — faction flag + class visible
        Inbound,     // Course set for station — course line on map, partial manifest
        Approach,    // Within 1 sector — full manifest preview, ETA, docking policy fires
        Holding,     // Bays full — holding pattern, ETA based on queue
        Docking,     // Bay assigned — docking animation
        Docked,      // On station — NPCs active, trade window open
        Departing,   // Trade complete — crew returns to ship
        Departed     // Left sensor range — deregistered
    }

    // ── Docking Policy ────────────────────────────────────────────────────
    public enum DockingPolicy
    {
        Open,              // Auto-dock
        Inspect,           // Dock but cargo flagged for inspection
        ApproveRequired,   // Player must manually approve
        Deny,              // Turned away (reputation hit)
        Hostile            // Treated as hostile contact
    }

    public enum DockingOverride
    {
        None,
        Approve,
        Deny,
        Inspect,
        Hold,           // Dock but hold crew on board
        PriorityDock    // Jump the queue
    }

    // ── Visitor Intent Base ───────────────────────────────────────────────
    public class VisitorIntent
    {
        public string type = "unknown";
    }

    public class TraderVisitorIntent : VisitorIntent
    {
        public TraderManifest manifest;
        public TraderVisitorIntent() { type = "trader"; }
    }

    public class DiplomatVisitorIntent : VisitorIntent
    {
        public string message;
        public List<string> offers = new List<string>();
        public bool negotiationAvailable;
        public DiplomatVisitorIntent() { type = "diplomat"; }
    }

    public class RefugeeVisitorIntent : VisitorIntent
    {
        public int crewCount;
        public bool injured;
        public RefugeeVisitorIntent() { type = "refugee"; }
    }

    public class RaiderVisitorIntent : VisitorIntent
    {
        public int probeStrength;
        public RaiderVisitorIntent() { type = "raider"; }
    }

    public class InspectorVisitorIntent : VisitorIntent
    {
        public InspectorVisitorIntent() { type = "inspector"; }
    }

    public class SmugglerVisitorIntent : VisitorIntent
    {
        public TraderManifest manifest;
        public List<string> contraband = new List<string>();
        public SmugglerVisitorIntent() { type = "smuggler"; }
    }

    public class MedicalVisitorIntent : VisitorIntent
    {
        public int urgency = 1; // 1-3 severity
        public MedicalVisitorIntent() { type = "medical"; }
    }

    public class UnknownVisitorIntent : VisitorIntent
    {
        public string actualType;
        public string actualFaction;
        public bool revealed;
        public UnknownVisitorIntent() { type = "unknown"; }
    }

    public class PasserbyVisitorIntent : VisitorIntent
    {
        public PasserbyVisitorIntent() { type = "passerby"; }
    }

    // ── Manifest Preview ──────────────────────────────────────────────────
    /// <summary>Partial or full manifest data visible at various approach stages.</summary>
    public class ManifestPreview
    {
        public string shipName;
        public string factionId;
        public string visitorType;
        public int    crewCount;
        public float  estimatedDockingDuration;
        public float  reputationScore;

        // Partial (Inbound stage): cargo class without quantities
        public List<string> cargoClasses = new List<string>();
        // Full (Approach stage): complete cargo + want list
        public List<CargoEntry> fullCargo = new List<CargoEntry>();
        public List<WantEntry>  fullWantList = new List<WantEntry>();

        // Intent summary for non-traders
        public string intentSummary;
    }
}
