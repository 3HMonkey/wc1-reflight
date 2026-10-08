using WingCommander.Graphics.Raster;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// The flight engine's part of a TrainSim session, called by the TrainSim menus around
/// <see cref="GameFlowScreens.FlyTrainSimMissionAsync"/> (which runs <c>RunSpaceFlight</c>):
/// mission set-up before "Get Ready", the forced-session handicap after it, the space view the
/// captions are drawn into (Get Ready, Victory, Game Over) and the clean-up. The flight layer
/// provides it through <see cref="GameFlowScreens.TrainSimFlight"/> or by implementing it on its
/// <see cref="Wc1Game.FlightLayer"/>; until then <see cref="FallbackTrainSimFlight"/> shows an
/// empty simulator screen.
/// </summary>
/// <remarks>C: the flight calls of RunTrainSim (0x427080, system.c) and of ShowGetReadyScreen
/// (0x439840), ShowVictoryScreen (0x439910) and ShowGameOverScreen (0x439A80), screens.c:
/// RefreshCockpitStatus (0x42A0C0, hudmsg.c = Update_3Space + clear_view_buffer +
/// Draw_3Space_Frame), dump_buffer_to_screen (0x427A40, main.c), force_view, clear_view_buffer,
/// Explosion, set_eye_direction_and_position, generate_stars, FigureArcadeTime, init_mission,
/// set_up_next_wave, InvalidateVduMode, free_all_slots, free_cockpit, free_3Space; stSpaceBuffer,
/// nViewCenterX/Y, nCannedSceneMode, nFrameSkipCounter.</remarks>
public interface ITrainSimFlight
{
    /// <summary>The off-screen space view (stSpaceBuffer).</summary>
    Viewport SpaceBuffer { get; }

    /// <summary>Centre of the view in space buffer coordinates.</summary>
    /// <remarks>C: nViewCenterX.</remarks>
    short ViewCenterX { get; }

    /// <remarks>C: nViewCenterY.</remarks>
    short ViewCenterY { get; }

    /// <summary>Start of a session: <c>nCannedSceneMode = 0; ResetStringBuilder(&amp;stHudMessageTextContext)</c>.</summary>
    void BeginSession();

    /// <summary>Before "Get Ready": <c>FigureArcadeTime(); init_mission(0, mission)</c>.</summary>
    void InitializeMission(short mission);

    /// <summary>
    /// After "Get Ready", before the flight: in the forced first session of a campaign the
    /// handicap (no shields, component 2 damaged, hull one point over capacity, wave 2,
    /// <c>set_up_next_wave()</c>, 25 seconds); then <c>InvalidateVduMode(0); InvalidateVduMode(1)</c>.
    /// </summary>
    void PrepareFlight(bool campaignStartup);

    /// <summary>End of a session: <c>free_all_slots(); free_cockpit(); free_3Space()</c>.</summary>
    void EndSession();

    /// <summary>Get Ready set-up: <c>nCannedSceneMode = 1; force_view(0, 0); nFrameSkipCounter = 1</c>.</summary>
    void BeginGetReady();

    /// <summary>After Get Ready: <c>clear_view_buffer(); nCannedSceneMode = 0; ResetSoundState()</c>.</summary>
    void EndGetReady();

    /// <summary>Victory set-up: <c>nFrameSkipCounter = 1</c>.</summary>
    void BeginVictory();

    /// <summary>
    /// Game Over set-up: the player's ship explodes (<c>Explosion(0)</c>), an external camera 300
    /// units behind it, <c>generate_stars()</c>, <c>nFrameSkipCounter = 1</c>.
    /// </summary>
    void BeginGameOver();

    /// <summary>Renders the next space frame into <see cref="SpaceBuffer"/>; false when the frame is skipped.</summary>
    /// <remarks>C: RefreshCockpitStatus (0x42A0C0, hudmsg.c).</remarks>
    bool RefreshCockpitStatus();

    /// <summary>Composites the space buffer onto the screen through the cockpit mask.</summary>
    /// <remarks>C: dump_buffer_to_screen (0x427A40, main.c).</remarks>
    void DumpBufferToScreen();
}
