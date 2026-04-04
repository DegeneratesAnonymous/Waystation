using System.IO;
using UnityEngine;

namespace Waystation.Creator.DevMode
{
    public class SidecarJsonEditor
    {
        public string RawJson { get; set; }
        public bool IsValid { get; private set; }
        public string ValidationError { get; private set; }

        public void LoadFromAsset(string assetDir)
        {
            string path = Path.Combine(assetDir, "sidecar.json");
            if (File.Exists(path))
                RawJson = File.ReadAllText(path);
            else
                RawJson = "{}";
            Validate();
        }

        public void Validate()
        {
            try
            {
                // Use Unity's JsonUtility just to check if it parses
                var test = JsonUtility.FromJson<TileEditor.Export.SidecarData>(RawJson);
                IsValid = test != null;
                ValidationError = IsValid ? null : "Invalid JSON structure";
            }
            catch (System.Exception e)
            {
                IsValid = false;
                ValidationError = e.Message;
            }
        }

        public bool Save(string assetDir)
        {
            Validate();
            if (!IsValid) return false;
            string path = Path.Combine(assetDir, "sidecar.json");
            File.WriteAllText(path, RawJson);
            return true;
        }
    }
}
