using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace WowAddonUpdater.Models
{
    public class AddonDatabase
    {
        [JsonProperty("addons")]
        public List<AddonDatabaseEntry> Addons { get; set; } = new List<AddonDatabaseEntry>();
    }

    public class AddonDatabaseEntry
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("versions")]
        public Dictionary<string, AddonVersionInfo> Versions { get; set; } = new Dictionary<string, AddonVersionInfo>();
    }

    public class AddonVersionInfo
    {
        [JsonProperty("folders")]
        public List<string> Folders { get; set; } = new List<string>();

        [JsonProperty("upload_date")]
        public string UploadDate { get; set; }
    }
}