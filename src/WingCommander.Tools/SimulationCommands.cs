using System.Globalization;
using System.Text;
using WingCommander.Core.Numerics;
using WingCommander.Core.Resources;
using WingCommander.Simulation;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;

namespace WingCommander.Tools;

internal static partial class Commands
{
    static partial void RegisterSimulationCommands()
    {
        Register("missions", "[MODULE.00x] [--series N]  list missions: header, nav points, ships, objectives", Missions);
        Register("shiptypes", "print the compiled-in object type table (ship/weapon stats)", ShipTypes);
        Register("flight", "<series> <mission> [frames] [seed] [--campaign 0..2] [--aim]  fly a mission headless (scripted player) and print it", Flight);
    }

    private static int Missions(ToolOptions o)
    {
        string file = o.PositionalOrNull(0) ?? "MODULE.000";
        int? onlySeries = o.Option("series") is { } seriesText
            ? int.Parse(seriesText, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : null;

        var module = MissionModule.Parse(OpenPacket(o, file));
        int missionCount = 0;
        for (int series = 0; series < MissionModule.SeriesSlots; series++)
        {
            if (onlySeries is { } wanted && wanted != series)
                continue;
            bool headerPrinted = false;
            for (int mission = 0; mission < MissionModule.MissionsPerSeries; mission++)
            {
                var data = module.GetMission(series, mission);
                if (data is null)
                    continue;
                if (!headerPrinted)
                {
                    Console.WriteLine($"=== series {series} \"{module.GetSeriesName(series)}\"");
                    headerPrinted = true;
                }
                PrintMission(data);
                missionCount++;
            }
        }
        Console.WriteLine($"{module.Name}: {missionCount} missions");
        return 0;
    }

    private static void PrintMission(MissionData data)
    {
        var h = data.Header;
        var team = h.InitialMissionShips.Where(s => s != -1).ToArray();
        Console.WriteLine();
        Console.WriteLine($"--- series {data.Series} mission {data.Mission} (index {data.MissionIndex}) \"{data.MissionName}\"");
        Console.WriteLine($"    entry nav {h.EntryNavPoint}, home ship {h.HomeMissionShip}, player ship {h.PlayerMissionShip}, " +
                          $"team [{string.Join(", ", team)}], field_16 {h.Field16}");

        Console.WriteLine("    nav points:");
        for (int i = 0; i < data.NavPoints.Count; i++)
        {
            var nav = data.NavPoints[i];
            if (nav.Type == 0 && nav.Name.Length == 0)
                continue;
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"      {i,2} {Quote(nav.Name),-22} type {nav.Type,2} at {Units(nav.Position)} radius {(ushort)nav.ProximityRadius}");
            var preload = new List<string>();
            for (int p = 0; p < 2; p++)
            {
                if (nav.PreloadObjectTypes[p] != ObjectType.None)
                    preload.Add(TypeName(nav.PreloadObjectTypes[p]));
            }
            if (preload.Count != 0)
                sb.Append(CultureInfo.InvariantCulture, $" preload {string.Join(",", preload)}");
            var ships = new List<short>();
            for (int s = 0; s < 10; s++)
            {
                if (nav.MissionShips[s] != -1)
                    ships.Add(nav.MissionShips[s]);
            }
            if (ships.Count != 0)
                sb.Append(CultureInfo.InvariantCulture, $" ships {string.Join(",", ships)}");
            var triggers = new List<string>();
            for (int t = 0; t < 8; t += 2)
            {
                if (nav.Triggers[t] != -1)
                    triggers.Add($"nav{nav.Triggers[t + 1]}:={nav.Triggers[t]}");
            }
            if (triggers.Count != 0)
                sb.Append(CultureInfo.InvariantCulture, $" triggers {string.Join(" ", triggers)}");
            Console.WriteLine(sb.ToString());
        }

        Console.WriteLine("    ships:");
        for (int i = 0; i < data.Ships.Count; i++)
        {
            var s = data.Ships[i];
            if (IsEmptyShip(s))
                continue;
            string role = s.Type is ObjectType.AsteroidField or ObjectType.MineField
                ? $"hazard radius {s.Speed + 3000} density {s.Pilot}"
                : $"{s.MissionType,-14} pilot {PilotName(s.Pilot),-8} speed {s.Speed,3}";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"      {i,2} {TypeName(s.Type),-13} {s.Side,-8} {role} nav {s.NavPoint,2} at {Units(s.Position)} " +
                $"rot {s.Pitch}/{s.Yaw}/{s.Roll} leader {s.LeaderMissionIndex} form {s.FormationIndex}/{s.FormationSpot} " +
                $"target {s.TargetMissionIndex} rating {s.Rating} state {s.State}"));
        }

        Console.WriteLine("    objectives:");
        for (int i = 0; i < data.ObjectiveSources.Count && data.ObjectiveSources[i].Type != -1; i++)
        {
            var obj = data.ObjectiveSources[i];
            string kind = obj.Type switch
            {
                0 => "nav point",
                1 => "home base",
                2 => "escort",
                3 => "reach",
                4 => "destroy",
                _ => $"type {obj.Type}",
            };
            string target = obj.Type == 0
                ? (obj.Index >= 0 && obj.Index < data.NavPoints.Count ? Quote(data.NavPoints[obj.Index].Name) : "?")
                : (obj.Index >= 0 && obj.Index < data.Ships.Count ? $"ship {obj.Index} {TypeName(data.Ships[obj.Index].Type)}" : "?");
            Console.WriteLine($"      {i,2} {kind,-9} {obj.Index,2} {target,-24} {Quote(obj.Description)}");
        }
    }

    /// <summary>Unused records have type -1.</summary>
    private static bool IsEmptyShip(in MissionShipRecord s) => s.Type == ObjectType.None;

    private static int ShipTypes(ToolOptions o)
    {
        _ = o;
        Console.WriteLine(" #  name           class        cRad radar scale delay life/fuel  dmg  expl  Vmax cruise accel  p/y/r    inert  shields  armor F/R/L/R      weapons");
        for (int i = 0; i < ObjectTypeTable.Count; i++)
        {
            var t = ObjectTypeTable.All[i];
            string life = t.ObjectClass >= ObjectClass.Ship ? t.Fuel.ToString(CultureInfo.InvariantCulture) : t.Lifetime.ToString(CultureInfo.InvariantCulture);
            string name = t.DisplayName.Length != 0 ? t.DisplayName : ((ObjectType)i).ToString();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{i,2}  {name,-14} {t.ObjectClass,-12} {t.CollisionRadius,4} {t.RadarRadius,5} {t.Scale,5} {t.AnimationDelay,5} {life,9} " +
                $"{t.DamageCapacity,4} {t.ExplosionDamage,5} {t.MaximumVelocity,5} {t.CruiseVelocity,6} {t.Acceleration,5}  " +
                $"{t.PitchRate,2}/{t.YawRate,2}/{t.RollRate,2} {t.AfterburnerVelocity,6}  {t.ShieldFore,3}/{t.ShieldAft,-3}  " +
                $"{t.ArmorFront,3}/{t.ArmorRear,3}/{t.ArmorLeft,3}/{t.ArmorRight,3}  {Loadout(t.WeaponLoadout)}"));
        }
        return 0;
    }

    private static string Loadout(WeaponLoadout loadout)
    {
        var parts = new List<string>();
        for (int i = 0; i < loadout.Count; i++)
        {
            string disabled = loadout.GetDisabled(i) != 0 ? "(off)" : "";
            parts.Add($"{WeaponShortName(loadout.GetWeaponType(i))}@{loadout.GetHardpoint(i)}{disabled}");
        }
        return string.Join(" ", parts);
    }

    private static string WeaponShortName(ObjectType type) => type switch
    {
        ObjectType.LaserCannon => "laser",
        ObjectType.NeutronParticleGun => "neutron",
        ObjectType.MassDriverCannon => "MD",
        ObjectType.Turret => "turret",
        ObjectType.DumbFireMissile => "DF",
        ObjectType.HeatSeekingMissile => "HS",
        ObjectType.FriendOrFoeMissile => "FF",
        ObjectType.ImageRecognitionMissile => "IR",
        ObjectType.Torpedo => "torpedo",
        ObjectType.SpaceMine => "mine",
        _ => type.ToString(),
    };

    private static string TypeName(ObjectType type) =>
        type >= 0 && (int)type < ObjectTypeTable.Count && ObjectTypeTable.Get(type).DisplayName.Length != 0
            ? ObjectTypeTable.Get(type).DisplayName
            : type.ToString();

    private static string PilotName(int pilot) => pilot switch
    {
        >= 0 and <= 4 => $"AI{pilot}",
        >= 5 and <= 12 => ((Rating)pilot).ToString()["Ace".Length..],
        13 => "player",
        >= 14 and <= 17 => AceNames[pilot - 14],
        _ => pilot.ToString(CultureInfo.InvariantCulture),
    };

    private static readonly string[] AceNames = ["Bhurak", "Dakhath", "Khajja", "Bakhtosh"];

    private static string Units(FixedVector v) =>
        string.Create(CultureInfo.InvariantCulture, $"({v.X / 256.0:0.##}, {v.Y / 256.0:0.##}, {v.Z / 256.0:0.##})");

    private static string Quote(string text) => $"\"{text}\"";

    private static int Flight(ToolOptions o)
    {
        short series = short.Parse(o.Positional(0), CultureInfo.InvariantCulture);
        short mission = short.Parse(o.Positional(1), CultureInfo.InvariantCulture);
        int frames = o.PositionalOrNull(2) is { } framesText ? int.Parse(framesText, CultureInfo.InvariantCulture) : 1200;
        uint seed = o.PositionalOrNull(3) is { } seedText ? uint.Parse(seedText, CultureInfo.InvariantCulture) : 1;
        bool aim = o.Has("aim");
        var events = new CountingSimulationEvents();
        var sim = new SpaceSimulation(new CRandom(seed), new GameDirectoryResources(RequireGameDirectory(o)), events)
        {
            CampaignDataSet = (short)o.IntOption("campaign", 0),
            TrainSimActive = series == 0,
        };
        if (!sim.InitMission(series, mission))
            throw new GameDataException($"no mission {series}/{mission} in campaign {sim.CampaignDataSet}");
        sim.ForceView(0, 0);
        sim.FrameSkipCounter = 1;
        sim.SetUpActionSphere(sim.MissionShips[sim.PlayerMissionShipIndex].NavPoint);
        sim.ArcadeState = 0;
        Console.WriteLine($"series {series} mission {mission} \"{sim.MissionModule.GetMissionName(series, mission)}\", seed {seed}, " +
                          $"player {TypeName(sim.Objects[0].Type)}, nav {sim.CurrentNavPoint}, {(aim ? "aiming" : "idle")} pilot");
        PrintShips(sim);

        int frame = 0;
        for (; frame < frames && sim.ArcadeState == 0; frame++)
        {
            short pitch = 0, yaw = 0;
            bool fire = false;
            if (aim)
                AimAtNearestEnemy(sim, out pitch, out yaw, out fire);
            sim.PlayersFlightDynamics(pitch, yaw, 0);
            if (fire)
                sim.FirePlayersLasers();
            if (aim && sim.TargetLockCountdown == 0 && sim.SelectedReleaseWeaponIndex != -1)
                sim.PlayerReleaseWeapon();
            sim.Update3Space();
            if (sim.ArcadeState != 0)
                break;
            sim.PrepareSpaceView();
            sim.UpdateCockpitSimulation();
            sim.CheckStranded();
            if (frame % 100 == 99)
                PrintStatus(sim, frame + 1);
        }
        Console.WriteLine();
        Console.WriteLine($"ended after {frame} frames, arcade state {sim.ArcadeState}, kills {sim.PlayerKillCount} " +
                          $"(wingman {sim.WingmanKillCount}), mission score {sim.Campaign.MissionScore}, medal score {sim.MissionMedalScore}, " +
                          $"state hash {sim.ComputeStateHash():x16}");
        PrintShips(sim);
        Console.WriteLine("events: " + string.Join(", ", events.Counts.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} {p.Value}")));
        return 0;
    }

    /// <summary>A test pilot: turns toward the nearest enemy, closes in, fires when lined up.</summary>
    private static void AimAtNearestEnemy(SpaceSimulation sim, out short pitch, out short yaw, out bool fire)
    {
        pitch = 0;
        yaw = 0;
        fire = false;
        short best = -1;
        int bestDistance = int.MaxValue;
        for (short obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            ref readonly var other = ref sim.Objects[obj];
            if (other.Class < ObjectClass.Ship || sim.Ships[obj].Side == sim.Ships[0].Side ||
                sim.Ships[obj].SpecialManeuver == SpecialManeuver.Unknown9)
            {
                continue;
            }
            int distance = VectorMath.Delta(sim.Objects[0].Position, other.Position).Magnitude();
            if (distance < bestDistance)
            {
                best = obj;
                bestDistance = distance;
            }
        }
        if (best == -1)
            return;
        var local = sim.Objects[0].TransformToObjectsFrame(VectorMath.Delta(sim.Objects[0].Position, sim.Objects[best].Position));
        var spherical = default(SphericalVector);
        VectorMath.RectangularToSpherical(local, ref spherical);
        var typeData = ObjectTypeTable.Get(sim.Objects[0].Type);
        pitch = (short)Math.Clamp(-spherical.Pitch * 8 / Math.Max((int)typeData.YawRate, 1), -8, 8);
        yaw = (short)Math.Clamp(spherical.Yaw * 8 / Math.Max((int)typeData.PitchRate, 1), -8, 8);
        fire = Math.Abs((int)spherical.Pitch) < 4 && Math.Abs((int)spherical.Yaw) < 4 && bestDistance >> 8 < 2500;
        if (bestDistance >> 8 > 800)
            sim.Accelerate(2);
        else
            sim.Accelerate(-2);
    }

    private static void PrintStatus(SpaceSimulation sim, int frame)
    {
        ref readonly var player = ref sim.Objects[0];
        ref readonly var ship = ref sim.Ships[0];
        var classes = new SortedDictionary<ObjectClass, int>();
        for (int obj = 0; obj <= ObjectSlots.LastMoving; obj++)
        {
            var objectClass = sim.Objects[obj].Class;
            if (objectClass != ObjectClass.Null)
                classes[objectClass] = classes.GetValueOrDefault(objectClass) + 1;
        }
        string census = string.Join(" ", classes.Select(p => $"{p.Key} {p.Value}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{frame,5}: at {Units(player.Position)} speed {player.Speed >> 8}/{ship.MaximumSpeed} energy {ship.WeaponEnergy} " +
            $"shields {ship.Shield[0]}/{ship.Shield[1]} armor {ship.Armor[0]}/{ship.Armor[1]}/{ship.Armor[2]}/{ship.Armor[3]} " +
            $"core {ship.Damage} target {ship.Target} lock {sim.TargetLockCountdown} hazards {sim.ActiveHazards} | {census}"));
    }

    private static void PrintShips(SpaceSimulation sim)
    {
        for (int obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            ref readonly var o = ref sim.Objects[obj];
            if (o.Class < ObjectClass.Missile)
                continue;
            ref readonly var s = ref sim.Ships[obj];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  [{obj}] {o.Class,-11} {TypeName(o.Type),-12} {s.Side,-8} at {Units(o.Position)} speed {o.Speed >> 8} " +
                $"shields {s.Shield[0]}/{s.Shield[1]} armor {s.Armor[0]}/{s.Armor[1]}/{s.Armor[2]}/{s.Armor[3]} core {s.Damage} " +
                $"special {s.SpecialManeuver} target {s.Target}"));
        }
    }

    /// <summary>Counts every simulation callback (sounds by number).</summary>
    private sealed class CountingSimulationEvents : ISimulationEvents
    {
        public Dictionary<string, int> Counts { get; } = [];

        private void Count(string name) => Counts[name] = Counts.GetValueOrDefault(name) + 1;

        public void PlaySoundEffect(int effect, int sourceObject) => Count($"sfx{effect}");

        public void ReleaseSoundSource(int sourceObject) => Count("releaseSource");

        public void WeaponSelectionChanged() => Count("weaponSelection");

        public void DestinationChanged() => Count("destination");

        public void ClearHudGunReadouts() => Count("clearGuns");

        public void InitializeCockpit(int cockpitMode) => Count("cockpit");

        public void TrainSimWaveCleared(bool waveActive) => Count("waveCleared");

        public void InitializeCockpitView(int mode) => Count($"view{mode}");

        public void ServiceTrack(short spaceFrame) => Count("serviceTrack");

        public void NewSpaceMusicChanges(short attacker, short victim) => Count("killMusic");

        public void HouseKeepCockpit(int cameraViewMode) => Count("houseKeep");

        public void AfterburnerExpired() => Count("afterburnerOut");

        public void PlayerAfterburnerEngaged(short spaceFrame) => Count("afterburner");

        public void TriggerPlayerHitPaletteFlash() => Count("playerHit");

        public void FlashCockpitPaletteEntry(int entry) => Count($"hitSide{entry}");

        public void PlaceDamageOnCockpit(short damage) => Count("cockpitDamage");

        public void ShowComponentHitHudMessage(SimulationHudMessage message, int component) => Count(message.ToString());

        public void VduMalfunction(int vdu, int sound) => Count("vduMalfunction");

        public void SelectCockpitVduMode(int vdu, int mode) => Count("selectVdu");

        public void ShowMissileLockedMessage() => Count("locked");

        public void RemoveMissileLockedMessage() => Count("lockOff");

        public void PlayerReleaseWeaponLaunched(ObjectType weaponType, short hardpoint) => Count("launch");

        public void SpaceBufferFlash() => Count("jumpFlash");

        public void ShowCockpitMessage(SimulationCockpitMessage message, ObjectType shipType) => Count($"message {message}");

        public void ResetSoundState() => Count("resetSound");
    }
}
