using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTAMissions.Core;

namespace GTAMissions.Missions
{
    /// <summary>
    /// Escort-interdiction mission: an FIB VIP convoy departs a gas station and heads
    /// for a military base. Stop them before they arrive - by killing the VIP, or by
    /// running out the clock. Getting too close or shooting at the convoy puts them on
    /// full alert: the VIP keeps running for the checkpoint while its police escort
    /// breaks off to hunt the player. Partway along the route, passing the tunnel
    /// entrance stages a police roadblock ahead of the convoy.
    /// </summary>
    public class VipEscortMission : Mission
    {
        private enum Stage
        {
            WaitingForPlayerToEnterVehicle,
            WaitingToDepart,
            Cruising,
            Evasive
        }

        // --- Scouted locations ---
        private static readonly Vector3 PlayerVehiclePosition = new Vector3(-2521.68f, 2322.58f, 32.47f);
        private const float PlayerVehicleHeading = 80.01f;

        private static readonly Vector3 VipPosition = new Vector3(-2557.68f, 2337.35f, 32.69f);
        private const float VipHeading = 94.81f;

        private static readonly Vector3 Bodyguard1Position = new Vector3(-2583.67f, 2326.49f, 32.57f);
        private const float Bodyguard1Heading = 103.90f;

        private static readonly Vector3 Bodyguard2Position = new Vector3(-2566.34f, 2320.67f, 32.69f);
        private const float Bodyguard2Heading = 92.87f;

        // VIP escape checkpoint. The final guard vehicle stays back at LOC8, but the
        // mission only fails once the VIP makes it farther up the road toward the base.
        private static readonly Vector3 ConvoyDestination = new Vector3(-2361.32f, 3433.90f, 27.26f);

        private static readonly Vector3 TunnelEntrancePosition = new Vector3(-2602.45f, 3007.29f, 16.04f);

        private static readonly Vector3 Roadblock1Position = new Vector3(-2549.69f, 3377.99f, 12.97f);
        private const float Roadblock1Heading = 17.11f;

        private static readonly Vector3 Roadblock2Position = new Vector3(-2576.13f, 3392.30f, 12.88f);
        private const float Roadblock2Heading = 297.54f;

        private static readonly Vector3 Roadblock3Position = new Vector3(-2562.77f, 3389.57f, 12.90f);
        private const float Roadblock3Heading = 219.43f;

        private static readonly Vector3 Roadblock1ConvergedPosition = new Vector3(-2558.50f, 3386.50f, 12.90f);
        private static readonly Vector3 Roadblock2ConvergedPosition = new Vector3(-2567.00f, 3392.60f, 12.90f);

        private static readonly Vector3 FinalGuardPosition = new Vector3(-2459.73f, 3670.61f, 13.43f);
        private const float FinalGuardHeading = 137.10f;

        // --- Tuning constants (easy to adjust later) ---
        private const float AlertRadius = 10f;              // per spec: 10m proximity puts the convoy on alert
        private const float DestinationRadius = 15f;         // how close the VIP needs to get to LOC1 to "arrive"
        private const float RoadblockTriggerRadius = 35f;    // tunnel entrance trigger - spawns roadblock early
        private const float RoadblockConvergeTriggerY = 3405f; // once VIP has passed LOC7, outer cars close the road
        private const int TimeLimitMs = 3 * 60 * 1000;       // 3 minutes from departure
        private const float NormalConvoySpeed = 25f;         // ~90 km/h - relatively fast, not chased
        private const float EvasiveConvoySpeed = 38f;        // ~135 km/h - once alerted
        private const float PolicePursuitSpeed = 42f;

        private static readonly Random Rng = new Random();

        private Stage _stage;

        private Vehicle _playerVehicle;
        private Blip _playerVehicleBlip;

        private readonly VehicleGroup _vipGroup = new VehicleGroup();
        private Ped _vipPed;

        private readonly VehicleGroup _bodyguard1Group = new VehicleGroup();
        private readonly VehicleGroup _bodyguard2Group = new VehicleGroup();

        private readonly VehicleGroup _roadblock1Group = new VehicleGroup();
        private readonly VehicleGroup _roadblock2Group = new VehicleGroup();
        private readonly VehicleGroup _roadblock3Group = new VehicleGroup();
        private readonly VehicleGroup _finalGuardGroup = new VehicleGroup();

        private Blip _vipBlip;

        private int _countdownStartGameTime;
        private int _countdownDurationMs;
        private int _chaseStartGameTime;
        private bool _hasTriggeredRoadblock;
        private bool _hasConvergedRoadblock;

        private IEnumerable<VehicleGroup> ConvoyGroups
        {
            get
            {
                yield return _vipGroup;
                yield return _bodyguard1Group;
                yield return _bodyguard2Group;
            }
        }

        private IEnumerable<VehicleGroup> AllGroups
        {
            get
            {
                yield return _vipGroup;
                yield return _bodyguard1Group;
                yield return _bodyguard2Group;
                yield return _roadblock1Group;
                yield return _roadblock2Group;
                yield return _roadblock3Group;
                yield return _finalGuardGroup;
            }
        }

        protected override void OnStart()
        {
            _stage = Stage.WaitingForPlayerToEnterVehicle;

            // No wanted level for the whole mission - real dispatched police would
            // otherwise wander into the convoy's path and make the AI swerve/avoid them.
            Game.Player.Wanted.DispatchesCopsForPlayer = false;
            ClearWantedLevel();

            if (!SpawnPlayerVehicle())
            {
                return;
            }

            SpawnConvoy();

            ShowObjective("Get in your vehicle.");
        }

        protected override void OnUpdate()
        {
            ClearWantedLevel();

            switch (_stage)
            {
                case Stage.WaitingForPlayerToEnterVehicle:
                    UpdateWaitingForPlayer();
                    break;

                case Stage.WaitingToDepart:
                    UpdateCountdown();
                    break;

                case Stage.Cruising:
                case Stage.Evasive:
                    UpdateConvoy();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        // VIPERFORAI - noted directly from its Rampage Trainer XML save rather than
        // looked up at runtime, since this vehicle never changes.
        private const int PlayerVehicleModelHash = -660007725;
        private const VehicleColor PlayerVehiclePrimaryColor = VehicleColor.MetallicRed;
        private const VehicleColor PlayerVehicleSecondaryColor = VehicleColor.MetallicRed;
        private const VehicleColor PlayerVehiclePearlescentColor = VehicleColor.MetallicBlue;
        private const VehicleColor PlayerVehicleRimColor = VehicleColor.MetallicBlack;
        private const string PlayerVehiclePlate = "UZIG718";

        private bool SpawnPlayerVehicle()
        {
            Model model = new Model(PlayerVehicleModelHash);
            bool loaded = model.Request(3000);

            if (!loaded)
            {
                Fail($"VIPERFORAI model (hash {PlayerVehicleModelHash}) did not load in time.");
                return false;
            }

            _playerVehicle = Vehicle.Create(model, PlayerVehiclePosition, PlayerVehicleHeading);
            if (_playerVehicle == null)
            {
                Fail("Vehicle.Create returned null for VIPERFORAI.");
                return false;
            }

            _playerVehicle.Mods.PrimaryColor = PlayerVehiclePrimaryColor;
            _playerVehicle.Mods.SecondaryColor = PlayerVehicleSecondaryColor;
            _playerVehicle.Mods.PearlescentColor = PlayerVehiclePearlescentColor;
            _playerVehicle.Mods.RimColor = PlayerVehicleRimColor;
            _playerVehicle.Mods.LicensePlate = PlayerVehiclePlate;
            _playerVehicle.Mods.LicensePlateStyle = LicensePlateStyle.YellowOnBlack;

            _playerVehicleBlip = _playerVehicle.AddBlip();
            _playerVehicleBlip.Sprite = BlipSprite.PersonalVehicleCar;
            _playerVehicleBlip.Color = BlipColor.Blue;
            _playerVehicleBlip.ShowRoute = true;

            return true;
        }

        private void SpawnConvoy()
        {
            Vehicle vipVehicle = SpawnVehicle(VehicleHash.FBI, VipPosition, VipHeading);
            if (vipVehicle != null)
            {
                vipVehicle.Mods.PrimaryColor = VehicleColor.MetallicBlack;
                vipVehicle.Mods.SecondaryColor = VehicleColor.MetallicBlack;

                _vipGroup.Vehicle = vipVehicle;
                _vipGroup.Driver = SpawnPedInSeat(PedHash.FibSec01SMM, vipVehicle, VehicleSeat.Driver);
                _vipGroup.Add(_vipGroup.Driver);
                _vipGroup.Add(SpawnPedInSeat(PedHash.FibSec01SMM, vipVehicle, VehicleSeat.Passenger));

                _vipPed = SpawnPedInSeat(PedHash.Business01AMM, vipVehicle, VehicleSeat.LeftRear);
                _vipGroup.Add(_vipPed);
            }

            SpawnPoliceEscort(_bodyguard1Group, Bodyguard1Position, Bodyguard1Heading);
            SpawnPoliceEscort(_bodyguard2Group, Bodyguard2Position, Bodyguard2Heading);
        }

        // ------------------------------------------------------------------
        // Stage: waiting for player / countdown
        // ------------------------------------------------------------------

        private void UpdateWaitingForPlayer()
        {
            Ped player = Game.Player.Character;

            if (_playerVehicle != null && _playerVehicle.Exists() && player.IsInVehicle(_playerVehicle))
            {
                _playerVehicleBlip?.Delete();
                _playerVehicleBlip = null;

                _countdownStartGameTime = Game.GameTime;
                _countdownDurationMs = Rng.Next(10000, 15001);
                _stage = Stage.WaitingToDepart;

                ShowObjective("The convoy is about to move out...");
            }
        }

        private void UpdateCountdown()
        {
            if (Game.GameTime - _countdownStartGameTime >= _countdownDurationMs)
            {
                DepartConvoy();

                _chaseStartGameTime = Game.GameTime;
                _stage = Stage.Cruising;

                ShowObjective("Stop the convoy before it reaches the military base!");
            }
        }

        private void DepartConvoy()
        {
            AssignCruiseTask(_vipGroup, NormalConvoySpeed, VehicleDrivingFlags.DrivingModeStopForVehicles);
            AssignCruiseTask(_bodyguard1Group, NormalConvoySpeed, VehicleDrivingFlags.DrivingModeStopForVehicles);
            AssignCruiseTask(_bodyguard2Group, NormalConvoySpeed, VehicleDrivingFlags.DrivingModeStopForVehicles);

            if (_vipGroup.VehicleExists())
            {
                _vipBlip = _vipGroup.Vehicle.AddBlip();
                _vipBlip.Sprite = BlipSprite.Enemy;
                _vipBlip.Color = BlipColor.Yellow;
                _vipBlip.ShowRoute = true;
            }
        }

        private static void AssignCruiseTask(VehicleGroup group, float speed, VehicleDrivingFlags flags)
        {
            if (!group.VehicleExists() || group.Driver == null || !group.Driver.Exists())
            {
                return;
            }

            group.Driver.Task.DriveTo(group.Vehicle, ConvoyDestination, speed, flags, DestinationRadius);
        }

        // ------------------------------------------------------------------
        // Stage: cruising / evasive
        // ------------------------------------------------------------------

        private void UpdateConvoy()
        {
            if (IsVipDown())
            {
                Succeed("VIP eliminated.");
                return;
            }

            Ped player = Game.Player.Character;

            if (player.IsDead || player.IsCuffed)
            {
                Fail(player.IsCuffed ? "Busted." : "You went down.");
                return;
            }

            if (HasVipReachedDestination())
            {
                Fail("The convoy reached the base.");
                return;
            }

            if (Game.GameTime - _chaseStartGameTime > TimeLimitMs)
            {
                Succeed("Time's up - the convoy never made it.");
                return;
            }

            if (_stage == Stage.Cruising && HasBeenAlerted(player))
            {
                GoEvasive();
            }

            if (_stage == Stage.Evasive)
            {
                CheckRoadblockTrigger();
                CheckRoadblockConvergence();
            }
        }

        private bool IsVipDown()
        {
            bool pedDown = _vipPed == null || !_vipPed.Exists() || _vipPed.IsDead;
            bool vehicleDestroyed = _vipGroup.VehicleExists() && _vipGroup.Vehicle.IsDead;
            return pedDown || vehicleDestroyed;
        }

        private bool HasVipReachedDestination()
        {
            return _vipGroup.VehicleExists() && _vipGroup.Vehicle.Position.DistanceTo(ConvoyDestination) < DestinationRadius;
        }

        private bool HasBeenAlerted(Ped player)
        {
            foreach (VehicleGroup group in ConvoyGroups)
            {
                if (!group.VehicleExists())
                {
                    continue;
                }

                if (player.Position.DistanceTo(group.Vehicle.Position) < AlertRadius)
                {
                    return true;
                }

                if (group.Vehicle.HasBeenDamagedBy(player))
                {
                    return true;
                }

                foreach (Ped ped in group.Peds)
                {
                    if (ped != null && ped.Exists() && ped.HasBeenDamagedBy(player))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void GoEvasive()
        {
            _stage = Stage.Evasive;

            // Fast and reckless (ignores lights/rules), but still avoids other vehicles
            // instead of ramming through them.
            AssignCruiseTask(_vipGroup, EvasiveConvoySpeed, VehicleDrivingFlags.DrivingModeAvoidVehiclesReckless);

            AssignPolicePursuitTask(_bodyguard1Group, Game.Player.Character);
            AssignPolicePursuitTask(_bodyguard2Group, Game.Player.Character);

            if (_vipBlip != null)
            {
                _vipBlip.Color = BlipColor.Red;
            }

            ShowObjective("The convoy is onto you! Don't let them reach the base!");
        }

        private void AssignPolicePursuitTask(VehicleGroup policeGroup, Ped player)
        {
            if (!policeGroup.VehicleExists())
            {
                return;
            }

            policeGroup.EnableSiren();

            if (policeGroup.Driver != null && policeGroup.Driver.Exists())
            {
                policeGroup.Driver.Task.VehicleChase(player);
            }

            policeGroup.SetPassengerCombat(player);
        }

        // ------------------------------------------------------------------
        // Roadblock
        // ------------------------------------------------------------------

        private void CheckRoadblockTrigger()
        {
            if (_hasTriggeredRoadblock || !_vipGroup.VehicleExists())
            {
                return;
            }

            if (_vipGroup.Vehicle.Position.DistanceTo(TunnelEntrancePosition) < RoadblockTriggerRadius)
            {
                _hasTriggeredRoadblock = true;
                SpawnRoadblock();
            }
        }

        private void CheckRoadblockConvergence()
        {
            if (!_hasTriggeredRoadblock || _hasConvergedRoadblock || !_vipGroup.VehicleExists())
            {
                return;
            }

            if (_vipGroup.Vehicle.Position.Y > RoadblockConvergeTriggerY)
            {
                _hasConvergedRoadblock = true;
                ConvergeRoadblockCars();
            }
        }

        private void SpawnRoadblock()
        {
            Ped player = Game.Player.Character;

            SpawnRoadblockCar(_roadblock1Group, Roadblock1Position, Roadblock1Heading);
            SpawnRoadblockCar(_roadblock2Group, Roadblock2Position, Roadblock2Heading);

            SpawnStationaryRiotVan(_roadblock3Group, Roadblock3Position, Roadblock3Heading);

            Vehicle finalTruck = SpawnVehicle(VehicleHash.Riot, FinalGuardPosition, FinalGuardHeading);
            _finalGuardGroup.Vehicle = finalTruck;
            if (finalTruck != null)
            {
                finalTruck.IsPositionFrozen = true;
            }

            Vector3[] officerOffsets =
            {
                new Vector3(2f, 0f, 0f),
                new Vector3(-2f, 0f, 0f),
                new Vector3(0f, 2f, 0f),
                new Vector3(0f, -2f, 0f),
                new Vector3(1.5f, 1.5f, 0f),
            };

            foreach (Vector3 offset in officerOffsets)
            {
                Ped officer = SpawnPedOnFoot(PedHash.Cop01SMY, FinalGuardPosition + offset, FinalGuardHeading);
                _finalGuardGroup.Add(officer);
            }

            // The roadblock stages early, but the vehicles stay in roadblock duty instead
            // of chasing. Officers engage on foot/from passenger seats.
            _roadblock1Group.SetPassengerCombat(player);
            _roadblock2Group.SetPassengerCombat(player);
            _roadblock3Group.SetPassengerCombat(player);
            _finalGuardGroup.SetAllCombat(player);

            GTA.UI.Screen.ShowSubtitle("~r~Roadblock staged ahead!");
        }

        private void ConvergeRoadblockCars()
        {
            SendRoadblockCarTo(_roadblock1Group, Roadblock1ConvergedPosition);
            SendRoadblockCarTo(_roadblock2Group, Roadblock2ConvergedPosition);

            Ped player = Game.Player.Character;
            _roadblock1Group.SetPassengerCombat(player);
            _roadblock2Group.SetPassengerCombat(player);

            GTA.UI.Screen.ShowSubtitle("~r~Roadblock closing!");
        }

        private static void SendRoadblockCarTo(VehicleGroup group, Vector3 position)
        {
            if (!group.VehicleExists() || group.Driver == null || !group.Driver.Exists())
            {
                return;
            }

            group.Driver.Task.DriveTo(
                group.Vehicle,
                position,
                22f,
                VehicleDrivingFlags.DrivingModeAvoidVehiclesReckless,
                2f);
        }

        private void SpawnRoadblockCar(VehicleGroup group, Vector3 position, float heading)
        {
            SpawnPoliceCar(group, position, heading);
            group.EnableSiren();
        }

        private void SpawnStationaryRiotVan(VehicleGroup group, Vector3 position, float heading)
        {
            Vehicle vehicle = SpawnVehicle(VehicleHash.Riot, position, heading);
            if (vehicle == null)
            {
                return;
            }

            vehicle.IsPositionFrozen = true;
            group.Vehicle = vehicle;

            group.Driver = SpawnPedInSeat(PedHash.Cop01SMY, vehicle, VehicleSeat.Driver);
            group.Add(group.Driver);
            group.Add(SpawnPedInSeat(PedHash.Cop01SMY, vehicle, VehicleSeat.Passenger));
        }

        // ------------------------------------------------------------------
        // Spawning helpers
        // ------------------------------------------------------------------

        private void SpawnPoliceEscort(VehicleGroup group, Vector3 position, float heading)
        {
            SpawnPoliceCar(group, position, heading);
        }

        private void SpawnPoliceCar(VehicleGroup group, Vector3 position, float heading)
        {
            Vehicle vehicle = SpawnVehicle(VehicleHash.Police, position, heading);
            if (vehicle == null)
            {
                return;
            }

            group.Vehicle = vehicle;
            group.Driver = SpawnPedInSeat(PedHash.Cop01SMY, vehicle, VehicleSeat.Driver);
            group.Add(group.Driver);
            group.Add(SpawnPedInSeat(PedHash.Cop01SMY, vehicle, VehicleSeat.Passenger));
        }

        private static Vehicle SpawnVehicle(VehicleHash hash, Vector3 position, float heading)
        {
            Model model = new Model(hash);
            model.Request(1000);

            if (!model.IsLoaded)
            {
                return null;
            }

            return Vehicle.Create(model, position, heading);
        }

        private static Ped SpawnPedInSeat(PedHash pedHash, Vehicle vehicle, VehicleSeat seat)
        {
            if (vehicle == null || !vehicle.Exists())
            {
                return null;
            }

            Ped ped = SpawnPedOnFoot(pedHash, vehicle.Position, vehicle.Heading);
            if (ped == null)
            {
                return null;
            }

            ped.SetIntoVehicle(vehicle, seat);
            return ped;
        }

        private static Ped SpawnPedOnFoot(PedHash pedHash, Vector3 position, float heading)
        {
            Model model = new Model(pedHash);
            model.Request(1000);

            if (!model.IsLoaded)
            {
                return null;
            }

            Ped ped = Ped.Create(model, position, heading);
            if (ped == null)
            {
                return null;
            }

            ped.BlockPermanentEvents = true;
            ped.KeepTaskWhenMarkedAsNoLongerNeeded = true;

            return ped;
        }

        // ------------------------------------------------------------------
        // Cleanup
        // ------------------------------------------------------------------

        public override void SoftCleanup()
        {
            foreach (VehicleGroup group in AllGroups)
            {
                group.ClearCombat();
            }

            Game.Player.Wanted.DispatchesCopsForPlayer = true;
            ClearWantedLevel();
        }

        public override void HardCleanup()
        {
            _playerVehicleBlip?.Delete();
            _vipBlip?.Delete();

            foreach (VehicleGroup group in AllGroups)
            {
                group.Delete();
            }

            if (_playerVehicle != null && _playerVehicle.Exists() && !Game.Player.Character.IsInVehicle(_playerVehicle))
            {
                _playerVehicle.Delete();
            }

            Game.Player.Wanted.DispatchesCopsForPlayer = true;
            ClearWantedLevel();
        }

        private static void ClearWantedLevel()
        {
            Game.Player.Wanted.SetWantedLevel(0, false);
            Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
        }

        /// <summary>
        /// A spawned vehicle plus everyone riding in/around it, so spawn/cleanup/combat
        /// logic for each convoy or roadblock unit doesn't have to be repeated by hand.
        /// </summary>
        private class VehicleGroup
        {
            public Vehicle Vehicle;
            public Ped Driver;
            public readonly List<Ped> Peds = new List<Ped>();

            public bool VehicleExists() => Vehicle != null && Vehicle.Exists();

            public void Add(Ped ped)
            {
                if (ped != null)
                {
                    Peds.Add(ped);
                }
            }

            public void ClearCombat()
            {
                foreach (Ped ped in Peds)
                {
                    if (ped != null && ped.Exists())
                    {
                        ped.Task.ClearAllImmediately();
                    }
                }
            }

            public void SetAllCombat(Ped target)
            {
                foreach (Ped ped in Peds)
                {
                    if (ped != null && ped.Exists())
                    {
                        ped.Task.Combat(target, TaskCombatFlags.None, TaskThreatResponseFlags.None);
                    }
                }
            }

            public void SetPassengerCombat(Ped target)
            {
                foreach (Ped ped in Peds)
                {
                    if (ped != null && ped.Exists() && ped != Driver)
                    {
                        ped.Task.Combat(target, TaskCombatFlags.None, TaskThreatResponseFlags.None);
                    }
                }
            }

            public void EnableSiren()
            {
                if (VehicleExists())
                {
                    Vehicle.IsSirenActive = true;
                }
            }

            public void Delete()
            {
                foreach (Ped ped in Peds)
                {
                    if (ped != null && ped.Exists())
                    {
                        ped.Delete();
                    }
                }

                if (VehicleExists())
                {
                    Vehicle.Delete();
                }
            }
        }
    }
}
