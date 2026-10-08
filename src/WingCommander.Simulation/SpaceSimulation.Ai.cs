using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// AI hierarchy setters and canned sequences used by the mission setup (logic.c, brains.c, mono.c).
public sealed partial class SpaceSimulation
{
    /// <summary>Sets a new mission type and resets the objective; a routing Kilrathi triggers the rout report.</summary>
    /// <remarks>C: reset_mission_type (0x422BE0, logic.c).</remarks>
    public void ResetMissionType(short obj, ShipMissionType missionType)
    {
        if (missionType == ShipMissionType.Rout && Ships[obj].Side == Side.Kilrathi)
            ReportKilrathiRout(1);
        ResetObjective(obj, ShipObjective.None);
        Ships[obj].MissionType = missionType;
    }

    /// <summary>Keeps the objective while engaging, else <see cref="ResetMissionType"/>.</summary>
    /// <remarks>C: change_mission_type (0x422C30, logic.c).</remarks>
    public void ChangeMissionType(short obj, ShipMissionType missionType)
    {
        if (Ships[obj].Objective == ShipObjective.EngageEnemy)
            Ships[obj].MissionType = missionType;
        else
            ResetMissionType(obj, missionType);
    }

    /// <summary>Steadies the ship, resets the tactic (clearing the target) and sets the objective.</summary>
    /// <remarks>C: reset_objective (0x422C70, logic.c).</remarks>
    public void ResetObjective(short ship, ShipObjective objective)
    {
        SteadyObject(ship);
        ResetTactic(ship, ShipTactic.None);
        Ships[ship].Objective = objective;
    }

    /// <summary>As <see cref="ResetObjective"/> but keeps the target.</summary>
    /// <remarks>C: alter_objective (0x422CA0, logic.c).</remarks>
    public void AlterObjective(short ship, ShipObjective objective)
    {
        SteadyObject(ship);
        AlterTactic(ship, ShipTactic.None);
        Ships[ship].Objective = objective;
    }

    /// <remarks>C: reset_tactic (0x422CD0, logic.c).</remarks>
    public void ResetTactic(short ship, ShipTactic tactic)
    {
        ResetManeuver(ship, ShipManeuver.None);
        Ships[ship].Tactic = tactic;
        Ships[ship].Target = -1;
    }

    /// <remarks>C: alter_tactic (0x422D00, logic.c).</remarks>
    public void AlterTactic(short ship, ShipTactic tactic)
    {
        ResetManeuver(ship, ShipManeuver.None);
        Ships[ship].Tactic = tactic;
    }

    /// <summary>Sets the maneuver and clears its counter and step.</summary>
    /// <remarks>C: reset_maneuver (0x422D30, logic.c).</remarks>
    public void ResetManeuver(short ship, ShipManeuver maneuver)
    {
        ref var s = ref Ships[ship];
        s.Maneuver = maneuver;
        s.Count = 0;
        s.Sequence = 0;
    }

    /// <summary>Switches to <paramref name="maneuver"/> (and steadies) only when it is not already running.</summary>
    /// <remarks>C: try2reset_maneuver (0x422D60, logic.c).</remarks>
    public void Try2ResetManeuver(short obj, ShipManeuver maneuver)
    {
        if (Ships[obj].Maneuver != maneuver)
        {
            ResetManeuver(obj, maneuver);
            SteadyObject(obj);
        }
    }

    /// <remarks>C: maneuver_complete (0x4060B0, brains.c).</remarks>
    public void ManeuverComplete(short ship)
    {
        SetSpecial(ship, SpecialManeuver.None);
        ResetManeuver(ship, ShipManeuver.None);
    }

    /// <summary>
    /// Kilrathi presence test over ships 0..9 (dying ships ignored): mode 0 any Kilrathi, 1 any
    /// non-routing Kilrathi within 16000 of the player (then, if none, the next wave is checked),
    /// 2 any Kilrathi engaging or attacking.
    /// </summary>
    /// <remarks>C: report_kilrathi_rout (0x422640, logic.c).</remarks>
    public bool ReportKilrathiRout(int mode)
    {
        for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Objects[obj].Class < ObjectClass.Ship || Ships[obj].SpecialManeuver == SpecialManeuver.Unknown9)
                continue;
            ref var ship = ref Ships[obj];
            switch (mode)
            {
                case 0:
                    if (ship.Side == Side.Kilrathi)
                        return true;
                    break;
                case 1:
                    if (ship.Side == Side.Kilrathi &&
                        ship.MissionType != ShipMissionType.Rout &&
                        DistanceFromObject(0, obj) < 16000)
                        return true;
                    break;
                case 2:
                    if (ship.Side == Side.Kilrathi &&
                        (ship.Objective == ShipObjective.EngageEnemy || ship.Objective == ShipObjective.DestroyShip))
                        return true;
                    break;
            }
        }
        if (mode == 1 && CurrentWave != -1)
            CheckNextWave();
        return false;
    }

    /// <summary>Starts the next follow-up wave when no Kilrathi fighter is left.</summary>
    /// <remarks>C: check_next_wave (0x41F7C0, ship.c).</remarks>
    public void CheckNextWave()
    {
        if (CurrentWave == -1)
            return;
        for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Objects[obj].Class == ObjectClass.Ship && Ships[obj].Side == Side.Kilrathi)
                return;
        }
        SetUpNextWave();
    }

    /// <summary>
    /// Reads the next canned command: <c>0 n</c> wait, <c>1 yaw pitch roll speed</c> goals and speed,
    /// <c>2</c> explode, <c>3</c> fire guns, <c>4</c> afterburner.
    /// </summary>
    /// <remarks>C: advance_canned_sequence (0x403A80, mono.c).</remarks>
    public void AdvanceCannedSequence(short obj)
    {
        var command = Ships[obj].CannedSequence;
        if (command.IsNull)
            return;
        short canned = command.Next();
        Ships[obj].CannedCommand = canned;
        switch (canned)
        {
            case 0:
                Ships[obj].ActionCount = command.Next();
                break;
            case 1:
                Ships[obj].YawGoal = command.Next();
                Ships[obj].PitchGoal = command.Next();
                Ships[obj].RollGoal = command.Next();
                Objects[obj].Speed = command.Next() << 8;
                break;
            case 2:
                Explode(-1, obj);
                break;
            case 3:
                FireFixedProjectileWeapon(obj);
                break;
            case 4:
                Ships[obj].SpecialManeuver = SpecialManeuver.Afterburner;
                break;
        }
        Ships[obj].CannedSequence = command;
    }
}
