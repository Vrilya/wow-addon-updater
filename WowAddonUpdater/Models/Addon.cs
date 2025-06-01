using System;
using System.Collections.Generic;

namespace WowAddonUpdater.Models
{
    public class Addon
    {
        public string Name { get; set; }
        public string LocalVersion { get; set; }
        public string OnlineVersion { get; set; }
        public bool NeedsUpdate { get; set; }
        public DateTime? LastUpdated { get; set; }
        public int? Id { get; set; }
        public int? FileId { get; set; }
        public List<string> Folders { get; set; } = new List<string>();
        public string Status => NeedsUpdate ? $"{LocalVersion} → {OnlineVersion}" : LocalVersion;

        // Installationsinformation
        public string InstallationId { get; set; }
        public string InstallationName { get; set; }

        // Formatterad last updated text för UI
        public string LastUpdatedText
        {
            get
            {
                return LastUpdated.HasValue
                    ? LastUpdated.Value.ToString("yyyy-MM-dd HH:mm")
                    : "Never";
            }
        }
    }
}