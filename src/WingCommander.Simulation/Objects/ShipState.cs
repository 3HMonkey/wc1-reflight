using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;

namespace WingCommander.Simulation.Objects;

/// <summary>
/// Ship-only state of slots 0..9 (player, ships, capital ships, missiles): every per-ship array
/// of the original (sized 10, 12 or 16 there; only 0..9 are real ships). Like the original, a
/// reused slot inherits stale values until <c>set_objects_data</c>/<c>Set_up_ship_info</c>
/// overwrite them.
/// </summary>
/// <remarks>C: the ship arrays of include/globals.h (aeShipSide, aiPilotLevel, acShipRating,
/// anShipFuel, asShipMaximumSpeed, aeSpecialManeuver, aeShipMissionType, ...).</remarks>
public struct ShipState
{
    /// <summary>aeShipSide.</summary>
    public Side Side;

    /// <summary>aiPilotLevel: 0..4 generic, 5..12 named wingmen, 13 player, 14..17 Kilrathi aces.</summary>
    public int PilotLevel;

    /// <summary>acShipRating: <c>PilotLevel - 5</c>, -1 for generic pilots.</summary>
    public sbyte Rating;

    /// <summary>anShipFuel.</summary>
    public int Fuel;

    /// <summary>asShipMaximumSpeed: current maximum speed in whole units.</summary>
    public short MaximumSpeed;

    /// <summary>asShipAfterburnerTimer: frames of afterburner / super brake left.</summary>
    public short AfterburnerTimer;

    /// <summary>aeSpecialManeuver.</summary>
    public SpecialManeuver SpecialManeuver;

    /// <summary>aeShipMissionType.</summary>
    public ShipMissionType MissionType;

    /// <summary>aeShipObjective.</summary>
    public ShipObjective Objective;

    /// <summary>aeShipTactic.</summary>
    public ShipTactic Tactic;

    /// <summary>aeShipManeuver.</summary>
    public ShipManeuver Maneuver;

    /// <summary>asShipCount: maneuver/tactic counter (AI ticks).</summary>
    public short Count;

    /// <summary>acShipSequence: maneuver step index.</summary>
    public sbyte Sequence;

    /// <summary>anYawGoal: degrees still to turn.</summary>
    public short YawGoal;

    /// <summary>anPitchGoal.</summary>
    public short PitchGoal;

    /// <summary>anRollGoal.</summary>
    public short RollGoal;

    /// <summary>acShipPointingMode: 1 = spherical goal solver.</summary>
    public sbyte PointingMode;

    /// <summary>acShipTarget: target slot or -1.</summary>
    public sbyte Target;

    /// <summary>aasShipShield: [0] fore, [1] aft.</summary>
    public ShieldValues Shield;

    /// <summary>aasShipMaximumShield.</summary>
    public ShieldValues MaximumShield;

    /// <summary>aasShipArmor: [0] front, [1] rear, [2] left, [3] right.</summary>
    public ArmorValues Armor;

    /// <summary>asShipWeaponEnergy: gun energy 0..100.</summary>
    public short WeaponEnergy;

    /// <summary>aShipWeapons: the 0x47-byte loadout record.</summary>
    public WeaponLoadout Weapons;

    /// <summary>acShipDamage: core damage events.</summary>
    public sbyte Damage;

    /// <summary>acShipIonDriveDamage: 0..3, max speed × (4 - n) / 4.</summary>
    public sbyte IonDriveDamage;

    /// <summary>acShipDestroyedWeaponCount: 0..5.</summary>
    public sbyte DestroyedWeaponCount;

    /// <summary>acShipCommunicator: -1 = destroyed.</summary>
    public sbyte Communicator;

    /// <summary>acPilotHitPoints: 4 → 0.</summary>
    public sbyte PilotHitPoints;

    /// <summary>acLastAttacker.</summary>
    public sbyte LastAttacker;

    /// <summary>acShipAiCooldown: +4 per hit, -1 per AI tick.</summary>
    public sbyte AiCooldown;

    /// <summary>acShipStress: morale/stress 0..~30.</summary>
    public sbyte Stress;

    /// <summary>anShipAlertFlags: bit 1 collision alert active, bit 2 alert ending.</summary>
    public uint AlertFlags;

    /// <summary>abCollisionAlertTarget: object being avoided (0xff none).</summary>
    public byte CollisionAlertTarget;

    /// <summary>asCollisionCountdown.</summary>
    public short CollisionCountdown;

    /// <summary>asCollisionPartner: per-frame crash prediction cache.</summary>
    public short CollisionPartner;

    /// <summary>asCollisionTime.</summary>
    public short CollisionTime;

    /// <summary>acTurnRegulator: frames until the next AI tick.</summary>
    public sbyte TurnRegulator;

    /// <summary>acTurnInterval: frames between AI ticks.</summary>
    public sbyte TurnInterval;

    /// <summary>abShipTurn: AI tick counter (used as <c>&amp; 7</c> phase selector).</summary>
    public sbyte Turn;

    /// <summary>asShipWingLeader: leader slot or -1.</summary>
    public short WingLeader;

    /// <summary>aShipFormationOffset: offset from the leader (formation table difference).</summary>
    public ShortVector FormationOffset;

    /// <summary>anShipMissionShip: mission record of the escort/strike/defend target (GOTO_WARP: nav index).</summary>
    public short MissionShip;

    /// <summary>nShipMissionIndices: this ship's mission record.</summary>
    public short MissionIndex;

    /// <summary>acShipSpawnNavPoint: nav point that spawned it (-1 = team member).</summary>
    public sbyte SpawnNavPoint;

    /// <summary>abShipNavPointIndex: flight-path index (COME_HOME/GOTO_WARP); also stashes the pre-warp type.</summary>
    public sbyte NavPointIndex;

    /// <summary>aShipDestination.</summary>
    public FixedVector Destination;

    /// <summary>aShipMissionSpot: patrol centre / warp point / home.</summary>
    public FixedVector MissionSpot;

    /// <summary>abShipExhaustHeat: 0 idle, 2 thrusting, 3 afterburner.</summary>
    public sbyte ExhaustHeat;

    /// <summary>acWingmanMessageState: pending comm line (-1 none).</summary>
    public sbyte WingmanMessageState;

    /// <summary>asCapitalShipViewFrame: loaded capital-ship frame (-1 none).</summary>
    public short CapitalShipViewFrame;

    /// <summary>asCannedCommand: current canned command.</summary>
    public short CannedCommand;

    /// <summary>asActionCount: frames left of a canned wait.</summary>
    public short ActionCount;

    /// <summary>apCannedSequence: read position in the canned command stream.</summary>
    public CannedSequenceCursor CannedSequence;

    /// <summary>aiIntelligenceEvent: last AI event.</summary>
    public int IntelligenceEvent;

    public override readonly string ToString() =>
        $"{Side} pilot {PilotLevel} {MissionType}/{Objective}/{Tactic}/{Maneuver} speed max {MaximumSpeed}";
}
