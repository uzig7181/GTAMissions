using System.Collections.Generic;
using System.IO;

namespace GTAMissions.Core
{
    /// <summary>
    /// Loads and caches every vehicle saved via Rampage Trainer's "Saved Vehicles"
    /// XML Loader, so scripts can spawn any of them by name without depending on
    /// Rampage being installed/running.
    /// </summary>
    public static class SavedVehicleLibrary
    {
        private static List<SavedVehicle> _cache;

        public static IReadOnlyList<SavedVehicle> All => _cache ?? (_cache = LoadAll());

        private static List<SavedVehicle> LoadAll()
        {
            var result = new List<SavedVehicle>();
            string folder = GetFolderPath();

            if (!Directory.Exists(folder))
            {
                return result;
            }

            foreach (string file in Directory.GetFiles(folder, "*.xml"))
            {
                try
                {
                    result.Add(SavedVehicle.Load(file));
                }
                catch
                {
                    // Skip files that don't parse as expected so one bad save doesn't
                    // break cycling through the rest.
                }
            }

            return result;
        }

        private static string GetFolderPath()
        {
            return GameDirectory.Combine("RampageFiles", "Saved Vehicles");
        }
    }
}
