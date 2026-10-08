namespace WingCommander.Game.Flow;

/// <summary>
/// A canned 3D scene used by a 2D cutscene (the campaign endings): the flight layer set up the
/// action sphere and the scripted camera; every <see cref="Step"/> advances the simulation one
/// tick and draws the space view into the screen rows the cutscene reserved for it. Dispose ends
/// the scene and frees its 3D state.
/// </summary>
/// <remarks>C: set_up_action_sphere(n) + initialize_scripted_view, then per frame Update_3Space,
/// Draw_3Space_Frame and dump_buffer_to_screen; free_3Space at the end (screen.c endings).</remarks>
public interface ICannedSpaceScene : IDisposable
{
    /// <summary>One simulation tick and the space view drawn into the screen; false when the frame was skipped.</summary>
    bool Step();
}

/// <summary>Action spheres of the canned scenes (set_up_action_sphere arguments).</summary>
public static class CannedScene
{
    /// <summary>Title attract dogfight.</summary>
    public const int AttractDogfight = 16;

    /// <summary>Title attract asteroid flight with the credits.</summary>
    public const int AttractCredits = 17;

    /// <summary>The Tiger's Claw's final attack of the won Vega campaign (ShowCampaignVictorySequence).</summary>
    public const int CampaignVictory = 0x12;

    /// <summary>The Tiger's Claw's escape of the lost Vega campaign (ShowTigerClawEscapeScene).</summary>
    public const int TigerClawEscape = 0x13;
}
