using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Export
{
    [System.Serializable]
    public class SidecarData
    {
        public string asset_id;
        public string asset_name;
        public string category;
        public string atlas;
        public SidecarSize tile_size = new SidecarSize { w = 64, h = 64 };
        public SidecarSize slot_size = new SidecarSize { w = 66, h = 66 };
        public int padding = 1;
        public List<SidecarTile> tiles = new List<SidecarTile>();
        public SidecarMeta meta = new SidecarMeta();
    }

    [System.Serializable]
    public class SidecarSize
    {
        public int w;
        public int h;
    }

    [System.Serializable]
    public class SidecarTile
    {
        public string id;
        public int col;
        public int row;
        public List<string> tags;
    }

    [System.Serializable]
    public class SidecarMeta
    {
        public string placement_surface = "any";
        public int south_slab_height = 5;
        public SidecarFootprint footprint;
        public SidecarPoint interaction_point;
        public List<SidecarOffset> clearance;
        public bool allow_rotation = true;
        public bool status_led;
        public SidecarPoint status_led_position;
    }

    [System.Serializable]
    public class SidecarFootprint
    {
        public int w = 1;
        public int h = 1;
    }

    [System.Serializable]
    public class SidecarPoint
    {
        public int x;
        public int y;
    }

    [System.Serializable]
    public class SidecarOffset
    {
        public int dx;
        public int dy;
    }

    public static class TileSidecarExporter
    {
        public static string ExportFloorSidecar(Creator.AssetDefinition def)
        {
            var sidecar = CreateBase(def);
            sidecar.tiles.Add(new SidecarTile { id = "normal", col = 0, row = 0, tags = new List<string> { "normal" } });
            sidecar.tiles.Add(new SidecarTile { id = "damaged", col = 1, row = 0, tags = new List<string> { "damaged" } });
            sidecar.tiles.Add(new SidecarTile { id = "destroyed", col = 2, row = 0, tags = new List<string> { "destroyed" } });
            return JsonUtility.ToJson(sidecar, true);
        }

        public static string ExportWallSidecar(Creator.AssetDefinition def)
        {
            var sidecar = CreateBase(def);
            string[] bitmaskNames = {
                "none", "n", "s", "ns", "e", "ne", "se", "nse",
                "w", "nw", "sw", "nsw", "ew", "new", "sew", "nsew"
            };
            for (int i = 0; i < 16; i++)
            {
                sidecar.tiles.Add(new SidecarTile
                {
                    id = bitmaskNames[i],
                    col = i,
                    row = 0,
                    tags = new List<string> { "wall", bitmaskNames[i] }
                });
            }
            sidecar.meta.south_slab_height = def.editor_state?.south_slab_height ?? 5;
            return JsonUtility.ToJson(sidecar, true);
        }

        public static string ExportFurnitureSidecar(Creator.AssetDefinition def, int directionCount, int stateCount)
        {
            var sidecar = CreateBase(def);
            var fp = def.editor_state?.footprint;
            int cellCount = (fp != null) ? fp.w * fp.h : 1;

            string[] directions = { "south", "north", "side_r", "side_l" };
            string[] states = { "idle", "active", "damaged", "destroyed", "powered_off", "broken" };

            for (int dir = 0; dir < directionCount && dir < directions.Length; dir++)
            {
                for (int state = 0; state < stateCount && state < states.Length; state++)
                {
                    for (int cell = 0; cell < cellCount; cell++)
                    {
                        int col = state * cellCount + cell;
                        sidecar.tiles.Add(new SidecarTile
                        {
                            id = $"{directions[dir]}_{states[state]}_c{cell}",
                            col = col,
                            row = dir,
                            tags = new List<string> { directions[dir], states[state] }
                        });
                    }
                }
            }

            if (fp != null)
            {
                sidecar.meta.footprint = new SidecarFootprint { w = fp.w, h = fp.h };
            }
            if (def.editor_state != null)
            {
                sidecar.meta.allow_rotation = def.editor_state.allow_rotation;
                sidecar.meta.status_led = def.editor_state.status_led_enabled;
                if (def.editor_state.interaction_point != null)
                    sidecar.meta.interaction_point = new SidecarPoint
                    {
                        x = def.editor_state.interaction_point.x,
                        y = def.editor_state.interaction_point.y
                    };
                if (def.editor_state.status_led_position != null)
                    sidecar.meta.status_led_position = new SidecarPoint
                    {
                        x = def.editor_state.status_led_position.x,
                        y = def.editor_state.status_led_position.y
                    };
                if (def.editor_state.clearance != null)
                {
                    sidecar.meta.clearance = new List<SidecarOffset>();
                    foreach (var c in def.editor_state.clearance)
                        sidecar.meta.clearance.Add(new SidecarOffset { dx = c.dx, dy = c.dy });
                }
            }

            return JsonUtility.ToJson(sidecar, true);
        }

        private static SidecarData CreateBase(Creator.AssetDefinition def)
        {
            return new SidecarData
            {
                asset_id = def.id,
                asset_name = def.name,
                category = def.type,
                atlas = def.id + ".png"
            };
        }
    }
}
