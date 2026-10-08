using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// The mission level of the AI (brains.c, cruise_home .. capital_ship_intelligence): every AI tick
// ship_intelligence runs the handler of the ship's mission type, which works through objectives and
// tactics and, in a fight, runs the dogfight tick (maneuvering = intelligence_events +
// perform_maneuver). Capital ships have their own, simpler brain.
public sealed partial class SpaceSimulation
{
    /// <summary>
    /// The CRUISE tactic of a ship coming home (every 8th AI tick, phase 5): the player's wingman more
    /// than 16000 away is removed; capital ships cruise, fighters use the afterburner; at the home spot
    /// (within 5000) the ship stops (HEAD_HOME, engines off); at a waypoint (within 1500) the reached
    /// flight-path objective is flagged visited (unless it is the home base) and the next one follows.
    /// </summary>
    /// <remarks>C: cruise_home (0x409760, brains.c). Like the original the code goes on with a wingman it
    /// just removed, and a -1 flight-path entry is objective -1 (<see cref="ObjectiveRecord"/>). A path
    /// index of -1 (no waypoint was ever found) skips the flag (the original reads before
    /// <c>abFlightPath</c>).</remarks>
    public void CruiseHome(short obj)
    {
        if (Abandoned(obj, 0) || (Ships[obj].Turn & 7) != 5)
            return;
        if (obj == YourWingman && DistanceFromObject(obj, 0) > 16000)
            RemoveObject(obj);
        if (Objects[obj].Class == ObjectClass.CapitalShip)
            ApproachCruiseSpeed(obj);
        else if (NormalSpeed(obj))
            FireAfterburner(obj, 10);

        if (NoGoal(obj))
            PointShipAtPoint(obj, Ships[obj].Destination);
        short range = DistanceFromPoint(obj, Ships[obj].Destination);
        if (VectorMath.AreEqual(Ships[obj].Destination, Ships[obj].MissionSpot))
        {
            if (range < 5000)
            {
                ResetTactic(obj, ShipTactic.HeadHome);
                SetSpecial(obj, SpecialManeuver.KillEngines);
                Objects[obj].Velocity = FixedVector.Zero;
            }
            return;
        }
        if (range < 1500)
        {
            sbyte pathIndex = Ships[obj].NavPointIndex;
            if ((uint)pathIndex < (uint)FlightPath.Length)
            {
                short objective = FlightPath[pathIndex];
                if (ObjectiveRecord(objective).Type != 1)
                    FlagObjective(objective, MissionObjective.FlagVisited);
            }
            GetFollowPoint(obj, ref Ships[obj].Destination);
        }
    }

    /// <summary>Gives up the objective (the next tick starts over).</summary>
    /// <remarks>C: fail (0x4098C0, brains.c).</remarks>
    public void Fail(short obj) => ResetObjective(obj, ShipObjective.None);

    /// <summary>
    /// COME_HOME: starts cruising (a routing Confed ship heads for the home-base objective, others follow
    /// the flight path from the player's entry), cruises home, and finally flies parallel to the carrier.
    /// </summary>
    /// <remarks>C: coming_home (0x4098D0, brains.c). Without a home-base objective the original reads
    /// <c>aMissionObjectives[-1]</c> (<see cref="ObjectiveRecord"/>: the origin).</remarks>
    public void ComingHome(short obj)
    {
        switch (Ships[obj].Tactic)
        {
            case ShipTactic.None:
                ResetTactic(obj, ShipTactic.Cruise);
                if (Ships[obj].Side == Side.Imperial && Ships[obj].MissionType == ShipMissionType.Rout)
                {
                    short objective = FindObjective(1, -1);
                    Ships[obj].Destination = ObjectiveRecord(objective).Position;
                }
                else
                {
                    GetFirstFollowPoint(obj, ref Ships[obj].Destination);
                }
                break;
            case ShipTactic.Cruise:
                CruiseHome(obj);
                break;
            case ShipTactic.HeadHome:
                if (NoGoal(obj))
                    PointParallel(obj, FindShipIndex(HomeMissionShipIndex));
                break;
            default:
                Fail(obj);
                break;
        }
    }

    /// <summary>
    /// ROUT: stays in formation while its leader routs too; Confed ships go home; Kilrathi climb straight
    /// up (odd slots down) with the afterburner (half of the ticks, or always with enemies within 16000)
    /// and vanish beyond 16000 from the player.
    /// </summary>
    /// <remarks>C: run_away (0x4099C0, brains.c).</remarks>
    public void RunAway(short obj)
    {
        short leader = Ships[obj].WingLeader;
        if (!Unactive(leader) && Ships[leader].MissionType == ShipMissionType.Rout)
        {
            MaintainFormation(obj);
            return;
        }
        if (Ships[obj].Side == Side.Imperial)
        {
            ComingHome(obj);
            return;
        }
        var direction = new FixedVector(0, (obj & 1) != 0 ? -0x100 : 0x100, 0);
        PointShip(obj, 0, direction);
        if (NormalSpeed(obj) && (Random.Below(100) < 50 || AnyEnemy(obj, 16000)))
            FireAfterburner(obj, 40);
        else
            ApproachFullSpeed(obj);
        if (DistanceFromObject(obj, 0) > 16000)
            RemoveObject(obj);
    }

    /// <summary>Target for a fight: whoever sits on our tail, else the current target while it is a valid
    /// enemy, else a new one (<see cref="SelectTarget"/>).</summary>
    /// <remarks>C: check_engage_target (0x409AC0, brains.c).</remarks>
    public short CheckEngageTarget(short obj)
    {
        short newTarget = DetectEnemyTail(obj);
        if (newTarget != -1 && newTarget != Ships[obj].Target)
            Ships[obj].Target = unchecked((sbyte)newTarget);
        else if (!TargetValid(obj))
            SelectTarget(obj);
        return Ships[obj].Target;
    }

    /// <summary>
    /// Target of a strike: the mission target while the pilot's health exceeds
    /// <c>70 - 15 * min(4, level)</c> (none if it is a friend); otherwise the current fight goes on
    /// (97%) or the mission target is resumed; a target that is gone, still warping in or not in space
    /// leaves the ordinary fight logic.
    /// </summary>
    /// <remarks>C: check_destroy_target (0x409B10, brains.c).</remarks>
    public short CheckDestroyTarget(short obj)
    {
        short destroyTarget = FindShipIndex(Ships[obj].MissionShip);
        if (destroyTarget == -1)
        {
            Ships[obj].Target = unchecked((sbyte)CheckEngageTarget(obj));
        }
        else if (Objects[destroyTarget].Class == ObjectClass.Futurion || GoneShip(Ships[obj].MissionShip))
        {
            CheckEngageTarget(obj);
        }
        else
        {
            int determination = 70 - ScalarMath.MaxShort(0, ScalarMath.MinShort(4, (short)Ships[obj].PilotLevel)) * 15;
            if (EvaluateDamage(obj) > determination)
            {
                Ships[obj].Target = unchecked((sbyte)destroyTarget);
                if (Ships[destroyTarget].Side == Ships[obj].Side)
                    Ships[obj].Target = -1;
            }
            else if (TargetValid(obj) && Random.Below(100) > 3)
            {
                CheckEngageTarget(obj);
            }
            else
            {
                Ships[obj].Target = unchecked((sbyte)destroyTarget);
            }
        }
        return Ships[obj].Target;
    }

    /// <summary>The dogfight tick against <paramref name="newTarget"/>: perception and maneuver choice,
    /// then the maneuver.</summary>
    /// <remarks>C: maneuvering (0x409C20, brains.c).</remarks>
    public void Maneuvering(short obj, short newTarget)
    {
        Ships[obj].Target = unchecked((sbyte)newTarget);
        IntelligenceEvents(obj);
        PerformManeuver(obj);
    }

    /// <summary>BREAK_FORMATION of a squad: full speed for nine ticks, then engage (strike ships go for
    /// the mission target). The original points the ship with its destination position used as a
    /// direction.</summary>
    /// <remarks>C: formation_burst (0x409C50, brains.c): <c>point_ship(obj, 0, &amp;aShipDestination[obj])</c>.</remarks>
    public void FormationBurst(short obj)
    {
        ApproachFullSpeed(obj);
        if (NoGoal(obj))
        {
            var destination = Ships[obj].Destination;
            PointShip(obj, 0, destination);
        }
        Ships[obj].Count++;
        if (Ships[obj].Count > 9)
        {
            Engage(obj, Ships[obj].Target,
                Ships[obj].MissionType == ShipMissionType.Strike ? ShipObjective.DestroyShip : ShipObjective.EngageEnemy);
        }
    }

    /// <summary>
    /// HOLD_FORMATION of a Confed wingman: keep the slot; when an enemy attacks the leader within 12000
    /// the player's wingman asks to engage (line 3, then waits 40 ticks for the permission, see
    /// <see cref="Try2AllowEngage"/>) and engages once allowed; otherwise it reports the first enemy of a
    /// wave within 16000 (line 2) when the message line is free in the cockpit view; beyond 9000 from the
    /// leader it catches up (afterburner when lined up and slow).
    /// </summary>
    /// <remarks>C: imperial_formation (0x409D60, brains.c).</remarks>
    public void ImperialFormation(short obj)
    {
        short leader = Ships[obj].WingLeader;
        if (leader == -1)
            leader = obj;
        MaintainFormation(obj);
        if (AttackerInRange(leader, 12000))
        {
            if (obj == YourWingman || YourWingman == -1)
            {
                if (AutoEngageTimer < -1)
                    AutoEngageTimer++;
                else if (AutoEngageTimer != -1 && --AutoEngageTimer == 0)
                    Try2AllowEngage(Ships[obj].PilotLevel);
            }
            if (EngageAllowed)
            {
                Engage(obj, TargetShip, ShipObjective.EngageEnemy);
            }
            else if (obj == YourWingman && AutoEngageTimer == -1)
            {
                SendMessage(obj, 3);
                AutoEngageTimer = 40;
            }
        }
        else if (obj == YourWingman && EnemySighting != CurrentWave && AnyEnemy(obj, 16000) &&
                 !Cockpit.MessageShowing() && CameraViewMode == 0)
        {
            SendMessage(obj, 2);
            EnemySighting = CurrentWave;
        }

        if (Ships[obj].SpecialManeuver == SpecialManeuver.None && DistanceFromObject(obj, leader) > 9000)
        {
            if (FacingToObject(obj, Objects[leader].Position) > 85 && RealVelocity(obj) < 110)
            {
                FireAfterburner(obj, 10);
                return;
            }
            PointShipAtObject(obj, leader);
            ApproachShipSpeed(obj, leader);
        }
    }

    /// <summary>The scripted peel-off of a Confed wingman (yaw -30, roll -45, pitch -20), then engage.</summary>
    /// <remarks>C: formation_break (0x409F00, brains.c).</remarks>
    public void FormationBreak(short obj)
    {
        switch (Ships[obj].Sequence)
        {
            case 0:
                SteadyObject(obj);
                Ships[obj].YawGoal = -30;
                Ships[obj].RollGoal = -45;
                Ships[obj].PitchGoal = -20;
                Ships[obj].Sequence++;
                break;
            case 1:
                if (NoGoal(obj))
                    Engage(obj, Ships[obj].Target, ShipObjective.EngageEnemy);
                break;
            default:
                Ships[obj].Sequence = 0;
                break;
        }
    }

    /// <remarks>C: imperial_wingman (0x409F80, brains.c).</remarks>
    public void ImperialWingman(short obj)
    {
        switch (Ships[obj].Objective)
        {
            case ShipObjective.DestroyShip:
                Maneuvering(obj, CheckDestroyTarget(obj));
                break;
            case ShipObjective.EngageEnemy:
                Maneuvering(obj, CheckEngageTarget(obj));
                break;
            case ShipObjective.HoldFormation:
                ImperialFormation(obj);
                break;
            case ShipObjective.BreakFormation:
                FormationBreak(obj);
                break;
            case ShipObjective.None:
                ResetObjective(obj, ShipObjective.HoldFormation);
                break;
            default:
                Fail(obj);
                break;
        }
    }

    /// <summary>A Kilrathi wingman: without a leader it patrols, a dead leader is replaced
    /// (<see cref="InheritLeader"/>), the leader's attack objective is copied; otherwise it fights, keeps
    /// formation or bursts out of it.</summary>
    /// <remarks>C: kilrathi_wingman (0x40A030, brains.c).</remarks>
    public void KilrathiWingman(short obj)
    {
        short leader = Ships[obj].WingLeader;
        if (leader == -1)
        {
            ChangeMissionType(obj, ShipMissionType.Patrol);
            return;
        }
        if (Unactive(leader))
        {
            InheritLeader(obj);
            return;
        }
        var objective = Ships[leader].Objective;
        if ((objective == ShipObjective.EngageEnemy || objective == ShipObjective.DestroyShip) &&
            Ships[obj].Objective != objective)
        {
            Engage(obj, Ships[obj].Target, objective);
        }

        switch (Ships[obj].Objective)
        {
            case ShipObjective.DestroyShip:
            case ShipObjective.EngageEnemy:
                Maneuvering(obj, CheckEngageTarget(obj));
                break;
            case ShipObjective.HoldFormation:
                MaintainFormation(obj);
                break;
            case ShipObjective.BreakFormation:
                FormationBurst(obj);
                break;
            case ShipObjective.None:
                ResetObjective(obj, ShipObjective.HoldFormation);
                break;
            default:
                Fail(obj);
                break;
        }
    }

    /// <remarks>C: wingman_mission (0x40A130, brains.c).</remarks>
    public void WingmanMission(short obj)
    {
        if (Ships[obj].Side == Side.Imperial)
            ImperialWingman(obj);
        else
            KilrathiWingman(obj);
    }

    /// <remarks>C: dist_from_home (0x40A160, brains.c).</remarks>
    public short DistFromHome(short obj) => DistanceFromPoint(obj, Ships[obj].MissionSpot);

    /// <summary>Targets the nearest enemy within 14000 (the range argument is ignored, as in the
    /// original) and switches to <paramref name="newTactic"/> when there is one.</summary>
    /// <remarks>C: scan_and_lock (0x40A180, brains.c).</remarks>
    public bool ScanAndLock(short obj, int scanRange, ShipTactic newTactic)
    {
        _ = scanRange;
        Ships[obj].Target = unchecked((sbyte)ScanForEnemy(obj, 14000));
        if (Ships[obj].Target != -1)
            Ships[obj].Tactic = newTactic;
        return Ships[obj].Target != -1;
    }

    /// <summary>
    /// WANDER of a patrol: HEAD_HOME cruises back to the mission spot (LOOK_OUT within 3000), LOOK_OUT
    /// cruises around (back home beyond 8000), APPROACH_TARGET closes in at full speed and bursts the
    /// squad out of formation within 10000; both scan for enemies within 14000.
    /// </summary>
    /// <remarks>C: patrol_area (0x40A1C0, brains.c).</remarks>
    public void PatrolArea(short obj)
    {
        short target = Ships[obj].Target;
        switch (Ships[obj].Tactic)
        {
            case ShipTactic.HeadHome:
                ApproachCruiseSpeed(obj);
                if (!ScanAndLock(obj, 14000, ShipTactic.ApproachTarget))
                {
                    ShipVsPoint(obj, Ships[obj].MissionSpot);
                    if (TargetRange < 3000)
                    {
                        ResetTactic(obj, ShipTactic.LookOut);
                        return;
                    }
                    PointShipAtPoint(obj, Ships[obj].MissionSpot);
                    TrimGoals(obj, 7);
                }
                break;
            case ShipTactic.LookOut:
                ApproachCruiseSpeed(obj);
                if (!ScanAndLock(obj, 14000, ShipTactic.ApproachTarget) && DistFromHome(obj) > 8000)
                    ResetTactic(obj, ShipTactic.HeadHome);
                break;
            case ShipTactic.ApproachTarget:
                ApproachFullSpeed(obj);
                if (Unactive(target))
                {
                    if (!ScanAndLock(obj, 14000, ShipTactic.ApproachTarget))
                        AlterTactic(obj, ShipTactic.LookOut);
                }
                else
                {
                    ShipVsShip(obj, target);
                    if (TargetRange < 10000)
                    {
                        InitFormationBurst(obj);
                        return;
                    }
                    if (NoGoal(obj))
                        PointShipAtObject(obj, target);
                }
                break;
            case ShipTactic.None:
                ResetTactic(obj, ShipTactic.ApproachTarget);
                break;
            default:
                Fail(obj);
                break;
        }
    }

    /// <summary>PATROL: wander (or hold formation) through <see cref="PatrolArea"/>, fight, burst out of
    /// formation; a new patrol starts wandering toward a target.</summary>
    /// <remarks>C: kilrathi_patrol (0x40A360, brains.c).</remarks>
    public void KilrathiPatrol(short obj)
    {
        switch (Ships[obj].Objective)
        {
            case ShipObjective.Wander:
            case ShipObjective.HoldFormation:
                PatrolArea(obj);
                break;
            case ShipObjective.EngageEnemy:
                Maneuvering(obj, CheckEngageTarget(obj));
                break;
            case ShipObjective.BreakFormation:
                FormationBurst(obj);
                break;
            case ShipObjective.None:
                Ships[obj].Objective = ShipObjective.Wander;
                Ships[obj].Tactic = ShipTactic.ApproachTarget;
                break;
            default:
                Fail(obj);
                break;
        }
    }

    /// <summary>The Confed patrol leader: identical to <see cref="KilrathiPatrol"/>.</summary>
    /// <remarks>C: imperial_wingleader (0x40A400, brains.c). <c>ship_intelligence</c> never reaches it (it
    /// tests the address of the side array), which makes no difference.</remarks>
    public void ImperialWingleader(short obj) => KilrathiPatrol(obj);

    /// <summary>
    /// The CRUISE tactic of a ship heading for its jump point: every 8th tick (phase 6) it scans 15000
    /// for enemies (full speed unless the enemy is ahead, then half), at phase 2 it re-aims and checks
    /// the waypoint (within 1500: a Confed ship flags the flight-path objective reached; at the jump
    /// point it stops, else the next waypoint follows).
    /// </summary>
    /// <remarks>C: cruise_to_destination (0x40A410, brains.c). A path index of -1 (no waypoint was ever
    /// found) skips the reached flag (the original reads before <c>abFlightPath</c>).</remarks>
    public void CruiseToDestination(short obj)
    {
        if (Abandoned(obj, 0))
            return;
        if ((Ships[obj].Turn & 7) == 6)
            Ships[obj].Target = unchecked((sbyte)ScanForEnemy(obj, 15000));

        if (Ships[obj].Target == -1)
        {
            ApproachCruiseSpeed(obj);
        }
        else
        {
            GetFacingRangeFromObject(obj, Ships[obj].Target);
            if (FacingToTarget <= 65)
                ApproachFullSpeed(obj);
            else
                ApproachHalfSpeed(obj);
        }

        if ((Ships[obj].Turn & 7) != 2)
            return;
        if (NoGoal(obj))
            PointShipAtPoint(obj, Ships[obj].Destination);
        short range = DistanceFromPoint(obj, Ships[obj].Destination);
        if (range < 1500)
        {
            if (Ships[obj].Side == Side.Imperial && (uint)Ships[obj].NavPointIndex < (uint)FlightPath.Length)
                FlagReached(FlightPath[Ships[obj].NavPointIndex], true);
            if (VectorMath.AreEqual(Ships[obj].Destination, Ships[obj].MissionSpot))
            {
                ResetTactic(obj, ShipTactic.SitStill);
                SetSpecial(obj, SpecialManeuver.KillEngines);
            }
            else
            {
                GetFollowPoint(obj, ref Ships[obj].Destination);
            }
        }
    }

    /// <summary>
    /// SIT_STILL at the jump point: stop the drift, wait 25 ticks (Kilrathi 250), turn away while the
    /// player is ahead, then after 45 ticks (Kilrathi 270) or when the player approaches from behind
    /// within 6000, light the afterburner for the jump (WARP_OUT).
    /// </summary>
    /// <remarks>C: prepare_for_jump (0x40A540, brains.c).</remarks>
    public void PrepareForJump(short obj)
    {
        if (Objects[obj].Speed != 0)
        {
            SetSpecial(obj, SpecialManeuver.StopDrift);
            return;
        }
        short count = ++Ships[obj].Count;
        short delay = Ships[obj].Side == Side.Kilrathi ? (short)250 : (short)25;
        if (count <= delay)
            return;
        GetFacingRangeFromObject(obj, 0);
        if (FacingToTarget > 90 && NoGoal(obj))
        {
            Ships[obj].YawGoal = Random.Signed(30);
            return;
        }
        delay = Ships[obj].Side == Side.Kilrathi ? (short)270 : (short)45;
        if (Ships[obj].Count > delay || (TargetFacing > 80 && TargetRange < 6000))
        {
            ResetTactic(obj, ShipTactic.WarpOut);
            FireAfterburner(obj, 10);
        }
    }

    /// <summary>Full speed; on the fifth tick the ship jumps (<see cref="Warp"/>).</summary>
    /// <remarks>C: accelerate_and_jump (0x40A630, brains.c).</remarks>
    public void AccelerateAndJump(short obj)
    {
        ApproachFullSpeed(obj);
        if (Ships[obj].Count++ == 4)
            Warp(obj);
    }

    /// <summary>GOTO_WARP: cruise along the waypoints to the jump point, sit still, jump.</summary>
    /// <remarks>C: reach_warp (0x40A670, brains.c).</remarks>
    public void ReachWarp(short obj)
    {
        switch (Ships[obj].Tactic)
        {
            case ShipTactic.Cruise:
                CruiseToDestination(obj);
                break;
            case ShipTactic.SitStill:
                PrepareForJump(obj);
                break;
            case ShipTactic.WarpOut:
                AccelerateAndJump(obj);
                break;
            case ShipTactic.None:
                ResetTactic(obj, ShipTactic.Cruise);
                GetFirstFollowPoint(obj, ref Ships[obj].Destination);
                break;
            default:
                Fail(obj);
                break;
        }
    }

    /// <summary>WARP_ARRIVE: arrive (<see cref="ArriveFromWarp"/>) once the tactic is WARP_IN.</summary>
    /// <remarks>C: warp_arrival (0x40A710, brains.c).</remarks>
    public void WarpArrival(short obj)
    {
        if (Ships[obj].Tactic == ShipTactic.WarpIn)
            ArriveFromWarp(obj);
        else
            ResetTactic(obj, ShipTactic.WarpIn);
    }

    /// <summary>Back to the escorted ship at cruise speed; within 1000 it wanders alongside.</summary>
    /// <remarks>C: return_to_buddy (0x40A740, brains.c).</remarks>
    public void ReturnToBuddy(short obj, short buddy)
    {
        ApproachCruiseSpeed(obj);
        if (NoGoal(obj))
            PointShipAtObject(obj, buddy);
        if (DistanceFromObject(obj, buddy) < 1000)
        {
            ResetObjective(obj, ShipObjective.Wander);
            PointParallel(obj, buddy);
        }
    }

    /// <summary>Flies alongside the escorted ship at its speed.</summary>
    /// <remarks>C: escort_buddy (0x40A7A0, brains.c).</remarks>
    public void EscortBuddy(short obj, short buddy)
    {
        ApproachShipSpeed(obj, buddy);
        if (NoGoal(obj))
            PointParallel(obj, buddy);
    }

    /// <summary>
    /// ESCORT: without the escorted ship it patrols; every 4th tick an attacker within 3000 of the
    /// escorted ship is engaged; every 8th tick (phase 4) a ship more than 5000 away returns (HOME_BASE).
    /// </summary>
    /// <remarks>C: escort_mission (0x40A7D0, brains.c).</remarks>
    public void EscortMission(short obj)
    {
        short buddy = FindShipIndex(Ships[obj].MissionShip);
        if (Unactive(buddy))
        {
            ChangeMissionType(obj, ShipMissionType.Patrol);
            return;
        }
        if ((Ships[obj].Turn & 3) == 0 && InDanger(buddy) && TargetRange < 3000)
            Engage(obj, TargetShip, ShipObjective.EngageEnemy);
        if (Ships[obj].Objective != ShipObjective.HomeBase && (Ships[obj].Turn & 7) == 4 &&
            DistanceFromObject(obj, buddy) > 5000)
        {
            ResetObjective(obj, ShipObjective.HomeBase);
        }

        switch (Ships[obj].Objective)
        {
            case ShipObjective.HomeBase:
                ReturnToBuddy(obj, buddy);
                break;
            case ShipObjective.Wander:
                EscortBuddy(obj, buddy);
                break;
            case ShipObjective.EngageEnemy:
                Maneuvering(obj, CheckEngageTarget(obj));
                break;
            case ShipObjective.None:
                ResetObjective(obj, ShipObjective.Wander);
                break;
            default:
                Fail(obj);
                break;
        }
    }

    /// <summary>The strike target is gone: rout when it was destroyed or left, else patrol.</summary>
    /// <remarks>C: check_goal (0x40A900, brains.c).</remarks>
    public void CheckGoal(short obj) =>
        ResetMissionType(obj, GoneShip(Ships[obj].MissionShip) ? ShipMissionType.Rout : ShipMissionType.Patrol);

    /// <summary>Races toward <paramref name="goal"/> (95% aimed, else a veer of 20), with the afterburner
    /// beyond 2000.</summary>
    /// <remarks>C: streak_toward (0x40A940, brains.c).</remarks>
    public void StreakToward(short obj, short goal, short range)
    {
        if (NoGoal(obj))
        {
            if (Random.Below(100) < 95)
                PointShipAtObject(obj, goal);
            else
                VeerRandom(obj, 20);
        }
        if (range > 2000 && NormalSpeed(obj))
            FireAfterburner(obj, 10);
        else
            ApproachFullSpeed(obj);
    }

    /// <summary>
    /// The approach of a strike: a healthy pilot streaks toward a target farther than 5000; enemy fighters
    /// within 10000 that are much nearer than the goal (or a goal still warping in) make the squad break
    /// formation against them; within 5000 the goal is attacked (DESTROY_SHIP).
    /// </summary>
    /// <remarks>C: approach_and_engage (0x40A9B0, brains.c); the ranges are compared unsigned.</remarks>
    public void ApproachAndEngage(short obj, short goal)
    {
        ushort range = unchecked((ushort)DistanceFromObject(obj, goal));
        if (Objects[goal].Class != ObjectClass.Futurion)
        {
            int determination = 70 - ScalarMath.MaxShort(0, ScalarMath.MinShort(4, (short)Ships[obj].PilotLevel)) * 15;
            if (EvaluateDamage(obj) > determination && range > 5000)
            {
                StreakToward(obj, goal, unchecked((short)range));
                return;
            }
        }
        short possibleTarget = ScanForEnemy(obj, 10000);
        ushort possibleRange = unchecked((ushort)TargetRange);
        if (possibleTarget != -1 && (possibleRange * 3 < range || Objects[goal].Class == ObjectClass.Futurion))
        {
            InitFormationBurst(obj);
            Ships[obj].Target = unchecked((sbyte)possibleTarget);
        }
        else if (range < 5000)
        {
            Engage(obj, goal, ShipObjective.DestroyShip);
        }
        else
        {
            StreakToward(obj, goal, unchecked((short)range));
        }
    }

    /// <summary>STRIKE: approach (HOME_BASE / HOLD_FORMATION), destroy the target or fight, burst out of
    /// formation; a missing target ends the strike (<see cref="CheckGoal"/>).</summary>
    /// <remarks>C: strike_mission (0x40AAC0, brains.c) with the SDL port's guard. The Win32 build also
    /// tested <c>aeObjectClass[-1] != FUTURION</c>; in the Kilrathi Saga image that reads the previous
    /// distances of slots 62 and 63, which are never written (class NULL), so the result is the same.</remarks>
    public void StrikeMission(short obj)
    {
        short goal = FindShipIndex(Ships[obj].MissionShip);
        if (goal == -1)
            CheckGoal(obj);
        switch (Ships[obj].Objective)
        {
            case ShipObjective.HomeBase:
            case ShipObjective.HoldFormation:
                ApproachAndEngage(obj, goal);
                break;
            case ShipObjective.DestroyShip:
            case ShipObjective.EngageEnemy:
                Maneuvering(obj, CheckDestroyTarget(obj));
                break;
            case ShipObjective.BreakFormation:
                FormationBurst(obj);
                break;
            case ShipObjective.None:
                ResetObjective(obj, ShipObjective.HomeBase);
                break;
            default:
                Fail(obj);
                break;
        }
    }

    /// <summary>Back to the defended ship; within 5000 it wanders around it.</summary>
    /// <remarks>C: return_to_master (0x40ABB0, brains.c).</remarks>
    public void ReturnToMaster(short obj, short master)
    {
        short range = DistanceFromObject(obj, master);
        StreakToward(obj, master, range);
        if (range < 5000)
        {
            ResetObjective(obj, ShipObjective.Wander);
            PointPerpendicular(obj, master);
        }
    }

    /// <summary>
    /// DEFEND: every 10th tick an attacker of the defended ship within 6000 is engaged; every 8th tick
    /// (phase 4) a ship more than 10000 away returns; wandering ships engage enemies within 7000 or
    /// circle the defended ship at half speed.
    /// </summary>
    /// <remarks>C: defend_mission (0x40AC00, brains.c). The tick counter is a signed byte
    /// (<c>% 10</c> of a negative count is negative).</remarks>
    public void DefendMission(short obj)
    {
        short master = FindShipIndex(Ships[obj].MissionShip);
        if (master == -1)
        {
            ChangeMissionType(obj, ShipMissionType.Patrol);
            return;
        }
        if (Ships[obj].Turn % 10 == 0 && InDanger(master) && TargetRange < 6000 &&
            Ships[obj].Objective != ShipObjective.EngageEnemy)
        {
            Engage(obj, TargetShip, ShipObjective.EngageEnemy);
        }
        if (Ships[obj].Objective != ShipObjective.HomeBase && (Ships[obj].Turn & 7) == 4 &&
            DistanceFromObject(obj, master) > 10000)
        {
            ResetObjective(obj, ShipObjective.HomeBase);
        }

        switch (Ships[obj].Objective)
        {
            case ShipObjective.HomeBase:
                ReturnToMaster(obj, master);
                break;
            case ShipObjective.Wander:
                short target = ScanForEnemy(obj, 7000);
                Ships[obj].Target = unchecked((sbyte)target);
                if (target != -1)
                {
                    Engage(obj, target, ShipObjective.EngageEnemy);
                }
                else
                {
                    ApproachHalfSpeed(obj);
                    if (NoGoal(obj))
                        PointPerpendicular(obj, master);
                }
                break;
            case ShipObjective.EngageEnemy:
                Maneuvering(obj, CheckEngageTarget(obj));
                break;
            case ShipObjective.None:
                ResetObjective(obj, ShipObjective.Wander);
                break;
            default:
                Fail(obj);
                break;
        }
    }

    /// <summary>RENDEZVOUS: fly to the goal ship (full speed while it is attacked within 9000), engage
    /// attackers within 3500, and switch to DEFEND within 2500.</summary>
    /// <remarks>C: rendezvous_mission (0x40AD80, brains.c).</remarks>
    public void RendezvousMission(short obj)
    {
        short goal = FindShipIndex(Ships[obj].MissionShip);
        if (Unactive(goal))
        {
            ChangeMissionType(obj, ShipMissionType.Patrol);
            return;
        }
        switch (Ships[obj].Objective)
        {
            case ShipObjective.ReachShip:
                if (AttackerInRange(obj, 3500))
                    Engage(obj, TargetShip, ShipObjective.EngageEnemy);
                if (DistanceFromObject(obj, goal) < 2500)
                {
                    ResetMissionType(obj, ShipMissionType.Defend);
                    return;
                }
                if (AttackerInRange(goal, 9000))
                    ApproachFullSpeed(obj);
                else
                    ApproachCruiseSpeed(obj);
                if (NoGoal(obj))
                    PointShipAtObject(obj, goal);
                break;
            case ShipObjective.EngageEnemy:
                Maneuvering(obj, CheckEngageTarget(obj));
                break;
            default:
                ResetObjective(obj, ShipObjective.ReachShip);
                break;
        }
    }

    /// <summary>
    /// The fighter AI, every frame for each ship: <see cref="RegulateTurn"/> decides whether this is an
    /// AI tick (collision avoidance first), then the mission handler runs; the hit cooldown decays.
    /// </summary>
    /// <remarks>C: ship_intelligence (0x40AE80, brains.c). The PATROL case tests the address of the side
    /// array, so it always takes <c>kilrathi_patrol</c> (identical to <c>imperial_wingleader</c>).
    /// CANNED_SEQUENCE (only reached outside canned mode 2) and unknown missions fail the objective.</remarks>
    public void ShipIntelligence(short obj)
    {
        if (RegulateTurn(obj))
            return;
        switch (Ships[obj].MissionType)
        {
            case ShipMissionType.Patrol:
                KilrathiPatrol(obj);
                break;
            case ShipMissionType.Escort:
                EscortMission(obj);
                break;
            case ShipMissionType.Strike:
                StrikeMission(obj);
                break;
            case ShipMissionType.Defend:
                DefendMission(obj);
                break;
            case ShipMissionType.Wingman:
                WingmanMission(obj);
                break;
            case ShipMissionType.Rout:
                RunAway(obj);
                break;
            case ShipMissionType.GotoWarp:
                ReachWarp(obj);
                break;
            case ShipMissionType.WarpArrive:
                WarpArrival(obj);
                break;
            case ShipMissionType.Rendezvous:
                RendezvousMission(obj);
                break;
            case ShipMissionType.ComeHome:
                ComingHome(obj);
                break;
            case ShipMissionType.None:
                InheritLeaderMission(obj);
                break;
            default:
                Fail(obj);
                break;
        }
        if (Ships[obj].AiCooldown > 0)
            Ships[obj].AiCooldown--;
    }

    /// <summary>Circles the current nav point at half its radius (heads in when outside, flies
    /// perpendicular near the rim), turns trimmed to 10.</summary>
    /// <remarks>C: orbit_sphere (0x40AF70, brains.c).</remarks>
    public void OrbitSphere(short obj)
    {
        short radius = (short)(MissionNavPoints[CurrentNavPoint].ProximityRadius >> 1);
        var center = MissionNavPoints[CurrentNavPoint].Position;
        short range = DistanceFromPoint(obj, center);
        if (NoGoal(obj) && range > radius - 750)
        {
            if (range > radius)
                PointShipAtPoint(obj, center);
            else
                PointPerpendicularToPoint(obj, center);
        }
        TrimGoals(obj, 10);
    }

    /// <summary>Tankers (Dorkir, Lumbari): attacked within 3000 they run at full speed, fire, and swerve
    /// (1/5: random yaw and roll ±90) or turn their tail to the attacker; otherwise they orbit at cruise speed.</summary>
    /// <remarks>C: tanker_intelligence (0x40B010, brains.c).</remarks>
    public void TankerIntelligence(short obj)
    {
        if (AttackerInRange(obj, 3000))
        {
            ApproachFullSpeed(obj);
            Ships[obj].Target = unchecked((sbyte)TargetShip);
            Fire(obj, TargetShip);
            if (NoGoal(obj))
            {
                if (Random.BelowOrEqual(4) == 0)
                {
                    Ships[obj].YawGoal = Random.Signed(90);
                    Ships[obj].RollGoal = Random.Signed(90);
                }
                else
                {
                    PointCapitalShipAtObject(obj, TargetShip);
                }
            }
            return;
        }
        ApproachCruiseSpeed(obj);
        OrbitSphere(obj);
    }

    /// <summary>Warships fire their turrets (half speed while firing, target cleared) and orbit the nav point.</summary>
    /// <remarks>C: destroyer_intelligence (0x40B0C0, brains.c).</remarks>
    public void DestroyerIntelligence(short obj)
    {
        if (FireTurrets(obj))
        {
            Ships[obj].Target = -1;
            ApproachHalfSpeed(obj);
        }
        else
        {
            ApproachCruiseSpeed(obj);
        }
        OrbitSphere(obj);
    }

    /// <summary>A capital ship without a mission: the Kilrathi starbase turns (yaw 4 per frame) and
    /// fires its turrets; others do nothing.</summary>
    /// <remarks>C: stationary_intelligence (0x40B110, brains.c).</remarks>
    public void StationaryIntelligence(short obj)
    {
        if (Objects[obj].Type == ObjectType.KilrathiBase)
        {
            Objects[obj].YawRotation = 4;
            FireTurrets(obj);
        }
    }

    /// <summary>
    /// The capital ship AI: rout, jump, arrive and come home like fighters; without a mission it is
    /// stationary; tankers and warships have their own brains; other capital ships (the Confed carrier
    /// and transports) defend themselves against the nearest enemy within 15000 with their turrets.
    /// </summary>
    /// <remarks>C: capital_ship_intelligence (0x40B140, brains.c); capital ships skip the collision
    /// avoidance of <see cref="RegulateTurn"/> and have no hit cooldown.</remarks>
    public void CapitalShipIntelligence(short obj)
    {
        if (RegulateTurn(obj))
            return;
        switch (Ships[obj].MissionType)
        {
            case ShipMissionType.Rout:
                RunAway(obj);
                return;
            case ShipMissionType.GotoWarp:
                ReachWarp(obj);
                return;
            case ShipMissionType.WarpArrive:
                WarpArrival(obj);
                return;
            case ShipMissionType.ComeHome:
                ComingHome(obj);
                return;
            case ShipMissionType.None:
                StationaryIntelligence(obj);
                return;
        }

        var type = Objects[obj].Type;
        if (type is ObjectType.Dorkir or ObjectType.Lumbari)
        {
            TankerIntelligence(obj);
            return;
        }
        if (type is ObjectType.Spikeri or ObjectType.Ralari or ObjectType.Fralthi or ObjectType.Snakeir or
            ObjectType.Sivar or ObjectType.KilrathiBase)
        {
            DestroyerIntelligence(obj);
            return;
        }

        TargetShip = Ships[obj].Target;
        if (Unactive(TargetShip))
            ScanForEnemy(obj, 15000);
        if (Ships[obj].Tactic != ShipTactic.SelfDefense)
        {
            if (TargetShip != -1)
            {
                ApproachFullSpeed(obj);
                Ships[obj].Tactic = ShipTactic.SelfDefense;
                Ships[obj].Target = unchecked((sbyte)TargetShip);
                FireTurrets(obj);
            }
            else
            {
                ApproachCruiseSpeed(obj);
            }
            return;
        }

        ApproachFullSpeed(obj);
        if (Unactive(Ships[obj].Target))
        {
            SelectTarget(obj);
            if (Unactive(Ships[obj].Target))
                ResetTactic(obj, ShipTactic.None);
        }
        else
        {
            FireTurrets(obj);
        }
    }
}
