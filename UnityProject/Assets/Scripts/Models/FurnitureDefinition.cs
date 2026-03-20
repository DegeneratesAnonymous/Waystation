// FurnitureDefinition — serialisable data model for a multi-tile furniture asset.
//
// A furniture definition describes:
//   • the tile-grid footprint (occupied cells relative to an origin cell)
//   • per-cell sprite surfaces (top-face and south-face strip) for each perspective
//   • behaviour component wiring (power, light, storage, etc.)
//   • interaction points (editor-only overlays for gameplay logic attachment)
//   • animation trigger bindings (condition → clip)
//
// FurnitureDefinition is stored as a plain [Serializable] class so it can be
// round-tripped through JSON with the rest of the save data.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Models
{
    // ── Interaction point ─────────────────────────────────────────────────────

    public enum ApproachDirection { N, S, E, W, Any }

    [Serializable]
    public class InteractionPoint
    {
        public string            pointId           = "";
        public Vector2           localPosition     = Vector2.zero;
        public ApproachDirection approachDirection = ApproachDirection.Any;
        public string            interactionType   = "";
    }

    // ── Animation trigger ─────────────────────────────────────────────────────

    /// <summary>Engine-level states that can drive an animation trigger.</summary>
    public enum AnimationCondition
    {
        IsPowered,
        IsProcessing,
        IsOccupied,
        IsDamaged,
        IsOpen,
    }

    [Serializable]
    public class AnimationTriggerBinding
    {
        public AnimationCondition condition  = AnimationCondition.IsPowered;
        public string             clipRef    = "";   // animation clip identifier / path
    }

    // ── Behaviour components ──────────────────────────────────────────────────

    [Serializable]
    public class PowerComponent
    {
        public bool  enabled       = false;
        public float powerDraw     = 0f;   // watts
        public int   powerPriority = 0;
    }

    [Serializable]
    public class LightEmitterComponent
    {
        public bool  enabled        = false;
        public float lightRadius    = 3f;
        public Color lightColour    = Color.white;
        public float lightIntensity = 1f;
    }

    [Serializable]
    public class LightRequirementComponent
    {
        public bool  enabled       = false;
        public float minLuxRequired = 10f;
    }

    [Serializable]
    public class StorageComponent
    {
        public bool         enabled                = false;
        public int          storageCapacity        = 10;
        public List<string> acceptedItemCategories = new List<string>();
    }

    [Serializable]
    public class EnvironmentRequirementComponent
    {
        public bool  enabled           = false;
        public float minTemperature    = -40f;
        public float maxTemperature    = 60f;
        public bool  requiresAtmosphere = true;
    }

    [Serializable]
    public class InputRequirementComponent
    {
        public bool   enabled              = false;
        public string inputItemCategory    = "";
        public float  inputRatePerSecond   = 0f;
    }

    [Serializable]
    public class ProcessingOutputComponent
    {
        public bool   enabled               = false;
        public string outputItemCategory    = "";
        public float  outputRatePerSecond   = 0f;
    }

    [Serializable]
    public class FurnitureBehaviourComponents
    {
        public PowerComponent                power              = new PowerComponent();
        public LightEmitterComponent         lightEmitter       = new LightEmitterComponent();
        public LightRequirementComponent     lightRequirement   = new LightRequirementComponent();
        public StorageComponent              storage            = new StorageComponent();
        public EnvironmentRequirementComponent environmentReq   = new EnvironmentRequirementComponent();
        public InputRequirementComponent     inputRequirement   = new InputRequirementComponent();
        public ProcessingOutputComponent     processingOutput   = new ProcessingOutputComponent();
        // TODO: stub additional behaviour components here as needed.
    }

    // ── Per-cell sprite surface ───────────────────────────────────────────────

    public enum FurniturePerspective { Horizontal, Vertical }

    [Serializable]
    public class FurnitureCellSurface
    {
        public Vector2Int         cell        = Vector2Int.zero;
        public FurniturePerspective perspective = FurniturePerspective.Horizontal;

        // Top face: 64×64 PNG pixel data (null = not yet authored)
        public byte[] topFacePng    = null;

        // South face strip: 64×N PNG pixel data (null = not yet authored)
        public byte[] southFacePng  = null;
    }

    // ── Furniture definition (top-level asset) ────────────────────────────────

    [Serializable]
    public class FurnitureDefinition
    {
        // ── Identity ──────────────────────────────────────────────────────────
        public string assetId   = "";
        public string assetName = "New Furniture";

        // ── Footprint ─────────────────────────────────────────────────────────
        /// <summary>List of occupied tile cells relative to the origin cell.</summary>
        public List<Vector2Int> occupiedCells = new List<Vector2Int> { Vector2Int.zero };

        /// <summary>The origin cell used as the world-placement anchor.</summary>
        public Vector2Int originCell = Vector2Int.zero;

        // ── Sprite surfaces ───────────────────────────────────────────────────
        /// <summary>
        /// Per-cell sprite surfaces; each combination of (cell, perspective) has
        /// its own entry.  The editor manages this list.
        /// </summary>
        public List<FurnitureCellSurface> cellSurfaces = new List<FurnitureCellSurface>();

        // ── Behaviour wiring ──────────────────────────────────────────────────
        public FurnitureBehaviourComponents behaviourComponents = new FurnitureBehaviourComponents();

        // ── Interaction points ────────────────────────────────────────────────
        public List<InteractionPoint> interactionPoints = new List<InteractionPoint>();

        // ── Animation trigger bindings ────────────────────────────────────────
        public List<AnimationTriggerBinding> animationTriggers = new List<AnimationTriggerBinding>();

        // ── Tags ──────────────────────────────────────────────────────────────
        public List<string> tags = new List<string>();

        // ── Factories ─────────────────────────────────────────────────────────

        public static FurnitureDefinition CreateBlank()
        {
            return new FurnitureDefinition
            {
                assetId = Guid.NewGuid().ToString("N")[..12],
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Returns true when the given cell is in the occupied list.</summary>
        public bool IsOccupied(Vector2Int cell)
            => occupiedCells.Contains(cell);

        /// <summary>
        /// Returns the FurnitureCellSurface for the given (cell, perspective),
        /// creating and adding one if it doesn't exist yet.
        /// </summary>
        public FurnitureCellSurface GetOrCreateSurface(Vector2Int cell, FurniturePerspective perspective)
        {
            foreach (var s in cellSurfaces)
                if (s.cell == cell && s.perspective == perspective) return s;

            var ns = new FurnitureCellSurface { cell = cell, perspective = perspective };
            cellSurfaces.Add(ns);
            return ns;
        }
    }
}
