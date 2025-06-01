using System.Collections.Generic;

namespace WowAddonUpdater.Services
{
    public class GameVersion
    {
        public string Name { get; set; }
        public int Id { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public class AutoScanInterval
    {
        public string Name { get; set; }
        public int Minutes { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public static class GameVersionProvider
    {
        public static List<GameVersion> GetGameVersions()
        {
            return new List<GameVersion>
            {
                new GameVersion { Name = "Classic Era", Id = 67408 },
                new GameVersion { Name = "Classic TBC", Id = 73246 },
                new GameVersion { Name = "Classic WOTLK", Id = 73713 },
                new GameVersion { Name = "Classic Cata", Id = 77522 },
                new GameVersion { Name = "Classic MOP", Id = 79434 },
                new GameVersion { Name = "Retail", Id = 517 }
            };
        }

        public static List<GameVersion> GetGameVersionsWithPlaceholder()
        {
            var versions = new List<GameVersion>();
            versions.Add(new GameVersion { Name = "Please select your WoW version...", Id = 0 });
            versions.AddRange(GetGameVersions());
            return versions;
        }

        public static List<AutoScanInterval> GetAutoScanIntervals()
        {
            return new List<AutoScanInterval>
            {
                new AutoScanInterval { Name = "1 hour", Minutes = 60 },
                new AutoScanInterval { Name = "2 hours", Minutes = 120 },
                new AutoScanInterval { Name = "4 hours", Minutes = 240 },
                new AutoScanInterval { Name = "6 hours", Minutes = 360 },
                new AutoScanInterval { Name = "12 hours", Minutes = 720 },
                new AutoScanInterval { Name = "24 hours", Minutes = 1440 }
            };
        }
    }
}