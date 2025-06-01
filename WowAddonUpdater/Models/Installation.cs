using System.Collections.Generic;
using Newtonsoft.Json;

namespace WowAddonUpdater.Models
{
    public class Installation
    {
        [JsonProperty("id")]
        public string Id { get; set; } = System.Guid.NewGuid().ToString();

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("addon_path")]
        public string AddonPath { get; set; } = "";

        [JsonProperty("game_version_id")]
        public int GameVersionId { get; set; } = 0;

        [JsonProperty("include_elvui")]
        public bool IncludeElvUI { get; set; } = false;

        [JsonProperty("color_hex")]
        public string ColorHex { get; set; } = "";

        [JsonProperty("addons")]
        public Dictionary<string, AddonConfig> Addons { get; set; } = new Dictionary<string, AddonConfig>();

        [JsonProperty("folder_mapping")]
        public Dictionary<string, List<string>> FolderMapping { get; set; } = new Dictionary<string, List<string>>();

        public override string ToString()
        {
            return Name;
        }
    }
}