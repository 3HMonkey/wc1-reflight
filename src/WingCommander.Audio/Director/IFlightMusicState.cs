namespace WingCommander.Audio.Director;

/// <summary>
/// Game state that the in-flight music selection reads (implemented by Game on top of the
/// simulation and campaign). Every member mirrors one C global or query function so the
/// audio code stays a literal transcription of <c>music.c</c>; values use the C enum numbers
/// (see the constants on <see cref="MusicDirector"/>). Queries are called in the original order.
/// </summary>
public interface IFlightMusicState
{
    /// <summary>C: nTrainSimActive (training simulator / arcade mode).</summary>
    bool TrainingSimulatorActive { get; }

    /// <summary>C: aeShipMissionType[0] (enum MissionType, e.g. 0 patrol, 1 escort, 2 strike, 3 defend, 9 rendezvous).</summary>
    int PlayerMissionType { get; }

    /// <summary>C: aMissionObjectives[cCurrentObjective].type == OBJECTIVE_HOME_BASE.</summary>
    bool CurrentObjectiveIsHomeBase { get; }

    /// <summary>C: nYourWingman (object index of the wingman, -1 = none).</summary>
    int YourWingman { get; }

    /// <summary>C: anShipMissionShip[0] (mission ship index of the escort/defend target).</summary>
    int PlayerMissionShip { get; }

    /// <summary>C: triumph(0) != 0.</summary>
    bool Triumph();

    /// <summary>C: missile_on_tail(0) != 0.</summary>
    bool MissileOnTail();

    /// <summary>C: any_enemy_tail(0) != 0.</summary>
    bool AnyEnemyOnTail();

    /// <summary>C: is_ship_tailing_player_target(0) != 0.</summary>
    bool IsShipTailingPlayerTarget();

    /// <summary>C: calculate_damage_level() (0 none, 1 medium, 2+ severe).</summary>
    int CalculateDamageLevel();

    /// <summary>C: report_kilrathi_rout(mode) != 0 (mode 1: enemies within 16000; mode 2: enemies present).</summary>
    bool ReportKilrathiRout(int mode);

    /// <summary>C: aeShipSide[obj] (0 imperial, 1 kilrathi, 2 neutral).</summary>
    int GetShipSide(int obj);

    /// <summary>C: acShipRating[obj] (-1 = unrated pilot).</summary>
    int GetShipRating(int obj);

    /// <summary>C: nShipMissionIndices[obj].</summary>
    int GetShipMissionIndex(int obj);
}
