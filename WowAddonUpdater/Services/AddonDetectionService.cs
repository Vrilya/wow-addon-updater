using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WowAddonUpdater.Models;

namespace WowAddonUpdater.Services
{
    public class DetectedAddon
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public List<string> Folders { get; set; } = new List<string>();
        public DateTime UploadDate { get; set; }
    }

    public class AddonDetectionService
    {
        private readonly AddonUpdater _addonUpdater;
        private readonly MainWindow _mainWindow;

        public AddonDetectionService(AddonUpdater addonUpdater, MainWindow mainWindow = null)
        {
            _addonUpdater = addonUpdater ?? throw new ArgumentNullException(nameof(addonUpdater));
            _mainWindow = mainWindow;
        }

        // Tar bort .zip från slutet av versionssträngar
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

        public async Task<Dictionary<int, DetectedAddon>> DetectAddonsAsync(string wowPath, int gameVersionId, bool includeElvUI = false)
        {
            var detectedAddons = new Dictionary<int, DetectedAddon>();

            try
            {
                // Minnesoptimering före auto-detection
                GC.Collect(1, GCCollectionMode.Optimized);

                // Skanna addon-mappen efter undermappar
                List<string> installedFolders = GetInstalledAddonFolders(wowPath);
                if (installedFolders.Count == 0)
                {
                    return detectedAddons; // Avsluta tyst om inga mappar hittas
                }

                // Kontrollera om ElvUI är installerat och hantera det
                if (includeElvUI)
                {
                    bool isElvUIDetected = DetectElvUI(installedFolders);
                    if (isElvUIDetected)
                    {
                        // ElvUI upptäckt – anropande kod ska hantera detta
                    }
                }

                // Ladda in addon database
                AddonDatabase database = LoadAddonDatabase();
                if (database == null)
                {
                    return detectedAddons; // Avsluta tyst om databas inte kunde laddas
                }

                // Använd angiven game version
                string gameVersionKey = gameVersionId.ToString();

                // Filtrera bort ElvUI-mappar från sökningen eftersom de hanteras separat
                List<string> foldersToSearch = installedFolders.Where(f =>
                    f != "ElvUI" && f != "ElvUI_Libraries" && f != "ElvUI_Options").ToList();

                // Hitta matchande addons (ej ElvUI)
                detectedAddons = DetectAddonsFromFolders(database, foldersToSearch, gameVersionKey);

                return detectedAddons;
            }
            finally
            {
                // Minnesrensning efter autodetektering
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(1, GCCollectionMode.Forced);
            }
        }

        // Äldre metod för bakåtkompatibilitet
        public async Task<Dictionary<int, DetectedAddon>> DetectAddonsAsync(string wowPath, bool includeElvUI = false)
        {
            var activeInstallation = _addonUpdater.Config.GetActiveInstallation();
            if (activeInstallation == null)
            {
                return new Dictionary<int, DetectedAddon>();
            }

            return await DetectAddonsAsync(wowPath, activeInstallation.GameVersionId, includeElvUI);
        }

        public async Task<int> AddDetectedAddonsToConfigAsync(Dictionary<int, DetectedAddon> detectedAddons, Installation installation)
        {
            int addedCount = 0;

            foreach (var detectedAddon in detectedAddons.Values)
            {
                // Kontrollera om addon redan finns i denna installations konfiguration
                if (!installation.Addons.ContainsKey(detectedAddon.Name))
                {
                    // Använd standarddatum för autodetektering (endast som initialt värde)
                    string defaultDate = "2010-01-01 00:00:00";

                    // Sätt local_version till "Detected" - TOC ska inte läsas för vanliga addons
                    string initialLocalVersion = "Detected";

                    // Lägg till addon i denna installations konfiguration
                    installation.Addons[detectedAddon.Name] = new AddonConfig
                    {
                        Id = detectedAddon.Id,
                        ModifiedDate = defaultDate,
                        // Initiera fält för beständig uppdateringsstatus
                        OnlineModifiedDate = "",
                        UpdateAvailable = false,
                        LastChecked = "",
                        OnlineVersion = "",
                        LocalVersion = initialLocalVersion // Använd "Detected" istället för TOC
                    };

                    // Lägg till mappkoppling för denna installation
                    installation.FolderMapping[detectedAddon.Name] = detectedAddon.Folders;

                    addedCount++;
                }
                else
                {
                    // Addon finns redan, uppdatera mappkoppling vid behov
                    if (!installation.FolderMapping.ContainsKey(detectedAddon.Name))
                    {
                        installation.FolderMapping[detectedAddon.Name] = detectedAddon.Folders;
                    }

                    // Initiera saknade beständiga fält för befintliga addons
                    var existingAddon = installation.Addons[detectedAddon.Name];
                    if (string.IsNullOrEmpty(existingAddon.OnlineModifiedDate))
                    {
                        existingAddon.OnlineModifiedDate = "";
                        existingAddon.UpdateAvailable = false;
                        existingAddon.LastChecked = "";
                        existingAddon.OnlineVersion = "";

                        // Sätt "Detected" om LocalVersion är tom eller "Detected" redan
                        if (string.IsNullOrEmpty(existingAddon.LocalVersion) || existingAddon.LocalVersion == "Detected")
                        {
                            existingAddon.LocalVersion = "Detected";
                        }
                        // Annars behåll den befintliga local_version (den som faktiskt installerades tidigare)
                    }
                }
            }

            return addedCount;
        }

        // Äldre metod för bakåtkompatibilitet
        public async Task<int> AddDetectedAddonsToConfigAsync(Dictionary<int, DetectedAddon> detectedAddons)
        {
            var activeInstallation = _addonUpdater.Config.GetActiveInstallation();
            if (activeInstallation == null)
            {
                return 0;
            }

            return await AddDetectedAddonsToConfigAsync(detectedAddons, activeInstallation);
        }

        public async Task DownloadAndInstallAllDetectedAddonsAsync(Dictionary<int, DetectedAddon> detectedAddons, Installation installation, bool showProgress = false)
        {
            if (detectedAddons.Count == 0)
                return;

            try
            {
                // Minnesoptimering före massinstallation
                GC.Collect(1, GCCollectionMode.Optimized);

                int currentAddon = 0;
                int totalAddons = detectedAddons.Count;
                int successfulInstalls = 0;
                List<string> failedAddons = new List<string>();

                // Visa förlopp i MainWindow
                if (showProgress)
                {
                    _mainWindow?.ShowSynchronizationProgress(0, totalAddons, "");
                }

                foreach (var detectedAddon in detectedAddons.Values)
                {
                    currentAddon++;

                    try
                    {
                        // Uppdatera förloppet i MainWindow
                        if (showProgress)
                        {
                            _mainWindow?.ShowSynchronizationProgress(currentAddon, totalAddons, detectedAddon.Name);
                        }

                        // Minnesoptimering under installation
                        OptimizeMemoryDuringBulkOperations(currentAddon, totalAddons);

                        if (showProgress)
                        {
                            Console.WriteLine($"Installing addon {currentAddon}/{totalAddons}: {detectedAddon.Name} to {installation.Name}");
                        }

                        // Använd delad CurseForgeService i stället för att skapa en ny
                        var curseForgeService = _addonUpdater.GetCurseForgeService();

                        JObject addonDetailsResult = null;
                        JToken files = null;
                        JObject latestCompatibleFile = null;

                        try
                        {
                            addonDetailsResult = await curseForgeService.GetAddonDetails(detectedAddon.Id);
                            files = addonDetailsResult["data"];

                            // Hitta den senaste kompatibla filen för denna installation
                            int currentGameVersionId = installation.GameVersionId;

                            foreach (var file in files)
                            {
                                var gameVersionTypeIds = file["gameVersionTypeIds"];
                                if (gameVersionTypeIds != null)
                                {
                                    bool isCompatible = false;

                                    foreach (var id in gameVersionTypeIds)
                                    {
                                        if (id.Value<int>() == currentGameVersionId)
                                        {
                                            isCompatible = true;
                                            break;
                                        }
                                    }

                                    if (isCompatible)
                                    {
                                        latestCompatibleFile = (JObject)file;
                                        break; // Ta första (senaste) kompatibla filen
                                    }
                                }
                            }

                            if (latestCompatibleFile != null)
                            {
                                if (latestCompatibleFile["id"]?.Value<int?>() is int fileId)
                                {
                                    string onlineModified = latestCompatibleFile["dateModified"]?.ToString() ?? "";
                                    // Rensa bort .zip från online version
                                    string rawOnlineVersion = latestCompatibleFile["displayName"]?.ToString() ?? "Unknown";
                                    string onlineVersion = CleanVersionString(rawOnlineVersion);

                                    // Ladda ner och installera addon i denna installation
                                    var (_, success, folders) = await curseForgeService.DownloadAndInstallAddon(
                                        detectedAddon.Id, fileId, installation.AddonPath);

                                    if (success)
                                    {
                                        // Uppdatera denna installations konfiguration med korrekt serverdatum efter lyckad installation
                                        if (installation.Addons.ContainsKey(detectedAddon.Name))
                                        {
                                            installation.Addons[detectedAddon.Name].ModifiedDate = onlineModified;

                                            // Sätt local_version till den rensade online_version som faktiskt installerades
                                            installation.Addons[detectedAddon.Name].LocalVersion = onlineVersion;
                                            installation.Addons[detectedAddon.Name].OnlineVersion = onlineVersion;
                                        }
                                        else
                                        {
                                            // Sätt local_version till rensad online version för nya addons också
                                            installation.Addons[detectedAddon.Name] = new AddonConfig
                                            {
                                                Id = detectedAddon.Id,
                                                ModifiedDate = onlineModified,
                                                LocalVersion = onlineVersion,
                                                OnlineVersion = onlineVersion
                                            };
                                        }

                                        // Uppdatera mappkoppling med faktiska mappar från nedladdningen
                                        installation.FolderMapping[detectedAddon.Name] = folders;

                                        successfulInstalls++;

                                        if (showProgress)
                                        {
                                            Console.WriteLine($"Successfully installed: {detectedAddon.Name} to {installation.Name}");
                                        }
                                    }
                                    else
                                    {
                                        failedAddons.Add(detectedAddon.Name);
                                        if (showProgress)
                                        {
                                            Console.WriteLine($"Failed to install: {detectedAddon.Name} to {installation.Name}");
                                        }
                                    }
                                }
                                else
                                {
                                    failedAddons.Add($"{detectedAddon.Name} (invalid file ID)");
                                }
                            }
                            else
                            {
                                failedAddons.Add($"{detectedAddon.Name} (no compatible version)");
                                if (showProgress)
                                {
                                    Console.WriteLine($"No compatible version found for: {detectedAddon.Name} in {installation.Name}");
                                }
                            }
                        }
                        finally
                        {
                            // Explicit minnesrensning efter varje addon
                            addonDetailsResult = null;
                            files = null;
                            latestCompatibleFile = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedAddons.Add($"{detectedAddon.Name} ({ex.Message})");
                        if (showProgress)
                        {
                            Console.WriteLine($"Error installing {detectedAddon.Name} to {installation.Name}: {ex.Message}");
                        }
                    }

                    // Kort paus mellan nedladdningar för att inte överbelasta servern
                    await Task.Delay(500);
                }

                // Dölj förloppet i MainWindow
                if (showProgress)
                {
                    _mainWindow?.HideSynchronizationProgress();
                }

                // Spara konfigurationen med alla uppdateringar
                _addonUpdater.SaveConfig();

                if (showProgress && failedAddons.Count > 0)
                {
                    string failedList = string.Join("\n", failedAddons);
                    System.Windows.MessageBox.Show($"Installation completed with some issues for {installation.Name}:\n\n" +
                                  $"Successful: {successfulInstalls}/{totalAddons}\n" +
                                  $"Failed addons:\n{failedList}",
                                  "Installation Warning", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }
                else if (showProgress)
                {
                    Console.WriteLine($"All {successfulInstalls} addons installed successfully to {installation.Name}!");
                }
            }
            catch (Exception ex)
            {
                // Dölj förloppet i MainWindow vid fel
                if (showProgress)
                {
                    _mainWindow?.HideSynchronizationProgress();
                }

                if (showProgress)
                {
                    System.Windows.MessageBox.Show($"Error during bulk addon installation to {installation.Name}: {ex.Message}", "Installation Error",
                                  System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                else
                {
                    Console.WriteLine($"Error during bulk addon installation to {installation.Name}: {ex.Message}");
                }
            }
            finally
            {
                // Mer aggressiv minnesrensning efter massinstallation
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(1, GCCollectionMode.Forced);
            }
        }

        // Äldre metod för bakåtkompatibilitet
        public async Task DownloadAndInstallAllDetectedAddonsAsync(Dictionary<int, DetectedAddon> detectedAddons, bool showProgress = false)
        {
            var activeInstallation = _addonUpdater.Config.GetActiveInstallation();
            if (activeInstallation == null)
            {
                return;
            }

            await DownloadAndInstallAllDetectedAddonsAsync(detectedAddons, activeInstallation, showProgress);
        }

        private List<string> GetInstalledAddonFolders(string wowPath)
        {
            List<string> folders = new List<string>();

            try
            {
                if (Directory.Exists(wowPath))
                {
                    DirectoryInfo[] directories = new DirectoryInfo(wowPath).GetDirectories();
                    foreach (var dir in directories)
                    {
                        folders.Add(dir.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning addon folders: {ex.Message}");
            }

            return folders;
        }

        private bool DetectElvUI(List<string> installedFolders)
        {
            // ElvUI-mappar vi letar efter
            var elvuiFolders = new List<string> { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" };

            // Kontrollera om åtminstone ElvUI-mappen finns (den viktigaste)
            bool hasElvUICore = installedFolders.Contains("ElvUI");

            // Om ElvUI-kärnan finns, räkna den som detekterad
            return hasElvUICore;
        }

        private AddonDatabase LoadAddonDatabase()
        {
            try
            {
                string databasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "addon_database.json");

                if (!File.Exists(databasePath))
                {
                    Console.WriteLine($"Addon database not found at: {databasePath}");
                    return null;
                }

                string json = File.ReadAllText(databasePath);
                return JsonConvert.DeserializeObject<AddonDatabase>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading addon database: {ex.Message}");
                return null;
            }
        }

        private Dictionary<int, DetectedAddon> DetectAddonsFromFolders(AddonDatabase database, List<string> installedFolders, string gameVersionKey)
        {
            Dictionary<int, DetectedAddon> detectedAddons = new Dictionary<int, DetectedAddon>();

            foreach (string installedFolder in installedFolders)
            {
                AddonDatabaseEntry bestMatch = null;
                DateTime latestUploadDate = DateTime.MinValue;

                // Sök igenom alla addons i databasen
                foreach (var addonEntry in database.Addons)
                {
                    // Kontrollera om detta addon har data för aktuell spelversion
                    if (!addonEntry.Versions.ContainsKey(gameVersionKey))
                        continue;

                    var versionInfo = addonEntry.Versions[gameVersionKey];

                    // Kontrollera om detta addon innehåller den installerade mappen
                    if (versionInfo.Folders.Contains(installedFolder))
                    {
                        // Försök tolka upload_date
                        if (DateTime.TryParse(versionInfo.UploadDate, out DateTime uploadDate))
                        {
                            // Om detta är den senaste versionen som innehåller denna mapp
                            if (uploadDate > latestUploadDate)
                            {
                                latestUploadDate = uploadDate;
                                bestMatch = addonEntry;
                            }
                        }
                    }
                }

                // Om vi hittar en träff, lägg till den i resultat
                if (bestMatch != null)
                {
                    if (!detectedAddons.ContainsKey(bestMatch.Id))
                    {
                        detectedAddons[bestMatch.Id] = new DetectedAddon
                        {
                            Id = bestMatch.Id,
                            Name = bestMatch.Name,
                            Folders = new List<string>(),
                            UploadDate = latestUploadDate
                        };
                    }

                    // Lägg till mapp i addons mapplista
                    if (!detectedAddons[bestMatch.Id].Folders.Contains(installedFolder))
                    {
                        detectedAddons[bestMatch.Id].Folders.Add(installedFolder);
                    }
                }
            }

            return detectedAddons;
        }

        private void OptimizeMemoryDuringBulkOperations(int currentItem, int totalItems)
        {
            try
            {
                // Mild GC var femte addon
                if (currentItem % 5 == 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }

                // Mer aggressiv GC var 15e addon
                if (currentItem % 15 == 0)
                {
                    GC.Collect(1, GCCollectionMode.Forced);
                    GC.WaitForPendingFinalizers();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during bulk operation memory optimization: {ex.Message}");
            }
        }
    }
}