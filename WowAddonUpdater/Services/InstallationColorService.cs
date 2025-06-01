using System.Collections.Generic;

namespace WowAddonUpdater.Services
{
    public class InstallationColorOption
    {
        public string Name { get; set; }
        public string HexValue { get; set; }
        public System.Windows.Media.Brush Brush { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public static class InstallationColorService
    {
        public static List<InstallationColorOption> GetColorOptions()
        {
            return new List<InstallationColorOption>
            {
                new InstallationColorOption
                {
                    Name = "Default (No Color)",
                    HexValue = "",
                    Brush = System.Windows.Media.Brushes.Transparent
                },
                new InstallationColorOption
                {
                    Name = "Light Blue",
                    HexValue = "#F0F4FF",
                    Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 244, 255))
                },
                new InstallationColorOption
                {
                    Name = "Light Green",
                    HexValue = "#F0FFF0",
                    Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 255, 240))
                },
                new InstallationColorOption
                {
                    Name = "Light Pink",
                    HexValue = "#FFF0F5",
                    Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 240, 245))
                },
                new InstallationColorOption
                {
                    Name = "Light Yellow",
                    HexValue = "#FFFEF0",
                    Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 254, 240))
                },
                new InstallationColorOption
                {
                    Name = "Light Purple",
                    HexValue = "#F8F0FF",
                    Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 240, 255))
                },
                new InstallationColorOption
                {
                    Name = "Light Peach",
                    HexValue = "#FFF8F0",
                    Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 248, 240))
                },
                new InstallationColorOption
                {
                    Name = "Light Mint",
                    HexValue = "#F0FFFF",
                    Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 255, 255))
                },
                new InstallationColorOption
                {
                    Name = "Light Coral",
                    HexValue = "#FFF5F5",
                    Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 245, 245))
                }
            };
        }

        public static InstallationColorOption GetColorOptionByHex(string hexValue)
        {
            var colors = GetColorOptions();
            return colors.Find(c => c.HexValue == hexValue) ?? colors[0]; // Returnera standardvärde om inget hittas
        }

        public static System.Windows.Media.Brush GetBrushByHex(string hexValue)
        {
            var colorOption = GetColorOptionByHex(hexValue);
            return colorOption.Brush;
        }
    }
}