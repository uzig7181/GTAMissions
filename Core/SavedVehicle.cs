using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using GTA;
using GTA.Math;

namespace GTAMissions.Core
{
    /// <summary>
    /// A vehicle definition parsed from a Rampage Trainer "Saved Vehicles" XML file,
    /// so we can spawn the exact same look (model/colors/plate) from our own code
    /// without needing Rampage running at all.
    /// </summary>
    public class SavedVehicle
    {
        public string Name { get; }
        public int ModelHash { get; }
        public int PrimaryColor { get; }
        public int SecondaryColor { get; }
        public int PearlescentColor { get; }
        public int RimColor { get; }
        public string Plate { get; }

        private SavedVehicle(string name, int modelHash, int primaryColor, int secondaryColor, int pearlescentColor, int rimColor, string plate)
        {
            Name = name;
            ModelHash = modelHash;
            PrimaryColor = primaryColor;
            SecondaryColor = secondaryColor;
            PearlescentColor = pearlescentColor;
            RimColor = rimColor;
            Plate = plate;
        }

        public static SavedVehicle Load(string xmlPath)
        {
            XDocument doc = XDocument.Load(xmlPath);
            XElement root = doc.Root;

            string hashText = (string)root?.Element("ModelHash") ?? "0x0";
            int modelHash = ParseHash(hashText);

            XElement properties = root?.Element("VehicleProperties");
            XElement colours = properties?.Element("Colours");

            int primary = (int?)colours?.Element("Primary") ?? 0;
            int secondary = (int?)colours?.Element("Secondary") ?? 0;
            int pearl = (int?)colours?.Element("Pearl") ?? 0;
            int rim = (int?)colours?.Element("Rim") ?? 0;
            string plate = (string)properties?.Element("NumberPlateText") ?? string.Empty;

            string name = Path.GetFileNameWithoutExtension(xmlPath);

            return new SavedVehicle(name, modelHash, primary, secondary, pearl, rim, plate);
        }

        private static int ParseHash(string hashText)
        {
            string trimmed = hashText.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(2);
            }

            uint unsignedValue = uint.Parse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return unchecked((int)unsignedValue);
        }

        /// <summary>
        /// Spawns this vehicle a short distance in front of the given ped, facing the
        /// same direction, with the saved colors/plate applied.
        /// </summary>
        public Vehicle SpawnInFrontOf(Ped ped, float distanceAhead = 6f)
        {
            Model model = new Model(ModelHash);
            model.Request(1000);

            if (!model.IsLoaded)
            {
                return null;
            }

            Vector3 spawnPosition = ped.Position + ped.ForwardVector * distanceAhead;
            Vehicle vehicle = Vehicle.Create(model, spawnPosition, ped.Heading);

            if (vehicle != null)
            {
                vehicle.Mods.PrimaryColor = (VehicleColor)PrimaryColor;
                vehicle.Mods.SecondaryColor = (VehicleColor)SecondaryColor;
                vehicle.Mods.PearlescentColor = (VehicleColor)PearlescentColor;
                vehicle.Mods.RimColor = (VehicleColor)RimColor;

                if (!string.IsNullOrEmpty(Plate))
                {
                    vehicle.Mods.LicensePlate = Plate;
                }
            }

            return vehicle;
        }
    }
}
