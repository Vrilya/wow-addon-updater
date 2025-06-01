using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace WowAddonUpdater
{
    public static class Utils
    {
        public static List<string> FindAddonFolders(string addonName, string wowPath)
        {
            List<string> folders = new List<string>();

            try
            {
                foreach (string folder in Directory.GetDirectories(wowPath))
                {
                    string folderName = Path.GetFileName(folder);
                    string[] tocFiles = Directory.GetFiles(folder, "*.toc");

                    foreach (string tocFile in tocFiles)
                    {
                        try
                        {
                            string content = File.ReadAllText(tocFile);

                            // Sök efter addon-namnet i TOC-titeln eller anteckningar
                            if (Regex.IsMatch(content, $@"## Title:.*{Regex.Escape(addonName)}.*", RegexOptions.IgnoreCase) ||
                                Regex.IsMatch(content, $@"## Notes:.*{Regex.Escape(addonName)}.*", RegexOptions.IgnoreCase))
                            {
                                folders.Add(folderName);
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading TOC file {tocFile}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing WoW path: {ex.Message}");
            }

            return folders;
        }
    }
}