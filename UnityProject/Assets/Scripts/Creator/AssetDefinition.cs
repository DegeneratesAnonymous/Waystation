using System.Collections.Generic;

namespace Waystation.Creator
{
    [System.Serializable]
    public class AssetDefinition
    {
        public string id;           // UUID v4
        public string name;
        public string type;         // "floor_tile" | "wall_tile" | "furniture"
        public List<string> tags = new List<string>();
        public int version = 1;
        public string created;      // ISO-8601
        public string modified;
        public string author = "local";
        public string workshop_id;
        public bool template;
        public string description = "";
        public EditorState editor_state = new EditorState();
    }

    [System.Serializable]
    public class EditorState
    {
        public string last_tool = "pencil";
        public int last_colour_index;
        public int zoom_level = 8;
        public int active_variant;
        public string placement_surface = "any"; // any | interior | exterior | vacuum
        public int south_slab_height = 5;
        public bool room_preview_open;
        public bool damage_states_enabled;
        public List<CustomSwatch> custom_palette = new List<CustomSwatch>();
        // Furniture-specific
        public FootprintData footprint;
        public PointData interaction_point;
        public List<OffsetData> clearance;
        public bool allow_rotation = true;
        public bool status_led_enabled;
        public PointData status_led_position;
    }

    [System.Serializable]
    public class CustomSwatch
    {
        public string name;
        public string hex;
    }

    [System.Serializable]
    public class FootprintData
    {
        public int w = 1;
        public int h = 1;
        public bool[] cells; // flattened w*h grid, true = occupied
    }

    [System.Serializable]
    public class PointData
    {
        public int x;
        public int y;
    }

    [System.Serializable]
    public class OffsetData
    {
        public int dx;
        public int dy;
    }
}
