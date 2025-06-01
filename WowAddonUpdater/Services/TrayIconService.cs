using System;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace WowAddonUpdater.Services
{
    public class TrayIconService : ITrayIconService
    {
        private Forms.NotifyIcon _notifyIcon;
        private bool _disposed = false;

        public event EventHandler ShowMainWindowRequested;
        public event EventHandler ExitApplicationRequested;

        public TrayIconService()
        {
            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            try
            {
                LogMessage("Creating tray icon");

                _notifyIcon = new Forms.NotifyIcon();
                _notifyIcon.Text = "WoW Addon Updater";

                // Läs in ikon från inbäddad resurs
                try
                {
                    // Hämta ikonen från programmets resurser
                    Uri iconUri = new Uri("pack://application:,,,/Assets/app_icon.ico", UriKind.Absolute);

                    var streamResourceInfo = System.Windows.Application.GetResourceStream(iconUri);
                    if (streamResourceInfo != null)
                    {
                        using (var iconStream = streamResourceInfo.Stream)
                        {
                            _notifyIcon.Icon = new Drawing.Icon(iconStream);
                        }
                        LogMessage("Custom icon loaded successfully for tray");
                    }
                    else
                    {
                        LogMessage("Icon resource not found for tray, using system icon");
                        _notifyIcon.Icon = Drawing.SystemIcons.Application;
                    }
                }
                catch (Exception ex)
                {
                    LogError("Error loading tray icon", ex);
                    _notifyIcon.Icon = Drawing.SystemIcons.Application;
                }

                // Skapa snabbmenyn
                CreateContextMenu();

                // Hantera dubbelklick
                _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

                LogMessage("Tray icon created successfully");
            }
            catch (Exception ex)
            {
                LogError("Failed to create tray icon", ex);
                throw;
            }
        }

        private void CreateContextMenu()
        {
            var contextMenu = new Forms.ContextMenuStrip();

            var openItem = new Forms.ToolStripMenuItem("Open");
            openItem.Click += (s, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new Forms.ToolStripSeparator());

            var exitItem = new Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitApplicationRequested?.Invoke(this, EventArgs.Empty);
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetVisible(bool visible)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = visible;
                LogMessage($"Tray icon visibility set to: {visible}");
            }
        }

        public void ShowBalloonTip(string title, string text, int timeout = 3000)
        {
            try
            {
                if (_notifyIcon != null && _notifyIcon.Visible)
                {
                    _notifyIcon.BalloonTipTitle = title;
                    _notifyIcon.BalloonTipText = text;
                    _notifyIcon.ShowBalloonTip(timeout);
                    LogMessage($"Balloon tip shown: {title} - {text}");
                }
            }
            catch (Exception ex)
            {
                LogError("Error showing balloon tip", ex);
            }
        }

        public void SetTooltipText(string text)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = text;
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
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                LogMessage("Tray icon service disposed");
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

                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: TrayIconService: {message}\r\n");
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
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: TrayIconService: {context}: {ex?.Message}\r\n{ex?.StackTrace}\r\n\r\n");
            }
            catch
            {
                // Om loggning misslyckas är det illa
            }
        }
    }
}