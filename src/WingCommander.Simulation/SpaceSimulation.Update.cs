using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// The per-frame world update (main.c Update_3Space/house_keep, spc.c house_keep_objects ..
// object_intelligence) and the missile, mine and futurion brains of brains.c.
public sealed partial class SpaceSimulation
{
    /// <summary>
    /// One 20 Hz simulation frame: housekeeping, object lifetimes, collisions, rotation,
    /// intelligence, movement, the camera, the music service and the frame counter. Strictly
    /// tick-based: no rendering, no waiting.
    /// </summary>
    /// <remarks>C: Update_3Space (0x427C50, main.c).</remarks>
    public void Update3Space()
    {
        HouseKeep();
        HouseKeepObjects();
        UpdateObjectsInSpace();
        SetEyeDirectionAndPosition();
        Events.ServiceTrack(SpaceFrame);
        SpaceFrame = unchecked((short)(SpaceFrame + 1));
    }

    /// <summary>Every 32 frames re-evaluates the nav sphere, every 16 frames the hazard fields;
    /// the palette/alarm half goes to <see cref="ISimulationEvents.HouseKeepCockpit"/>.</summary>
    /// <remarks>C: house_keep (0x427D40, main.c).</remarks>
    public void HouseKeep()
    {
        if (CannedSceneMode == 0 && !TrainSimActive)
        {
            if ((SpaceFrame & 0x1f) == 0)
                ReleaseStaleNavTarget();
            if (HazardFieldCount != 0 && (SpaceFrame & 0xf) == 0)
                CheckHazards();
        }
        Events.HouseKeepCockpit(CameraViewMode);
    }

    /// <summary>Decrements the counter of <paramref name="obj"/> unless it is -1 and returns it.</summary>
    /// <remarks>C: count_down (0x412410, spc.c).</remarks>
    public short CountDown(short obj)
    {
        ref short counter = ref Objects[obj].Counter;
        if (counter != -1)
            counter = unchecked((short)(counter - 1));
        return counter;
    }

    /// <summary>
    /// Lifetimes of slots 0..60: debris and dust expire, engine flames are removed (recreated each
    /// view frame), projectiles expire (flak shells explode), mines and missiles count down to their
    /// explosion, missiles ignite after their launch delay, dying ships run their explosion sequence,
    /// warping ships appear or leave, and the Tiger's Claw lands the player.
    /// </summary>
    /// <remarks>C: house_keep_objects (0x412430, spc.c).</remarks>
    public void HouseKeepObjects()
    {
        for (short obj = 0; obj <= ObjectSlots.LastMoving; obj++)
        {
            ref var o = ref Objects[obj];
            switch (o.Class)
            {
                case ObjectClass.Dust:
                    if (o.Type == ObjectType.DebrisDust && CountDown(obj) == -1 && o.ScreenX == ObjectSlots.NotVisible)
                        RemoveObject(obj);
                    break;
                case ObjectClass.Debris:
                    if (CountDown(obj) == -1)
                        RemoveObject(obj);
                    break;
                case ObjectClass.FixedObject:
                    if (o.Type is ObjectType.Turret or ObjectType.Thrusters)
                        RemoveObject(obj);
                    break;
                case ObjectClass.Projectile:
                    if (CountDown(obj) == 0)
                    {
                        if (o.Type == ObjectType.Turret)
                            Explode(o.Owner, obj);
                        else
                            RemoveObject(obj);
                    }
                    break;
                case ObjectClass.Mine:
                    if (o.CollisionGraceTicks > 0)
                        o.CollisionGraceTicks--;
                    if (CountDown(obj) == 0)
                        Explode(obj, obj);
                    break;
                case ObjectClass.Missile:
                    HouseKeepMissile(obj);
                    break;
                case ObjectClass.Ship:
                case ObjectClass.CapitalShip:
                    HouseKeepShip(obj);
                    break;
            }
        }
    }

    /// <summary>The MISSILE case of <c>house_keep_objects</c>.</summary>
    private void HouseKeepMissile(short obj)
    {
        ref var o = ref Objects[obj];
        ref var ship = ref Ships[obj];
        ship.ExhaustHeat = 0;
        if (o.CollisionGraceTicks > 0)
            o.CollisionGraceTicks--;
        if (ship.Tactic == ShipTactic.SitStill)
        {
            if (CountDown(obj) <= 0)
            {
                ship.Tactic = ShipTactic.Ram;
                o.Counter = TypeDataOf(obj).Lifetime;
                if (o.Type == ObjectType.DumbFireMissile)
                    o.Velocity = VectorMath.ComponentInDirection(o.Velocity, o.Forward);
            }
        }
        else if (CountDown(obj) <= 0)
        {
            Explode(obj, obj);
        }
    }

    /// <summary>The SHIP/CAPITAL_SHIP case of <c>house_keep_objects</c>.</summary>
    private void HouseKeepShip(short obj)
    {
        ref var o = ref Objects[obj];
        ref var ship = ref Ships[obj];
        ship.ExhaustHeat = 0;
        if (CountDown(obj) > 0)
        {
            if (ship.Maneuver == ShipManeuver.WarpingOut)
                o.Scale = (short)(o.Scale >> 1);
            if (ship.SpecialManeuver == SpecialManeuver.Unknown9 && o.Class == ObjectClass.CapitalShip)
            {
                if (o.Counter == 7)
                {
                    ShipExplosion(obj);
                    ExplosionShockWave(obj, TypeDataOf(obj).ExplosionDamage);
                }
                else
                {
                    while ((ushort)Random.InRange(0, 100) < 50)
                        OnboardExplosion(obj);
                }
            }
        }
        else if (o.Counter == 0 && ship.SpecialManeuver == SpecialManeuver.Unknown9)
        {
            if (YourWingman != -1 && ship.LastAttacker == 0 && ship.Side == Side.Kilrathi &&
                Random.BelowOrEqual(100) < 10)
            {
                SendMessage(YourWingman, 6);
            }
            CreateExplosionDebris(obj);
            return;
        }
        else if (o.Counter == 0)
        {
            if (ship.Maneuver == ShipManeuver.WarpingIn)
            {
                if (ship.Tactic != ShipTactic.WarpIn)
                {
                    if (o.Owner == obj)
                    {
                        SetObjectsData(obj, (ObjectType)ship.NavPointIndex, -1);
                        ResetManeuver(obj, ShipManeuver.None);
                    }
                    else
                    {
                        RemoveObject(obj);
                    }
                }
            }
            else if (ship.Maneuver == ShipManeuver.WarpingOut && ship.Side != Side.Neutral)
            {
                SetMissionShipState(ship.MissionIndex, 2);
                RemoveObject(obj);
            }
        }

        if (o.Type == ObjectType.TigersClaw && PlayerCollisionsEnabled && LandingAuthorized && NormalSpeed(0))
        {
            GetFacingRangeFromObject(0, obj);
            if (TargetRange < 700 && FacingToTarget > 75 && (LandFromAnyBearing || TargetFacing > 70))
            {
                ArcadeState = 1;
                PlayerCollisionObject = obj;
            }
        }
    }

    /// <summary>Sets <c>aMissionShips[index].state</c> (2 left by warp, 3 destroyed); indices outside the
    /// table are ignored (the original wrote out of bounds).</summary>
    private void SetMissionShipState(short index, sbyte state)
    {
        if ((uint)index < (uint)MissionShips.Length)
            MissionShips[index].State = state;
    }

    /// <summary>
    /// Pass 1 over slots 0..60: futurions wait for their warp-in, effects animate, everything from
    /// projectiles up collides, rotates and thinks; ships steer toward their goals and recharge guns.
    /// Pass 2 accelerates and moves every moving object; ships recharge shields and burn fuel.
    /// </summary>
    /// <remarks>C: update_objects_in_space (0x412820, spc.c).</remarks>
    public void UpdateObjectsInSpace()
    {
        ClearCrashCache();
        for (short obj = 0; obj <= ObjectSlots.LastMoving; obj++)
        {
            var objectClass = Objects[obj].Class;
            if (objectClass == ObjectClass.Futurion)
            {
                FuturionIntelligence(obj);
            }
            else if (objectClass > ObjectClass.Planet)
            {
                AnimateObject(obj);
                if (Objects[obj].Class != ObjectClass.Null && Objects[obj].Class >= ObjectClass.Projectile)
                {
                    ObjectCollision(obj);
                    RotateObject(obj);
                    if (obj >= ObjectSlots.ShipSlotCount || Ships[obj].SpecialManeuver != SpecialManeuver.Unknown9)
                    {
                        if (obj != 0)
                            ObjectIntelligence(obj);
                        if (obj < ObjectSlots.ShipSlotCount && Objects[obj].Class >= ObjectClass.Missile)
                        {
                            if (obj != 0)
                                RotateObjectToGoal(obj);
                            if (Objects[obj].Class == ObjectClass.Ship)
                                ReplenishWeaponEnergyBank(obj);
                        }
                    }
                }
            }
        }

        for (short obj = 0; obj <= ObjectSlots.LastMoving; obj++)
        {
            if (Objects[obj].Class > ObjectClass.Planet)
            {
                AccelerateAndMoveObject(obj);
                if (Objects[obj].Class >= ObjectClass.Ship)
                {
                    ReplenishShields(obj);
                    HousekeepPowerPlantAndFuel(obj);
                }
            }
        }
    }

    /// <summary>Applies the rotation rates (pitch, yaw, roll) and lets each decay by one degree.</summary>
    /// <remarks>C: rotate_object (0x412920, spc.c).</remarks>
    public void RotateObject(short obj)
    {
        ref var o = ref Objects[obj];
        if (o.PitchRotation != 0)
        {
            o.AlterPitch(o.PitchRotation);
            ScalarMath.DecayTowardZero(ref o.PitchRotation);
        }
        if (o.YawRotation != 0)
        {
            o.AlterYaw(o.YawRotation);
            ScalarMath.DecayTowardZero(ref o.YawRotation);
        }
        if (o.RollRotation != 0)
        {
            o.AlterRoll(o.RollRotation);
            ScalarMath.DecayTowardZero(ref o.RollRotation);
        }
    }

    /// <summary>
    /// Thrust of missiles and ships toward <c>forward * speed</c> (afterburner: maximum + 20 at double
    /// rate and 200 fuel per frame; super brake: zero at double rate), limited by the type's
    /// acceleration scaled with the alignment of the needed change with the nose; then every object
    /// moves by its velocity.
    /// </summary>
    /// <remarks>C: accelerate_and_move_object (0x4129A0, spc.c).</remarks>
    public void AccelerateAndMoveObject(short obj)
    {
        ref var o = ref Objects[obj];
        if (o.Class >= ObjectClass.Missile)
        {
            ref var ship = ref Ships[obj];
            if (ship.SpecialManeuver == SpecialManeuver.KillEngines)
            {
                ship.ExhaustHeat = 0;
                if (Random.BelowOrEqual(100) < 10)
                    SetSpecial(obj, SpecialManeuver.None);
            }
            else if (ship.SpecialManeuver == SpecialManeuver.StopDrift)
            {
                ApproachZeroSpeed(obj);
                VectorMath.NormalizeAndScale(ref o.Velocity, o.Speed);
                if (o.Speed == 0)
                    SetSpecial(obj, SpecialManeuver.None);
            }

            if (ship.SpecialManeuver < SpecialManeuver.KillEngines && ship.Tactic != ShipTactic.SitStill)
            {
                var accelerationVector = FixedVector.Zero;
                FixedVector delta;
                switch (ship.SpecialManeuver)
                {
                    case SpecialManeuver.Afterburner:
                        ship.AfterburnerTimer--;
                        if (ship.AfterburnerTimer == 0)
                        {
                            ship.SpecialManeuver = SpecialManeuver.None;
                            Events.AfterburnerExpired();
                            delta = VectorMath.Scale(o.Forward, o.Speed);
                        }
                        else
                        {
                            delta = VectorMath.Scale(o.Forward, (TypeDataOf(obj).MaximumVelocity + 20) * 0x200);
                            DrainFuel(obj, 200);
                            ship.ExhaustHeat = 3;
                        }
                        break;
                    case SpecialManeuver.SuperBrake:
                        ship.AfterburnerTimer--;
                        if (ship.AfterburnerTimer == 0)
                        {
                            ship.SpecialManeuver = SpecialManeuver.None;
                            delta = VectorMath.Scale(o.Forward, o.Speed);
                        }
                        else
                        {
                            delta = FixedVector.Zero;
                            DrainFuel(obj, 140);
                        }
                        break;
                    default:
                        delta = VectorMath.Scale(o.Forward, o.Speed);
                        break;
                }
                delta = VectorMath.Delta(o.Velocity, delta);
                int magnitude = delta.Magnitude();
                if (magnitude > 0)
                {
                    int acceleration = GetShipAccelerationRate(obj);
                    if (AlertFlag(obj, 1) && acceleration < 0x500)
                        acceleration = 0x500;
                    if (ship.SpecialManeuver is SpecialManeuver.Afterburner or SpecialManeuver.SuperBrake)
                        acceleration *= 2;
                    acceleration = FixedMath.Multiply(
                        acceleration,
                        FixedMath.Divide(VectorMath.Dot(delta, o.Forward), magnitude) + 0x200);
                    accelerationVector = VectorMath.Scale(
                        delta,
                        ScalarMath.MinInt(FixedMath.Divide(acceleration >> 1, magnitude), 0x100));
                    if (ship.SpecialManeuver != SpecialManeuver.Afterburner)
                        ship.ExhaustHeat = 2;
                }
                o.Velocity = VectorMath.Add(accelerationVector, o.Velocity);
                if (obj == ObjectSlots.Player)
                    PlayerAcceleration = accelerationVector;
            }
        }
        o.Position = VectorMath.Add(o.Position, o.Velocity);
    }

    /// <summary>
    /// Effects, debris, flames, asteroids and mines step their animation script; a ship with at least
    /// half its damage capacity in accumulated damage trails a spark (every 4th view frame while
    /// visible, sound 7 one time in four).
    /// </summary>
    /// <remarks>C: animate_object (0x412E30, spc.c).</remarks>
    public void AnimateObject(short obj)
    {
        switch (Objects[obj].Class)
        {
            case ObjectClass.Explosion:
            case ObjectClass.Debris:
            case ObjectClass.FixedObject:
            case ObjectClass.Asteroid:
            case ObjectClass.Mine:
                AnimateShape(obj);
                break;
            case ObjectClass.Ship:
                ref var o = ref Objects[obj];
                if (o.ScreenX == ObjectSlots.NotVisible ||
                    (RenderedSpaceFrame & 3) != 0 ||
                    (TypeDataOf(obj).DamageCapacity >> 1) - 1 > o.AccumulatedDamage)
                {
                    break;
                }
                short effect = FindVacant3dObject();
                if (effect == -1)
                    break;
                ref var spark = ref Objects[effect];
                var offset = VectorMath.Scale(o.Forward, -(o.CollisionRadius << 8));
                spark.Position = VectorMath.Add(o.Position, offset);
                offset = RandomVectors.FillFixedVectorWithRandomComponents(Random, 20);
                spark.Position = VectorMath.Add(spark.Position, offset);
                spark.Velocity = FixedVector.Zero;
                SetObjectsData(effect, (ObjectType)((ushort)Random.InRange(0, 2) + (int)ObjectType.RedSpark), obj);
                spark.Scale = o.Scale;
                if (Random.InRange(0, 3) == 0)
                    Events.PlaySoundEffect(7, obj);
                break;
        }
    }

    /// <summary>A hit asteroid shatters into 2..3 rock chunks plus an explosion with probability
    /// 1/<paramref name="destructionChance"/> (always for 0), else chips one chunk with probability 1/8.</summary>
    /// <remarks>C: hit_asteroid (0x413030, spc.c).</remarks>
    public void HitAsteroid(short asteroid, short destructionChance)
    {
        if (Random.BelowOrEqual(unchecked((short)(destructionChance - 1))) == 0)
        {
            short fragments = (short)(Random.BelowOrEqual(1) + 2);
            while (fragments > 0)
            {
                MakeShard(asteroid, Objects[asteroid].Velocity);
                fragments--;
            }
            Explode(-1, asteroid);
        }
        else if (Random.BelowOrEqual(7) == 0)
        {
            MakeShard(asteroid, CollisionDelta);
        }
    }

    /// <summary>
    /// The brain of one object: nothing during the autopilot, the canned sequence in canned mode 2,
    /// futurions wait for their warp, mines watch for ships, missiles steer every 4th frame (every
    /// frame for the missile camera's missile), ships and capital ships run their AI
    /// (<see cref="ShipIntelligence"/>, <see cref="CapitalShipIntelligence"/>).
    /// </summary>
    /// <remarks>C: object_intelligence (0x413880, spc.c).</remarks>
    public void ObjectIntelligence(short obj)
    {
        if (CannedSceneMode == 4)
            return;
        if (CannedSceneMode == 2 && Objects[obj].Class > ObjectClass.Missile)
        {
            UpdateCannedSequence(obj);
            return;
        }
        switch (Objects[obj].Class)
        {
            case ObjectClass.Futurion:
                FuturionIntelligence(obj);
                break;
            case ObjectClass.Mine:
                MineIntelligence(obj);
                break;
            case ObjectClass.Missile:
                if ((System.Math.Abs((int)SpaceFrame) & 3) != 0 && ExternalViewShip != obj)
                    break;
                if (Ships[obj].Target != -1)
                    GetFacingRangeFromObject(obj, Ships[obj].Target);
                switch (Objects[obj].Type)
                {
                    case ObjectType.DumbFireMissile:
                        Objects[obj].Speed = (GetShipMaxVelocity(obj) + 10) * 0x100;
                        break;
                    case ObjectType.HeatSeekingMissile:
                        HeatSeekingMissileIntelligence(obj);
                        break;
                    case ObjectType.FriendOrFoeMissile:
                        FfMissileIntelligence(obj);
                        break;
                    case ObjectType.ImageRecognitionMissile:
                        var toTarget = ToTarget;
                        PointShip(obj, 0, toTarget);
                        Objects[obj].Speed = (GetShipMaxVelocity(obj) + 10) * 0x100;
                        break;
                }
                break;
            case ObjectClass.Ship:
                ShipIntelligence(obj);
                break;
            case ObjectClass.CapitalShip:
                CapitalShipIntelligence(obj);
                break;
        }
    }

    /// <summary>
    /// A ship waiting to warp in (class FUTURION, its real class in the counter) appears once the
    /// player is 1000+ units away for 1000 frames, or after 200 frames when the player looks at its
    /// position from 1000..4000 units.
    /// </summary>
    /// <remarks>C: futurion_intelligence (0x40B320, brains.c).</remarks>
    public void FuturionIntelligence(short obj)
    {
        ShipVsShip(0, obj);
        short range = TargetRange;
        short count = ++Ships[obj].ActionCount;
        if (range > 1000 && count > 1000)
        {
            Objects[obj].Class = (ObjectClass)Objects[obj].Counter;
            return;
        }
        if (count > 200 && range < 4000 && range > 1000 && FacingToTarget > 80)
            Objects[obj].Class = (ObjectClass)Objects[obj].Counter;
    }

    /// <summary>An armed mine (counter -1) explodes when a ship touches its collision radius, or with
    /// probability 1/8 per frame when one is within 50 units.</summary>
    /// <remarks>C: mine_intelligence (0x40B3A0, brains.c).</remarks>
    public void MineIntelligence(short obj)
    {
        if (Objects[obj].Counter != -1)
            return;
        for (short other = 0; other <= ObjectSlots.LastShip; other++)
        {
            if (other == obj || Objects[other].Class < ObjectClass.Ship)
                continue;
            short distance = DistanceFromObject(obj, other);
            if (distance < TypeDataOf(obj).CollisionRadius || (distance < 50 && Random.BelowOrEqual(7) == 0))
            {
                Explode(obj, obj);
                return;
            }
        }
    }

    /// <summary>
    /// Heat seeker: steers at a target that is still ahead; otherwise re-acquires the nearest ship
    /// within 9000 that is ahead and flying away, preferring capital ships and the hottest exhaust
    /// (afterburner 3, thrust 2, ...); explodes when none qualifies.
    /// </summary>
    /// <remarks>C: heat_seeking_missile_intelligence (0x40B430, brains.c).</remarks>
    public void HeatSeekingMissileIntelligence(short obj)
    {
        if (FacingToTarget >= 0 && Ships[obj].Target != -1)
        {
            var toTarget = ToTarget;
            PointShip(obj, 0, toTarget);
            Objects[obj].Speed = (GetShipMaxVelocity(obj) + 10) << 8;
            return;
        }

        ViableTargetCount = 0;
        Ships[obj].Target = -1;
        for (short other = 0; other <= ObjectSlots.LastShip; other++)
        {
            if (other == obj || Objects[other].Class < ObjectClass.Ship)
                continue;
            GetFacingRangeFromObject(obj, other);
            if (TargetRange < 9000 && FacingToTarget > 0 && TargetFacing < 0)
            {
                ViableTargetDistance[ViableTargetCount] = TargetRange;
                ViableTarget[ViableTargetCount] = (sbyte)other;
                ViableTargetCount++;
            }
        }
        SortViableTargetList();
        sbyte targetCount = ViableTargetCount;
        if (targetCount > 0)
        {
            for (short heat = 3; heat > 0; heat--)
            {
                for (short candidate = 0; candidate < targetCount; candidate++)
                {
                    sbyte target = ViableTarget[candidate];
                    if (Objects[target].Class == ObjectClass.CapitalShip || Ships[target].ExhaustHeat == heat)
                    {
                        Ships[obj].Target = target;
                        heat = 0;
                        break;
                    }
                }
            }
        }
        if (Ships[obj].Target == -1)
            Explode(obj, obj);
    }

    /// <summary>Friend-or-foe missile: once burning (tactic RAM) without a target, picks the nearest
    /// ship within 9000 that is not a friend of its owner with a working communicator; then steers at it.</summary>
    /// <remarks>C: FF_missile_intelligence (0x40B570, brains.c). The owner's side is read through
    /// <see cref="SideOf"/> (the original indexed the side table with the owner unchecked).</remarks>
    public void FfMissileIntelligence(short obj)
    {
        if (Ships[obj].Tactic != ShipTactic.Ram)
            return;
        if (Ships[obj].Target == -1)
        {
            ViableTargetCount = 0;
            for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
            {
                if (other == obj || Objects[other].Class < ObjectClass.Ship)
                    continue;
                if (SideOf(Objects[obj].Owner) == Ships[other].Side && Ships[other].Communicator != -1)
                    continue;
                TargetRange = DistanceFromObject(obj, other);
                if (TargetRange < 9000)
                {
                    int candidate = ViableTargetCount++;
                    ViableTargetDistance[candidate] = TargetRange;
                    ViableTarget[candidate] = (sbyte)other;
                }
            }
            SortViableTargetList();
            if (ViableTargetCount > 0)
                Ships[obj].Target = ViableTarget[0];
        }
        else
        {
            var toTarget = ToTarget;
            PointShip(obj, 0, toTarget);
            Objects[obj].Speed = (GetShipMaxVelocity(obj) + 10) << 8;
        }
    }

    /// <summary>Sorts the viable target list by distance (selection sort, nearest first).</summary>
    /// <remarks>C: sort_viable_target_list (0x41E860, disk.c).</remarks>
    public void SortViableTargetList()
    {
        if (ViableTargetCount <= 1)
            return;
        short count = ViableTargetCount;
        for (short outer = 0; outer < count - 1; outer++)
        {
            for (short inner = (short)(outer + 1); inner < count; inner++)
            {
                short distance = ViableTargetDistance[outer];
                if (ViableTargetDistance[inner] < distance)
                {
                    ViableTargetDistance[outer] = ViableTargetDistance[inner];
                    sbyte target = ViableTarget[outer];
                    ViableTargetDistance[inner] = distance;
                    ViableTarget[outer] = ViableTarget[inner];
                    ViableTarget[inner] = target;
                }
            }
        }
    }

    /// <summary>
    /// Canned-sequence driver (cinematic AI): command 0 waits <c>ActionCount</c> frames, command 1
    /// waits until the rotation goals are reached and the speed condition holds, commands 3 and 4
    /// advance at once.
    /// </summary>
    /// <remarks>C: update_canned_sequence (0x403B70, mono.c). The speed condition of command 1 compares
    /// the player's velocity and evaluates <c>(velocity &gt; requested - 0x400) &lt; requested + 0x400</c>,
    /// a boolean against a speed, as in the original.</remarks>
    public void UpdateCannedSequence(short obj)
    {
        ref var ship = ref Ships[obj];
        switch (ship.CannedCommand)
        {
            case 0:
                ship.ActionCount--;
                if (ship.ActionCount == 0)
                    AdvanceCannedSequence(obj);
                break;
            case 1:
                if (ship.YawGoal == 0 && ship.PitchGoal == 0 && ship.RollGoal == 0)
                {
                    int requested = Objects[obj].Speed;
                    int velocity = Objects[ObjectSlots.Player].Velocity.Magnitude();
                    int faster = velocity > requested - 0x400 ? 1 : 0;
                    if (faster < requested + 0x400)
                        AdvanceCannedSequence(obj);
                }
                break;
            case 3:
            case 4:
                AdvanceCannedSequence(obj);
                break;
        }
    }

    /// <summary>Forgets the per-frame predicted collision partners.</summary>
    /// <remarks>C: clear_crash_cache (0x422440, logic.c).</remarks>
    public void ClearCrashCache()
    {
        for (int i = 0; i < ObjectSlots.ShipSlotCount; i++)
            Ships[i].CollisionPartner = -1;
    }

    /// <summary>Side of ship slot <paramref name="obj"/>, or <see cref="Side.Neutral"/> for any other
    /// index (the original read past the 12-entry side table for unowned objects).</summary>
    public Side SideOf(int obj) =>
        (uint)obj < ObjectSlots.ShipSlotCount ? Ships[obj].Side : Side.Neutral;
}
