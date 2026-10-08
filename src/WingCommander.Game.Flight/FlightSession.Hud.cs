using WingCommander.Core.Numerics;
using WingCommander.Game.Flight.Cockpit;
using WingCommander.Game.Screens.Ui;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The HUD drawn into the space buffer (target brackets, lock spiral, gunsight, mouse crosshair,
// the HUD message line) and the on-screen messages (cockpt.c, hudmsg.c).
internal sealed partial class FlightSession
{
    /// <summary>A rectangle the HUD remembers (left -0x7FFF = none).</summary>
    private struct BracketBounds
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
    }

    /// <remarks>C: stTargetBracketBounds / stPreviousTargetBracketBounds.</remarks>
    private BracketBounds _targetBracketBounds = new() { Left = -0x7fff };
    private BracketBounds _previousTargetBracketBounds = new() { Left = -0x7fff };

    /// <remarks>C: nTargetLockMarkerX / nTargetLockMarkerY (-0x7FFF = not drawn).</remarks>
    private short _targetLockMarkerX = -0x7fff;
    private short _targetLockMarkerY;

    /// <remarks>C: bTargetBracketVisible.</remarks>
    private byte _targetBracketVisible = 1;

    /// <summary>The object whose yellow speaker brackets were drawn (0 initially, like the global).</summary>
    /// <remarks>C: cPreviousTargetObject.</remarks>
    private sbyte _previousTargetObject;

    /// <remarks>C: pszPendingHudMessage / pszDisplayedHudMessage / DAT_005a7f00 (the message colour).</remarks>
    private HudText? _pendingHudMessage;
    private HudText? _displayedHudMessage;
    private byte _hudMessageColour;

    /// <remarks>C: szHudMessageBuffer.</remarks>
    private readonly HudText _hudMessageBuffer = new("");

    private static readonly HudText VideoSuppressedText = new("VIDEO IMAGES SUPRESSED");
    private static readonly HudText VideoEnabledText = new("VIDEO IMAGES ENABLED");
    private static readonly HudText MissileCameraOnText = new("MISSILE CAMERA ON");
    private static readonly HudText MissileCameraOffText = new("MISSILE CAMERA OFF");
    private static readonly HudText EmptyHudText = new("");

    /// <summary>Rendered frames the HUD message stays (&gt; 0 = a message or comm transmission shows).</summary>
    /// <remarks>C: nMessageTimer.</remarks>
    private short _messageTimer;

    /// <summary>0..4; the HUD message duration is <c>(min(5, length/2) + 5) * (speed + 1)</c>.</summary>
    /// <remarks>C: bMessageSpeed (2 initially).</remarks>
    public byte MessageSpeed { get; set; } = 2;

    /// <summary>The nav map is open (HUD messages are ignored).</summary>
    /// <remarks>C: bInflightComputerActive.</remarks>
    private bool _inflightComputerActive;

    /// <summary>Mouse steering is active (the crosshair is drawn into the space buffer).</summary>
    /// <remarks>C: bMouseCursorVisible.</remarks>
    private bool _mouseCursorVisible;

    /// <remarks>C: nSavedMouseCursorX/Y, abMouseCursorBackground.</remarks>
    private short _savedMouseCursorX;
    private short _savedMouseCursorY;
    private readonly byte[] _mouseCursorBackground = new byte[512];

    /// <summary>The pending HUD message text (tests).</summary>
    public string? PendingHudMessage => _pendingHudMessage?.Value;

    // ------------------------------------------------------------------ message timer

    /// <remarks>C: message_showing (0x4149F0, cockpt.c).</remarks>
    public bool MessageShowing() => _messageTimer > 0;

    /// <remarks>C: clear_message_time (0x4149E0, cockpt.c).</remarks>
    private void ClearMessageTime() => _messageTimer = 0;

    /// <summary>Counts the HUD message down by one rendered frame; at 0 the message (or transmission) ends.</summary>
    /// <remarks>C: check_message (0x414A20, cockpt.c).</remarks>
    private void CheckMessage()
    {
        if (MessageShowing())
        {
            _messageTimer--;
            if (_messageTimer <= 0)
                EndCommMenu();
        }
    }

    /// <summary>Dwell time of a message in rendered frames.</summary>
    /// <remarks>C: MeasureMessageWidth (0x428E70, hudmsg.c).</remarks>
    private short MeasureMessageWidth(string text) =>
        (short)((Math.Min(5, CLength(text) >> 1) + 5) * (MessageSpeed + 1));

    private static int CLength(string text)
    {
        int end = text.IndexOf('\0', StringComparison.Ordinal);
        return end < 0 ? text.Length : end;
    }

    // ------------------------------------------------------------------ HUD message line

    /// <summary>Shows a message on the HUD line for <paramref name="duration"/> rendered frames (ignored while the nav map runs).</summary>
    /// <remarks>C: SetHudMessageText (0x416DE0, cockpt.c).</remarks>
    private void SetHudMessageText(HudText text, byte colour, short duration)
    {
        if (_inflightComputerActive)
            return;
        if (MessageShowing())
            SetHudTextColour(true);
        _hudMessageColour = colour;
        _pendingHudMessage = text;
        _messageTimer = duration;
    }

    /// <remarks>C: ShowHudTextLine (0x416460, cockpt.c).</remarks>
    private void ShowHudTextLine(HudText? text, byte colour)
    {
        _pendingHudMessage = text;
        PrintMessageText(text, colour);
    }

    /// <summary>Erases the displayed message by redrawing it in the space colour (ending a comm session first when asked).</summary>
    /// <remarks>C: SetHudTextColour (0x416480, cockpt.c).</remarks>
    private void SetHudTextColour(bool endComm)
    {
        if (endComm)
            EndCommMenu();
        PrintMessageText(_displayedHudMessage, PaletteColours.PrimaryViewBuffer);
    }

    /// <summary>
    /// Draws the HUD message line into the space buffer, centred in a 60-row band under the
    /// cockpit's message origin, with the original's manual word wrapping (including the quirk
    /// that writes the line break ahead of the output, where later characters overwrite it).
    /// </summary>
    /// <remarks>C: print_message_text (0x416260, cockpt.c).</remarks>
    private void PrintMessageText(HudText? text, byte colour)
    {
        if (text is null)
            return;
        string sourceText = text.Value;
        int length = Math.Min(CLength(sourceText), 83);
        int cockpit = CockpitIndex;
        var origin = CockpitTables.HudMessageOrigins[cockpit];
        var viewport = SpaceBuffer.Clone();
        viewport.SetViewportRect(origin.X, origin.Y, 319 - origin.X, origin.Y + 60);
        var context = HudMessageTextContext.Clone();
        context.Viewport = viewport;
        context.BackgroundColour = 0xff;
        context.Colour = colour;

        Span<byte> wrapped = stackalloc byte[256];
        wrapped.Clear();
        int charactersPerLine = (short)((viewport.Right - viewport.Left) / 6);
        short lastSpace = -1;
        int output = 0;
        for (short position = 0; position < length; position++, output++)
        {
            char c = sourceText[position];
            wrapped[output] = c <= 0xff ? (byte)c : (byte)'?';
            if (c == ' ')
                lastSpace = position;
            if (charactersPerLine != 0 && (position + 1) % charactersPerLine == 0)
            {
                if (lastSpace == -1)
                {
                    wrapped[output + 1] = (byte)'\n';
                    output++;
                }
                else
                {
                    wrapped[output + position - lastSpace] = (byte)'\n';
                }
            }
        }
        wrapped[output] = 0;
        ReadOnlySpan<byte> drawn = wrapped[..output];

        if (Sim.CockpitlessView != 0)
        {
            short y = cockpit switch
            {
                0 => (short)(origin.Y + 10),
                1 => (short)(origin.Y + 25),
                2 => (short)(origin.Y + 50),
                3 => origin.Y,
                _ => short.MinValue,
            };
            if (y != short.MinValue)
                Gfx.DrawTextAt(context, origin.X, y, drawn, TextContext.AlignCentre);
        }
        else
        {
            Gfx.DrawTextAt(context, origin.X, origin.Y, drawn, TextContext.AlignCentre);
        }
        _displayedHudMessage = _pendingHudMessage;
    }

    /// <summary>
    /// A message on the HUD line (or, with duration 9999, a modal panel that waits for a key,
    /// e.g. "GAME PAUSED"); duration 0 means the dwell time of the text.
    /// </summary>
    /// <remarks>C: ShowOnScreenMessage (0x428FA0, hudmsg.c). The formatted text is at most 51 characters.</remarks>
    private async ValueTask ShowOnScreenMessageAsync(bool waitForKeyCycle, short duration, string text)
    {
        if (text.Length > 51)
            text = text[..51];
        Events.FlushInputEvents();
        short messageDuration = duration;
        ModalTextPanel? panel = null;
        if (messageDuration == 9999)
        {
            BakeShownSpaceSprites();
            panel = await ModalPrompts.ShowModalTextPanelAsync(Game, 1, text);
        }
        if (panel is null)
        {
            if (messageDuration == 0)
                messageDuration = MeasureMessageWidth(text);
            SetHudTextColour(true);
            _hudMessageBuffer.Value = text;
            SetHudMessageText(_hudMessageBuffer, PaletteColours.Red, messageDuration);
            if (messageDuration == 9999)
            {
                ShowHudTextLine(_hudMessageBuffer, PaletteColours.Red);
                DumpBufferToScreen();
            }
        }
        if (messageDuration == 9999)
            await Events.WaitForKeyAcknowledgeAsync(waitForKeyCycle ? 1 : 0);
        if (panel is not null)
        {
            await ModalPrompts.ReleaseModalTextPanelAsync(Game, panel);
            return;
        }
        if (messageDuration == 9999)
            SetHudMessageText(EmptyHudText, PaletteColours.Red, 2);
    }

    /// <summary>The synchronous form of a non-modal on-screen message (volume keys and the like).</summary>
    private void ShowOnScreenMessage(string text)
    {
        if (text.Length > 51)
            text = text[..51];
        Events.FlushInputEvents();
        short messageDuration = MeasureMessageWidth(text);
        SetHudTextColour(true);
        _hudMessageBuffer.Value = text;
        SetHudMessageText(_hudMessageBuffer, PaletteColours.Red, messageDuration);
    }

    /// <summary>P: modal "GAME PAUSED" panel; Ctrl+P: wait without a panel.</summary>
    /// <remarks>C: ShowGamePausedBanner (0x4290A0, hudmsg.c), then SetFrameTimerPeriodDirect(1).</remarks>
    private async ValueTask ShowGamePausedBannerAsync(bool showBanner)
    {
        if (showBanner)
            await ShowOnScreenMessageAsync(true, 9999, "GAME PAUSED");
        else
            await Events.WaitForKeyAcknowledgeAsync(1);
        Game.Timing.SetFrameTimerPeriod(1);
    }

    /// <summary>
    /// Esc (port, ADR-015): the pause menu over the cockpit; the flight stands still while it is
    /// open. Afterwards the frame timer restarts like after the original pause and steering goes
    /// back to the keyboard (the pointer moved in the menu).
    /// </summary>
    private async ValueTask<PauseMenuChoice> ShowPauseMenuAsync(PauseMenuContext context)
    {
        PauseMenuChoice choice = await Game.ShowPauseMenuAsync(context);
        Game.Timing.SetFrameTimerPeriod(1);
        InitPlayerInput();
        ShowFlightKeyHelp(); // the keys may have changed in the settings
        return choice;
    }

    /// <remarks>C: ShowVersionBanner (0x4290D0, hudmsg.c).</remarks>
    private ValueTask ShowVersionBannerAsync() =>
        ShowOnScreenMessageAsync(true, 9999, $"WING COMMANDER VER. {Wc1Game.GameVersion}");

    /// <remarks>C: SetMessageDisplaySpeed (0x4290F0, hudmsg.c).</remarks>
    private void SetMessageDisplaySpeed()
    {
        MessageSpeed = (byte)((MessageSpeed + 1) % 5);
        ShowOnScreenMessage($"MESSAGES SPEED IS NOW {MessageSpeed + 1}.");
    }

    /// <remarks>C: ReportFramesSkipped (0x429120, hudmsg.c).</remarks>
    private void ReportFramesSkipped(short adjustment)
    {
        Sim.FrameSkip = Math.Min(Math.Max((short)(Sim.FrameSkip + adjustment), (short)1), (short)5);
        ShowOnScreenMessage($"{Sim.FrameSkip - 1} FRAMES SKIPPED.");
    }

    // ------------------------------------------------------------------ HUD in the space buffer

    /// <summary>
    /// The HUD of the front view, drawn into the space buffer after the objects: speaker
    /// brackets (yellow) while a comm message shows, the target brackets (red, blue for a friend;
    /// solid in lock mode; blinking when locked) with the lock spiral, the gunsight, the message
    /// line and the mouse crosshair. The missile lock itself is simulation (in PrepareSpaceView).
    /// </summary>
    /// <remarks>C: overlay_head_up_display (0x416AC0, cockpt.c) after target_locking.</remarks>
    private void OverlayHeadUpDisplay()
    {
        var sim = Sim;
        if (MessageShowing() && _commSpeakerObject != -1)
        {
            _previousTargetObject = (sbyte)_commSpeakerObject;
            DrawTargetBox(PaletteColours.Yellow, _previousTargetObject, false, false, 2, ref _previousTargetBracketBounds);
        }
        sbyte target = sim.Ships[ObjectSlots.Player].Target;
        if (sim.TargetLockCountdown == 0)
        {
            if ((short)(sim.RenderedSpaceFrame % 2) == 0)
                _targetBracketVisible ^= 1;
            if (_targetBracketVisible == 1)
                DrawTargetBox(PaletteColours.Red, target, sim.TargetLockMode != 0, true, 1, ref _targetBracketBounds);
        }
        else
        {
            DrawTargetBox(PaletteColours.Red, target, sim.TargetLockMode != 0, true, 1, ref _targetBracketBounds);
        }

        var gunsight = TargetLockShape;
        if (sim.CockpitlessView != 0)
        {
            switch (CockpitIndex)
            {
                case 0:
                case 2:
                    Gfx.DrawSpriteDefault(SpaceBuffer, sim.ViewCenterX, sim.ViewCenterY, gunsight, 0);
                    break;
                case 1:
                    Gfx.DrawSpriteDefault(SpaceBuffer, sim.ViewCenterX, sim.ViewCenterY - 1, gunsight, 0);
                    break;
                case 3:
                    Gfx.DrawSpriteDefault(SpaceBuffer, sim.ViewCenterX, sim.ViewCenterY + 14, gunsight, 0);
                    break;
            }
        }
        else
        {
            Gfx.DrawSpriteDefault(SpaceBuffer, sim.ViewCenterX, sim.ViewCenterY, gunsight, 0);
        }

        if (_pendingHudMessage is not null)
            ShowHudTextLine(_pendingHudMessage, _hudMessageColour);
        if (_mouseCursorVisible)
        {
            var cursor = Events.Cursor;
            _savedMouseCursorX = cursor.X;
            _savedMouseCursorY = cursor.Y;
            var shape = Game.Cursor.Shape;
            Gfx.CaptureSpriteBackground(SpaceBuffer, _mouseCursorBackground, cursor.X, cursor.Y, shape, cursor.Frame);
            Gfx.DrawSpriteDefault(SpaceBuffer, cursor.X, cursor.Y, shape, cursor.Frame);
        }
    }

    /// <summary>
    /// Draws (or with the space colour, erases) brackets around an object: the corners of its
    /// transformed sprite bounds plus padding, or a full rectangle (solid). With a lock marker the
    /// lock spiral is drawn: radius 2 x countdown pixels, turning with the player's roll and pitch.
    /// </summary>
    /// <remarks>C: draw_target_box (0x4164B0, cockpt.c).</remarks>
    private void DrawTargetBox(byte colour, int obj, bool solid, bool drawLockMarker, short padding, ref BracketBounds saved)
    {
        var sim = Sim;
        bool valid;
        BracketBounds bounds;
        short centerX = 0, centerY = 0;
        if (colour == PaletteColours.PrimaryViewBuffer)
        {
            valid = saved.Left != -0x7fff;
            bounds = saved;
        }
        else
        {
            bounds = default;
            valid = obj != -1 && sim.Objects[obj].ScreenX != -0x7fff;
            if (valid)
            {
                ref readonly var o = ref sim.Objects[obj];
                centerX = (short)(o.ScreenX + sim.ViewCenterX);
                centerY = (short)(o.ScreenY + sim.ViewCenterY);
                Span<short> box = stackalloc short[4];
                ShapeTable? shape = obj == sim.NavPointerObject ? TargetLockShape : Shapes.Get(o.Shape);
                if ((short)ShapeBounds.GetTransformedShapeBounds(SpaceBuffer, centerX, centerY, shape, o.ViewFrame,
                        o.ScreenAngle, o.ScreenScale, o.Flip, box) != 0)
                {
                    bounds.Left = (short)(box[0] - padding);
                    bounds.Top = (short)(box[1] - padding);
                    bounds.Right = (short)(box[2] + padding);
                    bounds.Bottom = (short)(box[3] + padding);
                }
                else
                {
                    valid = false;
                }
            }
        }
        if (!valid)
        {
            saved.Left = -0x7fff;
            return;
        }
        if (colour == PaletteColours.Red && (uint)obj < ObjectSlots.ShipSlotCount &&
            sim.Ships[obj].Side == sim.Ships[ObjectSlots.Player].Side)
            colour = PaletteColours.Blue;
        var buffer = SpaceBuffer;
        if (solid)
        {
            if (!TryRecordBorder(bounds, colour, obj))
                Gfx.DrawViewportBorder(buffer, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, colour);
        }
        else
        {
            short segment = (short)((bounds.Right - bounds.Left) / 6 + 1);
            DrawBracketLine(bounds.Left, bounds.Top, bounds.Left + segment, bounds.Top, colour, obj);
            DrawBracketLine(bounds.Left, bounds.Bottom, bounds.Left + segment, bounds.Bottom, colour, obj);
            DrawBracketLine(bounds.Right, bounds.Top, bounds.Right - segment, bounds.Top, colour, obj);
            DrawBracketLine(bounds.Right, bounds.Bottom, bounds.Right - segment, bounds.Bottom, colour, obj);
            segment = (short)((bounds.Bottom - bounds.Top) / 6 + 1);
            DrawBracketLine(bounds.Left, bounds.Top, bounds.Left, bounds.Top + segment, colour, obj);
            DrawBracketLine(bounds.Left, bounds.Bottom, bounds.Left, bounds.Bottom - segment, colour, obj);
            DrawBracketLine(bounds.Right, bounds.Top, bounds.Right, bounds.Top + segment, colour, obj);
            DrawBracketLine(bounds.Right, bounds.Bottom, bounds.Right, bounds.Bottom - segment, colour, obj);
        }
        if (drawLockMarker)
        {
            if (colour != PaletteColours.PrimaryViewBuffer)
            {
                if (sim.TargetLockCountdown > -1)
                {
                    ref readonly var player = ref sim.Objects[ObjectSlots.Player];
                    sim.TargetLockMarkerAngle = unchecked((short)(sim.TargetLockMarkerAngle + player.RollRotation + player.PitchRotation));
                    centerX = unchecked((short)(centerX + (FixedMath.Cos(sim.TargetLockMarkerAngle) * sim.TargetLockCountdown * 2 >> 8)));
                    centerY = unchecked((short)(centerY + (FixedMath.Sin(sim.TargetLockMarkerAngle) * sim.TargetLockCountdown * 2 >> 8)));
                    if (!TryRecordSpaceSprite(TargetLockShape, 1, centerX, centerY, 0, 0x100, 0, obj))
                        Gfx.DrawSpriteDefault(buffer, centerX, centerY, TargetLockShape, 1);
                    _targetLockMarkerX = centerX;
                    _targetLockMarkerY = centerY;
                }
            }
            else if (_targetLockMarkerX != -0x7fff)
            {
                Gfx.DrawSolidColourSprite(buffer, _targetLockMarkerX, _targetLockMarkerY, TargetLockShape, 1,
                    PaletteColours.PrimaryViewBuffer);
                _targetLockMarkerX = -0x7fff;
            }
        }
        if (colour == PaletteColours.PrimaryViewBuffer)
            saved.Left = -0x7fff;
        else
            saved = bounds;
    }

    /// <summary>
    /// One bracket stroke: a sprite tied to the object while the renderer draws the space view (it
    /// moves with the object between ticks, R2b), otherwise a line in the space buffer. Erasing
    /// (the space colour) always draws, which is harmless where nothing was drawn.
    /// </summary>
    private void DrawBracketLine(int x0, int y0, int x1, int y1, byte colour, int obj)
    {
        if (colour == PaletteColours.PrimaryViewBuffer || !TryRecordLineSprite(x0, y0, x1, y1, colour, obj))
            Gfx.DrawViewportLine(SpaceBuffer, x0, y0, x1, y1, colour);
    }

    /// <summary>The solid lock-mode frame as four line sprites (see <see cref="DrawBracketLine"/>); false when the CPU draws it.</summary>
    private bool TryRecordBorder(BracketBounds bounds, byte colour, int obj)
    {
        if (colour == PaletteColours.PrimaryViewBuffer || !TryRecordLineSprite(bounds.Left, bounds.Top, bounds.Right, bounds.Top, colour, obj))
            return false;
        TryRecordLineSprite(bounds.Left, bounds.Bottom, bounds.Right, bounds.Bottom, colour, obj);
        if (bounds.Bottom - bounds.Top > 1)
        {
            TryRecordLineSprite(bounds.Left, bounds.Top + 1, bounds.Left, bounds.Bottom - 1, colour, obj);
            TryRecordLineSprite(bounds.Right, bounds.Top + 1, bounds.Right, bounds.Bottom - 1, colour, obj);
        }
        return true;
    }

    /// <summary>
    /// After the dump: restores the crosshair background, erases the brackets (which resets their
    /// saved state), erases a message that is no longer pending and redraws a running cockpit
    /// explosion on the screen.
    /// </summary>
    /// <remarks>C: RestoreTransientCockpitGraphics (0x416CB0, cockpt.c).</remarks>
    private void RestoreTransientCockpitGraphics()
    {
        var sim = Sim;
        if (_mouseCursorVisible)
        {
            Gfx.RestoreSpriteBackground(SpaceBuffer, _mouseCursorBackground, _savedMouseCursorX, _savedMouseCursorY,
                Game.Cursor.Shape, Events.Cursor.Frame);
        }
        if (_previousTargetObject != -1)
        {
            DrawTargetBox(PaletteColours.PrimaryViewBuffer, _previousTargetObject, false, false, 2, ref _previousTargetBracketBounds);
            _previousTargetObject = -1;
        }
        DrawTargetBox(PaletteColours.PrimaryViewBuffer, sim.Ships[ObjectSlots.Player].Target, sim.TargetLockMode != 0, true, 1,
            ref _targetBracketBounds);
        if (!ReferenceEquals(_displayedHudMessage, _pendingHudMessage) && _displayedHudMessage is not null)
            SetHudTextColour(false);
        if (IsCockpitExplosionActive && CockpitExplosionShape is { } shape)
        {
            if (sim.CockpitlessView == 0)
            {
                Gfx.CaptureSpriteBackground(Screen, _cockpitExplosionBackground, _cockpitExplosionPosition.X,
                    _cockpitExplosionPosition.Y, shape, _cockpitExplosionFrame);
                Gfx.DrawSpriteDefault(Screen, _cockpitExplosionPosition.X, _cockpitExplosionPosition.Y, shape,
                    _cockpitExplosionFrame);
            }
            _renderedPilotHandFrame = 0xff;
        }
    }
}
