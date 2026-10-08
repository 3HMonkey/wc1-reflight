using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Objective list, flight path, current destination and nav map coordinates (brains.c, cockpt.c).
public sealed partial class SpaceSimulation
{
    private const int MaxObjectiveSelectionAttempts = 3 * (ObjectiveCount + 1);

    /// <summary>
    /// Converts the objective sources into runtime objectives until the -1 terminator: type 0 nav
    /// points and types 1..4 mission ships (positioned at their sphere point) join the flight path.
    /// Then selects the first visible destination.
    /// </summary>
    /// <remarks>C: Build_objective_list (0x40CED0, brains.c). Guards: at most 16 sources are read
    /// (the original reads past the table without a terminator) and the destination search stops
    /// after a bounded number of attempts when every objective is hidden (the original loops forever).</remarks>
    public void BuildObjectiveList()
    {
        int flightPathCount = 0;
        NavMapCoordinateScaling = false;
        MissionObjectiveCount = 0;
        var position = FixedVector.Zero;
        string displayName = "";
        for (int source = 0; source < ObjectiveCount && MissionObjectiveSources[source].Type != -1; source++)
        {
            ref var src = ref MissionObjectiveSources[source];
            int type = src.Type;
            ref var objective = ref MissionObjectives[MissionObjectiveCount];
            objective.Flags = 0;
            if (type == 0)
            {
                position = MissionNavPoints[src.Index].Position;
                displayName = MissionNavPoints[src.Index].Name ?? "";
                FlightPath[flightPathCount++] = MissionObjectiveCount;
            }
            else if (type >= 1 && type <= 4)
            {
                ref var ship = ref MissionShips[src.Index];
                displayName = ObjectTypeTable.Get(ship.Type).DisplayName;
                position = SetSpherePoint(ship);
                FlightPath[flightPathCount++] = MissionObjectiveCount;
            }
            objective.Type = type;
            objective.Index = unchecked((sbyte)src.Index);
            objective.Name = src.Description ?? "";
            objective.Position = position;
            (objective.MapX, objective.MapY) = NavGetXY(position.X, position.Z);
            objective.DisplayName = displayName;
            MissionObjectiveCount++;
        }

        FlightPath[flightPathCount] = -1;
        MissionObjectives[(byte)MissionObjectiveCount].Type = -1;
        CurrentNavPointIndex = 0;
        CurrentObjective = 0;
        if (MissionObjectiveCount != 0)
        {
            for (int attempt = 0; attempt < MaxObjectiveSelectionAttempts && !SetNewObjective(CurrentNavPointIndex); attempt++)
                CurrentNavPointIndex++;
        }
    }

    /// <summary>World X/Z to nav map coordinates (<c>(v / 100) &gt;&gt; 8</c>), optionally scaled to the map.</summary>
    /// <remarks>C: nav_getxy (0x40CC30, brains.c).</remarks>
    public (short X, short Y) NavGetXY(int worldX, int worldZ)
    {
        short x = unchecked((short)((worldX / 100) >> 8));
        short y = unchecked((short)((worldZ / 100) >> 8));
        if (NavMapCoordinateScaling)
            return ScaleNavMapCoordinates(x, y);
        return (x, y);
    }

    /// <summary>Map coordinates to nav map pixels around the centre (75, 67).</summary>
    /// <remarks>C: ScaleNavMapCoordinates (0x40CBE0, brains.c).</remarks>
    public (short X, short Y) ScaleNavMapCoordinates(short mapX, short mapY) =>
        (unchecked((short)((mapX - NavMapCentreX) / NavMapScale + 75)),
         unchecked((short)((NavMapCentreY - mapY) / NavMapScale + 67)));

    /// <remarks>C: ScaleNavMapMarkerSize (0x40CBC0, brains.c).</remarks>
    public short ScaleNavMapMarkerSize(short size) => unchecked((short)(size / (NavMapScale * 100)));

    /// <summary>Extends the nav map bounding box by a map point.</summary>
    /// <remarks>C: CheckPoint (0x40CC80, brains.c).</remarks>
    public void CheckPoint(short x, short y)
    {
        NavMapMinimumX = ScalarMath.MinShort(NavMapMinimumX, x);
        NavMapMaximumX = ScalarMath.MaxShort(NavMapMaximumX, x);
        NavMapMinimumY = ScalarMath.MinShort(NavMapMinimumY, y);
        NavMapMaximumY = ScalarMath.MaxShort(NavMapMaximumY, y);
    }

    /// <remarks>C: IncludeNavMapWorldPoint (0x40CCF0, brains.c).</remarks>
    public void IncludeNavMapWorldPoint(int worldX, int worldZ)
    {
        var (x, y) = NavGetXY(worldX, worldZ);
        CheckPoint(x, y);
    }

    /// <summary>Fits the nav map scale and centre to all objectives and the player.</summary>
    /// <remarks>C: SetScale (0x40CD30, brains.c).</remarks>
    public void SetScale()
    {
        NavMapCoordinateScaling = false;
        NavMapMinimumX = MissionObjectives[0].MapX;
        NavMapMaximumX = MissionObjectives[0].MapX;
        NavMapMinimumY = MissionObjectives[0].MapY;
        NavMapMaximumY = MissionObjectives[0].MapY;
        for (short objectiveIndex = 0; objectiveIndex < MissionObjectiveCount; objectiveIndex++)
        {
            ref var objective = ref MissionObjectives[objectiveIndex];
            if (MobileObjective(objectiveIndex))
            {
                short ship = FindShipIndex(objective.Index);
                if (ship != -1)
                    objective.Position = Objects[ship].Position;
            }
            (objective.MapX, objective.MapY) = NavGetXY(objective.Position.X, objective.Position.Z);
            CheckPoint(objective.MapX, objective.MapY);
        }
        IncludeNavMapWorldPoint(Objects[0].Position.X, Objects[0].Position.Z);
        short width = unchecked((short)(NavMapMaximumX - NavMapMinimumX));
        short height = unchecked((short)(NavMapMaximumY - NavMapMinimumY));
        short halfWidth = (short)(width / 2);
        NavMapCentreX = unchecked((short)(NavMapMinimumX + halfWidth));
        short halfHeight = (short)(height / 2);
        NavMapCentreY = unchecked((short)(NavMapMinimumY + halfHeight));
        NavMapScale = ScalarMath.MaxShort(unchecked((short)((width + halfWidth) / 150)), unchecked((short)((halfHeight + height) / 135)));
        if (NavMapScale == 0)
            NavMapScale = 100;
        NavMapCoordinateScaling = true;
    }

    /// <summary>Objective types 1..4 (mission ships) move.</summary>
    /// <remarks>C: mobile_objective (0x415A30, cockpt.c). Objective -1: see <see cref="ObjectiveRecord"/>.</remarks>
    public bool MobileObjective(short objective)
    {
        int type = ObjectiveRecord(objective).Type;
        return type == 1 || type == 3 || type == 4 || type == 2;
    }

    /// <remarks>C: sighted (0x415050, cockpt.c). Objective -1: see <see cref="ObjectiveRecord"/>.</remarks>
    public bool Sighted(short objective) => (ObjectiveRecord(objective).Flags & MissionObjective.FlagSighted) == MissionObjective.FlagSighted;

    /// <remarks>C: visited (0x415070, cockpt.c). Objective -1: see <see cref="ObjectiveRecord"/>.</remarks>
    public bool Visited(short objective) => (ObjectiveRecord(objective).Flags & MissionObjective.FlagVisited) == MissionObjective.FlagVisited;

    /// <remarks>C: achieved (0x415090, cockpt.c). Objective -1: see <see cref="ObjectiveRecord"/>.</remarks>
    public bool Achieved(short objective) => (ObjectiveRecord(objective).Flags & MissionObjective.FlagAchieved) == MissionObjective.FlagAchieved;

    /// <remarks>C: flag_objective (0x4150B0, cockpt.c). Objective -1 writes the flags byte of the record
    /// before the table (see <see cref="ObjectiveRecord"/>): the high byte of slot 61's animation index.</remarks>
    public void FlagObjective(short objective, byte flags)
    {
        if (objective == -1)
        {
            ref short animationIndex = ref Objects[ObjectSlots.Eye].AnimationIndex;
            animationIndex = unchecked((short)((ushort)animationIndex | flags << 8));
            return;
        }
        MissionObjectives[objective].Flags |= flags;
    }

    /// <summary>
    /// The objective record at <paramref name="objective"/>, including -1. Ships that fly past the end of
    /// the flight path use its -1 terminator as an objective index (<c>get_follow_point</c>,
    /// <c>cruise_home</c>, <c>cruise_to_destination</c>, <c>arrive_from_warp</c>, and <c>coming_home</c> /
    /// the autopilot without a home-base objective); the original then reads the 0x1f bytes before
    /// <c>aMissionObjectives</c> (0x0059dac0). In the Kilrathi Saga image that record overlays the end of
    /// <c>asObjectAnimationIndex</c> (0x0059da30): its type is the animation indices of slots 59 (low
    /// word) and 60 (high word), its index and flags are the two bytes of slot 61's animation index, and
    /// its position lies in the zero padding after <c>cCockpitView</c> (0x0059dab0). The port reproduces
    /// that layout (assuming the padding is zero, as the image's BSS is).
    /// </summary>
    /// <remarks>C: <c>aMissionObjectives[-1]</c>; layout from the address map of globals.c.</remarks>
    public MissionObjective ObjectiveRecord(int objective)
    {
        if (objective != -1)
            return MissionObjectives[objective];
        ushort eye = unchecked((ushort)Objects[ObjectSlots.Eye].AnimationIndex);
        return new MissionObjective
        {
            Type = unchecked((int)((ushort)Objects[59].AnimationIndex | (uint)(ushort)Objects[60].AnimationIndex << 16)),
            Index = unchecked((sbyte)eye),
            Flags = (byte)(eye >> 8),
            Position = FixedVector.Zero,
            DisplayName = "",
            Name = "",
        };
    }

    /// <summary>
    /// Hidden objectives: names starting with '.', mobile objectives whose ship left or died (state
    /// ≠ 0), and (a test that never fires because it reads the -1 terminator's type) spawned
    /// WARP_ARRIVE ships.
    /// </summary>
    /// <remarks>C: hidden_objective (0x4151F0, cockpt.c).</remarks>
    public bool HiddenObjective(short objective)
    {
        if (objective < 0 || objective >= MissionObjectives.Length)
            return true;
        ref var o = ref MissionObjectives[objective];
        bool nameHidden = StartsWithDot(o.DisplayName) || StartsWithDot(o.Name);
        bool mobile = MobileObjective(objective);
        bool hidden = nameHidden || (mobile && MissionShips[o.Index].State != 0);
        if (!hidden && mobile && MissionObjectives[(byte)MissionObjectiveCount].Type == 0)
        {
            short ship = FindShipIndex(o.Index);
            if (MissionShips[o.Index].MissionType == ShipMissionType.WarpArrive && ship != -1)
                hidden = true;
        }
        return hidden;
    }

    /// <summary>
    /// Makes flight-path entry <paramref name="pathIndex"/> the current destination (negative = the
    /// last entry; out of range = the first). Returns false when that objective is hidden.
    /// </summary>
    /// <remarks>C: set_new_objective (0x4152C0, cockpt.c).</remarks>
    public bool SetNewObjective(short pathIndex)
    {
        if (pathIndex < 0)
        {
            pathIndex = -1;
            do
            {
                pathIndex++;
            }
            while (FlightPath[pathIndex] != -1 && pathIndex < MissionObjectiveCount);
            pathIndex--;
        }
        if (pathIndex > MissionObjectiveCount)
            pathIndex = 0;
        if (pathIndex < 0 || FlightPath[pathIndex] == -1)
            pathIndex = 0;
        CurrentNavPointIndex = unchecked((sbyte)pathIndex);
        if (HiddenObjective(FlightPath[pathIndex]))
            return false;
        CurrentObjective = FlightPath[CurrentNavPointIndex];
        Ships[0].Objective = (ShipObjective)MissionObjectives[CurrentObjective].Type;
        SetObjectiveRange(false);
        return true;
    }

    /// <summary>Advances to the next visible objective (gives up after three wraps).</summary>
    /// <remarks>C: cycle_next_objective (0x415370, cockpt.c).</remarks>
    public bool CycleNextObjective()
    {
        int wraps = 0;
        do
        {
            if (SetNewObjective(unchecked((short)(CurrentNavPointIndex + 1))))
                break;
            if (CurrentNavPointIndex == 0)
                wraps++;
        }
        while (wraps < 3);
        if (wraps >= 3)
        {
            CurrentNavPointIndex = 0;
            CurrentObjective = FlightPath[0];
        }
        return wraps < 3;
    }

    /// <summary>Selects the first visible, unvisited flight-path entry (or cycles when all are visited).</summary>
    /// <remarks>C: set_next_destination (0x4153D0, cockpt.c).</remarks>
    public void SetNextDestination()
    {
        SetNewObjective(0);
        do
        {
            if (SetNewObjective(CurrentNavPointIndex) && !Visited(FlightPath[CurrentNavPointIndex]))
                break;
            CurrentNavPointIndex++;
        }
        while (CurrentNavPointIndex < MissionObjectiveCount && FlightPath[CurrentNavPointIndex] != -1);
        if (CurrentNavPointIndex >= MissionObjectiveCount || FlightPath[CurrentNavPointIndex] == -1)
        {
            SetNewObjective(0);
            CycleNextObjective();
        }
        Events.DestinationChanged();
    }

    /// <summary>Moves a mobile objective to its ship's current or expected position.</summary>
    /// <remarks>C: LocateMobileObjective (0x415470, cockpt.c); returns locate_ship's 1/0, or -1 when not mobile.</remarks>
    public short LocateMobileObjective(short objective)
    {
        if (!MobileObjective(objective))
            return -1;
        if (objective == -1)
        {
            // The original writes the position into the padding before the table, which the port
            // models as zero (ObjectiveRecord); the write is not kept. Not reached by the shipped data.
            var position = FixedVector.Zero;
            return LocateShip(ObjectiveRecord(-1).Index, ref position) ? (short)1 : (short)0;
        }
        ref var o = ref MissionObjectives[objective];
        return LocateShip(o.Index, ref o.Position) ? (short)1 : (short)0;
    }

    /// <summary>Updates <see cref="CurrentObjectiveRange"/> (whole units) to the current objective.</summary>
    /// <remarks>C: set_objective_range (0x415B70, cockpt.c); the scanner marker (showOnScanner) is
    /// cockpit drawing and not part of the simulation.</remarks>
    public void SetObjectiveRange(bool showOnScanner)
    {
        _ = showOnScanner;
        LocateMobileObjective(CurrentObjective);
        var relative = VectorMath.Delta(Objects[0].Position, ObjectiveRecord(CurrentObjective).Position);
        var rotated = TransformToObjectsFrame(relative, 0);
        var spherical = default(SphericalVector);
        VectorMath.RectangularToSpherical(rotated, ref spherical);
        CurrentObjectiveRange = spherical.Radius >> 8;
    }

    private static bool StartsWithDot(string? text) => !string.IsNullOrEmpty(text) && text[0] == '.';
}
