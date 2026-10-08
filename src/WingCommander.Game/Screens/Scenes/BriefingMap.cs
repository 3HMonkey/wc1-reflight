using WingCommander.Core.Resources;
using WingCommander.Game.Scenes;
using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;

namespace WingCommander.Game.Screens.Scenes;

/// <summary>
/// The briefing's nav map: a 260x156 picture with the map (left, COCKPIT.VGA section 2 art) and
/// the readout column (right: title, sector, system, mission name and type, notes on the current
/// objective). The map fits all objectives and the player into 150x135 pixels and draws hazard
/// fields, objective markers by type and labels placed so they avoid each other. The picture is
/// copied to the screen at row 4, and the screen viewport keeps that top edge afterwards, as in
/// the original. Reflight also draws the picture, shrunk, onto the wall screen of the briefing
/// room (<see cref="DrawOnBoard"/>).
/// </summary>
/// <remarks>
/// C (nav.c): BriefingMap_DisplayMap (0x40E210), BriefingMap_LoadShapes (0x40E190),
/// DrawNavLocationReadout (0x40DF70), BuildMap (0x40DA00), the label placement family
/// (NavMapPointInsideReservedArea 0x40D090 .. DrawNavMapLabels 0x40D540), the marker family
/// (DrawNavRectangleMarker 0x40D5A0 .. DrawNavCrossMarker 0x40D830), DrawNavHazardMarker (0x40D8F0),
/// SetScreenClipRect (0x40D8C0); brains.c: SetScale (0x40CD30), CheckPoint, IncludeNavMapWorldPoint,
/// nav_getxy (0x40CC30), ScaleNavMapCoordinates (0x40CBE0), ScaleNavMapMarkerSize (0x40CBC0),
/// DrawNavTextLine (brains.c). The in-flight parts (player marker, flight-path legend, location
/// line) are not drawn by the briefing (showFlightData = 0) and belong to the flight layer.
/// </remarks>
public sealed class BriefingMap
{
    private const int LabelCapacity = 20;
    private const int ReservedAreaCapacity = 21;

    /// <remarks>C: cDefaultTextColour (0xA8).</remarks>
    private const byte DefaultTextColour = 0xa8;

    /// <remarks>C: cMagentaColour.</remarks>
    private const byte MagentaColour = 0xb6;

    /// <remarks>C: cAsteroidColour.</remarks>
    private const byte AsteroidColour = 0xf5;

    /// <remarks>C: OBJECT_TYPE_ASTEROID_FIELD, OBJECT_TYPE_MINE_FIELD.</remarks>
    private const short AsteroidFieldType = 22;
    private const short MineFieldType = 23;

    /// <remarks>C: szCampaignSector (0x00468718).</remarks>
    public const string CampaignSector = "Vega XR-231.3";

    /// <remarks>C: szBriefingNavMapTitle.</remarks>
    public const string Title = "Briefing Nav Map";

    /// <remarks>C: apszShipMissionTypeNames (0x00468728).</remarks>
    private static readonly string[] ShipMissionTypeNames =
        ["Patrol", "Escort", "Strike", "Defend", "Wingman", "Flee", "Goto Warp", "err", "err", "Rendezvous", "err"];

    /// <summary>Marker type, marker size and the colours of the unvisited dot, the marker and the label, by objective type.</summary>
    /// <remarks>C: aNavMapObjectiveStyles (0x00468668).</remarks>
    private static readonly (short Marker, short Size, byte Unvisited, byte MarkerColour, byte Label)[] Styles =
    [
        (1, 2, PaletteColours.PrimaryText, DefaultTextColour, DefaultTextColour),
        (3, 2, PaletteColours.Black, PaletteColours.ViewportClear, DefaultTextColour),
        (4, 2, MagentaColour, MagentaColour, DefaultTextColour),
        (2, 3, MagentaColour, DefaultTextColour, DefaultTextColour),
        (2, 3, PaletteColours.Red, PaletteColours.Red, DefaultTextColour),
    ];

    private readonly ConversationStage _stage;
    private readonly Label[] _labels = new Label[LabelCapacity];
    private readonly Area[] _reserved = new Area[ReservedAreaCapacity];
    private readonly TextContext _labelText = new() { TextBuffer = new byte[256] };
    private readonly TextContext _readoutText = new() { TextBuffer = new byte[256] };
    private Viewport? _target;
    private MissionBriefingData _mission = null!;
    private ShapeTable? _mapShape;
    private int _labelCount;
    private int _reservedCount;
    private bool _scaling;
    private short _scale = 1;
    private short _centreX;
    private short _centreY;
    private short _minimumX;
    private short _maximumX;
    private short _minimumY;
    private short _maximumY;

    public BriefingMap(ConversationStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        _stage = stage;
    }

    /// <summary>Number of labels placed by the last map (tests).</summary>
    public int LabelCount => _labelCount;

    /// <summary>Text of label <paramref name="index"/> of the last map (tests).</summary>
    public string LabelText(int index) => _labels[index].Text;

    /// <summary>Map scale of the last map (world units per pixel / 25600).</summary>
    /// <remarks>C: nNavMapScale.</remarks>
    public short Scale => _scale;

    private GraphicsContext Gfx => _stage.Graphics;

    /// <summary>The picture being drawn: the stage's scene buffer, or the board's own buffer.</summary>
    private Viewport Scene => _target ?? _stage.SceneBuffer;

    /// <summary>
    /// Port addition (Reflight): draws the map of <paramref name="mission"/> into a buffer of its
    /// own and copies it, shrunk to <paramref name="width"/> x <paramref name="height"/>, to
    /// (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="destination"/>. The copy
    /// keeps the map's thin lines, and the output-resolution text follows the readout and the
    /// labels to their new size, so they stay readable. Leaves the current text context alone.
    /// </summary>
    public void DrawOnBoard(MissionBriefingData mission, Viewport destination, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(destination);
        _mission = mission;
        TextContext? savedText = Gfx.CurrentTextContext;
        var picture = Viewport.Allocate(0, 0, 259, 155, PaletteColours.Black);
        _target = picture;
        try
        {
            _mapShape = _stage.Game.Resources.GetShape(LogicalFile.CockpitVga, 2);
            DrawReadout(Title);
            BuildMap();
            Gfx.CopyViewportContentsScaled(picture, destination, x, y, width, height, keepThinLines: true);
        }
        finally
        {
            _target = null;
            _mapShape = null;
            Gfx.TextTracker?.Forget(picture.Surface!);
            Gfx.SetTextContext(savedText);
        }
    }

    /// <summary>
    /// Draws the map of <paramref name="mission"/> (current objective highlighted) into a temporary
    /// 260x156 scene buffer, shows it at screen row 4 and gives the conversation a fresh black
    /// 320x128 buffer afterwards.
    /// </summary>
    /// <remarks>C: BriefingMap_DisplayMap (0x40E210, nav.c).</remarks>
    public async Task DisplayAsync(MissionBriefingData mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        _mission = mission;
        short savedLeft = Scene.Left, savedTop = Scene.Top, savedRight = Scene.Right, savedBottom = Scene.Bottom;
        Scene.FreeViewport();
        LoadShapes();
        _stage.Screen.Top = 4;
        await DrawNavLocationReadoutAsync(Title);
        Scene.FreeViewport();
        _mapShape = null;
        Scene.SetViewportRect(savedLeft, savedTop, savedRight, savedBottom);
        Scene.AllocateViewport(PaletteColours.Black);
    }

    /// <summary>Loads the map art and allocates the 260x156 buffer (mobile objectives are not located: no ship exists before launch).</summary>
    /// <remarks>C: BriefingMap_LoadShapes (0x40E190, nav.c).</remarks>
    private void LoadShapes()
    {
        _mapShape = _stage.Game.Resources.GetShape(LogicalFile.CockpitVga, 2);
        SetScreenClipRect(0, 0, 259, 155);
        Scene.AllocateViewport(PaletteColours.Black);
    }

    /// <summary>The right-hand readout column, then the map; copies the picture to the screen and presents.</summary>
    /// <remarks>C: DrawNavLocationReadout (0x40DF70, nav.c) with showFlightData = 0.</remarks>
    private async Task DrawNavLocationReadoutAsync(string title)
    {
        DrawReadout(title);
        BuildMap();
        Gfx.CopyViewportContents(Scene, _stage.Screen);
        await _stage.PresentAsync();
    }

    /// <summary>The readout column: title, sector, system, mission name and type, the note on the current objective.</summary>
    /// <remarks>C: the text part of DrawNavLocationReadout (0x40DF70, nav.c).</remarks>
    private void DrawReadout(string title)
    {
        Gfx.ClearViewport(Scene, PaletteColours.Black);
        SetScreenClipRect(155, 2, 259, 155);
        _readoutText.Viewport = Scene;
        Gfx.InitializeTextContextFromFont(_readoutText, 1, PaletteColours.PrimaryText, PaletteColours.Black);
        _readoutText.Alignment = 0;
        _readoutText.CursorX = 0;
        _readoutText.CursorY = 0;
        Gfx.SetTextContext(_readoutText);
        DrawNavTextLine(0, DefaultTextColour, "\n"u8);
        DrawNavTextLine(TextContext.AlignCentre, DefaultTextColour, "%s\n\n"u8, title);
        DrawNavTextLine(0, DefaultTextColour, "Sector: %s\n"u8, CampaignSector);
        DrawNavTextLine(0, DefaultTextColour, "System: %s\n\n"u8, _mission.SystemName);
        DrawNavTextLine(TextContext.AlignCentre, DefaultTextColour, "* %s *\n"u8, _mission.MissionName);
        int missionType = _mission.PlayerMissionType;
        string missionTypeName = (uint)missionType < (uint)ShipMissionTypeNames.Length ? ShipMissionTypeNames[missionType] : "";
        DrawNavTextLine(TextContext.AlignCentre, DefaultTextColour, "* %s *\n"u8, missionTypeName);
        DrawNavTextLine(TextContext.AlignCentre, DefaultTextColour, "\nNotes\n"u8);
        DrawNavTextLine(0, DefaultTextColour, "%s\n"u8, _mission.NavNote(_mission.CurrentObjective));
    }

    /// <summary>Formats one readout line into the context's buffer and draws it at the cursor.</summary>
    /// <remarks>C: DrawNavTextLine (brains.c).</remarks>
    private void DrawNavTextLine(byte alignment, byte colour, ReadOnlySpan<byte> format, params ReadOnlySpan<TextArg> args)
    {
        var context = Gfx.CurrentTextContext!;
        context.Colour = colour;
        context.Alignment = alignment;
        Gfx.FormatTextBufferFromStart(format, args);
        Gfx.DrawTextString(context.GetBufferText());
    }

    /// <summary>Map art, hazard fields, objective markers and labels.</summary>
    /// <remarks>C: BuildMap (0x40DA00, nav.c) with showPlayer = 0.</remarks>
    private void BuildMap()
    {
        SetScreenClipRect(1, 1, 153, 138);
        Gfx.DrawSpriteDefault(Scene, 1, 1, _mapShape, 0);
        SetScreenClipRect(2, 2, 152, 137);
        _labelText.Viewport = Scene;
        Gfx.InitializeTextContextFromFont(_labelText, 2, PaletteColours.PrimaryText, PaletteColours.Transparent);
        _labelText.Alignment = 0;
        Gfx.SetTextContext(_labelText);
        _labelCount = 0;
        _reservedCount = 0;
        SetScale();

        foreach (var navPoint in _mission.NavPoints)
        {
            if (navPoint.Type == 0)
                break;
            foreach (short missionShipIndex in navPoint.MissionShips)
            {
                if (missionShipIndex == -1)
                    continue;
                var ship = _mission.Ship(missionShipIndex);
                if (ship.Type == AsteroidFieldType)
                    DrawNavHazardMarker(navPoint, ship, AsteroidColour, AsteroidColour, "Asteroids");
                else if (ship.Type == MineFieldType)
                    DrawNavHazardMarker(navPoint, ship, PaletteColours.Red, PaletteColours.Red, "Mines");
            }
        }

        for (int objectiveIndex = 0; objectiveIndex < _mission.ObjectiveListCount; objectiveIndex++)
        {
            var objective = _mission.Objectives[objectiveIndex];
            if (_mission.MobileObjective(objectiveIndex) &&
                (_mission.Ship(objective.Index).State != 0 || _mission.Achieved(objectiveIndex)))
                continue;
            var (x, y) = ScaleNavMapCoordinates(objective.MapX, objective.MapY);
            if (_mission.HiddenObjective(objectiveIndex))
                continue;
            var style = Styles[Math.Clamp(objective.Type, 0, Styles.Length - 1)];
            if (!_mission.Visited(objectiveIndex))
                Gfx.DrawViewportPixel(Scene, x, y, style.Unvisited);
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
            byte labelColour = _mission.CurrentObjective == objectiveIndex ? PaletteColours.Yellow : style.Label;
            AddUniqueObjectiveNavLabel(x, y, labelColour, _mission.ObjectiveName(objectiveIndex), objectiveIndex, objective.Index);
        }
        DrawNavMapLabels();
        SetScreenClipRect(0, 0, 259, 155);
    }

    /// <summary>Sets the scene buffer's clip rectangle (the buffer itself is not reallocated).</summary>
    /// <remarks>C: SetScreenClipRect (0x40D8C0, nav.c) -> SetRectBounds(&amp;stSceneBuffer, ...).</remarks>
    private void SetScreenClipRect(int left, int top, int right, int bottom) => Scene.SetViewportRect(left, top, right, bottom);

    // ------------------------------------------------------------------ scale (brains.c)

    /// <summary>
    /// Fits the map: bounding box of every objective and of the player (at the origin: no ship exists
    /// before launch), centre and scale so the box fills 150x135 pixels with a margin.
    /// </summary>
    /// <remarks>C: SetScale (0x40CD30, brains.c). Mobile objectives keep their nav point positions
    /// (find_ship_index finds no spawned ship).</remarks>
    private void SetScale()
    {
        _scaling = false;
        var objectives = _mission.Objectives;
        _minimumX = objectives[0].MapX;
        _maximumX = objectives[0].MapX;
        _minimumY = objectives[0].MapY;
        _maximumY = objectives[0].MapY;
        for (int i = 0; i < _mission.ObjectiveListCount; i++)
        {
            var objective = objectives[i];
            (objective.MapX, objective.MapY) = NavGetXY(objective.X, objective.Z);
            CheckPoint(objective.MapX, objective.MapY);
        }
        var (playerX, playerY) = NavGetXY(0, 0);
        CheckPoint(playerX, playerY);
        short width = unchecked((short)(_maximumX - _minimumX));
        short height = unchecked((short)(_maximumY - _minimumY));
        short halfWidth = (short)(width / 2);
        _centreX = unchecked((short)(_minimumX + halfWidth));
        short halfHeight = (short)(height / 2);
        _centreY = unchecked((short)(_minimumY + halfHeight));
        _scale = Math.Max(unchecked((short)((width + halfWidth) / 150)), unchecked((short)((halfHeight + height) / 135)));
        if (_scale == 0)
            _scale = 100;
        _scaling = true;
    }

    /// <remarks>C: CheckPoint (0x40CC80, brains.c).</remarks>
    private void CheckPoint(short x, short y)
    {
        _minimumX = Math.Min(_minimumX, x);
        _maximumX = Math.Max(_maximumX, x);
        _minimumY = Math.Min(_minimumY, y);
        _maximumY = Math.Max(_maximumY, y);
    }

    /// <remarks>C: nav_getxy (0x40CC30, brains.c).</remarks>
    private (short X, short Y) NavGetXY(int worldX, int worldZ)
    {
        var (x, y) = MissionBriefingData.NavGetXY(worldX, worldZ);
        return _scaling ? ScaleNavMapCoordinates(x, y) : (x, y);
    }

    /// <remarks>C: ScaleNavMapCoordinates (0x40CBE0, brains.c).</remarks>
    private (short X, short Y) ScaleNavMapCoordinates(short mapX, short mapY) =>
        (unchecked((short)((mapX - _centreX) / _scale + 75)), unchecked((short)((_centreY - mapY) / _scale + 67)));

    // ------------------------------------------------------------------ markers (nav.c)

    /// <summary>An asteroid or mine field: circle sized by the field and a label.</summary>
    /// <remarks>C: DrawNavHazardMarker (0x40D8F0, nav.c) with ScaleNavMapMarkerSize (0x40CBC0).</remarks>
    private void DrawNavHazardMarker(BriefingNavPoint navPoint, BriefingMissionShip field, byte markerColour, byte textColour, string text)
    {
        int worldX = unchecked(navPoint.X + field.X);
        int worldZ = unchecked(navPoint.Z + field.Z);
        short size = unchecked((short)(field.Speed / (_scale * 100)));
        var (x, y) = NavGetXY(worldX, worldZ);
        DrawNavRectangleMarker(x, y, size, markerColour, true);
        PlaceNavMapLabel(x, y, textColour, text);
    }

    /// <summary>
    /// Ellipse marker (the "rectangle" of the original's naming) with radii size and 7/8 size. The
    /// ellipse wrapper adds the viewport origin, so these markers sit one clip offset (2, 2) away
    /// from the other markers, like the original.
    /// </summary>
    /// <remarks>C: DrawNavRectangleMarker (0x40D5A0, nav.c).</remarks>
    private void DrawNavRectangleMarker(short x, short y, short size, byte colour, bool reserve)
    {
        Gfx.DrawViewportEllipse(Scene, x, y, size, size * 7 / 8, colour);
        if (reserve && size < 5)
            ReserveNavMapArea((short)(x - size), (short)(y - size), (short)(size * 2 + 1), (short)(size * 2 + 1));
    }

    /// <remarks>C: DrawNavSquareMarker (0x40D680) / DrawNavSquareOutline (0x40D640), nav.c.</remarks>
    private void DrawNavSquareMarker(short x, short y, short size, byte colour, bool reserve)
    {
        if (size == 0)
        {
            Gfx.DrawViewportPixel(Scene, x, y, colour);
            Gfx.DrawViewportPixel(Scene, x + 1, y, colour);
            Gfx.DrawViewportPixel(Scene, x, y + 1, colour);
            Gfx.DrawViewportPixel(Scene, x + 1, y + 1, colour);
        }
        else
        {
            Gfx.DrawViewportBorder(Scene, x - size, y - size, x + size, y + size, colour);
        }
        if (reserve && size < 5)
            ReserveNavMapArea((short)(x - size), (short)(y - size), (short)(size * 2 + 1), (short)(size * 2 + 1));
    }

    /// <remarks>C: DrawNavTriangleMarker (0x40D7D0) / DrawNavTriangleOutline (0x40D740), nav.c.</remarks>
    private void DrawNavTriangleMarker(short x, short y, short size, byte colour, bool reserve)
    {
        Gfx.DrawViewportLine(Scene, x, y - size, x + size, y + size, colour);
        Gfx.DrawViewportLine(Scene, x + size, y + size, x - size, y + size, colour);
        Gfx.DrawViewportLine(Scene, x - size, y + size, x, y - size, colour);
        if (reserve && size < 5)
            ReserveNavMapArea((short)(x - size), (short)(y - size), (short)(size * 2 + 1), (short)(size * 2 + 1));
    }

    /// <remarks>C: DrawNavCrossMarker (0x40D830, nav.c).</remarks>
    private void DrawNavCrossMarker(short x, short y, short size, byte colour, bool reserve)
    {
        Gfx.DrawViewportLine(Scene, x - size, y - size, x + size, y + size, colour);
        Gfx.DrawViewportLine(Scene, x - size, y + size, x + size, y - size, colour);
        if (reserve && size < 5)
            ReserveNavMapArea((short)(x - size), (short)(y - size), (short)(size * 2 + 1), (short)(size * 2 + 1));
    }

    // ------------------------------------------------------------------ labels (nav.c)

    /// <remarks>C: NavMapPointInsideReservedArea (0x40D090, nav.c).</remarks>
    private bool PointInsideReservedArea(int area, int x, int y)
    {
        var r = _reserved[area];
        return r.Left <= x && x <= r.Right && r.Top <= y && y <= r.Bottom;
    }

    /// <summary>The label box must lie inside the map (1..149 x 1..134).</summary>
    /// <remarks>C: NavMapLabelFits (0x40D0E0, nav.c).</remarks>
    private static bool LabelFits(int x, int y, int width, int height) => x > 0 && y > 0 && x + width < 150 && y + height < 135;

    /// <remarks>C: NavMapLabelPositionAvailable (0x40D120, nav.c).</remarks>
    private bool LabelPositionAvailable(short x, short y, short width, short height)
    {
        bool available = LabelFits(x, y, width, height);
        if (available)
        {
            for (int area = 0; area < _reservedCount && available; area++)
            {
                for (int checkX = x; checkX < x + width && available; checkX++)
                {
                    for (int checkY = y; checkY < y + height && available; checkY++)
                        available = !PointInsideReservedArea(area, checkX, checkY);
                }
            }
        }
        return available;
    }

    /// <remarks>C: ReserveNavMapArea (0x40D1E0, nav.c). Areas beyond the original's 21 slots are dropped (the original overran the table).</remarks>
    private void ReserveNavMapArea(short x, short y, short width, short height)
    {
        if (_reservedCount >= _reserved.Length)
            return;
        _reserved[_reservedCount++] = new Area(x, y, unchecked((short)(x + width)), unchecked((short)(y + height)));
    }

    /// <remarks>C: TryPlaceNavMapLabel (0x40D250, nav.c).</remarks>
    private bool TryPlaceLabel(short x, short y, short width, bool force)
    {
        if (LabelPositionAvailable(x, y, width, 6) || (force && LabelFits(x, y, width, 6)))
        {
            _labels[_labelCount].X = x;
            _labels[_labelCount].Y = y;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Finds a free spot for a label (right, below, left, below-right, above, moving out up to 12
    /// pixels; the last ring accepts any spot inside the map), reserves it and records the label.
    /// A label that finds no spot keeps the slot's previous position, like the original.
    /// </summary>
    /// <remarks>C: PlaceNavMapLabel (0x40D2C0, nav.c). Labels beyond the original's 20 slots are dropped.</remarks>
    private void PlaceNavMapLabel(short x, short y, byte colour, string text)
    {
        if (_labelCount >= _labels.Length)
            return;
        short width = unchecked((short)(text.Length * 4 + 2));
        _labels[_labelCount].Colour = colour;
        _labels[_labelCount].Text = text;
        short offset = -1;
        do
        {
            offset++;
            if (TryPlaceLabel((short)(x + offset + 4), y, width, false))
                break;
            bool force = offset == 12;
            if (TryPlaceLabel((short)(x - width / 2), (short)(y + offset + 5), width, force))
                break;
            if (TryPlaceLabel((short)(x - offset - width - 3), y, width, force))
                break;
            if (TryPlaceLabel(x, (short)(y + offset + 5), width, force))
                break;
            if (TryPlaceLabel((short)(x - width / 2), (short)(y - offset - 9), width, force))
                break;
        }
        while (offset != 12);
        ReserveNavMapArea(_labels[_labelCount].X, _labels[_labelCount].Y, width, 6);
        _labelCount++;
    }

    /// <summary>Labels an objective unless an earlier objective refers to the same mission ship index.</summary>
    /// <remarks>C: AddUniqueObjectiveNavLabel (0x40D410, nav.c).</remarks>
    private void AddUniqueObjectiveNavLabel(short x, short y, byte colour, string text, int objective, short missionShip)
    {
        if (missionShip != -1)
        {
            for (int previous = 0; previous < objective; previous++)
            {
                if (_mission.Objectives[previous].Index == missionShip)
                    return;
            }
        }
        PlaceNavMapLabel(x, y, colour, text);
    }

    /// <remarks>C: DrawNavMapLabels (0x40D540, nav.c): "%X%Y%F%s" per label.</remarks>
    private void DrawNavMapLabels()
    {
        for (int i = 0; i < _labelCount; i++)
            Gfx.DrawFormattedText("%X%Y%F%s"u8, _labels[i].X, _labels[i].Y, _labels[i].Colour, _labels[i].Text);
    }

    private struct Label
    {
        public short X;
        public short Y;
        public byte Colour;
        public string Text;
    }

    private readonly record struct Area(short Left, short Top, short Right, short Bottom);
}
