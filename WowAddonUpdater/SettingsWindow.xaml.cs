using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using WowAddonUpdater.Services;
using WowAddonUpdater.Models;
using WowAddonUpdater.Controls;
using MessageBox = System.Windows.MessageBox;
using AutoScanInterval = WowAddonUpdater.Services.AutoScanInterval;
using System.Windows.Controls;

namespace WowAddonUpdater
{
    public partial class SettingsWindow : Window, IDisposable
    {
        private readonly AddonUpdater _addonUpdater;
        private readonly Action _settingsSavedCallback;
        private readonly StartupRegistryService _startupRegistryService;
        private bool _isInitializing = true;
        private bool _disposed = false;
        private List<InstallationControl> _installationControls = new List<InstallationControl>();

        public SettingsWindow(AddonUpdater addonUpdater, Action settingsSavedCallback)
        {
            InitializeComponent();

            _addonUpdater = addonUpdater;
            _settingsSavedCallback = settingsSavedCallback;
            _startupRegistryService = new StartupRegistryService();

            // Hantera fönsterstängning för att säkerställa korrekt frigöring
            Closing += SettingsWindow_Closing;

            // Initialisera användargränssnitt
            InitializeUI();
        }

        private void InitializeUI()
        {
            try
            {
                _isInitializing = true;

                // Läs in globala inställningar
                LoadGlobalSettings();

                // Läs in installationer
                LoadInstallations();

                _isInitializing = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing settings window: {ex.Message}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadGlobalSettings()
        {
            // Läs in nuvarande globala inställningar
            MinimizeToTrayCheckBox.IsChecked = _addonUpdater.Config.Settings.MinimizeToTray;
            StartWithWindowsCheckBox.IsChecked = _addonUpdater.Config.Settings.StartWithWindows;
            StartMinimizedCheckBox.IsChecked = _addonUpdater.Config.Settings.StartMinimized;
            AutoScanCheckBox.IsChecked = _addonUpdater.Config.Settings.AutoScanEnabled;
            AutoUpdateAfterScanCheckBox.IsChecked = _addonUpdater.Config.Settings.AutoUpdateAfterScan;

            // Initialisera auto scan interval dropdown (rullgardin)
            InitializeAutoScanDropdown();

            // Initialisera addon sort dropdown (rullgardin)
            InitializeAddonSortDropdown();

            // Uppdatera kontrollernas tillstånd
            UpdateAutoScanControlStates();

            UseCustomUserAgentCheckBox.IsChecked = _addonUpdater.Config.Settings.UseCustomUserAgent;
            CustomUserAgentTextBox.Text = _addonUpdater.Config.Settings.CustomUserAgent; // Tom om inte satt
            UpdateUserAgentControlStates();
        }

        private void UseCustomUserAgentCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                UpdateUserAgentControlStates();
                SaveGlobalSettings();
            }
        }

        private void CustomUserAgentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                SaveGlobalSettings();
            }
        }

        private void UpdateUserAgentControlStates()
        {
            bool customEnabled = UseCustomUserAgentCheckBox.IsChecked ?? false;
            CustomUserAgentTextBox.IsEnabled = customEnabled;

            if (!customEnabled)
            {
                CustomUserAgentTextBox.Background = System.Windows.Media.Brushes.LightGray;
            }
            else
            {
                CustomUserAgentTextBox.Background = System.Windows.Media.Brushes.White;
            }
        }

        // Initiera dropdown för sortering av addons
        private void InitializeAddonSortDropdown()
        {
            var sortOptions = AddonSortProvider.GetSortOptions();
            AddonSortComboBox.ItemsSource = sortOptions;

            // Välj det sparade sorteringsläget
            var selectedOption = sortOptions.FirstOrDefault(o => o.Mode == _addonUpdater.Config.Settings.AddonSortMode);
            if (selectedOption != null)
            {
                AddonSortComboBox.SelectedItem = selectedOption;
            }
            else
            {
                // Standardinställning till första alternativet (Namn)
                AddonSortComboBox.SelectedIndex = 0;
            }
        }

        private void LoadInstallations()
        {
            try
            {
                // Rensa befintliga kontroller
                InstallationsPanel.Children.Clear();
                _installationControls.Clear();

                var installations = _addonUpdater.GetInstallations();

                if (installations.Count == 0)
                {
                    // Visa meddelande när inga installationer finns
                    var noInstallationsMessage = new System.Windows.Controls.TextBlock
                    {
                        Text = "No WoW installations configured yet. Click 'Add New' to get started.",
                        FontStyle = FontStyles.Italic,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 20)
                    };
                    InstallationsPanel.Children.Add(noInstallationsMessage);
                }
                else
                {
                    // Skapa kontroller för varje installation
                    foreach (var installation in installations)
                    {
                        var installationControl = new InstallationControl(
                            _addonUpdater,
                            installation,
                            OnInstallationRemoveRequested,
                            OnInstallationChanged);

                        _installationControls.Add(installationControl);
                        InstallationsPanel.Children.Add(installationControl);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading installations: {ex.Message}",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnInstallationChanged()
        {
            try
            {
                // Spara alla installationer och meddela om ändringar
                _addonUpdater.SaveConfig();
                _settingsSavedCallback?.Invoke();
            }
            catch (Exception ex)
            {
                LogError("Error handling installation change", ex);
            }
        }

        private void OnInstallationRemoveRequested(InstallationControl installationControl)
        {
            try
            {
                // Ta bort från AddonUpdater
                bool removed = _addonUpdater.RemoveInstallation(installationControl.Installation.Id);

                if (removed)
                {
                    // Ta bort från användargränssnitt
                    _installationControls.Remove(installationControl);
                    InstallationsPanel.Children.Remove(installationControl);

                    // Uppdatera installationsvyn om den nu är tom
                    if (_installationControls.Count == 0)
                    {
                        LoadInstallations();
                    }

                    // Spara config och uppdatera MainWindow direkt från config
                    _addonUpdater.SaveConfig();

                    // Hitta MainWindow och uppdatera UI direkt från config
                    var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                    if (mainWindow != null)
                    {
                        mainWindow.RefreshUIAfterSettings(); // Denna metod finns redan och laddar från config
                    }

                    MessageBox.Show($"Installation '{installationControl.Installation.Name}' has been removed successfully.",
                        "Installation Removed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to remove installation '{installationControl.Installation.Name}'.",
                        "Removal Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing installation: {ex.Message}",
                    "Removal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SettingsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Dispose();
        }

        // Händelserhanterare
        private void AddInstallationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Visa dialogruta för att ange installationsnamn
                string installationName = ShowInstallationNameDialog();
                if (string.IsNullOrEmpty(installationName))
                {
                    return; // Användaren avbröt
                }

                // Skapa ny installation
                var newInstallation = _addonUpdater.AddInstallation(installationName, "", 0);

                // Uppdatera installationsvy
                LoadInstallations();

                // Ta bort OnInstallationChanged() här!
                // Den nya installationen är inte redo för scanning än - bara spara config utan callbacks
                // OnInstallationChanged(); // BORTTAGET - orsakar onödig scanning

                // Bara spara config tyst
                _addonUpdater.SaveConfig();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding installation: {ex.Message}",
                    "Add Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ShowInstallationNameDialog()
        {
            // Enkel inmatningsdialog
            var dialog = new InstallationNameDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                return dialog.InstallationName;
            }

            return null;
        }

        private void MinimizeToTrayCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                SaveGlobalSettings();
            }
        }

        // Sorteringsval för addon-dropdown ändrat
        private void AddonSortComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isInitializing && AddonSortComboBox.SelectedItem is AddonSortOption selectedOption)
            {
                // Spara det nya sorteringsläget
                _addonUpdater.Config.Settings.AddonSortMode = selectedOption.Mode;
                _addonUpdater.SaveConfig();

                LogMessage($"Addon sort mode changed to: {selectedOption.Mode}");

                // Tillämpa ny sortering omedelbart i huvudfönstret
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.ApplyNewSorting();
                }
            }
        }

        private void StartWithWindowsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                bool isChecked = StartWithWindowsCheckBox.IsChecked ?? false;

                try
                {
                    _startupRegistryService.SetStartupRegistry(isChecked);
                    SaveGlobalSettings();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error setting Windows startup: {ex.Message}", "Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);

                    // Återställ kryssrutan till rätt tillstånd
                    StartWithWindowsCheckBox.Checked -= StartWithWindowsCheckBox_CheckedChanged;
                    StartWithWindowsCheckBox.Unchecked -= StartWithWindowsCheckBox_CheckedChanged;
                    StartWithWindowsCheckBox.IsChecked = !isChecked;
                    StartWithWindowsCheckBox.Checked += StartWithWindowsCheckBox_CheckedChanged;
                    StartWithWindowsCheckBox.Unchecked += StartWithWindowsCheckBox_CheckedChanged;
                }
            }
        }

        private void StartMinimizedCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                SaveGlobalSettings();
            }
        }

        private void AutoScanCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                UpdateAutoScanControlStates();
                SaveGlobalSettings();

                // Uppdatera autosöknings-tjänsten direkt vid på/avslagning
                RefreshAutoScanService();
            }
        }

        private void AutoScanIntervalComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                SaveGlobalSettings();

                // Uppdatera autosöknings-tjänsten omedelbart vid ändring av intervall
                RefreshAutoScanService();
            }
        }

        private void AutoUpdateAfterScanCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                SaveGlobalSettings();

                // Ingen uppdatering av autosöknings-tjänsten behövs för denna inställning
            }
        }

        private void InitializeAutoScanDropdown()
        {
            var autoScanIntervals = GameVersionProvider.GetAutoScanIntervals();
            AutoScanIntervalComboBox.ItemsSource = autoScanIntervals;

            // Välj sparat intervall eller använd standardvärdet 6 timmar
            var selectedInterval = autoScanIntervals.FirstOrDefault(i => i.Minutes == _addonUpdater.Config.Settings.AutoScanIntervalMinutes);
            if (selectedInterval != null)
            {
                AutoScanIntervalComboBox.SelectedItem = selectedInterval;
            }
            else
            {
                // Sätt standardvärde till 6 timmar (360 minuter)
                var defaultInterval = autoScanIntervals.FirstOrDefault(i => i.Minutes == 360);
                if (defaultInterval != null)
                {
                    AutoScanIntervalComboBox.SelectedItem = defaultInterval;
                }
                else
                {
                    AutoScanIntervalComboBox.SelectedIndex = 0; // Fallback
                }
            }
        }

        private void UpdateAutoScanControlStates()
        {
            bool autoScanEnabled = AutoScanCheckBox.IsChecked ?? false;
            AutoScanIntervalLabel.IsEnabled = autoScanEnabled;
            AutoScanIntervalComboBox.IsEnabled = autoScanEnabled;
            AutoUpdateAfterScanCheckBox.IsEnabled = autoScanEnabled;
        }

        private void SaveGlobalSettings()
        {
            try
            {
                _addonUpdater.Config.Settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked ?? false;
                _addonUpdater.Config.Settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked ?? false;
                _addonUpdater.Config.Settings.StartMinimized = StartMinimizedCheckBox.IsChecked ?? false;
                _addonUpdater.Config.Settings.AutoScanEnabled = AutoScanCheckBox.IsChecked ?? false;
                _addonUpdater.Config.Settings.AutoUpdateAfterScan = AutoUpdateAfterScanCheckBox.IsChecked ?? false;

                if (AutoScanIntervalComboBox.SelectedItem is AutoScanInterval selectedInterval)
                {
                    _addonUpdater.Config.Settings.AutoScanIntervalMinutes = selectedInterval.Minutes;
                }

                // Spara addon-sorteringsläge (hanteras i AddonSortComboBox_SelectionChanged)

                _addonUpdater.Config.Settings.UseCustomUserAgent = UseCustomUserAgentCheckBox.IsChecked ?? false;
                _addonUpdater.Config.Settings.CustomUserAgent = CustomUserAgentTextBox.Text?.Trim() ?? "";

                _addonUpdater.SaveConfig();
            }
            catch (Exception ex)
            {
                LogError("Error saving global settings", ex);
            }
        }

        private void RefreshAutoScanService()
        {
            try
            {
                // Hämta referens till MainWindow och uppdatera dess autosöknings-tjänst
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    // Hämta AutoScanService från MainWindow och uppdatera den
                    var autoScanService = mainWindow.GetAutoScanService();
                    if (autoScanService != null)
                    {
                        autoScanService.RefreshConfiguration();

                        // Logga vad som hände
                        bool isEnabled = AutoScanCheckBox.IsChecked ?? false;
                        if (isEnabled)
                        {
                            var selectedInterval = AutoScanIntervalComboBox.SelectedItem as AutoScanInterval;
                            int minutes = selectedInterval?.Minutes ?? 360;
                            LogMessage($"Auto scan service refreshed - enabled with {minutes} minute interval");
                        }
                        else
                        {
                            LogMessage("Auto scan service refreshed - disabled");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Error refreshing auto scan service", ex);
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
                // Rensa installationskontroller
                _installationControls?.Clear();
                _installationControls = null;

                // Minnesrensning
                GC.Collect(0, GCCollectionMode.Optimized);
            }
            _disposed = true;
        }

        private void LogMessage(string message)
        {
            try
            {
                string logFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WowAddonUpdater");

                System.IO.Directory.CreateDirectory(logFolder);

                string logFile = System.IO.Path.Combine(logFolder, "app.log");

                System.IO.File.AppendAllText(logFile, $"{DateTime.Now}: SettingsWindow: {message}\r\n");
            }
            catch
            {
                // Om loggning misslyckas, fortsätt ändå
            }
        }

        private void LogError(string context, Exception ex)
        {
            try
            {
                string logFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WowAddonUpdater");

                System.IO.Directory.CreateDirectory(logFolder);

                string logFile = System.IO.Path.Combine(logFolder, "error.log");

                System.IO.File.AppendAllText(logFile,
                    $"{DateTime.Now}: SettingsWindow: {context}: {ex?.Message}\r\n{ex?.StackTrace}\r\n\r\n");
            }
            catch
            {
                // Om loggning misslyckas är det illa
            }
        }
    }

    // Enkel dialog för att ange installationsnamn
    public class InstallationNameDialog : Window
    {
        private System.Windows.Controls.TextBox _nameTextBox;
        public string InstallationName { get; private set; }

        public InstallationNameDialog()
        {
            Title = "Add New Installation";
            Width = 400;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new System.Windows.Controls.Grid();
            grid.Margin = new Thickness(20);

            var stackPanel = new System.Windows.Controls.StackPanel();

            // Titel
            var titleBlock = new System.Windows.Controls.TextBlock
            {
                Text = "Enter a name for this WoW installation:",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 15)
            };
            stackPanel.Children.Add(titleBlock);

            // Text-box
            _nameTextBox = new System.Windows.Controls.TextBox
            {
                FontSize = 14,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 20)
            };
            _nameTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    OkButton_Click(null, null);
                }
            };
            stackPanel.Children.Add(_nameTextBox);

            // Knappar
            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var okButton = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0)
            };
            okButton.Click += OkButton_Click;
            buttonPanel.Children.Add(okButton);

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30
            };
            cancelButton.Click += (s, e) => { DialogResult = false; };
            buttonPanel.Children.Add(cancelButton);

            stackPanel.Children.Add(buttonPanel);
            grid.Children.Add(stackPanel);
            Content = grid;

            Loaded += (s, e) => _nameTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string name = _nameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a name for the installation.", "Name Required",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            InstallationName = name;
            DialogResult = true;
        }
    }
}