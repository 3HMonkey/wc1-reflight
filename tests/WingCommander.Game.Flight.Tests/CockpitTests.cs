using WingCommander.Core.Platform;
using WingCommander.Core.Video;
using WingCommander.Simulation;
using WingCommander.Tests;

namespace WingCommander.Game.Flight.Tests;

/// <summary>Cockpit pages, the comm menu, the nav map and damage decals (M2, M4).</summary>
public class CockpitTests
{
    private static void KeyAt(FlightRig rig, int scanCode, int virtualKey, double now, double hold = 40)
    {
        var events = rig.Runtime.Events;
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), now + 1);
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), now + 1 + hold);
    }

    private static int CountDifferences(byte[] a, byte[] b, int left, int top, int right, int bottom)
    {
        int count = 0;
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (a[y * Framebuffer.Width + x] != b[y * Framebuffer.Width + x])
                    count++;
            }
        }
        return count;
    }

    [DataFact]
    public void Comm_menu_lists_the_wingman_orders_and_sends_radio_silence()
    {
        var rig = new FlightRig(31);
        var menus = new List<string[]>();
        bool radioSilence = false;
        int rightVduAfter = -1;
        rig.Start(async r =>
        {
            int pressed = 0;
            r.StopAfter(40, (session, count) =>
            {
                double now = r.Runtime.Scheduler.Now;
                if (count == 10)
                    KeyAt(r, 0x2e, 'C', now);
                if (count is 13 or 16 && session.GetVduMode(1) == 4 && menus.Count < 2 && pressed < count)
                {
                    var choices = new string[session.CommMenuChoiceCount];
                    for (int i = 0; i < choices.Length; i++)
                        choices[i] = session.GetCommMenuChoice(i) ?? "";
                    menus.Add(choices);
                    r.SavePng($"comm-menu-{count}");
                    int silence = Array.IndexOf(choices, "Keep radio silence");
                    // The recipient menu (with a target) first asks for the wingman (choice 1).
                    int choice = silence >= 0 ? silence : 0;
                    KeyAt(r, 0x02 + choice, '1' + choice, now);
                    pressed = count;
                }
                if (count == 30)
                {
                    radioSilence = session.Sim.RadioSilence;
                    rightVduAfter = session.GetVduMode(1);
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        Assert.NotEmpty(menus);
        var orders = menus[^1];
        Assert.Contains("Keep radio silence", orders);
        Assert.Equal("Never mind...", orders[^1]);
        Assert.Contains("Return to base.", orders);
        Assert.True(radioSilence);
        Assert.NotEqual(4, rightVduAfter);
    }

    [DataFact]
    public void Target_and_damage_pages_redraw_the_vdus()
    {
        var rig = new FlightRig(33);
        byte[] before = [], afterRight = [], afterLeft = [];
        rig.Start(async r =>
        {
            r.StopAfter(30, (session, count) =>
            {
                double now = r.Runtime.Scheduler.Now;
                if (count == 9)
                    before = (byte[])r.Game.Display.Working.Pixels.Clone();
                if (count == 10)
                {
                    KeyAt(r, 0x14, 'T', now);
                    KeyAt(r, 0x20, 'D', now + 50);
                }
                if (count == 14)
                {
                    afterRight = (byte[])r.Game.Display.Working.Pixels.Clone();
                    afterLeft = afterRight;
                    r.SavePng("vdu-target-damage");
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        Assert.Equal(3, rig.Session.GetVduMode(1));
        Assert.Equal(2, rig.Session.GetVduMode(0));
        // Hornet VDUs: left (10,133)-(82,198), right (236,133)-(309,198).
        Assert.True(CountDifferences(before, afterRight, 236, 133, 309, 198) > 100, "the right VDU did not change");
        Assert.True(CountDifferences(before, afterLeft, 10, 133, 82, 198) > 100, "the left VDU did not change");
    }

    [DataFact]
    public void Nav_map_replaces_the_screen_and_the_cockpit_returns()
    {
        var rig = new FlightRig(35);
        int mapPixels = 0;
        int windowAfter = 0;
        bool sampled = false;
        double openedAt = double.MaxValue;
        rig.Start(async r =>
        {
            r.OnPresent(_ =>
            {
                if (!sampled && r.Session.NavMapRuns == 1 && r.Runtime.Scheduler.Now > openedAt + 500)
                {
                    sampled = true;
                    mapPixels = r.CountPixels(0, 199);
                    r.SavePng("nav-map");
                }
            });
            r.StopAfter(30, (session, count) =>
            {
                double now = r.Runtime.Scheduler.Now;
                if (count == 10)
                {
                    openedAt = now;
                    KeyAt(r, 0x31, 'N', now);
                    KeyAt(r, 0x01, 0x1b, now + 1_500);
                }
                if (count == 20)
                    windowAfter = r.CountPixels(20, 100, Graphics.Palettes.PaletteColours.PrimaryViewBuffer);
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        Assert.True(sampled);
        Assert.True(mapPixels > 5_000, $"map pixels {mapPixels}");
        Assert.True(windowAfter > 10_000, $"space pixels after the map {windowAfter}");
    }

    [DataTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_knocked_out_vdu_shows_static_only_with_the_option(bool staticNoise)
    {
        var rig = new FlightRig(39, new FlightOptions { VduStaticNoise = staticNoise });
        var frames = new List<byte[]>();
        int mode = -1;
        rig.Start(async r =>
        {
            r.StopAfter(20, (session, count) =>
            {
                if (count == 10)
                    ((ISimulationEvents)session).VduMalfunction(1, 0x18);
                if (count is >= 12 and <= 14)
                {
                    frames.Add((byte[])r.Game.Display.Working.Pixels.Clone());
                    mode = session.GetVduMode(1);
                    if (count == 12)
                        r.SavePng($"vdu-static-{(staticNoise ? "on" : "off")}");
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        Assert.Equal(0, mode);
        // Hornet right VDU (236,133)-(309,198): fresh static every tick, or a frozen picture.
        int changed = CountDifferences(frames[0], frames[1], 236, 133, 309, 198) +
            CountDifferences(frames[1], frames[2], 236, 133, 309, 198);
        if (staticNoise)
            Assert.True(changed > 500, $"static pixels changed: {changed}");
        else
            Assert.Equal(0, changed);
    }

    [DataTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cockpit_damage_shows_the_explosion_then_the_decal(bool animation)
    {
        // The same flight with and without the hit: the decal changes nothing but pixels.
        (byte[] Early, byte[] Late, (short X, short Y) Decal, bool Active) Run(bool hit)
        {
            var rig = new FlightRig(37, new FlightOptions { CockpitExplosionAnimation = animation });
            byte[] early = [], late = [];
            (short X, short Y) position = default;
            rig.Start(async r =>
            {
                r.StopAfter(40, (session, count) =>
                {
                    var working = r.Game.Display.Working.Pixels;
                    if (count == 10)
                    {
                        if (hit)
                            ((ISimulationEvents)session).PlaceDamageOnCockpit(1);
                        var decal = Cockpit.CockpitTables.DamagePositions[session.CockpitIndex][1];
                        position = (decal.X, decal.Y);
                    }
                    if (count == 11)
                    {
                        early = (byte[])working.Clone();
                        if (hit)
                            r.SavePng($"cockpit-damage-{(animation ? "animated" : "instant")}-011");
                    }
                    if (count == 30)
                    {
                        late = (byte[])working.Clone();
                        if (hit)
                            r.SavePng($"cockpit-damage-{(animation ? "animated" : "instant")}-030");
                    }
                });
                await r.Layer.FlyMissionAsync(1, 0);
            });
            rig.Run();
            return (early, late, position, rig.Session.IsCockpitExplosionActive);
        }

        var hit = Run(true);
        var clean = Run(false);
        Assert.Equal(clean.Late.Length, hit.Late.Length);
        int earlyChange = CountDifferences(clean.Early, hit.Early, 0, 0, 319, 199);
        int lateChange = CountDifferences(clean.Late, hit.Late, 0, 0, 319, 199);
        var (x, y) = hit.Decal;
        int lateNearDecal = CountDifferences(clean.Late, hit.Late, Math.Max(0, x - 30), Math.Max(0, y - 30),
            Math.Min(319, x + 30), Math.Min(199, y + 30));
        Assert.True(earlyChange > 20, $"no change after the hit ({earlyChange})");
        Assert.True(lateNearDecal > 20, $"no decal at the end ({lateNearDecal})");
        Assert.Equal(lateChange, lateNearDecal);
        Assert.False(hit.Active);
    }
}
