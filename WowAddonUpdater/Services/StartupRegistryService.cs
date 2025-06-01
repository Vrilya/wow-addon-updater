using System;
using System.IO;
using Microsoft.Win32;

namespace WowAddonUpdater.Services
{
    public class StartupRegistryService
    {
        private const string STARTUP_KEY_PATH = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "WowAddonUpdater";

        public void SetStartupRegistry(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(STARTUP_KEY_PATH, true))
                {
                    if (enable)
                    {
                        string appPath = GetApplicationPath();

                        if (string.IsNullOrEmpty(appPath))
                        {
                            throw new Exception("Could not determine application path for startup registry");
                        }

                        // Enkel registry entry utan cmd - programmet fixar working directory själv
                        string startupValue = $"\"{appPath}\" --minimized";
                        key?.SetValue(APP_NAME, startupValue);

                        LogMessage($"Added to Windows startup: {startupValue}");
                    }
                    else
                    {
                        // Ta bort från registry
                        key?.DeleteValue(APP_NAME, false);
                        LogMessage("Removed from Windows startup");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Error setting startup registry", ex);
                throw; // Kasta om undantaget så att anropare kan hantera UI-feedback
            }
        }

        private string GetApplicationPath()
        {
            string appPath = null;

            // Första försöket: Environment.ProcessPath (bäst för .NET 5+)
            try
            {
                appPath = Environment.ProcessPath;
            }
            catch { }

            // Andra försöket: Process.GetCurrentProcess()
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

                    // Om det är en .dll eller tom, försök med process path igen
                    if (string.IsNullOrEmpty(appPath) || appPath.EndsWith(".dll"))
                    {
                        appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    }
                }
                catch { }
            }

            // Fjärde försöket: Application.ExecutablePath
            if (string.IsNullOrEmpty(appPath))
            {
                try
                {
                    appPath = System.Windows.Forms.Application.ExecutablePath;
                }
                catch { }
            }

            return appPath;
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

                File.AppendAllText(logFile, $"{DateTime.Now}: StartupRegistryService: {message}\r\n");
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
                    $"{DateTime.Now}: StartupRegistryService: {context}: {ex?.Message}\r\n{ex?.StackTrace}\r\n\r\n");
            }
            catch
            {
                // Om loggning misslyckas är det illa
            }
        }
    }
}