using WingCommander.Game.Campaign;
using WingCommander.Game.Input;
using WingCommander.Game.Scenes;
using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Scenes;

/// <summary>
/// The conversation engine and the cutscenes built on it. <see cref="RunAsync"/> plays one scene
/// script record by record: it applies the branch tests, prepares the record's camera shot (draws
/// the briefing room, loads a talking head, sets up the star field), parses the mouth and face
/// scripts and, when the record has a subtitle, runs the shot's handler (talking head, briefing
/// room animations, nav map, debriefing room, medal ceremony, funeral, MIDGAME animation). Any key
/// or button ends the current record, Esc the whole scene.
/// </summary>
/// <remarks>
/// <para>The fields are the original's conversation globals (pConversationBackdropShape,
/// pTalkingHeadShape, nConversationCharacter, nTalkingHeadFace, ...), kept for the lifetime of the
/// game like the C globals; every blocking call of the original is an await on the virtual clock
/// (ADR-009).</para>
/// C: SceneDirector (0x438C00), TalkerInit (0x438B90), FreeTalker (0x438BC0), screens.c; the shot
/// handlers in screens.c, cmpgn.c, brains.c, logic.c and nav.c (see the partial files).
/// </remarks>
public sealed partial class SceneDirector
{
    /// <summary>Scene types (nConversationSceneType); CloseTalk picks its background rule by them.</summary>
    public static class SceneType
    {
        public const short Briefing = 0;
        public const short Debriefing = 1;
        public const short RecRoom = 2;
        public const short Funeral = 3;
        public const short Office = 4;
        public const short MedalCeremony = 5;
        public const short Meanwhile = 6;
    }

    private const byte Black = PaletteColours.Black;

    private readonly Wc1Game _game;

    // C: pConversationBackdropShape, pTalkingHeadShape, pConversationOverlayShape,
    // pBriefingAnimationShape, pBriefingCloseupShape, pBriefingBodyShape, pBriefingPortraitShape,
    // pMedalSceneShape, pConversationSpecialShape, pIntroFont.
    private ShapeTable? _backdrop;
    private ShapeTable? _talkingHead;
    private ShapeTable? _overlay;
    private ShapeTable? _briefingAnimation;
    private ShapeTable? _briefingCloseup;
    private ShapeTable? _briefingBody;
    private ShapeTable? _briefingPortrait;
    private ShapeTable? _medalScene;
    private ShapeTable? _special;
    private ShapeTable? _introFont;

    /// <remarks>C: nConversationSceneType.</remarks>
    private short _sceneType;

    /// <remarks>C: nConversationCharacter.</remarks>
    private short _character = -1;

    /// <remarks>C: nTalkingHeadFace.</remarks>
    private short _talkingHeadFace = -1;

    /// <remarks>C: nConversationBackdropFrame.</remarks>
    private short _backdropFrame = -1;

    /// <remarks>C: bConversationOverlay.</remarks>
    private bool _overlayEnabled;

    /// <remarks>C: bConversationConstellation.</remarks>
    private bool _constellationEnabled;

    /// <remarks>C: nConversationTextColour.</remarks>
    private short _textColour;

    /// <remarks>C: nTalkingHeadFaceX/Y, nTalkingHeadMouthX/Y.</remarks>
    private short _faceX, _faceY, _mouthX, _mouthY;

    /// <summary>The star field (pConstellationShape + the field globals); null when freed.</summary>
    private ConstellationField? _constellation;

    /// <remarks>C: stConstellationViewport.</remarks>
    private readonly Viewport _constellationViewport = new();

    /// <summary>Parsed mouth and face scripts of the current record, -1 terminated pairs.</summary>
    /// <remarks>C: pMouthAnimationCommands, pFaceAnimationCommands (TalkerInit).</remarks>
    private short[] _mouthCommands = [-1];
    private short[] _faceCommands = [-1];

    public SceneDirector(Wc1Game game, IMissionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(outcome);
        _game = game;
        Stage = new ConversationStage(game);
        Context = new CampaignSceneContext(game.Session, outcome);
    }

    /// <summary>Viewports, subtitle strip and presentation helpers.</summary>
    public ConversationStage Stage { get; }

    /// <summary>Branch conditions and text macro values.</summary>
    public CampaignSceneContext Context { get; }

    public Wc1Game Game => _game;

    /// <summary>The mission data last loaded by <see cref="LoadMissionData"/>.</summary>
    public MissionBriefingData? Mission => Context.Mission;

    /// <summary>Number of records whose handler ran in the last scene (diagnostics and tests).</summary>
    public int RecordsPlayed => PlayedRecords.Count;

    /// <summary>The records whose handler ran in the last scene, in order (diagnostics and tests).</summary>
    public List<PlayedRecord> PlayedRecords { get; } = [];

    private GraphicsContext Gfx => _game.Graphics;

    private EventManager Events => _game.Events;

    private Viewport Scene => Stage.SceneBuffer;

    private CampaignSession Session => _game.Session;

    /// <summary>Loads the conversation shape of a logical file section (FetchDiskPacketRetrying).</summary>
    private ShapeTable? Shape(int logicalFile, int section)
    {
        var packet = _game.Resources.GetPacket(logicalFile);
        if (section >= packet.SectionCount || packet.GetInfo(section).StoredSize == 0)
            return null;
        return _game.Resources.GetShape(logicalFile, section);
    }

    /// <summary>
    /// Loads the mission's MODULE records (nav points, objectives, ships, names) for the scenes
    /// (the $S macro, the nav map, the objective count). Keeps the previous data when the slot is
    /// empty, like the SDL port's guard.
    /// </summary>
    /// <remarks>C: LoadMissionData (0x4059B0, cmpgn.c) + Build_objective_list (0x40CED0, brains.c).</remarks>
    public MissionBriefingData? LoadMissionData(int series, int mission)
    {
        var data = MissionBriefingData.Load(_game.Directory, Session.CampaignDataSet, series, mission);
        if (data is not null)
            Context.Mission = data;
        return data;
    }

    /// <summary>
    /// A conversation started by another screen (the rec room's talks). Like the original callers
    /// it runs in the conversation layout: screen rows 24..151 and the subtitle strip; when the
    /// caller has not allocated a scene buffer a 320x128 one is used. The screen clip, the scene
    /// buffer and the backdrop are restored afterwards.
    /// </summary>
    /// <remarks>C: the SceneDirector call sites in RecRoom (killbrd.c): stSceneBuffer.bottom = 127,
    /// stScreen rows 24..151, InitializeConversationText, pConversationBackdropShape = the room's
    /// conversation backdrop.</remarks>
    public async Task PlayConversationAsync(short sceneType, ConversationScript script, ShapeTable? backdrop)
    {
        ArgumentNullException.ThrowIfNull(script);
        var screen = Stage.Screen;
        short savedTop = screen.Top, savedBottom = screen.Bottom;
        bool ownBuffer = !Scene.IsAllocated;
        if (ownBuffer)
        {
            Scene.SetViewportRect(0, 0, 319, 127);
            Scene.AllocateViewport(Black);
        }
        screen.Top = ConversationStage.SceneTop;
        screen.Bottom = ConversationStage.SceneBottom;
        Stage.InitializeConversationText();
        var savedBackdrop = _backdrop;
        if (backdrop is not null)
            _backdrop = backdrop;
        await RunAsync(sceneType, script);
        _backdrop = savedBackdrop;
        if (ownBuffer)
            Scene.FreeViewport();
        screen.Top = savedTop;
        screen.Bottom = savedBottom;
    }

    /// <summary>Plays one conversation scene and returns when it ends (shot -2) or Esc was pressed.</summary>
    /// <remarks>C: SceneDirector (0x438C00, screens.c). The joystick pump the original installed
    /// (PollJoystickButtonEvents) is not ported; the pump is cleared at the end like the original.</remarks>
    public async Task RunAsync(short sceneType, ConversationScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        _sceneType = sceneType;
        short previousShot = -2;
        short previousColour = -2;
        PlayedRecords.Clear();
        TalkerInit();
        Events.InputMode = 1;
        Events.ClearInputKeyState();
        Events.FlushInputEvents();
        Events.EscapePressed = false;
        int index = 0;
        do
        {
            ConversationRecord record;
            short shot;
            int selected;
            do
            {
                record = script[index];
                shot = record.Shot;
                if (shot == ConversationRecord.EndOfScene)
                    goto SceneComplete;
                if (shot != ConversationRecord.KeepShot)
                {
                    if ((shot & ConversationRecord.OverlayFlag) != 0)
                    {
                        _overlayEnabled = true;
                        shot &= 0x3f;
                    }
                    else
                    {
                        _overlayEnabled = false;
                    }
                }
                selected = index;
                if (record.TestsOffset != 0)
                    index = script.EvaluateTests(index, Context);
            }
            while (selected != index);

            if (record.Talker != -2)
                _character = record.Talker;
            short duration = record.Duration;
            switch (shot & 0x3f)
            {
                case 0:
                case 3:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                case 11:
                case 16:
                case 17:
                    _talkingHeadFace = -1;
                    previousShot = shot;
                    break;
                case 1:
                    if (previousShot != 1)
                    {
                        previousShot = 1;
                        await DrawBriefingLongShotAsync();
                        _talkingHeadFace = -1;
                    }
                    break;
                case 2:
                    if (previousShot != 2)
                    {
                        previousShot = 2;
                        await DrawPodiumShotAsync();
                        _talkingHeadFace = -1;
                    }
                    break;
                case 4:
                    previousShot = 4;
                    if (Mission is { } mission)
                        mission.CurrentObjective = unchecked((sbyte)(_character < 0 ? -_character : _character));
                    _talkingHeadFace = -1;
                    break;
                case 12:
                case 13:
                case 14:
                case 15:
                    if (previousShot != shot)
                    {
                        _constellation ??= ConstellationField.Create(_game);
                        _constellationViewport.CopyFrom(Scene);
                        _constellation.Initialize(_constellationViewport, -1, 16);
                        _constellationEnabled = true;
                        _talkingHeadFace = -1;
                        previousShot = shot;
                    }
                    break;
                case >= 20 and <= 30:
                    if (previousShot != shot)
                    {
                        await LoadFaceAsync((short)(shot - 20));
                        previousShot = shot;
                    }
                    break;
                case >= 50 and <= 59:
                    previousShot = 50;
                    _talkingHeadFace = -1;
                    break;
            }

            if (previousColour != record.TextColour && record.TextColour != -1)
            {
                var colours = TextMacros.ConversationTextColours;
                _textColour = (uint)record.TextColour < (uint)colours.Length ? colours[record.TextColour] : _textColour;
                previousColour = record.TextColour;
            }
            _mouthCommands = ParseMouthCommands(script.GetBytes(record.MouthAnimationOffset));
            _faceCommands = ParseFaceCommands(script.GetBytes(record.FaceAnimationOffset));
            Events.FlushInputEvents();

            string text = script.GetText(record.TextOffset);
            if (text.Length != 0)
            {
                PlayedRecords.Add(new PlayedRecord(index, shot, previousShot, _game.Scheduler.Now));
                switch (previousShot)
                {
                    case 0:
                        await EstablishingShotAsync(text, duration);
                        break;
                    case 1:
                    case 2:
                    case 11:
                        await CloseLookAsync(_briefingCloseup, previousShot, _mouthCommands, text, duration);
                        break;
                    case 3:
                        previousShot = 4;
                        await DismissedAsync(text, duration);
                        break;
                    case 4:
                        await UpdateMapAsync(text, duration);
                        break;
                    case 5:
                        previousShot = 1;
                        await ReturnToBriefingLongShotAsync(text, duration);
                        break;
                    case 6:
                        await DrawMedalLongShotAsync(_mouthCommands, text, duration);
                        break;
                    case 7:
                        await MedalEstablishAsync(text, duration);
                        break;
                    case 8:
                        await PinMedalAsync(text, duration);
                        break;
                    case 9:
                    case 12:
                    case 13:
                    case 14:
                    case 15:
                        await DrawFuneralLongShotAsync(previousShot, text, duration);
                        break;
                    case 10:
                        await DebriefingEstablishingShotAsync(text, duration);
                        break;
                    case 16:
                        await DrawMedalChestAsync(text, duration);
                        break;
                    case 17:
                        await FuneralWingmanAsync(text, duration);
                        break;
                    case 50:
                        await PlaySceneAnimationAsync(text, (short)(shot - 50), duration);
                        break;
                    default:
                        await LongTalkAsync(_talkingHead, text, _mouthCommands, _faceCommands, duration);
                        break;
                }
            }
            index++;
        }
        while (!Events.EscapePressed);

    SceneComplete:
        Stage.ClearSubtitle();
        FreeTalker();
        Events.Pump = null;
        if (_constellationEnabled)
        {
            _constellation = null;
            _constellationEnabled = false;
        }
    }

    /// <summary>Resets the parsed animation scripts.</summary>
    /// <remarks>C: TalkerInit (0x438B90): allocated the two 0x140-byte command buffers.</remarks>
    private void TalkerInit()
    {
        _mouthCommands = [-1];
        _faceCommands = [-1];
    }

    /// <remarks>C: FreeTalker (0x438BC0).</remarks>
    private void FreeTalker()
    {
        _overlay = null;
        _talkingHead = null;
        _mouthCommands = [-1];
        _faceCommands = [-1];
    }

    /// <summary>Expands the subtitle macros and prints it in the current conversation colour.</summary>
    /// <remarks>C: AddPCName + ClearViewport(&amp;stConversationTextViewport) + FormatTextBufferFromStart(fmt, 0, 160, nConversationTextColour, szTextScratchBuffer).</remarks>
    private void ShowText(string text) => Stage.ShowSubtitle(TextMacros.Expand(text, Context), _textColour);

    /// <summary>Mouth script as the original's command buffer: (frame, ticks) pairs, -1 terminated.</summary>
    /// <remarks>C: ParseMouthAnimation (0x404D70, cmpgn.c), called only for a non-empty script.</remarks>
    internal static short[] ParseMouthCommands(ReadOnlySpan<byte> script) =>
        script.IsEmpty ? [-1] : ToCommands(AnimationScripts.ParseMouth(script));

    /// <summary>Face script as the original's command buffer; 'R' becomes (-2, entry index).</summary>
    /// <remarks>C: ParseFaceAnimation (0x404CD0, cmpgn.c), called only for a non-empty script.</remarks>
    internal static short[] ParseFaceCommands(ReadOnlySpan<byte> script) =>
        script.IsEmpty ? [-1] : ToCommands(AnimationScripts.ParseFace(script));

    private static short[] ToCommands(List<AnimationStep> steps)
    {
        var commands = new short[steps.Count * 2 + 1];
        for (int i = 0; i < steps.Count; i++)
        {
            commands[i * 2] = steps[i].Frame;
            commands[i * 2 + 1] = steps[i].Ticks;
        }
        commands[^1] = -1;
        return commands;
    }

    /// <summary>The value at a command position; past the end reads as the terminator.</summary>
    private static short At(short[] commands, int position) =>
        (uint)position < (uint)commands.Length ? commands[position] : (short)-1;
}
