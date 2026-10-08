using WingCommander.Core.Numerics;
using WingCommander.Simulation;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation.Tests.TestSupport;

/// <summary>Builds small worlds without game data (every shape "exists", sections are empty).</summary>
internal static class TestWorld
{
    public static SpaceSimulation Create(uint seed = 12345, RecordingEvents? events = null)
    {
        var sim = new SpaceSimulation(new CRandom(seed), new FakeResources(), events);
        sim.Init3SpaceObjects(1);
        sim.LoadMissionResources();
        return sim;
    }

    /// <summary>Puts the player's ship of <paramref name="type"/> into slot 0 at the origin facing +z.</summary>
    public static void AddPlayer(SpaceSimulation sim, ObjectType type = ObjectType.Hornet)
    {
        sim.Campaign.PlayerShipType = type;
        sim.InitializeObject(0, type, -1);
        sim.Ships[0].Side = Side.Imperial;
        sim.Ships[0].PilotLevel = 13;
        sim.Ships[0].Rating = 8;
        sim.Objects[0].Counter = -1;
    }

    /// <summary>A ship of <paramref name="type"/> and <paramref name="side"/> at <paramref name="position"/> (units).</summary>
    public static short AddShip(SpaceSimulation sim, ObjectType type, Side side, int x, int y, int z, sbyte rating = -1)
    {
        short slot = sim.InitializeShip(type, -1);
        Assert.NotEqual(-1, slot);
        sim.Ships[slot].Side = side;
        sim.Ships[slot].Rating = rating;
        sim.Ships[slot].PilotLevel = rating == -1 ? 2 : rating + 5;
        sim.Ships[slot].MissionIndex = -1;
        sim.Objects[slot].Position = Units(x, y, z);
        sim.Objects[slot].Counter = -1;
        return slot;
    }

    /// <summary>Takes a ship out of the AI: with mission type CANNED_SEQUENCE outside canned mode 2,
    /// ship_intelligence fails the objective every tick (goals zeroed, target cleared) and never
    /// maneuvers; only the collision avoidance still runs.</summary>
    public static void MakeInert(SpaceSimulation sim, short slot) => sim.Ships[slot].MissionType = ShipMissionType.CannedSequence;

    public static FixedVector Units(int x, int y, int z) => new(x << 8, y << 8, z << 8);

    /// <summary>A generator in the same state as <paramref name="sim"/>'s (to predict its next draws).</summary>
    public static CRandom Peek(SpaceSimulation sim) => new(sim.Random.Seed);
}
