// Solar system data models — generated once per new game from the station seed
// and stored on StationState for deterministic, reproducible rendering.
//
// SolarSystemGenerator (Systems/) creates one of these; SystemMapController (UI/) renders it.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Models
{
    public enum StarType  { RedDwarf, YellowDwarf, BlueGiant, OrangeSubgiant, WhiteDwarf }
    public enum BodyType  { RockyPlanet, GasGiant, IcePlanet, AsteroidBelt }
    public enum MapLayer  { System, Sector, Galaxy }

    /// <summary>
    /// Detailed classification of a planetary body.
    /// Terrestrial T-I..T-VII, Gas Giants G-I..G-V, Ice Bodies I-I..I-III,
    /// Exotic E-I..E-V. AsteroidBelt has no PlanetClass (None).
    /// </summary>
    public enum PlanetClass
    {
        None = 0,
        // Terrestrial
        T1_BarrenRock,
        T2_Volcanic,
        T3_Desert,
        T4_Tectonic,
        T5_Oceanic,
        T6_Terran,
        T7_Frozen,
        // Gas Giants (Sudarsky)
        G1_AmmoniaCloud,
        G2_WaterCloud,
        G3_Cloudless,
        G4_AlkaliMetal,
        G5_SilicateCloud,
        // Ice Bodies
        I1_IceDwarf,
        I2_CryogenicMoon,
        I3_CometaryBody,
        // Exotic
        E1_Chthonian,
        E2_CarbonPlanet,
        E3_IronPlanet,
        E4_HeliumPlanet,
        E5_RogueBody,
    }

    [Serializable]
    public class SolarBody
    {
        public string       name;
        public BodyType     bodyType;
        /// <summary>Detailed classification — populated by SolarSystemGenerator.</summary>
        public PlanetClass  planetClass;

        // Orbital parameters
        public float        orbitalRadius;   // AU equivalents; used as display-space scale factor
        public float        orbitalPeriod;   // ticks for one complete orbit (Kepler: r^1.5 × 200)
        public float        initialPhase;    // radians — random offset so orbits start staggered

        // Visual
        public float        size;            // relative radius (planet) or 0 for belts
        public string       colorHex;        // HTML hex colour, e.g. "#CC8866"
        public bool         hasRings;        // gas giants and some ice planets may have rings

        // Gameplay
        public bool         stationIsHere;   // true on the body / belt closest to the station
        public List<string> tags = new List<string>();  // "habitable" | "rich_ore" | "gas_harvest"
                                                        // | "ice_deposits" | "ancient_ruins"
                                                        // | "storm_activity" | "subsurface_ocean"

        // Moons (rocky sub-bodies; only rocky planets and gas giants have them)
        public List<SolarBody> moons = new List<SolarBody>();
    }

    [Serializable]
    public class SolarSystemState
    {
        public string    starName;    // e.g. "Arcturus"
        public string    systemName;  // e.g. "Arcturus System"
        public int       seed;

        // Star
        public StarType  starType;
        public string    starColorHex;
        public float     starSize;       // relative display radius

        // Orbital bodies (planets, asteroid belts), ordered by ascending orbitalRadius
        public List<SolarBody> bodies = new List<SolarBody>();

        // Index into bodies[] of the body the station orbits near (-1 = open space)
        public int stationOrbitIndex = -1;
    }

    // A neighbouring star system for the Sector / Galaxy map
    [Serializable]
    public class NeighborSystem
    {
        public string   starName;    // e.g. "Arcturus"
        public string   systemName;  // e.g. "Arcturus System"
        public int      seed;
        public Vector2  positionLY;   // light-years relative to the home system (0,0)
        public StarType starType;
        public string   starColorHex;
        public float    starSize;
    }
}
