// SectorData — runtime state for a galaxy sector.
//
// Each sector has a permanent designation (SurveyPrefix + PhenomenonCodes + Coordinates)
// generated once by GalaxyGenerator and a player-editable ProperName.
//
// Discovery states:
//   Uncharted  — exists in galaxy data but player has no information.
//   Detected   — within Antenna range; designation, proper name, and codes are visible.
//   Visited    — player has sent a shuttle or established a station here.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Models
{
    // ── Enums ──────────────────────────────────────────────────────────────────

    public enum SurveyPrefix
    {
        GSC,  // Galactic Survey Commission — inner/mid galaxy (X < 40)
        FRN,  // Frontier Navigation Registry — edge space (X 40–70)
        ANC,  // Ancient Catalogue — pre-civilisation charts (~8% of sectors)
        IND,  // Independent Survey — player-discovered frontier
        UNK,  // Unknown Origin — deep void (X > 85, low density)
    }

    public enum PhenomenonCode
    {
        // Primary — stellar character (exactly 1 per sector)
        NB,  // Nebula
        PL,  // Pulsar
        BH,  // Black Hole
        DW,  // Dwarf Stars
        GI,  // Giant Stars
        MS,  // Main Sequence

        // Resource — prevailing body types (0–2 additional)
        OR,  // Ore-Rich
        IC,  // Ice-Rich
        GS,  // Gas-Rich
        VD,  // Void

        // Hazard — environmental conditions (0–2 additional)
        RD,  // Radiation
        GV,  // Gravitational
        DK,  // Dark Matter
        ST,  // Storm
    }

    public enum SectorDiscoveryState
    {
        Uncharted,  // no player information
        Detected,   // within Antenna range
        Visited,    // player has been here
    }

    /// <summary>
    /// How many star systems are packed into a sector.
    /// Determines the dot count rendered in the sector box and future
    /// gameplay effects (trade route density, spawn rates, etc.).
    /// </summary>
    public enum SystemDensity
    {
        Sparse,    // 3 –  6  systems
        Low,       // 7 – 10  systems
        Standard,  // 11 – 15  systems
        High,      // 16 – 20  systems
    }

    /// <summary>
    /// Environmental or political modifier present in a sector.
    /// None = standard space. 40 % of sectors receive one modifier.
    /// </summary>
    public enum SectorModifier
    {
        None = 0,
        // Physical
        Nebula,
        AsteroidBelt,
        DustCloud,
        PlanetaryRingDebris,
        CometaryTail,
        AccretionDisk,
        PulsarWash,
        MagnetarField,
        GravitationalLens,
        GravityWell,
        TidalShearZone,
        CosmicRaySurge,
        RadiationBelt,
        DarkMatterFilament,
        FrameDraggingAnomaly,
        GravitationalTimeDilation,
        EinsteinRosenRemnant,
        QuantumFoamPocket,
        HawkingRadiationZone,
        // Resource
        RichOreDeposit,
        IceField,
        GasPocket,
        SalvageGraveyard,
        DerelictStation,
        AncientRuins,
        BiologicalBloom,
        // Political
        ContestedSpace,
        ExclusionZone,
        QuarantineSeal,
        PatrolRoute,
    }

    // ── SectorData ────────────────────────────────────────────────────────────

    [Serializable]
    public class SectorData
    {
        // ── Permanent identity ─────────────────────────────────────────────────

        /// <summary>Unique identifier for this sector (generated from seed + index).</summary>
        public string uid;

        /// <summary>Galactic XY coordinates, 0.0–99.9 per axis. Home = (22f, 51f).</summary>
        public Vector2 coordinates;

        public SurveyPrefix surveyPrefix;

        /// <summary>1 Primary + 0–2 Resource/Hazard codes. Assigned once; never changes.</summary>
        public List<PhenomenonCode> phenomenonCodes = new List<PhenomenonCode>();

        /// <summary>
        /// Full designation code segment: e.g. "GSC-NB·OR".
        /// Generated once and cached; never recalculated.
        /// </summary>
        public string designationCode;

        // ── Mutable display ────────────────────────────────────────────────────

        /// <summary>Player-visible name. Generated at creation; player can rename.</summary>
        public string properName;

        /// <summary>True once the player has renamed this sector.</summary>
        public bool isRenamed;

        public SectorDiscoveryState discoveryState = SectorDiscoveryState.Uncharted;

        /// <summary>How densely packed with star systems this sector is.</summary>
        public SystemDensity systemDensity = SystemDensity.Standard;

        /// <summary>Environmental / political modifier for this sector. None = standard space.</summary>
        public SectorModifier modifier = SectorModifier.None;

        // ── Derived display helpers ────────────────────────────────────────────

        /// <summary>
        /// Coordinates formatted as XX.YY (zero-padded, integer-truncated per axis).
        /// X=22, Y=51 → "22.51". X=8, Y=7 → "08.07".
        /// </summary>
        public string CoordString()
        {
            int xi = Mathf.FloorToInt(coordinates.x);
            int yi = Mathf.FloorToInt(coordinates.y);
            return $"{xi:D2}.{yi:D2}";
        }

        /// <summary>Full format: GSC-NB·OR 22.51 "The Cradle"</summary>
        public string FullDesignation()
        {
            if (string.IsNullOrEmpty(properName)) return $"{designationCode} {CoordString()}";
            return $"{designationCode} {CoordString()} \"{properName}\"";
        }

        /// <summary>Short format: NB·OR 22.51 "The Cradle"  (strip survey prefix)</summary>
        public string ShortDesignation()
        {
            // designationCode is e.g. "GSC-NB·OR"; strip the prefix up to and including '-'
            int dash = designationCode.IndexOf('-');
            string codes = dash >= 0 ? designationCode[(dash + 1)..] : designationCode;
            if (string.IsNullOrEmpty(properName)) return $"{codes} {CoordString()}";
            return $"{codes} {CoordString()} \"{properName}\"";
        }

        /// <summary>Short codes + coordinate without proper name: NB·OR 22.51</summary>
        public string ShortCodeAndCoord()
        {
            int dash = designationCode.IndexOf('-');
            string codes = dash >= 0 ? designationCode[(dash + 1)..] : designationCode;
            return $"{codes} {CoordString()}";
        }

        /// <summary>Minimal format: 22.51 "The Cradle"</summary>
        public string MinimalDesignation()
        {
            if (string.IsNullOrEmpty(properName)) return CoordString();
            return $"{CoordString()} \"{properName}\"";
        }

        /// <summary>Code-only format: GSC-NB·OR 22.51  (no proper name, for comms/logs)</summary>
        public string CodeOnlyDesignation()
            => $"{designationCode} {CoordString()}";

        // ── Factory ────────────────────────────────────────────────────────────

        public static SectorData Create(string uid, Vector2 coordinates,
                                        SurveyPrefix prefix, List<PhenomenonCode> codes,
                                        string properName)
        {
            // Build the designation code string: PREFIX-CODE1[·CODE2[·CODE3]]
            var codeStrings = new List<string>();
            foreach (var c in codes)
                codeStrings.Add(c.ToString());
            string codeSegment = string.Join("·", codeStrings);
            string designationCode = $"{prefix}-{codeSegment}";

            return new SectorData
            {
                uid              = uid,
                coordinates      = coordinates,
                surveyPrefix     = prefix,
                phenomenonCodes  = new List<PhenomenonCode>(codes),
                designationCode  = designationCode,
                properName       = properName,
                isRenamed        = false,
                discoveryState   = SectorDiscoveryState.Uncharted,
                systemDensity    = SystemDensity.Standard,  // overwritten by GalaxyGenerator
            };
        }

        /// <summary>
        /// True when the player can rename this sector
        /// (Detected or Visited state only).
        /// </summary>
        public bool CanRename()
            => discoveryState == SectorDiscoveryState.Detected
            || discoveryState == SectorDiscoveryState.Visited;

        /// <summary>
        /// Apply a player rename. Truncates to 32 characters.
        /// Returns false and leaves name unchanged if newName is empty or sector is Uncharted.
        /// </summary>
        public bool TryRename(string newName)
        {
            if (!CanRename()) return false;
            if (string.IsNullOrWhiteSpace(newName)) return false;
            properName = newName.Length > 32 ? newName[..32] : newName;
            isRenamed  = true;
            return true;
        }

        /// <summary>True if this sector has the DK (Dark Matter) phenomenon code.</summary>
        public bool HasDarkMatter() => phenomenonCodes.Contains(PhenomenonCode.DK);

        // ── Quadrant helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Returns a quadrant key string for collision detection.
        /// The galaxy is divided into 4×4 quadrants of 25 coordinate units each.
        /// </summary>
        public string QuadrantKey()
        {
            int qx = Mathf.FloorToInt(coordinates.x / 25f);
            int qy = Mathf.FloorToInt(coordinates.y / 25f);
            return $"{qx}:{qy}";
        }
    }
}
