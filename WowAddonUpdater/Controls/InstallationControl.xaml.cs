using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WowAddonUpdater.Models;
using WowAddonUpdater.Services;
using MessageBox = System.Windows.MessageBox;

namespace WowAddonUpdater.Controls
{
    public partial class InstallationControl : System.Windows.Controls.UserControl
    {
        private readonly AddonUpdater _addonUpdater;
        private readonly Action<InstallationControl> _onRemoveRequested;
        private readonly Action _onInstallationChanged;
        private Installation _installation;
        private bool _isInitializing = true;
        private bool _isExpanded = false; // Börja kollapsad för snyggare utseende
        private bool _isAutoDetecting = false; // NY FLAGGA FÖR ATT FÖRHINDRA CALLBACKS

        // Tjänster för hantering av addons
        private ElvUIService _elvUIService;
        private ElvUIManagementService _elvUIManagementService;
        private AddonDetectionService _addonDetectionService;
        private MainWindow _mainWindow;

        public Installation Installation => _installation;

        public InstallationControl(AddonUpdater addonUpdater, Installation installation,
            Action<InstallationControl> onRemoveRequested, Action onInstallationChanged)
        {
            InitializeComponent();

            _addonUpdater = addonUpdater ?? throw new ArgumentNullException(nameof(addonUpdater));
            _installation = installation ?? throw new ArgumentNullException(nameof(installation));
            _onRemoveRequested = onRemoveRequested;
            _onInstallationChanged = onInstallationChanged;

            // Hämta referens till MainWindow
            _mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;

            // Initiera tjänster
            _elvUIService = new ElvUIService(_addonUpdater.GetCurseForgeService().GetHttpClient());
            _elvUIManagementService = new ElvUIManagementService(_addonUpdater, _elvUIService);
            _addonDetectionService = new AddonDetectionService(_addonUpdater, _mainWindow);

            // Säkerställ att UI laddas helt innan vi uppdaterar controls
            this.Loaded += (s, e) => {
                UpdateControlStates(); // Uppdatera när UI är helt laddat
            };

            InitializeUI();
        }

        private void InitializeUI()
        {
            try
            {
                _isInitializing = true;

                // Ange installationsnamn
                NameTextBox.Text = _installation.Name;

                // Initiera rullgardinsmeny för game version
                InitializeGameVersionDropdown();

                // Initiera rullgardinsmeny för färgval
                InitializeColorDropdown();

                // Ange aktuella värden
                AddonPathTextBox.Text = _installation.AddonPath;
                ElvUICheckBox.IsChecked = _installation.IncludeElvUI;

                // Sätt sökvägsförhandsvisning EFTER att textrutans värde har angetts
                UpdatePathPreview();

                // Synka UI med intern expand-status
                SyncExpandUIState();

                // Updatera ControlStates
                UpdateControlStates();

                _isInitializing = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing installation control: {ex.Message}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Säkerställer att auto-detect knappen alltid syns
        private void EnsureAutoDetectButtonVisible()
        {
            try
            {
                if (DetectAddonsButton != null)
                {
                    DetectAddonsButton.Visibility = Visibility.Visible;
                    DetectAddonsButton.Width = 32;
                    DetectAddonsButton.Height = 32;
                    DetectAddonsButton.Content = "🔍";
                    DetectAddonsButton.Background = (System.Windows.Media.Brush)this.FindResource("SecondaryColor");
                    DetectAddonsButton.Foreground = System.Windows.Media.Brushes.White;

                    // Tvinga refresh
                    DetectAddonsButton.UpdateLayout();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ensuring auto-detect button visible: {ex.Message}");
            }
        }

        // Synkroniserar UI med intern expand-status
        private void SyncExpandUIState()
        {
            try
            {
                if (DetailsPanel != null && ExpandButton != null)
                {
                    DetailsPanel.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
                    ExpandButton.Content = _isExpanded ? "▼" : "▶";
                }

                // Säkerställ att auto-detect knappen syns varje gång UI synkas
                EnsureAutoDetectButtonVisible();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error syncing expand UI state: {ex.Message}");
            }
        }

        private void InitializeGameVersionDropdown()
        {
            var gameVersionsWithPlaceholder = GameVersionProvider.GetGameVersionsWithPlaceholder();
            GameVersionComboBox.ItemsSource = gameVersionsWithPlaceholder;

            // Välj sparad spelversion eller använd platshållare som standard
            if (_installation.GameVersionId > 0)
            {
                GameVersion selectedVersion = gameVersionsWithPlaceholder.Find(v => v.Id == _installation.GameVersionId);
                if (selectedVersion != null)
                {
                    GameVersionComboBox.SelectedItem = selectedVersion;
                }
                else
                {
                    GameVersionComboBox.SelectedIndex = 0; // Använd en platshållare tills ett värde har valts
                }
            }
            else
            {
                GameVersionComboBox.SelectedIndex = 0; // Använd platshållare som standard
            }
        }

        private void InitializeColorDropdown()
        {
            var colorOptions = InstallationColorService.GetColorOptions();
            ColorComboBox.ItemsSource = colorOptions;

            // Välj sparad färg eller använd ingen färg som standard
            string savedColorHex = _installation.ColorHex ?? "";

            // Hitta det färgalternativ som matchar i samma lista vi just satte som ItemsSource
            for (int i = 0; i < colorOptions.Count; i++)
            {
                if (colorOptions[i].HexValue == savedColorHex)
                {
                    ColorComboBox.SelectedIndex = i;
                    return;
                }
            }

            // Använd första alternativet (Ingen färg) som standard om inget matchande hittas
            ColorComboBox.SelectedIndex = 0;
        }

        private void UpdatePathPreview()
        {
            // Läs direkt från TextBox istället för från _installation.AddonPath
            string currentPath = AddonPathTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(currentPath))
            {
                PathPreviewTextBlock.Text = "No path configured";
                return;
            }

            string path = currentPath;
            if (path.Length > 50)
            {
                path = "..." + path.Substring(path.Length - 47);
            }
            PathPreviewTextBlock.Text = path;
        }

        private void UpdateControlStates()
        {
            try
            {
                // Säkerställ att auto-detect knappen är synlig FÖRST
                EnsureAutoDetectButtonVisible();

                // Säkerställ att alla UI-element existerar innan vi uppdaterar dem
                if (DetectAddonsButton == null || ElvUICheckBox == null)
                {
                    return; // UI inte helt laddat än
                }

                bool gameVersionSelected = IsGameVersionSelected();
                bool hasValidPath = !string.IsNullOrEmpty(_installation.AddonPath) &&
                                   Directory.Exists(_installation.AddonPath);

                // ElvUI-kryssrutan är aktiverad endast om både spelversion har valts OCH sökvägen är giltig
                ElvUICheckBox.IsEnabled = gameVersionSelected && hasValidPath;

                // Färgväljaren är alltid aktiverad (färg kan ställas in även innan sökvägen är konfigurerad)
                ColorComboBox.IsEnabled = true;

                // Detect addons button ska ALLTID vara synlig, bara enabled/disabled
                DetectAddonsButton.Visibility = Visibility.Visible; // ALLTID synlig
                DetectAddonsButton.IsEnabled = gameVersionSelected && hasValidPath;

                // Förbättra visuell feedback för disabled state
                if (DetectAddonsButton.IsEnabled)
                {
                    DetectAddonsButton.Opacity = 1.0;
                    DetectAddonsButton.ToolTip = "Auto-detect addons";
                }
                else
                {
                    DetectAddonsButton.Opacity = 0.5; // Gör disabled knapp halvtransparent
                    if (!gameVersionSelected)
                    {
                        DetectAddonsButton.ToolTip = "Select WoW version first";
                    }
                    else if (!hasValidPath)
                    {
                        DetectAddonsButton.ToolTip = "Configure addon path first";
                    }
                    else
                    {
                        DetectAddonsButton.ToolTip = "Auto-detect addons";
                    }
                }

                // Uppdatera ikonen baserat på spelversion
                UpdateInstallationIcon();
            }
            catch (Exception ex)
            {
                // Logga fel men krascha inte
                Console.WriteLine($"Error updating control states: {ex.Message}");
            }
        }

        private void UpdateInstallationIcon()
        {
            if (!IsGameVersionSelected())
            {
                IconText.Text = "❓";
                return;
            }

            var selectedVersion = GameVersionComboBox.SelectedItem as GameVersion;
            if (selectedVersion != null)
            {
                switch (selectedVersion.Name)
                {
                    case var name when name.Contains("Classic"):
                        IconText.Text = "🏛️";
                        break;
                    case var name when name.Contains("Retail"):
                        IconText.Text = "🚀";
                        break;
                    default:
                        IconText.Text = "🎮";
                        break;
                }
            }
        }

        private bool IsGameVersionSelected()
        {
            return GameVersionComboBox.SelectedItem is GameVersion selectedVersion && selectedVersion.Id > 0;
        }

        private void SaveInstallation()
        {
            if (_isInitializing) return;

            try
            {
                _installation.Name = NameTextBox.Text?.Trim() ?? "";
                _installation.AddonPath = AddonPathTextBox.Text?.Trim() ?? "";
                _installation.IncludeElvUI = ElvUICheckBox.IsChecked ?? false;

                if (GameVersionComboBox.SelectedItem is GameVersion selectedVersion)
                {
                    _installation.GameVersionId = selectedVersion.Id;
                }

                if (ColorComboBox.SelectedItem is InstallationColorOption selectedColor)
                {
                    _installation.ColorHex = selectedColor.HexValue;
                }

                _addonUpdater.UpdateInstallation(_installation);

                // Bara trigga callbacks om installationen är KOMPLETT och redo för scanning
                if (!_isAutoDetecting && IsInstallationReadyForScanning())
                {
                    _onInstallationChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving installation: {ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Kontrollerar om installationen är redo för scanning
        private bool IsInstallationReadyForScanning()
        {
            return !string.IsNullOrEmpty(_installation.Name?.Trim()) &&
                   !string.IsNullOrEmpty(_installation.AddonPath?.Trim()) &&
                   Directory.Exists(_installation.AddonPath) &&
                   _installation.GameVersionId > 0;
        }

        // HJÄLPMETOD FÖR ATT SPARA UTAN CALLBACKS
        private void SaveInstallationSilently()
        {
            if (_isInitializing) return;

            try
            {
                _installation.Name = NameTextBox.Text?.Trim() ?? "";
                _installation.AddonPath = AddonPathTextBox.Text?.Trim() ?? "";
                _installation.IncludeElvUI = ElvUICheckBox.IsChecked ?? false;

                if (GameVersionComboBox.SelectedItem is GameVersion selectedVersion)
                {
                    _installation.GameVersionId = selectedVersion.Id;
                }

                if (ColorComboBox.SelectedItem is InstallationColorOption selectedColor)
                {
                    _installation.ColorHex = selectedColor.HexValue;
                }

                _addonUpdater.UpdateInstallation(_installation);
                // INGEN CALLBACK HÄR - SPARAR BARA CONFIG
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving installation: {ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Händelsehanterare
        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            _isExpanded = !_isExpanded;
            SyncExpandUIState(); // Använd ny metod för konsistens
        }

        private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveInstallation();
        }

        private void GameVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                UpdateControlStates();
                SaveInstallation();
            }
        }

        private void ColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                SaveInstallation();
            }
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsGameVersionSelected())
            {
                MessageBox.Show("Please select a WoW version first before choosing an addon path.",
                              "Select Game Version", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select AddOns Folder"
            };

            // Ange startkatalog om den finns
            string currentPath = AddonPathTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
            {
                dialog.InitialDirectory = currentPath;
            }

            if (dialog.ShowDialog() == true)
            {
                string oldPath = _installation.AddonPath;
                AddonPathTextBox.Text = dialog.FolderName;
                UpdatePathPreview();
                SaveInstallation();
                UpdateControlStates();

                // Auto-detect bara om installationen är HELT klar
                if (oldPath != _installation.AddonPath && IsInstallationReadyForScanning())
                {
                    await AutoDetectAddons();
                }
            }
        }

        private void AddonPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                UpdatePathPreview();

                // Använd en timer för att undvika att spara efter varje tangentnedtryckning
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(1000);
                timer.Tick += async (s, args) =>
                {
                    timer.Stop();
                    string oldPath = _installation.AddonPath;
                    SaveInstallation();
                    UpdateControlStates();

                    // Auto-detect bara om installationen är HELT klar
                    if (oldPath != _installation.AddonPath &&
                        IsInstallationReadyForScanning())
                    {
                        await AutoDetectAddons();
                    }
                };
                timer.Start();
            }
        }

        private async void ElvUICheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            bool newState = ElvUICheckBox.IsChecked ?? false;
            ElvUICheckBox.IsEnabled = false;

            try
            {
                if (!IsGameVersionSelected() || string.IsNullOrEmpty(_installation.AddonPath) ||
                    !Directory.Exists(_installation.AddonPath))
                {
                    ResetElvUICheckbox(false);
                    return;
                }

                if (newState) // Installera ElvUI
                {
                    if (MessageBox.Show($"Do you want to install ElvUI to {_installation.Name}?", "Install ElvUI",
                                      MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                            // Download and install ElvUI to this installation
                            var (version, lastUpdate) = await _elvUIService.DownloadAndExtractElvUI(_installation.AddonPath);

                            // Update folder mapping for this installation
                            _installation.FolderMapping["ElvUI"] = Utils.FindAddonFolders("ElvUI", _installation.AddonPath);

                            // Initialize ElvUI in this installation's addon config
                            if (!_installation.Addons.ContainsKey("ElvUI"))
                            {
                                _installation.Addons["ElvUI"] = new AddonConfig
                                {
                                    Id = 0,
                                    ModifiedDate = lastUpdate,
                                    LocalVersion = version
                                };
                            }

                            SaveInstallation();
                            _mainWindow?.RefreshUIAfterSettings();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Failed to install ElvUI: {ex.Message}", "Error",
                                          MessageBoxButton.OK, MessageBoxImage.Error);
                            ResetElvUICheckbox(false);
                        }
                        finally
                        {
                            Mouse.OverrideCursor = null;
                        }
                    }
                    else
                    {
                        ResetElvUICheckbox(false);
                    }
                }
                else // Avinstallera ElvUI
                {
                    if (MessageBox.Show($"Do you want to uninstall ElvUI from {_installation.Name}?", "Uninstall ElvUI",
                                      MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        try
                        {
                            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                            // Avinstallera ElvUI från denna installation
                            var elvuiFolders = _installation.FolderMapping.ContainsKey("ElvUI")
                                ? _installation.FolderMapping["ElvUI"]
                                : new System.Collections.Generic.List<string> { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" };

                            foreach (var folder in elvuiFolders)
                            {
                                string folderPath = Path.Combine(_installation.AddonPath, folder);
                                if (Directory.Exists(folderPath))
                                {
                                    Directory.Delete(folderPath, true);
                                }
                            }

                            // Ta bort från den här installationens config
                            if (_installation.FolderMapping.ContainsKey("ElvUI"))
                            {
                                _installation.FolderMapping.Remove("ElvUI");
                            }

                            if (_installation.Addons.ContainsKey("ElvUI"))
                            {
                                _installation.Addons.Remove("ElvUI");
                            }

                            SaveInstallation();
                            _mainWindow?.RefreshUIAfterSettings();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Failed to uninstall ElvUI: {ex.Message}", "Error",
                                          MessageBoxButton.OK, MessageBoxImage.Error);
                            ResetElvUICheckbox(true);
                        }
                        finally
                        {
                            Mouse.OverrideCursor = null;
                        }
                    }
                    else
                    {
                        ResetElvUICheckbox(true);
                    }
                }
            }
            finally
            {
                ElvUICheckBox.IsEnabled = true;
            }
        }

        private void ResetElvUICheckbox(bool newState)
        {
            ElvUICheckBox.Checked -= ElvUICheckBox_CheckedChanged;
            ElvUICheckBox.Unchecked -= ElvUICheckBox_CheckedChanged;
            ElvUICheckBox.IsChecked = newState;
            ElvUICheckBox.Checked += ElvUICheckBox_CheckedChanged;
            ElvUICheckBox.Unchecked += ElvUICheckBox_CheckedChanged;
        }

        private async void DetectAddonsButton_Click(object sender, RoutedEventArgs e)
        {
            // Extra validering för säkerhets skull
            if (!DetectAddonsButton.IsEnabled)
            {
                return; // Ska inte kunna hända, men säkerhetscheck
            }

            if (!IsGameVersionSelected())
            {
                MessageBox.Show("Please select a WoW version first.", "Select Game Version",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_installation.AddonPath) || !Directory.Exists(_installation.AddonPath))
            {
                MessageBox.Show("Please configure a valid addon path first.", "Invalid Path",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                DetectAddonsButton.IsEnabled = false;
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

                // Använd detekteringstjänsten för just denna installation
                var detectedAddons = await _addonDetectionService.DetectAddonsAsync(_installation.AddonPath, _installation.GameVersionId, includeElvUI: true);

                if (detectedAddons.Count == 0)
                {
                    MessageBox.Show("No addons were detected from the database.", "No Addons Detected",
                                  MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Hantera detektering av ElvUI
                ResetElvUICheckbox(true);

                // Initiera ElvUI i denna installations konfiguration om den upptäcks
                if (!_installation.Addons.ContainsKey("ElvUI"))
                {
                    _installation.Addons["ElvUI"] = new AddonConfig
                    {
                        Id = 0,
                        ModifiedDate = "",
                        LocalVersion = "Detected"
                    };
                }
                _installation.FolderMapping["ElvUI"] = new System.Collections.Generic.List<string> { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" };

                // Lägg till detekterade addons i denna installations konfiguration
                int addedCount = await _addonDetectionService.AddDetectedAddonsToConfigAsync(detectedAddons, _installation);

                // Ladda ner och installera alla detekterade addons för denna installation
                await _addonDetectionService.DownloadAndInstallAllDetectedAddonsAsync(detectedAddons, _installation, showProgress: true);

                SaveInstallation();

                // Gör en riktig scan efter manuell auto-detect också
                if (_mainWindow != null)
                {
                    await _mainWindow.ForceScanAfterAutoDetect();
                }

                MessageBox.Show($"Detection and synchronization complete for {_installation.Name}!\n\n" +
                              $"Detected addons: {detectedAddons.Count}\n" +
                              $"New addons added: {addedCount}",
                              "Detection Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error detecting addons: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Uppdatera control states efter operation
                UpdateControlStates();
                Mouse.OverrideCursor = null;
            }
        }

        // FIXAD AUTO-DETECT METOD - GARANTERAR KORREKT FLÖDE
        private async Task AutoDetectAddons()
        {
            try
            {
                // Kontrollera att installationen verkligen är redo
                if (!IsInstallationReadyForScanning())
                {
                    return; // Tyst exit om installationen inte är komplett
                }

                // SÄTT FLAGGA FÖR ATT FÖRHINDRA CALLBACKS UNDER PROCESSEN
                _isAutoDetecting = true;

                // Använd detekteringstjänsten för auto-detection
                var detectedAddons = await _addonDetectionService.DetectAddonsAsync(_installation.AddonPath, _installation.GameVersionId, includeElvUI: true);

                if (detectedAddons.Count == 0)
                {
                    return; // Tyst exit om inga addons funna
                }

                // Hantera ElvUI-detektering i tysthet
                ResetElvUICheckbox(true);

                if (!_installation.Addons.ContainsKey("ElvUI"))
                {
                    _installation.Addons["ElvUI"] = new AddonConfig
                    {
                        Id = 0,
                        ModifiedDate = "",
                        LocalVersion = "Detected"
                    };
                }
                _installation.FolderMapping["ElvUI"] = new System.Collections.Generic.List<string> { "ElvUI", "ElvUI_Libraries", "ElvUI_Options" };

                // Lägg till upptäckta addons i denna installations konfiguration
                await _addonDetectionService.AddDetectedAddonsToConfigAsync(detectedAddons, _installation);

                // SPARA TYST UTAN CALLBACKS UNDER NEDLADDNINGEN
                SaveInstallationSilently();

                // Ladda ner och installera alla upptäckta addons med förloppsvisning
                await _addonDetectionService.DownloadAndInstallAllDetectedAddonsAsync(detectedAddons, _installation, showProgress: true);

                // NU ÄR ALLT KLART - SPARA OCH GÖR EN RIKTIG SCAN
                _isAutoDetecting = false;
                SaveInstallation();

                // Efter auto-detect MÅSTE vi göra en riktig scan för korrekt "needs update" status
                if (_mainWindow != null)
                {
                    await _mainWindow.ForceScanAfterAutoDetect(); // Tvingar en riktig scan
                }
            }
            catch (Exception ex)
            {
                // Logga felet men visa inget meddelande
                Console.WriteLine($"Error auto-detecting addons for {_installation.Name}: {ex.Message}");
            }
            finally
            {
                // SÄKERSTÄLL ATT FLAGGAN ÅTERSTÄLLS ÄVEN VID FEL
                _isAutoDetecting = false;
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show($"Are you sure you want to remove the installation '{_installation.Name}'?\n\n" +
                              "This will not delete any files, only remove it from the application.",
                              "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _onRemoveRequested?.Invoke(this);
            }
        }
    }
}