using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Export
{
    public class ValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class ExportValidator
    {
        public static ValidationResult ValidateFloor(TileEditorController controller)
        {
            var result = new ValidationResult();
            if (!controller.HasVariantContent(0))
                result.Errors.Add("Normal variant has no content — at least the base tile must be drawn.");
            return result;
        }

        public static ValidationResult ValidateWall(TileEditorController controller)
        {
            var result = new ValidationResult();
            // Minimum: variant 0 (none), 3 (NS), 12 (EW) must have content
            if (!controller.HasVariantContent(0))
                result.Errors.Add("Variant 0 (None) must have content.");
            if (!controller.HasVariantContent(3))
                result.Warnings.Add("Variant 3 (NS) is empty — consider using auto-generate.");
            if (!controller.HasVariantContent(12))
                result.Warnings.Add("Variant 12 (EW) is empty — consider using auto-generate.");

            int emptyCount = 0;
            for (int i = 0; i < 16; i++)
                if (!controller.HasVariantContent(i)) emptyCount++;
            if (emptyCount > 0)
                result.Errors.Add($"{emptyCount} of 16 wall variants are empty — all variants must have content (use auto-generate for missing variants).");

            return result;
        }

        public static ValidationResult ValidateFurniture(TileEditorController controller, Creator.AssetDefinition def)
        {
            var result = new ValidationResult();
            if (!controller.HasVariantContent(0))
                result.Errors.Add("South/Idle variant must have content.");

            if (def.editor_state?.footprint != null)
            {
                var fp = def.editor_state.footprint;
                if (fp.w < 1 || fp.w > 4 || fp.h < 1 || fp.h > 4)
                    result.Errors.Add("Footprint must be 1-4 tiles in each dimension.");
            }

            return result;
        }
    }
}
