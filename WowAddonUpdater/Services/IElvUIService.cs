using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WowAddonUpdater.Services
{
    public interface IElvUIService : IDisposable
    {
        Task<JObject> GetElvUIInfo();
        Task<string> GetElvUIVersion();
        Task<string> GetElvUILastUpdate();
        Task<(string Version, string LastUpdate)> DownloadAndExtractElvUI(string wowPath);
    }
}