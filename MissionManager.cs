using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GTA;
using GTAMissions.Core;
using GTAMissions.Missions;

namespace GTAMissions
{
    /// <summary>
    /// The single ScriptHookVDotNet entry point for this mod. It owns the currently
    /// active <see cref="Mission"/>, ticks it every frame, and lets you launch
    /// missions and testing utilities with keybinds.
    /// </summary>
    public class MissionManager : Script
    {
        // Waypoints get logged in the game's "scripts" folder.
        private static readonly string WaypointLogPath = Path.Combine(GameDirectory.Scripts, "GTAMissions_Waypoints.txt");

        private Mission _activeMission;

        // Kept around after a mission ends so F8 (hard cleanup) still works afterward,
        // not just while the mission is actively running.
        private Mission _lastMission;

        // Shared by the U and I test-spawn keys, so cycling never leaves more than one
        // spawned test vehicle in the world at a time.
        private Vehicle _spawnedTestVehicle;
        private int _savedVehicleCycleIndex = -1;

        // How far around the player F8 sweeps for wrecked/blown-up vehicles left over
        // from earlier mission runs (e.g. if you start/end the mission several times).
        private const float WreckSweepRadius = 1000f;

        public MissionManager()
        {
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
        }

        private void OnTick(object sender, EventArgs e)
        {
            _activeMission?.Tick();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // F6: start the mission, for testing. Swap this out for a menu, a trigger
            // blip, an in-world marker, etc. once you have more than one mission.
            if (e.KeyCode == Keys.F6 && _activeMission == null)
            {
                StartMission(new VipEscortMission());
            }
            // F7: abort whatever mission is currently running (soft cleanup - peds stop
            // fighting, wanted level clears, but spawned vehicles/peds stay in the world).
            else if (e.KeyCode == Keys.F7 && _activeMission != null)
            {
                _activeMission.Cancel();
                GTA.UI.Screen.ShowSubtitle("~o~Mission reset. Press F6 to restart.");
            }
            // F8: hard cleanup - fully removes every vehicle/ped the last mission spawned,
            // plus sweeps for any wrecked/blown-up vehicles near the player (leftovers
            // from earlier mission runs that no longer have anything tracking them).
            // Works any time after a mission has started, even after it's already ended.
            else if (e.KeyCode == Keys.F8 && _lastMission != null)
            {
                _lastMission.HardCleanup();
                int wrecksRemoved = RemoveWreckedVehiclesNearPlayer(WreckSweepRadius);
                GTA.UI.Screen.ShowSubtitle($"~o~Mission vehicles/peds removed ({wrecksRemoved} wreck(s) swept).");
            }
            // F3: print the player's current coordinates/heading, for scouting mission
            // locations. Also appended to GTAMissions_Waypoints.txt in the scripts folder.
            else if (e.KeyCode == Keys.F3)
            {
                ShowCurrentCoordinates();
            }
            // F5: mission-specific debug helper (e.g. cycling AI driving styles to test).
            else if (e.KeyCode == Keys.F5 && _activeMission != null)
            {
                _activeMission.DebugCycle();
            }
            // U: spawn the VIPERFORAI saved vehicle in front of the player, for testing.
            else if (e.KeyCode == Keys.U)
            {
                SpawnSavedVehicleByName("VIPERFORAI");
            }
            // I: cycle through every vehicle saved via Rampage's XML Loader, one at a time.
            else if (e.KeyCode == Keys.I)
            {
                CycleSavedVehicle();
            }
        }

        private void ShowCurrentCoordinates()
        {
            Ped player = Game.Player.Character;
            var pos = player.Position;
            float heading = player.Heading;

            string line = string.Format(
                CultureInfo.InvariantCulture,
                "new Vector3({0:0.00}f, {1:0.00}f, {2:0.00}f), Heading: {3:0.00}f",
                pos.X, pos.Y, pos.Z, heading);

            GTA.UI.Screen.ShowSubtitle("~y~" + line, 8000);

            try
            {
                File.AppendAllText(WaypointLogPath, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Ignore - the on-screen subtitle is still shown even if logging to disk fails.
            }
        }

        private void SpawnSavedVehicleByName(string name)
        {
            SavedVehicle saved = SavedVehicleLibrary.All
                .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

            if (saved == null)
            {
                GTA.UI.Screen.ShowSubtitle("~r~Could not find saved vehicle: " + name);
                return;
            }

            SpawnAndReplaceTestVehicle(saved);
        }

        private void CycleSavedVehicle()
        {
            var all = SavedVehicleLibrary.All;
            if (all.Count == 0)
            {
                GTA.UI.Screen.ShowSubtitle("~r~No saved vehicles found.");
                return;
            }

            _savedVehicleCycleIndex = (_savedVehicleCycleIndex + 1) % all.Count;
            SpawnAndReplaceTestVehicle(all[_savedVehicleCycleIndex]);
        }

        private void SpawnAndReplaceTestVehicle(SavedVehicle saved)
        {
            if (_spawnedTestVehicle != null && _spawnedTestVehicle.Exists())
            {
                _spawnedTestVehicle.Delete();
            }

            _spawnedTestVehicle = saved.SpawnInFrontOf(Game.Player.Character);

            GTA.UI.Screen.ShowSubtitle(_spawnedTestVehicle != null
                ? "~b~Spawned: " + saved.Name
                : "~r~Failed to spawn: " + saved.Name);
        }

        /// <summary>
        /// Deletes any wrecked/blown-up vehicle within range of the player, regardless
        /// of which mission (or run) spawned it. Covers cases where the same mission has
        /// been started/ended multiple times and earlier wrecks are no longer tracked.
        /// </summary>
        private static int RemoveWreckedVehiclesNearPlayer(float radius)
        {
            Vehicle[] nearbyVehicles = World.GetNearbyVehicles(Game.Player.Character.Position, radius, new Model[0]);
            int removed = 0;

            foreach (Vehicle vehicle in nearbyVehicles)
            {
                if (vehicle == null || !vehicle.Exists())
                {
                    continue;
                }

                if (vehicle.IsDead || vehicle.IsConsideredDestroyed)
                {
                    vehicle.Delete();
                    removed++;
                }
            }

            return removed;
        }

        private void StartMission(Mission mission)
        {
            _activeMission = mission;
            _lastMission = mission;
            _activeMission.MissionEnded += OnMissionEnded;
            _activeMission.Start();
        }

        private void OnMissionEnded(object sender, MissionOutcome outcome)
        {
            if (_activeMission != null)
            {
                _activeMission.MissionEnded -= OnMissionEnded;
            }

            _activeMission = null;
        }

        private void OnAborted(object sender, EventArgs e)
        {
            _activeMission?.Cancel();
            _activeMission = null;
        }
    }
}
