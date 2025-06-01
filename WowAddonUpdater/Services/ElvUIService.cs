using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WowAddonUpdater.Services
{
    public class ElvUIService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed = false;

        public ElvUIService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<JObject> GetElvUIInfo()
        {
            string response = null;
            JObject result = null;

            try
            {
                response = await _httpClient.GetStringAsync("https://api.tukui.org/v1/addon/elvui");
                result = JObject.Parse(response);
                return result;
            }
            finally
            {
                // MINNESRENSNING: Nollställning av svarsdata
                response = null;

                // Snabbare GC för API-anrop
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        public async Task<string> GetElvUIVersion()
        {
            JObject info = null;
            string version = null;

            try
            {
                info = await GetElvUIInfo();
                version = info["version"]?.ToString() ?? "Unknown";
                return version;
            }
            finally
            {
                // MINNESRENSNING: Uttrycklig nollställning
                info = null;
                version = null;
            }
        }

        public async Task<string> GetElvUILastUpdate()
        {
            JObject info = null;
            string lastUpdate = null;

            try
            {
                info = await GetElvUIInfo();
                lastUpdate = info["last_update"]?.ToString() ?? "";
                return lastUpdate;
            }
            finally
            {
                // MINNESRENSNING: Uttrycklig nollställning
                info = null;
                lastUpdate = null;
            }
        }

        public async Task<(string Version, string LastUpdate)> DownloadAndExtractElvUI(string wowPath)
        {
            JObject info = null;
            string downloadUrl = null;
            string version = null;
            string lastUpdate = null;
            string filename = null;
            byte[] fileData = null;
            FileStream tempFileStream = null; // För minneseffektiv filhantering

            try
            {
                // Minnesoptimering före nedladdning
                GC.Collect(1, GCCollectionMode.Optimized);

                info = await GetElvUIInfo();
                downloadUrl = info["url"]?.ToString();
                version = info["version"]?.ToString() ?? "Unknown";
                lastUpdate = info["last_update"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception("Could not get download URL from ElvUI API");
                }

                filename = Path.Combine(Path.GetTempPath(), $"elvui-{version}.zip");

                // Minneseffektiv nedladdning med stream istället för att ladda allt i minnet
                using (var httpResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    httpResponse.EnsureSuccessStatusCode();

                    using (var httpStream = await httpResponse.Content.ReadAsStreamAsync())
                    using (tempFileStream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
                    {
                        await httpStream.CopyToAsync(tempFileStream);
                    }
                }

                // Frigör file stream omedelbart
                tempFileStream?.Dispose();
                tempFileStream = null;

                // Snabb minnesrensning efter nedladdning
                GC.Collect(0, GCCollectionMode.Optimized);

                // Extrahera filen
                ZipFile.ExtractToDirectory(filename, wowPath, true);

                // Ta bort temporärfilen omedelbart
                try
                {
                    File.Delete(filename);
                }
                catch { /* Ignorera cleanup-fel */ }

                return (version, lastUpdate);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to download and install ElvUI: {ex.Message}");
            }
            finally
            {
                // MINNESRENSNING: Uttrycklig nollställning av alla objekt
                info = null;
                downloadUrl = null;
                version = null;
                lastUpdate = null;
                fileData = null;

                // Säkerställ att streams är stängda
                tempFileStream?.Dispose();
                tempFileStream = null;

                // Rensa tempfilen om den finns
                if (!string.IsNullOrEmpty(filename) && File.Exists(filename))
                {
                    try
                    {
                        File.Delete(filename);
                    }
                    catch { /* Ignorera cleanup-fel */ }
                }
                filename = null;

                // Aggressivare minnesrensning efter ElvUI-operationer
                GC.Collect(1, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        // Metod för att optimera minne
        public void OptimizeMemory()
        {
            try
            {
                // Rensa eventuella interna cachar (för tillfället finns det inga specifika cachar)

                // Tvinga garbage collection
                GC.Collect(0, GCCollectionMode.Optimized);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error optimizing ElvUIService memory: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // NOTE: Vi dispsar INTE HttpClient här längre eftersom den är delad
                // HttpClient ägs av HttpClientService

                // NY: Optimera minne vid dispose
                try
                {
                    OptimizeMemory();
                }
                catch { /* Ignorera cleanup-fel */ }

                // MINNESRENSNING: Tvinga GC vid borttagning av tjänst
                GC.Collect(0, GCCollectionMode.Optimized);
            }
            _disposed = true;
        }
    }
}