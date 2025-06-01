using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using WowAddonUpdater.Models;

namespace WowAddonUpdater.Services
{
    public class ElvUIManagementService
    {
        private readonly AddonUpdater _addonUpdater;
        private readonly ElvUIService _elvUIService;

        public ElvUIManagementService(AddonUpdater addonUpdater, ElvUIService elvUIService)
        {
            _addonUpdater = addonUpdater ?? throw new ArgumentNullException(nameof(addonUpdater));
            _elvUIService = elvUIService ?? throw new ArgumentNullException(nameof(elvUIService));
        }

        public async Task<bool> InstallElvUIAsync(Installation installation)
        {
            try
            {
                // Minnesoptimering före installation av ElvUI
                GC.Collect(1, GCCollectionMode.Optimized);

                var (version, lastUpdate) = await _elvUIService.DownloadAndExtractElvUI(installation.AddonPath);

                // Uppdatera mappkoppling för denna installation
                installation.FolderMapping["ElvUI"] = Utils.FindAddonFolders("ElvUI", installation.AddonPath);

                // Uppdatera ElvUI-inställning för denna installation
                installation.IncludeElvUI = true;

                // Initiera eller uppdatera ElvUI i denna installations addon-konfiguration
                if (!installation.Addons.ContainsKey("ElvUI"))
                {
                    installation.Addons["ElvUI"] = new AddonConfig
                    {
                        Id = 0, // ElvUI har inget CurseForge-ID
                        ModifiedDate = lastUpdate,
                        LocalVersion = version
                    };
                }
                else
                {
                    installation.Addons["ElvUI"].ModifiedDate = lastUpdate;
                    installation.Addons["ElvUI"].LocalVersion = version;
                }

                _addonUpdater.UpdateInstallation(installation);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error installing ElvUI to {installation.Name}", ex);
                throw;
            }
            finally
            {
                // Minnesrensning efter installation av ElvUI
                GC.Collect(1, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
            }
        }

        public async Task<bool> UninstallElvUIAsync(Installation installation)
        {
            try
            {
                var elvuiFolders = installation.FolderMapping.ContainsKey("ElvUI")
                    ? installation.FolderMapping["ElvUI"]
                    : new List<string> { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" };

                foreach (var folder in elvuiFolders)
                {
                    string folderPath = Path.Combine(installation.AddonPath, folder);
                    if (Directory.Exists(folderPath))
                    {
                        Directory.Delete(folderPath, true);
                    }
                }

                // Ta bort från mappkoppling för denna installation
                if (installation.FolderMapping.ContainsKey("ElvUI"))
                {
                    installation.FolderMapping.Remove("ElvUI");
                }

                // Ta bort från denna installations addon-konfiguration
                if (installation.Addons.ContainsKey("ElvUI"))
                {
                    installation.Addons.Remove("ElvUI");
                }

                // Uppdatera ElvUI-inställning för denna installation
                installation.IncludeElvUI = false;

                _addonUpdater.UpdateInstallation(installation);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error uninstalling ElvUI from {installation.Name}", ex);
                throw;
            }
            finally
            {
                // Minnesrensning efter avinstallation av ElvUI
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        // Äldre metoder för bakåtkompatibilitet
        public async Task<bool> InstallElvUIAsync()
        {
            var activeInstallation = _addonUpdater.Config.GetActiveInstallation();
            if (activeInstallation == null)
            {
                throw new InvalidOperationException("No active installation found");
            }

            return await InstallElvUIAsync(activeInstallation);
        }

        public async Task<bool> UninstallElvUIAsync()
        {
            var activeInstallation = _addonUpdater.Config.GetActiveInstallation();
            if (activeInstallation == null)
            {
                throw new InvalidOperationException("No active installation found");
            }

            return await UninstallElvUIAsync(activeInstallation);
        }

        public void InitializeElvUIInConfig(Installation installation)
        {
            // Initiera ElvUI i denna installations konfiguration med persistenta fält om det inte redan finns
            if (!installation.Addons.ContainsKey("ElvUI"))
            {
                // Försök hämta befintlig ElvUI-version från TOC-filen
                List<string> elvuiFolders = new List<string> { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" };
                string existingVersion = _addonUpdater.GetCurseForgeService().GetVersionFromToc("ElvUI", installation.AddonPath, elvuiFolders);

                installation.Addons["ElvUI"] = new AddonConfig
                {
                    Id = 0, // ElvUI har inget CurseForge-ID
                    ModifiedDate = "",
                    OnlineModifiedDate = "",
                    UpdateAvailable = false,
                    LastChecked = "",
                    OnlineVersion = "",
                    LocalVersion = existingVersion ?? "Unknown" // Använd faktisk version eller reservvärde
                };
            }
            else
            {
                // Uppdatera befintlig ElvUI-konfiguration om LocalVersion är "Detected"
                var existingElvUI = installation.Addons["ElvUI"];
                if (existingElvUI.LocalVersion == "Detected" || string.IsNullOrEmpty(existingElvUI.LocalVersion))
                {
                    List<string> elvuiFolders = new List<string> { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" };
                    string actualVersion = _addonUpdater.GetCurseForgeService().GetVersionFromToc("ElvUI", installation.AddonPath, elvuiFolders);
                    existingElvUI.LocalVersion = actualVersion ?? "Unknown";
                }
            }

            // Uppdatera mappkoppling för ElvUI i denna installation
            installation.FolderMapping["ElvUI"] = new List<string> { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" };
        }

        // Äldre metod för bakåtkompatibilitet
        public void InitializeElvUIInConfig()
        {
            var activeInstallation = _addonUpdater.Config.GetActiveInstallation();
            if (activeInstallation != null)
            {
                InitializeElvUIInConfig(activeInstallation);
            }
        }

        private void LogError(string context, Exception ex)
        {
            try
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WowAddonUpdater");

                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(logFolder, "error.log");

                File.AppendAllText(logFile,
                    $"{DateTime.Now}: ElvUIManagementService: {context}: {ex?.Message}\r\n{ex?.StackTrace}\r\n\r\n");
            }
            catch
            {
                // Om loggning misslyckas är det illa
            }
        }
    }
}