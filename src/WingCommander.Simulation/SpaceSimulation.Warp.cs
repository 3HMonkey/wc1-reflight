using WingCommander.Simulation.Data;
using WingCommander.Simulation.Missions;

namespace WingCommander.Simulation;

// Hyperspace jumps of hudmsg.c: ships arriving at a nav point (WARP_ARRIVE) and leaving it
// (GOTO_WARP), and the objective lookup they use.
public sealed partial class SpaceSimulation
{
    /// <summary>First objective of <paramref name="type"/> (and, unless -1, with record/nav
    /// <paramref name="index"/>), or -1.</summary>
    /// <remarks>C: find_objective (0x42A8F0, hudmsg.c). RunSpaceFlight uses <c>find_objective(1, -1)</c>
    /// to flag the home base achieved after landing.</remarks>
    public short FindObjective(int type, short index)
    {
        for (short objective = 0; objective < MissionObjectiveCount; objective++)
        {
            if (MissionObjectives[objective].Type == type &&
                (index == -1 || MissionObjectives[objective].Index == index))
            {
                return objective;
            }
        }
        return -1;
    }

    /// <summary>
    /// A ship arrives from hyperspace at the player's nav point: the nav objective is flagged visited
    /// (and becomes the next destination when current), the ship appears with a jump flash at full
    /// speed and flies home (Confed) or patrols (Kilrathi).
    /// </summary>
    /// <remarks>C: arrive_from_warp (0x42A950, hudmsg.c). The original tests the type of
    /// <c>aMissionObjectives[abFlightPath[objective]]</c> (the flight path indexed by an objective
    /// number; a -1 entry reads the record before the table, <see cref="ObjectiveRecord"/>).</remarks>
    public void ArriveFromWarp(short obj)
    {
        short objective = FindObjective(0, CurrentNavPoint);
        if (objective != -1)
        {
            if (ObjectiveRecord(FlightPath[objective]).Type != 1)
                FlagObjective(objective, MissionObjective.FlagVisited);
            if (CurrentObjective == objective)
                SetNextDestination();
        }
        ApproveXyz(obj, 2000, 5000);
        Unwarp(obj);
        Objects[obj].Speed = Ships[obj].MaximumSpeed << 8;
        FixVelocity(obj);
        ResetMissionType(obj, Ships[obj].Side == Side.Imperial ? ShipMissionType.ComeHome : ShipMissionType.Patrol);
    }

    /// <summary>
    /// The jump-in flash: a hyperspace flash object at the ship (maneuver NONE, counter 6). Without a
    /// free effect slot the ship's own slot becomes the flash; its type is parked in the flight-path
    /// index, but nothing restores it (the flash is an explosion-class object that animates and is
    /// removed), so the ship is lost.
    /// </summary>
    /// <remarks>C: unwarp (0x42AA10, hudmsg.c); the space buffer clear is
    /// <see cref="ISimulationEvents.SpaceBufferFlash"/>.</remarks>
    public void Unwarp(short obj)
    {
        Events.SpaceBufferFlash();
        short effect = FindVacant3dObject();
        if (effect != -1)
        {
            SetObjectsData(effect, ObjectType.HyperspaceJumpFlash, obj);
            Objects[effect].Position = Objects[obj].Position;
            Objects[effect].Velocity = Objects[obj].Velocity;
            Ships[obj].Maneuver = ShipManeuver.None;
            Objects[obj].Counter = 6;
            return;
        }
        Ships[obj].NavPointIndex = unchecked((sbyte)Objects[obj].Type);
        SetObjectsData(obj, ObjectType.HyperspaceJumpFlash, obj);
    }

    /// <summary>
    /// The jump-out: a hyperspace flash at the ship and the WARPING_OUT maneuver with counter 6 (the
    /// ship shrinks every frame and is then removed with mission state 2, "left"). Without a free effect
    /// slot the ship itself becomes the flash (and its record keeps its state).
    /// </summary>
    /// <remarks>C: warp (0x42AAF0, hudmsg.c); the space buffer clear is
    /// <see cref="ISimulationEvents.SpaceBufferFlash"/>.</remarks>
    public void Warp(short obj)
    {
        Events.SpaceBufferFlash();
        short effect = FindVacant3dObject();
        if (effect != -1)
        {
            SetObjectsData(effect, ObjectType.HyperspaceJumpFlash, Objects[obj].Owner);
            Objects[effect].Position = Objects[obj].Position;
            Objects[effect].Velocity = Objects[obj].Velocity;
            Ships[obj].Maneuver = ShipManeuver.WarpingOut;
            Objects[obj].Counter = 6;
            return;
        }
        SetObjectsData(obj, ObjectType.HyperspaceJumpFlash, Objects[obj].Owner);
    }
}
