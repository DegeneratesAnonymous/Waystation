using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.TileEditor.Export
{
    [System.Serializable]
    public class SidecarData
    {
        public string asset_id;
        public string asset_name;
        public string asset_type;
        public string atlas_file;
        public int tile_size = 64;
        public int slot_size = 66;
        public List<SidecarFrame> frames = new List<SidecarFrame>();
        public SidecarMeta meta = new SidecarMeta();
    }

    [System.Serializable]
    public class SidecarFrame
    {
        public string key;
        public int x;
        public int y;
        public int w = 64;
        public int h = 64;
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
            sidecar.frames.Add(new SidecarFrame { key = "normal", x = 1, y = 1 });
            sidecar.frames.Add(new SidecarFrame { key = "damaged", x = 67, y = 1 });
            sidecar.frames.Add(new SidecarFrame { key = "destroyed", x = 133, y = 1 });
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
                sidecar.frames.Add(new SidecarFrame
                {
                    key = bitmaskNames[i],
                    x = i * 66 + 1,
                    y = 1
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
                        sidecar.frames.Add(new SidecarFrame
                        {
                            key = $"{directions[dir]}_{states[state]}_c{cell}",
                            x = col * 66 + 1,
                            y = dir * 66 + 1
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
                asset_type = def.type,
                atlas_file = def.id + ".png"
            };
        }
    }
}
