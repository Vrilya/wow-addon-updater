using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WowAddonUpdater.Models;
using WowAddonUpdater.Services;
using WowAddonUpdater.Commands;
using MessageBox = System.Windows.MessageBox;
using System.IO;
using Newtonsoft.Json.Linq;

namespace WowAddonUpdater
{
    public class NotElvUIToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == "ElvUI" ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class MainWindow : Window, IDisposable
    {
        // Kärntjänster
        private readonly AddonUpdater _addonUpdater;
        private readonly ITrayIconService _trayIconService;
        private readonly IAutoScanService _autoScanService;
        private readonly IdleMemoryManager _idleMemoryManager;

        // UI-tillstånd
        private List<Addon> _addons;
        private bool _isScanning;
        private bool _disposed = false;

        // Commands
        public ICommand ScanCommand { get; private set; }
        public ICommand UpdateAllCommand { get; private set; }
        public ICommand SearchCommand { get; private set; }
        public ICommand SettingsCommand { get; private set; }
        public ICommand UpdateSingleAddonCommand { get; private set; }
        public ICommand DeleteAddonCommand { get; private set; }

        // Flagga som styr hur vi hanterar stängning
        private bool _forceClose = false;

        public MainWindow()
        {
            try
            {
                LogMessage("MainWindow constructor starting");

                InitializeComponent();

                // Initiera kärntjänster
                _addonUpdater = new AddonUpdater();
                _trayIconService = new TrayIconService();
                _autoScanService = new AutoScanService(_addonUpdater);
                _idleMemoryManager = new IdleMemoryManager();

                // Initiera UI-tillstånd
                _addons = new List<Addon>();

                // Sätt upp tjänstens händelsehanterare
                SetupServiceEventHandlers();
                SetupMemoryManagementEventHandlers();

                // Initialisera commands
                InitializeCommands();

                // Sätt DataContext för command-bindning
                this.DataContext = this;

                UpdateButtonStates();

                // Kontrollera om programmet ska starta minimerat
                bool shouldStartMinimized = CheckIfShouldStartMinimized();

                if (shouldStartMinimized)
                {
                    LogMessage("Starting minimized to tray");
                    this.WindowState = WindowState.Minimized;
                    this.Loaded += (s, e) =>
                    {
                        this.Hide();
                        _trayIconService.SetVisible(true);
                    };
                }
                else
                {
                    // Läs automatiskt in från config först för snabbare uppstart
                    if (HasValidInstallations())
                    {
                        LoadAddonsFromConfig();
                        _ = CheckIfAutoScanNeeded();
                    }
                }

                // Starta auto scan service
                _autoScanService.Start();
                _idleMemoryManager.Start();

                // Fönsterhändelsehanterare
                this.Closing += MainWindow_Closing;

                LogMessage("MainWindow constructor completed successfully");
            }
            catch (Exception ex)
            {
                LogError("Error in MainWindow constructor", ex);
                MessageBox.Show($"Error initializing application: {ex.Message}\n\nMore details have been logged to: %LocalAppData%\\WowAddonUpdater\\error.log",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetupServiceEventHandlers()
        {
            // Händelser för Tray icon-tjänster
            _trayIconService.ShowMainWindowRequested += async (s, e) =>
            {
                await this.Dispatcher.InvokeAsync(async () => {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    this.Activate();
                    _trayIconService.SetVisible(false);

                    _idleMemoryManager?.NotifyUserActivity();

                    // Uppdatera addon-listan vid öppning från tray
                    await RefreshAddonListFromTray();
                });
            };

            _trayIconService.ExitApplicationRequested += (s, e) =>
            {
                this.Dispatcher.Invoke(() => {
                    _forceClose = true;
                    System.Windows.Application.Current.Shutdown();
                });
            };

            // Händelser för auto scan-tjänsten
            _autoScanService.AutoScanCompleted += (s, e) =>
            {
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Uppdatera addon-listan med nya data
                        if (e.UpdatedAddons != null)
                        {
                            _addons = e.UpdatedAddons;
                            ApplySortingAndUpdateUI();
                        }

                        // Updatera status
                        StatusTextBlock.Text = $"Last scanned: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                        LogMessage($"Auto scan completed: {e.Message}");
                        _idleMemoryManager?.NotifyUserActivity();
                    }
                    catch (Exception ex)
                    {
                        LogError("Error handling auto scan completed event", ex);
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
        }

        private void SetupMemoryManagementEventHandlers()
        {
            try
            {
                _idleMemoryManager.MemoryCleanupPerformed += (s, e) =>
                {
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            LogMessage($"Memory cleanup event: {e.CleanupType}, freed {e.FreedMemoryMB}MB");
                        }
                        catch (Exception ex)
                        {
                            LogError("Error handling memory cleanup event", ex);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                };

                // Lyssna på globala inmatningshändelser för att upptäcka användaraktivitet
                this.MouseMove += (s, e) => _idleMemoryManager?.NotifyUserActivity();
                this.KeyDown += (s, e) => _idleMemoryManager?.NotifyUserActivity();
                this.PreviewMouseDown += (s, e) => _idleMemoryManager?.NotifyUserActivity();

                LogMessage("Memory management event handlers setup completed");
            }
            catch (Exception ex)
            {
                LogError("Error setting up memory management event handlers", ex);
            }
        }

        private void InitializeCommands()
        {
            ScanCommand = new AsyncRelayCommand(
                execute: async () => await ScanForUpdatesAsync(),
                canExecute: () => HasValidInstallations() && !_isScanning
            );

            UpdateAllCommand = new AsyncRelayCommand(
                execute: async () => await UpdateAllAsync(),
                canExecute: () => HasValidInstallations() && !_isScanning && _addons.Any(a => a.NeedsUpdate)
            );

            SearchCommand = new RelayCommand(
                execute: () => OpenSearchWindow(),
                canExecute: () => HasValidInstallations() && !_isScanning
            );

            SettingsCommand = new RelayCommand(
                execute: () => OpenSettingsWindow(),
                canExecute: () => true // Settings kan alltid öppnas
            );

            UpdateSingleAddonCommand = new AsyncRelayCommand<Addon>(
                execute: async (addon) => await UpdateSingleAddonAsync(addon),
                canExecute: (addon) => HasValidInstallations() && !_isScanning && addon?.NeedsUpdate == true
            );

            DeleteAddonCommand = new AsyncRelayCommand<Addon>(
                execute: async (addon) => await DeleteAddonAsync(addon),
                canExecute: (addon) => !_isScanning && addon != null && addon.Name != "ElvUI"
            );
        }

        private bool CheckIfShouldStartMinimized()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                bool commandLineMinimized = args.Contains("--minimized");
                bool settingsMinimized = _addonUpdater?.Config?.Settings?.StartMinimized ?? false;

                LogMessage($"Command line minimized: {commandLineMinimized}");
                LogMessage($"Settings minimized: {settingsMinimized}");

                return commandLineMinimized || settingsMinimized;
            }
            catch (Exception ex)
            {
                LogError("Error checking if should start minimized", ex);
                return false;
            }
        }

        private bool HasValidInstallations()
        {
            var installations = _addonUpdater?.GetInstallations() ?? new List<Installation>();
            return installations.Any(i =>
                !string.IsNullOrEmpty(i.AddonPath) &&
                Directory.Exists(i.AddonPath) &&
                i.GameVersionId > 0);
        }

        private void UpdateButtonStates()
        {
            // Kontrollera om vi har giltiga installationer
            bool hasValidInstallations = HasValidInstallations();
            bool canPerformActions = hasValidInstallations && !_isScanning;

            // Uppdatera knappars aktiverade tillstånd
            ScanButton.IsEnabled = canPerformActions;
            UpdateAllButton.IsEnabled = canPerformActions && (_addons?.Any(a => a.NeedsUpdate) ?? false);
            SearchButton.IsEnabled = canPerformActions;

            // Settings-knappen ska alltid vara aktiv
            SettingsButton.IsEnabled = true;

            // Tvinga kommandohanteraren att utvärdera om alla kommandon
            CommandManager.InvalidateRequerySuggested();
        }

        // Tillämpa sortering och uppdatera UI
        private void ApplySortingAndUpdateUI()
        {
            if (_addons != null)
            {
                _addons = AddonUpdater.SortAddons(_addons, _addonUpdater.Config.Settings.AddonSortMode);
                AddonDataGrid.ItemsSource = null;
                AddonDataGrid.ItemsSource = _addons;
            }
        }

        private void LoadAddonsFromConfig()
        {
            try
            {
                _addonUpdater.LoadConfig();
                _addons = _addonUpdater.LoadAddonsFromConfig(); // Detta inkluderar nu mer sortering
                AddonDataGrid.ItemsSource = _addons;

                // Uppdatera status med senaste kontrollerade tid från konfigurationen
                var allInstallations = _addonUpdater.GetInstallations();
                var lastCheckedTimes = allInstallations
                    .SelectMany(i => i.Addons.Values)
                    .Where(a => !string.IsNullOrEmpty(a.LastChecked))
                    .Select(a => a.LastChecked)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(lastCheckedTimes))
                {
                    StatusTextBlock.Text = $"Last scanned: {lastCheckedTimes}";
                }
                else
                {
                    StatusTextBlock.Text = "Last scanned: Never";
                }

                LogMessage("Loaded addons from config on startup");
            }
            catch (Exception ex)
            {
                LogError("Error loading addons from config", ex);
            }
        }

        private async Task RefreshAddonListFromTray()
        {
            try
            {
                LogMessage("Refreshing addon list after opening from tray");

                if (HasValidInstallations())
                {
                    LoadAddonsFromConfig();
                    LogMessage("Addon list loaded from config successfully (no server scan)");
                }
                else
                {
                    LogMessage("Skipping refresh - no valid installations");
                }
            }
            catch (Exception ex)
            {
                LogError("Error refreshing addon list from tray", ex);
            }
        }

        private async Task CheckIfAutoScanNeeded()
        {
            try
            {
                _addonUpdater.LoadConfig();

                var cutoffTime = DateTime.Now.AddHours(-4);
                bool needsScan = false;

                var installations = _addonUpdater.GetInstallations();
                foreach (var installation in installations)
                {
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

                    if (needsScan) break;
                }

                if (needsScan)
                {
                    LogMessage("Auto-scanning because data is older than 4 hours");
                    await Task.Delay(2000);
                    await ScanForUpdatesAsync();
                }
                else
                {
                    LogMessage("Skipping auto-scan, data is fresh");
                }
            }
            catch (Exception ex)
            {
                LogError("Error checking if auto-scan needed", ex);
            }
        }

        // Command-implementationer
        private async Task ScanForUpdatesAsync()
        {
            System.Diagnostics.Debug.WriteLine("DEBUG: ScanForUpdatesAsync called!");
            if (_isScanning) return;

            _idleMemoryManager?.NotifyUserActivity();

            if (!HasValidInstallations())
            {
                return; // Tyst avslut om inga installationer är konfigurerade
            }

            try
            {
                _isScanning = true;
                UpdateButtonStates();

                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                ShowProgress();

                var progress = new Progress<(int current, int total, string currentAddon)>(report =>
                {
                    UpdateProgress(report.current, report.total, report.currentAddon);
                });

                _addons = await _addonUpdater.ScanForUpdates(progress); // Detta inkluderar nu mer sortering
                AddonDataGrid.ItemsSource = null;
                AddonDataGrid.ItemsSource = _addons;

                StatusTextBlock.Text = $"Last scanned: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                HideProgress();
            }
            catch (Exception ex)
            {
                LogError("Error scanning for updates", ex);
                MessageBox.Show($"Error scanning for updates: {ex.Message}", "Error", MessageBoxButton.OK);
                HideProgress();
            }
            finally
            {
                _isScanning = false;
                UpdateButtonStates();
                Mouse.OverrideCursor = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private async Task UpdateAllAsync()
        {
            if (_isScanning || _addons.Count == 0) return;

            _idleMemoryManager?.NotifyUserActivity();

            if (!HasValidInstallations())
            {
                MessageBox.Show("Please configure at least one WoW installation in Settings before updating addons.",
                              "Configure Installations", MessageBoxButton.OK);
                return;
            }

            try
            {
                _isScanning = true;
                UpdateButtonStates();

                ShowProgress();
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                int updatedCount = 0;
                int totalAddons = _addons.Count(a => a.NeedsUpdate);

                foreach (var addon in _addons.Where(a => a.NeedsUpdate))
                {
                    updatedCount++;
                    UpdateProgress(updatedCount, totalAddons, addon.Name);

                    await _addonUpdater.UpdateAddon(addon);
                }

                _addonUpdater.LoadConfig();
                _addons = _addonUpdater.LoadAddonsFromConfig(); // Detta inkluderar nu mer sortering

                AddonDataGrid.ItemsSource = null;
                AddonDataGrid.ItemsSource = _addons;

                StatusTextBlock.Text = $"Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                HideProgress();

                MessageBox.Show($"Updated {totalAddons} addons successfully across all installations.", "Update Complete", MessageBoxButton.OK);
            }
            catch (Exception ex)
            {
                LogError("Error updating addons", ex);
                MessageBox.Show($"Error updating addons: {ex.Message}", "Error", MessageBoxButton.OK);
                HideProgress();
            }
            finally
            {
                _isScanning = false;
                UpdateButtonStates();
                Mouse.OverrideCursor = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private async Task UpdateSingleAddonAsync(Addon addon)
        {
            if (_isScanning || addon == null) return;

            _idleMemoryManager?.NotifyUserActivity();

            if (!HasValidInstallations())
            {
                MessageBox.Show("Please configure at least one WoW installation in Settings before updating addons.",
                              "Configure Installations", MessageBoxButton.OK);
                return;
            }

            try
            {
                _isScanning = true;
                UpdateButtonStates();

                ShowProgress();
                UpdateProgress(1, 1, addon.Name);
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                // Hämta ny FileId för individuella uppdateringar från cachelagrad data
                if (addon.Id.HasValue && addon.Name != "ElvUI")
                {
                    var installation = _addonUpdater.GetInstallations().FirstOrDefault(i => i.Id == addon.InstallationId);
                    if (installation != null)
                    {
                        var addonDetails = await _addonUpdater.GetCurseForgeService().GetAddonDetails(addon.Id.Value);
                        var files = addonDetails["data"];

                        JObject compatibleFile = null;
                        foreach (var file in files)
                        {
                            var gameVersionTypeIds = file["gameVersionTypeIds"];
                            if (gameVersionTypeIds != null)
                            {
                                foreach (var id in gameVersionTypeIds)
                                {
                                    if (id.Value<int>() == installation.GameVersionId)
                                    {
                                        compatibleFile = (JObject)file;
                                        break;
                                    }
                                }
                            }
                            if (compatibleFile != null) break;
                        }

                        if (compatibleFile != null && compatibleFile["id"]?.Value<int?>() is int fileId)
                        {
                            addon.FileId = fileId;
                        }
                    }
                }

                bool success = await _addonUpdater.UpdateAddon(addon);

                if (success)
                {
                    _addonUpdater.LoadConfig();
                    _addons = _addonUpdater.LoadAddonsFromConfig(); // Detta inkluderar nu mer sortering

                    AddonDataGrid.ItemsSource = null;
                    AddonDataGrid.ItemsSource = _addons;

                    StatusTextBlock.Text = $"Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                    MessageBox.Show($"{addon.Name} has been updated successfully in {addon.InstallationName}.", "Update Complete", MessageBoxButton.OK);
                }
                else
                {
                    MessageBox.Show($"Failed to update {addon.Name} in {addon.InstallationName}.", "Update Failed", MessageBoxButton.OK);
                }

                HideProgress();
            }
            catch (Exception ex)
            {
                LogError($"Error updating addon {addon.Name}", ex);
                MessageBox.Show($"Error updating {addon.Name}: {ex.Message}", "Error", MessageBoxButton.OK);
                HideProgress();
            }
            finally
            {
                _isScanning = false;
                UpdateButtonStates();
                Mouse.OverrideCursor = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        private async Task DeleteAddonAsync(Addon addon)
        {
            if (_isScanning || addon == null) return;

            _idleMemoryManager?.NotifyUserActivity();

            if (MessageBox.Show($"Are you sure you want to delete {addon.Name} from {addon.InstallationName}?\nAll associated folders will be removed.",
                             "Confirm Deletion", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    _isScanning = true;
                    UpdateButtonStates();

                    Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                    bool success = _addonUpdater.DeleteAddon(addon);

                    if (success)
                    {
                        _addonUpdater.LoadConfig();
                        _addons = _addonUpdater.LoadAddonsFromConfig(); // Detta inkluderar nu mer sortering

                        AddonDataGrid.ItemsSource = null;
                        AddonDataGrid.ItemsSource = _addons;

                        StatusTextBlock.Text = $"Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

                        // BORTTAGET: Success message - onödig eftersom användaren ser att addonen försvinner
                        // MessageBox.Show($"{addon.Name} has been deleted successfully from {addon.InstallationName}.", "Deletion Complete", MessageBoxButton.OK);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to delete {addon.Name} from {addon.InstallationName}.", "Deletion Failed", MessageBoxButton.OK);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error deleting addon {addon.Name}", ex);
                    MessageBox.Show($"Error deleting {addon.Name}: {ex.Message}", "Error", MessageBoxButton.OK);
                }
                finally
                {
                    _isScanning = false;
                    UpdateButtonStates();
                    Mouse.OverrideCursor = null;

                    GC.Collect();
                }
            }
        }

        private void OpenSearchWindow()
        {
            _idleMemoryManager?.NotifyUserActivity();

            if (!HasValidInstallations())
            {
                MessageBox.Show("Please configure at least one WoW installation in Settings before searching for addons.",
                              "Configure Installations", MessageBoxButton.OK);
                return;
            }

            var searchWindow = new SearchWindow(_addonUpdater, () => {
                _addonUpdater.LoadConfig();
                _addons = _addonUpdater.LoadAddonsFromConfig(); // Detta inkluderar nu mer sortering
                AddonDataGrid.ItemsSource = null;
                AddonDataGrid.ItemsSource = _addons;
                StatusTextBlock.Text = $"Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            });
            searchWindow.Owner = this;
            searchWindow.ShowDialog();
        }

        private void OpenSettingsWindow()
        {
            _idleMemoryManager?.NotifyUserActivity();

            var settingsWindow = new SettingsWindow(_addonUpdater, async () =>
            {
                await RefreshAfterSettings();
            });

            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        // Metoder för progress-UI
        private void ShowProgress()
        {
            ProgressSection.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressTextBlock.Text = "Preparing to scan...";
            ProgressCounterBlock.Text = "0 / 0";
        }

        private void HideProgress()
        {
            ProgressSection.Visibility = Visibility.Collapsed;
        }

        private void UpdateProgress(int current, int total, string currentAddon)
        {
            ProgressBar.Maximum = total;
            ProgressBar.Value = current;

            if (!string.IsNullOrEmpty(currentAddon))
            {
                ProgressTextBlock.Text = $"Scanning {currentAddon}...";
            }
            else
            {
                ProgressTextBlock.Text = "Scanning addons...";
            }

            ProgressCounterBlock.Text = $"{current} / {total}";
        }

        public void ShowSynchronizationProgress(int current, int total, string currentAddon)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    ProgressSection.Visibility = Visibility.Visible;
                    ProgressBar.Maximum = total;
                    ProgressBar.Value = current;

                    if (!string.IsNullOrEmpty(currentAddon))
                    {
                        ProgressTextBlock.Text = $"Synchronizing {currentAddon}...";
                    }
                    else
                    {
                        ProgressTextBlock.Text = "Synchronizing addons...";
                    }

                    ProgressCounterBlock.Text = $"{current} / {total}";
                });
            }
            catch (Exception ex)
            {
                LogError("Error showing synchronization progress", ex);
            }
        }

        public void HideSynchronizationProgress()
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    HideProgress();
                });
            }
            catch (Exception ex)
            {
                LogError("Error hiding synchronization progress", ex);
            }
        }

        // Händelsehanterare som fortfarande använder gammalt mönster (från XAML)
        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (ScanCommand.CanExecute(null))
                ScanCommand.Execute(null);
        }

        private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateAllCommand.CanExecute(null))
                UpdateAllCommand.Execute(null);
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (System.Windows.Controls.Button)sender;
            var addon = (Addon)button.DataContext;

            if (UpdateSingleAddonCommand.CanExecute(addon))
                UpdateSingleAddonCommand.Execute(addon);
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (System.Windows.Controls.Button)sender;
            var addon = (Addon)button.DataContext;

            if (DeleteAddonCommand.CanExecute(addon))
                DeleteAddonCommand.Execute(addon);
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchCommand.CanExecute(null))
                SearchCommand.Execute(null);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsCommand.CanExecute(null))
                SettingsCommand.Execute(null);
        }

        // Fönstrets livscykel
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            LogMessage($"Window closing event - ForceClose: {_forceClose}");

            if (_forceClose)
            {
                Dispose();
                System.Windows.Application.Current.Shutdown();
                return;
            }

            try
            {
                bool minimizeToTray = _addonUpdater?.Config?.Settings?.MinimizeToTray ?? false;
                LogMessage($"MinimizeToTray setting: {minimizeToTray}");

                if (minimizeToTray && _trayIconService != null)
                {
                    e.Cancel = true;
                    this.Hide();
                    _trayIconService.SetVisible(true);
                    LogMessage("Window hidden and application minimized to tray");
                }
                else
                {
                    Dispose();
                    System.Windows.Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                LogError("Error in close handler", ex);
                MessageBox.Show("Error in tray functionality: " + ex.Message);
                Dispose();
                System.Windows.Application.Current.Shutdown();
            }
        }

        // Publika metoder för extern användning
        public AddonUpdater GetAddonUpdater()
        {
            return _addonUpdater;
        }

        public IAutoScanService GetAutoScanService()
        {
            return _autoScanService;
        }

        public async Task RefreshAfterSettings()
        {
            System.Diagnostics.Debug.WriteLine("DEBUG: RefreshAfterSettings called!");
            _idleMemoryManager?.NotifyUserActivity();
            UpdateButtonStates();
            _autoScanService.RefreshConfiguration();

            // Kontrollera om vi har installationer som faktiskt är redo för scanning
            if (!HasReadyInstallationsForScanning())
            {
                // Ingen installation är redo för scanning - bara ladda från config
                if (HasValidInstallations())
                {
                    LoadAddonsFromConfig(); // Ladda befintlig data utan scanning
                    LogMessage("Loaded addons from config - no scanning needed");
                }
                else
                {
                    // Inga giltiga installationer alls - rensa listan
                    _addons.Clear();
                    AddonDataGrid.ItemsSource = null;
                    AddonDataGrid.ItemsSource = _addons;
                    StatusTextBlock.Text = "No installations configured";
                    LogMessage("Cleared addon list - no valid installations remaining");
                }
                return;
            }

            // Kolla om vi bara behöver uppdatera från config istället för full scan
            var installations = _addonUpdater.GetInstallations();
            bool hasAddonsInConfig = installations.Any(i => i.Addons.Count > 0);

            if (hasAddonsInConfig)
            {
                // Vi har befintliga addons i config - bara ladda från config först
                LoadAddonsFromConfig();
                LogMessage("Loaded existing addons from config - skipping immediate scan");
                return;
            }

            // Vi har redo installationer men inga addons - gör en riktig scan
            await ScanForUpdatesAsync();
        }

        // Tvingar en riktig scan oavsett om addons finns i config eller inte
        public async Task ForceScanAfterAutoDetect()
        {
            System.Diagnostics.Debug.WriteLine("DEBUG: ForceScanAfterAutoDetect called!");
            _idleMemoryManager?.NotifyUserActivity();
            UpdateButtonStates();
            _autoScanService.RefreshConfiguration();

            // Tvinga en riktig scan - hoppa över all "smart" logik
            if (HasValidInstallations())
            {
                await ScanForUpdatesAsync(); // Direkt scan utan några checks
                LogMessage("Forced scan completed after auto-detect");
            }
            else
            {
                // Inga giltiga installationer alls - rensa listan
                _addons.Clear();
                AddonDataGrid.ItemsSource = null;
                AddonDataGrid.ItemsSource = _addons;
                StatusTextBlock.Text = "No installations configured";
                LogMessage("Cleared addon list - no valid installations remaining");
            }
        }

        // Kontrollerar om vi har installationer som är redo för scanning
        private bool HasReadyInstallationsForScanning()
        {
            var installations = _addonUpdater?.GetInstallations() ?? new List<Installation>();
            return installations.Any(i =>
                !string.IsNullOrEmpty(i.Name?.Trim()) &&
                !string.IsNullOrEmpty(i.AddonPath?.Trim()) &&
                Directory.Exists(i.AddonPath) &&
                i.GameVersionId > 0);
        }

        public void RefreshUIAfterSettings()
        {
            System.Diagnostics.Debug.WriteLine("DEBUG: RefreshUIAfterSettings called!");
            _idleMemoryManager?.NotifyUserActivity();

            UpdateButtonStates();
            _autoScanService.RefreshConfiguration();

            if (HasValidInstallations())
            {
                LoadAddonsFromConfig();
            }
        }

        // Tillämpa ny sortering när användaren ändrar sorteringspreferens
        public void ApplyNewSorting()
        {
            try
            {
                if (_addons != null)
                {
                    _addonUpdater.LoadConfig(); // Läs om konfigurationen för att hämta ny sorteringspreferens
                    ApplySortingAndUpdateUI();
                    LogMessage($"Applied new sorting: {_addonUpdater.Config.Settings.AddonSortMode}");
                }
            }
            catch (Exception ex)
            {
                LogError("Error applying new sorting", ex);
            }
        }

        // IDisposable-implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _autoScanService?.Dispose();
                _trayIconService?.Dispose();
                _addonUpdater?.Dispose();
                _idleMemoryManager?.Dispose();
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

                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: MainWindow: {message}\r\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}. Original message: {message}");
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
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: MainWindow: {context}: {ex?.Message}\r\n{ex?.StackTrace}\r\n\r\n");
            }
            catch (Exception logEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error logging failed: {logEx.Message}. Original error - {context}: {ex?.Message}");
            }
        }
    }
}