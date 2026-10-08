using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// smart.c: collision avoidance, the AI tick regulator, target selection, formation flying,
// stress and the maneuver choice of the dogfight tick (intelligence_events).
public sealed partial class SpaceSimulation
{
    /// <summary>Turns away from where <paramref name="other"/> will be next frame: yaw when the
    /// offset is mostly vertical, else pitch, by <paramref name="amount"/> degrees; roll goal 0.</summary>
    /// <remarks>C: steer_away_from_object (0x433AC0, smart.c); object -1: see <see cref="PositionOf"/>.</remarks>
    public void SteerAwayFromObject(short obj, short other, short amount)
    {
        Ships[obj].RollGoal = 0;
        var predicted = VectorMath.Add(PositionOf(other), VelocityOf(other));
        var difference = VectorMath.Delta(Objects[obj].Position, predicted);
        var relative = TransformToObjectsFrame(difference, obj);
        if (ScalarMath.AbsInt(relative.X) < ScalarMath.AbsInt(relative.Y))
        {
            if (relative.X > 0)
                amount = unchecked((short)-amount);
            Ships[obj].YawGoal = amount;
        }
        else
        {
            if (relative.Y > 0)
                amount = unchecked((short)-amount);
            Ships[obj].PitchGoal = amount;
        }
    }

    /// <summary>As <see cref="SteerAwayFromObject"/> against the position
    /// <paramref name="predictionTicks"/> frames ahead (note the yaw sign is the opposite one).</summary>
    /// <remarks>C: steer_away_from_predicted_object (0x433B90, smart.c).</remarks>
    public void SteerAwayFromPredictedObject(short obj, short other, short predictionTicks, short amount)
    {
        Ships[obj].RollGoal = 0;
        var predicted = VectorMath.Scale(Objects[other].Velocity, predictionTicks << 8);
        predicted = VectorMath.Add(Objects[other].Position, predicted);
        var difference = VectorMath.Delta(Objects[obj].Position, predicted);
        var relative = TransformToObjectsFrame(difference, obj);
        if (ScalarMath.AbsInt(relative.X) < ScalarMath.AbsInt(relative.Y))
        {
            if (relative.X < 0)
                amount = unchecked((short)-amount);
            Ships[obj].YawGoal = amount;
        }
        else
        {
            if (relative.Y > 0)
                amount = unchecked((short)-amount);
            Ships[obj].PitchGoal = amount;
        }
    }

    /// <summary>
    /// The avoidance of an active collision alert: once the crash is 30 frames or more away, full speed
    /// and the alert winds down; otherwise brake when meeting head on, burn away when the obstacle is
    /// behind, and steer away from its predicted position (or veer randomly when it chases us).
    /// </summary>
    /// <remarks>C: prevent_collision (0x433C80, smart.c). <c>ship_vs_point</c> does not set the target
    /// facing: the tests read the value of the last facing query, as in the original.</remarks>
    public void PreventCollision(short obj)
    {
        short other = (sbyte)Ships[obj].CollisionAlertTarget;
        if (other == -1)
        {
            ClearAlert(obj);
            return;
        }
        short collisionTime = CrashTime(obj, other);
        if (collisionTime >= 30)
        {
            ApproachFullSpeed(obj);
            Try2EndCollisionAlert(obj);
            return;
        }
        ShipVsPoint(obj, Objects[other].Position);
        short facing = FacingToTarget;
        if (facing > 75)
        {
            if (TargetFacing < -70)
                ApproachZeroSpeed(obj);
            else
                ApproachFullSpeed(obj);
        }
        else if (facing < -70 && NormalSpeed(obj))
        {
            FireAfterburner(obj, 8);
        }
        else
        {
            ApproachFullSpeed(obj);
        }
        if (NoGoal(obj))
        {
            facing = FacingToTarget;
            if (facing < -60 && TargetFacing > 60)
            {
                VeerRandom(obj, 14);
                return;
            }
            short amount = ScalarMath.MinShort(facing, 25);
            amount = ScalarMath.MaxShort(0, amount);
            SteerAwayFromPredictedObject(obj, other, (short)(collisionTime >> 1), amount);
        }
    }

    /// <summary>Starts an alert for a predicted crash and runs the avoidance; true while avoiding
    /// (the AI tick is skipped then).</summary>
    /// <remarks>C: handle_collisions (0x433D90, smart.c).</remarks>
    public bool HandleCollisions(short obj)
    {
        short other = DetectCollisions(obj);
        if (other != -1)
            StartCollisionAlert(obj, other);
        if (AlertFlag(obj, 1))
            PreventCollision(obj);
        return AlertFlag(obj, 1);
    }

    /// <summary>
    /// Gate of the AI tick: dying ships do nothing, fighters avoiding a crash skip their tick, the
    /// turn regulator counts the frames between ticks (the turn interval of the pilot level).
    /// Returns true when no tick happens this frame; otherwise the tick counter advances.
    /// </summary>
    /// <remarks>C: regulate_turn (0x433DE0, smart.c).</remarks>
    public bool RegulateTurn(short obj)
    {
        if (Ships[obj].SpecialManeuver == SpecialManeuver.Unknown9)
            return true;
        if (Objects[obj].Class != ObjectClass.CapitalShip && HandleCollisions(obj))
            return true;
        Ships[obj].TurnRegulator--;
        if (Ships[obj].TurnRegulator > 0)
            return true;
        Ships[obj].Turn++;
        Ships[obj].TurnRegulator = Ships[obj].TurnInterval;
        return false;
    }

    /// <summary>New target: a Kilrathi takes the player half of the time when nobody attacks the player
    /// within 16000 and the player is within 5000; otherwise the nearest enemy within 16000 (or -1).</summary>
    /// <remarks>C: select_target (0x433E50, smart.c).</remarks>
    public void SelectTarget(short obj)
    {
        if (Ships[obj].Side == Side.Kilrathi && Random.Below(100) < 50 &&
            !AttackerInRange(0, 16000) && DistanceFromObject(obj, 0) < 5000)
        {
            Ships[obj].Target = 0;
            return;
        }
        Ships[obj].Target = unchecked((sbyte)ScanForEnemy(obj, 16000));
    }

    /// <summary>Turns <paramref name="amount"/> degrees left, right, up or down at random.</summary>
    /// <remarks>C: veer_random (0x433EC0, smart.c).</remarks>
    public void VeerRandom(short obj, short amount)
    {
        switch (Random.BelowOrEqual(3))
        {
            case 0:
                Ships[obj].YawGoal = amount;
                break;
            case 1:
                Ships[obj].YawGoal = unchecked((short)-amount);
                break;
            case 2:
                Ships[obj].PitchGoal = amount;
                break;
            case 3:
                Ships[obj].PitchGoal = unchecked((short)-amount);
                break;
        }
    }

    /// <summary>Formation slot of a wingman: the offset in the leader's frame plus three frames of the
    /// leader's motion.</summary>
    /// <remarks>C: compute_formation_destination (0x433FF0, smart.c).</remarks>
    public FixedVector ComputeFormationDestination(short leader, in ShortVector offset)
    {
        var destination = OffsetLocation(leader, offset);
        destination = VectorMath.Add(destination, Objects[leader].Velocity);
        destination = VectorMath.Add(destination, Objects[leader].Velocity);
        return VectorMath.Add(destination, Objects[leader].Velocity);
    }

    /// <summary>
    /// Speed control toward a point <paramref name="range"/> units away: when the time to get there
    /// leaves no room to change the speed, approach <paramref name="desiredSpeed"/>; with a small margin
    /// accelerate by one unit, else by the full acceleration.
    /// </summary>
    /// <remarks>C: control_speed (0x434040, smart.c).</remarks>
    public void ControlSpeed(short obj, ushort range, int desiredSpeed)
    {
        ushort travelTime = unchecked((ushort)(range / ScalarMath.MaxShort(FixedMath.ToShortSaturating(Objects[obj].Speed), 1)));
        int brakingMargin = GetShipAccelerationRate(obj) * travelTime;
        brakingMargin -= ScalarMath.AbsInt(desiredSpeed - Objects[obj].Speed);
        if (brakingMargin <= 0)
        {
            ApproachSpeed(obj, desiredSpeed);
            return;
        }
        if (brakingMargin < 12800)
        {
            Celerate(obj, 0x100);
            return;
        }
        Celerate(obj, GetShipAccelerationRate(obj));
    }

    /// <summary>
    /// Follows a point that moves with <paramref name="reference"/> (CHILL maneuver): slows down when the
    /// point is behind, turns toward it when far, matches the reference's roll when lined up, and copies
    /// its frame and speed once within 175 units and aligned.
    /// </summary>
    /// <remarks>C: chase_location (0x4340F0, smart.c).</remarks>
    public void ChaseLocation(short obj, in FixedVector destination, short reference)
    {
        bool pointAtDestination = false;
        int desiredSpeed = Objects[reference].Speed - 0x200;
        if (desiredSpeed < 0)
            desiredSpeed = 0;
        var forwardTravel = VectorMath.Scale(Objects[reference].Forward, Objects[reference].Speed * 15);
        var projectedDestination = VectorMath.Add(destination, forwardTravel);
        GetFacingRangeFromPoint(obj, projectedDestination);
        short forwardFacing = FacingToTarget;
        GetFacingRangeFromPoint(obj, destination);
        if (FacingToTarget < 0)
        {
            ApproachSpeed(obj, 0);
            short speed = FixedMath.ToShortSaturating(Objects[reference].Speed);
            speed = ScalarMath.MaxShort(speed, 1);
            if (TargetRange / speed > 49)
                pointAtDestination = true;
        }
        else if (TargetRange > 175 && NoGoal(obj))
        {
            pointAtDestination = true;
        }
        ControlSpeed(obj, unchecked((ushort)TargetRange), desiredSpeed);
        if (FacingToTarget > 85 && TargetRange > 175)
            Ships[obj].RollGoal = MatchRollOrientation(obj, reference);
        if (pointAtDestination)
        {
            var toTarget = ToTarget;
            PointShip(obj, 0, toTarget);
        }
        if (TargetRange < 175)
        {
            if (forwardFacing > 90)
            {
                CopyFrame(reference, obj);
                SteadyObject(obj);
                ApproachSpeed(obj, Objects[reference].Speed);
                return;
            }
            PointShip(obj, 0, forwardTravel);
        }
        if (TargetRange < 600)
            TrimGoals(obj, 10);
    }

    /// <summary>Flies to a point: half speed while turning toward it, cruise speed when it is less than
    /// 50 frames away within 3000, else full speed.</summary>
    /// <remarks>C: goto_location (0x4342C0, smart.c). No caller in the game.</remarks>
    public void GotoLocation(short obj, in FixedVector destination)
    {
        ShipVsPoint(obj, destination);
        if (FacingToTarget < 51)
        {
            ApproachHalfSpeed(obj);
            if (NoGoal(obj))
                PointShipAtPoint(obj, destination);
            return;
        }
        short range = TargetRange;
        if (range <= 3000)
        {
            short speed = FixedMath.ToShortSaturating(Objects[obj].Speed);
            speed = ScalarMath.MaxShort(speed, 1);
            if (range / speed <= 50)
            {
                ApproachCruiseSpeed(obj);
                return;
            }
        }
        ApproachFullSpeed(obj);
    }

    /// <summary>
    /// Flies into the formation slot <paramref name="destination"/> of <paramref name="leader"/>: minimum
    /// speed while facing away, full speed (afterburner beyond 3000 when lined up) beyond 2000, speed
    /// control toward the leader's speed beyond 200, else the leader's speed; within 200 it holds the
    /// leader's heading (copies the frame when aligned), within 700 the turns are trimmed and the roll
    /// matched.
    /// </summary>
    /// <remarks>C: goto_formation (0x434360, smart.c).</remarks>
    public void GotoFormation(short obj, in FixedVector destination, short leader)
    {
        var projected = VectorMath.Add(Objects[obj].Position, Objects[leader].Forward);
        GetFacingRangeFromPoint(obj, projected);
        short forwardFacing = FacingToTarget;
        GetFacingRangeFromPoint(obj, destination);
        if (FacingToTarget < 40)
        {
            ApproachMinSpeed(obj);
        }
        else if (TargetRange > 2000)
        {
            ApproachFullSpeed(obj);
            if (TargetRange > 3000 && FacingToTarget > 70 && NormalSpeed(obj))
                FireAfterburner(obj, 5);
        }
        else if (TargetRange > 200)
        {
            ControlSpeed(obj, unchecked((ushort)TargetRange), Objects[leader].Speed);
        }
        else
        {
            ApproachShipSpeed(obj, leader);
        }
        if (TargetRange < 200)
        {
            SteadyObject(obj);
            if (forwardFacing > 90)
            {
                CopyFrame(leader, obj);
                return;
            }
            PointParallel(obj, leader);
        }
        else if (NoGoal(obj))
        {
            var toTarget = ToTarget;
            PointShip(obj, 0, toTarget);
        }
        if (TargetRange < 700)
        {
            TrimGoals(obj, 10);
            if (forwardFacing > 90)
                Ships[obj].RollGoal = MatchRollOrientation(obj, leader);
        }
    }

    /// <summary>Holds the formation slot behind the wing leader.</summary>
    /// <remarks>C: maintain_formation (0x4344E0, smart.c). A ship without a leader (-1: an Imperial
    /// wingman whose leader record was not in space) does nothing; the original reads slot -1.</remarks>
    public void MaintainFormation(short obj)
    {
        short leader = Ships[obj].WingLeader;
        if ((uint)leader >= ObjectSlots.Count)
            return;
        var destination = ComputeFormationDestination(leader, Ships[obj].FormationOffset);
        Ships[obj].Destination = destination;
        GotoFormation(obj, destination, leader);
    }

    /// <summary>Would set the stress from the ship's health after its target was lost, but the original
    /// only acts for object numbers of 12 and above, which are never ships: no effect.</summary>
    /// <remarks>C: reset_stress (0x434550, smart.c): <c>if (obj &gt;= 12)</c> guards the whole body.</remarks>
    public static void ResetStress(short obj)
    {
        _ = obj;
    }

    /// <summary>Morale from stress: 0 calm (below 15), 1 stressed (below 30), 2 panic.</summary>
    /// <remarks>C: stress_morale (0x4345D0, smart.c).</remarks>
    public short StressMorale(short obj)
    {
        if (Ships[obj].Stress < 15)
            return 0;
        if (Ships[obj].Stress < 30)
            return 1;
        return 2;
    }

    /// <summary>A random defense maneuver of the pilot level's list; like the original the roll can
    /// pick the list's -1 terminator (NONE) with probability 1/(n+1).</summary>
    /// <remarks>C: any_defense (0x4345F0, smart.c): <c>maneuvers[RandomBelowOrEqual(count)]</c>.</remarks>
    public ShipManeuver AnyDefense(short obj)
    {
        var maneuvers = AiTables.DefenseManeuvers(Ships[obj].PilotLevel);
        int index = Random.BelowOrEqual(maneuvers.Length);
        return index < maneuvers.Length ? (ShipManeuver)maneuvers[index] : ShipManeuver.None;
    }

    /// <summary>
    /// Maneuver choice of a generic Confed pilot for AI event <paramref name="aiEvent"/>: panic → OUTA_HERE;
    /// keep the maneuver for events 0/3/4/7 when the event repeats or 20% of the time (unless a 3%
    /// re-roll or no maneuver); else per event (0 strafe a capital ship / zip past by skill / defense,
    /// 2 try to tail, 3 drop a mine 10% / defense, 4 strafe by skill / defense, 5 tail fire, 6 hard turn
    /// (veterans) or wabble, 7 defense, 8 line up, 1 roll over).
    /// </summary>
    /// <remarks>C: pick_regular_maneuver (0x434630, smart.c).</remarks>
    public ShipManeuver PickRegularManeuver(short obj, int aiEvent)
    {
        bool reroll = Random.BelowOrEqual(100) < 3 || Ships[obj].Maneuver == ShipManeuver.None;
        short morale = StressMorale(obj);
        if (morale == 2)
            return ShipManeuver.OutaHere;
        if ((Ships[obj].IntelligenceEvent == aiEvent || Random.BelowOrEqual(100) < 20) &&
            (aiEvent == 0 || aiEvent == 3 || aiEvent == 4 || aiEvent == 7) &&
            !reroll)
        {
            return Ships[obj].Maneuver;
        }

        switch (aiEvent)
        {
            case 0:
                if (Objects[Ships[obj].Target].Class == ObjectClass.CapitalShip)
                    return ShipManeuver.StrafeEnemy;
                if (Random.Below(100) < Ships[obj].PilotLevel * 5 + 60)
                    return ShipManeuver.ZipPast;
                return AnyDefense(obj);
            case 2:
                return ShipManeuver.Try2Tail;
            case 3:
                if (MineAvailable(obj) != -1 && Random.Below(100) < 10)
                    return ShipManeuver.DropAMine;
                return AnyDefense(obj);
            case 4:
                if (Random.BelowOrEqual(100) >= Ships[obj].PilotLevel * 20 + 30)
                    return AnyDefense(obj);
                return ShipManeuver.StrafeEnemy;
            case 5:
                return ShipManeuver.TailFire;
            case 6:
                return Ships[obj].PilotLevel >= 2 ? ShipManeuver.HardTurn : ShipManeuver.Wabble;
            case 7:
                return AnyDefense(obj);
            case 8:
                return ShipManeuver.LineUpDrop;
            default:
                return ShipManeuver.RollOver;
        }
    }

    /// <summary>
    /// Weighted choice from a table entry: a new maneuver is chosen when none runs, 10% of the time when
    /// the running one is neither of the pair (and both are real maneuvers below 45), and 5% otherwise;
    /// the roll <c>RandomBelowOrEqual(100) &lt; threshold</c> picks the primary, else the secondary.
    /// </summary>
    /// <remarks>C: pick_from_list (0x434800, smart.c).</remarks>
    public ShipManeuver PickFromList(in ManeuverChoice choice, short obj)
    {
        var maneuver = Ships[obj].Maneuver;
        bool chooseAgain = maneuver == ShipManeuver.None;
        if (choice.Primary != (int)maneuver && choice.Secondary != (int)maneuver &&
            Random.Below(100) < 10 &&
            choice.Primary < (int)ShipManeuver.Unknown45 && choice.Secondary < (int)ShipManeuver.Unknown45)
        {
            chooseAgain = true;
        }
        if (!chooseAgain && Random.BelowOrEqual(100) < 5)
            chooseAgain = true;
        if (chooseAgain)
        {
            maneuver = Random.BelowOrEqual(100) >= choice.Threshold
                ? (ShipManeuver)choice.Secondary
                : (ShipManeuver)choice.Primary;
        }
        return maneuver;
    }

    /// <summary>Maneuver choice of a generic Kilrathi pilot from its table; 45 means strafe, 46 a
    /// random defense maneuver.</summary>
    /// <remarks>C: pick_kilrathi_maneuver (0x4348A0, smart.c).</remarks>
    public ShipManeuver PickKilrathiManeuver(short obj, int aiEvent)
    {
        var choice = AiTables.KilrathiManeuverChoice(Ships[obj].PilotLevel, aiEvent, StressMorale(obj));
        var maneuver = PickFromList(choice, obj);
        return maneuver switch
        {
            ShipManeuver.Unknown45 => ShipManeuver.StrafeEnemy,
            ShipManeuver.Unknown46 => AnyDefense(obj),
            _ => maneuver,
        };
    }

    /// <summary>Chooses the maneuver for an AI event (rated pilots from their personal table, generic
    /// Kilrathi from theirs, generic Confed by rule) and starts it when it differs.</summary>
    /// <remarks>C: process_maneuver_node (0x434900, smart.c).</remarks>
    public void ProcessManeuverNode(short obj, int aiEvent)
    {
        short rating = Ships[obj].Rating;
        ShipManeuver maneuver;
        if (rating == -1)
        {
            maneuver = Ships[obj].Side == Side.Kilrathi
                ? PickKilrathiManeuver(obj, aiEvent)
                : PickRegularManeuver(obj, aiEvent);
        }
        else
        {
            short morale = StressMorale(obj);
            var choice = AiTables.RatedManeuverChoice(rating, aiEvent, morale);
            maneuver = PickFromList(choice, obj);
        }
        if (Ships[obj].Maneuver != maneuver)
            ResetManeuver(obj, maneuver);
    }

    /// <summary>
    /// Stress bookkeeping of an AI tick: being tailed / head on / just hit (3, 4, 7) add the pilot's
    /// aggression, sitting on the enemy's tail (5) subtracts it, a missile on the tail (6) adds twice,
    /// a dying target (8) halves it, quiet ticks (-1, 2) recover; then by health (below 40: +2 x
    /// aggression, below 75: + aggression up to 28, else at most 7); a missile caps it at 29; never
    /// negative. The stress is a signed byte and wraps like the original's.
    /// </summary>
    /// <remarks>C: handle_stress (0x434980, smart.c).</remarks>
    public void HandleStress(short obj, int aiEvent)
    {
        short aggression = AiTables.PilotAggression[Ships[obj].PilotLevel];
        switch (aiEvent)
        {
            case 3:
            case 4:
            case 7:
                Ships[obj].Stress = unchecked((sbyte)(Ships[obj].Stress + aggression));
                break;
            case 5:
                Ships[obj].Stress = unchecked((sbyte)(Ships[obj].Stress - aggression));
                break;
            case 6:
                Ships[obj].Stress = unchecked((sbyte)(Ships[obj].Stress + aggression * 2));
                break;
            case 8:
                Ships[obj].Stress = unchecked((sbyte)(Ships[obj].Stress / 2));
                break;
            case -1:
            case 2:
                Ships[obj].Stress = unchecked((sbyte)(Ships[obj].Stress - AiTables.PilotRecovery[Ships[obj].PilotLevel]));
                break;
        }
        short damage = EvaluateDamage(obj);
        if (damage < 40)
            Ships[obj].Stress = unchecked((sbyte)(Ships[obj].Stress + aggression * 2));
        else if (damage < 75)
            Ships[obj].Stress = unchecked((sbyte)ScalarMath.MinShort(unchecked((short)(Ships[obj].Stress + aggression)), 28));
        else
            Ships[obj].Stress = unchecked((sbyte)ScalarMath.MinShort(Ships[obj].Stress, 7));
        if (aiEvent == 6 && Ships[obj].Stress >= 30)
            Ships[obj].Stress = 29;
        if (Ships[obj].Stress < 0)
            Ships[obj].Stress = 0;
    }

    /// <summary>
    /// The dogfight tick's perception: classifies the situation against the target into an AI event
    /// (-1 nothing / target gone, 6 missile on the tail, 8 target dying, 2 far beyond 8000, 7 just hit,
    /// 5 on the target's tail, 4 head on, 3 being tailed, 1 target stopped, 0 otherwise), updates the
    /// stress, picks a maneuver, retargets when the target is gone (or drops the objective without
    /// enemies within 16000), lets the player's wingman complain (stress crossing 15: line 4; 0.4% per
    /// tick when the player is badly hurt: line 8 if the wingman is healthier, else 4).
    /// </summary>
    /// <remarks>C: intelligence_events (0x434A80, smart.c).</remarks>
    public void IntelligenceEvents(short obj)
    {
        int aiEvent = -1;
        bool targetGone = false;
        short target = Ships[obj].Target;
        short previousStress = Ships[obj].Stress;
        if (MissileOnTail(obj))
        {
            aiEvent = 6;
        }
        else if (Unactive(target))
        {
            targetGone = true;
        }
        else if (Ships[target].SpecialManeuver == SpecialManeuver.Unknown9)
        {
            aiEvent = 8;
        }
        else
        {
            aiEvent = 0;
            ShipVsShip(obj, target);
            if (TargetRange > 8000)
                aiEvent = 2;
            else if (Ships[obj].AiCooldown > 0)
                aiEvent = 7;
            else if (FacingToTarget > 55 && TargetFacing < -55)
                aiEvent = 5;
            else if (FacingToTarget > 75 && TargetFacing > 75)
                aiEvent = 4;
            else if (FacingToTarget < -60 && TargetFacing > 85 && TargetRange < 7000)
                aiEvent = 3;
            else if (Objects[target].Speed < 20)
                aiEvent = 1;
        }

        HandleStress(obj, aiEvent);
        if (aiEvent != -1)
            ProcessManeuverNode(obj, aiEvent);
        if (aiEvent == -1 && targetGone)
        {
            if (!AnyEnemy(obj, 16000))
                ResetObjective(obj, ShipObjective.None);
            else
                SelectTarget(obj);
            ResetStress(obj);
        }

        if (YourWingman == obj && Objects[ObjectSlots.Player].Class == ObjectClass.Ship &&
            Ships[YourWingman].WingmanMessageState == -1)
        {
            if (previousStress < 15 && Ships[obj].Stress >= 15)
            {
                SendMessage(obj, 4);
            }
            else
            {
                short playerDamage = EvaluateDamage(0);
                if (Random.Below(1000) < 4 && playerDamage < 35)
                {
                    if (EvaluateDamage(obj) > playerDamage)
                        SendMessage(obj, 8);
                    else
                        SendMessage(obj, 4);
                }
            }
        }
        Ships[obj].IntelligenceEvent = aiEvent;
    }

    /// <summary>Holds a distance to the target: full speed when farther than <paramref name="range"/>,
    /// stop when nearer, else the target's speed.</summary>
    /// <remarks>C: chase_speed (0x434C70, smart.c), against the range of the last facing query.</remarks>
    public void ChaseSpeed(short obj, short range)
    {
        short targetRange = TargetRange;
        if (range < targetRange)
        {
            ApproachFullSpeed(obj);
            return;
        }
        if (range > targetRange)
        {
            ApproachZeroSpeed(obj);
            return;
        }
        ApproachSpeed(obj, Objects[Ships[obj].Target].Speed);
    }
}
