// SectorNoiseVisualizer — editor window that renders 2D previews of the five
// noise fields and archetype classification across a configurable grid.
//
// Menu: Waystation > Sector Noise Visualizer
// Features:
//   • Drop-down for world seed + field selector.
//   • 20×20 (or custom) grid rendered as coloured cells.
//   • Archetype colour overlay mode showing all ten archetypes.
//   • Generate button that logs field values and archetype to the console.

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Waystation.Models;
using Waystation.Systems;

namespace Waystation.Editor
{
    public class SectorNoiseVisualizer : EditorWindow
    {
        private int _worldSeed = 42;
        private int _gridSize  = 20;
        private int _selectedField = 5;  // 0-4 = individual fields, 5 = archetype view

        private static readonly string[] FieldLabels =
        {
            "Density", "Resources", "Hazard", "Faction Pressure", "Stellar Age", "Archetype"
        };

        private static readonly Color[] ArchetypeColors =
        {
            Color.gray,                                             // Unclassified
            new Color(0.165f, 0.290f, 0.416f, 1f),                // Confluence
            new Color(0.290f, 0.227f, 0.102f, 1f),                // MineralBelt
            new Color(0.30f, 0.05f, 0.50f, 1f),                   // SingularityReach
            new Color(0.15f, 0.15f, 0.25f, 1f),                   // RemnantsZone
            new Color(0.15f, 0.35f, 0.20f, 1f),                   // StormBelt
            new Color(0.35f, 0.15f, 0.50f, 1f),                   // NebulaField
            new Color(0.50f, 0.12f, 0.12f, 1f),                   // ContestedCore
            new Color(0.15f, 0.50f, 0.30f, 1f),                   // Cradle
            new Color(0.40f, 0.40f, 0.20f, 1f),                   // FrontierScatter
            new Color(0.20f, 0.20f, 0.20f, 1f),                   // VoidFringe
        };

        [MenuItem("Waystation/Sector Noise Visualizer")]
        public static void ShowWindow()
        {
            GetWindow<SectorNoiseVisualizer>("Sector Noise Visualizer");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            _worldSeed     = EditorGUILayout.IntField("World Seed", _worldSeed);
            _gridSize      = Mathf.Clamp(EditorGUILayout.IntField("Grid Size", _gridSize), 4, 40);
            _selectedField = EditorGUILayout.Popup("Field / View", _selectedField, FieldLabels);
            EditorGUILayout.Space(4);

            if (GUILayout.Button("Generate Sector at (0,0) — Log to Console"))
            {
                LogGeneratedSector(0, 0);
            }

            EditorGUILayout.Space(8);

            // Draw the grid.
            float availW = position.width - 20f;
            float availH = position.height - 120f;
            float cellSize = Mathf.Min(availW / _gridSize, availH / _gridSize, 24f);
            float offsetX = 10f;
            float offsetY = 110f;

            int halfGrid = _gridSize / 2;

            for (int gy = 0; gy < _gridSize; gy++)
            {
                for (int gx = 0; gx < _gridSize; gx++)
                {
                    int gridX = gx - halfGrid;
                    int gridY = gy - halfGrid;

                    Color cellColor;
                    if (_selectedField < 5)
                    {
                        // Individual noise field — grayscale intensity.
                        var field = (NoiseField)_selectedField;
                        float v = SectorNoiseFields.Sample(field, gridX, gridY, _worldSeed);
                        cellColor = Color.Lerp(Color.black, Color.white, v);
                    }
                    else
                    {
                        // Archetype classification view.
                        var nv = SectorNoiseFields.SampleAll(gridX, gridY, _worldSeed);
                        var arch = SectorClassifier.Classify(nv);
                        int idx = (int)arch;
                        cellColor = idx >= 0 && idx < ArchetypeColors.Length
                            ? ArchetypeColors[idx]
                            : Color.gray;
                    }

                    Rect r = new Rect(offsetX + gx * cellSize, offsetY + (_gridSize - 1 - gy) * cellSize,
                                      cellSize - 1, cellSize - 1);
                    EditorGUI.DrawRect(r, cellColor);
                }
            }

            // Legend (archetype mode only).
            if (_selectedField == 5)
            {
                float legendY = offsetY + _gridSize * cellSize + 8f;
                float legendX = 10f;
                var archetypes = System.Enum.GetValues(typeof(SectorArchetype));
                foreach (SectorArchetype a in archetypes)
                {
                    if (a == SectorArchetype.Unclassified) continue;
                    int idx = (int)a;
                    if (idx < 0 || idx >= ArchetypeColors.Length) continue;
                    Rect swatch = new Rect(legendX, legendY, 12, 12);
                    EditorGUI.DrawRect(swatch, ArchetypeColors[idx]);
                    GUI.Label(new Rect(legendX + 16, legendY - 1, 140, 16), a.ToString(),
                              EditorStyles.miniLabel);
                    legendX += 130f;
                    if (legendX + 130f > position.width)
                    {
                        legendX = 10f;
                        legendY += 16f;
                    }
                }
            }
        }

        private void LogGeneratedSector(int gridX, int gridY)
        {
            var nv = SectorNoiseFields.SampleAll(gridX, gridY, _worldSeed);
            var arch = SectorClassifier.Classify(nv);
            int count = SectorClassifier.ResolveSystemCount(arch, nv.density);

            Debug.Log($"[SectorNoiseVisualizer] Grid ({gridX},{gridY}) seed={_worldSeed}\n" +
                      $"  density={nv.density:F3}  resources={nv.resources:F3}  hazard={nv.hazard:F3}" +
                      $"  factionPressure={nv.factionPressure:F3}  stellarAge={nv.stellarAge:F3}\n" +
                      $"  archetype={arch}  systemCount={count}");
        }
    }
}
#endif
