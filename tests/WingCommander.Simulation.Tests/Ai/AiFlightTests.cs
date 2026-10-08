using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.Flight;
using WingCommander.Tests;

namespace WingCommander.Simulation.Tests.Ai;

public class AiFlightTests
{
    [DataTheory]
    [InlineData(0, 2, 0)] // McAuliffe, Beta wing: Dralthi and Salthi patrols, Paladin on the wing
    [InlineData(0, 5, 1)] // Brimstone, Theta wing: Exeter rendezvous, Salthi patrol, Dralthi strike
    [InlineData(0, 8, 2)] // Port Hedland, Sigma wing: strikes on the Exeter, Bakhtosh, a Fralthi warping out
    [InlineData(1, 3, 0)] // Secret Missions: Midgard, Delta wing
    public void The_ai_fights_deterministically_in_real_missions(int dataSet, short series, short mission)
    {
        const int frames = 2500;
        var first = FlightRunner.StartFlight(dataSet, series, mission, 31);
        bool engaged = false;
        bool wingmanSpoke = false;
        bool maneuvered = false;
        var hashes = FlightRunner.Run(first, frames, (sim, frame) =>
        {
            for (short obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
            {
                if (sim.Objects[obj].Class != ObjectClass.Ship)
                    continue;
                ref readonly var ship = ref sim.Ships[obj];
                engaged |= ship.Objective is ShipObjective.EngageEnemy or ShipObjective.DestroyShip;
                maneuvered |= ship.Maneuver > ShipManeuver.WarpingOut;
            }
            if (sim.YourWingman != -1)
                wingmanSpoke |= sim.Ships[sim.YourWingman].WingmanMessageState != -1;
        }, aim: true);
        var second = FlightRunner.Run(FlightRunner.StartFlight(dataSet, series, mission, 31), frames, aim: true);
        Assert.Equal(hashes.Count, second.Count);
        for (int i = 0; i < hashes.Count; i++)
            Assert.True(hashes[i] == second[i], $"state diverged at frame {i}");
        Assert.True(engaged, "nobody engaged");
        Assert.True(maneuvered, "no dogfight maneuver ran");
        _ = wingmanSpoke; // reported only when the mission has a rated wingman
    }

    [DataFact]
    public void The_tigers_claw_stays_put_while_the_ai_flies_around_it()
    {
        var sim = FlightRunner.StartFlight(0, 1, 0, 5);
        short claw = -1;
        for (short obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (sim.Objects[obj].Type == ObjectType.TigersClaw)
                claw = obj;
        }
        Assert.NotEqual(-1, claw);
        var start = sim.Objects[claw].Position;
        FlightRunner.Run(sim, 600);
        var moved = WingCommander.Simulation.Geometry.VectorMath.Delta(start, sim.Objects[claw].Position).Magnitude() >> 8;
        Assert.True(moved < 50, $"the carrier drifted {moved} units");
    }
}
