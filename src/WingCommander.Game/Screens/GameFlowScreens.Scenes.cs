using WingCommander.Game.Scenes;
using WingCommander.Game.Screens.Scenes;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens;

public sealed partial class GameFlowScreens
{
    private SceneDirector? _director;

    /// <summary>
    /// The conversation engine and the cutscenes (created on first use and kept for the game, like
    /// the original's conversation globals). Other screens use it for <see cref="SceneDirector.ViewMedalsAsync"/>,
    /// <see cref="SceneDirector.LoadMissionData"/> and <see cref="SceneDirector.FuneralSequenceAsync"/>.
    /// </summary>
    public SceneDirector Director => _director ??= new SceneDirector(Game, new FlightOutcome(this));

    /// <summary>
    /// Plays one conversation (talking heads, backdrops, subtitles, its music and sound cues) and
    /// returns when it ends. The caller prepares the screen like the original callers do (for the
    /// rec room: screen rows 24..151 and the REC ROOM conversation backdrop); when the caller has
    /// not allocated a scene buffer, a 320x128 one is used for the conversation and the screen clip
    /// is restored afterwards. Keep this signature stable; new needs go into optional parameters.
    /// </summary>
    /// <param name="sceneType">nConversationSceneType: 0 briefing, 1 debriefing, 2 rec room, 3 funeral,
    /// 4 office, 5 medal ceremony, 6 MIDGAME ("Meanwhile").</param>
    /// <param name="script">Scene records and text of the conversation.</param>
    /// <param name="backdrop">The backdrop shape the original kept in pConversationBackdropShape, if the caller loaded one.</param>
    /// <remarks>C: SceneDirector (0x438C00, screens.c).</remarks>
    public Task PlayConversationAsync(int sceneType, ConversationScript script, ShapeTable? backdrop = null) =>
        Director.PlayConversationAsync((short)sceneType, script, backdrop);

    /// <remarks>C: Briefing (0x405660, cmpgn.c) + LoadBriefingRoom (0x436D00, screens.c).</remarks>
    public Task BriefingAsync(int series, int mission) => Director.BriefingAsync(series, mission);

    /// <remarks>C: PlayScrambleHangarScene (0x4079C0, brains.c).</remarks>
    public Task PlayScrambleHangarSceneAsync() => Director.PlayScrambleHangarSceneAsync();

    /// <remarks>C: DeBriefing (0x4056F0, cmpgn.c).</remarks>
    public Task DebriefingAsync(int series, int mission) => Director.DebriefingAsync(series, mission);

    /// <remarks>C: AwardCampaignMedal (0x436F50, screens.c).</remarks>
    public Task AwardCampaignMedalAsync(int medal) => Director.AwardCampaignMedalAsync(medal);

    /// <remarks>C: ShowCampaignVictorySequence (0x42FC00, screen.c); the 3D part is a placeholder (see SceneDirector).</remarks>
    public Task CampaignVictorySequenceAsync() => Director.CampaignVictorySequenceAsync();

    /// <remarks>C: ShowTigerClawEscapeScene (0x430150, screen.c); the 3D part is a placeholder (see SceneDirector).</remarks>
    public Task TigerClawEscapeSceneAsync() => Director.TigerClawEscapeSceneAsync();

    /// <remarks>C: ShowMeanwhileTransition (0x425770, pilot.cpp) (MIDGAME scenes).</remarks>
    public Task MeanwhileTransitionAsync(int sequence, bool seriesFailed) => Director.MeanwhileTransitionAsync(sequence, seriesFailed);

    /// <remarks>C: ShowTheEndScreen (0x4304F0, screen.c), which shows ViewMedals first.</remarks>
    public Task TheEndScreenAsync(bool fireworks) => Director.TheEndScreenAsync(fireworks);

    /// <remarks>C: funeral_sequence(0) (0x408DE0, brains.c).</remarks>
    public Task WingmanFuneralAsync() => Director.FuneralSequenceAsync(playerFuneral: false);

    /// <remarks>C: Office (0x405840, cmpgn.c).</remarks>
    public Task OfficeAsync() => Director.OfficeAsync();

    /// <summary>
    /// The mission results the conversation tests read, taken from the flight partial
    /// (<see cref="MissionStatistics"/>, <see cref="ObjectiveAchieved"/>, <see cref="ObjectiveSighted"/>).
    /// </summary>
    private sealed class FlightOutcome(GameFlowScreens screens) : IMissionOutcome
    {
        public int PlayerKills => screens.MissionStatistics.PlayerKills;

        public int WingmanKills => screens.MissionStatistics.WingmanKills;

        public bool Achieved(int objective) => screens.ObjectiveAchieved(objective);

        public bool Sighted(int objective) => screens.ObjectiveSighted(objective);
    }
}
