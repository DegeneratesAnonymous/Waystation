using UnityEngine;

namespace Waystation.Creator
{
    public static class AssetDefinitionSerializer
    {
        public static string Serialize(AssetDefinition def)
        {
            def.modified = System.DateTime.UtcNow.ToString("o");
            return JsonUtility.ToJson(def, true);
        }

        public static AssetDefinition Deserialize(string json)
        {
            return JsonUtility.FromJson<AssetDefinition>(json);
        }
    }
}
