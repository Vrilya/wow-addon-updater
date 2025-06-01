using System;
using System.Globalization;
using System.Windows.Data;
using WowAddonUpdater.Services;

namespace WowAddonUpdater.Converters
{
    public class InstallationColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string installationId && !string.IsNullOrEmpty(installationId))
            {
                // Hämta instansen för AddonUpdater för att hitta installationen
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    var addonUpdater = mainWindow.GetAddonUpdater();
                    var installations = addonUpdater.GetInstallations();
                    var installation = installations.Find(i => i.Id == installationId);

                    if (installation != null && !string.IsNullOrEmpty(installation.ColorHex))
                    {
                        return InstallationColorService.GetBrushByHex(installation.ColorHex);
                    }
                }
            }

            return System.Windows.Media.Brushes.Transparent; // Ingen färg som default
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}