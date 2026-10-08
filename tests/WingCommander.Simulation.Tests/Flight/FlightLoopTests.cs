using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;
using WingCommander.Tests;

namespace WingCommander.Simulation.Tests.Flight;

public class FlightLoopTests
{
    public static TheoryData<int, short, short> Missions => new()
    {
        { 0, 1, 0 }, // Enyo, Alpha wing: carrier, asteroid field at nav 2
        { 0, 1, 1 },
        { 0, 3, 2 },
        { 0, 7, 0 },
        { 1, 1, 0 },
        { 2, 1, 0 },
    };

    [DataTheory]
    [MemberData(nameof(Missions))]
    public void Same_seed_gives_identical_state_hashes(int dataSet, short series, short mission)
    {
        const int frames = 600;
        var first = FlightRunner.Run(FlightRunner.StartFlight(dataSet, series, mission, 4242), frames, CheckPlausibility);
        var second = FlightRunner.Run(FlightRunner.StartFlight(dataSet, series, mission, 4242), frames);
        Assert.Equal(first.Count, second.Count);
        Assert.True(first.Count > 100, $"flight ended after {first.Count} frames");
        for (int i = 0; i < first.Count; i++)
            Assert.True(first[i] == second[i], $"state diverged at frame {i}");

        var other = FlightRunner.Run(FlightRunner.StartFlight(dataSet, series, mission, 99), frames);
        Assert.NotEqual(first[^1], other[^1]);
    }

    [DataTheory]
    [InlineData(0, 2, 0)] // McAuliffe, Beta wing: Dralthis
    [InlineData(0, 5, 1)] // Theta wing: Salthis
    public void Combat_is_deterministic_and_scores_kills(int dataSet, short series, short mission)
    {
        const int frames = 3000;
        var firstSim = FlightRunner.StartFlight(dataSet, series, mission, 7);
        var first = FlightRunner.Run(firstSim, frames, CheckPlausibility, aim: true);
        var secondSim = FlightRunner.StartFlight(dataSet, series, mission, 7);
        var second = FlightRunner.Run(secondSim, frames, aim: true);
        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
            Assert.True(first[i] == second[i], $"state diverged at frame {i}");
        Assert.True(secondSim.PlayerKillCount > 0, "the test pilot should have scored");
        Assert.True(secondSim.Campaign.MissionScore > 0);
    }

    /// <summary>Invariants after every frame.</summary>
    private static void CheckPlausibility(SpaceSimulation sim, int frame)
    {
        string where = $"frame {frame}";
        for (short obj = 0; obj < ObjectSlots.Count; obj++)
        {
            ref readonly var o = ref sim.Objects[obj];
            Assert.True(o.Class is >= ObjectClass.Null and <= ObjectClass.CapitalShip, $"{where}: slot {obj} class {o.Class}");
            if (o.Class == ObjectClass.Null)
                continue;
            Assert.True(obj <= ObjectSlots.LastMoving, $"{where}: scratch slot {obj} has class {o.Class}");
            if (o.Class >= ObjectClass.Missile)
                Assert.True(obj < ObjectSlots.ShipSlotCount, $"{where}: {o.Class} in slot {obj}");
            // The basis stays orthonormal within the 8.8 rounding of fix_objects_ijk.
            if (o.Class >= ObjectClass.Projectile)
            {
                int length = o.Forward.Magnitude();
                Assert.InRange(length, 0xf0, 0x110);
                Assert.InRange(System.Math.Abs(VectorMath.Dot(o.Forward, o.Up)), 0, 0x10);
            }
            if (obj >= ObjectSlots.FirstStar && obj <= ObjectSlots.LastStar && o.Class == ObjectClass.Star)
                Assert.InRange(o.Position.Magnitude() >> 8, 14000, 16000);
        }

        for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            ref readonly var o = ref sim.Objects[obj];
            if (o.Class < ObjectClass.Ship)
                continue;
            ref readonly var ship = ref sim.Ships[obj];
            for (int i = 0; i < 2; i++)
                Assert.True(ship.Shield[i] <= ship.MaximumShield[i], $"{where}: ship {obj} shield {i}");
            Assert.InRange(ship.WeaponEnergy, -100, 100);
            Assert.InRange(o.PitchRotation, -30, 30);
            Assert.InRange(o.YawRotation, -30, 30);
        }

        ref readonly var player = ref sim.Objects[ObjectSlots.Player];
        Assert.InRange(player.Speed, 0, sim.Ships[ObjectSlots.Player].MaximumSpeed << 8);

        // The draw list is sorted far to near after its first entry.
        for (int i = 2; i < ObjectSlots.Count && sim.SortedObjects[i] != -1; i++)
        {
            int previous = (ushort)sim.Objects[sim.SortedObjects[i - 1]].Distance;
            int current = (ushort)sim.Objects[sim.SortedObjects[i]].Distance;
            Assert.True(previous >= current, $"{where}: draw order {i}");
        }
    }
}
