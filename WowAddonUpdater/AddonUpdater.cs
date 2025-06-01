using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WowAddonUpdater.Models;
using WowAddonUpdater.Services;
using WPFMessageBox = System.Windows.MessageBox;

namespace WowAddonUpdater
{
    public class AddonUpdater : IDisposable
    {
        private Config _config;
        private readonly string _configPath = "config.json";
        private readonly HttpClientService _httpClientService;
        private readonly ElvUIService _elvUIService;
        private readonly CurseForgeService _curseForgeService;
        private bool _disposed = false;

        public Config Config => _config;

        // Äldre egenskap för bakåtkompatibilitet
        public string WowPath => _config.GetActiveInstallation()?.AddonPath ?? "";

        public AddonUpdater()
        {
            _httpClientService = new HttpClientService();
            _elvUIService = new ElvUIService(_httpClientService.Client);
            _curseForgeService = new CurseForgeService(_httpClientService.Client);
            LoadConfig();
        }

        // Ta bort .zip från slutet av versionssträngar
        private static string CleanVersionString(string version)
        {
            if (string.IsNullOrEmpty(version))
                return version;

            if (version.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return version.Substring(0, version.Length - 4);
            }

            return version;
        }

        // Central sorteringsmetod
        public static List<Addon> SortAddons(List<Addon> addons, AddonSortMode sortMode)
        {
            if (addons == null || addons.Count == 0)
                return addons;

            switch (sortMode)
            {
                case AddonSortMode.Name:
                    return addons.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

                case AddonSortMode.Installation:
                    return addons
                        .OrderBy(a => a.InstallationName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                case AddonSortMode.LastUpdated:
                    return addons
                        .OrderByDescending(a => a.LastUpdated ?? DateTime.MinValue)
                        .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                default:
                    return addons.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<Config>(json);

                    // Migrera gammal konfiguration vid behov
                    MigrateLegacyConfig();
                }
                else
                {
                    _config = new Config();
                }
            }
            catch (Exception)
            {
                _config = new Config();
            }
        }

        private void MigrateLegacyConfig()
        {
            // Kontrollera om vi har äldre data som behöver migreras
            bool hasLegacyData = !string.IsNullOrEmpty(_config.Settings.AddonPath) ||
                                _config.Addons.Count > 0 ||
                                _config.FolderMapping.Count > 0;

            if (hasLegacyData && _config.Installations.Count == 0)
            {
                // Skapa standardinstallation från äldre inställningar
                var defaultInstallation = new Installation
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Default",
                    AddonPath = _config.Settings.AddonPath,
                    GameVersionId = _config.Settings.GameVersionId,
                    IncludeElvUI = _config.Settings.IncludeElvUI,
                    Addons = new Dictionary<string, AddonConfig>(_config.Addons),
                    FolderMapping = new Dictionary<string, List<string>>(_config.FolderMapping)
                };

                _config.Installations[defaultInstallation.Id] = defaultInstallation;
                _config.ActiveInstallationId = defaultInstallation.Id;

                // Behåll inställningar för autosökning i globala inställningar (ingen migrering behövs)
                // Rensa äldre data
                _config.Settings.AddonPath = "";
                _config.Settings.GameVersionId = 0;
                _config.Settings.IncludeElvUI = false;
                _config.Addons.Clear();
                _config.FolderMapping.Clear();

                SaveConfig();
            }
        }

        public void SaveConfig()
        {
            string json = JsonConvert.SerializeObject(_config, Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }

        // Hämta alla installationer
        public List<Installation> GetInstallations()
        {
            return _config.Installations.Values.ToList();
        }

        // lägg till installationer
        public Installation AddInstallation(string name, string path, int gameVersionId)
        {
            var installation = new Installation
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                AddonPath = path,
                GameVersionId = gameVersionId
            };

            _config.Installations[installation.Id] = installation;

            // Ange som aktiv om det är den första installationen
            if (_config.Installations.Count == 1)
            {
                _config.ActiveInstallationId = installation.Id;
            }

            SaveConfig();
            return installation;
        }

        // Ta bort installation
        public bool RemoveInstallation(string installationId)
        {
            if (_config.Installations.ContainsKey(installationId))
            {
                _config.Installations.Remove(installationId);

                // Om detta var den aktiva installationen, ange en ny
                if (_config.ActiveInstallationId == installationId)
                {
                    _config.ActiveInstallationId = _config.Installations.Keys.FirstOrDefault() ?? "";
                }

                SaveConfig();
                return true;
            }
            return false;
        }

        // Uppdatera installation
        public bool UpdateInstallation(Installation installation)
        {
            if (_config.Installations.ContainsKey(installation.Id))
            {
                _config.Installations[installation.Id] = installation;
                SaveConfig();
                return true;
            }
            return false;
        }

        // Ange aktiv installation
        public void SetActiveInstallation(string installationId)
        {
            _config.SetActiveInstallation(installationId);
            SaveConfig();
        }

        public async Task<List<Addon>> ScanForUpdates(IProgress<(int current, int total, string currentAddon)> progress = null)
        {
            List<Addon> allAddons = new List<Addon>();

            var installations = GetInstallations().Where(i =>
                !string.IsNullOrEmpty(i.AddonPath) &&
                Directory.Exists(i.AddonPath) &&
                i.GameVersionId > 0).ToList();

            if (installations.Count == 0)
            {
                return allAddons;
            }

            int totalAddons = 0;
            foreach (var installation in installations)
            {
                totalAddons += installation.Addons.Count;
                if (installation.IncludeElvUI)
                {
                    totalAddons++;
                }
            }

            int currentAddon = 0;
            string currentScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            try
            {
                foreach (var installation in installations)
                {
                    var installationAddons = await ScanInstallationForUpdates(installation, progress, currentAddon, totalAddons, currentScanTime);
                    allAddons.AddRange(installationAddons);
                    currentAddon += installation.Addons.Count + (installation.IncludeElvUI ? 1 : 0);
                }

                SaveConfig();

                // Tillämpa sortering baserat på användarens inställning
                allAddons = SortAddons(allAddons, _config.Settings.AddonSortMode);

                return allAddons;
            }
            finally
            {
                // MINNESRENSNING: Tvinga garbage collection efter skanning
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true);

                try
                {
                    var process = System.Diagnostics.Process.GetCurrentProcess();
                    process.Refresh();
                }
                catch { /* Ignorera errors */ }
            }
        }

        private async Task<List<Addon>> ScanInstallationForUpdates(Installation installation, IProgress<(int current, int total, string currentAddon)> progress, int baseCount, int totalCount, string scanTime)
        {
            List<Addon> addons = new List<Addon>();
            int currentAddon = baseCount;

            try
            {
                // Skanna efter Curse addons
                foreach (var addonEntry in installation.Addons)
                {
                    string addonName = addonEntry.Key;
                    int addonId = addonEntry.Value.Id;

                    // HOPPA ÖVER ElvUI i den vanliga addon-hanteringen... hanteras separat
                    if (addonName == "ElvUI") continue;

                    currentAddon++;
                    progress?.Report((currentAddon, totalCount, addonName));

                    JObject addonDetails = null;
                    JToken files = null;
                    JObject classicFile = null;

                    try
                    {
                        addonDetails = await _curseForgeService.GetAddonDetails(addonId);
                        files = addonDetails["data"];

                        foreach (var file in files)
                        {
                            var gameVersionTypeIds = file["gameVersionTypeIds"];
                            if (gameVersionTypeIds == null) continue;

                            bool isCompatible = false;

                            foreach (var id in gameVersionTypeIds)
                            {
                                if (id.Value<int>() == installation.GameVersionId)
                                {
                                    isCompatible = true;
                                    break;
                                }
                            }

                            if (isCompatible)
                            {
                                classicFile = (JObject)file;
                                break;
                            }
                        }

                        if (classicFile != null)
                        {
                            // Rensa bort .zip från online version
                            string rawOnlineVersion = classicFile["displayName"]?.ToString() ?? "Unknown Version";
                            string onlineVersion = CleanVersionString(rawOnlineVersion);
                            string onlineModified = classicFile["dateModified"]?.ToString() ?? "";

                            if (classicFile["id"]?.Value<int?>() is int fileIdValue)
                            {
                                int fileId = fileIdValue;

                                List<string> folderMapping = installation.FolderMapping.ContainsKey(addonName)
                                    ? installation.FolderMapping[addonName]
                                    : Utils.FindAddonFolders(addonName, installation.AddonPath);

                                // Använd local_version från config istället för TOC
                                string localVersion = addonEntry.Value.LocalVersion ?? "Not installed";

                                DateTime? lastUpdated = null;

                                if (!string.IsNullOrEmpty(addonEntry.Value.ModifiedDate))
                                {
                                    try
                                    {
                                        lastUpdated = DateTime.Parse(addonEntry.Value.ModifiedDate.Split('.')[0]);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error parsing date: {ex.Message}");
                                    }
                                }

                                bool needsUpdate = false;
                                if (lastUpdated.HasValue && !string.IsNullOrEmpty(onlineModified))
                                {
                                    try
                                    {
                                        DateTime onlineDate = DateTime.Parse(onlineModified.Split('.')[0]);
                                        needsUpdate = lastUpdated.Value < onlineDate;
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error comparing dates: {ex.Message}");
                                        needsUpdate = string.IsNullOrEmpty(localVersion) || localVersion != onlineVersion;
                                    }
                                }
                                else
                                {
                                    needsUpdate = string.IsNullOrEmpty(localVersion) || localVersion != onlineVersion;
                                }

                                // Uppdatera konfigurationen med skanningsresultat för att spara dem permanent
                                installation.Addons[addonName].OnlineModifiedDate = onlineModified;
                                installation.Addons[addonName].UpdateAvailable = needsUpdate;
                                installation.Addons[addonName].LastChecked = scanTime;
                                installation.Addons[addonName].OnlineVersion = onlineVersion;

                                addons.Add(new Addon
                                {
                                    Name = addonName,
                                    Id = addonId,
                                    LocalVersion = localVersion,
                                    OnlineVersion = onlineVersion,
                                    NeedsUpdate = needsUpdate,
                                    LastUpdated = lastUpdated,
                                    FileId = fileId,
                                    Folders = folderMapping,
                                    InstallationId = installation.Id,
                                    InstallationName = installation.Name
                                });
                            }
                        }
                        else
                        {
                            // Uppdatera konfigurationen även när ingen kompatibel version finns tillgänglig
                            installation.Addons[addonName].UpdateAvailable = false;
                            installation.Addons[addonName].LastChecked = scanTime;
                            installation.Addons[addonName].OnlineVersion = "No compatible version available";

                            addons.Add(new Addon
                            {
                                Name = addonName,
                                Id = addonId,
                                LocalVersion = addonEntry.Value.LocalVersion ?? "Not installed",
                                OnlineVersion = "No compatible version available",
                                NeedsUpdate = false,
                                LastUpdated = null,
                                InstallationId = installation.Id,
                                InstallationName = installation.Name
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        WPFMessageBox.Show($"Error checking version for {addonName} in {installation.Name}: {ex.Message}", "Error", MessageBoxButton.OK);
                    }
                    finally
                    {
                        // MINNESRENSNING
                        addonDetails = null;
                        files = null;
                        classicFile = null;

                        if (currentAddon % 2 == 0)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }

                        if (currentAddon % 10 == 0)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                    }

                    await Task.Delay(100);
                }

                // Lägg till ElvUI om det är aktiverat för denna installation
                if (installation.IncludeElvUI)
                {
                    currentAddon++;
                    progress?.Report((currentAddon, totalCount, "ElvUI"));

                    JObject elvuiInfo = null;

                    try
                    {
                        elvuiInfo = await _elvUIService.GetElvUIInfo();
                        string onlineVersion = elvuiInfo["version"]?.ToString() ?? "Unknown";
                        string lastUpdate = elvuiInfo["last_update"]?.ToString() ?? "";
                        DateTime? lastUpdated = null;

                        if (!string.IsNullOrEmpty(lastUpdate))
                        {
                            try
                            {
                                lastUpdated = DateTime.Parse(lastUpdate);
                            }
                            catch
                            {
                                // Om datumformatet inte kan tolkas, fortsätt utan datum
                            }
                        }

                        List<string> folderMapping = installation.FolderMapping.ContainsKey("ElvUI")
                            ? installation.FolderMapping["ElvUI"]
                            : Utils.FindAddonFolders("ElvUI", installation.AddonPath);

                        // ElvUI SKA fortfarande läsa från TOC... detta är korrekt
                        string localVersion = _curseForgeService.GetVersionFromToc("ElvUI", installation.AddonPath, folderMapping);
                        bool needsUpdate = localVersion != onlineVersion;

                        // Hantera ElvUI i konfigurationen om det inte finns som ett vanligt tillägg
                        if (!installation.Addons.ContainsKey("ElvUI"))
                        {
                            installation.Addons["ElvUI"] = new AddonConfig
                            {
                                Id = 0, // ElvUI har inte CurseForge ID
                                ModifiedDate = lastUpdate
                            };
                        }

                        // Updatera ElvUI config med BÅDA datum-fälten för kompatibilitet
                        installation.Addons["ElvUI"].ModifiedDate = lastUpdate;           // Huvuddatum för LastUpdated
                        installation.Addons["ElvUI"].OnlineModifiedDate = lastUpdate;    // Online-datum för jämförelser
                        installation.Addons["ElvUI"].UpdateAvailable = needsUpdate;
                        installation.Addons["ElvUI"].LastChecked = scanTime;
                        installation.Addons["ElvUI"].OnlineVersion = onlineVersion;
                        installation.Addons["ElvUI"].LocalVersion = localVersion ?? "Not installed";

                        addons.Add(new Addon
                        {
                            Name = "ElvUI",
                            LocalVersion = localVersion ?? "Not installed",
                            OnlineVersion = onlineVersion,
                            NeedsUpdate = needsUpdate,
                            LastUpdated = lastUpdated,
                            Folders = folderMapping,
                            InstallationId = installation.Id,
                            InstallationName = installation.Name
                        });
                    }
                    catch (Exception ex)
                    {
                        WPFMessageBox.Show($"Error checking ElvUI version for {installation.Name}: {ex.Message}", "Error", MessageBoxButton.OK);
                    }
                    finally
                    {
                        elvuiInfo = null;
                    }

                    await Task.Delay(100);
                }

                return addons;
            }
            finally
            {
                // Minnesrensning efter installation scan
                GC.Collect(1, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
            }
        }

        public async Task<bool> UpdateAddon(Addon addon)
        {
            var installation = _config.Installations.Values.FirstOrDefault(i => i.Id == addon.InstallationId);
            if (installation == null)
            {
                return false;
            }

            JObject addonDetails = null;
            string onlineModified = null;
            string onlineVersion = null; // Behöver online version för local_version

            try
            {
                if (addon.Name == "ElvUI")
                {
                    var (version, lastUpdate) = await _elvUIService.DownloadAndExtractElvUI(installation.AddonPath);

                    // Uppdatera mappkoppling
                    installation.FolderMapping["ElvUI"] = Utils.FindAddonFolders("ElvUI", installation.AddonPath);

                    // Uppdatera config med information om lyckad uppdatering
                    if (installation.Addons.ContainsKey("ElvUI"))
                    {
                        installation.Addons["ElvUI"].ModifiedDate = lastUpdate;
                        installation.Addons["ElvUI"].OnlineModifiedDate = lastUpdate;
                        installation.Addons["ElvUI"].UpdateAvailable = false;
                        installation.Addons["ElvUI"].LocalVersion = version;
                        installation.Addons["ElvUI"].OnlineVersion = version;
                    }

                    SaveConfig();
                    return true;
                }
                else if (addon.Id.HasValue && addon.FileId.HasValue)
                {
                    // Hämta addon-detaljer för att få modified_date och onlineversion
                    addonDetails = await _curseForgeService.GetAddonDetails(addon.Id.Value);

                    // Hitta rätt fil och hämta dateModified samt displayName
                    foreach (var file in addonDetails["data"])
                    {
                        if (file["id"].Value<int>() == addon.FileId.Value)
                        {
                            onlineModified = file["dateModified"]?.ToString();
                            // Rensa bort .zip från online version
                            string rawOnlineVersion = file["displayName"]?.ToString();
                            onlineVersion = CleanVersionString(rawOnlineVersion);
                            break;
                        }
                    }

                    var (_, success, folders) = await _curseForgeService.DownloadAndInstallAddon(
                        addon.Id.Value, addon.FileId.Value, installation.AddonPath);

                    if (success)
                    {
                        // Uppdatera mappkoppling
                        installation.FolderMapping[addon.Name] = folders;

                        // Uppdatera config med information om lyckad uppdatering
                        if (installation.Addons.ContainsKey(addon.Name) && onlineModified != null)
                        {
                            installation.Addons[addon.Name].ModifiedDate = onlineModified;
                            installation.Addons[addon.Name].OnlineModifiedDate = onlineModified;
                            installation.Addons[addon.Name].UpdateAvailable = false;

                            // Sätt local_version till den rensade online_version som faktiskt installerades
                            installation.Addons[addon.Name].LocalVersion = onlineVersion ?? addon.OnlineVersion;
                            installation.Addons[addon.Name].OnlineVersion = onlineVersion ?? addon.OnlineVersion;
                        }

                        SaveConfig();
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"Error updating {addon.Name} in {installation.Name}: {ex.Message}", "Error", MessageBoxButton.OK);
                return false;
            }
            finally
            {
                // MINNESRENSNING
                addonDetails = null;
                onlineModified = null;
                onlineVersion = null; // Rensa också online version

                GC.Collect(1, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(1, GCCollectionMode.Forced);
            }
        }

        public async Task<bool> InstallAddon(string addonName, int addonId, int fileId, string installationId)
        {
            var installation = _config.Installations.Values.FirstOrDefault(i => i.Id == installationId);
            if (installation == null)
            {
                return false;
            }

            JObject addonDetails = null;
            string onlineModified = null;
            string onlineVersion = null; // Behöver online version

            try
            {
                // Hämta addondetaljer för att få ändringsdatum och version på nätet
                addonDetails = await _curseForgeService.GetAddonDetails(addonId);

                // Hitta rätt fil och hämta dateModified och displayName
                foreach (var file in addonDetails["data"])
                {
                    if (file["id"].Value<int>() == fileId)
                    {
                        onlineModified = file["dateModified"]?.ToString();
                        // Rensa bort .zip från online version
                        string rawOnlineVersion = file["displayName"]?.ToString();
                        onlineVersion = CleanVersionString(rawOnlineVersion);
                        break;
                    }
                }

                var (_, success, folders) = await _curseForgeService.DownloadAndInstallAddon(
                    addonId, fileId, installation.AddonPath);

                if (success)
                {
                    // Lägg till i config
                    if (!installation.Addons.ContainsKey(addonName))
                    {
                        installation.Addons[addonName] = new AddonConfig
                        {
                            Id = addonId,
                            ModifiedDate = onlineModified
                        };
                    }
                    else
                    {
                        // Uppdatera existerande addon
                        installation.Addons[addonName].Id = addonId;
                        installation.Addons[addonName].ModifiedDate = onlineModified;
                    }

                    // Initiera beständig uppdateringsstatus för ny addon
                    installation.Addons[addonName].OnlineModifiedDate = onlineModified;
                    installation.Addons[addonName].UpdateAvailable = false;
                    installation.Addons[addonName].LastChecked = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    // Sätt local_version till den rensade online_version som faktiskt installerades
                    installation.Addons[addonName].LocalVersion = onlineVersion ?? "Installed";
                    installation.Addons[addonName].OnlineVersion = onlineVersion ?? "Latest";

                    // Uppdatera mappkoppling
                    installation.FolderMapping[addonName] = folders;
                    SaveConfig();

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"Error installing {addonName} to {installation.Name}: {ex.Message}", "Error", MessageBoxButton.OK);
                return false;
            }
            finally
            {
                // MEMORY CLEANUP
                addonDetails = null;
                onlineModified = null;
                onlineVersion = null; // Rensa också online version

                GC.Collect(1, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(1, GCCollectionMode.Forced);
            }
        }

        // Äldre egenskap för bakåtkompatibilitet
        public async Task<bool> InstallAddon(string addonName, int addonId, int fileId)
        {
            var activeInstallation = _config.GetActiveInstallation();
            if (activeInstallation == null)
            {
                return false;
            }

            return await InstallAddon(addonName, addonId, fileId, activeInstallation.Id);
        }

        // Läs in addons från config för samtliga installationer
        public List<Addon> LoadAddonsFromConfig()
        {
            List<Addon> addons = new List<Addon>();

            try
            {
                foreach (var installation in _config.Installations.Values)
                {
                    var installationAddons = LoadAddonsFromInstallation(installation);
                    addons.AddRange(installationAddons);
                }

                // Tillämpa sortering baserat på användarens preferens
                addons = SortAddons(addons, _config.Settings.AddonSortMode);

                return addons;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading addons from config: {ex.Message}");
                return new List<Addon>();
            }
            finally
            {
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        private List<Addon> LoadAddonsFromInstallation(Installation installation)
        {
            List<Addon> addons = new List<Addon>();

            foreach (var addonEntry in installation.Addons)
            {
                string addonName = addonEntry.Key;
                var addonConfig = addonEntry.Value;

                // Hantera ElvUI unikt
                if (addonName == "ElvUI")
                {
                    if (!installation.IncludeElvUI)
                        continue;

                    DateTime? elvuiLastUpdated = null;

                    // Förbättrad datum-parsing för ElvUI
                    if (!string.IsNullOrEmpty(addonConfig.ModifiedDate))
                    {
                        try
                        {
                            // Prova olika datum-format
                            string dateStr = addonConfig.ModifiedDate;

                            // Ta bort millisekunder om de finns
                            if (dateStr.Contains('.'))
                            {
                                dateStr = dateStr.Split('.')[0];
                            }

                            // Prova ISO format först
                            if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                            {
                                elvuiLastUpdated = parsedDate;
                            }
                            else
                            {
                                Console.WriteLine($"Could not parse ElvUI date: {addonConfig.ModifiedDate}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing date for ElvUI: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Om ModifiedDate är tom, försök använda OnlineModifiedDate
                        if (!string.IsNullOrEmpty(addonConfig.OnlineModifiedDate))
                        {
                            try
                            {
                                string dateStr = addonConfig.OnlineModifiedDate;
                                if (dateStr.Contains('.'))
                                {
                                    dateStr = dateStr.Split('.')[0];
                                }

                                if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                                {
                                    elvuiLastUpdated = parsedDate;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error parsing OnlineModifiedDate for ElvUI: {ex.Message}");
                            }
                        }
                    }

                    List<string> elvuiFolderMapping = installation.FolderMapping.ContainsKey("ElvUI")
                        ? installation.FolderMapping["ElvUI"]
                        : Utils.FindAddonFolders("ElvUI", installation.AddonPath);

                    var elvuiAddon = new Addon
                    {
                        Name = "ElvUI",
                        Id = null,
                        LocalVersion = addonConfig.LocalVersion ?? "Unknown",
                        OnlineVersion = addonConfig.OnlineVersion ?? "Unknown",
                        NeedsUpdate = addonConfig.UpdateAvailable,
                        LastUpdated = elvuiLastUpdated,
                        Folders = elvuiFolderMapping,
                        InstallationId = installation.Id,
                        InstallationName = installation.Name
                    };

                    addons.Add(elvuiAddon);
                    continue;
                }

                DateTime? lastUpdated = null;
                if (!string.IsNullOrEmpty(addonConfig.ModifiedDate))
                {
                    try
                    {
                        string dateStr = addonConfig.ModifiedDate;
                        if (dateStr.Contains('.'))
                        {
                            dateStr = dateStr.Split('.')[0];
                        }

                        if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                        {
                            lastUpdated = parsedDate;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing date for {addonName}: {ex.Message}");
                    }
                }

                List<string> folderMapping = installation.FolderMapping.ContainsKey(addonName)
                    ? installation.FolderMapping[addonName]
                    : Utils.FindAddonFolders(addonName, installation.AddonPath);

                var addon = new Addon
                {
                    Name = addonName,
                    Id = addonConfig.Id > 0 ? addonConfig.Id : (int?)null,
                    LocalVersion = addonConfig.LocalVersion ?? "Unknown",
                    OnlineVersion = addonConfig.OnlineVersion ?? "Unknown",
                    NeedsUpdate = addonConfig.UpdateAvailable,
                    LastUpdated = lastUpdated,
                    Folders = folderMapping,
                    InstallationId = installation.Id,
                    InstallationName = installation.Name
                };

                addons.Add(addon);
            }

            return addons;
        }

        public bool DeleteAddon(Addon addon)
        {
            var installation = _config.Installations.Values.FirstOrDefault(i => i.Id == addon.InstallationId);
            if (installation == null)
            {
                return false;
            }

            try
            {
                // Radera mappar
                if (addon.Folders != null)
                {
                    foreach (string folder in addon.Folders)
                    {
                        string folderPath = Path.Combine(installation.AddonPath, folder);
                        if (Directory.Exists(folderPath))
                        {
                            Directory.Delete(folderPath, true);
                        }
                    }
                }

                // Radera från config
                if (installation.Addons.ContainsKey(addon.Name))
                {
                    installation.Addons.Remove(addon.Name);
                }

                if (installation.FolderMapping.ContainsKey(addon.Name))
                {
                    installation.FolderMapping.Remove(addon.Name);
                }

                SaveConfig();
                return true;
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"Error deleting {addon.Name} from {installation.Name}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        // Hjälpklass för att få åtkomst till CurseForgeService vid specifika åtgärder
        public CurseForgeService GetCurseForgeService()
        {
            return _curseForgeService;
        }

        // Metod för minnesoptimering
        public void OptimizeMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true);

                try
                {
                    var process = System.Diagnostics.Process.GetCurrentProcess();
                    process.Refresh();
                }
                catch { /* Ignorera errors */ }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error optimizing memory: {ex.Message}");
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
                _elvUIService?.Dispose();
                _curseForgeService?.Dispose();
                _httpClientService?.Dispose();

                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true);

                try
                {
                    var process = System.Diagnostics.Process.GetCurrentProcess();
                    process.Refresh();
                }
                catch { /* Ignorera errors */ }
            }
            _disposed = true;
        }
    }
}