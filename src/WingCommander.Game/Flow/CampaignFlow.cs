using WingCommander.Core.Numerics;
using WingCommander.Core.Resources;
using WingCommander.Game.Campaign;

namespace WingCommander.Game.Flow;

/// <summary>How a flight ended (the original's nArcadeState).</summary>
public enum FlightResult
{
    Running = 0,
    Landed = 1,
    Ejected = 2,
    Stranded = 3,
    Killed = 4,
    Aborted = 5,
}

/// <summary>Results of the barracks screen.</summary>
public static class BarracksResult
{
    public const int ExitToTitle = 6;
    public const int LaunchMission = 7;
    public const int ReturnToBar = 8;
}

/// <summary>
/// The screens and sequences the campaign flow drives. Each one is a coroutine that completes
/// when the screen is done (ADR-009); the implementations live with the screens, tests use fakes.
/// </summary>
public interface IGameFlowScreens
{
    /// <summary>Rec room; 5 = simulator, anything else = barracks door.</summary>
    Task<int> RecRoomAsync();

    /// <summary>TrainSim arcade (also the forced first session with name entry).</summary>
    Task RunTrainSimAsync();

    /// <summary>6 exit to title, 7 launch, 8 back to the bar.</summary>
    Task<int> BarracksScreenAsync();

    void PumpWindowMessages();

    Task BriefingAsync(int series, int mission);

    Task PlayScrambleHangarSceneAsync();

    /// <summary>Type of the player's ship in the mission just briefed (aMissionShips[nPlayerMissionShipIndex].type).</summary>
    int BriefedPlayerShipType { get; }

    Task ScrambleAsync();

    /// <summary>init_mission + LaunchPlayerShip + RunSpaceFlight(-1).</summary>
    Task<FlightResult> FlyMissionAsync(int series, int mission);

    /// <summary>free_cockpit, ShowCarrierLaunchSequence, free_3Space, landing(calculate_damage_level()).</summary>
    Task LandingSequenceAsync();

    /// <summary>ejection_sequence + check_stranded; true when stranded (then stranded_sequence ran).</summary>
    Task<bool> EjectionSequenceAsync();

    Task StrandedSequenceAsync();

    /// <summary>death_sequence, free_3Space, funeral_sequence(player).</summary>
    Task DeathSequenceAsync();

    /// <summary>Flight quit with Alt-X: free cockpit, slots and 3D space.</summary>
    void AbortFlight();

    /// <summary>Kill counts and the wingman of the mission just flown.</summary>
    MissionStatistics MissionStatistics { get; }

    /// <summary>achieved(objective) of the mission just flown.</summary>
    bool ObjectiveAchieved(int objective);

    Task DebriefingAsync(int series, int mission);

    Task AwardCampaignMedalAsync(int medal);

    Task CampaignVictorySequenceAsync();

    Task TigerClawEscapeSceneAsync();

    Task MeanwhileTransitionAsync(int sequence, bool seriesFailed);

    Task TheEndScreenAsync(bool fireworks);

    /// <summary>funeral_sequence(0): a wingman died.</summary>
    Task WingmanFuneralAsync();

    Task OfficeAsync();
}

/// <summary>
/// Campaign orchestration: new campaigns and one pass of the room/briefing/flight/debriefing
/// cycle with all of its post-mission bookkeeping, ported literally from nav.c.
/// </summary>
/// <remarks>C: StartNewCampaign (0x40F440), GameFlow (0x40F4B0).</remarks>
public sealed class CampaignFlow
{
    private readonly CampaignSession _session;
    private readonly IGameFlowScreens _screens;
    private readonly CRandom _random;
    private readonly GameDirectory _directory;
    private readonly Input.EventManager? _events;

    public CampaignFlow(CampaignSession session, IGameFlowScreens screens, CRandom random, GameDirectory directory,
        Input.EventManager? events = null)
    {
        _session = session;
        _screens = screens;
        _random = random;
        _directory = directory;
        _events = events;
    }

    /// <summary>Wingman shown in the debriefing long shot (series record +0).</summary>
    /// <remarks>C: nDebriefingPersonality.</remarks>
    public short DebriefingPersonality { get; private set; }

    /// <summary>The rec room fades in after a completed mission.</summary>
    /// <remarks>C: bPanRoomTransition.</remarks>
    public bool PanRoomTransition { get; set; }

    /// <remarks>C: StartNewCampaign (0x40F440).</remarks>
    public async Task StartNewCampaignAsync(int campaign)
    {
        _session.CampaignActive = true;
        _session.Reset();
        _session.CampaignStartupMode = true;
        await _screens.RunTrainSimAsync();
        _session.State.CampaignIndex = (short)campaign;
        _session.CampaignDataSet = (short)campaign;
        _session.CampaignStartupMode = false;
        _session.LoadCampaignData(_directory, campaign);
        PanRoomTransition = false;
        _session.PendingCampaignIndex = -1;
    }

    /// <summary>
    /// One campaign cycle. Returns true to run again, false to go back to the title (campaign
    /// over, player killed, stranded, abandoned).
    /// </summary>
    /// <remarks>C: GameFlow (0x40F4B0).</remarks>
    public async Task<bool> RunAsync()
    {
        var s = _session;
        var state = s.State;
        if (s.PendingCampaignIndex != -1)
        {
            state.CampaignIndex = s.PendingCampaignIndex;
            s.CampaignDataSet = s.PendingCampaignIndex;
        }

        // The rooms run without the virtual-key duplicates; briefing and flight with them.
        if (_events is not null)
            _events.KeyEventQueueEnabled = false;
        bool launch = false;
        do
        {
            int room = 0;
            s.PlayerEjectedThisMission = false;
            s.PostSeriesSequence = -1;
            s.PromotionPending = false;
            s.PendingMedalIndex = -1;
            s.OfficeVisitPending = false;
            s.PlayerShipTypeChanged = false;
            if (!s.CampaignStartupMode)
                room = await _screens.RecRoomAsync();
            PanRoomTransition = false;
            if (room == 5)
            {
                await _screens.RunTrainSimAsync();
            }
            else
            {
                int barracks = await _screens.BarracksScreenAsync();
                s.CampaignStartupMode = false;
                if (barracks == BarracksResult.ExitToTitle)
                    return false;
                if (barracks == BarracksResult.LaunchMission)
                    launch = true;
            }
            _screens.PumpWindowMessages();
        }
        while (!launch);

        if (_events is not null)
            _events.KeyEventQueueEnabled = true;
        state = s.State;
        var data = s.Data ?? throw new InvalidOperationException("No campaign data loaded.");
        DebriefingPersonality = data.GetSeries(state.CurrentSeries).DebriefPersonality;
        await _screens.BriefingAsync(state.CurrentSeries, state.CurrentMission);
        await _screens.PlayScrambleHangarSceneAsync();
        state.PlayerShipType = _screens.BriefedPlayerShipType;
        await _screens.ScrambleAsync();
        short flownSeries = state.CurrentSeries;
        short flownMission = state.CurrentMission;

        switch (await _screens.FlyMissionAsync(flownSeries, flownMission))
        {
            case FlightResult.Landed:
                await _screens.LandingSequenceAsync();
                break;
            case FlightResult.Ejected:
                if (await _screens.EjectionSequenceAsync())
                    return false;
                s.PlayerEjectedThisMission = true;
                state.PromotionScore = Math.Max((short)0, unchecked((short)(state.PromotionScore - 1)));
                state.ElapsedDate.Year++;
                if (state.ElapsedDate.Year == 1)
                    s.PendingMedalIndex = (short)Medal.GoldenSun;
                s.OfficeVisitPending = true;
                break;
            case FlightResult.Stranded:
                await _screens.StrandedSequenceAsync();
                return false;
            case FlightResult.Killed:
                await _screens.DeathSequenceAsync();
                s.CampaignActive = false;
                return false;
            default:
                _screens.AbortFlight();
                return false;
        }

        s.PostMission(_screens.MissionStatistics, _random);
        s.UpdateSeries(_screens.ObjectiveAchieved);
        sbyte nextSeries = state.CurrentSeries;
        sbyte nextMission = state.CurrentMission;
        state.CurrentSeries = (sbyte)flownSeries;
        state.CurrentMission = (sbyte)flownMission;

        if (!s.PlayerEjectedThisMission && (ushort)_random.InRange(0, 5) + state.PromotionScore > 7)
        {
            state.PromotionScore = 0;
            int rank = s.Player.Rank;
            s.PromotionPending = s.CampaignDataSet == 0 ? rank < 3 : s.CampaignDataSet > 0 && rank < 4;
            s.OfficeVisitPending = s.OfficeVisitPending || s.PromotionPending;
        }

        await _screens.DebriefingAsync(flownSeries, flownMission);
        if (s.PromotionPending)
            s.Player.Rank++;

        if (nextSeries == -1)
        {
            if (s.PendingMedalIndex != -1)
                await _screens.AwardCampaignMedalAsync(s.PendingMedalIndex);
            bool fireworks;
            if (s.PostSeriesSequence == -1)
            {
                fireworks = false;
            }
            else if (s.PostSeriesSequence == 0x40)
            {
                await _screens.CampaignVictorySequenceAsync();
                fireworks = true;
            }
            else if (s.PostSeriesSequence == 0x41)
            {
                await _screens.TigerClawEscapeSceneAsync();
                fireworks = false;
            }
            else
            {
                await _screens.MeanwhileTransitionAsync(s.PostSeriesSequence, s.SeriesFailed);
                fireworks = s.SeriesFailed;
            }
            await _screens.TheEndScreenAsync(fireworks);
            s.CampaignActive = false;
            return false;
        }

        if (s.WingmanKilledThisMission)
            await _screens.WingmanFuneralAsync();
        if (s.OfficeVisitPending)
            await _screens.OfficeAsync();
        if (s.PendingMedalIndex != -1)
        {
            await _screens.AwardCampaignMedalAsync(s.PendingMedalIndex);
            s.PendingMedalIndex = -1;
        }
        if (s.PostSeriesSequence != -1)
            await _screens.MeanwhileTransitionAsync(s.PostSeriesSequence, s.SeriesFailed);
        state.CurrentSeries = nextSeries;
        state.CurrentMission = nextMission;
        s.MoveNewCampaign(_random);
        s.HighScores.AddRandomScores(_random, state);
        PanRoomTransition = true;
        return true;
    }
}
