using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WowAddonUpdater.Models;

namespace WowAddonUpdater.Services
{
    public class CurseForgeService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed = false;

        public CurseForgeService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<List<JObject>> SearchAddons(string query, int gameVersionId)
        {
            string url = "https://www.curseforge.com/api/v1/mods/search";
            var parameters = new Dictionary<string, string>
    {
        { "gameId", "1" },
        { "index", "0" },
        { "classId", "1" },
        { "filterText", query },
        { "pageSize", "50" },
        { "sortField", "1" },
        { "gameFlavors[0]", gameVersionId.ToString() } // Använd vald spelversion
    };

            string queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
            string requestUrl = $"{url}?{queryString}";

            string response = null;
            JObject jsonData = null;
            List<JObject> result = null;

            try
            {
                // Minnesoptimering före API-anrop
                GC.Collect(0, GCCollectionMode.Optimized);

                response = await _httpClient.GetStringAsync(requestUrl);
                jsonData = JObject.Parse(response);

                // Kontrollera om 'data' existerar och är en array
                if (jsonData["data"] == null)
                {
                    return new List<JObject>();
                }

                try
                {
                    result = jsonData["data"].ToObject<List<JObject>>() ?? new List<JObject>();
                }
                catch
                {
                    // Om vi inte kan konvertera data till List<JObject>, returnera en tom lista
                    result = new List<JObject>();
                }

                return result;
            }
            finally
            {
                // MINNESRENSNING: Explicit nollställning av stora objekt
                if (response != null)
                {
                    response = null;
                }

                jsonData = null;

                // NY: Mer effektiv minnesrensning för API-anrop
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        public async Task<JObject> GetAddonDetails(int addonId)
        {
            string url = $"https://www.curseforge.com/api/v1/mods/{addonId}/files?pageSize=20&index=0";
            string response = null;
            JObject result = null;

            try
            {
                response = await _httpClient.GetStringAsync(url);
                result = JObject.Parse(response);
                return result;
            }
            finally
            {
                // MINNESRENSNING: Explicit nollställning av stora objekt
                response = null;

                // Snabbare GC för små API-anrop
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        public async Task<(string Version, bool Success, List<string> Folders)> DownloadAndInstallAddon(int addonId, int fileId, string wowPath)
        {
            string url = null;
            string response = null;
            JObject jsonData = null;
            string fileName = null;
            string downloadUrl = null;
            string tempPath = null;
            byte[] fileBytes = null;
            List<string> folderNames = null;
            ZipArchive archive = null;
            FileStream tempFileStream = null; // För minneseffektiv filhantering

            try
            {
                // Minnesoptimering före nedladdning
                GC.Collect(1, GCCollectionMode.Optimized);

                url = $"https://www.curseforge.com/api/v1/mods/{addonId}/files/{fileId}";
                response = await _httpClient.GetStringAsync(url);
                jsonData = JObject.Parse(response);

                fileName = jsonData["data"]["fileName"]?.ToString() ?? $"addon_{fileId}.zip";
                tempPath = Path.Combine(Path.GetTempPath(), fileName);

                // Robust nedladdning med fallback URLs
                bool downloadSuccessful = await TryDownloadWithFallbacks(fileId, fileName, tempPath);

                if (!downloadSuccessful)
                {
                    throw new Exception("Failed to download file from all possible URLs");
                }

                // Frigör file stream omedelbart
                tempFileStream?.Dispose();
                tempFileStream = null;

                // Snabb minnesrensning efter nedladdning
                GC.Collect(0, GCCollectionMode.Optimized);

                // Hämta mappnamn innan extrahering
                folderNames = new List<string>();
                using (archive = ZipFile.OpenRead(tempPath))
                {
                    var entries = archive.Entries
                        .Where(e => e.FullName.Contains('/'))
                        .Select(e => e.FullName.Split('/')[0])
                        .Distinct();

                    folderNames.AddRange(entries);
                }

                // Arkivet kommer att frigöras via using-satsen
                archive = null;

                // Extrahera filen
                ZipFile.ExtractToDirectory(tempPath, wowPath, true);

                // Rensa temporärfil omedelbart
                try
                {
                    File.Delete(tempPath);
                }
                catch { /* Ignorera cleanup-fel */ }

                return (fileName, true, folderNames);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to download and install addon: {ex.Message}");
            }
            finally
            {
                // MINNESRENSNING: Explicit nollställning av alla stora objekt
                response = null;
                jsonData = null;
                fileName = null;
                downloadUrl = null;
                fileBytes = null;
                folderNames = null;

                // Säkerställ att streams är stängda
                tempFileStream?.Dispose();
                tempFileStream = null;

                archive?.Dispose();
                archive = null;

                // Clean up temp file if it exists
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { /* Ignorera cleanup-fel */ }
                }
                tempPath = null;

                // Aggressivare minnesrensning efter nedladdningsoperationer
                GC.Collect(1, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        public string GetVersionFromToc(string addonName, string wowPath, List<string> folders)
        {
            if (folders == null) return null;

            foreach (string folder in folders)
            {
                try
                {
                    string folderPath = Path.Combine(wowPath, folder);
                    if (!Directory.Exists(folderPath))
                        continue;

                    string[] tocFiles = Directory.GetFiles(folderPath, "*.toc");
                    if (tocFiles.Length == 0)
                        continue;

                    string content = File.ReadAllText(tocFiles[0]);
                    Match match = Regex.Match(content, @"## Version:\s*(.+)");
                    if (match.Success)
                    {
                        return NormalizeVersion(match.Groups[1].Value);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading TOC for {folder}: {ex.Message}");
                }
            }

            return null;
        }

        public string NormalizeVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
                return null;

            // Ta bort v eller V från början av versionssträngen
            version = Regex.Replace(version, @"^[vV#]", "").Trim();

            // Ta bort suffixen -Retail, -Cata och -Era
            version = Regex.Replace(version, @"-(?:Retail|Cata|Era)(?:\s*)?$", "");

            return version.Trim();
        }

        // Metod för att ge andra tjänster tillgång till delad HttpClient
        public HttpClient GetHttpClient()
        {
            return _httpClient;
        }

        // Metod för att rensa interna cachar och optimera minne
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
                Console.WriteLine($"Error optimizing CurseForgeService memory: {ex.Message}");
            }
        }

        // Robust nedladdningsmetod med fallback URLs
        private async Task<bool> TryDownloadWithFallbacks(int fileId, string fileName, string tempPath)
        {
            string fileIdStr = fileId.ToString();

            // Lista över CDN endpoints att prova
            string[] cdnEndpoints = {
                "https://edge.forgecdn.net/files",
                "https://mediafilez.forgecdn.net/files"
            };

            // För varje CDN - prova vanliga metoden först, sedan alla split-varianter
            foreach (string cdnEndpoint in cdnEndpoints)
            {
                // 1. Prova vanliga metoden först (första 4 siffror / resten)
                if (fileIdStr.Length >= 4)
                {
                    string firstPart = fileIdStr.Substring(0, 4);
                    string secondPart = fileIdStr.Substring(4);
                    string downloadUrl = $"{cdnEndpoint}/{firstPart}/{secondPart}/{fileName}";

                    if (await TryDownloadFromUrl(downloadUrl, tempPath))
                    {
                        Console.WriteLine($"Successfully downloaded using standard method from: {downloadUrl}");
                        return true;
                    }
                }

                // 2. Om vanliga metoden misslyckades, prova alla andra split-varianter
                List<(int firstPart, int secondPart)> alternativeSplits = GenerateAlternativeUrlSplits(fileIdStr);

                foreach (var (firstPart, secondPart) in alternativeSplits)
                {
                    string downloadUrl = $"{cdnEndpoint}/{firstPart}/{secondPart}/{fileName}";

                    if (await TryDownloadFromUrl(downloadUrl, tempPath))
                    {
                        Console.WriteLine($"Successfully downloaded using alternative split from: {downloadUrl}");
                        return true;
                    }
                }
            }

            return false; // Alla nedladdningsförsök misslyckades
        }

        // NY: Försöker ladda ner från en specifik URL
        private async Task<bool> TryDownloadFromUrl(string downloadUrl, string tempPath)
        {
            try
            {
                Console.WriteLine($"Trying download URL: {downloadUrl}");

                using (var httpResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    // Om HTTP status är OK, ladda ner filen
                    if (httpResponse.IsSuccessStatusCode)
                    {
                        using (var httpStream = await httpResponse.Content.ReadAsStreamAsync())
                        using (var tempFileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
                        {
                            await httpStream.CopyToAsync(tempFileStream);
                        }

                        return true; // Nedladdning lyckades!
                    }
                }
            }
            catch (Exception ex)
            {
                // Logga felet men fortsätt med nästa URL
                Console.WriteLine($"Failed to download from {downloadUrl}: {ex.Message}");
            }

            return false;
        }

        // Genererar alternativa URL-splittningar (UTAN vanliga metoden)
        private List<(int firstPart, int secondPart)> GenerateAlternativeUrlSplits(string fileIdStr)
        {
            var splits = new List<(int firstPart, int secondPart)>();

            // Prova alla möjliga splittningar UTOM position 4 (vanliga metoden)
            for (int splitPos = 1; splitPos < fileIdStr.Length; splitPos++)
            {
                // Hoppa över den vanliga splittningen (position 4)
                if (splitPos == 4) continue;

                string firstPart = fileIdStr.Substring(0, splitPos);
                string secondPart = fileIdStr.Substring(splitPos);

                // Se till att båda delarna är giltiga nummer
                if (int.TryParse(firstPart, out int first) && int.TryParse(secondPart, out int second))
                {
                    splits.Add((first, second));
                }
            }

            return splits;
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
                // NOTIS: Vi dispsar INTE HttpClient här längre eftersom den är delad
                // HttpClient ägs av HttpClientService

                // Optimera minne vid dispose
                try
                {
                    OptimizeMemory();
                }
                catch { /* Ignorera cleanup-fel */ }

                // MINNESRENSNING: Tvinga GC vid borttagning av tjänsten
                GC.Collect(0, GCCollectionMode.Optimized);
            }
            _disposed = true;
        }
    }
}