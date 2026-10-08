using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// In-flight objective tracking of cockpt.c: sighting, visiting/reaching (with the escort wait
// logic), lost objectives and the nav VDU's check.
public sealed partial class SpaceSimulation
{
    /// <summary>A ship on its way home (COME_HOME) has not passed the player's current flight-path entry.</summary>
    /// <remarks>C: someone_coming (0x4154C0, cockpt.c).</remarks>
    public bool SomeoneComing()
    {
        for (short ship = 0; ship < ObjectSlots.ShipSlotCount; ship++)
        {
            if (Objects[ship].Class >= ObjectClass.Ship &&
                Ships[ship].MissionType == ShipMissionType.ComeHome &&
                Ships[ship].NavPointIndex <= CurrentNavPointIndex)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The player escorts a ship (own mission ESCORT) or someone is still coming home.</summary>
    /// <remarks>C: escorting_a_ship (0x415510, cockpt.c).</remarks>
    public bool EscortingAShip() =>
        Ships[ObjectSlots.Player].MissionType == ShipMissionType.Escort || SomeoneComing();

    /// <summary>
    /// Objective <paramref name="objective"/> was reached (by the player: <paramref name="reached"/>
    /// false; by a ship flying the flight path: true). For the current objective the player escorting
    /// a ship has to wait for it ("Wait for %s", unless it is the home base whose record is already
    /// home); otherwise "Objective Reached" / "Already Visited" and the destination advances. Reached
    /// objectives other than the home base are flagged visited; reaching the escorted ship's own
    /// objective makes that ship say line 6 (unless it is the Tiger's Claw).
    /// </summary>
    /// <remarks>C: flag_reached (0x415530, cockpt.c). The "carrier" of the original is the player's mission
    /// ship (<c>anShipMissionShip[0]</c>). Objective -1 (a flight-path terminator handed in by a
    /// travelling ship) is the record before the table, as in the original (<see cref="ObjectiveRecord"/>).</remarks>
    public void FlagReached(short objective, bool reached)
    {
        var record = ObjectiveRecord(objective);
        short carrierMissionShip = Ships[ObjectSlots.Player].MissionShip;
        short objectiveType = unchecked((short)record.Type);
        short carrierObject = FindShipIndex(carrierMissionShip);
        bool markVisited = objective != CurrentObjective;
        bool advanceDestination = false;
        if (objective == CurrentObjective)
        {
            if (!reached && EscortingAShip() && carrierObject != -1 &&
                record.Index != Ships[ObjectSlots.Player].MissionShip)
            {
                if (objectiveType != 1 || MissionShips[carrierMissionShip].State != 1)
                    Events.ShowCockpitMessage(SimulationCockpitMessage.WaitFor, Objects[carrierObject].Type);
            }
            else
            {
                advanceDestination = true;
                Events.ShowCockpitMessage(
                    Visited(objective) ? SimulationCockpitMessage.AlreadyVisited : SimulationCockpitMessage.ObjectiveReached,
                    ObjectType.None);
                markVisited = true;
            }
        }
        if (objectiveType != 1 && markVisited)
        {
            if (!Visited(objective) && carrierObject != -1 &&
                record.Index == Ships[ObjectSlots.Player].MissionShip &&
                Objects[carrierObject].Type != ObjectType.TigersClaw)
            {
                SendMessage(carrierObject, 6);
            }
            FlagObjective(objective, MissionObjective.FlagVisited);
        }
        if (advanceDestination)
            SetNextDestination();
    }

    /// <summary>Flags an objective sighted when it is within 16000 and on screen.</summary>
    /// <remarks>C: check_sighting (0x4156D0, cockpt.c). <paramref name="obj"/> is what
    /// <see cref="LocateMobileObjective"/> returned (-1, or locate_ship's 1/0 used as a slot number,
    /// as in the original).</remarks>
    public void CheckSighting(short objective, short range, short obj)
    {
        if (!Sighted(objective) && range < 16000 &&
            (obj == -1 || Objects[obj].ScreenX != ObjectSlots.NotVisible))
        {
            FlagObjective(objective, MissionObjective.FlagSighted);
        }
    }

    /// <summary>The player reached the objective: within 6000 for escort and destroy targets (types 3
    /// and 4), else 1500.</summary>
    /// <remarks>C: check_visit (0x415720, cockpt.c).</remarks>
    public void CheckVisit(short objective, short range)
    {
        short type = unchecked((short)MissionObjectives[objective].Type);
        int reachedRange = type == 3 || type == 4 ? 6000 : 1500;
        if (range < reachedRange)
            FlagReached(objective, false);
    }

    /// <summary>
    /// Round-robin objective check (one objective per frame, from <see cref="UpdateCockpitSimulation"/>,
    /// and the current one from <see cref="CheckObjectives"/>): relocates mobile objectives, flags them
    /// sighted and visited/reached. Objectives already sighted and visited are skipped unless current.
    /// </summary>
    /// <remarks>C: update_objective_location (0x415770, cockpt.c). Like the original, locate_ship's 1/0
    /// result is passed to <see cref="CheckSighting"/> as an object slot, and mobile objectives are always
    /// visit-checked. A nav point index outside the table (an objective of an unknown type) skips the
    /// visit check.</remarks>
    public void UpdateObjectiveLocation(short objective)
    {
        short obj = LocateMobileObjective(objective);
        if (Sighted(objective) && Visited(objective) && CurrentObjective != objective)
            return;
        var delta = VectorMath.Delta(Objects[ObjectSlots.Player].Position, MissionObjectives[objective].Position);
        short range = FixedMath.ToShortSaturating(delta.Magnitude());
        CheckSighting(objective, range, obj);
        if (MobileObjective(objective))
        {
            if (obj != -1)
                CheckVisit(objective, range);
        }
        else
        {
            int navPoint = MissionObjectives[objective].Index;
            if ((uint)navPoint < NavPointTableSize && MissionNavPoints[navPoint].Type >= 1)
                CheckVisit(objective, range);
        }
    }

    /// <summary>The ship of an escort/defend objective (types 2, 3) left or died (state &gt;= 1, compared
    /// unsigned), or the target of a destroy objective (type 4) was destroyed.</summary>
    /// <remarks>C: objective_lost (0x415850, cockpt.c). A record index outside the ship table counts as
    /// state 0.</remarks>
    public bool ObjectiveLost(short objective)
    {
        ref readonly var o = ref MissionObjectives[objective];
        int index = o.Index;
        ushort state = (uint)index < MissionShips.Length ? unchecked((ushort)MissionShips[index].State) : (ushort)0;
        return o.Type switch
        {
            2 or 3 => state >= 1,
            4 => state == 3,
            _ => false,
        };
    }

    /// <summary>
    /// The navigation VDU's per-frame check: a lost current objective cycles to the next one (and the
    /// VDU is refreshed through <see cref="ISimulationEvents.DestinationChanged"/>), otherwise the current
    /// objective is updated. Returns true when it cycled.
    /// </summary>
    /// <remarks>C: check_objectives (0x4158A0, cockpt.c), called by update_VDUs (UI) while the right VDU
    /// shows navigation. Its <c>DrawCalculatingLabel</c> (displayed range differs from
    /// <see cref="CurrentObjectiveRange"/>) is UI code that runs after this call. Without a current
    /// objective (before the first mission) nothing happens.</remarks>
    public bool CheckObjectives()
    {
        if (CurrentObjective < 0)
            return false;
        if (ObjectiveLost(CurrentObjective))
        {
            CycleNextObjective();
            Events.DestinationChanged();
            return true;
        }
        UpdateObjectiveLocation(CurrentObjective);
        return false;
    }
}
