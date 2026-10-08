using WingCommander.Core.Resources;
using WingCommander.Game.Flight.Cockpit;
using WingCommander.Graphics.Cockpit;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// Cockpit resources and the cockpit picture: logic.c InitializeCockpitResources, free_cockpit,
// init_vdus, initialize_cockpit, ResetCockpitPaletteEntries.
internal sealed partial class FlightSession
{
    /// <remarks>C: bCockpitResourcesActive.</remarks>
    private bool _cockpitResourcesActive;

    /// <summary>Set while the attract mode and the landing approach run: the letterbox view then draws no
    /// backdrop and does not present.</summary>
    /// <remarks>C: bIntroSceneResourcesActive.</remarks>
    public bool IntroSceneResourcesActive { get; set; } = true;

    /// <summary>The cockpit set index (0..4) used for every layout table and PCSHIP file.</summary>
    /// <remarks>C: cCockpitView.</remarks>
    public int CockpitIndex => Math.Clamp((int)Sim.CockpitView, 0, CockpitTables.CockpitCount - 1);

    /// <summary>Logical file of the cockpit (PCSHIP.V00..V04).</summary>
    /// <remarks>C: cCockpitLogicalFile.</remarks>
    public int CockpitFile => FlightShapes.CockpitFile(CockpitIndex);

    /// <summary>Gunsight, lock marker, scanner marker, nav pointer (COCKPIT.VGA section 0).</summary>
    /// <remarks>C: pTargetLockShape.</remarks>
    public ShapeTable? TargetLockShape => Shapes.Get(LogicalFile.CockpitVga, 0);

    /// <summary>Cockpit lights and bars (cockpit section 7).</summary>
    /// <remarks>C: pCockpitDamageShape (misnamed).</remarks>
    public ShapeTable? CockpitLightShape => Shapes.Get(CockpitFile, 7);

    /// <summary>Weapon and damage VDU sprites (cockpit section 9).</summary>
    /// <remarks>C: pCockpitWeaponShape.</remarks>
    public ShapeTable? CockpitWeaponShape => Shapes.Get(CockpitFile, 9);

    /// <summary>Cockpit damage decals (cockpit section 4).</summary>
    /// <remarks>C: pCockpitPilotShape (misnamed).</remarks>
    public ShapeTable? CockpitDecalShape => Shapes.Get(CockpitFile, 4);

    /// <summary>The pilot's hand on the stick (PILOTANM.VGA section 3).</summary>
    /// <remarks>C: pPilotHandShape.</remarks>
    public ShapeTable? PilotHandShape => Shapes.Get(LogicalFile.PilotAnimVga, 3);

    /// <summary>The target VDU's shield arcs (COCKPIT.VGA section 4).</summary>
    /// <remarks>C: pCockpitIndicatorShape.</remarks>
    public ShapeTable? CockpitIndicatorShape => Shapes.Get(LogicalFile.CockpitVga, 4);

    /// <summary>The letterbox backdrop of the external views (COCKPIT.VGA section 6).</summary>
    /// <remarks>C: pCinematicViewBackdrop.</remarks>
    public ShapeTable? CinematicViewBackdrop => Shapes.Get(LogicalFile.CockpitVga, 6);

    /// <summary>The escape-pod interior (COCKPIT.VGA section 7).</summary>
    /// <remarks>C: pRearViewBackdrop (misnamed).</remarks>
    public ShapeTable? PodInteriorBackdrop => Shapes.Get(LogicalFile.CockpitVga, 7);

    /// <summary>A cockpit art frame (cockpit sections 0..3, front/right/left/rear).</summary>
    /// <remarks>C: apCockpitShapes[view].</remarks>
    public ShapeTable? CockpitArt(int view) => Shapes.Get(CockpitFile, view);

    /// <summary>The cockpit set a mission uses: 4 in the training simulator, else the player's ship
    /// type. DEVIATION (ADR-012): ship types without a cockpit layout (the Secret Missions 2 Dralthi,
    /// PCSHIP.V05) use the Hornet cockpit.</summary>
    private static sbyte CockpitSetFor(int mode) => mode is >= 0 and < CockpitTables.CockpitCount ? (sbyte)mode : (sbyte)0;

    /// <summary>
    /// Loads the cockpit of a mission (nothing when it is already loaded): clears damage, gun
    /// readouts and lights, reads the view geometries, sets up the VDUs, readouts, pilot hand and
    /// the comm speech of the mission's pilots.
    /// </summary>
    /// <remarks>C: InitializeCockpitResources (0x4245B0, logic.c).</remarks>
    public void InitializeCockpitResources(int mode)
    {
        sbyte cockpit = CockpitSetFor(mode);
        if (_cockpitResourcesActive)
        {
            if (cockpit == Sim.CockpitView)
                return;
            FreeCockpit();
        }
        _cockpitResourcesActive = true;
        Sim.CockpitView = cockpit;
        ClearCockpitDamage();
        ClearHudGunReadouts();
        ResetCockpitLights();
        GetScreenUpdateFlag();

        var geometrySection = Shapes.GetSection(CockpitFile, ViewGeometrySet.PcShipSection);
        ScreenViewportPacket = geometrySection.IsEmpty
            ? null
            : ViewGeometrySet.Parse($"PCSHIP.V0{CockpitIndex}", geometrySection.Span);
        _cockpitBar = Screen.Clone();
        InitializeVdus();

        CockpitReadoutTextContext.Viewport = Screen;
        Gfx.InitializeTextContextFromFont(CockpitReadoutTextContext, 2, PaletteColours.PrimaryText, PaletteColours.Black);
        Gfx.SetTextContext(CockpitReadoutTextContext);
        int cockpitIndex = CockpitIndex;
        InitializeReadoutAt(4, CockpitTables.ReadoutOrigins[0][cockpitIndex]);
        InitializeReadoutAt(5, CockpitTables.ReadoutOrigins[1][cockpitIndex]);
        InitializeReadoutAt(2, CockpitTables.ReadoutOrigins[2][cockpitIndex]);
        InitializeReadoutAt(3, CockpitTables.ReadoutOrigins[3][cockpitIndex]);

        var left = CockpitTables.LeftVduBounds[cockpitIndex];
        LeftVdu.SetViewportRect(left.Left, left.Top, left.Right, left.Bottom);
        var right = CockpitTables.RightVduBounds[cockpitIndex];
        RightVdu.SetViewportRect(right.Left, right.Top, right.Right, right.Bottom);

        if (PilotHandShape is not null)
        {
            var hand = CockpitTables.PilotHandBounds[cockpitIndex];
            _pilotHand = Screen.Clone();
            _pilotHand.SetViewportRect(hand.Left, hand.Top, hand.Right, hand.Bottom);
            _pilotHandComposite = Viewport.Allocate(0, 0, hand.Right - hand.Left, hand.Bottom - hand.Top, PaletteColours.Black);
            _pilotHandBackdrop = Viewport.Allocate(0, 0, hand.Right - hand.Left, hand.Bottom - hand.Top, PaletteColours.Black);
        }

        ResetScannerContacts();
        InitializePersonalities();
        _cockpitExplosionFrame = 8;
        Sim.RadioSilence = false;
        _commVideoEnabled = true;
    }

    private void InitializeReadoutAt(int slot, ScreenPoint origin)
    {
        Gfx.SetTextCursor(origin.X, origin.Y);
        InitializeCockpitReadout(slot, CockpitReadoutTextContext);
    }

    /// <summary>Releases the cockpit (the next mission loads it again).</summary>
    /// <remarks>C: free_cockpit (0x4249A0, logic.c).</remarks>
    public void FreeCockpit()
    {
        if (!_cockpitResourcesActive)
            return;
        _cockpitResourcesActive = false;
        GetScreenUpdateFlag();
        ScreenViewportPacket = null;
        _pilotHandComposite?.FreeViewport();
        _pilotHandBackdrop?.FreeViewport();
        FreeCommDisplayResources();
    }

    /// <summary>Space background (0, 0, 32) and black cockpit hit lights.</summary>
    /// <remarks>C: ResetCockpitPaletteEntries (0x423E10, logic.c).</remarks>
    public void ResetCockpitPaletteEntries() => Gfx.FlightPalette.ResetCockpitPaletteEntries(Gfx.Palette);

    /// <summary>
    /// Sets up the screen for a camera view: draws the cockpit art (or the letterbox / pod
    /// backdrop), resets the cockpit instruments for the front view, selects the view geometry
    /// and allocates the space buffer. The same picture again only clears the buffer. The
    /// letterbox backdrop is presented immediately (outside cockpitless mode).
    /// </summary>
    /// <remarks>C: initialize_cockpit (0x423E90, logic.c); ISimulationEvents.InitializeCockpitView.</remarks>
    public void InitializeCockpit(int mode)
    {
        if (MessageShowing())
            EndCommMenu();
        var sim = Sim;
        if (sim.CockpitlessView == 0 && mode == ScreenViewportMode)
        {
            if (SpaceBuffer.IsAllocated)
                ClearViewBuffer();
            else
                InitializeViewBuffer();
            return;
        }

        GetScreenUpdateFlag();
        var screen = Screen;
        short savedLeft = screen.Left, savedTop = screen.Top, savedRight = screen.Right, savedBottom = screen.Bottom;
        screen.SetViewportRect(0, 0, 319, 199);
        var wholeScreen = screen.Clone();
        Gfx.ClearViewport(wholeScreen, PaletteColours.Black);

        bool cockpitless = sim.CockpitlessView != 0;
        ScreenViewportMode = (sbyte)mode;
        switch (mode)
        {
            case 0:
                if (!cockpitless)
                    Gfx.DrawSpriteDefault(screen, 0, 0, CockpitArt(0), 0);
                ResetCockpitPaletteEntries();
                if (!cockpitless)
                    ExplosionDraw();
                ResetCockpitLights();
                InvalidateVduMode(0);
                InvalidateVduMode(1);
                UpdateVdus();
                ClearHeadUpDisplay();
                if (!cockpitless)
                    ResetPilotHandAnimation();
                SetUpScreenViewport(0);
                break;
            case 1:
            case 2:
            case 3:
                if (!cockpitless)
                    Gfx.DrawSpriteDefault(screen, 0, 0, CockpitArt(mode), 0);
                SetUpScreenViewport((sbyte)mode);
                break;
            case 4:
                if (IntroSceneResourcesActive && CinematicViewBackdrop is { } backdrop)
                {
                    Gfx.DrawSpriteDefault(screen, 0, 0, backdrop, 0);
                    if (sim.CockpitlessView < 1)
                    {
                        Display.Slam();
                        Display.SlamRealNow();
                    }
                }
                SetUpScreenViewport(4);
                break;
            case 5:
                SetUpScreenViewport(4);
                break;
            case 6:
                SetUpScreenViewport(5);
                break;
            case 7:
                if (!cockpitless)
                    Gfx.DrawSpriteDefault(screen, 0, 0, PodInteriorBackdrop, 0);
                SetUpScreenViewport(0);
                break;
        }

        _viewBufferEnabled = true;
        SetSpaceBufferSize(sim.ScreenWidth, sim.ScreenHeight);
        InitializeViewBuffer();
        screen.SetViewportRect(savedLeft, savedTop, savedRight, savedBottom);
    }

    /// <summary>The cockpit readout context: font 2, primary text colour on black, on the screen.</summary>
    /// <remarks>C: stCockpitReadoutTextContext.</remarks>
    public TextContext CockpitReadoutTextContext { get; } = new();
}
