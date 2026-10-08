using WingCommander.Core.Platform;
using WingCommander.Game.Flow;
using WingCommander.Graphics.Palettes;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Tests;

namespace WingCommander.Game.Flight.Tests;

/// <summary>The cinematic sequences around a flight (M5): presents, pacing, Esc, state afterwards.</summary>
public class SequenceTests
{
    private const double CinematicFrame = 62;

    /// <summary>Flies series 1 mission 0 for <paramref name="frames"/> frames, then runs <paramref name="after"/>.</summary>
    private static FlightRig FlyThen(int seed, int frames, Func<FlightRig, Task> after, Action<FlightRig>? setUp = null)
    {
        var rig = new FlightRig(seed);
        rig.Start(async r =>
        {
            setUp?.Invoke(r);
            r.StopAfter(frames);
            await r.Layer.FlyMissionAsync(1, 0);
            await after(r);
        });
        return rig;
    }

    private static short FindCarrier(FlightSession session)
    {
        var sim = session.Sim;
        for (short obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (sim.Objects[obj].Type == ObjectType.TigersClaw && sim.Objects[obj].Class != ObjectClass.Null)
                return obj;
        }
        return -1;
    }

    [DataTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Scramble_shows_the_pilot_boarding(int shipType)
    {
        var rig = new FlightRig(11);
        int presents = 0;
        var times = new List<double>();
        rig.Start(async r =>
        {
            r.Game.Session.State.PlayerShipType = (sbyte)shipType;
            r.OnPresent(count =>
            {
                presents++;
                times.Add(r.Runtime.Scheduler.Now);
                if (count == 30)
                    r.SavePng($"scramble-{shipType}-030");
                if (count == 59)
                    r.SavePng($"scramble-{shipType}-059");
            });
            await r.Layer.ScrambleAsync();
        });
        rig.Run();
        // 10 + 27 + 23 frames, then the pause.
        Assert.Equal(60, presents);
        // 16 fps after the first frame (the throttle does not wait when the previous present is long
        // past); each copy waits for the 70 Hz vertical blank, so single gaps are 4 or 5 retraces.
        for (int i = 2; i < times.Count; i++)
            Assert.InRange(times[i] - times[i - 1], 4 * 1000.0 / 70 - 0.01, 5 * 1000.0 / 70 + 0.01);
        double average = (times[^1] - times[1]) / (times.Count - 2);
        Assert.InRange(average, CinematicFrame - 1, CinematicFrame + 2);
        Assert.Equal(0, rig.Game.Graphics.Screen!.Top);
        Assert.Equal(199, rig.Game.Graphics.Screen!.Bottom);
        Assert.False(rig.Game.Events.EscapePressed);
    }

    [DataFact]
    public void Landing_approaches_the_carrier_then_shows_the_hangar()
    {
        int presents = 0;
        int damageLevel = -1;
        int hangarPixels = 0;
        var rig = FlyThen(21, 40, async r =>
        {
            var session = r.Session;
            short carrier = FindCarrier(session);
            Assert.True(carrier > 0, "no carrier in the mission");
            session.Sim.PlayerCollisionObject = carrier;
            damageLevel = session.Sim.CalculateDamageLevel();
            r.OnPresent(count =>
            {
                presents++;
                if (presents is 50 or 120 or 160 or 200 or 230)
                    r.SavePng($"landing-{presents:D3}");
                if (presents == 230)
                    hangarPixels = r.CountPixels(24, 151);
            });
            await r.Layer.LandingSequenceAsync();
            r.SavePng("landing-end");
        });
        rig.Run();
        // Approach 100 + deck 35 + path 50, hangar 30 + 30, the deck officer's comment.
        Assert.Equal(100 + 35 + 50 + 30 + 30 + 1, presents);
        Assert.Equal(0, damageLevel);
        Assert.True(hangarPixels > 20000, $"hangar pixels: {hangarPixels}");
        var sim = rig.Session.Sim;
        Assert.False(sim.Space3DObjectsActive);
        Assert.Equal(-1, sim.PlayerCollisionObject);
        Assert.Equal(0, sim.ArcadeState);
        Assert.False(sim.ScriptedView);
        Assert.True(rig.Session.IntroSceneResourcesActive);
    }

    [DataFact]
    public void Esc_skips_the_landing()
    {
        int presents = 0;
        var rig = FlyThen(21, 40, async r =>
        {
            var session = r.Session;
            session.Sim.PlayerCollisionObject = FindCarrier(session);
            double now = r.Runtime.Scheduler.Now;
            r.Key(now + 1000, 0x01);
            r.Key(now + 14000, 0x01);
            r.OnPresent(_ => presents++);
            await r.Layer.LandingSequenceAsync();
        });
        rig.Run();
        // About 16 frames of the approach (185 without Esc), then the hangar scene (61) and its fades.
        Assert.InRange(presents, 15 + 61, 18 + 61 + 3);
        Assert.False(rig.Game.Events.EscapePressed);
    }

    [DataFact]
    public void Ejection_runs_the_three_phases_and_returns_not_stranded()
    {
        int presents = 0;
        bool stranded = true;
        var rig = FlyThen(31, 40, async r =>
        {
            int first = r.Game.Display.SlamCount;
            r.OnPresent(count =>
            {
                if (count - first is 5 or 15 or 60 or 150)
                    r.SavePng($"ejection-{count - first:D3}");
            });
            stranded = await r.Layer.EjectionSequenceAsync();
            presents = r.Game.Display.SlamCount - first;
        });
        rig.Run();
        Assert.False(stranded);
        // 10 + 10 + 200 frames, two letterbox backdrops of initialize_cockpit(4) when the view script
        // switches to views 11 and 4, the faded screen, the cleared screen (two slams).
        Assert.Equal(10 + 10 + 200 + 2 + 3, presents);
        var sim = rig.Session.Sim;
        Assert.Equal(0, sim.ArcadeState);
        Assert.False(sim.Space3DObjectsActive);
        Assert.False(sim.ScriptedView);
    }

    [DataFact]
    public void Stranded_sequence_shows_the_text_and_the_end()
    {
        int presents = 0;
        int textPixels = 0;
        var rig = FlyThen(41, 40, async r =>
        {
            r.OnPresent(count =>
            {
                presents++;
                if (presents == 200)
                {
                    textPixels = r.CountPixels(24, 151, PaletteColours.ViewportClear);
                    r.SavePng("stranded-200");
                }
                if (presents == 350)
                    r.SavePng("stranded-350");
            });
            await r.Layer.StrandedSequenceAsync();
        });
        rig.Run();
        Assert.InRange(presents, 400, 404);
        Assert.False(rig.Session.Sim.Space3DObjectsActive);
        Assert.Equal(0, rig.Game.Graphics.Screen!.Top);
        Assert.Equal(199, rig.Game.Graphics.Screen!.Bottom);
        _ = textPixels;
    }

    [DataFact]
    public void Death_sequence_shows_the_pilot_then_the_explosion()
    {
        int presents = 0;
        int whitePixels = 0;
        var rig = FlyThen(51, 40, async r =>
        {
            r.OnPresent(count =>
            {
                presents++;
                if (presents == 3)
                    r.SavePng("death-003");
                if (presents == 8)
                {
                    whitePixels = r.CountPixels(0, 199, PaletteColours.ViewportClear);
                    r.SavePng("death-008");
                }
                if (presents == 20)
                    r.SavePng("death-020");
            });
            await r.Layer.DeathSequenceAsync();
        });
        rig.Run();
        Assert.InRange(presents, 8 + 60, 8 + 60 + 3);
        Assert.True(whitePixels > 10000, $"white pixels of the last death frame: {whitePixels}");
        Assert.False(rig.Session.Sim.Space3DObjectsActive);
    }

    [DataFact]
    public void Attract_mode_runs_until_a_key_and_frees_the_scene()
    {
        var rig = new FlightRig(61);
        int presents = 0;
        rig.Start(async r =>
        {
            r.OnPresent(count =>
            {
                presents++;
                if (presents is 10 or 100 or 200 or 300)
                    r.SavePng($"attract-{presents:D3}");
            });
            r.Key(r.Runtime.Scheduler.Now + 20_000, 0x39, ' ');
            await r.Layer.PlayAttractSequenceAsync();
        });
        rig.Run(60_000);
        var session = rig.Session;
        // 20 s at 16 fps.
        Assert.InRange(session.AttractFrames, 300, 330);
        Assert.Equal(session.AttractFrames, presents);
        Assert.Equal(0, session.Sim.CannedSceneMode);
        Assert.False(session.Sim.ScriptedView);
        Assert.False(session.Sim.Space3DObjectsActive);
        Assert.True(session.IntroSceneResourcesActive);
    }

    [DataFact]
    public void Attract_mode_is_deterministic()
    {
        static ulong Run(int seed)
        {
            var rig = new FlightRig(seed);
            ulong hash = 14695981039346656037ul;
            rig.Start(async r =>
            {
                r.OnPresent(_ => hash = (hash ^ FlightRig.HashFrame(r.Front)) * 1099511628211ul);
                r.Key(r.Runtime.Scheduler.Now + 12_000, 0x39, ' ');
                await r.Layer.PlayAttractSequenceAsync();
            });
            rig.Run(60_000);
            return hash ^ rig.Layer.Simulation.ComputeStateHash();
        }

        Assert.Equal(Run(3), Run(3));
    }

    [DataFact]
    public void Campaign_victory_scene_draws_the_attack_into_the_cutscene_rows()
    {
        var rig = new FlightRig(71);
        int drawn = 0;
        int topPixels = -1;
        int viewPixels = 0;
        rig.Start(async r =>
        {
            var screen = r.Game.Graphics.Screen!;
            await r.Game.Display.ClearViewportAsync(screen, PaletteColours.Black);
            using (var scene = r.Layer.BeginCannedScene(CannedScene.CampaignVictory))
            {
                Assert.NotNull(scene);
                for (int frame = 0; frame < 250; frame++)
                {
                    if (scene!.Step())
                        drawn++;
                    await r.Game.Display.PresentAsync();
                    if (frame is 50 or 150 or 200 or 249)
                        r.SavePng($"victory-{frame:D3}");
                    if (frame == 200)
                    {
                        topPixels = r.CountPixels(0, 23);
                        viewPixels = r.CountPixels(24, 151);
                    }
                }
            }
        });
        rig.Run();
        Assert.Equal(250, drawn);
        Assert.Equal(0, topPixels);
        Assert.True(viewPixels > 500, $"space view pixels: {viewPixels}");
        var sim = rig.Session.Sim;
        Assert.False(sim.Space3DObjectsActive);
        Assert.False(sim.ScriptedView);
        Assert.Equal(0, sim.CannedSceneMode);
        Assert.True(rig.Session.IntroSceneResourcesActive);
    }

    [DataFact]
    public void Tiger_claw_escape_scene_ends_with_the_jump_flash()
    {
        var rig = new FlightRig(81);
        int whiteAfterJump = 0;
        int whiteBefore = 0;
        rig.Start(async r =>
        {
            r.Game.Session.State.CurrentSeries = 3;
            await r.Game.Display.ClearViewportAsync(r.Game.Graphics.Screen!, PaletteColours.Black);
            using var scene = r.Layer.BeginCannedScene(CannedScene.TigerClawEscape);
            Assert.NotNull(scene);
            for (int frame = 0; frame < 260; frame++)
            {
                scene!.Step();
                await r.Game.Display.PresentAsync();
                if (frame is 60 or 120 or 185 or 199)
                    r.SavePng($"escape-{frame:D3}");
                if (frame == 150)
                    whiteBefore = r.CountPixels(24, 151, PaletteColours.ViewportClear);
                if (frame == 199)
                    whiteAfterJump = r.CountPixels(24, 151, PaletteColours.ViewportClear);
            }
        });
        rig.Run();
        Assert.True(whiteAfterJump > 30000, $"white pixels after the jump: {whiteAfterJump}");
        Assert.True(whiteBefore < 5000, $"white pixels before the jump: {whiteBefore}");
        Assert.True(rig.Session.Sim.TypeResources[(int)ObjectType.HyperspaceJumpFlash].ShapeSet.IsNone);
        Assert.Null(rig.Layer.BeginCannedScene(CannedScene.AttractDogfight));
    }

    [DataFact]
    public void Training_simulator_flight_shares_the_arcade_score()
    {
        var rig = new FlightRig(91);
        FlightResult result = FlightResult.Running;
        int score = 0;
        rig.Start(async r =>
        {
            var flight = (Screens.Rooms.ITrainSimFlight)r.Layer;
            var arcade = r.Game.Screens.TrainSim;
            arcade.ArcadeWave = 0;
            arcade.Mission = 0;
            arcade.ArcadeScore = 1000;
            arcade.Active = true;
            r.Game.Session.CampaignDataSet = 0;
            flight.BeginSession();
            flight.InitializeMission(0);
            flight.BeginGetReady();
            for (int i = 0; i < 5; i++)
            {
                flight.RefreshCockpitStatus();
                flight.DumpBufferToScreen();
                await r.Game.Display.PresentAsync();
            }
            flight.EndGetReady();
            flight.PrepareFlight(false);
            r.StopAfter(100, (session, count) =>
            {
                if (count == 60)
                    r.SavePng("trainsim-060");
            });
            result = await r.Layer.FlyTrainSimMissionAsync(0);
            score = arcade.ArcadeScore;
            flight.EndSession();
        });
        rig.Run();
        Assert.Equal(FlightResult.Aborted, result);
        // One point per rendered frame without a bonus countdown (plus any kills); the time (2400 for
        // wave 0) runs down by one per rendered frame.
        Assert.True(score >= 1000 + 90, $"score {score}");
        Assert.InRange(rig.Session.ArcadeTimeRemaining, 2400 - 101, 2400 - 90);
        Assert.False(rig.Session.Sim.TrainSimActive);
        Assert.False(rig.Session.Sim.Space3DObjectsActive);
    }
}
