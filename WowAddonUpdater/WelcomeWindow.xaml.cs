using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace WowAddonUpdater
{
    public partial class WelcomeWindow : Window
    {
        public WelcomeWindow()
        {
            InitializeComponent();

            // Logga skapandet av välkomstfönstret
            LogMessage("WelcomeWindow created for new user");

            // Hantera fönsterstängningshändelse (kryssknappen)
            this.Closing += WelcomeWindow_Closing;
        }

        private void WelcomeWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                LogMessage("Welcome window closing - treating as OK button click");

                // Avbryt inte stängningen, låt den ske som vanligt
                // Använd istället en timer för att starta huvudapplikationen efter att fönstret har stängts
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(100);
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    StartMainApplicationAfterClose();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                LogError("Error in welcome window closing", ex);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            StartMainApplication();
        }

        private void StartMainApplication()
        {
            try
            {
                LogMessage("Welcome window starting main application");

                // Stäng detta välkomstfönster
                this.Closing -= WelcomeWindow_Closing; // Ta bort händelsehanteraren för att undvika rekursion
                this.Close();

                StartMainApplicationAfterClose();
            }
            catch (Exception ex)
            {
                LogError("Error starting main application", ex);
                MessageBox.Show($"Error starting application: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartMainApplicationAfterClose()
        {
            try
            {
                LogMessage("Starting main application after welcome window closed");

                // Skapa och visa huvudfönstret
                var mainWindow = new MainWindow();
                mainWindow.Show();

                // Använd dispatcher för att säkerställa att huvudfönstret visas helt innan inställningarna öppnas
                mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        LogMessage("Main window shown - now opening settings");

                        // Använd MainWindows AddonUpdater-instans genom att anropa dess inställningsmetod
                        var settingsWindow = new SettingsWindow(mainWindow.GetAddonUpdater(), async () =>
                        {
                            // Denna callback uppdaterar huvudfönstret
                            await mainWindow.RefreshAfterSettings();
                        });

                        settingsWindow.Owner = mainWindow;
                        settingsWindow.ShowDialog();

                        LogMessage("Welcome sequence completed - settings window closed");

                        // Inga popup-meddelanden – användaren förstår förmodligen vad som ska göras ändå
                        // Inställningsfönstret och huvudfönstret talar för sig själva
                    }
                    catch (Exception ex)
                    {
                        LogError("Error in welcome sequence completion", ex);
                        MessageBox.Show($"Error completing setup: {ex.Message}", "Setup Error",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                LogError("Error starting main application after close", ex);
                MessageBox.Show($"Error starting application: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
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

                File.AppendAllText(logFile, $"{DateTime.Now}: WelcomeWindow: {message}\r\n");
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
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WowAddonUpdater");

                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(logFolder, "error.log");

                File.AppendAllText(logFile,
                    $"{DateTime.Now}: WelcomeWindow: {context}: {ex?.Message}\r\n{ex?.StackTrace}\r\n\r\n");
            }
            catch
            {
                // Om loggning misslyckas är det illa
            }
        }
    }
}