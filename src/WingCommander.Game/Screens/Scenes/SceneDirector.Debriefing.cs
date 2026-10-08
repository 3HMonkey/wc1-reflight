using WingCommander.Audio.Director;
using WingCommander.Core.Resources;
using WingCommander.Game.Scenes;

namespace WingCommander.Game.Screens.Scenes;

public sealed partial class SceneDirector
{
    /// <summary>Per-frame pan of the debriefing establishing shot (layers move by delta, delta+1, ...).</summary>
    /// <remarks>C: abDebriefingEstablishDeltas (0x0046E538).</remarks>
    private static ReadOnlySpan<sbyte> DebriefingEstablishDeltas =>
    [
        -2, -2, -1, -1, 0, 0, 1, 0,
        1, 0, 1, 0, 1, 0, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 0, 1, 0, 1,
        0, 1, 0, 1, 0, 0, -1, -1,
    ];

    // C: nDebriefingLeftX, nDebriefingPilotX, nDebriefingRightX, nDebriefingOfficerX, nDebriefingPodiumX.
    private short _debriefLeftX;
    private short _debriefPilotX = 80;
    private short _debriefRightX = 278;
    private short _debriefOfficerX = 200;
    private short _debriefPodiumX = 344;

    /// <summary>
    /// The mission debriefing: music by result (33 when the player scored more than 70 % of the
    /// mission's points, else 34), then the debriefing room scene of the flown mission.
    /// </summary>
    /// <remarks>C: DeBriefing (0x4056F0, cmpgn.c).</remarks>
    public async Task DebriefingAsync(int series, int mission)
    {
        Events.EscapePressed = false;
        int fullScore = Context.FullMissionScore();
        int playerScore = Context.PlayersMissionScore();
        if (fullScore == 0 || playerScore * 100 / fullScore > 70)
        {
            Stage.PreloadMusicTrack(MusicTrack.DebriefingSuccessful);
            Stage.SpaceTrack(MusicTrack.DebriefingSuccessful, 2, 1);
        }
        else
        {
            Stage.PreloadMusicTrack(MusicTrack.DebriefingUnsuccessful);
            Stage.SpaceTrack(MusicTrack.DebriefingUnsuccessful, 2, 1);
        }
        LoadMissionData(series, mission);
        Stage.InitializeConversationViewport();
        Stage.InitializeConversationText();
        Stage.ClearSubtitle();
        Gfx.SetTextContext(Stage.Text);
        var file = BriefingFile.Load(_game.Directory, Session.CampaignDataSet);
        _backdrop = Shape(LogicalFile.BriefingVga, 6);
        if (file.HasMission(series, mission))
            await RunAsync(SceneType.Debriefing, file.GetMission(series, mission).Debriefing);
        await Stage.PresentAsync();
        Events.EscapePressed = false;
        _backdrop = null;
        Stage.ResetScreenClipToFullHeight();
        Stage.StopMusicUnlessSuppressed();
    }

    /// <summary>The Colonel's office (promotion, new ship, ejection reprimand); music 36.</summary>
    /// <remarks>C: Office (0x405840, cmpgn.c).</remarks>
    public async Task OfficeAsync()
    {
        Events.EscapePressed = false;
        Stage.PreloadMusicTrack(MusicTrack.CommandersOffice);
        Stage.SpaceTrack(MusicTrack.CommandersOffice, 2, 1);
        Stage.InitializeConversationViewport();
        Stage.InitializeConversationText();
        var script = BriefingFile.Load(_game.Directory, Session.CampaignDataSet).GetOffice();
        _backdrop = Shape(LogicalFile.BriefingVga, 7);
        await RunAsync(SceneType.Office, script);
        await Stage.PresentAsync();
        Events.EscapePressed = false;
        _backdrop = null;
        Stage.ResetScreenClipToFullHeight();
        Stage.StopMusicUnlessSuppressed();
    }

    /// <summary>
    /// The debriefing room: wall, pilot bench, podium, the wingman (portrait 9 + personality, or
    /// Spirit's portrait 9 over the officer; nobody when the wingman is dead) and the right wall.
    /// Presents (before the scene is copied to the screen, like the original).
    /// </summary>
    /// <remarks>C: DrawDebriefingLongShot (0x437DC0, screens.c).</remarks>
    private async Task DrawDebriefingLongShotAsync()
    {
        short personality = _game.Flow.DebriefingPersonality;
        Gfx.DrawSpriteDefault(Scene, _debriefLeftX, 0, _backdrop, 2);
        Gfx.DrawSpriteDefault(Scene, _debriefLeftX + 320, 0, _backdrop, 3);
        Gfx.DrawSpriteDefault(Scene, _debriefPilotX - 1, 127, _backdrop, 4);
        Gfx.DrawSpriteDefault(Scene, _debriefPilotX, 127, _backdrop, 5);
        Gfx.DrawSpriteDefault(Scene, _debriefPodiumX, 127, _backdrop, 8);
        var deaths = Session.State.PersonalityDeathMission;
        if ((uint)personality >= (uint)deaths.Length || deaths[personality] == 0)
        {
            if (personality != 0)
                Gfx.DrawSpriteDefault(Scene, _debriefOfficerX, 32, _backdrop, personality + 9);
            Gfx.DrawSpriteDefault(Scene, _debriefOfficerX, 32, _backdrop, 6);
            if (personality == 0)
                Gfx.DrawSpriteDefault(Scene, _debriefOfficerX, 32, _backdrop, 9);
        }
        Gfx.DrawSpriteDefault(Scene, _debriefRightX, 127, _backdrop, 7);
        await Stage.PresentAsync();
    }

    /// <summary>Shot 10: the camera pans across the debriefing room (48 steps, layers at parallax speeds).</summary>
    /// <remarks>C: DebriefingEstablishingShot (0x437F20, screens.c).</remarks>
    private async Task DebriefingEstablishingShotAsync(string text, short duration)
    {
        _debriefPilotX = 80;
        _debriefRightX = 278;
        short frame = 0;
        _debriefLeftX = 0;
        _debriefOfficerX = 200;
        _debriefPodiumX = 344;
        ShowText(text);
        int frameSkipCounter = 1;
        while (true)
        {
            int delta = DebriefingEstablishDeltas[frame];
            _debriefLeftX = (short)(_debriefLeftX - Math.Max(delta, 0));
            _debriefPilotX = (short)(_debriefPilotX - Math.Max(delta + 1, 0));
            _debriefPodiumX = (short)(_debriefPodiumX - Math.Max(delta + 2, 0));
            _debriefOfficerX = (short)(_debriefOfficerX - Math.Max(delta + 3, 0));
            _debriefRightX = (short)(_debriefRightX - Math.Max(delta + 3, 0));
            if (frame == 47)
                frameSkipCounter = 1;
            frameSkipCounter--;
            if (frameSkipCounter < 1)
            {
                frameSkipCounter = FrameSkip;
                await DrawDebriefingLongShotAsync();
                await Stage.RefreshAsync();
                await Stage.PresentAsync();
            }
            if (Events.CheckEscaped() != 0)
            {
                duration = -1;
                break;
            }
            frame++;
            if (frame >= 48)
                break;
        }
        await Events.WaitForSceneAdvanceAsync(duration);
    }
}
