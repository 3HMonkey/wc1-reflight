using WingCommander.Core.Resources;
using WingCommander.Game.Input;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;
using WingCommander.Simulation;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The in-flight computer (nav map): nav.c InflightComputer, ShowConfedNavScan,
// DrawNavLocationReadout, BuildMap and the label/marker family, SelectNavObjectiveAtPoint,
// CentreMouseOnCurrentNavObjective, UpdateInflightNavText, FormatNavCoordinates, DrawNavMapLegend;
// brains.c DrawNavTextLine. The map math (SetScale, nav_getxy, ScaleNavMapCoordinates) is the
// simulation's. The briefing's copy of the map lives in Game.Screens.Scenes.BriefingMap.
internal sealed partial class FlightSession
{
    private const int NavLabelCapacity = 20;
    private const int NavReservedAreaCapacity = 21;

    /// <summary>Marker type, marker size and the colours of the unvisited dot, the marker and the label, by objective type.</summary>
    /// <remarks>C: aNavMapObjectiveStyles (0x00468668, nav.c).</remarks>
    private static readonly (short Marker, short Size, byte Unvisited, byte MarkerColour, byte Label)[] NavMapObjectiveStyles =
    [
        (1, 2, PaletteColours.PrimaryText, 0xa8, 0xa8),
        (3, 2, PaletteColours.Black, PaletteColours.ViewportClear, 0xa8),
        (4, 2, 0xb6, 0xb6, 0xa8),
        (2, 3, 0xb6, 0xa8, 0xa8),
        (2, 3, PaletteColours.Red, PaletteColours.Red, 0xa8),
    ];

    /// <remarks>C: apszShipMissionTypeNames (0x00468728, nav.c).</remarks>
    private static readonly string[] ShipMissionTypeNames =
        ["Patrol", "Escort", "Strike", "Defend", "Wingman", "Flee", "Goto Warp", "err", "err", "Rendezvous", "err"];

    /// <remarks>C: stSceneBuffer while the nav map runs (260x156).</remarks>
    private readonly Viewport _navScene = new();

    /// <remarks>C: stNavLabelTextContext (font 2) / stNavMapTextContext (font 1).</remarks>
    private readonly TextContext _navLabelTextContext = new();
    private readonly TextContext _navMapTextContext = new();

    /// <remarks>C: aNavMapLabels[20] / nNavMapLabelCount, aNavMapExclusionRects / nNavMapReservedAreaCount,
    /// awNavObjectiveLabelIndex[16].</remarks>
    private readonly (short X, short Y, byte Colour, string Text)[] _navLabels = new (short, short, byte, string)[NavLabelCapacity];
    private int _navLabelCount;
    private readonly ScreenArea[] _navReservedAreas = new ScreenArea[NavReservedAreaCapacity];
    private int _navReservedAreaCount;
    private readonly short[] _navObjectiveLabelIndex = new short[SpaceSimulation.ObjectiveCount + 1];

    private readonly record struct ScreenArea(short Left, short Top, short Right, short Bottom);

    /// <summary>The nav map opens and closes (tests count the runs).</summary>
    public int NavMapRuns { get; private set; }

    /// <summary>
    /// The nav map: the in-flight computer frame, the map with the readout column ("ConFed Nav
    /// Scan"), then (with visible objectives) a pointer loop at the flight rate: the blinking player
    /// marker, the standard time, objective selection by clicking near a marker or label, N for the
    /// next objective; a button, Enter or Space close it, Esc restores the previous objective. The
    /// simulation does not tick meanwhile. Writes the clock into the campaign's elapsed day (saved).
    /// </summary>
    /// <remarks>C: InflightComputer (0x40E480, nav.c).</remarks>
    private async Task InflightComputerAsync()
    {
        var sim = Sim;
        var events = Events;
        NavMapRuns++;
        CancelSpaceSpriteFrame();
        short savedNavPoint = sim.CurrentNavPointIndex;
        bool done = false;
        bool hasObjectives = false;
        short displayedNavPoint = savedNavPoint;
        _inflightComputerActive = true;
        var cursor = events.Cursor;
        short savedX = cursor.X, savedY = cursor.Y, savedFrame = cursor.Frame;
        byte savedPrimary = cursor.PrimaryButton, savedSecondary = cursor.SecondaryButton;
        var savedViewport = Game.Cursor.Viewport;

        if (MessageShowing())
            EndCommMenu();
        GetScreenUpdateFlag();
        ScreenViewportMode = -1;
        await Display.ClearViewportAsync(Screen, PaletteColours.Black);
        Gfx.DrawSpriteDefault(Screen, 0, 0, Shapes.Get(LogicalFile.CockpitVga, 1), 0);

        NavMapLoadShapes();
        await ShowConfedNavScanAsync();
        for (short objective = 0; objective < sim.MissionObjectiveCount; objective++)
        {
            if (!sim.HiddenObjective(objective))
                hasObjectives = true;
        }

        if (!hasObjectives)
        {
            events.Pump = null;
            await events.WaitForInputKeyAsync();
            await Game.Timing.SetFrameTimerAndWaitAsync(20);
            events.Pump = GetPlayerInput;
        }
        else
        {
            var pointerViewport = Screen.Clone();
            pointerViewport.SetViewportRect(32, 24, 182, 159);
            byte savedInputMode = events.InputMode;
            Game.Cursor.Viewport = pointerViewport;
            events.InputMode = 1;
            events.Pump = null;
            events.MenuInputRepeatDelay = 6;
            events.ShowCursor();
            CentreMouseOnCurrentNavObjective();

            var e = default(InputEventState);
            do
            {
                if (displayedNavPoint != sim.CurrentNavPointIndex)
                {
                    displayedNavPoint = sim.CurrentNavPointIndex;
                    Audio.PlaySfx(0x19);
                    await ShowConfedNavScanAsync();
                }
                var screen = Screen;
                screen.SetViewportRect(32, 24, 289, 177);
                Game.Cursor.SetShape(Game.Cursor.Shape, 0);
                FormatNavCoordinates();
                _navLabelTextContext.Viewport = screen;
                int frame = (int)Game.Timing.Ticks60Hz / 15;
                byte markerColour = frame % 2 != 0 ? PaletteColours.ViewportClear : PaletteColours.DarkGrey;
                DrawNavPlayerMarker(markerColour, false);
                _navMapTextContext.Viewport = screen;
                UpdateInflightNavText(frame / 4 % 2 != 0);
                screen.SetViewportRect(0, 0, 319, 199);

                short type = events.PollInputEvent(ref e);
                switch (type)
                {
                    case InputEventType.ButtonDown:
                    case InputEventType.JoystickButton:
                        done = true;
                        break;
                    case InputEventType.KeyDown:
                    case InputEventType.Character:
                    {
                        short value = (short)e.Value;
                        if (value is 0x1c or 0x39)
                        {
                            done = true;
                        }
                        else if (value == 0x31)
                        {
                            sim.CycleNextObjective();
                            CentreMouseOnCurrentNavObjective();
                        }
                        else
                        {
                            events.MoveMenuPointerFromKeyboard(e);
                        }
                        break;
                    }
                }
                SelectNavObjectiveAtPoint(cursor.X, cursor.Y);
                await Display.PresentAsync();
            }
            while (!done && !events.EscapePressed);

            if (events.EscapePressed)
            {
                events.EscapePressed = false;
                sim.CurrentNavPointIndex = (sbyte)savedNavPoint;
                sim.SetNewObjective(savedNavPoint);
            }
            _navScene.FreeViewport();
            events.HideCursor();
            events.Pump = GetPlayerInput;
            events.InputMode = savedInputMode;
        }

        Gfx.SetTextContext(Game.DefaultText);
        Audio.PlaySfx(0x19);
        cursor.X = savedX;
        cursor.Y = savedY;
        cursor.Frame = savedFrame;
        cursor.PrimaryButton = savedPrimary;
        cursor.SecondaryButton = savedSecondary;
        if (savedViewport is not null)
            Game.Cursor.Viewport = savedViewport;
        events.WarpMouseTo(savedX, savedY);
        if (sim.CockpitlessView == 0)
        {
            sim.ForceView(0, 0);
        }
        else
        {
            GetScreenUpdateFlag();
            SetSpaceBufferSize(sim.ScreenWidth, sim.ScreenHeight);
            InitializeViewBuffer();
            sim.ForceView(0, 0);
            sim.CockpitlessView = 1;
            GetScreenUpdateFlag();
            SetSpaceBufferSize(320, 200);
            InitializeViewBuffer();
        }
        _inflightComputerActive = false;
    }

    /// <summary>The 260x156 picture and the mobile objectives' current positions.</summary>
    /// <remarks>C: BriefingMap_LoadShapes (0x40E190, nav.c).</remarks>
    private void NavMapLoadShapes()
    {
        _navScene.SetViewportRect(0, 0, 259, 155);
        _navScene.AllocateViewport(PaletteColours.Black);
        for (short objective = 0; objective < Sim.MissionObjectiveCount; objective++)
            Sim.LocateMobileObjective(objective);
    }

    /// <remarks>C: ShowConfedNavScan (0x40E430, nav.c): the readout and map copied to the screen at (30,22) with the cursor hidden.</remarks>
    private async Task ShowConfedNavScanAsync()
    {
        Screen.SetViewportRect(30, 22, 289, 177);
        Events.HideCursor();
        await DrawNavLocationReadoutAsync("ConFed Nav Scan", true);
        Events.ShowCursor();
        Screen.SetViewportRect(0, 0, 319, 199);
    }

    /// <summary>Formats a line into the current context's buffer and draws it at the cursor.</summary>
    /// <remarks>C: DrawNavTextLine (brains.c).</remarks>
    private void DrawNavTextLine(byte alignment, byte colour, ReadOnlySpan<byte> format, params ReadOnlySpan<TextArg> args)
    {
        var context = Gfx.CurrentTextContext!;
        context.Colour = colour;
        context.Alignment = alignment;
        Gfx.FormatTextBufferFromStart(format, args);
        Gfx.DrawTextString(context.GetBufferText());
    }

    /// <summary>Readout column (title, sector, system, mission, mission type, notes, legend), the map, the location line; copied to the screen and presented.</summary>
    /// <remarks>C: DrawNavLocationReadout (0x40DF70, nav.c).</remarks>
    private async Task DrawNavLocationReadoutAsync(string title, bool showFlightData)
    {
        var sim = Sim;
        Gfx.ClearViewport(_navScene, PaletteColours.Black);
        _navScene.SetViewportRect(155, 2, 259, 155);
        _navMapTextContext.Viewport = _navScene;
        _navMapTextContext.TextBuffer = DefaultTextBuffer;
        Gfx.InitializeTextContextFromFont(_navMapTextContext, 1, PaletteColours.PrimaryText, PaletteColours.Black);
        _navMapTextContext.Alignment = 0;
        _navMapTextContext.CursorX = 0;
        _navMapTextContext.CursorY = 0;
        Gfx.SetTextContext(_navMapTextContext);
        DrawNavTextLine(0, 0xa8, "\n"u8);
        DrawNavTextLine(TextContext.AlignCentre, 0xa8, "%s\n\n"u8, title);
        DrawNavTextLine(0, 0xa8, "Sector: %s\n"u8, "Vega XR-231.3");
        DrawNavTextLine(0, 0xa8, "System: %s\n\n"u8, sim.SeriesAuxData);
        DrawNavTextLine(TextContext.AlignCentre, 0xa8, "* %s *\n"u8, sim.MissionAuxData);
        int missionType = (int)sim.MissionShips[sim.PlayerMissionShipIndex].MissionType;
        string missionTypeName = (uint)missionType < (uint)ShipMissionTypeNames.Length ? ShipMissionTypeNames[missionType] : "";
        DrawNavTextLine(TextContext.AlignCentre, 0xa8, "* %s *\n"u8, missionTypeName);
        DrawNavTextLine(TextContext.AlignCentre, 0xa8, "\nNotes\n"u8);
        DrawNavTextLine(0, 0xa8, "%s\n"u8, NavNote(sim.CurrentObjective));
        if (showFlightData)
            DrawNavMapLegend();
        BuildMap(showFlightData);
        if (showFlightData)
        {
            _navScene.SetViewportRect(0, 0, 259, 155);
            Gfx.SetTextContext(_navMapTextContext);
            ref readonly var player = ref sim.Objects[ObjectSlots.Player];
            DrawNavTextLine(0, 0xa8, "%X%Y                         Location: %d.%d.%d"u8, 8, 142,
                unchecked((short)player.Position.X), unchecked((short)player.Position.Y), unchecked((short)player.Position.Z));
        }
        Gfx.CopyViewportContents(_navScene, Screen);
        await Display.PresentAsync();
    }

    /// <summary>The objective's note: its mission description without a leading '?'.</summary>
    /// <remarks>C: nav_note (0x40DF50, nav.c); an objective index of -1 read before the table there.</remarks>
    private string NavNote(int objective)
    {
        var objectives = Sim.MissionObjectives;
        if ((uint)objective >= (uint)objectives.Length)
            return "";
        string note = objectives[objective].Name ?? "";
        return note.Length > 0 && note[0] == '?' ? note[1..] : note;
    }

    /// <summary>"MISSION FLIGHT PATH" or "HOME BASE" in yellow when that part of the flight path is selected.</summary>
    /// <remarks>C: DrawNavMapLegend (0x40DEE0) / DrawSelectedNavLegendEntry (0x40DEA0, nav.c).</remarks>
    private void DrawNavMapLegend()
    {
        var sim = Sim;
        short objective = 0;
        while (objective < sim.MissionObjectiveCount)
        {
            if (!sim.Visited(objective) && !sim.HiddenObjective(objective))
                break;
            objective++;
        }
        Gfx.SetTextCursor(_navScene.Left, 120);
        DrawSelectedNavLegendEntry("MISSION FLIGHT PATH", objective);
        DrawSelectedNavLegendEntry("HOME BASE", (short)(sim.MissionObjectiveCount - 1));
    }

    private void DrawSelectedNavLegendEntry(string text, short navPoint)
    {
        if (Sim.CurrentNavPointIndex != navPoint)
            return;
        Span<byte> bytes = stackalloc byte[text.Length];
        Graphics.GraphicsContext.EncodeText(text, bytes);
        DrawNavTextLine(0, PaletteColours.Yellow, bytes);
        DrawNavTextLine(0, PaletteColours.Yellow, "\n"u8);
    }

    /// <summary>Map art, hazard fields, objective markers and labels, and (in flight) the player and his callsign.</summary>
    /// <remarks>C: BuildMap (0x40DA00, nav.c).</remarks>
    private void BuildMap(bool showPlayer)
    {
        var sim = Sim;
        _navScene.SetViewportRect(1, 1, 153, 138);
        Gfx.DrawSpriteDefault(_navScene, 1, 1, Shapes.Get(LogicalFile.CockpitVga, 2), 0);
        _navScene.SetViewportRect(2, 2, 152, 137);
        _navLabelTextContext.Viewport = _navScene;
        _navLabelTextContext.TextBuffer = DefaultTextBuffer;
        Gfx.InitializeTextContextFromFont(_navLabelTextContext, 2, PaletteColours.PrimaryText, PaletteColours.Transparent);
        _navLabelTextContext.Alignment = 0;
        Gfx.SetTextContext(_navLabelTextContext);
        _navLabelCount = 0;
        _navReservedAreaCount = 0;
        sim.SetScale();

        for (int nav = 0; nav < sim.MissionNavPoints.Length && sim.MissionNavPoints[nav].Type != 0; nav++)
        {
            ref readonly var navPoint = ref sim.MissionNavPoints[nav];
            for (int slot = 0; slot < 10; slot++)
            {
                short missionShipIndex = navPoint.MissionShips[slot];
                if (missionShipIndex == -1)
                    continue;
                ref readonly var ship = ref sim.MissionShips[missionShipIndex];
                if (ship.Type == ObjectType.AsteroidField)
                    DrawNavHazardMarker(navPoint.Position, ship.Position, ship.Speed, 0xf5, 0xf5, "Asteroids");
                else if (ship.Type == ObjectType.MineField)
                    DrawNavHazardMarker(navPoint.Position, ship.Position, ship.Speed, PaletteColours.Red, PaletteColours.Red, "Mines");
            }
        }

        for (short objectiveIndex = 0; objectiveIndex < sim.MissionObjectiveCount; objectiveIndex++)
        {
            ref readonly var objective = ref sim.MissionObjectives[objectiveIndex];
            if (sim.MobileObjective(objectiveIndex) &&
                (sim.MissionShips[objective.Index].State != 0 || sim.Achieved(objectiveIndex)))
                continue;
            var (x, y) = sim.ScaleNavMapCoordinates(objective.MapX, objective.MapY);
            if (sim.HiddenObjective(objectiveIndex))
                continue;
            var style = NavMapObjectiveStyles[Math.Clamp(objective.Type, 0, NavMapObjectiveStyles.Length - 1)];
            if (!sim.Visited(objectiveIndex))
                Gfx.DrawViewportPixel(_navScene, x, y, style.Unvisited);
            switch (style.Marker)
            {
                case 1:
                    DrawNavSquareMarker(x, y, style.Size, style.MarkerColour, true);
                    break;
                case 2:
                    DrawNavRectangleMarker(x, y, style.Size, style.MarkerColour, true);
                    break;
                case 3:
                    DrawNavTriangleMarker(x, y, style.Size, style.MarkerColour, true);
                    break;
                case 4:
                    DrawNavCrossMarker(x, y, style.Size, style.MarkerColour, true);
                    break;
            }
            byte labelColour = sim.CurrentObjective == objectiveIndex ? PaletteColours.Yellow : style.Label;
            _navObjectiveLabelIndex[objectiveIndex] = (short)_navLabelCount;
            AddUniqueObjectiveNavLabel(x, y, labelColour, ObjectiveName(objectiveIndex), objectiveIndex, objective.Index);
        }
        if (showPlayer)
        {
            DrawNavPlayerMarker(PaletteColours.ViewportClear, true);
            ref readonly var player = ref sim.Objects[ObjectSlots.Player];
            var (x, y) = sim.NavGetXY(player.Position.X, player.Position.Z);
            PlaceNavMapLabel(x, y, PaletteColours.LightGrey, Game.Session.Player.Callsign);
        }
        DrawNavMapLabels();
        _navScene.SetViewportRect(0, 0, 259, 155);
    }

    /// <remarks>C: DrawNavHazardMarker (0x40D8F0, nav.c).</remarks>
    private void DrawNavHazardMarker(Core.Numerics.FixedVector navPosition, Core.Numerics.FixedVector offset, short size,
        byte markerColour, byte textColour, string text)
    {
        var sim = Sim;
        var position = Simulation.Geometry.VectorMath.Add(navPosition, offset);
        size = sim.ScaleNavMapMarkerSize(size);
        var (x, y) = sim.NavGetXY(position.X, position.Z);
        DrawNavRectangleMarker(x, y, size, markerColour, true);
        PlaceNavMapLabel(x, y, textColour, text);
    }

    /// <remarks>C: DrawNavPlayerMarker (0x40D980, nav.c).</remarks>
    private void DrawNavPlayerMarker(byte colour, bool reserve)
    {
        var sim = Sim;
        ref readonly var player = ref sim.Objects[ObjectSlots.Player];
        var (x, y) = sim.NavGetXY(player.Position.X, player.Position.Z);
        var viewport = _navLabelTextContext.Viewport ?? _navScene;
        x = (short)(x + viewport.Left);
        y = (short)(y + viewport.Top);
        Gfx.DrawViewportPixel(viewport, x, y, colour);
        DrawNavSquareMarker(x, y, 0, colour, reserve);
    }

    /// <remarks>C: DrawNavRectangleMarker (0x40D5A0, nav.c): an ellipse (the "shadow" variant is identical).</remarks>
    private void DrawNavRectangleMarker(short x, short y, short size, byte colour, bool reserve)
    {
        var viewport = _navLabelTextContext.Viewport ?? _navScene;
        Gfx.DrawViewportEllipse(viewport, x, y, size, (short)(size * 7 / 8), colour);
        if (reserve && size < 5)
            ReserveNavMapArea((short)(x - size), (short)(y - size), (short)(size * 2 + 1), (short)(size * 2 + 1));
    }

    /// <remarks>C: DrawNavSquareMarker (0x40D680) / DrawNavSquareOutline (0x40D640, nav.c).</remarks>
    private void DrawNavSquareMarker(short x, short y, short size, byte colour, bool reserve)
    {
        var viewport = _navLabelTextContext.Viewport ?? _navScene;
        if (size == 0)
        {
            Gfx.DrawViewportPixel(viewport, x, y, colour);
            Gfx.DrawViewportPixel(viewport, x + 1, y, colour);
            Gfx.DrawViewportPixel(viewport, x, y + 1, colour);
            Gfx.DrawViewportPixel(viewport, x + 1, y + 1, colour);
        }
        else
        {
            Gfx.DrawViewportBorder(viewport, x - size, y - size, x + size, y + size, colour);
        }
        if (reserve && size < 5)
            ReserveNavMapArea((short)(x - size), (short)(y - size), (short)(size * 2 + 1), (short)(size * 2 + 1));
    }

    /// <remarks>C: DrawNavTriangleMarker (0x40D7D0) / DrawNavTriangleOutline (0x40D740, nav.c).</remarks>
    private void DrawNavTriangleMarker(short x, short y, short size, byte colour, bool reserve)
    {
        var viewport = _navLabelTextContext.Viewport ?? _navScene;
        Gfx.DrawViewportLine(viewport, x, y - size, x + size, y + size, colour);
        Gfx.DrawViewportLine(viewport, x + size, y + size, x - size, y + size, colour);
        Gfx.DrawViewportLine(viewport, x - size, y + size, x, y - size, colour);
        if (reserve && size < 5)
            ReserveNavMapArea((short)(x - size), (short)(y - size), (short)(size * 2 + 1), (short)(size * 2 + 1));
    }

    /// <remarks>C: DrawNavCrossMarker (0x40D830, nav.c).</remarks>
    private void DrawNavCrossMarker(short x, short y, short size, byte colour, bool reserve)
    {
        var viewport = _navLabelTextContext.Viewport ?? _navScene;
        Gfx.DrawViewportLine(viewport, x - size, y - size, x + size, y + size, colour);
        Gfx.DrawViewportLine(viewport, x - size, y + size, x + size, y - size, colour);
        if (reserve && size < 5)
            ReserveNavMapArea((short)(x - size), (short)(y - size), (short)(size * 2 + 1), (short)(size * 2 + 1));
    }

    /// <remarks>C: ReserveNavMapArea (0x40D1E0, nav.c); areas beyond the original's 21 slots are dropped.</remarks>
    private void ReserveNavMapArea(short x, short y, short width, short height)
    {
        if (_navReservedAreaCount >= NavReservedAreaCapacity)
            return;
        _navReservedAreas[_navReservedAreaCount++] = new ScreenArea(x, y, (short)(x + width), (short)(y + height));
    }

    /// <remarks>C: NavMapLabelFits (0x40D0E0, nav.c).</remarks>
    private static bool NavMapLabelFits(short x, short y, short width, short height) =>
        x > 0 && y > 0 && x + width < 150 && y + height < 135;

    /// <remarks>C: NavMapLabelPositionAvailable (0x40D120) / NavMapPointInsideReservedArea (0x40D090, nav.c).</remarks>
    private bool NavMapLabelPositionAvailable(short x, short y, short width, short height)
    {
        bool available = NavMapLabelFits(x, y, width, height);
        for (int area = 0; available && area < _navReservedAreaCount; area++)
        {
            var rectangle = _navReservedAreas[area];
            for (int checkX = x; available && checkX < x + width; checkX++)
            {
                for (int checkY = y; available && checkY < y + height; checkY++)
                {
                    available = !(rectangle.Left <= checkX && checkX <= rectangle.Right &&
                                  rectangle.Top <= checkY && checkY <= rectangle.Bottom);
                }
            }
        }
        return available;
    }

    /// <remarks>C: TryPlaceNavMapLabel (0x40D250, nav.c).</remarks>
    private bool TryPlaceNavMapLabel(short x, short y, short width, bool force)
    {
        if (NavMapLabelPositionAvailable(x, y, width, 6) || (force && NavMapLabelFits(x, y, width, 6)))
        {
            _navLabels[_navLabelCount].X = x;
            _navLabels[_navLabelCount].Y = y;
            return true;
        }
        return false;
    }

    /// <summary>Places a label in up to 13 rounds of five candidate positions around the point (forced in the last round).</summary>
    /// <remarks>C: PlaceNavMapLabel (0x40D2C0, nav.c); labels beyond the original's 20 slots are dropped.</remarks>
    private void PlaceNavMapLabel(short x, short y, byte colour, string text)
    {
        if (_navLabelCount >= NavLabelCapacity)
            return;
        short width = (short)(text.Length * 4 + 2);
        _navLabels[_navLabelCount].Colour = colour;
        _navLabels[_navLabelCount].Text = text;
        short offset = -1;
        do
        {
            offset++;
            if (TryPlaceNavMapLabel((short)(x + offset + 4), y, width, false))
                break;
            bool force = offset == 12;
            if (TryPlaceNavMapLabel((short)(x - width / 2), (short)(y + offset + 5), width, force))
                break;
            if (TryPlaceNavMapLabel((short)(x - offset - width - 3), y, width, force))
                break;
            if (TryPlaceNavMapLabel(x, (short)(y + offset + 5), width, force))
                break;
            if (TryPlaceNavMapLabel((short)(x - width / 2), (short)(y - offset - 9), width, force))
                break;
        }
        while (offset != 12);
        ReserveNavMapArea(_navLabels[_navLabelCount].X, _navLabels[_navLabelCount].Y, width, 6);
        _navLabelCount++;
    }

    /// <summary>A label per mission ship (the first objective naming a ship gets it).</summary>
    /// <remarks>C: AddUniqueObjectiveNavLabel (0x40D410, nav.c).</remarks>
    private void AddUniqueObjectiveNavLabel(short x, short y, byte colour, string text, short objective, short missionShip)
    {
        if (missionShip == -1)
        {
            PlaceNavMapLabel(x, y, colour, text);
            return;
        }
        var objectives = Sim.MissionObjectives;
        short previous = 0;
        while (previous < objective && objectives[previous].Index != missionShip)
            previous++;
        if (previous < objective)
            return;
        PlaceNavMapLabel(x, y, colour, text);
    }

    /// <remarks>C: IsPointInNavMapLabel (0x40D490, nav.c).</remarks>
    private bool IsPointInNavMapLabel(int labelIndex, short x, short y)
    {
        if ((uint)labelIndex >= (uint)_navLabels.Length || _navLabels[labelIndex].Text is not { } text)
            return false;
        var label = _navLabels[labelIndex];
        return label.X <= x && (uint)x <= (uint)(label.X + text.Length * 4) && label.Y <= y && y <= label.Y + 6;
    }

    /// <remarks>C: DrawNavMapLabels (0x40D540, nav.c): "%X%Y%F%s" per label.</remarks>
    private void DrawNavMapLabels()
    {
        for (int label = 0; label < _navLabelCount; label++)
        {
            var entry = _navLabels[label];
            Gfx.DrawFormattedText("%X%Y%F%s"u8, entry.X, entry.Y, entry.Colour, entry.Text);
        }
    }

    /// <summary>Clicking within 6 pixels (Manhattan) of an objective's marker, or on the label stored at its path
    /// index, selects it.</summary>
    /// <remarks>C: SelectNavObjectiveAtPoint (0x40E2B0, nav.c). RE-CHECK: the label table is indexed by the
    /// flight-path index although BuildMap filled it by objective index (literal).</remarks>
    private bool SelectNavObjectiveAtPoint(short mouseX, short mouseY)
    {
        var sim = Sim;
        short oldNavPoint = sim.CurrentNavPointIndex;
        mouseX = (short)(mouseX - 30);
        mouseY = (short)(mouseY - 22);
        bool selected = false;
        for (short pathIndex = 0; pathIndex < sim.FlightPath.Length && sim.FlightPath[pathIndex] != -1; pathIndex++)
        {
            sbyte objective = sim.FlightPath[pathIndex];
            if (sim.HiddenObjective(objective))
                continue;
            var (mapX, mapY) = sim.ScaleNavMapCoordinates(sim.MissionObjectives[objective].MapX, sim.MissionObjectives[objective].MapY);
            if ((short)(Math.Abs(mouseX - mapX) + Math.Abs(mouseY - mapY)) < 6 ||
                IsPointInNavMapLabel(_navObjectiveLabelIndex[pathIndex], mouseX, mouseY))
            {
                selected = true;
                sim.SetNewObjective(pathIndex);
                if (pathIndex == oldNavPoint)
                    return selected;
            }
        }
        return selected;
    }

    /// <remarks>C: CentreMouseOnCurrentNavObjective (0x40E3C0, nav.c).</remarks>
    private void CentreMouseOnCurrentNavObjective()
    {
        var sim = Sim;
        sbyte objective = sim.FlightPath[Math.Max(0, (int)sim.CurrentNavPointIndex)];
        if (objective < 0)
            objective = 0;
        var (x, y) = sim.ScaleNavMapCoordinates(sim.MissionObjectives[objective].MapX, sim.MissionObjectives[objective].MapY);
        Events.HideCursor();
        Events.WarpMouseTo((short)(x + 30), (short)(y + 22));
        Events.ShowCursor();
    }

    /// <summary>The game clock's hours and minutes, written into the two bytes of the campaign's elapsed day (saved with the game).</summary>
    /// <remarks>C: FormatNavCoordinates (0x40DE70, nav.c) with SplitGameClockTicks (mono.c) on pElapsedCampaignDate.</remarks>
    private void FormatNavCoordinates()
    {
        int ticks = unchecked((int)Game.Timing.GameClockTicks);
        ticks /= 60;
        ticks /= 60;
        byte minutes = unchecked((byte)(ticks % 60));
        ticks /= 60;
        byte hours = unchecked((byte)(ticks % 24));
        Game.Session.State.ElapsedDate.Day = unchecked((short)(hours | (minutes << 8)));
    }

    /// <summary>"Standard time HH:MM" with a blinking colon, from the elapsed day's two bytes.</summary>
    /// <remarks>C: UpdateInflightNavText (0x40DDA0, nav.c).</remarks>
    private void UpdateInflightNavText(bool showColon)
    {
        var context = _navMapTextContext;
        Gfx.SetTextContext(context);
        short day = Game.Session.State.ElapsedDate.Day;
        int hours = unchecked((sbyte)(day & 0xff));
        int minutes = unchecked((sbyte)(day >> 8));
        var viewport = context.Viewport ?? Screen;
        Gfx.DrawFormattedText("%X%YStandard time %s"u8, viewport.Left + 150, viewport.Top + 140, hours.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        short cursorX = context.CursorX;
        if (!showColon)
            Gfx.DrawFormattedText(" "u8);
        else
            context.CursorX = (short)(context.CursorX + 4);
        Gfx.DrawFormattedText(minutes.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "  ");
        context.CursorX = cursorX;
        if (showColon)
            Gfx.DrawFormattedText(":"u8);
    }
}
