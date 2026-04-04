using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.DevMode
{
    public static class BatchExporter
    {
        public static int ExportAll(AssetLibrary library)
        {
            int count = 0;
            string exportDir = Path.Combine(Application.persistentDataPath, "CreatorExports", "batch");
            if (!Directory.Exists(exportDir))
                Directory.CreateDirectory(exportDir);

            foreach (var def in library.Assets)
            {
                string assetDir = library.GetAssetDirectory(def);
                var bundleData = Workshop.WayCreator2DBundle.Pack(assetDir);
                if (bundleData != null)
                {
                    string safeName = SanitizeFileName(def.name);
                    File.WriteAllBytes(Path.Combine(exportDir, safeName + Workshop.WayCreator2DBundle.Extension), bundleData);
                    count++;
                }
            }

            Debug.Log($"[BatchExporter] Exported {count} assets to {exportDir}");
            return count;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
