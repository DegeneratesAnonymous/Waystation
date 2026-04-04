using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using UnityEngine;

namespace Waystation.Creator.Workshop
{
    public static class WayCreator2DBundle
    {
        public const string Extension = ".wc2d";

        public static byte[] Pack(string assetDir)
        {
            if (!Directory.Exists(assetDir)) return null;

            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var file in Directory.GetFiles(assetDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        string fileName = Path.GetFileName(file);
                        var entry = zip.CreateEntry(fileName, System.IO.Compression.CompressionLevel.Optimal);
                        using (var entryStream = entry.Open())
                        {
                            byte[] data = File.ReadAllBytes(file);
                            entryStream.Write(data, 0, data.Length);
                        }
                    }
                }
                return ms.ToArray();
            }
        }

        public static bool Unpack(byte[] bundleData, string targetDir)
        {
            try
            {
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                using (var ms = new MemoryStream(bundleData))
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    foreach (var entry in zip.Entries)
                    {
                        // Security: prevent path traversal
                        string name = Path.GetFileName(entry.FullName);
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name.Contains("..")) continue;

                        string destPath = Path.Combine(targetDir, name);
                        using (var entryStream = entry.Open())
                        using (var fileStream = File.Create(destPath))
                        {
                            entryStream.CopyTo(fileStream);
                        }
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[WayCreator2DBundle] Unpack failed: {e.Message}");
                return false;
            }
        }

        public static string GetBundlePath(string assetName)
        {
            return Path.Combine(Application.persistentDataPath, "CreatorExports", assetName + Extension);
        }

        public static void SaveBundle(byte[] data, string assetName)
        {
            string dir = Path.Combine(Application.persistentDataPath, "CreatorExports");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, assetName + Extension), data);
        }
    }
}
