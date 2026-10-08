using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Text;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The training simulator inside the flight (hudmsg.c GetArcadeBonus, FigureArcadeTime,
// DrawArcadeScorePanel, UpdateArcadeScoreDisplay; the arcade part of brains.c set_up_next_wave;
// the flight calls of system.c RunTrainSim and screens.c ShowGetReadyScreen/ShowVictoryScreen/
// ShowGameOverScreen). The menus own the arcade session (Game.Screens.TrainSim): score, wave,
// enemy and bonus countdown; the flight's score is the simulation's (kills add to it).
internal sealed partial class FlightSession
{
    /// <remarks>C: nArcadeTimeRemaining.</remarks>
    private short _arcadeTimeRemaining;

    /// <remarks>C: nArcadeWaveBonus.</remarks>
    private int _arcadeWaveBonus;

    /// <summary>Seconds-like counter of the simulator (rendered frames) left for this wave.</summary>
    public short ArcadeTimeRemaining => _arcadeTimeRemaining;

    /// <remarks>C: GetArcadeBonus (0x429E30, hudmsg.c).</remarks>
    private void GetArcadeBonus()
    {
        var arcade = Game.Screens.TrainSim;
        _arcadeWaveBonus = (_arcadeTimeRemaining * (arcade.Mission + 1) + (arcade.Mission + (arcade.ArcadeWave * 5 + 5) * 2) * 50) * 2;
    }

    /// <remarks>C: FigureArcadeTime (0x429E70, hudmsg.c).</remarks>
    private void FigureArcadeTime() => _arcadeTimeRemaining = (short)((Game.Screens.TrainSim.ArcadeWave + 6) * 400);

    /// <summary>A simulator wave was cleared: music cue, bonus countdown (60, or 30 while more waves come), bonus and time.</summary>
    /// <remarks>C: the nTrainSimActive branch of set_up_next_wave (0x40C3C0, brains.c).</remarks>
    private void TrainSimWaveCleared()
    {
        var arcade = Game.Screens.TrainSim;
        Audio.SpaceTrack(21, 2, 0);
        arcade.ArcadeBonusCountdown = 60;
        if (Sim.CurrentWave != -1)
            arcade.ArcadeBonusCountdown = 30;
        GetArcadeBonus();
        FigureArcadeTime();
    }

    /// <summary>
    /// The simulator's score panel in the space buffer: score (printed with a trailing 0), time and
    /// "1 UP"; without a bonus countdown the score grows and the time runs out (game over at 0);
    /// during the countdown the wave or mission bonus text.
    /// </summary>
    /// <remarks>C: UpdateArcadeScoreDisplay (0x429EE0) and DrawArcadeScorePanel (0x429E90, hudmsg.c).</remarks>
    private void UpdateArcadeScoreDisplay()
    {
        var sim = Sim;
        if (!sim.TrainSimActive)
            return;
        var arcade = Game.Screens.TrainSim;
        Gfx.SetTextContext(HudMessageTextContext);
        string score = sim.ArcadeScore.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Gfx.DrawFormattedText("%X%YScore: %s0 %XTime: %u %X1 UP", 10, 10, score, 10 + 0x82, _arcadeTimeRemaining, 10 + 0xbe);
        if (arcade.ArcadeBonusCountdown < 1)
        {
            sim.ArcadeScore++;
            _arcadeTimeRemaining--;
            if (_arcadeTimeRemaining < 1)
                sim.ArcadeState = 4;
            return;
        }
        string bonus = _arcadeWaveBonus.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Gfx.SetTextCursor(SpaceBuffer.Left, (SpaceBuffer.Top + SpaceBuffer.Bottom) / 2 - 5);
        if (sim.CurrentWave != -1)
            Gfx.FormatTextBufferFromStart(UiBytes("Wave %d complete.\n\nBonus Points: %s0%P"), arcade.ArcadeWave + 1, bonus);
        else
            Gfx.FormatTextBufferFromStart(UiBytes("Mission %d complete.\n\nBonus Points: %s0%P"), arcade.Mission + 1, bonus);
    }

    private static byte[] UiBytes(string text)
    {
        var bytes = new byte[text.Length];
        Graphics.GraphicsContext.EncodeText(text, bytes);
        return bytes;
    }

    // ------------------------------------------------------------------ ITrainSimFlight support

    /// <remarks>C: RunTrainSim: <c>nCannedSceneMode = 0; ResetStringBuilder(&amp;stHudMessageTextContext)</c>.</remarks>
    public void BeginTrainSimSession()
    {
        Sim.CannedSceneMode = 0;
        Sim.TrainSimActive = true;
        HudMessageTextContext.TextCursor = 0;
        if (HudMessageTextContext.TextBuffer is { Length: > 0 } buffer)
            buffer[0] = 0;
    }

    /// <remarks>C: RunTrainSim: <c>nTrainSimActive = 1; FigureArcadeTime(); init_mission(0, mission)</c>.</remarks>
    public void InitializeTrainSimMission(short mission)
    {
        PrepareCampaignData(trainingSimulator: true);
        FigureArcadeTime();
        InitMission(0, (short)mission);
    }

    /// <summary>After "Get Ready": the forced first session's handicap (no shields, shield generator destroyed,
    /// hull one point over capacity, wave 2, set_up_next_wave, 25 time units), then both VDUs redraw.</summary>
    /// <remarks>C: RunTrainSim (0x427080, system.c).</remarks>
    public void PrepareTrainSimFlight(bool campaignStartup)
    {
        var sim = Sim;
        if (campaignStartup)
        {
            ref var player = ref sim.Ships[ObjectSlots.Player];
            player.Shield[ShieldValues.Fore] = 0;
            player.MaximumShield[ShieldValues.Fore] = 0;
            sim.PlayerComponentDamage[2] = 4;
            player.Shield[ShieldValues.Aft] = 0;
            player.MaximumShield[ShieldValues.Aft] = 0;
            _arcadeTimeRemaining = 100;
            sim.CurrentWave = 2;
            player.Damage = (sbyte)(sim.TypeDataOf(ObjectSlots.Player).DamageCapacity + 1);
            sim.SetUpNextWave();
            _arcadeTimeRemaining = 25;
        }
        InvalidateVduMode(0);
        InvalidateVduMode(1);
    }

    /// <remarks>C: RunTrainSim: <c>free_all_slots(); free_cockpit(); free_3Space()</c>.</remarks>
    public void EndTrainSimSession()
    {
        CancelSpaceSpriteFrame();
        Sim.FreeAllSlots();
        FreeCockpit();
        Sim.Free3Space();
        Sim.TrainSimActive = false;
    }

    /// <remarks>C: ShowGetReadyScreen: <c>nCannedSceneMode = 1; force_view(0, 0); nFrameSkipCounter = 1</c>.</remarks>
    public void BeginGetReady()
    {
        Sim.CannedSceneMode = 1;
        Sim.ForceView(0, 0);
        Sim.FrameSkipCounter = 1;
    }

    /// <remarks>C: ShowGetReadyScreen: <c>clear_view_buffer(); nCannedSceneMode = 0; ResetSoundState()</c>.</remarks>
    public void EndGetReady()
    {
        CancelSpaceSpriteFrame();
        ClearViewBuffer();
        Sim.CannedSceneMode = 0;
        Audio.Sfx.ResetSoundState();
    }

    /// <summary>The player's ship explodes and an external camera 300 units behind the explosion looks at it.</summary>
    /// <remarks>C: ShowGameOverScreen (0x439A80, screens.c) up to generate_stars; RunTrainSim set nArcadeState = 4 before.</remarks>
    public void BeginGameOver()
    {
        var sim = Sim;
        sim.ArcadeState = 4;
        sim.ViewObject = (sbyte)sim.Explosion(ObjectSlots.Player);
        sim.CameraViewMode = 4;
        ref var eye = ref sim.Objects[ObjectSlots.Eye];
        eye.CollisionRadius = 100;
        int viewObject = sim.ViewObject;
        if ((uint)viewObject < ObjectSlots.Count)
        {
            ref readonly var target = ref sim.Objects[viewObject];
            var cameraOffset = Simulation.Geometry.VectorMath.Scale(target.Forward, -0x12c00);
            eye.Position = Simulation.Geometry.VectorMath.Add(target.Position, cameraOffset);
            eye.Up = target.Up;
            eye.Forward = cameraOffset;
            eye.FixIjk();
        }
        eye.Velocity = default;
        sim.SetEyeDirectionAndPosition();
        sim.GenerateStars();
        sim.FrameSkipCounter = 1;
    }

    /// <summary>Copies the menus' arcade score into the flight (the simulation adds kills to it).</summary>
    private void LoadArcadeScore() => Sim.ArcadeScore = Game.Screens.TrainSim.ArcadeScore;

    /// <summary>Copies the flight's arcade score back to the menus.</summary>
    private void StoreArcadeScore() => Game.Screens.TrainSim.ArcadeScore = Sim.ArcadeScore;

    /// <summary>One simulator mission: RunSpaceFlight(nArcadeWave) with the arcade score shared with the menus.</summary>
    /// <remarks>C: the RunSpaceFlight call of RunTrainSim (0x427080, system.c).</remarks>
    public async Task<int> FlyTrainSimMissionAsync()
    {
        LoadArcadeScore();
        try
        {
            return await RunSpaceFlightAsync(Game.Screens.TrainSim.ArcadeWave);
        }
        finally
        {
            StoreArcadeScore();
        }
    }

    /// <summary>The caption screens draw into the space buffer through the same text path.</summary>
    internal TextContext ArcadeTextContext => HudMessageTextContext;
}
