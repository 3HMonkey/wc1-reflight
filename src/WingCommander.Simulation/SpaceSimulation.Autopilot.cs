using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// The autopilot (auto.c, auto_pilot_valid of cockpt.c). The original auto_pilot_sequence blocks:
// set-up, a 120-frame cinematic (visit_the_cinema, UI), an instant travel loop, the arrival
// placement, one Update_3Space and the return to the cockpit view (UI). The port splits it around
// the cinematic (ADR-009/ADR-012):
//
//   if (sim.BeginAutopilot())                   // false: refused, the reason was shown
//   {
//       UI: visit_the_cinema(12, 0, 120)         // saves/clears PlayerVulnerable and
//                                                // PlayerCollisionResponse, ForceView(12, 0),
//                                                // 120 x (Update3Space + draw + present), restores
//       sim.AutopilotTravel();                   // or AutopilotTravelStep() until it returns false
//       sim.EndAutopilot();                      // speeds, formation, one Update3Space
//       UI: force_view(0, 0) (cockpitless: the viewport dance), mouse to the centre
//   }
public sealed partial class SpaceSimulation
{
    /// <summary>nAutopilotFormationShipCount: team ships (besides the player and the wingman) that
    /// travel with the autopilot; the last of them flies centred behind the player.</summary>
    public short AutopilotFormationShipCount;

    // The locals of auto_pilot_sequence that live across the cinematic.
    private int _autopilotSavedCannedSceneMode;
    private FixedVector _autopilotDestination;
    private short _autopilotInitialDistance;
    private FixedVector _autopilotTravelStep;
    private readonly sbyte[] _autopilotTravelMode = new sbyte[ObjectSlots.ShipSlotCount];

    /// <summary>
    /// Can the autopilot engage? Not without objectives, nor within 8000 of the current objective,
    /// with Kilrathi within 16000 or inside a hazard field; with <paramref name="showReason"/> the
    /// refusal is shown ("Already Near", "Enemy Near", "Hazard Near").
    /// </summary>
    /// <remarks>C: auto_pilot_valid (0x414380, cockpt.c); the cockpit light 4 polls it every 4th rendered
    /// frame (UI). It leaves <see cref="ToTarget"/> set, as in the original.</remarks>
    public bool AutoPilotValid(bool showReason)
    {
        SimulationCockpitMessage? reason = null;
        if (MissionObjectiveCount == 0)
            return false;
        if (DistanceFromPoint(ObjectSlots.Player, MissionObjectives[CurrentObjective].Position) < 8000)
            reason = SimulationCockpitMessage.AlreadyNear;
        else if (KilrathiNear(ObjectSlots.Player, 16000))
            reason = SimulationCockpitMessage.EnemyNear;
        else if (ActiveHazardField != -1)
            reason = SimulationCockpitMessage.HazardNear;
        if (showReason && reason is { } shown)
            Events.ShowCockpitMessage(shown, ObjectType.None);
        return reason is null;
    }

    /// <summary>The player's wingman: a ship whose wing leader is the player.</summary>
    /// <remarks>C: player_wingman (0x403EE0, auto.c).</remarks>
    public bool PlayerWingman(short obj) => obj != -1 && Ships[obj].WingLeader == 0;

    /// <summary>Sets the speed (whole units) and the velocity along the ship's nose.</summary>
    /// <remarks>C: set_speed (0x403F10, auto.c).</remarks>
    public void SetSpeed(short obj, short speed)
    {
        Objects[obj].Speed = speed << 8;
        FixVelocity(obj);
    }

    /// <summary>
    /// Autopilot formation around the player: wingmen of the player take their formation offset;
    /// other travellers fill slots alternating 650 left/right (the last one centred), 1800 units back
    /// per pair, and 500 up when they would overlap the wingman.
    /// </summary>
    /// <remarks>C: auto_position (0x403F40, auto.c).</remarks>
    public void AutoPosition(short obj, ref short formationSlot)
    {
        if (PlayerWingman(obj))
        {
            ref readonly var offset = ref Ships[obj].FormationOffset;
            Objects[obj].Position = PositionRelativeIjk(ObjectSlots.Player, offset.X, offset.Y, offset.Z);
            return;
        }
        formationSlot++;
        short lateral = (formationSlot & 1) >= 1 ? (short)650 : (short)-650;
        if (AutopilotFormationShipCount == formationSlot)
            lateral = 0;
        short vertical = 0;
        short forward = ScalarMath.MaxShort(1, (short)(formationSlot >> 1));
        forward = unchecked((short)(forward * -1800));
        if (YourWingman != -1)
        {
            int radii = Objects[obj].CollisionRadius + Objects[YourWingman].CollisionRadius;
            int separation = ScalarMath.AbsInt(Ships[YourWingman].FormationOffset.Z - forward);
            if (radii > separation)
                vertical = 500;
        }
        Objects[obj].Position = PositionRelativeIjk(ObjectSlots.Player, lateral, vertical, forward);
    }

    /// <summary>
    /// First part of the autopilot: refuses (false, reason shown) like <see cref="AutoPilotValid"/>;
    /// otherwise clears the cockpit and the sounds, stops every ship, lets the Confed team members
    /// without Kilrathi within 10000 travel along (other Confed ships are removed when the destination
    /// lies outside the current nav sphere), points the player at the destination at speed 60, puts
    /// the travellers near the player (or the wingman) into the autopilot formation and switches the AI
    /// off (<see cref="CannedSceneMode"/> 4). The UI then runs the 120-frame cinematic.
    /// </summary>
    /// <remarks>C: auto_pilot_sequence (0x404050, auto.c) up to <c>visit_the_cinema(12, 0, 120)</c>. Without
    /// objectives the flight path starts with -1 and the destination is the record before the objective
    /// table (<see cref="ObjectiveRecord"/>; the request is refused anyway).</remarks>
    public bool BeginAutopilot()
    {
        _autopilotSavedCannedSceneMode = CannedSceneMode;
        short formationSlot = 0;
        _autopilotDestination = ObjectiveRecord(FlightPath[CurrentNavPointIndex]).Position;
        _autopilotTravelStep = FixedVector.Zero;
        bool leaveCurrentNavPoint = true;
        if (!AutoPilotValid(true))
            return false;

        ref readonly var navPoint = ref MissionNavPoints[CurrentNavPoint];
        if (VectorMath.DistanceBetweenPoints(MissionObjectives[CurrentObjective].Position, navPoint.Position) <
            navPoint.ProximityRadius + 25)
        {
            leaveCurrentNavPoint = false;
        }
        CleanUpCockpit();
        Events.ResetSoundState();
        AutopilotFormationShipCount = 0;

        for (short ship = 0; ship < ObjectSlots.ShipSlotCount; ship++)
        {
            _autopilotTravelMode[ship] = 0;
            Objects[ship].Speed = 0;
            Objects[ship].Velocity = FixedVector.Zero;
            if (Objects[ship].Class < ObjectClass.Ship ||
                Ships[ship].SpecialManeuver == SpecialManeuver.Unknown9 ||
                Ships[ship].Side != Side.Imperial)
            {
                continue;
            }
            if (IsTeamMember(Ships[ship].MissionIndex))
            {
                if (!KilrathiNear(ship, 10000))
                {
                    _autopilotTravelMode[ship] = -1;
                    ref var o = ref Objects[ship];
                    ref var s = ref Ships[ship];
                    s.SpecialManeuver = SpecialManeuver.None;
                    s.RollGoal = 0;
                    o.RollRotation = 0;
                    s.PitchGoal = 0;
                    o.PitchRotation = 0;
                    s.YawGoal = 0;
                    o.YawRotation = 0;
                    if (ship != 0 && ship != YourWingman)
                        AutopilotFormationShipCount++;
                }
            }
            else if (leaveCurrentNavPoint)
            {
                RemoveObject(ship);
            }
        }

        _autopilotInitialDistance = DistanceFromPoint(ObjectSlots.Player, _autopilotDestination);
        PointAt(ObjectSlots.Player, _autopilotDestination);
        SetSpeed(ObjectSlots.Player, 60);
        _autopilotTravelMode[0] = 1;
        formationSlot = 0;

        for (short ship = 1; ship < ObjectSlots.ShipSlotCount; ship++)
        {
            if (_autopilotTravelMode[ship] != -1)
                continue;
            if (DistanceFromObject(ship, 0) > 20000)
            {
                if (PlayerWingman(ship))
                    _autopilotTravelMode[ship] = 1;
                else if (VectorMath.AreEqual(Ships[ship].Destination, _autopilotDestination))
                    _autopilotTravelMode[ship] = 2;
                else
                    _autopilotTravelMode[ship] = 3;
            }
            else
            {
                _autopilotTravelMode[ship] = PlayerWingman(ship) || VectorMath.AreEqual(Ships[ship].Destination, _autopilotDestination)
                    ? (sbyte)1
                    : (sbyte)3;
            }
            if (_autopilotTravelMode[ship] == 1)
            {
                AutoPosition(ship, ref formationSlot);
                Objects[ship].Velocity = VectorMath.Scale(Objects[0].Forward, 0x3c00);
                CopyFrame(0, ship);
                Objects[ship].Speed = Objects[0].Speed;
                Objects[ship].Velocity = Objects[0].Velocity;
            }
        }

        CannedSceneMode = 4;
        return true;
    }

    /// <summary>
    /// One step of the instant travel after the cinematic: the player jumps 400 units toward the
    /// destination, the nav sphere and hazard fields are updated; the travel ends (true → false) within
    /// 1000 of the destination, in a hazard field, within 4000 of a ship that does not travel along, or
    /// when non-routing Kilrathi are within 16000. Returns true while the travel continues.
    /// </summary>
    /// <remarks>C: one iteration of the <c>while (nCannedSceneMode == 4)</c> loop of auto_pilot_sequence
    /// (0x404050, auto.c). Draws no random numbers itself; entering a nav sphere or a hazard field can.</remarks>
    public bool AutopilotTravelStep()
    {
        if (CannedSceneMode != 4)
            return false;
        var travelStep = VectorMath.Delta(Objects[0].Position, _autopilotDestination);
        VectorMath.Normalize(ref travelStep);
        _autopilotTravelStep = VectorMath.Scale(travelStep, 0x19000);
        Objects[0].Position = VectorMath.Add(Objects[0].Position, _autopilotTravelStep);
        ReleaseStaleNavTarget();
        CheckHazards();

        short nearestShipRange = 0x7fff;
        short destinationRange = DistanceFromPoint(ObjectSlots.Player, _autopilotDestination);
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (Objects[other].Class >= ObjectClass.Ship && _autopilotTravelMode[other] == 0)
                nearestShipRange = ScalarMath.MinShort(nearestShipRange, DistanceFromObject(0, other));
        }
        if ((ushort)destinationRange < 1000 || ActiveHazardField != -1 || nearestShipRange < 4000 || ReportKilrathiRout(1))
            CannedSceneMode = _autopilotSavedCannedSceneMode;
        return CannedSceneMode == 4;
    }

    /// <summary>Runs <see cref="AutopilotTravelStep"/> until the travel ends (instant: no frames).</summary>
    /// <remarks>C: the <c>while (nCannedSceneMode == 4)</c> loop of auto_pilot_sequence (0x404050, auto.c).</remarks>
    public void AutopilotTravel()
    {
        while (AutopilotTravelStep())
        {
        }
    }

    /// <summary>
    /// Arrival: the player steps back the last 400 units, every traveller flies at the slowest cruise
    /// speed of the group (at most the player's maximum), the travellers are put back into the autopilot
    /// formation (or, far ones with their own destination, onto it when it is nearer than the trip),
    /// and one simulation frame runs. The UI then returns to the cockpit view.
    /// </summary>
    /// <remarks>C: auto_pilot_sequence (0x404050, auto.c) after the travel loop, up to and including
    /// <c>Update_3Space()</c>; the following <c>force_view(0, 0)</c> with the cockpitless viewport dance and
    /// <c>SetMousePosition</c> are UI.</remarks>
    public void EndAutopilot()
    {
        Objects[0].Position = VectorMath.Subtract(Objects[0].Position, _autopilotTravelStep);
        short cruiseSpeed = Ships[0].MaximumSpeed;
        for (short ship = 0; ship < ObjectSlots.ShipSlotCount; ship++)
        {
            if (_autopilotTravelMode[ship] != 0 && TypeDataOf(ship).CruiseVelocity < cruiseSpeed)
                cruiseSpeed = TypeDataOf(ship).CruiseVelocity;
        }

        short formationSlot = 0;
        for (short ship = 0; ship < ObjectSlots.ShipSlotCount; ship++)
        {
            if (_autopilotTravelMode[ship] == 0)
                continue;
            SetSpeed(ship, cruiseSpeed);
            if (ship == 0 || _autopilotTravelMode[ship] < 1)
                continue;
            switch (_autopilotTravelMode[ship])
            {
                case 1:
                case 2:
                    AutoPosition(ship, ref formationSlot);
                    break;
                case 3:
                    if (DistanceFromPoint(ship, Ships[ship].Destination) < _autopilotInitialDistance)
                        Objects[ship].Position = Ships[ship].Destination;
                    break;
            }
        }
        Update3Space();
    }
}
