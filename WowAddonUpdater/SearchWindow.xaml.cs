using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using WowAddonUpdater.Models;
using WowAddonUpdater.Services;
using WPFMessageBox = System.Windows.MessageBox;

namespace WowAddonUpdater
{
    public partial class SearchWindow : Window, IDisposable
    {
        private readonly AddonUpdater _addonUpdater;
        private readonly HttpClientService _httpClientService;
        private readonly CurseForgeService _curseForgeService;
        private JObject _selectedAddon;
        private readonly Action _refreshMainWindow;
        private bool _disposed = false;
        private Installation _selectedInstallation;

        public SearchWindow(AddonUpdater addonUpdater, Action refreshMainWindow)
        {
            InitializeComponent();

            _addonUpdater = addonUpdater;
            _httpClientService = new HttpClientService();
            _curseForgeService = new CurseForgeService(_httpClientService.Client);
            _refreshMainWindow = refreshMainWindow;

            // Initiera rullgardinsmenyn för installationer
            InitializeInstallationDropdown();

            // Sätt fokus på sökrutan
            Loaded += (s, e) => SearchTextBox.Focus();

            // Hantera fönsterstängning för att säkerställa korrekt frigöring
            Closing += SearchWindow_Closing;
        }

        private void InitializeInstallationDropdown()
        {
            try
            {
                var installations = _addonUpdater.GetInstallations()
                    .Where(i => !string.IsNullOrEmpty(i.AddonPath) &&
                               System.IO.Directory.Exists(i.AddonPath) &&
                               i.GameVersionId > 0)
                    .ToList();

                if (installations.Count == 0)
                {
                    // Inga giltiga installationer
                    InstallationComboBox.Items.Add("No valid installations configured");
                    InstallationComboBox.SelectedIndex = 0;
                    InstallationComboBox.IsEnabled = false;
                    SearchButton.IsEnabled = false;
                    SearchTextBox.IsEnabled = false;
                    return;
                }

                // Lägg till installationer i rullgardinsmenyn
                InstallationComboBox.ItemsSource = installations;
                InstallationComboBox.DisplayMemberPath = "Name";
                InstallationComboBox.SelectedValuePath = "Id";

                // Välj första installationen som standard
                InstallationComboBox.SelectedIndex = 0;
                _selectedInstallation = installations[0];

                // Aktivera kontroller
                SearchButton.IsEnabled = true;
                SearchTextBox.IsEnabled = true;
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"Error initializing installations: {ex.Message}",
                               "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InstallationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InstallationComboBox.SelectedItem is Installation selectedInstallation)
            {
                _selectedInstallation = selectedInstallation;

                // Rensa tidigare sökresultat vid byte av installation
                ResultsDataGrid.ItemsSource = null;
                _selectedAddon = null;
                InstallButton.IsEnabled = false;

                // Aktivera/inaktivera sökning baserat på giltigt val
                SearchButton.IsEnabled = _selectedInstallation != null;
                SearchTextBox.IsEnabled = _selectedInstallation != null;
            }
        }

        private void SearchWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Säkerställ att resurser frigörs när fönstret stängs
            Dispose();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await SearchAddons();
        }

        private async void SearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await SearchAddons();
            }
        }

        private async Task SearchAddons()
        {
            string query = SearchTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(query))
            {
                WPFMessageBox.Show("Please enter a search term", "Search Error",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedInstallation == null)
            {
                WPFMessageBox.Show("Please select an installation first", "Search Error",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<JObject> results = null;
            List<object> displayItems = null;

            try
            {
                // Minnesoptimering före sökning
                GC.Collect(0, GCCollectionMode.Optimized);

                // Visa laddningsindikator
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                SearchButton.IsEnabled = false;

                // Sök med den valda installationens spelversion
                results = await _curseForgeService.SearchAddons(query, _selectedInstallation.GameVersionId);

                // Skapa listobjekt
                displayItems = new List<object>();

                if (results != null)
                {
                    foreach (var addon in results)
                    {
                        string name = addon["name"]?.ToString() ?? "Unknown Name";
                        string summary = addon["summary"]?.ToString() ?? "No description available";

                        displayItems.Add(new
                        {
                            Name = name,
                            Summary = summary,
                            OriginalData = addon
                        });

                        // Minnesoptimering under loop för stora resultat
                        if (displayItems.Count % 20 == 0)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                }

                if (displayItems.Count == 0)
                {
                    WPFMessageBox.Show($"No addons found matching your search criteria for {_selectedInstallation.Name}.",
                                 "No Results", MessageBoxButton.OK);
                }

                ResultsDataGrid.ItemsSource = displayItems;
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"Error searching for addons: {ex.Message}",
                               "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // MINNESRENSNING: Uttrycklig nollställning av stora objekt
                results?.Clear();
                results = null;

                // Mer aggressiv minnesrensning
                GC.Collect(1, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(0, GCCollectionMode.Optimized);

                // Dölj laddningsindikator
                Mouse.OverrideCursor = null;
                SearchButton.IsEnabled = true;
            }
        }

        private void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsDataGrid.SelectedItem != null)
            {
                var selectedItem = ResultsDataGrid.SelectedItem;
                _selectedAddon = (JObject)selectedItem.GetType().GetProperty("OriginalData")?.GetValue(selectedItem);
                InstallButton.IsEnabled = _selectedAddon != null && _selectedInstallation != null;
            }
            else
            {
                _selectedAddon = null;
                InstallButton.IsEnabled = false;
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAddon == null)
            {
                WPFMessageBox.Show("Please select an addon first", "Installation Error",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedInstallation == null)
            {
                WPFMessageBox.Show("Please select an installation first", "Installation Error",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            JObject addonDetails = null;
            JToken files = null;
            JObject compatibleFile = null;

            try
            {
                // Minnesoptimering före installation
                GC.Collect(0, GCCollectionMode.Optimized);

                // Visa laddningsindikator
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                InstallButton.IsEnabled = false;

                string addonName = _selectedAddon["name"]?.ToString() ?? "Unknown Addon";

                if (_selectedAddon["id"]?.Value<int?>() is int addonId)
                {
                    // Hämta addon-filer för att hitta den senaste som är kompatibel med vald installation
                    addonDetails = await _curseForgeService.GetAddonDetails(addonId);
                    files = addonDetails["data"];

                    foreach (var file in files)
                    {
                        var gameVersionTypeIds = file["gameVersionTypeIds"];
                        if (gameVersionTypeIds != null)
                        {
                            foreach (var id in gameVersionTypeIds)
                            {
                                if (id.Value<int>() == _selectedInstallation.GameVersionId)
                                {
                                    compatibleFile = (JObject)file;
                                    break;
                                }
                            }
                        }

                        if (compatibleFile != null) break;
                    }

                    if (compatibleFile == null)
                    {
                        WPFMessageBox.Show($"No compatible version found for {_selectedInstallation.Name}",
                                       "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    if (compatibleFile["id"]?.Value<int?>() is int fileId)
                    {
                        // Installera till vald installation
                        bool success = await _addonUpdater.InstallAddon(addonName, addonId, fileId, _selectedInstallation.Id);

                        if (success)
                        {
                            WPFMessageBox.Show($"{addonName} has been installed successfully to {_selectedInstallation.Name}",
                                           "Installation Complete", MessageBoxButton.OK);

                            // Uppdatera huvudfönstret
                            _refreshMainWindow?.Invoke();

                            // Behåll sökresultaten så att användaren kan installera flera addons
                        }
                        else
                        {
                            WPFMessageBox.Show($"Failed to install {addonName} to {_selectedInstallation.Name}",
                                           "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        WPFMessageBox.Show($"Invalid file information for {addonName}",
                                       "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    WPFMessageBox.Show($"Invalid addon information for {addonName}",
                                   "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                WPFMessageBox.Show($"Error installing addon: {ex.Message}",
                               "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // MINNESRENSNING: Uttrycklig nollställning av stora objekt
                addonDetails = null;
                files = null;
                compatibleFile = null;

                // Mer aggressiv minnesrensning efter installation
                GC.Collect(1, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(0, GCCollectionMode.Optimized);

                // Dölj laddningsindikator
                Mouse.OverrideCursor = null;
                InstallButton.IsEnabled = _selectedInstallation != null && _selectedAddon != null;
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
                // Rensa sökresultaten före dispose
                try
                {
                    ResultsDataGrid.ItemsSource = null;
                    _selectedAddon = null;
                    _selectedInstallation = null;
                }
                catch { /* Ignorera cleanup-fel */ }

                _curseForgeService?.Dispose();
                _httpClientService?.Dispose();

                // MINNESRENSNING: Slutgiltig nollställning och garbage collection
                _selectedAddon = null;
                _selectedInstallation = null;

                // Mer aggressiv slutlig rensning
                GC.Collect(1, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect(0, GCCollectionMode.Optimized);
            }
            _disposed = true;
        }
    }
}