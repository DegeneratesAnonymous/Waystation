using UnityEngine;

namespace Waystation.Creator
{
    public static class CreatorSettings
    {
        private const string KeyFirstLaunch = "creator_first_launch";
        private const string KeyFirstAsset = "creator_first_asset";
        private const string KeyTipsDismissed = "creator_tips_dismissed";
        private const string KeyToolExpander = "creator_tool_expander_open";
        private const string KeyLibraryView = "creator_library_view";
        private const string KeyLibrarySort = "creator_library_sort";

        public static bool FirstLaunch
        {
            get => PlayerPrefs.GetInt(KeyFirstLaunch, 1) == 1;
            set { PlayerPrefs.SetInt(KeyFirstLaunch, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool FirstAsset
        {
            get => PlayerPrefs.GetInt(KeyFirstAsset, 1) == 1;
            set { PlayerPrefs.SetInt(KeyFirstAsset, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool TipsDismissed
        {
            get => PlayerPrefs.GetInt(KeyTipsDismissed, 0) == 1;
            set { PlayerPrefs.SetInt(KeyTipsDismissed, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool ToolExpanderOpen
        {
            get => PlayerPrefs.GetInt(KeyToolExpander, 0) == 1;
            set { PlayerPrefs.SetInt(KeyToolExpander, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static string LibraryView
        {
            get => PlayerPrefs.GetString(KeyLibraryView, "grid");
            set { PlayerPrefs.SetString(KeyLibraryView, value); PlayerPrefs.Save(); }
        }

        public static string LibrarySort
        {
            get => PlayerPrefs.GetString(KeyLibrarySort, "date");
            set { PlayerPrefs.SetString(KeyLibrarySort, value); PlayerPrefs.Save(); }
        }

        // Dev mode is read from a config file, not PlayerPrefs
        public static bool DevMode
        {
            get
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "creator_config.json");
                if (!System.IO.File.Exists(path)) return false;
                // Simple check for "dev_mode": true in the file
                string content = System.IO.File.ReadAllText(path);
                return content.Contains("\"creator_dev_mode\": true") || content.Contains("\"creator_dev_mode\":true");
            }
        }
    }
}
