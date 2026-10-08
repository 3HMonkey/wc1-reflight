using WingCommander.Core.Numerics;
using WingCommander.Game.Flight.Cockpit;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// Cockpit instruments drawn on the screen over the cockpit art: lights, bars, digital readouts,
// the 3D scanner, the pilot's hand, damage decals and the cockpit explosion, the weapon launch
// animation (cockpt.c).
internal sealed partial class FlightSession
{
    private const int LightCount = 7;

    /// <remarks>C: abCockpitLightGoal[7].</remarks>
    private readonly byte[] _cockpitLightGoal = new byte[LightCount];

    /// <remarks>C: abCockpitLightState[7].</remarks>
    private readonly byte[] _cockpitLightState = new byte[LightCount];

    /// <summary>Screen alias whose rectangle clips one bar.</summary>
    /// <remarks>C: stCockpitBar.</remarks>
    private Viewport _cockpitBar = new();

    private readonly CockpitReadout[] _readouts = new CockpitReadout[CockpitTables.ReadoutCount];

    /// <remarks>C: aCockpitReadouts[6].</remarks>
    private struct CockpitReadout
    {
        public TextContext? Context;
        public short X;
        public short Y;
        public short PreviousRight;
    }

    // ------------------------------------------------------------------ lights

    /// <remarks>C: reset_cockpit (0x414410, cockpt.c).</remarks>
    public void ResetCockpitLights()
    {
        Array.Clear(_cockpitLightGoal);
        Array.Clear(_cockpitLightState);
    }

    /// <summary>Light state for tests (C: abCockpitLightGoal).</summary>
    public byte GetCockpitLightGoal(int light) => _cockpitLightGoal[light];

    /// <summary>Blinks a light: interval 0 toggles every call, below 20 every interval-th frame, 20 and more switch it off.</summary>
    /// <remarks>C: SetCockpitLightBlink (0x414440, cockpt.c).</remarks>
    private void SetCockpitLightBlink(int light, short interval)
    {
        if (interval < 20)
        {
            if (interval == 0 || Sim.SpaceFrame % interval == 0)
                _cockpitLightGoal[light] ^= 1;
        }
        else
        {
            _cockpitLightGoal[light] = 0;
        }
    }

    /// <summary>Every 4th rendered frame light 4 shows whether the autopilot is available; lights whose
    /// state changed are redrawn (all of them in cockpitless mode).</summary>
    /// <remarks>C: draw_cockpit_lights (0x414490, cockpt.c).</remarks>
    private void DrawCockpitLights()
    {
        if (Sim.RenderedSpaceFrame % 4 == 0)
            _cockpitLightGoal[4] = (byte)(AutoPilotValid(false) ? 1 : 0);
        int cockpit = CockpitIndex;
        bool cockpitless = Sim.CockpitlessView != 0;
        for (int light = 0; light < LightCount; light++)
        {
            if (cockpitless || _cockpitLightState[light] != _cockpitLightGoal[light])
            {
                short x = CockpitTables.LightX[cockpit][light];
                short y = CockpitTables.LightY[cockpit][light];
                int frame = _cockpitLightGoal[light] == 1
                    ? CockpitTables.LightOnFrame[cockpit][light]
                    : CockpitTables.LightOffFrame[cockpit][light];
                Gfx.DrawSpriteDefault(Screen, x, y, CockpitLightShape, frame);
                _cockpitLightState[light] = _cockpitLightGoal[light];
            }
        }
    }

    /// <summary>Fuel light and bar, gun energy bar, and the damage alarm (light 3 and sound 32).</summary>
    /// <remarks>C: update_lights (0x4145B0, cockpt.c). The fuel capacity is the 32-bit
    /// lifetime/weaponDamage pair of the type record. The KS alarm handle is never stored, so the
    /// alarm sound is requested every tick while the condition holds (literal).</remarks>
    private void UpdateLights()
    {
        var sim = Sim;
        ref readonly var player = ref sim.Ships[ObjectSlots.Player];
        int fuelCapacity = sim.TypeDataOf(ObjectSlots.Player).Fuel;
        short fuelPercent = fuelCapacity == 0 ? (short)0 : unchecked((short)(player.Fuel * 100 / fuelCapacity));
        SetCockpitLightBlink(6, fuelPercent);
        DrawBar(0, fuelPercent);
        DrawBar(1, player.WeaponEnergy);
        if (!sim.TrainSimActive)
        {
            bool alarm = sim.CalculateDamageLevel() >= 3 && player.Shield[ShieldValues.Aft] + player.Shield[ShieldValues.Fore] < 10;
            if (alarm)
                SetCockpitLightBlink(3, 2);
            if (Audio.Sfx.ServiceDamageAlarm(alarm, sim.SpaceFrame))
                _cockpitLightGoal[3] = 0;
        }
    }

    /// <summary>Missile on the player's tail: light 2 blinks and the missile music plays.</summary>
    /// <remarks>C: update_missile_warning (0x417190, cockpt.c).</remarks>
    private void UpdateMissileWarning()
    {
        if (Sim.MissileOnTail(ObjectSlots.Player))
        {
            SetCockpitLightBlink(2, 1);
            if (!Sim.TrainSimActive)
                Audio.SpaceTrack(3, 1, -1);
        }
        else
        {
            _cockpitLightGoal[2] = 0;
        }
    }

    // ------------------------------------------------------------------ bars

    /// <summary>
    /// Draws one instrument bar at <paramref name="percent"/>: the bar rectangle is split into a
    /// filled and an empty part, each drawn with its frame anchored at the bar's top-left.
    /// </summary>
    /// <remarks>C: vdu_polygon (0x413DA0, cockpt.c).</remarks>
    private void DrawBar(int bar, short percent)
    {
        var definition = CockpitTables.Bars[CockpitIndex][bar];
        short length = definition.Length;
        short extent = unchecked((short)(percent * length / 100));
        short left = definition.Left;
        var clip = _cockpitBar;
        clip.Left = left;
        if (left == CockpitTables.Disabled)
            return;
        short top = definition.Top, right = definition.Right, bottom = definition.Bottom;
        clip.Right = right;
        clip.Top = top;
        clip.Bottom = bottom;
        short filledFrame = definition.FilledFrame;
        short emptyFrame = definition.EmptyFrame;
        var shape = CockpitLightShape;
        if (definition.Direction < 2)
        {
            if (definition.Direction == 1)
            {
                extent = (short)(length - extent);
                (filledFrame, emptyFrame) = (emptyFrame, filledFrame);
            }
            clip.Bottom = (short)(clip.Bottom - extent);
            if (clip.Top <= clip.Bottom)
                Gfx.DrawSpriteDefault(clip, left, top, shape, filledFrame);
            clip.Top = (short)(clip.Bottom + 1);
            clip.Bottom = bottom;
            if (clip.Top <= bottom)
                Gfx.DrawSpriteDefault(clip, left, top, shape, emptyFrame);
        }
        else
        {
            if (definition.Direction == 3)
            {
                extent = (short)(length - extent);
                (filledFrame, emptyFrame) = (emptyFrame, filledFrame);
            }
            clip.Right = (short)(clip.Right - extent);
            if (clip.Left <= clip.Right)
                Gfx.DrawSpriteDefault(clip, left, top, shape, filledFrame);
            clip.Left = (short)(clip.Right + 1);
            clip.Right = right;
            if (clip.Left <= right)
                Gfx.DrawSpriteDefault(clip, left, top, shape, emptyFrame);
        }
    }

    /// <summary>Armour bars, shield bars and lights, numeric shield readouts.</summary>
    /// <remarks>C: update_bars (0x414690, cockpt.c). Armour index 2 is divided by the right capacity and
    /// index 3 by the left one, as in the original.</remarks>
    private void UpdateBars()
    {
        var sim = Sim;
        var typeData = sim.TypeDataOf(ObjectSlots.Player);
        ref readonly var player = ref sim.Ships[ObjectSlots.Player];
        DrawBar(2, Percent(player.Armor[ArmorValues.Front], typeData.ArmorFront));
        DrawBar(3, Percent(player.Armor[ArmorValues.Rear], typeData.ArmorRear));
        DrawBar(4, Percent(player.Armor[ArmorValues.Left], typeData.ArmorRight));
        DrawBar(5, Percent(player.Armor[ArmorValues.Right], typeData.ArmorLeft));
        short forePercent = Percent(player.Shield[ShieldValues.Fore], typeData.ShieldFore);
        SetCockpitLightBlink(0, forePercent);
        DrawBar(6, forePercent);
        DrawCockpitReadout(4, player.Shield[ShieldValues.Fore].ToString(System.Globalization.CultureInfo.InvariantCulture));
        short aftPercent = Percent(player.Shield[ShieldValues.Aft], typeData.ShieldAft);
        SetCockpitLightBlink(1, aftPercent);
        DrawBar(7, aftPercent);
        DrawCockpitReadout(5, player.Shield[ShieldValues.Aft].ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static short Percent(int value, int maximum) => maximum == 0 ? (short)0 : unchecked((short)(value * 100 / maximum));

    // ------------------------------------------------------------------ readouts

    /// <remarks>C: InitializeCockpitReadout (0x413F70, cockpt.c).</remarks>
    private void InitializeCockpitReadout(int slot, TextContext context)
    {
        _readouts[slot].Context = context;
        _readouts[slot].X = context.CursorX;
        _readouts[slot].Y = context.CursorY;
        _readouts[slot].PreviousRight = 0;
    }

    /// <summary>Draws a readout (the text is used as a format string) and erases what is left of the previous, longer value.</summary>
    /// <remarks>C: DrawCockpitReadout (0x413FB0, cockpt.c).</remarks>
    private void DrawCockpitReadout(int slot, string text)
    {
        ref var readout = ref _readouts[slot];
        if (readout.X == CockpitTables.Disabled || readout.Context is not { } context)
            return;
        Gfx.SetTextContext(context);
        Gfx.SetTextCursor(readout.X, readout.Y);
        Gfx.DrawFormattedText(text);
        int fontHeight = context.Font?.Height ?? 0;
        EraseCockpitReadoutRegion(Screen, context.CursorX, readout.Y, readout.PreviousRight,
            (short)(fontHeight + readout.Y - 1), PaletteColours.Black);
        readout.PreviousRight = context.CursorX;
    }

    /// <remarks>C: EraseCockpitReadoutRegion (0x413D40, cockpt.c): a cleared copy of the viewport, never a present.</remarks>
    private void EraseCockpitReadoutRegion(Viewport viewport, short left, short top, short right, short bottom, byte colour)
    {
        if (right >= left && bottom >= top)
        {
            var clipped = viewport.Clone();
            clipped.SetViewportRect(left, top, right, bottom);
            Gfx.ClearViewport(clipped, colour);
        }
    }

    /// <summary>Set speed and actual speed, both times ten.</summary>
    /// <remarks>C: update_digital_readouts (0x414A50, cockpt.c).</remarks>
    private void UpdateDigitalReadouts()
    {
        var sim = Sim;
        Gfx.SetTextContext(CockpitReadoutTextContext);
        ref readonly var player = ref sim.Objects[ObjectSlots.Player];
        int setSpeed = (short)((player.Speed >> 8) * 10);
        DrawCockpitReadout(2, setSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        int velocity = FixedMath.Multiply(player.Velocity.Magnitude(), 0xa00);
        int actualSpeed = (short)(velocity >> 8);
        DrawCockpitReadout(3, actualSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // ------------------------------------------------------------------ scanner

    /// <remarks>C: asScannerObjectX/Y[11], asScannerBackgroundColour[11] (index 10 = nav marker).</remarks>
    private readonly short[] _scannerObjectX = new short[11];
    private readonly short[] _scannerObjectY = new short[11];
    private readonly short[] _scannerBackgroundColour = new short[11];

    /// <remarks>C: nScannerTargetObject.</remarks>
    private short _scannerTargetObject = -1;

    /// <remarks>C: nScannerCursorX / nScannerCursorY.</remarks>
    private short _scannerCursorX;
    private short _scannerCursorY;

    /// <remarks>C: pScannerMarkerBackground.</remarks>
    private readonly byte[] _scannerMarkerBackground = new byte[256];

    /// <remarks>C: ResetScannerContacts (0x415A70, cockpt.c): slots 10..1 cleared (slot 0 is never used).</remarks>
    private void ResetScannerContacts()
    {
        for (int i = 10; i != 0; i--)
            _scannerObjectX[i] = 0;
        _scannerTargetObject = -1;
    }

    /// <summary>Restores last tick's scanner blips and the nav marker background.</summary>
    /// <remarks>C: clear_head_up_display (0x415A90, cockpt.c).</remarks>
    private void ClearHeadUpDisplay()
    {
        var screen = Screen;
        if (_scannerTargetObject != -1)
        {
            int obj = _scannerTargetObject;
            Gfx.DrawViewportPixel(screen, _scannerObjectX[obj], _scannerObjectY[obj], (byte)_scannerBackgroundColour[obj]);
            _scannerObjectX[_scannerTargetObject] = 0;
        }
        if (_scannerObjectX[10] != 0)
        {
            Gfx.RestoreSpriteBackground(screen, _scannerMarkerBackground, _scannerObjectX[10], _scannerObjectY[10], TargetLockShape, 2);
            _scannerObjectX[10] = 0;
        }
        for (int obj = 9; obj != 0; obj--)
        {
            if (_scannerObjectX[obj] != 0)
            {
                Gfx.DrawViewportPixel(screen, _scannerObjectX[obj], _scannerObjectY[obj], (byte)_scannerBackgroundColour[obj]);
                _scannerObjectX[obj] = 0;
            }
        }
        _scannerTargetObject = -1;
    }

    /// <summary>Spherical position to scanner pixel: yaw/4 within 45 degrees, else yaw/6; pitch/-3; clamped to the box.</summary>
    /// <remarks>C: rotational_pos_to_scanner_pos (0x4158F0, cockpt.c).</remarks>
    private void RotationalPositionToScannerPosition(int obj, in SphericalVector position)
    {
        var scanner = CockpitTables.Scanners[CockpitIndex];
        short horizontal = position.Yaw;
        _scannerCursorX = Math.Abs((int)horizontal) < 45
            ? (short)(scanner.CenterX + horizontal / 4)
            : (short)(scanner.CenterX + horizontal / 6);
        _scannerCursorY = (short)(scanner.CenterY + position.Pitch / -3);
        _scannerCursorX = Math.Max(scanner.MinimumX, Math.Min(scanner.MaximumX, _scannerCursorX));
        _scannerCursorY = Math.Max(scanner.MinimumY, Math.Min(scanner.MaximumY, _scannerCursorY));
        _scannerObjectX[obj] = _scannerCursorX;
        _scannerObjectY[obj] = _scannerCursorY;
    }

    /// <summary>Scanner colour of an object: fighters red/blue/neutral, capital ships orange/white/grey,
    /// missiles aimed at the player yellow; false for anything else.</summary>
    /// <remarks>C: get_color (0x415C00, cockpt.c).</remarks>
    private bool GetScannerColour(int obj, out byte colour)
    {
        var sim = Sim;
        colour = 0;
        var objectClass = sim.Objects[obj].Class;
        if (objectClass < ObjectClass.Missile)
            return false;
        if (objectClass == ObjectClass.Ship)
        {
            colour = sim.Ships[obj].Side switch
            {
                Side.Kilrathi => PaletteColours.Red,
                Side.Imperial => PaletteColours.Blue,
                _ => PaletteColours.PrimaryText,
            };
            return true;
        }
        if (objectClass == ObjectClass.CapitalShip)
        {
            if (sim.Ships[obj].Side == Side.Kilrathi)
                colour = PaletteColours.Orange;
            else if (sim.Objects[obj].Type == ObjectType.TigersClaw)
                colour = PaletteColours.ViewportClear;
            else
                colour = PaletteColours.DarkGrey;
            return true;
        }
        if (sim.Ships[obj].Target == ObjectSlots.Player)
        {
            colour = PaletteColours.Yellow;
            return true;
        }
        return false;
    }

    /// <summary>
    /// The 3D scanner: in cockpitless mode the radar grid; blips of ships 1..9 within 15000 units
    /// (the background pixel is saved; the current target blinks black on even frames); the
    /// objective marker while the right VDU shows navigation.
    /// </summary>
    /// <remarks>C: draw_3d_scanner (0x415CE0, cockpt.c).</remarks>
    private void Draw3dScanner()
    {
        var sim = Sim;
        var screen = Screen;
        if (sim.CockpitlessView != 0)
        {
            var scanner = CockpitTables.Scanners[CockpitIndex];
            int row = 0;
            var grid = CockpitTables.ScannerGridRows;
            for (int i = 0; grid[i] != -2; i++)
            {
                int value = grid[i];
                if (value == -1)
                {
                    row++;
                    continue;
                }
                Gfx.DrawViewportPixel(screen, scanner.CenterX + row, scanner.CenterY + value, PaletteColours.DarkGreen);
                if (value != 0)
                    Gfx.DrawViewportPixel(screen, scanner.CenterX + row, scanner.CenterY - value, PaletteColours.DarkGreen);
                if (row != 0)
                {
                    Gfx.DrawViewportPixel(screen, scanner.CenterX - row, scanner.CenterY + value, PaletteColours.DarkGreen);
                    if (value != 0)
                        Gfx.DrawViewportPixel(screen, scanner.CenterX - row, scanner.CenterY - value, PaletteColours.DarkGreen);
                }
            }
        }

        ClearHeadUpDisplay();
        _scannerTargetObject = sim.Ships[ObjectSlots.Player].Target;
        if (_scannerTargetObject != -1 && sim.Objects[_scannerTargetObject].Class < ObjectClass.Ship)
            _scannerTargetObject = -1;

        var spherical = default(SphericalVector);
        for (int obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (!GetScannerColour(obj, out byte colour))
                continue;
            VectorMath.RectangularToSpherical(sim.Objects[obj].ViewPosition, ref spherical);
            if (spherical.Radius < 0xea6000)
            {
                RotationalPositionToScannerPosition(obj, spherical);
                _scannerBackgroundColour[obj] = (short)Gfx.GetViewportPixel(screen, _scannerCursorX, _scannerCursorY);
                if (_scannerTargetObject != obj)
                    Gfx.DrawViewportPixel(screen, _scannerCursorX, _scannerCursorY, colour);
            }
        }

        if (GetVduMode(1) == 5)
        {
            SetObjectiveRange(true);
            Gfx.CaptureSpriteBackground(screen, _scannerMarkerBackground, _scannerCursorX, _scannerCursorY, TargetLockShape, 2);
            Gfx.DrawSpriteDefault(screen, _scannerCursorX, _scannerCursorY, TargetLockShape, 2);
        }

        if (_scannerTargetObject != -1 && GetScannerColour(_scannerTargetObject, out byte targetColour))
        {
            if ((Math.Abs((int)sim.SpaceFrame) & 1) == 0)
                targetColour = PaletteColours.Black;
            int obj = _scannerTargetObject;
            Gfx.DrawViewportPixel(screen, _scannerObjectX[obj], _scannerObjectY[obj], targetColour);
        }
    }

    /// <summary>Objective range (and, for the scanner, the objective's scanner position, slot 10).</summary>
    /// <remarks>C: set_objective_range (0x415B70, cockpt.c): the simulation computes the range, the scanner
    /// position is cockpit drawing.</remarks>
    private void SetObjectiveRange(bool showOnScanner)
    {
        var sim = Sim;
        int objective = sim.CurrentObjective;
        // Guard (see Requests in docs/progress/flight.md): without an objective (simulator missions)
        // the original reads aMissionObjectives[-1]; the simulation's SetObjectiveRange indexes the
        // table directly, so the range is left as it is.
        if ((uint)objective < (uint)sim.MissionObjectives.Length)
            sim.SetObjectiveRange(showOnScanner);
        if (!showOnScanner)
            return;
        var position = (uint)objective < (uint)sim.MissionObjectives.Length
            ? sim.MissionObjectives[objective].Position
            : default;
        ref readonly var player = ref sim.Objects[ObjectSlots.Player];
        var relative = VectorMath.Delta(player.Position, position);
        var rotated = player.TransformToObjectsFrame(relative);
        var spherical = default(SphericalVector);
        VectorMath.RectangularToSpherical(rotated, ref spherical);
        RotationalPositionToScannerPosition(10, spherical);
    }

    // ------------------------------------------------------------------ pilot hand

    /// <remarks>C: stPilotHand (screen alias), stPilotHandComposite, stPilotHandBackdrop.</remarks>
    private Viewport _pilotHand = new();
    private Viewport? _pilotHandComposite;
    private Viewport? _pilotHandBackdrop;

    /// <remarks>C: cRenderedPilotHandFrame (0xff = none).</remarks>
    private byte _renderedPilotHandFrame = 0xff;

    /// <remarks>C: bStickIndicatorFrame.</remarks>
    private byte _stickIndicatorFrame;

    /// <summary>The hand frame from the stick input: right 9..12, left 5..8, forward 13..16, back 1..4, centre 0.</summary>
    /// <remarks>C: determine_pilot_hand (0x4171D0, cockpt.c).</remarks>
    private void DeterminePilotHand()
    {
        short yaw = (short)(_yawInput / 2);
        short pitch = (short)(_pitchInput / 2);
        if (yaw > 0)
            _stickIndicatorFrame = (byte)Math.Min(yaw + 8, 12);
        else if (yaw < 0)
            _stickIndicatorFrame = (byte)Math.Min(4 - yaw, 8);
        else if (pitch > 0)
            _stickIndicatorFrame = (byte)Math.Min(pitch + 12, 16);
        else if (pitch < 0)
            _stickIndicatorFrame = (byte)Math.Min(-pitch, 4);
        else
            _stickIndicatorFrame = 0;
    }

    /// <summary>Composes backdrop, hand and sleeve off screen and copies the result to the screen.</summary>
    /// <remarks>C: DrawPilotHandFrame (0x417260, cockpt.c).</remarks>
    private void DrawPilotHandFrame()
    {
        if (_pilotHandComposite is not { IsAllocated: true } composite || _pilotHandBackdrop is not { IsAllocated: true } backdrop)
            return;
        var origin = CockpitTables.PilotHandOrigins[CockpitIndex];
        short x = (short)(origin.X - _pilotHand.Left);
        short y = (short)(origin.Y - _pilotHand.Top);
        Gfx.CopyViewportContents(backdrop, composite);
        Gfx.DrawSpriteDefault(composite, x, y, PilotHandShape, _stickIndicatorFrame);
        var offset = CockpitTables.PilotHandOffsets[_stickIndicatorFrame];
        Gfx.DrawSpriteDefault(composite, x + offset.X, y + offset.Y, PilotHandShape, 0x11);
        Gfx.CopyViewportContents(composite, _pilotHand);
        _renderedPilotHandFrame = _stickIndicatorFrame;
    }

    /// <remarks>C: animate_pilot (0x4173C0, cockpt.c).</remarks>
    private void AnimatePilot()
    {
        if (PilotHandShape is null)
            return;
        DeterminePilotHand();
        if (_renderedPilotHandFrame != _stickIndicatorFrame)
            DrawPilotHandFrame();
    }

    /// <summary>Grabs the hand backdrop from the freshly drawn cockpit art and draws the hand.</summary>
    /// <remarks>C: ResetPilotHandAnimation (0x4173F0, cockpt.c).</remarks>
    private void ResetPilotHandAnimation()
    {
        if (PilotHandShape is null || _pilotHandBackdrop is not { IsAllocated: true } backdrop)
            return;
        _renderedPilotHandFrame = 0xff;
        Gfx.CopyViewportContents(_pilotHand, backdrop);
        AnimatePilot();
    }

    /// <summary>In the simulator the right VDU overlaps the hand rectangle: its columns are copied into the
    /// hand backdrop so the hand animation does not erase them.</summary>
    /// <remarks>C: CopyTrainSimPilotViewToRightVdu (0x417320, cockpt.c), with the lazily built
    /// stTrainSimVduSource.</remarks>
    private void CopyTrainSimPilotViewToRightVdu()
    {
        if (_pilotHandBackdrop is not { IsAllocated: true } backdrop)
            return;
        var destination = backdrop.Clone();
        if (_trainSimVduSource.Left == 0)
        {
            _trainSimVduSource = _pilotHand.Clone();
            _trainSimVduSource.Left = RightVdu.Left;
            _trainSimVduSource.Top = _pilotHand.Top;
            _trainSimVduSource.Right = _pilotHand.Right;
            _trainSimVduSource.Bottom = RightVdu.Bottom;
        }
        destination.Left = (short)(_trainSimVduSource.Left - _pilotHand.Left);
        destination.Bottom = (short)(_trainSimVduSource.Bottom - _pilotHand.Top);
        Gfx.CopyViewportContents(_trainSimVduSource, destination);
    }

    /// <remarks>C: stTrainSimVduSource.</remarks>
    private Viewport _trainSimVduSource = new();

    // ------------------------------------------------------------------ damage decals and the cockpit explosion

    /// <remarks>C: anCockpitDamageState[4].</remarks>
    private readonly short[] _cockpitDamageState = new short[4];

    /// <remarks>C: nPendingCockpitDamage.</remarks>
    private short _pendingCockpitDamage;

    /// <summary>8 = no explosion, 0x7FFF = starting, 0..7 = frame.</summary>
    /// <remarks>C: nCockpitExplosionFrame.</remarks>
    private short _cockpitExplosionFrame = 8;

    /// <remarks>C: stCockpitExplosionPosition.</remarks>
    private ScreenPoint _cockpitExplosionPosition;

    /// <summary>The explosion shape is still loaded (the Kilrathi Saga frees it the first time no explosion runs).</summary>
    /// <remarks>C: pCockpitExplosionShape != 0.</remarks>
    private bool _cockpitExplosionShapeLoaded = true;

    /// <remarks>C: pCockpitExplosionBackground.</remarks>
    private readonly byte[] _cockpitExplosionBackground = new byte[4096];

    private ShapeTable? CockpitExplosionShape => _cockpitExplosionShapeLoaded ? Shapes.Get(Core.Resources.LogicalFile.CockpitVga, 5) : null;

    internal bool IsCockpitExplosionActive => _cockpitExplosionFrame < 8;

    /// <summary>Where the cockpit explosion and the pending decal are drawn (tests).</summary>
    internal (short X, short Y) CockpitExplosionPosition => (_cockpitExplosionPosition.X, _cockpitExplosionPosition.Y);

    /// <remarks>C: clear_cockpit_damage (0x417610, cockpt.c).</remarks>
    private void ClearCockpitDamage() => Array.Clear(_cockpitDamageState);

    /// <summary>Draws every damage decal that was placed (on cockpit set-up).</summary>
    /// <remarks>C: explosion_draw (0x417630, cockpt.c).</remarks>
    private void ExplosionDraw()
    {
        var decals = CockpitDecalShape;
        int cockpit = CockpitIndex;
        for (int damage = 0; damage < 4; damage++)
        {
            if (_cockpitDamageState[damage] == 1)
            {
                var position = CockpitTables.DamagePositions[cockpit][damage];
                Gfx.DrawSpriteDefault(Screen, position.X, position.Y, decals, damage);
            }
        }
    }

    /// <summary>Draws the pending decal on the screen and into the pilot-hand backdrop.</summary>
    /// <remarks>C: DrawPendingCockpitDamage (0x4176C0, cockpt.c).</remarks>
    private void DrawPendingCockpitDamage()
    {
        var decals = CockpitDecalShape;
        Gfx.DrawSpriteDefault(Screen, _cockpitExplosionPosition.X, _cockpitExplosionPosition.Y, decals, _pendingCockpitDamage);
        if (PilotHandShape is not null && _pilotHandBackdrop is { IsAllocated: true } backdrop)
        {
            Gfx.DrawSpriteDefault(backdrop, _cockpitExplosionPosition.X - _pilotHand.Left,
                _cockpitExplosionPosition.Y - _pilotHand.Top, decals, _pendingCockpitDamage);
        }
    }

    /// <remarks>C: RestoreCockpitExplosionBackground (0x417760, cockpt.c).</remarks>
    private void RestoreCockpitExplosionBackground()
    {
        if (IsCockpitExplosionActive && CockpitExplosionShape is { } shape)
        {
            Gfx.RestoreSpriteBackground(Screen, _cockpitExplosionBackground, _cockpitExplosionPosition.X,
                _cockpitExplosionPosition.Y, shape, _cockpitExplosionFrame);
        }
    }

    /// <remarks>C: RestoreCockpitExplosionIfVisible (0x416C90, cockpt.c).</remarks>
    private void RestoreCockpitExplosionIfVisible()
    {
        if (IsCockpitExplosionActive)
            RestoreCockpitExplosionBackground();
    }

    /// <summary>Advances the cockpit explosion: sound at frame 0, the decal at frame 3, frames 0..7 drawn
    /// with a saved background. Without a running explosion the Kilrathi Saga frees the shape.</summary>
    /// <remarks>C: cockpit_explosion (0x4177B0, cockpt.c). <see cref="FlightOptions.CockpitExplosionAnimation"/>
    /// keeps the shape loaded so the animation plays (DOS behaviour).</remarks>
    private void CockpitExplosion()
    {
        if (_cockpitExplosionFrame == 0x7fff)
            _cockpitExplosionFrame = 0;
        if (IsCockpitExplosionActive)
        {
            if (_cockpitExplosionFrame == 0)
                Audio.PlaySfx(0x1b);
            if (++_cockpitExplosionFrame == 3)
                DrawPendingCockpitDamage();
            if (IsCockpitExplosionActive && CockpitExplosionShape is { } shape)
            {
                Gfx.CaptureSpriteBackground(Screen, _cockpitExplosionBackground, _cockpitExplosionPosition.X,
                    _cockpitExplosionPosition.Y, shape, _cockpitExplosionFrame);
                Gfx.DrawSpriteDefault(Screen, _cockpitExplosionPosition.X, _cockpitExplosionPosition.Y, shape,
                    _cockpitExplosionFrame);
                _renderedPilotHandFrame = 0xff;
            }
            return;
        }
        if (!Options.CockpitExplosionAnimation)
            _cockpitExplosionShapeLoaded = false;
    }

    /// <summary>A hit damaged the cockpit (front view, not the simulator, decal not shown yet): the decal
    /// appears at once, or after the cockpit explosion when its shape is loaded.</summary>
    /// <remarks>C: place_damage_on_cockpit (0x4178A0, cockpt.c).</remarks>
    private void PlaceDamageOnCockpit(short damage)
    {
        var sim = Sim;
        if (sim.CameraViewMode != 0 || sim.TrainSimActive || (uint)damage >= 4 || _cockpitDamageState[damage] != 0)
            return;
        _pendingCockpitDamage = damage;
        _cockpitDamageState[damage] = 1;
        if (CockpitExplosionShape is null)
        {
            ExplosionDraw();
            return;
        }
        if (!IsCockpitExplosionActive)
        {
            _cockpitExplosionFrame = 0x7fff;
            _cockpitExplosionPosition = CockpitTables.DamagePositions[CockpitIndex][damage];
        }
    }

    // ------------------------------------------------------------------ weapon launch animation

    /// <remarks>C: cReleaseWeaponDisplayFrame (-1 = none), cReleaseWeaponDisplayTicks,
    /// cReleaseWeaponDisplayState, nReleaseWeaponDisplayX/Y, eReleaseWeaponDisplayType,
    /// nWeaponDisplayOriginX/Y, pReleaseWeaponDisplayBackground.</remarks>
    private sbyte _releaseWeaponDisplayFrame = -1;
    private sbyte _releaseWeaponDisplayTicks;
    private sbyte _releaseWeaponDisplayState;
    private short _releaseWeaponDisplayX;
    private short _releaseWeaponDisplayY;
    private ObjectType _releaseWeaponDisplayType = ObjectType.None;
    private short _weaponDisplayOriginX;
    private short _weaponDisplayOriginY;
    private readonly byte[] _releaseWeaponDisplayBackground = new byte[1024];

    /// <summary>The player launched a missile or mine: its weapon VDU sprite starts flying off the display.</summary>
    /// <remarks>C: the display part of RemovePlayerReleaseWeapon (0x414CB0, cockpt.c). The original had
    /// no save buffer (and no animation) when the loadout held no missile at cockpit set-up; the port
    /// always animates.</remarks>
    private void PlayerReleaseWeaponLaunched(ObjectType weaponType, short hardpoint)
    {
        _releaseWeaponDisplayType = weaponType;
        _releaseWeaponDisplayFrame = (sbyte)((int)weaponType * 2 - 0x2f);
        var position = (uint)hardpoint < (uint)CockpitTables.WeaponDisplayPositions.Length
            ? CockpitTables.WeaponDisplayPositions[hardpoint]
            : default;
        _releaseWeaponDisplayX = (short)(position.X + _weaponDisplayOriginX);
        _releaseWeaponDisplayY = (short)(position.Y + _weaponDisplayOriginY);
        _releaseWeaponDisplayTicks = 3;
        _releaseWeaponDisplayState = 0;
    }

    /// <summary>Moves the launched weapon's sprite up (mines down) the weapon display, faster each tick.</summary>
    /// <remarks>C: fire_computer_graphic_missile (0x414D50, cockpt.c).</remarks>
    private void FireComputerGraphicMissile()
    {
        if (_releaseWeaponDisplayFrame == -1)
            return;
        var shape = CockpitWeaponShape;
        bool visible = Sim.CameraViewMode == 0 && GetVduMode(0) == 1;
        if (_releaseWeaponDisplayState != 0)
        {
            Gfx.RestoreSpriteBackground(LeftVdu, _releaseWeaponDisplayBackground, _releaseWeaponDisplayX,
                _releaseWeaponDisplayY, shape, _releaseWeaponDisplayFrame);
        }
        if (_releaseWeaponDisplayY > LeftVdu.Top - 10 && _releaseWeaponDisplayY < LeftVdu.Bottom)
        {
            if (_releaseWeaponDisplayType == ObjectType.SpaceMine)
                _releaseWeaponDisplayY += _releaseWeaponDisplayTicks;
            else
                _releaseWeaponDisplayY -= _releaseWeaponDisplayTicks;
            _releaseWeaponDisplayTicks++;
            if (visible)
            {
                Gfx.CaptureSpriteBackground(LeftVdu, _releaseWeaponDisplayBackground, _releaseWeaponDisplayX,
                    _releaseWeaponDisplayY, shape, _releaseWeaponDisplayFrame);
                Gfx.DrawSpriteDefault(LeftVdu, _releaseWeaponDisplayX, _releaseWeaponDisplayY, shape, _releaseWeaponDisplayFrame);
            }
            _releaseWeaponDisplayState = (sbyte)(visible ? 1 : 0);
        }
        else
        {
            _releaseWeaponDisplayFrame = -1;
        }
    }
}
