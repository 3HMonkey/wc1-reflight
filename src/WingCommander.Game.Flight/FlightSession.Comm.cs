using System.Text;
using WingCommander.Core.Resources;
using WingCommander.Game.Flight.Cockpit;
using WingCommander.Game.Scenes;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// Communication: pilot speech (COMMUNIC.DAT), the comm menu on the right VDU, wingman orders,
// transmissions with portraits (comm video) and the NPC chatter (screen.c, cockpt.c, brains.c).
// ADR-012: npc_communication, vid_equiv and real_vid_transmit are UI; the simulation only queues
// lines in Ships[].WingmanMessageState and executes the orders (Request).
internal sealed partial class FlightSession
{
    private const int FaceCount = 14;
    private const int LinesPerFace = 11;
    private const int LineSize = 80;

    /// <remarks>C: aapszPilotSpeech[14][11].</remarks>
    private readonly string?[][] _pilotSpeech = CreateSpeechTable();

    /// <summary>Which portrait shapes are loaded (start-up loads the wingmen 0..7; the aces and the generic
    /// Kilrathi are loaded on demand; the generic Confed face 12 has none).</summary>
    /// <remarks>C: apCommPortraitShapes[face] != 0.</remarks>
    private readonly bool[] _commPortraitLoaded = [true, true, true, true, true, true, true, true, false, false, false, false, false, false];

    /// <remarks>C: pConfedCommBackground / pKilrathiCommBackground / pCommStaticShape != 0 (loaded at start-up,
    /// freed after every transmission, reloaded by LoadCommDisplayResources).</remarks>
    private bool _confedBackgroundLoaded = true;
    private bool _kilrathiBackgroundLoaded = true;
    private bool _commStaticLoaded = true;

    /// <summary>The object speaking (0 at start-up, like the original global; -1 = nobody).</summary>
    /// <remarks>C: nCommSpeakerObject.</remarks>
    private short _commSpeakerObject;

    /// <remarks>C: nCommSpeakerRating.</remarks>
    private short _commSpeakerRating;

    /// <remarks>C: nCommPortraitIndex (-1 = none).</remarks>
    private short _commPortraitIndex = -1;

    /// <remarks>C: nCommPortraitFrame (-1 = not chosen yet).</remarks>
    private int _commPortraitFrame = -1;

    /// <remarks>C: bVideoImagesSuppressed (the V key).</remarks>
    public bool VideoImagesSuppressed { get; set; }

    /// <remarks>C: bCommVideoEnabled.</remarks>
    private bool _commVideoEnabled = true;

    /// <remarks>C: apszCommMenuChoiceText[7], abCommMenuChoiceCommand[7], nCommMenuChoiceCount (-1 initially),
    /// nCommMenuReuseMode, cPendingCommMenuAction (1), cCommMenuRecipient (-1), pszCommMenuHeading.</remarks>
    private readonly string?[] _commMenuChoiceText = new string?[7];
    private readonly sbyte[] _commMenuChoiceCommand = [-1, -1, -1, -1, -1, -1, -1];
    private short _commMenuChoiceCount = -1;
    private short _commMenuReuseMode;
    private sbyte _pendingCommMenuAction = 1;
    private sbyte _commMenuRecipient = -1;
    private string _commMenuHeading = "";

    /// <summary>Number of entries of the open comm menu (tests).</summary>
    public int CommMenuChoiceCount => _commMenuChoiceCount;

    /// <summary>Text of a comm menu entry (tests).</summary>
    public string? GetCommMenuChoice(int choice) => _commMenuChoiceText[choice];

    private static string?[][] CreateSpeechTable()
    {
        var table = new string?[FaceCount][];
        for (int face = 0; face < FaceCount; face++)
            table[face] = new string?[LinesPerFace];
        return table;
    }

    /// <summary>Portrait index of a speaker: unrated pilots 12 (Confed) / 13 (Kilrathi), Kilrathi aces rating - 1.</summary>
    /// <remarks>C: get_face (0x430BC0, screen.c).</remarks>
    private static short GetFace(int rating, Side side)
    {
        if (rating == -1)
            return (short)(13 + (side < Side.Kilrathi ? -1 : 0));
        if (side == Side.Kilrathi)
            rating--;
        return (short)rating;
    }

    /// <summary>Section of WINGMEN.VGA holding a face's portrait (-1 = none).</summary>
    /// <remarks>C: LoadCommPortraitShape (0x430BF0, screen.c).</remarks>
    private static int PortraitSection(int face) => face switch
    {
        >= 0 and <= 7 => face + 1,
        (> 7 and < 12) or 13 => 10,
        _ => -1,
    };

    /// <remarks>C: LoadCommPortraitShape (0x430BF0, screen.c).</remarks>
    private void LoadCommPortraitShape(int face)
    {
        int section = PortraitSection(face);
        if (section != -1 && (uint)face < FaceCount)
            _commPortraitLoaded[face] = Shapes.Exists(LogicalFile.WingmenVga, section);
    }

    private ShapeTable? CommPortrait(int face)
    {
        if ((uint)face >= FaceCount || !_commPortraitLoaded[face])
            return null;
        int section = PortraitSection(face);
        return section == -1 ? null : Shapes.Get(LogicalFile.WingmenVga, section);
    }

    /// <summary>Reads the 11 speech lines of a face (80-byte records of COMMUNIC.DAT) and loads its portrait.</summary>
    /// <remarks>C: get_pilot_talk (0x40C220, brains.c).</remarks>
    private void GetPilotTalk(int face)
    {
        if ((uint)face >= FaceCount)
            return;
        var data = CommunicData;
        for (int line = 0; line < LinesPerFace; line++)
        {
            int offset = (face * LinesPerFace + line) * LineSize;
            if (offset + LineSize > data.Length)
            {
                _pilotSpeech[face][line] = null;
                continue;
            }
            var record = data.AsSpan(offset, LineSize);
            int end = record.IndexOf((byte)0);
            _pilotSpeech[face][line] = Encoding.Latin1.GetString(end < 0 ? record : record[..end]);
        }
        LoadCommPortraitShape(face);
    }

    private byte[]? _communicData;

    /// <summary>COMMUNIC.DAT (logical file 13): 14 faces x 11 lines x 80 bytes.</summary>
    private byte[] CommunicData
    {
        get
        {
            if (_communicData is null)
            {
                var directory = Game.Directory;
                _communicData = directory.InstallTable.TryGet(LogicalFile.CommunicDat, out var record) && directory.Exists(record.Name)
                    ? directory.ReadFile(record.Name)
                    : [];
            }
            return _communicData;
        }
    }

    /// <summary>Loads the speech of every pilot in the mission (and prepares the Kilrathi aces) plus both generic faces.</summary>
    /// <remarks>C: init_personalities (0x40C2B0, brains.c).</remarks>
    private void InitializePersonalities()
    {
        var sim = Sim;
        for (int missionShip = 0; missionShip < 32; missionShip++)
        {
            int personality = sim.MissionShips[missionShip].Pilot - 5;
            if (personality is >= 0 and < 8)
                GetPilotTalk(GetFace(personality, Side.Imperial));
            if (personality > 8)
            {
                GetPilotTalk(GetFace(personality, Side.Kilrathi));
                sim.PrepareAce((short)(personality - 9));
            }
        }
        GetPilotTalk(GetFace(-1, Side.Kilrathi));
        GetPilotTalk(GetFace(-1, Side.Imperial));
    }

    // ------------------------------------------------------------------ transmissions

    /// <summary>Loads the comm background of the speaker's side and the static shape.</summary>
    /// <remarks>C: LoadCommDisplayResources (0x431520, screen.c).</remarks>
    private bool LoadCommDisplayResources(Side side)
    {
        bool loaded = true;
        switch (side)
        {
            case Side.Imperial:
                _confedBackgroundLoaded = _confedBackgroundLoaded || Shapes.Exists(LogicalFile.WingmenVga, 0);
                loaded = _confedBackgroundLoaded;
                break;
            case Side.Kilrathi:
                _kilrathiBackgroundLoaded = _kilrathiBackgroundLoaded || Shapes.Exists(LogicalFile.WingmenVga, 9);
                loaded = _kilrathiBackgroundLoaded;
                break;
        }
        _commStaticLoaded = _commStaticLoaded || Shapes.Exists(LogicalFile.WingmenVga, 11);
        return loaded && _commStaticLoaded;
    }

    /// <summary>Ends the speaker state and releases the transmission's shapes.</summary>
    /// <remarks>C: FreeCommDisplayResources (0x431410, screen.c), with the SDL port's guard for index -1.</remarks>
    private void FreeCommDisplayResources()
    {
        if (_commPortraitIndex != -1 && _commPortraitIndex < FaceCount)
            _commPortraitLoaded[_commPortraitIndex] = false;
        _confedBackgroundLoaded = false;
        _kilrathiBackgroundLoaded = false;
        _commStaticLoaded = false;
        _commSpeakerRating = -1;
        _commSpeakerObject = -1;
        _commPortraitIndex = -1;
    }

    /// <summary>Static on the right VDU (when a portrait showed), release, pop the comm video page.</summary>
    /// <remarks>C: EndCommSessionWithWingman (0x431470, screen.c).</remarks>
    private void EndCommSessionWithWingman()
    {
        if (_commPortraitIndex != -1 && CommPortrait(_commPortraitIndex) is not null)
            MalfNoise(1, 1, 12, 23, true);
        FreeCommDisplayResources();
        if (GetVduMode(1) == 6)
            PopMode(1);
    }

    /// <summary>Ends the HUD message (and a running transmission).</summary>
    /// <remarks>C: EndCommMenu (0x4314C0, screen.c).</remarks>
    private void EndCommMenu()
    {
        ClearMessageTime();
        if (GetVduMode(1) == 6)
            EndCommSessionWithWingman();
        _pendingHudMessage = null;
    }

    /// <summary>A speech line on the HUD line in yellow.</summary>
    /// <remarks>C: ShowCentredPrompt (0x4314F0, screen.c).</remarks>
    private void ShowCentredPrompt(string text, short duration)
    {
        _hudMessageBuffer.Value = text;
        SetHudMessageText(_hudMessageBuffer, PaletteColours.Yellow, duration);
    }

    /// <summary>Replaces $C (callsign), $N/$P (name) and $R (rank, a doubled '.' removed) in a speech line.</summary>
    /// <remarks>C: ExpandCommMessageTokens (0x4315C0, screen.c).</remarks>
    private string ExpandCommMessageTokens(string text)
    {
        var player = Game.Session.Player;
        var result = new StringBuilder();
        int position = 0;
        while (true)
        {
            int marker = text.IndexOf('$', position);
            if (marker < 0)
            {
                result.Append(text, position, text.Length - position);
                return result.ToString();
            }
            result.Append(text, position, marker - position);
            char token = marker + 1 < text.Length ? text[marker + 1] : '\0';
            position = Math.Min(marker + 2, text.Length);
            switch (token)
            {
                case 'C':
                    result.Append(player.Callsign);
                    break;
                case 'N':
                case 'P':
                    result.Append(player.Name);
                    break;
                case 'R':
                    result.Append((uint)player.Rank < (uint)TextMacros.RankNames.Length ? TextMacros.RankNames[player.Rank] : "");
                    if (result.Length > 0 && result[^1] == '.' && position < text.Length && text[position] == '.')
                        result.Length--;
                    break;
            }
        }
    }

    /// <summary>
    /// A ship speaks line <paramref name="message"/>: with comm video the right VDU switches to the
    /// speaker's portrait (static first), and "name: text" shows in yellow on the HUD line.
    /// </summary>
    /// <remarks>C: real_vid_transmit (0x4316E0, screen.c).</remarks>
    private void RealVidTransmit(short obj, short message)
    {
        var sim = Sim;
        _commSpeakerObject = obj;
        _commSpeakerRating = sim.Ships[obj].Rating;
        Side side = sim.Ships[obj].Side;
        _commPortraitIndex = GetFace(_commSpeakerRating, side);
        if (_commPortraitIndex == -1)
            return;
        if (_commVideoEnabled && !VideoImagesSuppressed)
        {
            if (CommPortrait(_commPortraitIndex) is null)
                LoadCommPortraitShape(_commPortraitIndex);
            if (CommPortrait(_commPortraitIndex) is { } portrait && LoadCommDisplayResources(sim.Ships[_commSpeakerObject].Side))
            {
                PushMode(1, 6);
                MalfNoise(1, 3, 12, 23, true);
                var background = sim.Ships[_commSpeakerObject].Side == Side.Imperial
                    ? Shapes.Get(LogicalFile.WingmenVga, 0)
                    : Shapes.Get(LogicalFile.WingmenVga, 9);
                Gfx.DrawSpriteDefault(RightVdu, RightVdu.Left, RightVdu.Top, background, 0);
                Gfx.DrawSpriteDefault(RightVdu, RightVdu.Left, RightVdu.Top, portrait, 0);
            }
        }
        string speech = (uint)_commPortraitIndex < FaceCount && (uint)message < LinesPerFace
            ? _pilotSpeech[_commPortraitIndex][message] ?? ""
            : "";
        string text;
        if (_commSpeakerRating is >= 0 and <= 7)
            text = $"{WingmanCallsign(_commSpeakerRating)}: {speech}";
        else if (_commSpeakerRating is >= 9 and <= 12)
            text = $"{CockpitTables.KilrathiAceNames[_commSpeakerRating - 9]}: {speech}";
        else
            text = $"{ObjectTypeTable.Get(sim.Objects[obj].Type).DisplayName}: {speech}";
        if (text.Length > 83)
            text = text[..83];
        ShowCentredPrompt(ExpandCommMessageTokens(text), MeasureMessageWidth(text));
    }

    /// <summary>
    /// Every tick on the comm video page: a neutral speaker ends the session; on drawn frames
    /// (cockpitless, or odd rendered frames) the portrait gets a random mouth frame (or the
    /// static of a dying speaker).
    /// </summary>
    /// <remarks>C: vid_transmit (0x417910, cockpt.c); random draws RandomInRange(0, 2) once per session
    /// and RandomInRange(0, 3) per drawn frame.</remarks>
    private void VidTransmit()
    {
        var sim = Sim;
        short speaker = _commSpeakerObject;
        if ((uint)speaker >= ObjectSlots.ShipSlotCount || sim.Ships[speaker].Side == Side.Neutral)
        {
            EndCommSessionWithWingman();
            return;
        }
        // RE-CHECK: the original also tests aapszPilotSpeech[index] != 0, which is the address of a
        // char*[11] row and therefore always true.
        if ((sim.CockpitlessView != 0 || sim.RenderedSpaceFrame % 2 != 0) && _commPortraitIndex != -1 &&
            !VideoImagesSuppressed)
        {
            var staticShape = _commStaticLoaded ? Shapes.Get(LogicalFile.WingmenVga, 11) : null;
            if (sim.Ships[speaker].SpecialManeuver == SpecialManeuver.Unknown9)
            {
                if (sim.Ships[speaker].Side == Side.Imperial)
                    Gfx.DrawSpriteDefault(RightVdu, RightVdu.Left, RightVdu.Top, staticShape, sim.Objects[speaker].Counter / 5);
                else
                    Gfx.DrawSpriteDefault(RightVdu, RightVdu.Left, RightVdu.Top, staticShape, 2);
                return;
            }
            if (_commPortraitFrame == -1)
                _commPortraitFrame = (ushort)sim.Random.InRange(0, 2);
            short randomFrame = sim.Random.InRange(0, 3);
            if (randomFrame < 3)
                _commPortraitFrame = randomFrame;
            SetNewVdu(1);
            bool confed = sim.Ships[_commSpeakerObject].Side == Side.Imperial;
            var background = confed
                ? (_confedBackgroundLoaded ? Shapes.Get(LogicalFile.WingmenVga, 0) : null)
                : (_kilrathiBackgroundLoaded ? Shapes.Get(LogicalFile.WingmenVga, 9) : null);
            Gfx.DrawSpriteDefault(RightVdu, RightVdu.Left, RightVdu.Top, background, 0);
            Gfx.DrawSpriteDefault(RightVdu, RightVdu.Left, RightVdu.Top, CommPortrait(_commPortraitIndex), _commPortraitFrame);
        }
    }

    /// <summary>A queued line is spoken when the comm menu is closed, in the front view and nothing else talks.</summary>
    /// <remarks>C: vid_equiv (0x417AC0, cockpt.c).</remarks>
    private void VidEquiv(short obj, short message)
    {
        var sim = Sim;
        if (GetVduMode(1) != 4 && !sim.TrainSimActive && sim.CannedSceneMode == 0 && sim.CameraViewMode == 0 && !MessageShowing())
            RealVidTransmit(obj, message);
    }

    /// <summary>
    /// The chatter of the cockpit view: queued lines of ships 1..9 are spoken (one per free message
    /// line), then rarely an engaging Kilrathi queues a taunt.
    /// </summary>
    /// <remarks>C: npc_communication (0x4174F0, cockpt.c). Draws RandomBelowOrEqual(5000) on every call,
    /// RandomBelowOrEqual(100) per unrated engaging Kilrathi and RandomBelowOrEqual(2) for the taunt.</remarks>
    private void NpcCommunication()
    {
        var sim = Sim;
        if (sim.CannedSceneMode != 0 || sim.TrainSimActive)
            return;
        bool messageActive = MessageShowing();
        for (short obj = 1; !messageActive && obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (sim.Objects[obj].Class >= ObjectClass.Ship && sim.Ships[obj].WingmanMessageState != -1)
            {
                sbyte message = sim.Ships[obj].WingmanMessageState;
                VidEquiv(obj, message);
                sim.Ships[obj].WingmanMessageState = -1;
            }
            messageActive = MessageShowing();
        }
        if (sim.Random.BelowOrEqual(5000) > 4998 && _commSpeakerObject == -1)
        {
            for (short obj = 1; obj < ObjectSlots.ShipSlotCount;)
            {
                ref readonly var ship = ref sim.Ships[obj];
                if (sim.Objects[obj].Class >= ObjectClass.Ship && ship.Side == Side.Kilrathi &&
                    (ship.Objective == ShipObjective.EngageEnemy || ship.Objective == ShipObjective.DestroyShip) &&
                    (ship.Rating != -1 || sim.Random.BelowOrEqual(100) < 20))
                {
                    sim.Ships[obj].WingmanMessageState = (sbyte)(sim.Random.BelowOrEqual(2) + 2);
                    return;
                }
                obj++;
                if (_commSpeakerObject != -1)
                    return;
            }
        }
    }

    // ------------------------------------------------------------------ the comm menu

    /// <remarks>C: ResetCommMenuChoices (0x430C50, screen.c).</remarks>
    private void ResetCommMenuChoices(short reuse)
    {
        if (reuse == 0)
        {
            Array.Fill(_commMenuChoiceCommand, (sbyte)-1);
            Array.Clear(_commMenuChoiceText);
        }
        _commMenuChoiceCount = 0;
        _commMenuReuseMode = reuse;
    }

    /// <remarks>C: AppendCommMenuChoice (0x430CB0, screen.c); a changed entry ends the reuse mode (redraw).</remarks>
    private void AppendCommMenuChoice(string text, short command)
    {
        int index = _commMenuChoiceCount;
        if (index >= _commMenuChoiceText.Length)
            return;
        if (_commMenuReuseMode == 1 &&
            (!ReferenceEquals(_commMenuChoiceText[index], text) || _commMenuChoiceCommand[index] != command))
            _commMenuReuseMode = 0;
        _commMenuChoiceText[index] = text;
        _commMenuChoiceCount = (short)(index + 1);
        _commMenuChoiceCommand[index] = (sbyte)command;
    }

    /// <remarks>C: SendCommMenuChoice (0x430D30, screen.c).</remarks>
    private void SendCommMenuChoice(int command) => AppendCommMenuChoice(CockpitTables.CommMenuText[command], (short)command);

    /// <remarks>C: OpenCommMenuForTarget (0x430D50, screen.c).</remarks>
    private void OpenCommMenuForTarget(string heading, HudText message)
    {
        CockpitMessage(message, PaletteColours.Yellow, -1);
        _commMenuHeading = heading;
    }

    /// <remarks>C: IsCommChoiceMenuOpen (0x430D80, screen.c).</remarks>
    private bool IsCommChoiceMenuOpen() => GetVduMode(1) == 4;

    /// <remarks>C: CloseCommChoiceMenu (0x430DE0, screen.c); the original exited ("!stop") when no menu
    /// was open, which no caller allows.</remarks>
    private void CloseCommChoiceMenu()
    {
        if (GetVduMode(1) == 4)
            PopMode(1);
    }

    /// <remarks>C: CanOpenCommMenu (0x430E50, screen.c): a live target or a wingman.</remarks>
    private bool CanOpenCommMenu() => Sim.HaveTarget() || !Sim.WingmanDead();

    /// <remarks>C: SelectCommRecipient (0x430E70, screen.c).</remarks>
    private void SelectCommRecipient(int recipient)
    {
        _commMenuRecipient = (sbyte)recipient;
        _pendingCommMenuAction = 2;
    }

    private static readonly string EnemyTargetText = "ENEMY TARGET";

    /// <summary>"Send message to?": the wingman, the enemy target or a friendly target, "Never mind".</summary>
    /// <remarks>C: BuildCommunicationRecipientMenu (0x430E90, screen.c).</remarks>
    private void BuildCommunicationRecipientMenu()
    {
        var sim = Sim;
        ResetCommMenuChoices(_commMenuReuseMode);
        OpenCommMenuForTarget("VID-COM SYSTEM\n\nSend message to?\n\n", CommSelectText);
        if (sim.WingmanDead())
        {
            SelectCommRecipient(sim.Ships[ObjectSlots.Player].Target);
            return;
        }
        if (!sim.HaveTarget() || sim.Ships[ObjectSlots.Player].Target == sim.YourWingman)
        {
            SelectCommRecipient(sim.YourWingman);
            return;
        }
        AppendCommMenuChoice(WingmanCallsign(sim.Ships[sim.YourWingman].Rating), 1);
        int target = sim.Ships[ObjectSlots.Player].Target;
        if (target != -1)
        {
            if (sim.Ships[target].Side == Side.Kilrathi && sim.Objects[target].Class == ObjectClass.Ship)
            {
                AppendCommMenuChoice(EnemyTargetText, 2);
            }
            else if (sim.Ships[target].Side == Side.Imperial &&
                     ((sim.Objects[target].Class == ObjectClass.Ship && sim.AnyEnemy(ObjectSlots.Player, 14000)) ||
                      sim.Objects[target].Type == ObjectType.TigersClaw))
            {
                AppendCommMenuChoice(ObjectTypeTable.Get(sim.Objects[target].Type).DisplayName, 3);
            }
        }
        SendCommMenuChoice(0);
    }

    /// <summary>The orders available for the chosen recipient (formation, engage, radio silence, landing,
    /// attack, help, return, taunts), then "Never mind"; an empty menu closes.</summary>
    /// <remarks>C: BuildCommunicationCommandMenu (0x430FC0, screen.c).</remarks>
    private void BuildCommunicationCommandMenu()
    {
        var sim = Sim;
        ResetCommMenuChoices(_commMenuReuseMode);
        int recipient = _commMenuRecipient;
        if (recipient == sim.YourWingman)
        {
            if (sim.Ships[sim.YourWingman].Objective == ShipObjective.HoldFormation && sim.AnyEnemy(ObjectSlots.Player, 14000))
                SendCommMenuChoice(7);
            if (sim.AutoEngageTimer == -1)
            {
                if (sim.Ships[sim.YourWingman].Objective != ShipObjective.HoldFormation)
                    SendCommMenuChoice(9);
            }
            else
            {
                SendCommMenuChoice(8);
            }
            SendCommMenuChoice(sim.RadioSilence ? 11 : 10);
        }
        bool validRecipient = (uint)recipient < ObjectSlots.ShipSlotCount;
        if (validRecipient && sim.Ships[recipient].Side == sim.Ships[ObjectSlots.Player].Side)
        {
            if (sim.Objects[recipient].Type == ObjectType.TigersClaw && !sim.LandingAuthorized)
                SendCommMenuChoice(12);
            if (sim.HaveTarget() && sim.Ships[sim.Ships[ObjectSlots.Player].Target].Side == Side.Kilrathi)
                SendCommMenuChoice(1);
            if (sim.EvaluateDamage(ObjectSlots.Player) < 50 && sim.AnyEnemy(ObjectSlots.Player, 14000))
                SendCommMenuChoice(2);
        }
        if (recipient == sim.YourWingman)
            SendCommMenuChoice(3);
        if (validRecipient && sim.Ships[recipient].Side == Side.Kilrathi)
        {
            SendCommMenuChoice(4);
            SendCommMenuChoice(5);
            SendCommMenuChoice(6);
        }
        if (_commMenuChoiceCount != 0)
            SendCommMenuChoice(0);
        else
            CloseCommChoiceMenu();

        if (IsCommChoiceMenuOpen())
        {
            int rating = validRecipient ? sim.Ships[recipient].Rating : -1;
            string name;
            if (rating == -1)
                name = validRecipient ? ObjectTypeTable.Get(sim.Objects[recipient].Type).DisplayName : "";
            else if (rating < 8)
                name = WingmanCallsign(rating);
            else
                name = rating - 9 is >= 0 and < 4 ? CockpitTables.KilrathiAceNames[rating - 9] : "";
            OpenCommMenuForTarget("VID-COM SYSTEM\n\nTo: " + name + "\n", CommChooseText);
        }
    }

    /// <summary>Rebuilds the open menu; a changed menu is redrawn.</summary>
    /// <remarks>C: RefreshCommunicationMenu (0x431200, screen.c).</remarks>
    private void RefreshCommunicationMenu()
    {
        if (!IsCommChoiceMenuOpen())
            return;
        if (_pendingCommMenuAction == 1)
            BuildCommunicationRecipientMenu();
        if (_pendingCommMenuAction == 2)
            BuildCommunicationCommandMenu();
        if (_commMenuReuseMode == 0)
            InvalidateVduMode(1);
    }

    /// <remarks>C: HandleCommunicationMenuRequest (0x431240, screen.c).</remarks>
    private void HandleCommunicationMenuRequest()
    {
        if (IsCommChoiceMenuOpen())
            CloseCommChoiceMenu();
        if (!MessageShowing() && !IsCommChoiceMenuOpen() && CanOpenCommMenu())
        {
            PushMode(1, 4);
            _pendingCommMenuAction = 1;
            ResetCommMenuChoices(0);
            RefreshCommunicationMenu();
        }
    }

    /// <summary>The comm menu page: heading and numbered choices (the cursor shape of the original was never loaded).</summary>
    /// <remarks>C: show_communications_disp (0x431290, screen.c).</remarks>
    private void ShowCommunicationsDisplay()
    {
        if (!IsCommChoiceMenuOpen())
            HandleCommunicationMenuRequest();
        if (!IsCommChoiceMenuOpen())
            return;
        SetNewVdu(1);
        Gfx.DrawTextAt(RightVduTextContext, RightVdu.Left, RightVdu.Top, _commMenuHeading, TextContext.AlignCentre);
        for (int choice = 0; choice < _commMenuChoiceCount; choice++)
            Gfx.DrawFormattedText("\n%d %s", choice + 1, _commMenuChoiceText[choice] ?? "");
        _commMenuReuseMode = 1;
    }

    /// <summary>A number key in the comm menu: choose the recipient, or send the order (the simulation's request).</summary>
    /// <remarks>C: Chosen_communicate_option (0x431350, screen.c). The key test accepts one entry past the
    /// list, whose command reads -1 (literal, ADR-012).</remarks>
    private void ChosenCommunicateOption(int choice)
    {
        var sim = Sim;
        Audio.PlaySfx(0x19);
        sbyte command = (uint)choice < (uint)_commMenuChoiceCommand.Length ? _commMenuChoiceCommand[choice] : (sbyte)-1;
        switch (_pendingCommMenuAction)
        {
            case 0:
                CloseCommChoiceMenu();
                return;
            case 1:
                if (command == 0)
                {
                    CloseCommChoiceMenu();
                    return;
                }
                SelectCommRecipient(command == 1 ? sim.YourWingman : sim.Ships[ObjectSlots.Player].Target);
                RefreshCommunicationMenu();
                return;
            case 2:
                CloseCommChoiceMenu();
                sim.Request(ObjectSlots.Player, _commMenuRecipient, (CommCommand)command);
                return;
        }
    }

    /// <remarks>C: talk_equiv (0x431400, screen.c).</remarks>
    private void TalkEquiv() => RefreshCommunicationMenu();
}
