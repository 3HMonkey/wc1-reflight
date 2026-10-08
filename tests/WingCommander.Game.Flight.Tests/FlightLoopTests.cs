using WingCommander.Game.Flow;
using WingCommander.Graphics.Palettes;
using WingCommander.Simulation.Objects;
using WingCommander.Tests;

namespace WingCommander.Game.Flight.Tests;

public class FlightLoopTests
{
    /// <summary>Flies series 1 mission 0 of the Vega campaign for <paramref name="frames"/> presented frames.</summary>
    private static (ulong FrameHash, ulong StateHash, List<double> PresentTimes, FlightResult Result) Fly(int seed, int frames,
        Action<FlightRig>? script = null, string? pngPrefix = null, int series = 1, int mission = 0)
    {
        var rig = new FlightRig(seed);
        var presentTimes = new List<double>();
        FlightResult result = FlightResult.Running;
        ulong frameHash = 14695981039346656037ul;
        rig.Start(async r =>
        {
            r.StopAfter(frames, (session, count) =>
            {
                presentTimes.Add(r.Runtime.Scheduler.Now);
                frameHash = (frameHash ^ FlightRig.HashFrame(r.Front)) * 1099511628211ul;
                if (pngPrefix is not null && count % 50 == 0)
                    r.SavePng($"{pngPrefix}-{count:D4}");
            });
            script?.Invoke(r);
            result = await r.Layer.FlyMissionAsync(series, mission);
        });
        rig.Run();
        return (frameHash, rig.Layer.Simulation.ComputeStateHash(), presentTimes, result);
    }

    [DataFact]
    public void A_mission_flies_and_presents_at_20_fps()
    {
        var (_, _, presentTimes, result) = Fly(1, 120, pngPrefix: "loop-s1m0");
        Assert.Equal(FlightResult.Aborted, result);
        Assert.Equal(120, presentTimes.Count);
        for (int i = 1; i < presentTimes.Count; i++)
            Assert.Equal(50.0, presentTimes[i] - presentTimes[i - 1], 3);
    }

    [DataFact]
    public void The_same_seed_gives_the_same_frames_and_state()
    {
        var first = Fly(7, 150);
        var second = Fly(7, 150);
        Assert.Equal(first.FrameHash, second.FrameHash);
        Assert.Equal(first.StateHash, second.StateHash);
        var other = Fly(8, 150);
        Assert.NotEqual(first.StateHash, other.StateHash);
    }

    [DataFact]
    public void The_cockpit_window_shows_the_space_view_and_the_art_around_it()
    {
        var rig = new FlightRig(3);
        rig.Start(async r =>
        {
            r.StopAfter(40);
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        var session = rig.Session;
        var front = rig.Front;
        // Hornet front view: the space window is 320x105 at (0,10) minus the cockpit frame; its centre shows space.
        Assert.Equal(0, session.CockpitIndex);
        var geometry = session.ScreenViewportGeometry!;
        Assert.Equal(320, geometry.Width);
        Assert.Equal(105, geometry.Height);
        Assert.Equal(PaletteColours.PrimaryViewBuffer, front.Pixels[60 * 320 + 40]);
        // The lower half of the screen is cockpit art (VDUs, instruments): plenty of non-space colours.
        int art = 0;
        for (int i = 150 * 320; i < 200 * 320; i++)
        {
            if (front.Pixels[i] != PaletteColours.PrimaryViewBuffer && front.Pixels[i] != 0)
                art++;
        }
        Assert.True(art > 3000, $"cockpit art pixels: {art}");
        rig.SavePng("loop-cockpit");
    }

    [DataFact]
    public void Draw_order_follows_the_sorted_list()
    {
        var rig = new FlightRig(5);
        int drawn = 0, sorted = 0;
        rig.Start(async r =>
        {
            r.StopAfter(60, (session, count) =>
            {
                if (count != 60)
                    return;
                drawn = session.DrawnObjectCount;
                var sim = session.Sim;
                foreach (int obj in sim.SortedObjects)
                {
                    if (obj < 0 || (int)sim.Objects[obj].Type < 0)
                        break;
                    if (sim.Objects[obj].Class != Simulation.Data.ObjectClass.Null &&
                        (sim.Objects[obj].Class is Simulation.Data.ObjectClass.Star or Simulation.Data.ObjectClass.Dust or Simulation.Data.ObjectClass.Planet ||
                         !sim.Objects[obj].Shape.IsNone || obj == sim.NavPointerObject))
                        sorted++;
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        Assert.True(drawn > 7, $"drawn {drawn}");
        Assert.True(drawn <= sorted, $"drawn {drawn}, sorted {sorted}");
    }

    public static TheoryData<int, int, int> AllMissions()
    {
        var data = new TheoryData<int, int, int>();
        for (int campaign = 0; campaign < 3; campaign++)
        {
            for (int s = 0; s <= 13; s++)
            {
                for (int m = 0; m < 4; m++)
                    data.Add(campaign, s, m);
            }
        }
        return data;
    }

    [DataTheory]
    [MemberData(nameof(AllMissions))]
    public void Every_mission_flies_without_exceptions(int campaign, int series, int mission)
    {
        // Every other mission records its sprites for an R2 renderer (the recorder sees all content).
        bool r2 = (campaign + series + mission) % 2 == 1;
        var rig = new FlightRig(campaign * 1000 + series * 10 + mission,
            r2 ? new FlightOptions { RendererSupportsSpaceSprites = true } : null);
        FlightResult result = FlightResult.Running;
        bool exists = true;
        rig.Start(async r =>
        {
            r.Game.Session.CampaignDataSet = (short)campaign;
            var module = r.Layer.Simulation;
            module.CampaignDataSet = (short)campaign;
            if (module.MissionModule.GetMission((short)series, (short)mission) is null)
            {
                exists = false;
                return;
            }
            r.StopAfter(300);
            result = await r.Layer.FlyMissionAsync(series, mission);
        });
        rig.Run();
        if (!exists)
            return;
        Assert.NotEqual(FlightResult.Running, result);
        Assert.NotEqual(0, rig.Session.PresentedSpaceFrames);
        if (r2)
            Assert.True(rig.Session.PublishedSpaceFrames >= rig.Session.PresentedSpaceFrames);
    }
}

public class MissionInventoryTests
{
    [DataFact]
    public void The_campaigns_have_the_expected_missions()
    {
        var rig = new FlightRig();
        int[] counts = new int[3];
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
                        if (sim.MissionModule.GetMission((short)series, (short)mission) is not null)
                            counts[campaign]++;
                    }
                }
            }
            return Task.CompletedTask;
        });
        rig.Run();
        Assert.Equal([44, 22, 22], counts);
    }
}
