using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// AI helpers of logic.c: collision prediction, target scans, tails, squads, follow points,
// engagement and damage evaluation. Several of them report through the scratch globals
// (TargetShip, TargetRange, FacingToTarget, ...) exactly like the original.
public sealed partial class SpaceSimulation
{
    /// <summary>A Kilrathi ace greets the player on its first engagement: line 1 when the ace met
    /// the player before (ace flag 4), else line 0; then marks it greeted (flag 8).</summary>
    /// <remarks>C: ace_greeting (0x422090, logic.c).</remarks>
    public void AceGreeting(short obj)
    {
        short ace = unchecked((short)(Ships[obj].PilotLevel - 14));
        SendMessage(obj, AceStatus(ace, 4) ? (sbyte)1 : (sbyte)0);
        FlagAce(ace, 8);
    }

    /// <summary>Starts (or refreshes) the collision alert against <paramref name="other"/>: three AI
    /// ticks of avoidance, afterburner and special maneuver cancelled; a new partner steadies the ship.</summary>
    /// <remarks>C: start_collision_alert (0x422180, logic.c).</remarks>
    public void StartCollisionAlert(short obj, short other)
    {
        if ((sbyte)Ships[obj].CollisionAlertTarget != other)
        {
            Ships[obj].CollisionAlertTarget = unchecked((byte)other);
            SteadyObject(obj);
        }
        Ships[obj].CollisionCountdown = 3;
        SetAlert(obj, 1);
        Ships[obj].AfterburnerTimer = 0;
        SetSpecial(obj, SpecialManeuver.None);
    }

    /// <summary>Counts the collision alert down; at zero it is cleared, else flagged as ending (bit 2).</summary>
    /// <remarks>C: try2end_collision_alert (0x4221E0, logic.c).</remarks>
    public void Try2EndCollisionAlert(short obj)
    {
        Ships[obj].CollisionCountdown--;
        if (Ships[obj].CollisionCountdown <= 0)
            ClearAlert(obj);
        else
            SetAlert(obj, 2);
    }

    /// <summary>
    /// Frames until <paramref name="obj"/> and <paramref name="other"/> touch (radii + 30): 0x7fff when
    /// they are more than 1500 units apart, 0x7fbc without relative motion, the plain
    /// distance / closing-speed estimate when it is 30 or more, 32000 when they pass each other,
    /// else the frame found by halving the remaining time.
    /// </summary>
    /// <remarks>C: real_crash_time (0x422260, logic.c). The estimate ignores the direction of the
    /// relative motion; the branch that returns 25 can never be taken (it needs a separation below
    /// radius / 8 that is also above the radius).</remarks>
    public short RealCrashTime(short obj, short other)
    {
        short collisionRadius = unchecked((short)(Objects[obj].CollisionRadius + Objects[other].CollisionRadius + 30));
        bool collisionFound = false;
        var relativePosition = VectorMath.Delta(Objects[obj].Position, Objects[other].Position);
        int distance = relativePosition.Magnitude();
        if ((collisionRadius + 1500) * 0x100 < distance)
            return 0x7fff;

        var relativeVelocity = VectorMath.Delta(Objects[obj].Velocity, Objects[other].Velocity);
        int relativeSpeed = relativeVelocity.Magnitude();
        if (relativeSpeed == 0)
            return 0x7fbc;

        short time = FixedMath.ToShortSaturating(FixedMath.Divide(distance, relativeSpeed));
        if (time >= 30)
            return time;

        var separation = VectorMath.Add(relativePosition, VectorMath.Scale(relativeVelocity, time << 8));
        short range = FixedMath.ToShortSaturating(separation.Magnitude());
        if (range > collisionRadius)
        {
            if ((collisionRadius * 2 >> 4) > range)
                return 25;
            return 32000;
        }

        short elapsed = 0;
        do
        {
            if (elapsed >= time)
                break;
            short step = ScalarMath.MaxShort(1, unchecked((short)((time - elapsed) >> 1)));
            elapsed = unchecked((short)(elapsed + step));
            separation = VectorMath.Add(relativePosition, VectorMath.Scale(relativeVelocity, elapsed << 8));
            range = FixedMath.ToShortSaturating(separation.Magnitude());
            if (collisionRadius >= range)
                collisionFound = true;
        }
        while (!collisionFound);
        return elapsed;
    }

    /// <summary>Crash time with the per-frame cache (either ship's last prediction against the
    /// other); asteroids that are off screen (or seen from an off-screen ship) never collide.</summary>
    /// <remarks>C: crash_time (0x422460, logic.c).</remarks>
    public short CrashTime(short obj, short other)
    {
        if (Ships[obj].CollisionPartner == other)
            return Ships[obj].CollisionTime;
        if (other < ObjectSlots.ShipSlotCount && Ships[other].CollisionPartner == obj)
            return Ships[other].CollisionTime;
        if (Objects[other].Class == ObjectClass.Asteroid &&
            (Objects[other].ScreenX == ObjectSlots.NotVisible || Objects[obj].ScreenX == ObjectSlots.NotVisible))
        {
            return 0x7fff;
        }
        return RealCrashTime(obj, other);
    }

    /// <summary>The asteroid, mine or ship (slots 0..60, missiles excluded) <paramref name="obj"/>
    /// would hit first within 30 frames, cached in the ship's collision partner; -1 for none.</summary>
    /// <remarks>C: detect_collisions (0x4224F0, logic.c).</remarks>
    public short DetectCollisions(short obj)
    {
        short candidate = -1;
        short closestTime = 30;
        for (short other = 0; other <= ObjectSlots.LastMoving; other++)
        {
            if (other != obj && Objects[other].Class >= ObjectClass.Asteroid && Objects[other].Class != ObjectClass.Missile)
            {
                short time = CrashTime(obj, other);
                if (closestTime > time)
                {
                    closestTime = time;
                    candidate = other;
                }
            }
        }
        if (candidate != -1)
        {
            Ships[obj].CollisionPartner = candidate;
            Ships[obj].CollisionTime = closestTime;
        }
        return candidate;
    }

    /// <summary>True for -1, anything that is not a (capital) ship, and a ship in the HARD_BRAKE
    /// maneuver (the original treats a hard-braking ship as out of the fight).</summary>
    /// <remarks>C: unactive (0x422560, logic.c). Ships only live in slots 0..9; a ship class in
    /// another slot (never created by the game) counts as inactive.</remarks>
    public bool Unactive(short ship)
    {
        if (ship == -1 || (uint)ship >= ObjectSlots.Count || Objects[ship].Class < ObjectClass.Ship)
            return true;
        return ship >= ObjectSlots.ShipSlotCount || Ships[ship].Maneuver == ShipManeuver.HardBrake;
    }

    /// <remarks>C: are_alive (0x422590, logic.c).</remarks>
    public bool AreAlive(short obj) => !Unactive(obj) && Ships[obj].Objective != ShipObjective.Wander;

    /// <summary>
    /// A ship that wants to flee: with a friendly capital ship in the sphere (or in the training
    /// simulator) it calms down (stress 0) and drops the maneuver; otherwise it switches to the ROUT
    /// mission (the wingman says line 9). Returns true when it routs.
    /// </summary>
    /// <remarks>C: try2rout (0x422780, logic.c).</remarks>
    public bool Try2Rout(short obj)
    {
        bool canContinue = false;
        if (TrainSimActive)
        {
            canContinue = true;
        }
        else
        {
            for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
            {
                if (Objects[other].Class == ObjectClass.CapitalShip &&
                    Ships[other].SpecialManeuver != SpecialManeuver.Unknown9 &&
                    Ships[obj].Side == Ships[other].Side)
                {
                    canContinue = true;
                }
            }
        }
        if (canContinue)
        {
            Ships[obj].Stress = 0;
            ManeuverComplete(obj);
        }
        else
        {
            ResetMissionType(obj, ShipMissionType.Rout);
            if (obj == YourWingman)
                SendMessage(obj, 9);
        }
        return !canContinue;
    }

    /// <summary>The ship <paramref name="other"/> sits on the tail of <paramref name="obj"/>: it faces
    /// us (above 85), we face away (below -60), closer than 7000.</summary>
    /// <remarks>C: being_tailed (0x422860, logic.c); leaves the facing/range globals of the pair.</remarks>
    public bool BeingTailed(short obj, short other)
    {
        ShipVsShip(obj, other);
        return FacingToTarget < -60 && TargetFacing > 85 && TargetRange < 7000;
    }

    /// <summary>An enemy that targets <paramref name="obj"/> sits on its tail (<see cref="TargetShip"/>,
    /// -1 when none).</summary>
    /// <remarks>C: any_enemy_tail (0x4228A0, logic.c); also used by the music director and
    /// <c>disobey_formation</c>.</remarks>
    public bool AnyEnemyTail(short obj)
    {
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (IsLivingEnemyShip(obj, other) && Ships[other].Target == obj && BeingTailed(obj, other))
            {
                TargetShip = other;
                return true;
            }
        }
        TargetShip = -1;
        return false;
    }

    /// <summary>The first enemy that targets <paramref name="obj"/> and sits on its tail, or -1.</summary>
    /// <remarks>C: detect_enemy_tail (0x422930, logic.c).</remarks>
    public short DetectEnemyTail(short obj)
    {
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (IsLivingEnemyShip(obj, other) && Ships[other].Target == obj && BeingTailed(obj, other))
                return other;
        }
        return -1;
    }

    /// <summary><paramref name="obj"/> sits on the tail of the player's target.</summary>
    /// <remarks>C: is_ship_tailing_player_target (0x4229B0, logic.c); used by the music director.</remarks>
    public bool IsShipTailingPlayerTarget(short obj)
    {
        short target = Ships[ObjectSlots.Player].Target;
        return !Unactive(target) && BeingTailed(target, obj);
    }

    /// <summary>A missile (slots 0..9) is chasing <paramref name="obj"/>.</summary>
    /// <remarks>C: missile_on_tail (0x4229F0, logic.c).</remarks>
    public bool MissileOnTail(short obj)
    {
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (Objects[other].Class == ObjectClass.Missile && Ships[other].Target == obj)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Picks the value of a weighted list of <c>{weight, value}</c> pairs ended by a weight of -1:
    /// a roll of 1..101 minus the weights until it is used up; -1 when the list runs out.
    /// </summary>
    /// <remarks>C: select_weighted_value (0x422A30, logic.c). No caller in the game.</remarks>
    public short SelectWeightedValue(ReadOnlySpan<short> choices)
    {
        int index = 0;
        short roll = unchecked((short)(Random.BelowOrEqual(100) + 1));
        roll -= choices[index];
        while (roll > 0)
        {
            if (choices[index] == -1)
                break;
            index += 2;
            roll -= choices[index];
        }
        if (choices[index] == -1)
            return -1;
        return choices[index + 1];
    }

    /// <summary>Fills <see cref="FormationMemberList"/> with <paramref name="leader"/> and every slot
    /// 0..9 whose wing leader it is (stale entries of empty or reused slots included, as in the original).</summary>
    /// <remarks>C: build_squad_list (0x422A70, logic.c).</remarks>
    public void BuildSquadList(short leader)
    {
        int index = 1;
        FormationMemberList[0] = unchecked((sbyte)leader);
        FormationMemberList[1] = -1;
        for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Ships[obj].WingLeader == leader)
            {
                FormationMemberList[index++] = unchecked((sbyte)obj);
                FormationMemberList[index] = -1;
            }
        }
    }

    /// <summary>Average position of the members in <see cref="FormationMemberList"/>.</summary>
    /// <remarks>C: find_squad_center (0x422AC0, logic.c).</remarks>
    public FixedVector FindSquadCenter()
    {
        int count = 0;
        var center = FixedVector.Zero;
        while (FormationMemberList[count] != -1)
        {
            center = VectorMath.Add(center, Objects[FormationMemberList[count]].Position);
            count++;
        }
        if (count != 0)
            center = VectorMath.Divide(center, count << 8);
        return center;
    }

    /// <summary>
    /// The squad of <paramref name="obj"/> breaks formation: every member flies to a point ten times
    /// its offset from the squad centre (BREAK_FORMATION, steadied); after nine AI ticks each engages.
    /// </summary>
    /// <remarks>C: init_formation_burst (0x422B30, logic.c). Like the original it also resets stale
    /// list entries (an empty slot or a missile reusing a dead wingman's slot).</remarks>
    public void InitFormationBurst(short obj)
    {
        BuildSquadList(obj);
        var center = FindSquadCenter();
        int index = 0;
        short member = FormationMemberList[0];
        while (member != -1)
        {
            var destination = VectorMath.Delta(center, Objects[member].Position);
            destination = VectorMath.Scale(destination, 0xa00);
            Ships[member].Destination = VectorMath.Add(destination, Objects[member].Position);
            SteadyObject(member);
            ResetObjective(member, ShipObjective.BreakFormation);
            member = FormationMemberList[++index];
        }
    }

    /// <summary>The last facing/range query found us behind the target (target facing below -50)
    /// within <paramref name="range"/>.</summary>
    /// <remarks>C: close_behind (0x422F60, logic.c).</remarks>
    public bool CloseBehind(short range) => TargetRange < range && TargetFacing < -0x32;

    /// <summary>
    /// The nearest living enemy ship within <paramref name="range"/> of <paramref name="obj"/>'s
    /// centre (measured from the enemy's hull), or -1; also left in <see cref="TargetShip"/> with the
    /// facing globals and <see cref="TargetRange"/> (hull to hull) set for it.
    /// </summary>
    /// <remarks>C: scan_for_enemy (0x422F80, logic.c).</remarks>
    public short ScanForEnemy(short obj, ushort range)
    {
        short target = -1;
        TargetRange = 0;
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (Objects[other].Class < ObjectClass.Ship || Ships[other].SpecialManeuver == SpecialManeuver.Unknown9)
                continue;
            TargetShip = target;
            if (Ships[obj].Side == Ships[other].Side)
                continue;
            short distance = DistanceFromPoint(other, Objects[obj].Position);
            target = TargetShip;
            if (distance < range && (target == -1 || distance < TargetRange))
            {
                target = other;
                TargetRange = distance;
            }
        }
        if (target != -1)
        {
            TargetShip = target;
            GetFacingRangeFromObject(obj, TargetShip);
            TargetRange = DistanceFromObject(obj, TargetShip);
            target = TargetShip;
        }
        TargetShip = target;
        return target;
    }

    /// <summary>Distance to the nearest living enemy ship (0x7fff when none).</summary>
    /// <remarks>C: nearest_enemy_range (0x4230F0, logic.c); clears <see cref="TargetShip"/>.</remarks>
    public short NearestEnemyRange(short obj)
    {
        TargetShip = -1;
        short range = 0x7fff;
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (IsLivingEnemyShip(obj, other))
                range = ScalarMath.MinShort(range, DistanceFromObject(obj, other));
        }
        return range;
    }

    /// <summary>NPC fire decision against the current target; the player's wingman holds fire while
    /// the player is in front of it (facing above 80), except Maniac (pilot 11).</summary>
    /// <remarks>C: fire_when_ready (0x423210, logic.c); <paramref name="aimed"/> is ignored.</remarks>
    public void FireWhenReady(short obj, bool aimed)
    {
        _ = aimed;
        if (YourWingman == obj && Ships[obj].PilotLevel != 11)
        {
            ShipVsShip(obj, 0);
            if (FacingToTarget > 80)
                return;
        }
        Fire(obj, Ships[obj].Target);
    }

    /// <remarks>C: ships_within_range (0x423260, logic.c): centre distance within <paramref name="range"/>.</remarks>
    public bool ShipsWithinRange(short obj, short other, short range) =>
        VectorMath.IsVectorWithinRange(VectorMath.Delta(Objects[obj].Position, Objects[other].Position), range);

    /// <summary>An enemy that targets <paramref name="obj"/> is within <paramref name="range"/>
    /// (unsigned compare): left in <see cref="TargetShip"/> and <see cref="TargetRange"/>.</summary>
    /// <remarks>C: attacker_in_range (0x4232B0, logic.c).</remarks>
    public bool AttackerInRange(short obj, short range)
    {
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (IsLivingEnemyShip(obj, other) && Ships[other].Target == obj)
            {
                TargetRange = DistanceFromObject(other, obj);
                if ((ushort)TargetRange < (ushort)range)
                {
                    TargetShip = other;
                    return true;
                }
            }
        }
        TargetShip = -1;
        return false;
    }

    /// <summary>The nearest enemy that targets <paramref name="obj"/> (any range): left in
    /// <see cref="TargetShip"/> and <see cref="TargetRange"/>.</summary>
    /// <remarks>C: in_danger (0x423350, logic.c). The distance is compared unsigned against the
    /// signed global, as in the original.</remarks>
    public bool InDanger(short obj)
    {
        short target = -1;
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (!IsLivingEnemyShip(obj, other))
                continue;
            TargetShip = target;
            if (Ships[other].Target != obj)
                continue;
            ushort range = unchecked((ushort)DistanceFromObject(other, obj));
            target = TargetShip;
            if (target == -1 || range < TargetRange)
            {
                target = other;
                TargetRange = unchecked((short)range);
            }
        }
        TargetShip = target;
        return target != -1;
    }

    /// <remarks>C: target_within_range (0x423400, logic.c): the current target is active and within 7000.</remarks>
    public bool TargetWithinRange(short obj)
    {
        short target = Ships[obj].Target;
        if (Unactive(target))
            return false;
        return ShipsWithinRange(obj, target, 7000);
    }

    /// <summary>The first enemy within 7000 that nobody attacks yet, else a random one of the list
    /// (left in <see cref="TargetShip"/>).</summary>
    /// <remarks>C: select_safe_target (0x4234C0, logic.c). No caller in the game.</remarks>
    public bool SelectSafeTarget(short obj)
    {
        BuildTargetList(obj, 7000);
        short target;
        int index = -1;
        do
        {
            index++;
            target = FormationMemberList[index];
            if (target == -1)
                break;
        }
        while (InDanger(target));
        if (target == -1 && index > 0)
        {
            index--;
            target = FormationMemberList[Random.BelowOrEqual(index)];
        }
        TargetShip = target;
        return target != -1;
    }

    /// <summary>A ship without a mission takes over its leader's mission, mission ship and spot and
    /// leaves the formation.</summary>
    /// <remarks>C: inherit_leader_mission (0x423530, logic.c).</remarks>
    public void InheritLeaderMission(short obj)
    {
        short leader = Ships[obj].WingLeader;
        if (leader != -1 && Objects[obj].Class >= ObjectClass.Ship)
        {
            Ships[obj].MissionType = Ships[leader].MissionType;
            Ships[obj].MissionShip = Ships[leader].MissionShip;
            Ships[obj].WingLeader = -1;
            Ships[obj].MissionSpot = Ships[leader].MissionSpot;
        }
    }

    /// <summary>The leader is gone: take over its mission, follow its leader and become the leader of
    /// its wingmen.</summary>
    /// <remarks>C: inherit_leader (0x4235B0, logic.c).</remarks>
    public void InheritLeader(short obj)
    {
        short leader = Ships[obj].WingLeader;
        if (leader == -1 || Objects[obj].Class < ObjectClass.Ship)
            return;
        InheritLeaderMission(obj);
        Ships[obj].WingLeader = Ships[leader].WingLeader;
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (Ships[other].WingLeader == leader)
                Ships[other].WingLeader = obj;
        }
    }

    /// <summary>
    /// Next waypoint of a travelling ship: Kilrathi fly to the nav point in their mission ship
    /// field; Confed ships take the next flight-path entry after their path index that is a nav
    /// point (type 0) or the home base (type 1: the carrier, or its record's index used as a nav
    /// index when it is not in space) and advance the index.
    /// </summary>
    /// <remarks>C: get_follow_point (0x423820, logic.c). The -1 terminator of the flight path is read as
    /// objective -1 like in the original (<see cref="ObjectiveRecord"/>: usually type 0 at the origin).
    /// Guard: a Kilrathi mission ship or a home-base record index outside the 20-entry nav table leaves
    /// the point unchanged (the original reads after the table).</remarks>
    public void GetFollowPoint(short obj, ref FixedVector point)
    {
        if (Ships[obj].Side == Side.Kilrathi)
        {
            short navPoint = Ships[obj].MissionShip;
            if ((uint)navPoint < NavPointTableSize)
                point = MissionNavPoints[navPoint].Position;
            return;
        }
        short pathIndex = Ships[obj].NavPointIndex;
        while (++pathIndex < ObjectiveCount)
        {
            short objective = FlightPath[pathIndex];
            var record = ObjectiveRecord(objective);
            switch (record.Type)
            {
                case 0:
                    point = record.Position;
                    Ships[obj].NavPointIndex = unchecked((sbyte)pathIndex);
                    return;
                case 1:
                    short missionShip = record.Index;
                    short ship = FindShipIndex(missionShip);
                    if (ship != -1)
                        point = Objects[ship].Position;
                    else if ((uint)missionShip < NavPointTableSize)
                        point = MissionNavPoints[missionShip].Position;
                    Ships[obj].NavPointIndex = unchecked((sbyte)pathIndex);
                    return;
            }
        }
    }

    /// <summary>First waypoint: Confed ships restart the flight path at the player's current entry.</summary>
    /// <remarks>C: get_first_follow_point (0x423930, logic.c).</remarks>
    public void GetFirstFollowPoint(short obj, ref FixedVector point)
    {
        if (Ships[obj].Side == Side.Imperial)
            Ships[obj].NavPointIndex = unchecked((sbyte)(CurrentNavPointIndex - 1));
        GetFollowPoint(obj, ref point);
    }

    /// <summary>Nav point <paramref name="navPoint"/> spawns a ship of another side than <paramref name="obj"/>.</summary>
    /// <remarks>C: hostile_sphere (0x423970, logic.c).</remarks>
    public bool HostileSphere(short obj, short navPoint)
    {
        for (int index = 0; index < 10; index++)
        {
            short missionShip = MissionNavPoints[navPoint].MissionShips[index];
            if (missionShip != -1 && Ships[obj].Side != MissionShips[missionShip].Side)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Clean-up of stragglers: every 8th AI tick a non-Kilrathi ship has a 1/9 chance to be blown up
    /// when it is in another nav sphere than the player's, that sphere is hostile, and it is more
    /// than 10000 from <paramref name="other"/>.
    /// </summary>
    /// <remarks>C: abandoned (0x4239D0, logic.c).</remarks>
    public bool Abandoned(short obj, short other)
    {
        if ((Ships[obj].Turn & 7) == 0 && Ships[obj].Side != Side.Kilrathi && Random.BelowOrEqual(8) == 0)
        {
            short navPoint = FindNearestNavPoint(obj);
            if (CurrentNavPoint != navPoint && HostileSphere(obj, navPoint) && DistanceFromObject(obj, other) > 10000)
            {
                Explode(-1, obj);
                return true;
            }
        }
        return false;
    }

    /// <summary>Switches to an attack objective (a Kilrathi ace greets the player the first time)
    /// and sets the target.</summary>
    /// <remarks>C: engage (0x423A50, logic.c).</remarks>
    public void Engage(short obj, short target, ShipObjective objective)
    {
        if (Ships[obj].Objective != objective)
        {
            ResetObjective(obj, objective);
            if (Ships[obj].Rating > 8 && !AceStatus(unchecked((short)(Ships[obj].PilotLevel - 14)), 8))
                AceGreeting(obj);
        }
        Ships[obj].Target = unchecked((sbyte)target);
    }

    /// <summary>The current target is an active ship of another side.</summary>
    /// <remarks>C: target_valid (0x423AC0, logic.c).</remarks>
    public bool TargetValid(short obj)
    {
        short target = Ships[obj].Target;
        return !Unactive(target) && Ships[target].Side != Ships[obj].Side;
    }

    /// <summary>
    /// Mission success as far as the ship can tell: PATROL always (the original walks the flight path
    /// but ignores the result), ESCORT/DEFEND/WINGMAN while the mission ship lives, STRIKE once it is
    /// destroyed, else false.
    /// </summary>
    /// <remarks>C: triumph (0x423B00, logic.c); used by the music director and <c>i_wanna_rout</c>.</remarks>
    public bool Triumph(short obj)
    {
        switch (Ships[obj].MissionType)
        {
            case ShipMissionType.Patrol:
                int objective = 0;
                while (FlightPath[objective] != -1 && Visited(FlightPath[objective]))
                    objective++;
                return true;
            case ShipMissionType.Escort:
            case ShipMissionType.Defend:
            case ShipMissionType.Wingman:
                return !DeadShip(Ships[obj].MissionShip);
            case ShipMissionType.Strike:
                return DeadShip(Ships[obj].MissionShip);
        }
        return false;
    }

    /// <summary>
    /// Health in percent (about 26..100 for an intact ship, can go negative): rear armor counts 27,
    /// front 23, sides 12 each, core damage -26, plus 26. Non-ships report 100.
    /// </summary>
    /// <remarks>C: evaluate_damage (0x423C00, logic.c).</remarks>
    public short EvaluateDamage(short obj)
    {
        if (Objects[obj].Class < ObjectClass.Ship)
            return 100;
        var typeData = TypeDataOf(obj);
        ref readonly var ship = ref Ships[obj];
        return unchecked((short)(
            ship.Damage * -26 / typeData.DamageCapacity +
            ship.Armor[ArmorValues.Rear] * 27 / typeData.ArmorRear +
            ship.Armor[ArmorValues.Front] * 23 / typeData.ArmorFront +
            ship.Armor[ArmorValues.Left] * 12 / typeData.ArmorLeft +
            ship.Armor[ArmorValues.Right] * 12 / typeData.ArmorRight + 26));
    }

    /// <summary>Loadout slot of a space mine, or -1.</summary>
    /// <remarks>C: mine_available (0x423CD0, logic.c).</remarks>
    public short MineAvailable(short obj) => FindWeapon(obj, ObjectType.SpaceMine);

    /// <summary>The common filter of the target scans: a (capital) ship that is not dying and on
    /// another side than <paramref name="obj"/>.</summary>
    private bool IsLivingEnemyShip(short obj, short other) =>
        Objects[other].Class >= ObjectClass.Ship &&
        Ships[other].SpecialManeuver != SpecialManeuver.Unknown9 &&
        Ships[obj].Side != Ships[other].Side;
}
