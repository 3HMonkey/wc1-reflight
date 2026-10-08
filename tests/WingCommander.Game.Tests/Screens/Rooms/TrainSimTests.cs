using WingCommander.Game.Screens.Rooms;
using WingCommander.Graphics.Cockpit;
using WingCommander.Graphics.Raster;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens.Rooms;

/// <summary>
/// The TrainSim menus around the arcade flight. The flight itself is still the coordinator's
/// placeholder (FlyTrainSimMissionAsync shows "not ported" until a key and reports a loss), so
/// every session here ends with Game Over after the first enemy.
/// </summary>
public class TrainSimTests
{
    private static (RoomScreenRig Rig, Func<bool> Done) StartTrainSim(bool startupMode, Action<RoomScreenRig>? setup = null)
    {
        var rig = RoomsRig.CreateCampaign();
        rig.Game.Session.CampaignStartupMode = startupMode;
        setup?.Invoke(rig);
        bool done = false;
        rig.Start(async game =>
        {
            await game.Screens.RunTrainSimAsync();
            done = true;
        });
        return (rig, () => done);
    }

    [DataFact]
    public void Startup_session_ends_with_name_and_callsign_entry()
    {
        var (rig, done) = StartTrainSim(startupMode: true);
        var session = rig.Game.Session;
        var events = rig.Game.Events;

        Assert.False(rig.Runtime.RunHeadless(1_500));
        RoomsRig.Snap(rig, "trainsim-get-ready");
        Assert.Equal(TrainSimStartupScore, rig.Game.Screens.TrainSim.ArcadeScore);
        Assert.Equal(2, rig.Game.Screens.TrainSim.Mission);
        Assert.Equal(0, session.State.CampaignIndex);

        // The placeholder flight waits for a key, then the loss shows Game Over.
        RoomsRig.Space(rig, 3_500);
        Assert.False(rig.Runtime.RunHeadless(6_500));
        RoomsRig.Snap(rig, "trainsim-game-over");

        // Name entry: replace "Blair" by "Steele", replace "Maverick" by "Ace".
        Assert.False(rig.Runtime.RunHeadless(9_500));
        RoomsRig.Snap(rig, "trainsim-name-entry");
        double at = 10_000;
        for (int i = 0; i < 5; i++, at += 150)
            RoomsRig.Backspace(rig, at);
        RoomsRig.ShiftedKey(rig, at, 's');
        at = RoomsRig.Type(rig, at + 300, "teele", 150);
        RoomsRig.Enter(rig, at += 200);
        at += 500;
        for (int i = 0; i < 8; i++, at += 150)
            RoomsRig.Backspace(rig, at);
        RoomsRig.ShiftedKey(rig, at, 'a');
        at = RoomsRig.Type(rig, at + 300, "ce", 150);
        Assert.False(rig.Runtime.RunHeadless(at));
        RoomsRig.Snap(rig, "trainsim-callsign-typed");
        RoomsRig.Enter(rig, at += 200);

        // The ranking follows until a key.
        Assert.False(rig.Runtime.RunHeadless(at + 2_000));
        RoomsRig.Snap(rig, "trainsim-ranking-after-startup");
        RoomsRig.Space(rig, at + 2_500);
        Assert.True(rig.Runtime.RunHeadless(at + 5_000), "the ranking did not close");
        Assert.True(done());
        Assert.Null(rig.Runtime.Failure);

        Assert.Equal("Steele", session.Player.Name);
        Assert.Equal("Ace", session.Player.Callsign);
        int rank = session.HighScores.Find(8);
        Assert.True(rank >= 0);
        Assert.Equal((uint)TrainSimStartupScore, session.HighScores.Entries[rank].Score);
        Assert.Equal(4, rig.Game.Screens.TrainSim.Mission);
        Assert.False(rig.Game.Screens.TrainSim.Active);
    }

    private const int TrainSimStartupScore = 4000;

    [DataFact]
    public void Default_name_is_kept_and_byte_layout_follows_the_original_copies()
    {
        var (rig, _) = StartTrainSim(startupMode: true);
        RoomsRig.Space(rig, 3_500);
        Assert.False(rig.Runtime.RunHeadless(9_500));
        RoomsRig.Enter(rig, 10_000);                 // "Blair"
        for (int i = 0; i < 8; i++)
            RoomsRig.Backspace(rig, 10_500 + i * 150);
        double at = RoomsRig.Type(rig, 12_000, "ace", 150);
        RoomsRig.Enter(rig, at + 200);
        RoomsRig.Space(rig, at + 2_500);
        Assert.True(rig.Runtime.RunHeadless(at + 5_000));
        var player = rig.Game.Session.Player;
        Assert.Equal("Blair", player.Name);
        Assert.Equal("ace", player.Callsign);             // letters are lower case without Shift
        // strcpy of "Maverick" then of "ace": the tail of the default stays behind the NUL.
        Assert.Equal("ace\0rick\0"u8.ToArray(), player.CallsignBytes.AsSpan(0, 9).ToArray());
    }

    [DataFact]
    public void Ranking_enemy_selection_and_low_score_message()
    {
        var (rig, done) = StartTrainSim(startupMode: false);
        Assert.False(rig.Runtime.RunHeadless(1_000));
        RoomsRig.Snap(rig, "trainsim-ranking");
        RoomsRig.Space(rig, 1_500);
        Assert.False(rig.Runtime.RunHeadless(2_500));
        RoomsRig.Snap(rig, "trainsim-select-enemy");
        Assert.Equal(1, rig.Game.Events.CursorShowCount);
        rig.Click(3_000, 270, 105);                  // bottom-right portrait: enemy 3
        Assert.False(rig.Runtime.RunHeadless(4_000));
        Assert.Equal(3, rig.Game.Screens.TrainSim.Mission);
        Assert.Equal(0, rig.Game.Events.CursorShowCount);
        RoomsRig.Space(rig, 6_000);                  // placeholder flight
        Assert.False(rig.Runtime.RunHeadless(12_000));
        RoomsRig.Snap(rig, "trainsim-low-score");
        RoomsRig.Space(rig, 12_500);                 // "YOUR SCORE IS ONLY 00"
        Assert.False(rig.Runtime.RunHeadless(14_000));
        RoomsRig.Space(rig, 14_500);                 // ranking
        Assert.True(rig.Runtime.RunHeadless(17_000));
        Assert.True(done());
        Assert.Null(rig.Runtime.Failure);
        Assert.Equal(0u, rig.Game.Session.HighScores.Entries[rig.Game.Session.HighScores.Find(8)].Score);
    }

    [DataFact]
    public void Esc_on_the_ranking_cancels_the_simulator()
    {
        var (rig, done) = StartTrainSim(startupMode: false);
        RoomsRig.Escape(rig, 1_500);
        Assert.True(rig.Runtime.RunHeadless(4_000), "Esc did not leave the simulator");
        Assert.True(done());
        Assert.Null(rig.Runtime.Failure);
        Assert.Equal(-1, rig.Game.Screens.TrainSim.Mission);
        Assert.False(rig.Game.Screens.TrainSim.Active);
    }

    [DataFact]
    public void Ranking_scrolls_the_title_after_twelve_seconds()
    {
        var (rig, _) = StartTrainSim(startupMode: false);
        Assert.False(rig.Runtime.RunHeadless(14_000));
        RoomsRig.Snap(rig, "trainsim-title-scroll");
        Assert.False(rig.Runtime.RunHeadless(18_500));
        RoomsRig.Snap(rig, "trainsim-title-top");
        Assert.False(rig.Runtime.RunHeadless(24_000));
        Assert.Null(rig.Runtime.Failure);
    }

    [DataFact]
    public void Beating_the_previous_score_congratulates()
    {
        var (rig, done) = StartTrainSim(startupMode: false, setup: r =>
        {
            // A negative previous score lets the placeholder's 0 points rank (signed comparison).
            r.Game.Session.HighScores.Set(r.Game.Session.HighScores.Find(8), 8, uint.MaxValue);
        });
        RoomsRig.Space(rig, 1_500);
        rig.Click(3_000, 55, 35);                    // top-left portrait: enemy 0
        RoomsRig.Space(rig, 6_000);
        Assert.False(rig.Runtime.RunHeadless(12_000));
        RoomsRig.Snap(rig, "trainsim-congratulations");
        Assert.Equal(0, rig.Game.Screens.TrainSim.ArcadeScore);
        var scores = rig.Game.Session.HighScores;
        Assert.Equal(0u, scores.Entries[scores.Find(8)].Score);
        RoomsRig.Space(rig, 12_500);
        RoomsRig.Space(rig, 14_500);
        Assert.True(rig.Runtime.RunHeadless(17_000));
        Assert.True(done());
    }

    [DataFact]
    public void Victory_screen_draws_fireworks_over_the_simulator_view()
    {
        var rig = RoomsRig.CreateCampaign();
        rig.Start(async game =>
        {
            var screens = new TrainSimStatusScreens(game, new TestFlight(game));
            await screens.ShowVictoryScreenAsync();
        });
        for (int at = 1_000; at <= 4_500; at += 250)
        {
            Assert.False(rig.Runtime.RunHeadless(at));
            RoomsRig.Snap(rig, $"trainsim-victory-{at}");
        }
        Assert.True(rig.Runtime.RunHeadless(8_000));
        Assert.Null(rig.Runtime.Failure);
    }


    [DataFact]
    public void A_provided_flight_is_driven_in_the_original_order()
    {
        var rig = RoomsRig.CreateCampaign();
        rig.Game.Session.CampaignStartupMode = true;
        var flight = new TestFlight(rig.Game);
        rig.Game.Screens.TrainSimFlight = flight;
        bool done = false;
        rig.Start(async game =>
        {
            await game.Screens.RunTrainSimAsync();
            done = true;
        });
        RoomsRig.Space(rig, 3_500);                  // placeholder flight
        RoomsRig.Enter(rig, 11_000);
        RoomsRig.Enter(rig, 12_000);
        RoomsRig.Space(rig, 14_000);
        Assert.True(rig.Runtime.RunHeadless(17_000));
        Assert.True(done);
        Assert.Null(rig.Runtime.Failure);
        string[] expected =
            ["BeginSession", "InitializeMission 2", "BeginGetReady", "EndGetReady", "PrepareFlight True", "BeginGameOver", "EndSession"];
        Assert.Equal(expected, flight.Calls);
        Assert.Equal(40, flight.GetReadyFrames);
        Assert.Equal(80, flight.GameOverFrames);
    }

    /// <summary>A recording flight with a plain 320x128 space view at screen rows 24..151 (view mode 4).</summary>
    private sealed class TestFlight(Wc1Game game) : ITrainSimFlight
    {
        private string _phase = "";

        public List<string> Calls { get; } = [];

        public int GetReadyFrames { get; private set; }

        public int GameOverFrames { get; private set; }

        public Viewport SpaceBuffer { get; } = Viewport.Allocate(0, 0, 319, 127, 0xbf);

        public short ViewCenterX => 160;

        public short ViewCenterY => 64;

        public void BeginSession() => Calls.Add("BeginSession");

        public void InitializeMission(short mission) => Calls.Add($"InitializeMission {mission}");

        public void PrepareFlight(bool campaignStartup) => Calls.Add($"PrepareFlight {campaignStartup}");

        public void EndSession() => Calls.Add("EndSession");

        public void BeginGetReady() => Begin("BeginGetReady");

        public void EndGetReady() => Begin("EndGetReady");

        public void BeginVictory() => Begin("BeginVictory");

        public void BeginGameOver() => Begin("BeginGameOver");

        public bool RefreshCockpitStatus()
        {
            if (_phase == "BeginGetReady")
                GetReadyFrames++;
            else if (_phase == "BeginGameOver")
                GameOverFrames++;
            game.Events.PumpWindowMessages();
            game.Graphics.ClearViewport(SpaceBuffer, 0xbf);
            return true;
        }

        public void DumpBufferToScreen() =>
            game.Graphics.FizzleFade(SpaceBuffer, game.Graphics.Screen!, ViewGeometry.Mode4);

        private void Begin(string phase)
        {
            _phase = phase;
            Calls.Add(phase);
        }
    }
}
