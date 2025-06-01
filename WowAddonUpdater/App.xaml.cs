using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;

namespace WowAddonUpdater
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            // Lägg till undantagshanterare för att fånga uppstartsfel
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Logga programstart
            LogMessage("Application starting");
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                LogMessage("Application OnStartup called");

                // VIKTIG: Detta sätter working directory till programmets mapp
                // Detta fixar problemet när programmet startas från Windows startup
                SetCorrectWorkingDirectory();

                // Kontrollera command line arguments
                bool startMinimized = e.Args.Contains("--minimized");

                LogMessage($"Command line args: {string.Join(" ", e.Args)}");
                LogMessage($"Start minimized from command line: {startMinimized}");

                // Kontrollera om detta är en ny användare genom att se om config.json finns och innehåller giltiga installationer
                var addonUpdater = new AddonUpdater();
                bool isNewUser = !HasValidInstallations(addonUpdater);

                LogMessage($"New user check: {isNewUser}");

                // Kontrollera om användaren har valt att starta minimerat i inställningarna
                bool startMinimizedFromSettings = addonUpdater.Config.Settings.StartMinimized;
                LogMessage($"Start minimized from settings: {startMinimizedFromSettings}");

                // Kombinera command line och settings
                bool shouldStartMinimized = startMinimized || startMinimizedFromSettings;
                LogMessage($"Should start minimized: {shouldStartMinimized}");

                if (isNewUser)
                {
                    LogMessage("Starting with welcome window for new user");

                    // Nya användare startar alltid med welcome window, aldrig minimerat
                    var welcomeWindow = new WelcomeWindow();
                    welcomeWindow.Show();
                }
                else if (shouldStartMinimized)
                {
                    LogMessage("Starting minimized to tray");

                    // Skapa main window men visa den inte
                    var mainWindow = new MainWindow();

                    // Starta direkt minimerat till tray - detta hanteras i MainWindow konstruktorn
                    mainWindow.WindowState = WindowState.Minimized;
                    mainWindow.Show();

                    // Dölj omedelbart och visa i tray
                    mainWindow.Hide();

                    // Visa tray notification om det finns uppdateringar
                    mainWindow.Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            // Gör en snabb scan efter uppdateringar om inställningarna tillåter det
                            var installations = addonUpdater.GetInstallations();
                            if (installations.Any(i => !string.IsNullOrEmpty(i.AddonPath) &&
                                                      Directory.Exists(i.AddonPath) &&
                                                      i.GameVersionId > 0))
                            {
                                LogMessage("Performing background scan for updates on startup");
                                // Detta hanteras av MainWindow's auto-scan logik
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError("Error during background startup scan", ex);
                        }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
                else
                {
                    LogMessage("Starting with main window for existing user");

                    // Visa huvudfönstret som vanligt för befintliga användare
                    var mainWindow = new MainWindow();
                    mainWindow.Show();
                }

                LogMessage("Application startup completed");
            }
            catch (Exception ex)
            {
                LogError("Error in OnStartup", ex);

                // Fallback... visa huvudfönstret
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }

            // Anropa inte base.OnStartup eftersom vi hanterar fönsterskapandet manuellt
        }

        private bool HasValidInstallations(AddonUpdater addonUpdater)
        {
            try
            {
                var installations = addonUpdater.GetInstallations();
                bool hasValid = installations.Any(i =>
                    !string.IsNullOrEmpty(i.AddonPath) &&
                    Directory.Exists(i.AddonPath) &&
                    i.GameVersionId > 0);

                LogMessage($"HasValidInstallations check: {installations.Count} installations found, {(hasValid ? "at least one valid" : "none valid")}");
                return hasValid;
            }
            catch (Exception ex)
            {
                LogError("Error checking for valid installations", ex);
                return false;
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogError("DispatcherUnhandledException", e.Exception);

            // Visa en meddelanderuta med felet
            System.Windows.MessageBox.Show($"An error occurred: {e.Exception.Message}\n\nDetails have been logged to: %LocalAppData%\\WowAddonUpdater\\error.log",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogError("UnhandledException", e.ExceptionObject as Exception);
        }

        private void LogError(string type, Exception ex)
        {
            try
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WowAddonUpdater");

                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(logFolder, "error.log");

                File.AppendAllText(logFile,
                    $"{DateTime.Now}: {type}: {ex?.Message}\r\n{ex?.StackTrace}\r\n\r\n");
            }
            catch
            {
                // Om loggning misslyckas är det illa
            }
        }

        private void SetCorrectWorkingDirectory()
        {
            try
            {
                // Hämta programmets verkliga sökväg
                string appPath = null;

                // Första försöket: Environment.ProcessPath
                try
                {
                    appPath = Environment.ProcessPath;
                }
                catch { }

                // Andra försöket: Process path
                if (string.IsNullOrEmpty(appPath))
                {
                    try
                    {
                        appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    }
                    catch { }
                }

                // Tredje försöket: Assembly location
                if (string.IsNullOrEmpty(appPath))
                {
                    try
                    {
                        appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(appPath))
                {
                    string appDirectory = Path.GetDirectoryName(appPath);
                    if (!string.IsNullOrEmpty(appDirectory) && Directory.Exists(appDirectory))
                    {
                        string currentDir = Directory.GetCurrentDirectory();

                        if (!string.Equals(currentDir, appDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            LogMessage($"Changing working directory from '{currentDir}' to '{appDirectory}'");
                            Directory.SetCurrentDirectory(appDirectory);
                            LogMessage("Working directory changed successfully");
                        }
                        else
                        {
                            LogMessage($"Working directory already correct: '{currentDir}'");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Error setting working directory", ex);
            }
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

                File.AppendAllText(logFile, $"{DateTime.Now}: {message}\r\n");
            }
            catch
            {
                // Om loggning misslyckas är det illa... fortsätt ändå
            }
        }
    }
}