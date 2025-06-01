namespace WowAddonUpdater.Models
{
    public enum AddonSortMode
    {
        Name = 0,
        Installation = 1,
        LastUpdated = 2
    }

    public class AddonSortOption
    {
        public string Name { get; set; }
        public AddonSortMode Mode { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public static class AddonSortProvider
    {
        public static List<AddonSortOption> GetSortOptions()
        {
            return new List<AddonSortOption>
            {
                new AddonSortOption { Name = "Addon Name", Mode = AddonSortMode.Name },
                new AddonSortOption { Name = "Installation", Mode = AddonSortMode.Installation },
                new AddonSortOption { Name = "Last Updated", Mode = AddonSortMode.LastUpdated }
            };
        }
    }
}