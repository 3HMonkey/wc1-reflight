using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;
using WingCommander.Tests;

namespace WingCommander.Simulation.Tests.Flight;

/// <summary>Drives the simulation like RunSpaceFlight with scripted player input.</summary>
internal static class FlightRunner
{
    /// <summary>Sets up a mission and enters its first nav sphere like RunSpaceFlight.</summary>
    public static SpaceSimulation StartFlight(int dataSet, short series, short mission, uint seed, ISimulationEvents? events = null)
    {
        var sim = new SpaceSimulation(new CRandom(seed), new GameDirectoryResources(GameData.Require()), events)
        {
            CampaignDataSet = (short)dataSet,
            TrainSimActive = series == 0,
        };
        Assert.True(sim.InitMission(series, mission));
        sim.ForceView(0, 0);
        sim.FrameSkipCounter = 1;
        sim.SetUpActionSphere(sim.MissionShips[sim.PlayerMissionShipIndex].NavPoint);
        sim.ArcadeState = 0;
        return sim;
    }

    /// <summary>One flight frame with input <paramref name="frame"/> of a fixed script: turning,
    /// throttle changes, guns, afterburner, missiles; or a pilot hunting the nearest enemy.</summary>
    public static void Frame(SpaceSimulation sim, int frame, bool aim)
    {
        if (aim)
        {
            AimAtNearestEnemy(sim, out short pitch, out short yaw, out bool fire);
            sim.PlayersFlightDynamics(pitch, yaw, 0);
            if (fire)
                sim.FirePlayersLasers();
            if (sim.TargetLockCountdown == 0 && sim.SelectedReleaseWeaponIndex != -1)
                sim.PlayerReleaseWeapon();
        }
        else
        {
            short pitch = (short)((frame / 25 % 3) - 1);
            short yaw = (short)((frame / 40 % 3) - 1);
            short roll = (short)((frame / 70 % 3) - 1);
            sim.PlayersFlightDynamics((short)(pitch * 4), (short)(yaw * 4), roll);
            if (frame % 50 == 10)
                sim.Accelerate(5);
            if (frame % 3 == 0)
                sim.FirePlayersLasers();
            if (frame % 200 == 150)
                sim.YourAfterburner();
            if (frame % 160 == 80)
                sim.PlayerReleaseWeapon();
        }
        sim.Update3Space();
        if (sim.ArcadeState != 0)
            return;
        sim.PrepareSpaceView();
        sim.UpdateCockpitSimulation();
        sim.CheckStranded();
    }

    /// <summary>Runs <paramref name="frames"/> frames (or until the flight ends) and returns the state
    /// hash after every frame.</summary>
    public static List<ulong> Run(SpaceSimulation sim, int frames, Action<SpaceSimulation, int>? check = null, bool aim = false)
    {
        var hashes = new List<ulong>(frames);
        for (int frame = 0; frame < frames && sim.ArcadeState == 0; frame++)
        {
            Frame(sim, frame, aim);
            check?.Invoke(sim, frame);
            hashes.Add(sim.ComputeStateHash());
        }
        return hashes;
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
        sim.Accelerate(bestDistance >> 8 > 800 ? (short)2 : (short)-2);
    }
}
