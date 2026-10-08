using WingCommander.Tests;

namespace WingCommander.Simulation.Tests.Flight;

/// <summary>
/// Objective -1 (no current objective, e.g. in the training simulator) reads the record before
/// the objective table like the Kilrathi Saga image (ObjectiveRecord), instead of throwing.
/// </summary>
public class ObjectiveMinusOneTests
{
    [DataFact]
    public void Simulator_missions_with_no_objective_compute_a_range_without_throwing()
    {
        var sim = FlightRunner.StartFlight(0, 0, 0, 7);
        sim.CurrentObjective = -1;

        Assert.Equal(sim.ObjectiveRecord(-1).Type is 1 or 2 or 3 or 4, sim.MobileObjective(-1));
        short located = sim.LocateMobileObjective(-1);
        Assert.InRange(located, (short)-1, (short)1);
        sim.SetObjectiveRange(false);
        Assert.True(sim.CurrentObjectiveRange >= 0);

        // The range is measured to the record's (zero) position, i.e. to the origin.
        var again = FlightRunner.StartFlight(0, 0, 0, 7);
        again.CurrentObjective = -1;
        again.SetObjectiveRange(false);
        Assert.Equal(sim.CurrentObjectiveRange, again.CurrentObjectiveRange);
    }
}
