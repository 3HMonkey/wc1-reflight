using WingCommander.Core.Platform;
using WingCommander.Game.Flow;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Tests;

namespace WingCommander.Game.Flight.Tests;

/// <summary>The flight controls (M3) and the cockpit pages they open (M4), driven through host events.</summary>
public class ControlsTests
{
    private readonly record struct Press(int Frame, int Scan, int VirtualKey, HostModifiers Modifiers = HostModifiers.None,
        double Hold = 40);

    private static void KeyAt(FlightRig rig, Press press, double now)
    {
        var events = rig.Runtime.Events;
        // Ctrl is read from the held Ctrl key (GetControlKeyState), not from the event modifiers.
        if ((press.Modifiers & HostModifiers.Control) != 0)
        {
            events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, 0x1d, 0x11, 0, 0, 0, press.Modifiers, false), now + 0.5);
            events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, 0x1d, 0x11, 0, 0, 0, HostModifiers.None, false),
                now + 2 + press.Hold);
        }
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, press.Scan, press.VirtualKey, 0, 0, 0, press.Modifiers, false), now + 1);
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, press.Scan, press.VirtualKey, 0, 0, 0, press.Modifiers, false),
            now + 1 + press.Hold);
    }

    /// <summary>Flies series 1 mission 0 for <paramref name="frames"/> frames with keys pressed right after the given frames.</summary>
    private static FlightRig Fly(int seed, int frames, Press[] script, Action<FlightRig, FlightSession, int>? onFrame = null,
        Action<FlightRig>? setUp = null)
    {
        var rig = new FlightRig(seed);
        rig.Start(async r =>
        {
            setUp?.Invoke(r);
            r.StopAfter(frames, (session, count) =>
            {
                foreach (var press in script)
                {
                    if (press.Frame == count)
                        KeyAt(r, press, r.Runtime.Scheduler.Now);
                }
                onFrame?.Invoke(r, session, count);
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        return rig;
    }

    [DataFact]
    public void Throttle_keys_set_the_commanded_speed()
    {
        var speeds = new Dictionary<int, int>();
        Fly(3, 40,
        [
            new(10, 0x0e, 0x08),               // Backspace: full stop
            new(15, 0x0d, 0xbb, Hold: 240),    // '=' held five ticks
            new(25, 0x0c, 0xbd, Hold: 90),     // '-' held two ticks
        ], (_, session, count) => speeds[count] = session.Sim.Objects[ObjectSlots.Player].Speed);
        Assert.True(speeds[9] > 0);
        Assert.Equal(0, speeds[12]);
        int raised = speeds[22];
        Assert.True(raised > 0 && raised % 0x100 == 0, $"speed {raised}");
        int lowered = speeds[30];
        Assert.True(lowered < raised && (raised - lowered) % 0x100 == 0, $"{raised} -> {lowered}");
    }

    [DataFact]
    public void Ctrl_plus_and_minus_change_the_frame_skip_only_with_the_origin_switch()
    {
        int speedPlain = 0, speedCtrl = 0, frameSkip = 0;
        Fly(4, 30, [new(10, 0x0e, 0x08), new(15, 0x0d, 0xbb, HostModifiers.Control, Hold: 120)],
            (_, session, count) =>
            {
                if (count == 20)
                    speedCtrl = session.Sim.Objects[ObjectSlots.Player].Speed;
            });
        Fly(4, 30, [new(10, 0x0e, 0x08)],
            (_, session, count) =>
            {
                if (count == 20)
                    speedPlain = session.Sim.Objects[ObjectSlots.Player].Speed;
            });
        Assert.Equal(speedPlain, speedCtrl);

        Fly(4, 30, [new(10, 0x0d, 0xbb, HostModifiers.Control, Hold: 120)],
            (_, session, count) =>
            {
                if (count == 20)
                    frameSkip = session.Sim.FrameSkip;
            },
            r => r.Game.Options.OriginDevUnlock = true);
        Assert.Equal(2, frameSkip);
    }

    [DataFact]
    public void Afterburner_key_lights_the_afterburner()
    {
        SpecialManeuver before = SpecialManeuver.Unknown9, after = SpecialManeuver.Unknown9;
        int fuelBefore = 0, fuelAfter = 0;
        Fly(5, 30, [new(10, 0x0f, 0x09)], (_, session, count) =>
        {
            ref readonly var ship = ref session.Sim.Ships[ObjectSlots.Player];
            if (count == 10)
                (before, fuelBefore) = (ship.SpecialManeuver, ship.Fuel);
            if (count == 13)
                (after, fuelAfter) = (ship.SpecialManeuver, ship.Fuel);
        });
        Assert.NotEqual(SpecialManeuver.Afterburner, before);
        Assert.Equal(SpecialManeuver.Afterburner, after);
        Assert.True(fuelAfter < fuelBefore, $"fuel {fuelBefore} -> {fuelAfter}");
    }

    [DataFact]
    public void Space_fires_the_guns()
    {
        int before = 0, after = 0;
        Fly(6, 20, [new(10, 0x39, ' ')], (_, session, count) =>
        {
            int projectiles = 0;
            for (int obj = 0; obj <= ObjectSlots.LastMoving; obj++)
            {
                ref readonly var o = ref session.Sim.Objects[obj];
                if (o.Class == ObjectClass.Projectile && o.Owner == ObjectSlots.Player)
                    projectiles++;
            }
            if (count == 10)
                before = projectiles;
            if (count == 12)
                after = projectiles;
        });
        Assert.Equal(0, before);
        Assert.True(after > 0);
    }

    [DataTheory]
    [InlineData(0x21, 'F', true)]   // the guns' new key
    [InlineData(0x39, ' ', false)]  // Space, their old key, does nothing now
    public void Rebound_guns_fire_on_their_new_key_only(int scan, int virtualKey, bool fires)
    {
        int after = 0;
        Fly(6, 20, [new(10, scan, virtualKey)], (_, session, count) =>
        {
            if (count == 12)
                after = PlayerProjectiles(session);
        }, setUp: r =>
        {
            r.Game.Preferences.Controls.Bind(Input.FlightAction.FireGuns, 0x21);
            r.Game.ApplyControls();
        });
        Assert.Equal(fires, after > 0);
    }

    private static int PlayerProjectiles(FlightSession session)
    {
        int projectiles = 0;
        for (int obj = 0; obj <= ObjectSlots.LastMoving; obj++)
        {
            ref readonly var o = ref session.Sim.Objects[obj];
            if (o.Class == ObjectClass.Projectile && o.Owner == ObjectSlots.Player)
                projectiles++;
        }
        return projectiles;
    }

    [DataFact]
    public void Vdu_keys_select_the_pages()
    {
        var modes = new Dictionary<int, (int Left, int Right)>();
        Fly(7, 60,
        [
            new(10, 0x20, 'D'),   // left: damage
            new(15, 0x14, 'T'),   // right: target
            new(20, 0x11, 'W'),   // left: weapons
            new(25, 0x2e, 'C'),   // right: communication menu
            new(30, 0x2e, 'C'),   // closes it again
        ], (_, session, count) => modes[count] = (session.GetVduMode(0), session.GetVduMode(1)));
        Assert.Equal((1, 5), modes[9]);
        Assert.Equal(2, modes[13].Left);
        Assert.Equal(3, modes[18].Right);
        Assert.Equal(1, modes[23].Left);
        Assert.Equal(4, modes[28].Right);
        Assert.NotEqual(4, modes[33].Right);
    }

    [DataFact]
    public void Toggles_switch_once_per_press()
    {
        var states = new Dictionary<int, (bool MissileCamera, bool VideoOff)>();
        Fly(8, 40,
        [
            new(10, 0x42, 0x77, Hold: 200),   // F8 held: missile camera on, once
            new(20, 0x2f, 'V', Hold: 200),    // V held: comm video off, once
            new(30, 0x42, 0x77),              // F8: off again
        ], (_, session, count) => states[count] = (session.Sim.MissileCameraEnabled, session.VideoImagesSuppressed));
        Assert.Equal((false, false), states[9]);
        Assert.Equal((true, false), states[16]);
        Assert.Equal((true, true), states[26]);
        Assert.Equal((false, true), states[33]);
    }

    [DataFact]
    public void Lock_key_toggles_the_target_lock_mode()
    {
        var locks = new Dictionary<int, bool>();
        int wingman = -1;
        Fly(9, 30, [new(10, 0x26, 'L'), new(20, 0x26, 'L')],
            (_, session, count) =>
            {
                // The lock needs a target (check_target drops it without one): the wingman, set right
                // before tick 12 reads the key pressed after frame 10.
                if (count == 11)
                {
                    wingman = session.Sim.YourWingman;
                    if (wingman != -1)
                        session.Sim.Ships[ObjectSlots.Player].Target = (sbyte)wingman;
                }
                locks[count] = session.Sim.TargetLockMode != 0;
            });
        Assert.NotEqual(-1, wingman);
        Assert.NotEqual(locks[9], locks[13]);
        Assert.Equal(locks[9], locks[23]);
    }

    /// <summary>
    /// Ctrl+S toggles the effects volume. Ctrl+M, literally: with the key queue on (flight), the
    /// virtual-key duplicate of M (0x4D) comes first and is the Right-arrow scan code, so Ctrl+Right
    /// raises the music by one step, and that message flushes the queue before M's own scan code.
    /// </summary>
    [DataFact]
    public void Volume_keys_follow_the_original_key_queue()
    {
        int sfx = -1, music = -1, sfxBefore = -1;
        string? sfxMessage = null, musicMessage = null;
        Fly(10, 30, [new(10, 0x1f, 'S', HostModifiers.Control, Hold: 120), new(20, 0x32, 'M', HostModifiers.Control, Hold: 120)],
            (r, session, count) =>
            {
                if (count == 9)
                    sfxBefore = r.Game.Volumes.SfxVolume;
                if (count == 13)
                {
                    sfx = r.Game.Volumes.SfxVolume;
                    sfxMessage = session.PendingHudMessage;
                }
                if (count == 23)
                {
                    music = r.Game.Volumes.MusicVolume;
                    musicMessage = session.PendingHudMessage;
                }
            },
            r => r.Game.Volumes.MusicVolume = 10);
        Assert.Equal(sfxBefore == 0 ? 20 : 0, sfx);
        Assert.Equal($"SFX VOLUME: {sfx / 2}.", sfxMessage);
        Assert.Equal(11, music);
        Assert.Equal("MUSIC VOLUME: 5.", musicMessage);
    }

    [DataFact]
    public void Pause_holds_the_flight_until_a_key()
    {
        var times = new Dictionary<int, double>();
        // P is held past the start of the pause: the wait first needs its release, then any key press.
        var rig = Fly(11, 20, [new(10, 0x19, 'P', Hold: 150)], (r, session, count) =>
        {
            times[count] = r.Runtime.Scheduler.Now;
            if (count == 10)
                KeyAt(r, new Press(0, 0x39, ' '), r.Runtime.Scheduler.Now + 2_000);
        });
        // A key pressed after present n is read in tick n + 2 (its events arrive during the wait of tick n + 1).
        Assert.True(times[12] - times[11] > 1_900, $"gap {times[12] - times[11]}");
        Assert.Equal(20, rig.Session.PresentedSpaceFrames);
    }

    [DataTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Esc_pauses_a_campaign_flight_only_with_the_option(bool escapePauses)
    {
        var times = new Dictionary<int, double>();
        var rig = new FlightRig(16, new FlightOptions { EscapePausesFlight = escapePauses });
        rig.Start(async r =>
        {
            r.StopAfter(20, (session, count) =>
            {
                double now = r.Runtime.Scheduler.Now;
                times[count] = now;
                if (count == 10)
                {
                    KeyAt(r, new Press(0, 0x01, 0x1b, Hold: 150), now);
                    KeyAt(r, new Press(0, 0x39, ' '), now + 2_000);
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        double gap = times[12] - times[11];
        if (escapePauses)
            Assert.True(gap > 1_900, $"gap {gap}");
        else
            Assert.InRange(gap, 49, 51);
        Assert.Equal(20, rig.Session.PresentedSpaceFrames);
    }

    [DataFact]
    public void Esc_ends_a_simulator_flight_in_the_literal_mode()
    {
        var rig = new FlightRig(12, new FlightOptions { EscapePausesFlight = false });
        FlightResult result = FlightResult.Running;
        int frames = 0;
        rig.Start(async r =>
        {
            var flight = (Screens.Rooms.ITrainSimFlight)r.Layer;
            r.Game.Events.KeyEventQueueEnabled = true; // as RunTrainSim sets it around the flight
            r.Game.Screens.TrainSim.Mission = 0;
            r.Game.Screens.TrainSim.ArcadeWave = 0;
            flight.BeginSession();
            flight.InitializeMission(0);
            flight.BeginGetReady();
            flight.RefreshCockpitStatus();
            flight.DumpBufferToScreen();
            await r.Game.Display.PresentAsync();
            flight.EndGetReady();
            flight.PrepareFlight(false);
            r.Session.FramePresented = session =>
            {
                frames++;
                if (frames == 10)
                    KeyAt(r, new Press(0, 0x01, 0x1b), r.Runtime.Scheduler.Now);
                if (frames == 200)
                    session.Sim.ArcadeState = 4;
            };
            result = await r.Layer.FlyTrainSimMissionAsync(0);
            flight.EndSession();
        });
        rig.Run();
        Assert.Equal(FlightResult.Aborted, result);
        Assert.InRange(frames, 10, 12);
    }

    [DataFact]
    public void Esc_in_the_simulator_opens_the_pause_menu_which_can_end_the_simulation()
    {
        var rig = new FlightRig(12);
        FlightResult result = FlightResult.Running;
        int frames = 0;
        rig.Start(async r =>
        {
            var flight = (Screens.Rooms.ITrainSimFlight)r.Layer;
            r.Game.Events.KeyEventQueueEnabled = true;
            r.Game.Screens.TrainSim.Mission = 0;
            r.Game.Screens.TrainSim.ArcadeWave = 0;
            flight.BeginSession();
            flight.InitializeMission(0);
            flight.BeginGetReady();
            flight.RefreshCockpitStatus();
            flight.DumpBufferToScreen();
            await r.Game.Display.PresentAsync();
            flight.EndGetReady();
            flight.PrepareFlight(false);
            r.Session.FramePresented = session =>
            {
                frames++;
                if (frames == 10)
                {
                    double now = r.Runtime.Scheduler.Now;
                    KeyAt(r, new Press(0, 0x01, 0x1b), now);
                    // Resume, Settings, End simulation.
                    KeyAt(r, new Press(0, 0x50, 0x28), now + 1_000);
                    KeyAt(r, new Press(0, 0x50, 0x28), now + 1_300);
                    KeyAt(r, new Press(0, 0x1c, 0x0d), now + 1_600);
                }
                if (frames == 200)
                    session.Sim.ArcadeState = 4;
            };
            result = await r.Layer.FlyTrainSimMissionAsync(0);
            flight.EndSession();
        });
        rig.Run();
        Assert.Equal(FlightResult.Aborted, result);
        Assert.InRange(frames, 10, 12);
    }

    [DataFact]
    public void Nav_map_opens_on_n_and_returns_on_esc()
    {
        int runs = 0;
        short dayBefore = 0, dayAfter = 0;
        double gap = 0, last = 0;
        var rig = Fly(13, 30, [new(10, 0x31, 'N')], (r, session, count) =>
        {
            double now = r.Runtime.Scheduler.Now;
            if (count == 10)
            {
                dayBefore = r.Game.Session.State.ElapsedDate.Day;
                KeyAt(r, new Press(0, 0x01, 0x1b), now + 1_500);
            }
            if (count == 12)
            {
                gap = now - last;
                runs = session.NavMapRuns;
                dayAfter = r.Game.Session.State.ElapsedDate.Day;
            }
            last = now;
        }, r => r.Game.Session.State.ElapsedDate.Day = 0x1234);
        Assert.Equal(1, runs);
        Assert.True(gap > 1_000, $"gap {gap}");
        // The map writes the game clock (hours | minutes << 8) into the elapsed day: 00:00 two seconds in.
        Assert.Equal(0x1234, dayBefore);
        Assert.Equal(0, dayAfter);
        Assert.Equal(30, rig.Session.PresentedSpaceFrames);
    }

    [DataFact]
    public void Mouse_steers_the_ship()
    {
        int yawRight = 0, yawLeft = 0;
        Fly(14, 40, [], (r, session, count) =>
        {
            var events = r.Runtime.Events;
            double now = r.Runtime.Scheduler.Now;
            // The first move after the flight start is the pointer warp's echo and is dropped (PointerMovedByKeyboard).
            if (count == 8)
                events.EnqueueHostEvent(new HostInputEvent(HostInputKind.MouseMove, 0, 0, 160, 60, 0, HostModifiers.None, false), now + 1);
            if (count == 10)
                events.EnqueueHostEvent(new HostInputEvent(HostInputKind.MouseMove, 0, 0, 300, 60, 0, HostModifiers.None, false), now + 1);
            if (count == 14)
                yawRight = session.StickInput.Yaw;
            if (count == 20)
                events.EnqueueHostEvent(new HostInputEvent(HostInputKind.MouseMove, 0, 0, 20, 60, 0, HostModifiers.None, false), now + 1);
            if (count == 24)
                yawLeft = session.StickInput.Yaw;
        });
        Assert.True(yawRight > 0, $"yaw {yawRight}");
        Assert.True(yawLeft < 0, $"yaw {yawLeft}");
    }

    [DataFact]
    public void A_key_script_is_deterministic()
    {
        Press[] script =
        [
            new(5, 0x0d, 0xbb, Hold: 300), new(12, 0x39, ' ', Hold: 400), new(20, 0x14, 'T'), new(24, 0x26, 'L'),
            new(30, 0x0f, 0x09), new(36, 0x3d, 0x72), new(44, 0x3b, 0x70), new(50, 0x2e, 'C'), new(54, 0x02, '1'),
            new(60, 0x20, 'D'), new(66, 0x0c, 0xbd, Hold: 200),
        ];
        ulong Run()
        {
            ulong hash = 14695981039346656037ul;
            var rig = Fly(15, 80, script, (r, _, _) => hash = (hash ^ FlightRig.HashFrame(r.Front)) * 1099511628211ul);
            return hash ^ rig.Layer.Simulation.ComputeStateHash();
        }

        Assert.Equal(Run(), Run());
    }
}
