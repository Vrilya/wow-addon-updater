using System;
using System.IO;
using System.Windows;

namespace WowAddonUpdater.Services
{
    public class PathValidationService
    {
        public bool ValidatePath(string path, bool showMessages = true)
        {
            if (string.IsNullOrEmpty(path?.Trim()))
            {
                if (showMessages)
                {
                    System.Windows.MessageBox.Show("You must specify a path to the AddOns folder.", "Invalid Path",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    if (showMessages)
                    {
                        System.Windows.MessageBox.Show($"Failed to create the directory: {ex.Message}", "Error",
                                      MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    return false;
                }
            }

            return true;
        }

        public bool ValidatePathSilently(string path)
        {
            return ValidatePath(path, showMessages: false);
        }

        public bool ValidateSavedPath(string savedPath, bool showMessages = true)
        {
            if (string.IsNullOrEmpty(savedPath))
            {
                if (showMessages)
                {
                    System.Windows.MessageBox.Show("You must save a valid addon path before using this feature.",
                                  "Save Path First", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            if (!Directory.Exists(savedPath))
            {
                if (showMessages)
                {
                    System.Windows.MessageBox.Show("The saved addon path does not exist. Please select a valid path and save it first.",
                                  "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            return true;
        }
    }
}