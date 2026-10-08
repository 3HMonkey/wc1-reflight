using WingCommander.Core.Platform;
using WingCommander.Game.Flow;
using WingCommander.Graphics.Palettes;
using WingCommander.Tests;

namespace WingCommander.Game.Flight.Tests;

public class ViewTests
{
    /// <summary>Presses <paramref name="scanCode"/> right after presented frame <paramref name="frame"/>.</summary>
    private static void PressAt(FlightRig rig, int scanCode, int virtualKey, double now, HostModifiers modifiers = HostModifiers.None)
    {
        var events = rig.Runtime.Events;
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, scanCode, virtualKey, 0, 0, 0, modifiers, false), now + 1);
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, scanCode, virtualKey, 0, 0, 0, modifiers, false), now + 60);
    }

    [DataFact]
    public void Camera_views_switch_and_render()
    {
        var rig = new FlightRig(11);
        var views = new List<(int Frame, int View, sbyte Mode, int Cockpitless)>();
        (int Frame, int Scan, int Vk, string Name)[] script =
        [
            (20, 0x3c, 0x71, "left"),
            (30, 0x3d, 0x72, "right"),
            (40, 0x3e, 0x73, "rear"),
            (50, 0x3f, 0x74, "chase"),
            (60, 0x40, 0x75, "overview"),
            (70, 0x3b, 0x70, "front"),
            (80, 0x3b, 0x70, "cockpitless"),
            (90, 0x3b, 0x70, "cockpit"),
        ];
        rig.Start(async r =>
        {
            r.StopAfter(100, (session, count) =>
            {
                foreach (var step in script)
                {
                    if (count == step.Frame)
                        PressAt(r, step.Scan, step.Vk, r.Runtime.Scheduler.Now);
                    if (count == step.Frame + 3)
                    {
                        views.Add((count, session.Sim.CameraViewMode, session.ScreenViewportMode, session.Sim.CockpitlessView));
                        r.SavePng($"view-{step.Frame:D3}-{step.Name}");
                    }
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        Assert.Equal(
        [
            (23, 2, (sbyte)2, 0),
            (33, 1, (sbyte)1, 0),
            (43, 3, (sbyte)3, 0),
            (53, 4, (sbyte)4, 0),
            (63, 14, (sbyte)5, 0),
            (73, 0, (sbyte)0, 0),
            (83, 0, (sbyte)0, 1),
            (93, 0, (sbyte)0, 0),
        ], views);
    }

    [DataTheory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 9, 0, 1)]
    [InlineData(0, 2, 0, 2)]
    [InlineData(0, 4, 0, 3)]
    [InlineData(2, 4, 0, 0)]
    public void Every_cockpit_renders(int campaign, int series, int mission, int expectedCockpit)
    {
        var rig = new FlightRig(21);
        rig.Start(async r =>
        {
            r.Game.Session.CampaignDataSet = (short)campaign;
            r.StopAfter(30, (session, count) =>
            {
                if (count == 30)
                    r.SavePng($"cockpit-c{campaign}-s{series}-m{mission}");
            });
            await r.Layer.FlyMissionAsync(series, mission);
        });
        rig.Run();
        var session = rig.Session;
        Assert.Equal(expectedCockpit, session.CockpitIndex);
        int space = 0;
        foreach (byte pixel in rig.Front.Pixels)
        {
            if (pixel == PaletteColours.PrimaryViewBuffer)
                space++;
        }
        Assert.True(space > 10000, $"space pixels {space}");
        Assert.True(space < 64000 - 15000, $"space pixels {space}");
    }
}
