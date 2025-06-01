using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using WowAddonUpdater.Models;

namespace WowAddonUpdater.Services
{
    public class AutoScanService : IAutoScanService
    {
        private readonly AddonUpdater _addonUpdater;
        private DispatcherTimer _autoScanTimer;
        private bool _disposed = false;
        private bool _isScanning = false;

        public event EventHandler<AutoScanCompletedEventArgs> AutoScanCompleted;

        public bool IsRunning => _autoScanTimer?.IsEnabled ?? false;

        public AutoScanService(AddonUpdater addonUpdater)
        {
            _addonUpdater = addonUpdater ?? throw new ArgumentNullException(nameof(addonUpdater));
            InitializeTimer();
        }

        private void InitializeTimer()
        {
            try
            {
                LogMessage("Initializing auto scan timer");

                _autoScanTimer = new DispatcherTimer();
                _autoScanTimer.Tick += AutoScanTimer_Tick;

                LogMessage("Auto scan timer initialized successfully");
            }
            catch (Exception ex)
            {
                LogError("Failed to initialize auto scan timer", ex);
            }
        }

        public void Start()
        {
            try
            {
                // Stoppa timern först
                Stop();

                // Kontrollera om någon installation ska ha autosökning aktiverad
                bool shouldEnable = ShouldEnableAutoScan();

                if (shouldEnable)
                {
                    // Hitta det kortaste intervallet bland alla installationer med autosökning aktiverad
                    int shortestInterval = GetShortestAutoScanInterval();

                    // FÖR TESTNING: Tillåt mycket korta intervall (ta bort 60-minutersgränsen)
                    // Säkerställ att minsta tillåtna intervall är 1 minut av säkerhetsskäl
                    if (shortestInterval < 1)
                    {
                        shortestInterval = 1;
                        LogMessage($"Adjusted minimum interval to 1 minute for safety");
                    }

                    _autoScanTimer.Interval = TimeSpan.FromMinutes(shortestInterval);
                    _autoScanTimer.Start();

                    LogMessage($"Auto scan started with interval: {shortestInterval} minutes");
                    LogMessage($"Next auto scan will occur at: {DateTime.Now.AddMinutes(shortestInterval):yyyy-MM-dd HH:mm:ss}");
                }
                else
                {
                    LogMessage("Auto scan disabled - no installations have auto scan enabled");
                }
            }
            catch (Exception ex)
            {
                LogError("Error starting auto scan service", ex);
            }
        }

        public void Stop()
        {
            try
            {
                _autoScanTimer?.Stop();
                LogMessage("Auto scan timer stopped");
            }
            catch (Exception ex)
            {
                LogError("Error stopping auto scan timer", ex);
            }
        }

        public void RefreshConfiguration()
        {
            try
            {
                LogMessage("Refreshing auto scan configuration");
                Start(); // Start() hanterar stopp och omstart med ny konfiguration
            }
            catch (Exception ex)
            {
                LogError("Error refreshing auto scan configuration", ex);
            }
        }

        private bool ShouldEnableAutoScan()
        {
            // Kontrollera global inställning för autosökning
            bool globalAutoScanEnabled = _addonUpdater?.Config?.Settings?.AutoScanEnabled ?? false;

            if (!globalAutoScanEnabled)
            {
                return false;
            }

            // Kontrollera om det finns några giltiga installationer
            var installations = _addonUpdater?.GetInstallations() ?? new List<Installation>();
            return installations.Any(i =>
                !string.IsNullOrEmpty(i.AddonPath) &&
                Directory.Exists(i.AddonPath) &&
                i.GameVersionId > 0);
        }

        private int GetShortestAutoScanInterval()
        {
            // Använd globalt intervall för autosökning
            return _addonUpdater?.Config?.Settings?.AutoScanIntervalMinutes ?? 360;
        }

        private async void AutoScanTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                LogMessage("Auto scan timer triggered");

                // Utför inte autosökning om en sökning redan pågår
                if (_isScanning)
                {
                    LogMessage("Skipping auto scan - already scanning");
                    return;
                }

                // Dubbelkolla att vi fortfarande kör
                if (!ShouldEnableAutoScan())
                {
                    LogMessage("Stopping auto scan - configuration changed");
                    Stop();
                    return;
                }

                _isScanning = true;

                // Optimera minne före scan
                try
                {
                    _addonUpdater.OptimizeMemory();
                    LogMessage("Memory optimized before auto scan");
                }
                catch (Exception ex)
                {
                    LogError("Error optimizing memory before auto scan", ex);
                }

                LogMessage("Starting automatic scan for updates across all installations");

                // Kontrollera vilka installationer som är redo för skanning
                var installationsDueForScan = GetInstallationsDueForScan();

                if (installationsDueForScan.Count == 0)
                {
                    LogMessage("No installations are due for scanning at this time");
                    _isScanning = false;
                    return;
                }

                LogMessage($"Found {installationsDueForScan.Count} installations due for scanning");

                // Utför scan
                var progress = new Progress<(int current, int total, string currentAddon)>(report =>
                {
                    // Vi skulle kunna skicka ut förloppshändelser här vid behov, men för autosökning håller vi det enkelt
                });

                List<Addon> addons = null;
                try
                {
                    addons = await _addonUpdater.ScanForUpdates(progress);
                }
                catch (Exception ex)
                {
                    LogError("Error during auto scan", ex);
                    throw; // throw för att hanteras av yttre catch-sats
                }

                // Kontrollera om vi ska uppdatera automatiskt efter skanning (global inställning)
                int totalUpdatedCount = 0;
                bool anyAutoUpdatePerformed = false;
                bool globalAutoUpdateEnabled = _addonUpdater?.Config?.Settings?.AutoUpdateAfterScan ?? false;

                // Utför auto-update för varje installation om det är aktiverat globalt
                if (globalAutoUpdateEnabled && addons != null)
                {
                    foreach (var installation in installationsDueForScan)
                    {
                        // Hämta addons för denna installation som behöver uppdateras
                        var installationAddons = addons.Where(a =>
                            a.InstallationId == installation.Id && a.NeedsUpdate).ToList();

                        if (installationAddons.Count > 0)
                        {
                            LogMessage($"Starting automatic update of {installationAddons.Count} addons for {installation.Name}");
                            anyAutoUpdatePerformed = true;

                            // Uppdatera varje addon som behöver uppdateras för denna installation
                            foreach (var addon in installationAddons)
                            {
                                try
                                {
                                    LogMessage($"Auto updating addon: {addon.Name} in {installation.Name}");
                                    bool success = await _addonUpdater.UpdateAddon(addon);
                                    if (success)
                                    {
                                        totalUpdatedCount++;
                                        LogMessage($"Successfully updated addon: {addon.Name} in {installation.Name}");
                                    }
                                    else
                                    {
                                        LogMessage($"Failed to update addon: {addon.Name} in {installation.Name}");
                                    }

                                    // Minnesoptimering mellan uppdateringar
                                    if (totalUpdatedCount % 3 == 0)
                                    {
                                        GC.Collect(0, GCCollectionMode.Optimized);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogError($"Error auto updating addon {addon.Name} in {installation.Name}", ex);
                                }
                            }
                        }
                    }
                }

                if (anyAutoUpdatePerformed)
                {
                    // Läs om konfigurationen och hämta uppdaterad data från config
                    _addonUpdater.LoadConfig();
                    addons = _addonUpdater.LoadAddonsFromConfig();

                    LogMessage($"Auto update completed: {totalUpdatedCount} addons updated successfully across all installations");
                }

                // Räkna totala antal addons som behöver uppdateras i alla installationer
                int totalNeedingUpdates = addons?.Count(a => a.NeedsUpdate) ?? 0;

                // Informera lyssnare om att autosökningen är klar
                string message = anyAutoUpdatePerformed
                    ? $"Auto updated {totalUpdatedCount} addons across {installationsDueForScan.Count} installations"
                    : $"Scan completed - {totalNeedingUpdates} updates available across {installationsDueForScan.Count} installations";

                AutoScanCompleted?.Invoke(this, new AutoScanCompletedEventArgs(
                    addons, totalUpdatedCount, anyAutoUpdatePerformed, message));

                LogMessage("Automatic scan completed");
            }
            catch (Exception ex)
            {
                LogError("Error during auto scan", ex);

                // Informera om felet
                AutoScanCompleted?.Invoke(this, new AutoScanCompletedEventArgs(
                    new List<Addon>(), 0, false, $"Auto scan failed: {ex.Message}"));
            }
            finally
            {
                _isScanning = false;

                // MINNESRENSNING: Tvinga garbage collection efter autosökning
                try
                {
                    LogMessage("Starting post-scan memory cleanup");

                    GC.Collect(2, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, true);

                    // Försök frigöra working set
                    var process = System.Diagnostics.Process.GetCurrentProcess();
                    process.Refresh();

                    LogMessage("Post-scan memory cleanup completed");
                }
                catch (Exception ex)
                {
                    LogError("Error during post-scan memory cleanup", ex);
                }
            }
        }

        private List<Installation> GetInstallationsDueForScan()
        {
            var dueInstallations = new List<Installation>();
            var installations = _addonUpdater.GetInstallations();

            // Använd globalt autosökningsintervall
            int globalAutoScanInterval = _addonUpdater?.Config?.Settings?.AutoScanIntervalMinutes ?? 360;

            foreach (var installation in installations)
            {
                // Hoppa över om installationen inte är giltig
                if (string.IsNullOrEmpty(installation.AddonPath) ||
                    !Directory.Exists(installation.AddonPath) ||
                    installation.GameVersionId <= 0)
                    continue;

                // Kontrollera om denna installation ska skannas baserat på det globala intervallet
                DateTime cutoffTime = DateTime.Now.AddMinutes(-globalAutoScanInterval);
                bool needsScan = false;

                // Kontrollera om någon addon i denna installation behöver skannas
                foreach (var addon in installation.Addons.Values)
                {
                    if (string.IsNullOrEmpty(addon.LastChecked))
                    {
                        needsScan = true;
                        break;
                    }

                    if (DateTime.TryParse(addon.LastChecked, out DateTime lastChecked))
                    {
                        if (lastChecked < cutoffTime)
                        {
                            needsScan = true;
                            break;
                        }
                    }
                    else
                    {
                        needsScan = true;
                        break;
                    }
                }

                if (needsScan)
                {
                    dueInstallations.Add(installation);
                    LogMessage($"Installation '{installation.Name}' is due for scanning");
                }
                else
                {
                    LogMessage($"Installation '{installation.Name}' scan is up to date");
                }
            }

            return dueInstallations;
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
                Stop();
                _autoScanTimer = null;

                // Minnesrensning vid dispose
                GC.Collect(0, GCCollectionMode.Optimized);

                LogMessage("Auto scan service disposed");
            }
            _disposed = true;
        }

        private void LogMessage(string message)
        {
            try
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WowAddonUpdater");

                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(logFolder, "app.log");

                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: AutoScanService: {message}\r\n");
            }
            catch
            {
                // Om vi inte kan logga, fortsätt ändå
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
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: AutoScanService: {context}: {ex?.Message}\r\n{ex?.StackTrace}\r\n\r\n");
            }
            catch
            {
                // Om loggning misslyckas är det illa
            }
        }
    }
}