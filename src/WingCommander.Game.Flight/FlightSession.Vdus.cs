using WingCommander.Game.Flight.Cockpit;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Text;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

/// <summary>
/// A HUD text whose identity matters: the original compares message pointers, so constant
/// texts are shared instances and formatted buffers are one instance whose text changes.
/// </summary>
internal sealed class HudText(string value)
{
    public string Value { get; set; } = value;

    public override string ToString() => Value;
}

// The VDUs: mode stack, page drawing, malfunction static, the two message slots (cockpt.c,
// sound.c show_damage_disp/UpdateDamageDisplay, music.c show_target_disp/DrawTargetRangeReadout).
internal sealed partial class FlightSession
{
    /// <summary>Screen alias of the left VDU (weapons, damage).</summary>
    /// <remarks>C: stLeftVdu.</remarks>
    public Viewport LeftVdu { get; } = new();

    /// <summary>Screen alias of the right VDU (target, comm, navigation, transmission).</summary>
    /// <remarks>C: stRightVdu.</remarks>
    public Viewport RightVdu { get; } = new();

    /// <remarks>C: stLeftVduTextContext (font 2, primary text colour on black).</remarks>
    public TextContext LeftVduTextContext { get; } = new();

    /// <remarks>C: stRightVduTextContext.</remarks>
    public TextContext RightVduTextContext { get; } = new();

    /// <remarks>C: ausVduModeStack (four entries per VDU) and acVduModeStackDepth.</remarks>
    private readonly int[,] _vduModeStack = new int[2, 4];
    private readonly int[] _vduModeStackDepth = new int[2];

    /// <summary>Last drawn mode per VDU (0 forces a redraw).</summary>
    /// <remarks>C: anVduModeCache[2].</remarks>
    private readonly int[] _vduModeCache = new int[2];

    private readonly HudMessageSlot[] _hudMessageSlots = [new HudMessageSlot(), new HudMessageSlot()];

    /// <remarks>C: HudMessageSlot aHudMessageSlots[2]: 0 = left VDU line, 1 = right VDU line.</remarks>
    private sealed class HudMessageSlot
    {
        public TextContext? Context;
        public short X;
        public short Y;
        public HudText? Text;
        public byte Colour;
        public byte DrawColour;
        public sbyte FlashCount;
    }

    // Texts of the message slots (C: the global strings whose pointers the slots compare).
    private static readonly HudText MissileLockedText = new("MISSILE LOCKED ");
    private static readonly HudText AlreadyNearText = new("Already Near");
    private static readonly HudText EnemyNearText = new("Enemy Near");
    private static readonly HudText HazardNearText = new("Hazard Near");
    private static readonly HudText ObjectiveReachedText = new("Objective Reached");
    private static readonly HudText AlreadyVisitedText = new("Already Visited");
    private static readonly HudText CommSelectText = new("SELECT");
    private static readonly HudText CommChooseText = new("CHOOSE");

    /// <remarks>C: szObjectiveStatusMessage ("Wait for %s").</remarks>
    private readonly HudText _objectiveStatusMessage = new("Wait for ??????????????????");

    /// <remarks>C: szComponentHitMessage.</remarks>
    private readonly HudText _componentHitMessage = new("");

    /// <remarks>C: the xorshift32 state of SdlDrawViewportStatic (sdl/video.c).</remarks>
    private uint _staticNoiseSeed = 0x1f123bb5u;

    // ------------------------------------------------------------------ mode stack

    /// <summary>Mode of a VDU: 0 broken, 1 weapons, 2 damage, 3 target, 4 comm menu, 5 navigation, 6 comm video, 8 info.</summary>
    /// <remarks>C: get_mode (0x4147E0, cockpt.c).</remarks>
    public int GetVduMode(int vdu) => _vduModeStack[vdu, _vduModeStackDepth[vdu]];

    /// <remarks>C: set_mode (0x414800, cockpt.c).</remarks>
    private void SetMode(int vdu, int mode)
    {
        if (GetVduMode(vdu) != mode)
            ClearHudMessageSlot(_hudMessageSlots[vdu]);
        _vduModeStackDepth[vdu] = 0;
        _vduModeStack[vdu, 0] = mode;
    }

    /// <remarks>C: GetVduModeStackDepth (0x414890, cockpt.c).</remarks>
    private int GetVduModeStackDepth(int vdu) => _vduModeStackDepth[vdu];

    /// <remarks>C: push_mode (0x4148A0, cockpt.c).</remarks>
    private void PushMode(int vdu, int mode)
    {
        ClearHudMessageSlot(_hudMessageSlots[vdu]);
        _vduModeStackDepth[vdu]++;
        _vduModeStack[vdu, _vduModeStackDepth[vdu]] = mode;
    }

    /// <remarks>C: pop_mode (0x4148E0, cockpt.c).</remarks>
    private void PopMode(int vdu)
    {
        ClearHudMessageSlot(_hudMessageSlots[vdu]);
        _vduModeStackDepth[vdu]--;
    }

    /// <summary>Clears a VDU for a new page (static when broken) and remembers its mode.</summary>
    /// <remarks>C: set_new_vdu (0x414910, cockpt.c).</remarks>
    private void SetNewVdu(int vdu)
    {
        if (GetVduMode(vdu) == 0)
            MalfNoise(vdu, 1, PaletteColours.DarkGreen, 0x17, false);
        else
            Gfx.ClearViewport(vdu == 1 ? RightVdu : LeftVdu, PaletteColours.Black);
        _vduModeCache[vdu] = GetVduMode(vdu);
    }

    /// <remarks>C: update_vid_disp (0x414980, cockpt.c).</remarks>
    private bool UpdateVidDisp(int vdu)
    {
        bool changed = GetVduMode(vdu) != _vduModeCache[vdu];
        if (changed)
            SetNewVdu(vdu);
        return changed;
    }

    /// <summary>Forces the VDU's page to be redrawn on the next update.</summary>
    /// <remarks>C: InvalidateVduMode (0x4149C0, cockpt.c).</remarks>
    public void InvalidateVduMode(int vdu) => _vduModeCache[vdu] = 0;

    /// <summary>Left VDU weapons, right VDU navigation (target in the simulator).</summary>
    /// <remarks>C: init_vdus (0x4244E0, logic.c).</remarks>
    private void InitializeVdus()
    {
        RightVdu.CopyFrom(Screen);
        LeftVdu.CopyFrom(Screen);
        LeftVduTextContext.Viewport = LeftVdu;
        LeftVduTextContext.TextBuffer = DefaultTextBuffer;
        Gfx.InitializeTextContextFromFont(LeftVduTextContext, 2, PaletteColours.PrimaryText, PaletteColours.Black);
        SetMode(0, 1);
        _vduModeCache[0] = 0;
        RightVduTextContext.Viewport = RightVdu;
        RightVduTextContext.TextBuffer = DefaultTextBuffer;
        Gfx.InitializeTextContextFromFont(RightVduTextContext, 2, PaletteColours.PrimaryText, PaletteColours.Black);
        SetMode(1, Sim.TrainSimActive ? 3 : 5);
        _vduModeCache[1] = 0;
    }

    // ------------------------------------------------------------------ malfunctions

    /// <summary>VDU static: the static sound (sound 23) or another sound, static noise, optionally a fresh page.</summary>
    /// <remarks>C: malf_noise (0x416E20, cockpt.c). The Kilrathi Saga's snow_viewport draws nothing and its
    /// static sound is a Saga WAV; <see cref="FlightOptions.VduStaticNoise"/> draws the SDL port's static and
    /// plays the DOS sound 23.</remarks>
    private void MalfNoise(int vdu, int effect, byte colour, int sound, bool refresh)
    {
        var viewport = vdu == 0 ? LeftVdu : RightVdu;
        if (sound != -1)
        {
            if (sound == 0x17)
            {
                if (Options.VduStaticNoise)
                    Audio.PlaySfx(0x17);
                else
                    Audio.Sfx.PlaySnowStaticSound();
            }
            else
            {
                Audio.PlaySfx(sound);
            }
        }
        SnowViewport(viewport, effect, colour);
        if (refresh)
            SetNewVdu(vdu);
    }

    /// <summary>Static noise with the SDL port's own xorshift32 generator (never the game's random numbers).</summary>
    /// <remarks>C: snow_viewport (0x442300, gr.c) + SdlDrawViewportStatic (sdl/video.c).</remarks>
    private void SnowViewport(Viewport viewport, int effect, byte colour)
    {
        Gfx.SnowViewport(viewport, effect, colour);
        if (!Options.VduStaticNoise || !viewport.IsAllocated)
            return;
        short bright = (short)(effect >= 3 ? 1 : 2);
        for (int y = viewport.Top; y <= viewport.Bottom; y++)
        {
            for (int x = viewport.Left; x <= viewport.Right; x++)
            {
                _staticNoiseSeed ^= _staticNoiseSeed << 13;
                _staticNoiseSeed ^= _staticNoiseSeed >> 17;
                _staticNoiseSeed ^= _staticNoiseSeed << 5;
                uint sample = _staticNoiseSeed >> 16;
                if ((short)(sample & 3) > bright)
                    continue;
                Gfx.DrawViewportPixel(viewport, x, y, (sample & 4) != 0 ? colour : PaletteColours.Black);
            }
        }
    }

    /// <summary>A VDU breaks: static (in the cockpit view) and mode 0.</summary>
    /// <remarks>C: vdu_malf (0x414B20, cockpt.c).</remarks>
    private void VduMalfunction(int vdu, int sound)
    {
        if (Sim.CameraViewMode == 0)
            MalfNoise(vdu, 1, PaletteColours.DarkGreen, sound, false);
        SetMode(vdu, 0);
    }

    /// <remarks>C: update_dead_disp (0x417B10, cockpt.c).</remarks>
    private void UpdateDeadDisplay(int vdu) => MalfNoise(vdu, 1, PaletteColours.DarkGreen, 0x17, false);

    // ------------------------------------------------------------------ message slots

    /// <summary>Draws a slot's text, blinking on the 60 Hz clock (one third dark); counts the flashes.</summary>
    /// <remarks>C: DrawHudMessageSlot (0x4140A0, cockpt.c).</remarks>
    private bool DrawHudMessageSlot(HudMessageSlot slot)
    {
        if (Sim.CameraViewMode != 0)
            return true;
        byte oldDrawColour = slot.DrawColour;
        slot.DrawColour = (int)Game.Timing.Ticks60Hz / 40 % 3 == 0 ? PaletteColours.Black : slot.Colour;
        if (slot.FlashCount != -1)
        {
            if (slot.DrawColour == PaletteColours.Black && oldDrawColour == slot.Colour)
                slot.FlashCount = (sbyte)Math.Max(0, slot.FlashCount - 1);
            if (slot.FlashCount == 0)
                slot.DrawColour = PaletteColours.Black;
        }
        bool showingEraseColour = slot.DrawColour == PaletteColours.Black;
        if (slot.Context is { } context)
        {
            byte savedColour = context.Colour;
            context.Colour = slot.DrawColour;
            Gfx.DrawTextAt(context, slot.X, slot.Y, slot.Text?.Value ?? "", TextContext.AlignCentre);
            context.Colour = savedColour;
        }
        return showingEraseColour;
    }

    /// <remarks>C: ClearHudMessageSlot (0x414180, cockpt.c).</remarks>
    private void ClearHudMessageSlot(HudMessageSlot slot)
    {
        slot.FlashCount = 0;
        if (slot.Text is not null)
            DrawHudMessageSlot(slot);
        slot.Text = null;
    }

    /// <summary>Clears both message slots without drawing (entering a nav sphere).</summary>
    /// <remarks>C: ClearHudGunReadouts (0x4141D0, cockpt.c).</remarks>
    private void ClearHudGunReadouts()
    {
        foreach (var slot in _hudMessageSlots)
        {
            slot.Text = null;
            slot.FlashCount = 0;
        }
    }

    /// <remarks>C: SetHudMessageSlot (0x4141F0, cockpt.c).</remarks>
    private void SetHudMessageSlot(HudMessageSlot slot, TextContext context, short x, short y, HudText text, byte colour,
        sbyte flashCount)
    {
        if (slot.Text is not null)
            ClearHudMessageSlot(slot);
        slot.Context = context;
        slot.X = x;
        slot.Y = y;
        slot.Text = text;
        slot.Colour = colour;
        slot.DrawColour = colour;
        slot.FlashCount = flashCount;
    }

    /// <remarks>C: UpdateMessage (0x414240, cockpt.c).</remarks>
    private void UpdateMessage(HudMessageSlot slot)
    {
        if (slot.Text is null)
            return;
        bool showingEraseColour = DrawHudMessageSlot(slot);
        if (slot.FlashCount == 0 && showingEraseColour)
            ClearHudMessageSlot(slot);
    }

    /// <summary>Right VDU message line.</summary>
    /// <remarks>C: set_global_message (0x414270, cockpt.c).</remarks>
    private void SetGlobalMessage(HudText text, byte colour, int flashCount) =>
        SetHudMessageSlot(_hudMessageSlots[1], RightVduTextContext, RightVdu.Left, (short)(RightVdu.Bottom - 6), text,
            colour, (sbyte)flashCount);

    /// <summary>Right VDU message unless the same text already shows.</summary>
    /// <remarks>C: CockpitMessage (0x4142B0, cockpt.c).</remarks>
    private void CockpitMessage(HudText text, byte colour, int flashCount)
    {
        if (!ReferenceEquals(text, _hudMessageSlots[1].Text))
            SetGlobalMessage(text, colour, flashCount);
    }

    /// <remarks>C: remove_message (0x4142E0, cockpt.c) / ClearHudMessageIfMatching (0x4141B0).</remarks>
    private void RemoveMessage(HudText text)
    {
        var slot = _hudMessageSlots[1];
        if (ReferenceEquals(slot.Text, text))
            ClearHudMessageSlot(slot);
    }

    /// <summary>Left VDU message line (component hit and repair messages), not in the simulator and only with a working left VDU.</summary>
    /// <remarks>C: ShowComponentHitHudMessage (0x414B70, cockpt.c).</remarks>
    private void ShowComponentHitHudMessage(string text, byte colour, int flashCount)
    {
        if (Sim.TrainSimActive || GetVduMode(0) == 0)
            return;
        var slot = _hudMessageSlots[0];
        if (slot.Text is not null)
            ClearHudMessageSlot(slot);
        _componentHitMessage.Value = text;
        SetHudMessageSlot(slot, LeftVduTextContext, LeftVdu.Left, (short)(LeftVdu.Bottom - 6), _componentHitMessage, colour,
            (sbyte)flashCount);
    }

    /// <summary>Text of a message slot (tests; null when the slot is empty).</summary>
    public string? GetMessageSlotText(int slot) => _hudMessageSlots[slot].Text?.Value;

    // ------------------------------------------------------------------ VDU update and selection

    /// <summary>
    /// Every tick in the cockpit view: redraws a VDU whose page changed (or keeps its live parts
    /// up to date), then the message line under it. Cockpitless mode first blanks both VDU areas
    /// (the space view covered them) and redraws the pages every tick.
    /// </summary>
    /// <remarks>C: update_VDUs (0x417B70, cockpt.c). Mode 8 (the unreachable info page) is not ported.</remarks>
    private void UpdateVdus()
    {
        var sim = Sim;
        bool cockpitless = sim.CockpitlessView != 0;
        Gfx.SetTextContext(LeftVduTextContext);
        if (cockpitless)
        {
            var left = CockpitTables.LeftVduBounds[CockpitIndex];
            Gfx.DrawFilledViewportRect(Screen, left.Left, left.Top, left.Right, left.Bottom, PaletteColours.Black);
            var right = CockpitTables.RightVduBounds[CockpitIndex];
            Gfx.DrawFilledViewportRect(Screen, right.Left, right.Top, right.Right, right.Bottom, PaletteColours.Black);
        }
        if (UpdateVidDisp(0))
        {
            switch (GetVduMode(0))
            {
                case 0:
                    UpdateDeadDisplay(0);
                    break;
                case 1:
                    ShowWeaponDisplay();
                    break;
                case 2:
                    ShowDamageDisplay();
                    break;
            }
        }
        else
        {
            switch (GetVduMode(0))
            {
                case 0:
                    UpdateDeadDisplay(0);
                    break;
                case 1:
                    if (cockpitless)
                        ShowWeaponDisplay();
                    break;
                case 2:
                    if (cockpitless)
                        ShowDamageDisplay(); // bForceDamageDisplayRedraw is set around it but never read
                    UpdateDamageDisplay();
                    break;
            }
        }
        if (GetVduMode(0) == 0)
            _hudMessageSlots[0].Text = null;
        else
            UpdateMessage(_hudMessageSlots[0]);

        Gfx.SetTextContext(RightVduTextContext);
        if (UpdateVidDisp(1))
        {
            switch (GetVduMode(1))
            {
                case 0:
                    UpdateDeadDisplay(1);
                    break;
                case 3:
                    ShowTargetDisplay();
                    break;
                case 4:
                    ShowCommunicationsDisplay();
                    break;
                case 5:
                    ShowNavigationDisplay();
                    break;
                case 6:
                    VidTransmit();
                    break;
            }
        }
        else
        {
            switch (GetVduMode(1))
            {
                case 0:
                    UpdateDeadDisplay(1);
                    break;
                case 3:
                    if (cockpitless)
                        ShowTargetDisplay();
                    DrawTargetRangeReadout();
                    break;
                case 4:
                    if (cockpitless)
                        ShowCommunicationsDisplay();
                    TalkEquiv();
                    break;
                case 5:
                    if (cockpitless)
                        ShowNavigationDisplay();
                    CheckObjectives();
                    break;
                case 6:
                    VidTransmit();
                    break;
            }
        }
        if (GetVduMode(1) is 6 or 0)
            _hudMessageSlots[1].Text = null;
        else
            UpdateMessage(_hudMessageSlots[1]);
        if (sim.TrainSimActive && PilotHandShape is not null)
            CopyTrainSimPilotViewToRightVdu();
    }

    /// <summary>Pops every pushed page of a VDU (a comm video page ends through <see cref="EndCommMenu"/>).</summary>
    /// <remarks>C: vdu_pop_all (0x417F10, cockpt.c).</remarks>
    private void VduPopAll(int vdu)
    {
        while (GetVduModeStackDepth(vdu) > 0)
        {
            if (GetVduMode(vdu) != 6)
                PopMode(vdu);
            else
                EndCommMenu();
        }
    }

    /// <summary>
    /// A VDU key (or the fire key while navigation shows): in the cockpit view, a malfunction test
    /// may break the VDU; otherwise the click sound, then a new page, or the page's own action
    /// when it already shows (next weapon/gun, next damaged component, next target, refresh the
    /// comm menu). Selecting navigation while it shows opens the nav map, which only the N key's
    /// asynchronous path can do.
    /// </summary>
    /// <returns>True when the nav map must be opened (mode 5 selected while showing).</returns>
    /// <remarks>C: SelectCockpitVduMode (0x417F60, cockpt.c); random draws through malf (3, and 4 for the comm menu).</remarks>
    private bool SelectCockpitVduModeCore(int vdu, int mode)
    {
        var sim = Sim;
        if (sim.CameraViewMode != 0)
            return false;
        if (sim.Malf(3) || (mode == 4 && sim.Malf(4)))
        {
            VduMalfunction(vdu, 0x17);
            return false;
        }
        Audio.Sfx.PlayCockpitSelectionSfx(vdu == 0 ? (short)0x7f : (short)0);
        if (GetVduMode(vdu) != mode)
        {
            VduPopAll(vdu);
            InvalidateVduMode(vdu);
            if (mode != 4)
            {
                SetMode(vdu, mode);
                UpdateVdus();
                return false;
            }
            ShowCommunicationsDisplay();
            UpdateVdus();
            return false;
        }
        switch (mode)
        {
            case 1:
                if ((sbyte)_currentKey == 0x22)
                    sim.SelectNewGun();
                else
                    sim.SelectNewReleaseWeapon(ObjectType.None);
                break;
            case 2:
                _damageDisplayTicks = 0;
                break;
            case 3:
                sim.CycleOnscreenTargets();
                break;
            case 4:
                TalkEquiv();
                break;
            case 5:
                return true;
        }
        return false;
    }

    /// <summary>Synchronous VDU selection (keys and the simulation's fire key callback). Mode 5 while
    /// navigation shows would open the nav map; the callers that can reach it use
    /// <see cref="SelectCockpitVduModeAsync"/>.</summary>
    private void SelectCockpitVduMode(int vdu, int mode) => SelectCockpitVduModeCore(vdu, mode);

    /// <summary>VDU selection that can open the nav map (N, the autopilot's navigation switch).</summary>
    private async ValueTask SelectCockpitVduModeAsync(int vdu, int mode)
    {
        if (SelectCockpitVduModeCore(vdu, mode))
            await InflightComputerAsync();
    }

    // ------------------------------------------------------------------ weapons page

    /// <summary>"WEAPON DISPLAY": selected release weapon, gun, ship outline and every loadout slot.</summary>
    /// <remarks>C: show_weapon_disp (0x414EA0, cockpt.c).</remarks>
    private void ShowWeaponDisplay()
    {
        var sim = Sim;
        SetNewVdu(0);
        Gfx.DrawTextAt(LeftVduTextContext, LeftVdu.Left, LeftVdu.Top, "WEAPON DISPLAY", TextContext.AlignCentre);
        Gfx.DrawViewportLine(LeftVdu, LeftVdu.Left + 2, LeftVdu.Top + 5, LeftVdu.Right - 2, LeftVdu.Top + 5,
            PaletteColours.PrimaryText);
        ref readonly var weapons = ref sim.Ships[ObjectSlots.Player].Weapons;
        string releaseName = "";
        if (sim.SelectedReleaseWeaponIndex != -1)
            releaseName = ObjectTypeTable.Get(weapons.GetWeaponType(sim.SelectedReleaseWeaponIndex)).DisplayName;
        string gunName = (int)sim.SelectedGunType switch
        {
            -1 => "",
            0x80 => "Full Guns",
            _ => ObjectTypeTable.Get(sim.SelectedGunType).DisplayName,
        };
        Gfx.DrawFormattedText("\nWeapon: %s", releaseName);
        Gfx.DrawFormattedText("\nGun: %s", gunName);
        _weaponDisplayOriginX = (short)(LeftVdu.Left + CockpitTables.WeaponDisplayOrigin.X);
        _weaponDisplayOriginY = (short)(LeftVdu.Top + CockpitTables.WeaponDisplayOrigin.Y);
        var shape = CockpitWeaponShape;
        Gfx.DrawSpriteDefault(LeftVdu, _weaponDisplayOriginX, _weaponDisplayOriginY, shape, 0);
        int count = weapons.Count;
        for (int slot = 0; slot < count; slot++)
        {
            int hardpoint = weapons.GetHardpoint(slot);
            var position = (uint)hardpoint < (uint)CockpitTables.WeaponDisplayPositions.Length
                ? CockpitTables.WeaponDisplayPositions[hardpoint]
                : default;
            int frame = (int)weapons.GetWeaponType(slot) * 2 + weapons.GetDisabled(slot) - 0x2f;
            Gfx.DrawSpriteDefault(LeftVdu, _weaponDisplayOriginX + position.X, _weaponDisplayOriginY + position.Y, shape, frame);
        }
    }

    // ------------------------------------------------------------------ damage page

    /// <remarks>C: cDamagedComponentCount, nDamageDisplayTicks, nDamageDisplayPhase, cDamageDisplayComponent,
    /// nDisplayedComponentDamage, cDamageDisplayFrame, stDamageSpritePosition, szDamageStatusText,
    /// pDamageDisplayBackground.</remarks>
    private sbyte _damagedComponentCount;
    private short _damageDisplayTicks;
    private short _damageDisplayPhase;
    private sbyte _damageDisplayComponent;
    private int _displayedComponentDamage;
    private sbyte _damageDisplayFrame;
    private ScreenPoint _damageSpritePosition;
    private string _damageStatusText = "";
    private readonly byte[] _damageDisplayBackground = new byte[4096];

    /// <summary>"DAMAGE REPORT": "NO INTERNAL DAMAGE", or the ship outline and the count of damaged units.</summary>
    /// <remarks>C: show_damage_disp (0x42C800, sound.c).</remarks>
    private void ShowDamageDisplay()
    {
        var sim = Sim;
        _damagedComponentCount = 0;
        for (int component = 0; component < 9; component++)
        {
            if (sim.PlayerComponentDamage[component] >= 1)
                _damagedComponentCount++;
        }
        SetNewVdu(0);
        Gfx.DrawTextAt(LeftVduTextContext, LeftVdu.Left, LeftVdu.Top, "DAMAGE REPORT", TextContext.AlignCentre);
        Gfx.DrawViewportLine(LeftVdu, LeftVdu.Left + 2, LeftVdu.Top + 6, LeftVdu.Right - 2, LeftVdu.Top + 6,
            PaletteColours.PrimaryText);
        if (_damagedComponentCount == 0)
        {
            Gfx.DrawTextAt(LeftVduTextContext, LeftVdu.Left, LeftVdu.Top + 20, "NO INTERNAL\n\nDAMAGE", TextContext.AlignCentre);
            return;
        }
        _weaponDisplayOriginX = (short)(CockpitTables.WeaponDisplayOrigin.X + LeftVdu.Left);
        _weaponDisplayOriginY = (short)(CockpitTables.WeaponDisplayOrigin.Y + LeftVdu.Top);
        Gfx.DrawSpriteDefault(LeftVdu, _weaponDisplayOriginX, _weaponDisplayOriginY, CockpitWeaponShape, 0);
        string message = $"{_damagedComponentCount} Unit{(_damagedComponentCount == 1 ? ' ' : 's')} Damaged";
        ShowComponentHitHudMessage(message, PaletteColours.PrimaryText, -1);
    }

    /// <summary>
    /// Every tick on the damage page: a changed count redraws the page; otherwise the damaged
    /// components cycle: shown 50 ticks (name, severity, sprite and a line to it), erased for 2.
    /// Cockpitless mode redraws every tick and skips the erase phase.
    /// </summary>
    /// <remarks>C: UpdateDamageDisplay (0x42C970, sound.c).</remarks>
    private void UpdateDamageDisplay()
    {
        var sim = Sim;
        sbyte count = 0;
        for (int component = 0; component < 9; component++)
        {
            if (sim.PlayerComponentDamage[component] >= 1)
                count++;
        }
        if (count != _damagedComponentCount)
        {
            _damagedComponentCount = count;
            InvalidateVduMode(0);
            return;
        }
        _damagedComponentCount = count;
        if (count == 0)
            return;

        var shape = CockpitWeaponShape;
        if (sim.CockpitlessView == 0)
        {
            _damageDisplayTicks--;
            if (_damageDisplayTicks > 0)
                return;
            if (_damageDisplayPhase == 1)
            {
                sbyte component = _damageDisplayComponent;
                sbyte damage = 0;
                _damageDisplayTicks = 50;
                for (int attempts = 0; attempts < 9; attempts++)
                {
                    component++;
                    if (component >= 9)
                        component = 0;
                    damage = sim.PlayerComponentDamage[component];
                    _displayedComponentDamage = damage;
                    if (_displayedComponentDamage >= 1)
                    {
                        _damageDisplayComponent = component;
                        break;
                    }
                }
                _damageStatusText = $"{CockpitTables.ComponentNames[_damageDisplayComponent]}\nDamage: {SeverityName(damage)}";
                Gfx.DrawTextAt(LeftVduTextContext, LeftVdu.Left + 1, LeftVdu.Top + 7, _damageStatusText, TextContext.AlignCentre);
                PlaceDamageSprite();
                Gfx.CaptureSpriteBackground(LeftVdu, _damageDisplayBackground, _damageSpritePosition.X, _damageSpritePosition.Y,
                    shape, _damageDisplayFrame);
                Gfx.DrawViewportLine(LeftVdu, LeftVdu.Left + 36, LeftVdu.Top + 22, _damageSpritePosition.X,
                    _damageSpritePosition.Y, 0xa9);
                Gfx.DrawSpriteDefault(LeftVdu, _damageSpritePosition.X, _damageSpritePosition.Y, shape, _damageDisplayFrame);
            }
            else
            {
                Gfx.RestoreSpriteBackground(LeftVdu, _damageDisplayBackground, _damageSpritePosition.X, _damageSpritePosition.Y,
                    shape, _damageDisplayFrame);
                LeftVduTextContext.Colour = PaletteColours.Black;
                Gfx.DrawTextAt(LeftVduTextContext, LeftVdu.Left + 1, LeftVdu.Top + 7, _damageStatusText, TextContext.AlignCentre);
                LeftVduTextContext.Colour = PaletteColours.PrimaryText;
                Gfx.DrawViewportLine(LeftVdu, LeftVdu.Left + 36, LeftVdu.Top + 22, _damageSpritePosition.X,
                    _damageSpritePosition.Y, PaletteColours.Black);
                _damageDisplayTicks = 2;
            }
            _damageDisplayPhase = (short)(_damageDisplayPhase == 0 ? 1 : 0);
            return;
        }

        _damageDisplayTicks--;
        if (_damageDisplayTicks <= 0)
        {
            sbyte component = _damageDisplayComponent;
            _damageDisplayTicks = 50;
            for (int attempts = 0; attempts < 9; attempts++)
            {
                component++;
                if (component >= 9)
                    component = 0;
                _displayedComponentDamage = sim.PlayerComponentDamage[component];
                if (_displayedComponentDamage >= 1)
                {
                    _damageDisplayComponent = component;
                    return;
                }
            }
            return;
        }
        _damageStatusText = $"{CockpitTables.ComponentNames[_damageDisplayComponent]}\nDamage: {SeverityName(_displayedComponentDamage)}";
        Gfx.DrawTextAt(LeftVduTextContext, LeftVdu.Left + 1, LeftVdu.Top + 7, _damageStatusText, TextContext.AlignCentre);
        PlaceDamageSprite();
        Gfx.CaptureSpriteBackground(LeftVdu, _damageDisplayBackground, _damageSpritePosition.X, _damageSpritePosition.Y,
            shape, _damageDisplayFrame);
        Gfx.DrawViewportLine(LeftVdu, LeftVdu.Left + 36, LeftVdu.Top + 22, _damageSpritePosition.X, _damageSpritePosition.Y, 0xa9);
        Gfx.DrawSpriteDefault(LeftVdu, _damageSpritePosition.X, _damageSpritePosition.Y, shape, _damageDisplayFrame);
    }

    private void PlaceDamageSprite()
    {
        _damageDisplayFrame = (sbyte)(21 + _damageDisplayComponent);
        var position = CockpitTables.DamageDisplayPositions[_damageDisplayComponent];
        _damageSpritePosition = new ScreenPoint((short)(position.X + _weaponDisplayOriginX),
            (short)(position.Y + _weaponDisplayOriginY));
    }

    private static string SeverityName(int damage) =>
        (uint)damage < (uint)CockpitTables.DamageSeverityNames.Length ? CockpitTables.DamageSeverityNames[damage] : "";

    // ------------------------------------------------------------------ target page

    /// <remarks>C: cTargetDisplayObject.</remarks>
    private sbyte _targetDisplayObject = -1;

    /// <summary>
    /// "LOCKED TARGET" / "AUTO TARGETTING", the target's name and range label, and (when it is on
    /// screen) its silhouette with shield arcs and the armour quadrants (damaged quadrants in the
    /// second frame).
    /// </summary>
    /// <remarks>C: show_target_disp (0x42DB90, music.c).</remarks>
    private void ShowTargetDisplay()
    {
        var sim = Sim;
        Gfx.DrawTextAt(RightVduTextContext, RightVdu.Left, RightVdu.Top, "", TextContext.AlignCentre);
        if (sim.TargetLockMode != 0)
            Gfx.DrawFormattedText("%F%s%F", PaletteColours.Red, "   LOCKED TARGET", 0xa8);
        else
            Gfx.DrawFormattedText("%F%s", 0xa8, "  AUTO TARGETTING");
        int target = sim.Ships[ObjectSlots.Player].Target;
        if (target != -1 && (sim.Objects[target].Class < ObjectClass.Ship ||
                             sim.Ships[target].SpecialManeuver == SpecialManeuver.Unknown9))
        {
            target = -1;
            sim.Ships[ObjectSlots.Player].Target = -1;
        }
        _targetDisplayObject = (sbyte)target;
        Gfx.DrawFormattedText("\nTarget:");
        if (target == -1)
        {
            Gfx.DrawFormattedText(" None");
            return;
        }
        var type = sim.Objects[target].Type;
        var typeData = ObjectTypeTable.Get(type);
        sbyte rating = sim.Ships[target].Rating;
        if (rating is >= 0 and <= 7)
            Gfx.DrawFormattedText(" %s", WingmanCallsign(rating));
        else if (rating is >= 9 and <= 12)
            Gfx.DrawFormattedText(" %s", CockpitTables.KilrathiAceNames[rating - 9]);
        else
            Gfx.DrawFormattedText(" %s", typeData.DisplayName);
        Gfx.DrawFormattedText("\nRange : ");
        InitializeCockpitReadout(1, RightVduTextContext);
        if (sim.Objects[target].ScreenX == ObjectSlots.NotVisible)
        {
            _targetDisplayObject = -1;
            return;
        }

        short x = (short)(RightVdu.Left + 0x25);
        short y = (short)(RightVdu.Top + 0x26);
        ref readonly var ship = ref sim.Ships[target];
        var indicator = CockpitIndicatorShape;
        short frame = (short)((3 - Math.Min(typeData.ShieldAft == 0 ? 3 : ship.Shield[ShieldValues.Aft] * 6 / typeData.ShieldAft, 3)) * 2);
        if (frame < 6)
            Gfx.DrawSpriteDefault(RightVdu, x, y, indicator, frame);
        var silhouette = Shapes.Get(sim.TypeResources[(int)type].Shape);
        short[] maximumArmor = [typeData.ArmorFront, typeData.ArmorRear, typeData.ArmorLeft, typeData.ArmorRight];
        var quadrant = RightVdu.Clone();
        for (int armor = 0; armor < 4; armor++)
        {
            var clip = CockpitTables.TargetArmorClipRects[armor];
            quadrant.SetViewportRect(clip.Left + x, clip.Top + y, clip.Right + x, clip.Bottom + y);
            int quadrantFrame = ship.Armor[armor] > (short)(maximumArmor[armor] >> 1) ? 0 : 1;
            Gfx.DrawSpriteDefault(quadrant, x, y, silhouette, quadrantFrame);
        }
        Gfx.DrawSpriteDefault(RightVdu, x, y, silhouette, 2);
        frame = (short)((3 - Math.Min(typeData.ShieldFore == 0 ? 3 : ship.Shield[ShieldValues.Fore] * 6 / typeData.ShieldFore, 3)) * 2);
        if (frame < 6)
            Gfx.DrawSpriteDefault(RightVdu, x, y, indicator, frame + 1);
    }

    /// <summary>
    /// Every tick on the target page: a dying target is dropped; a new target or every 8th
    /// rendered frame redraws the page; the range readout ("----- m" off screen, "TOO FAR"
    /// beyond 30000); the lock readout erase.
    /// </summary>
    /// <remarks>C: DrawTargetRangeReadout (0x42DEA0, music.c), SDL port guard for target -1.</remarks>
    private void DrawTargetRangeReadout()
    {
        var sim = Sim;
        int target = sim.Ships[ObjectSlots.Player].Target;
        if (target != -1 && sim.Ships[target].SpecialManeuver == SpecialManeuver.Unknown9)
        {
            sim.Ships[ObjectSlots.Player].Target = -1;
            InvalidateVduMode(1);
            return;
        }
        if (target != -1 && sim.Objects[target].Class < ObjectClass.Ship)
        {
            sim.Ships[ObjectSlots.Player].Target = -1;
            target = -1;
        }
        if (_targetDisplayObject != target || (short)(sim.RenderedSpaceFrame % 8) == 0)
        {
            SetNewVdu(1);
            ShowTargetDisplay();
        }
        if (target == -1)
            return;
        string rangeText;
        if (sim.Objects[target].ScreenX == ObjectSlots.NotVisible)
            rangeText = "----- m";
        else if ((ushort)sim.Objects[target].Distance <= 30000)
            rangeText = ((ushort)sim.Objects[target].Distance).ToString(System.Globalization.CultureInfo.InvariantCulture) + " m";
        else
            rangeText = "TOO FAR";
        DrawCockpitReadout(1, rangeText);
        if (sim.TargetLockCountdown == 0)
        {
            if (sim.TargetLockAcquired)
                sim.TargetLockAcquired = false;
        }
        else if (sim.TargetLockReadoutDirty)
        {
            EraseCockpitReadoutRegion(RightVdu, RightVdu.Left, (short)(RightVdu.Bottom - 6), RightVdu.Right, RightVdu.Bottom,
                PaletteColours.Black);
            sim.TargetLockReadoutDirty = false;
        }
    }

    // ------------------------------------------------------------------ navigation page

    /// <remarks>C: nDisplayedObjectiveRange (40000 initially).</remarks>
    private int _displayedObjectiveRange = 40000;

    /// <summary>The destination's name: "NONE" past the objectives, "UNKNOWN" for unsighted '?' names.</summary>
    /// <remarks>C: objective_name (0x415130, cockpt.c).</remarks>
    private string ObjectiveName(int objective)
    {
        var sim = Sim;
        if (objective >= sim.MissionObjectiveCount)
            return "NONE";
        if (objective < 0)
            objective = sim.MissionObjectives.Length - 1;
        ref readonly var record = ref sim.MissionObjectives[objective];
        if (record.Name.Length > 0 && record.Name[0] == '?' && !sim.Sighted((short)objective))
            return "UNKNOWN";
        return record.DisplayName;
    }

    /// <summary>Range readout: "CALCULATING" until a range is known, else "&lt;units&gt; km".</summary>
    /// <remarks>C: DrawCalculatingLabel (0x4150D0, cockpt.c).</remarks>
    private void DrawCalculatingLabel()
    {
        var sim = Sim;
        if (sim.CurrentObjectiveRange <= 0)
            DrawCockpitReadout(0, "CALCULATING");
        else
            DrawCockpitReadout(0, sim.CurrentObjectiveRange.ToString(System.Globalization.CultureInfo.InvariantCulture) + " km");
        _displayedObjectiveRange = sim.CurrentObjectiveRange;
    }

    /// <summary>"COMP NAVIGATION", destination, range, "(N)ew Objective".</summary>
    /// <remarks>C: show_navigation_disp (0x415180, cockpt.c).</remarks>
    private void ShowNavigationDisplay()
    {
        Gfx.DrawTextAt(RightVduTextContext, RightVdu.Left, RightVdu.Top, "COMP NAVIGATION", TextContext.AlignCentre);
        Gfx.DrawFormattedText("\n\nDESTINATION\n  %s", ObjectiveName(Sim.CurrentObjective));
        Gfx.DrawFormattedText("\n\nRANGE\n  ");
        InitializeCockpitReadout(0, RightVduTextContext);
        Gfx.DrawFormattedText("\n\n(N)ew Objective");
        DrawCalculatingLabel();
    }

    /// <summary>Every tick on the navigation page: a lost objective moves on to the next one, else the
    /// objective is tracked; a changed range redraws the readout.</summary>
    /// <remarks>C: check_objectives (0x4158A0, cockpt.c); the tracking (and the page refresh through
    /// DestinationChanged after a lost objective) is the simulation's.</remarks>
    private void CheckObjectives()
    {
        Sim.CheckObjectives();
        if (_displayedObjectiveRange != Sim.CurrentObjectiveRange)
            DrawCalculatingLabel();
    }

    /// <summary>Callsign of wingman <paramref name="personality"/> (0..7).</summary>
    /// <remarks>C: apWingmanPilots[personality]->callsign.</remarks>
    private string WingmanCallsign(int personality)
    {
        var pilots = Game.Session.Pilots;
        return (uint)personality < (uint)pilots.Length ? pilots[personality].Callsign : "";
    }
}
