using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace WowAddonUpdater.Models
{
    public class Config
    {
        [JsonProperty("settings")]
        public Settings Settings { get; set; } = new Settings();

        [JsonProperty("installations")]
        public Dictionary<string, Installation> Installations { get; set; } = new Dictionary<string, Installation>();

        [JsonProperty("active_installation_id")]
        public string ActiveInstallationId { get; set; } = "";

        // Stöd för äldre format... kommer att migreras till nytt format
        [JsonProperty("addons")]
        public Dictionary<string, AddonConfig> Addons { get; set; } = new Dictionary<string, AddonConfig>();

        [JsonProperty("folder_mapping")]
        public Dictionary<string, List<string>> FolderMapping { get; set; } = new Dictionary<string, List<string>>();

        // Hjälpmetod för att hämta aktiv installation
        public Installation GetActiveInstallation()
        {
            if (!string.IsNullOrEmpty(ActiveInstallationId) && Installations.ContainsKey(ActiveInstallationId))
            {
                return Installations[ActiveInstallationId];
            }

            // Returnera första installationen om ingen aktiv är angiven
            return Installations.Values.FirstOrDefault();
        }

        // Hjälpmetod för att ange aktiv installation
        public void SetActiveInstallation(string installationId)
        {
            if (Installations.ContainsKey(installationId))
            {
                ActiveInstallationId = installationId;
            }
        }

        // Hjälpmetod för att hämta alla addons från alla installationer
        public Dictionary<string, (AddonConfig addon, string installationName)> GetAllAddons()
        {
            var allAddons = new Dictionary<string, (AddonConfig, string)>();

            foreach (var installation in Installations.Values)
            {
                foreach (var addon in installation.Addons)
                {
                    string key = $"{installation.Id}_{addon.Key}";
                    allAddons[key] = (addon.Value, installation.Name);
                }
            }

            return allAddons;
        }
    }

    public class Settings
    {
        [JsonProperty("minimize_to_tray")]
        public bool MinimizeToTray { get; set; }

        [JsonProperty("start_with_windows")]
        public bool StartWithWindows { get; set; }

        [JsonProperty("start_minimized")]
        public bool StartMinimized { get; set; }

        // Inställningar för auto scan (globala)
        [JsonProperty("auto_scan_enabled")]
        public bool AutoScanEnabled { get; set; }

        [JsonProperty("auto_scan_interval_minutes")]
        public int AutoScanIntervalMinutes { get; set; } = 360;

        [JsonProperty("auto_update_after_scan")]
        public bool AutoUpdateAfterScan { get; set; }

        // Sorteringspreferens för addons
        [JsonProperty("addon_sort_mode")]
        public AddonSortMode AddonSortMode { get; set; } = AddonSortMode.Name;

        // Äldre egenskaper – kommer att migreras till installationsobjekt
        [JsonProperty("addon_path")]
        public string AddonPath { get; set; } = "";

        [JsonProperty("include_elvui")]
        public bool IncludeElvUI { get; set; }

        [JsonProperty("game_version_id")]
        public int GameVersionId { get; set; } = 0;

        [JsonProperty("use_custom_user_agent")]
        public bool UseCustomUserAgent { get; set; } = false;

        [JsonProperty("custom_user_agent")]
        public string CustomUserAgent { get; set; } = ""; // Tom som default
    }

    public class AddonConfig
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("modified_date")]
        public string ModifiedDate { get; set; }

        [JsonProperty("online_modified_date")]
        public string OnlineModifiedDate { get; set; }

        [JsonProperty("update_available")]
        public bool UpdateAvailable { get; set; }

        [JsonProperty("last_checked")]
        public string LastChecked { get; set; }

        [JsonProperty("online_version")]
        public string OnlineVersion { get; set; }

        [JsonProperty("local_version")]
        public string LocalVersion { get; set; }
    }
}