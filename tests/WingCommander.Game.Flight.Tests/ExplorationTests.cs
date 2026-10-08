using WingCommander.Tests;
using Xunit.Abstractions;

namespace WingCommander.Game.Flight.Tests;

public class ExplorationTests(ITestOutputHelper output)
{
    [DataFact]
    public void List_player_ships()
    {
        var rig = new FlightRig();
        var lines = new List<string>();
        rig.Start(r =>
        {
            var sim = r.Layer.Simulation;
            for (int campaign = 0; campaign < 3; campaign++)
            {
                sim.CampaignDataSet = (short)campaign;
                for (int series = 0; series <= 13; series++)
                {
                    for (int mission = 0; mission < 4; mission++)
                    {
                        var data = sim.MissionModule.GetMission((short)series, (short)mission);
                        if (data is null)
                            continue;
                        lines.Add($"{campaign} {series} {mission}: {data.Ships[data.Header.PlayerMissionShip].Type}");
                    }
                }
            }
            return Task.CompletedTask;
        });
        rig.Run();
        foreach (var line in lines)
            output.WriteLine(line);
    }
}
