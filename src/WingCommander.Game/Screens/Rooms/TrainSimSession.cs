namespace WingCommander.Game.Screens.Rooms;

/// <summary>
/// The arcade (TrainSim) globals the menus and the flight layer share. The flight adds to
/// <see cref="ArcadeScore"/> while it runs; the menus read it afterwards for the ranking.
/// </summary>
/// <remarks>C: nArcadeScore, nArcadeWave, nTrainSimMission, nTrainSimActive, nArcadeBonusCountdown
/// (system.c / hudmsg.c globals).</remarks>
public sealed class TrainSimSession
{
    /// <summary>Score in tens (the screens print it with a trailing 0).</summary>
    /// <remarks>C: nArcadeScore.</remarks>
    public int ArcadeScore { get; set; }

    /// <remarks>C: nArcadeWave.</remarks>
    public short ArcadeWave { get; set; }

    /// <summary>Enemy (simulator mission 0..3) being flown; 4 ends the session.</summary>
    /// <remarks>C: nTrainSimMission.</remarks>
    public short Mission { get; set; }

    /// <remarks>C: nTrainSimActive.</remarks>
    public bool Active { get; set; }

    /// <remarks>C: nArcadeBonusCountdown.</remarks>
    public int ArcadeBonusCountdown { get; set; }

    /// <summary>Cockpit view while the simulator runs (4).</summary>
    /// <remarks>C: cCockpitView.</remarks>
    public sbyte CockpitView { get; set; }

    /// <summary>Cockpit art logical file (21 = PCSHIP.V04 for the simulator).</summary>
    /// <remarks>C: cCockpitLogicalFile.</remarks>
    public sbyte CockpitLogicalFile { get; set; } = 17;
}
