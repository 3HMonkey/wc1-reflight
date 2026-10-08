using WingCommander.Core.Resources;
using WingCommander.Game.Flight.Audio;
using WingCommander.Game.Flight.Bridges;
using WingCommander.Game.Input;
using WingCommander.Game.Video;
using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Text;
using WingCommander.Simulation;
using WingCommander.Simulation.Missions;

namespace WingCommander.Game.Flight;

/// <summary>
/// The flight layer's state and code: the C globals of the flight loop, cockpit, HUD, VDUs,
/// communication, nav map and flight sequences, and the space simulation they drive. One
/// instance lives as long as the game (like the original globals: cockpit resources, VDU
/// modes, message speed and the 3-space state survive between missions). The class is split
/// into partial files by area; every member names its C function or global.
/// </summary>
/// <remarks>
/// Runtime model (ADR-009): the flight loop and every sequence are coroutines; one simulation
/// tick and everything drawn in it is synchronous, awaits happen only where the original
/// presented (with its frame throttle) or waited in a modal loop. The simulation calls back
/// synchronously at the original statement positions through <see cref="ISimulationEvents"/>.
/// </remarks>
internal sealed partial class FlightSession
{
    public FlightSession(Wc1Game game, FlightOptions options)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(options);
        Game = game;
        Options = options;
        var simulationResources = new GameDirectoryResources(game.Directory);
        Shapes = new FlightShapes(game.Resources, simulationResources);
        Sim = new SpaceSimulation(game.Random, simulationResources, this, new CampaignBridge(game.Session))
        {
            Cockpit = this,
            ShapeBounds = this,
        };
        var simulation = Sim;
        Audio = new FlightAudio(game, () => simulation);
        ApplyStartupSwitches();
        InitializeTextContexts();
    }

    public Wc1Game Game { get; }

    public FlightOptions Options { get; }

    /// <summary>The space world (the original's 3-space globals).</summary>
    public SpaceSimulation Sim { get; }

    public FlightShapes Shapes { get; }

    public FlightAudio Audio { get; }

    public GraphicsContext Gfx => Game.Graphics;

    public EventManager Events => Game.Events;

    public Display Display => Game.Display;

    /// <summary>The 320x200 screen viewport (stScreen).</summary>
    public Viewport Screen => Game.Graphics.Screen!;

    /// <summary>Shared string-builder buffer of the text contexts (szDefaultTextBuffer).</summary>
    private byte[] DefaultTextBuffer
    {
        get
        {
            var buffer = Game.DefaultText.TextBuffer;
            if (buffer is null)
            {
                buffer = new byte[512];
                Game.DefaultText.TextBuffer = buffer;
            }
            return buffer;
        }
    }

    /// <summary>The HUD message line context: font 1, red, transparent background, centred, drawn into the space buffer.</summary>
    /// <remarks>C: stHudMessageTextContext (InitializeGameTextContexts 0x421D80, logic.c).</remarks>
    public TextContext HudMessageTextContext { get; } = new();

    /// <summary>Copies the developer switches the simulation reads.</summary>
    /// <remarks>C: bPlayerVulnerable, bPlayerCollisionResponse, nStartNavPointOverride (GameMain).</remarks>
    private void ApplyStartupSwitches()
    {
        var options = Game.Options;
        Sim.PlayerVulnerable = options.PlayerVulnerable;
        Sim.PlayerCollisionResponse = options.PlayerCollisionResponse;
        // DEVIATION (request in docs/progress/flight.md): StartupOptions defaults the override to 0, the
        // C global to -1 (no override); 0 is therefore treated as "no override" until the default is fixed.
        short startNav = options.StartNavPointOverride;
        Sim.StartNavPointOverride = startNav; // -1 = none (C default); "as0" selects nav point 0
    }

    /// <remarks>C: the stHudMessageTextContext half of InitializeGameTextContexts (0x421D80, logic.c).</remarks>
    private void InitializeTextContexts()
    {
        HudMessageTextContext.Viewport = SpaceBuffer;
        HudMessageTextContext.TextBuffer = DefaultTextBuffer;
        HudMessageTextContext.Alignment = TextContext.AlignCentre;
        var savedContext = Gfx.CurrentTextContext;
        Gfx.InitializeTextContextFromFont(HudMessageTextContext, 1, PaletteColours.Red, PaletteColours.Transparent);
        Gfx.SetTextContext(savedContext);
    }

    private ConstellationObjectDefinition[]? _constellations;
    private short _constellationDataSet = -1;

    /// <summary>
    /// Prepares the simulation for a mission of the current campaign: data set, the series'
    /// background planets (CAMP.xxx section 0) and the training simulator flag.
    /// </summary>
    /// <remarks>C: nCampaignDataSet, pConstellationDefinitions, nTrainSimActive.</remarks>
    public void PrepareCampaignData(bool trainingSimulator)
    {
        short dataSet = Game.Session.CampaignDataSet;
        Sim.CampaignDataSet = dataSet;
        if (_constellations is null || _constellationDataSet != dataSet)
        {
            int file = dataSet switch
            {
                1 => LogicalFile.Camp001,
                2 => LogicalFile.Camp002,
                _ => LogicalFile.Camp000,
            };
            var section = Shapes.GetSection(file, 0);
            _constellations = section.IsEmpty ? [] : ConstellationObjectDefinition.ParseTable(section.Span);
            _constellationDataSet = dataSet;
        }
        Sim.ConstellationDefinitions = _constellations;
        Sim.TrainSimActive = trainingSimulator;
    }
}
