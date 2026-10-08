using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Weapons: firing guns, missiles and mines, the NPC fire decision, capital ship turrets and flak
// (ship.c send_at_point .. fire_weapon), the loadout helpers of logic.c, afterburner and super
// brake, and the per-frame energy/shield/fuel housekeeping.
public sealed partial class SpaceSimulation
{
    /// <summary>Sends <paramref name="obj"/> straight at <paramref name="point"/> with <paramref name="speed"/> units/frame.</summary>
    /// <remarks>C: send_at_point (0x420190, ship.c).</remarks>
    public void SendAtPoint(short obj, in FixedVector point, short speed)
    {
        var velocity = VectorMath.Delta(Objects[obj].Position, point);
        VectorMath.SetLength(ref velocity, speed);
        Objects[obj].Velocity = velocity;
    }

    /// <summary>First object of class <paramref name="objectClass"/> owned by <paramref name="parent"/>, or -1.</summary>
    /// <remarks>C: find_child_object (0x4201D0, ship.c).</remarks>
    public short FindChildObject(short parent, ObjectClass objectClass)
    {
        for (short obj = 0; obj < ObjectSlots.Count; obj++)
        {
            if (Objects[obj].Owner == parent && Objects[obj].Class == objectClass)
                return obj;
        }
        return -1;
    }

    /// <summary>First ship slot of class <paramref name="objectClass"/> owned by <paramref name="parent"/>
    /// and aimed at <paramref name="target"/> (any target for -1), or -1.</summary>
    /// <remarks>C: find_child_ship (0x420210, ship.c).</remarks>
    public short FindChildShip(short parent, ObjectClass objectClass, short target)
    {
        for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Objects[obj].Owner == parent && Objects[obj].Class == objectClass &&
                (target == -1 || Ships[obj].Target == target))
            {
                return obj;
            }
        }
        return -1;
    }

    /// <summary>Gives <paramref name="child"/> the parent's velocity component along
    /// <paramref name="direction"/> plus <paramref name="speed"/> units/frame along it.</summary>
    /// <remarks>C: launch_object (0x420260, ship.c).</remarks>
    public void LaunchObject(short parent, short child, FixedVector direction, short speed)
    {
        VectorMath.Normalize(ref direction);
        var velocity = VectorMath.ComponentInDirection(Objects[parent].Velocity, direction);
        direction = VectorMath.Scale(direction, speed << 8);
        Objects[child].Velocity = VectorMath.Add(direction, velocity);
    }

    /// <summary>
    /// The NPC fire decision against <paramref name="target"/>: guns in range fire when aimed
    /// (laser &gt; 70 %, neutron &gt; 80 %, mass driver &gt; 85 %, turret &gt; 10 %) and their
    /// disabled flags record the decision; turret shells are launched at the target; one missile per
    /// call when a 1/20 roll and the range roll allow it and no own missile already chases the target.
    /// </summary>
    /// <remarks>C: fire (0x4202D0, ship.c). The loadout count is re-read every iteration (a fired
    /// missile shifts the slots). <c>velocityAngle</c> is read before it is assigned in the original;
    /// the mine branch it guards can never fire (its outer test requires facing &gt;= -50 and target
    /// facing &lt;= 90, its inner test the opposite), so the value is unobservable and starts at 0
    /// here. A turret shell that could not be created is not launched (the original read
    /// <c>aShipPosition[-1]</c>).</remarks>
    public void Fire(short obj, short target)
    {
        bool canFire = Objects[obj].Counter <= 0;
        GetFacingRangeFromObject(obj, target);
        short range = TargetRange;
        short closingSpeed = unchecked((short)(Objects[target].Speed * (int)TargetFacing / 100 >> 8));
        bool fireMissile = Random.BelowOrEqual(19) == 0 && Random.BelowOrEqual(7000) > range;
        if (fireMissile && FindChildShip(obj, ObjectClass.Missile, target) != -1)
            fireMissile = false;
        bool minePresent = FindChildObject(obj, ObjectClass.Mine) != -1;
        short velocityAngle = 0;
        short mineTime = 0;

        for (short weapon = 0; weapon < Ships[obj].Weapons.Count; weapon++)
        {
            var weaponType = Ships[obj].Weapons.GetWeaponType(weapon);
            var weaponData = ObjectTypeTable.Get(weaponType);
            short weaponVelocity = weaponData.MaximumVelocity;
            if (closingSpeed < 0)
                weaponVelocity = unchecked((short)(weaponVelocity + closingSpeed / 100));
            bool targetInRange = unchecked((short)(weaponData.Lifetime * weaponVelocity)) > range;
            bool shouldFire = false;
            var weaponClass = weaponData.ObjectClass;

            if (weaponClass == ObjectClass.Projectile)
            {
                if (canFire && targetInRange)
                {
                    shouldFire = weaponType switch
                    {
                        ObjectType.LaserCannon => FacingToTarget > 70,
                        ObjectType.NeutronParticleGun => FacingToTarget > 80,
                        ObjectType.MassDriverCannon => FacingToTarget > 85,
                        ObjectType.Turret => FacingToTarget > 10,
                        _ => false,
                    };
                }
                Ships[obj].Weapons.SetDisabled(weapon, (sbyte)(shouldFire ? 0 : 1));
                if (shouldFire)
                {
                    short firedObject = FireWeapon(obj, weapon);
                    if (weaponType == ObjectType.Turret && firedObject != -1)
                    {
                        var direction = VectorMath.Delta(Objects[firedObject].Position, Objects[target].Position);
                        LaunchObject(obj, firedObject, direction, RealVelocity(firedObject));
                    }
                }
            }
            else if (weaponClass == ObjectClass.Mine)
            {
                if (!minePresent && weaponType == ObjectType.SpaceMine &&
                    Objects[obj].Speed >= 0x500 &&
                    velocityAngle >= 75 && range <= 2000 &&
                    FacingToTarget >= -50 &&
                    TargetFacing <= 90)
                {
                    velocityAngle = VectorMath.VectorAngle(Objects[target].Velocity, Objects[obj].Velocity);
                    short predictionTime = unchecked((short)(900 / (short)((Objects[obj].Speed >> 8) + 20)));
                    short predictedSeparation = unchecked((short)(predictionTime * (short)(-20 - closingSpeed) + range));
                    mineTime = closingSpeed == -20 ? range : unchecked((short)(range / (closingSpeed + 20)));
                    if (range < 2000 && FacingToTarget < -50 && TargetFacing > 90)
                    {
                        shouldFire = true;
                        if (predictedSeparation <= 50)
                            shouldFire = false;
                    }
                }
                if (shouldFire)
                {
                    short firedObject = DropMine(obj, (sbyte)weapon, weaponType, unchecked((short)(mineTime + 15)));
                    if (firedObject != -1)
                    {
                        var direction = VectorMath.Scale(Objects[target].Velocity, mineTime);
                        ref var interceptPoint = ref Objects[ObjectSlots.Scratch].Position;
                        interceptPoint = VectorMath.Add(Objects[target].Position, direction);
                        direction = VectorMath.Delta(Objects[firedObject].Position, interceptPoint);
                        LaunchObject(obj, firedObject, direction, 20);
                    }
                    minePresent = true;
                }
            }
            else if (weaponClass == ObjectClass.Missile)
            {
                if (fireMissile && targetInRange)
                {
                    shouldFire = weaponType switch
                    {
                        ObjectType.DumbFireMissile => FacingToTarget > 97,
                        ObjectType.HeatSeekingMissile => FacingToTarget > 40 && TargetFacing < -60,
                        ObjectType.FriendOrFoeMissile => true,
                        ObjectType.ImageRecognitionMissile => FacingToTarget > 40,
                        _ => false,
                    };
                }
                if (shouldFire)
                {
                    FireWeapon(obj, weapon);
                    fireMissile = false;
                }
            }
        }
    }

    /// <summary>Percent cosine between the directions from <paramref name="parent"/> to
    /// <paramref name="target"/> and to <paramref name="hardpoint"/> (both taken parent-relative):
    /// is the target in the turret's half of the ship.</summary>
    /// <remarks>C: hemisphere (0x4207E0, ship.c).</remarks>
    public static short Hemisphere(in FixedVector target, in FixedVector parent, in FixedVector hardpoint)
    {
        var parentFromHardpoint = VectorMath.Delta(hardpoint, parent);
        var parentFromTarget = VectorMath.Delta(target, parent);
        return VectorMath.VectorAngle(parentFromTarget, parentFromHardpoint);
    }

    /// <summary>Turns <paramref name="explosion"/> into a flak shell (TURRET bolt) flying at
    /// <paramref name="aim"/> with a fuse of <c>range / speed - 5..13</c> frames (5..27).</summary>
    /// <remarks>C: fire_flack (0x420840, ship.c).</remarks>
    public void FireFlack(short owner, short explosion, short range, in FixedVector aim)
    {
        short projectileVelocity = ObjectTypeTable.Get(ObjectType.Turret).MaximumVelocity;
        SetObjectsData(explosion, ObjectType.Turret, owner);
        short lifetime = unchecked((short)(range / projectileVelocity - Random.BelowOrEqual(8) - 5));
        lifetime = ScalarMath.MaxShort(5, lifetime);
        lifetime = ScalarMath.MinShort(27, lifetime);
        Objects[explosion].Counter = lifetime;
        SendAtPoint(explosion, aim, projectileVelocity);
    }

    /// <summary><paramref name="value"/> or its negation (50 %).</summary>
    /// <remarks>C: rnd_sign (0x4208C0, ship.c).</remarks>
    public short RndSign(short value) => Random.BelowOrEqual(1) != 0 ? value : unchecked((short)-value);

    /// <summary>A random aim offset in 24.8: <c>±min(maximum, rnd(radius) + speed)</c>.</summary>
    /// <remarks>C: rnd_aim (0x4208E0, ship.c).</remarks>
    public int RndAim(short radius, short speed, short maximum)
    {
        short aim = ScalarMath.MinShort(maximum, unchecked((short)(Random.BelowOrEqual(radius) + speed)));
        return RndSign(aim) << 8;
    }

    /// <summary>
    /// A capital ship's flak: an EXPLOSION0 near its target (random box of <c>max(400, range/4)</c>
    /// plus 16 × the target's speed, at most 1500) either bursts at once as a shock wave (92 % while
    /// the gun cools down, 60 % when it is ready) or flies there as a shell from the hardpoint, after
    /// which the gun cools down for 7..16 frames.
    /// </summary>
    /// <remarks>C: pop_flack (0x420920, ship.c).</remarks>
    public short PopFlack(short obj, short range, in FixedVector hardpoint)
    {
        short target = Ships[obj].Target;
        short explosion = NewObject(ObjectType.Explosion0, obj);
        if (explosion != -1)
        {
            short aimRadius = ScalarMath.MaxShort(400, (short)(range >> 2));
            short targetSpeed = unchecked((short)(RealVelocity(target) << 4));
            var randomAim = new FixedVector(
                RndAim(aimRadius, targetSpeed, 1500), 0, 0);
            randomAim.Y = RndAim(aimRadius, targetSpeed, 1500);
            randomAim.Z = RndAim(aimRadius, targetSpeed, 1500);
            var aimPoint = VectorMath.Add(Objects[target].Position, randomAim);
            short chance = Random.BelowOrEqual(100);
            if ((Objects[obj].Counter != -1 || chance >= 40) && chance >= 8)
            {
                Objects[explosion].Position = aimPoint;
                ExplosionShockWave(explosion, ObjectTypeTable.Get(ObjectType.Turret).ExplosionDamage);
                return explosion;
            }
            Objects[explosion].Position = hardpoint;
            FireFlack(obj, explosion, range, aimPoint);
            Objects[obj].Counter = unchecked((short)(Random.Below(10) + 7));
        }
        return explosion;
    }

    /// <summary>
    /// Capital ship guns: every weapon slot (1/3 chance each) picks an enemy within 5000 starting at
    /// a random list entry; turrets need the target in their half (hemisphere ≥ 25) and pop flak,
    /// other weapons (missiles) fire with 1/15 chance when the hemisphere exceeds 50. Returns false
    /// when no enemy is in range.
    /// </summary>
    /// <remarks>C: fire_turrets (0x420AA0, ship.c).</remarks>
    public bool FireTurrets(short obj)
    {
        short lastTarget = BuildTargetList(obj, 5000);
        lastTarget--;
        if (lastTarget == -1)
            return false;
        for (short weapon = 0; weapon < Ships[obj].Weapons.Count; weapon++)
        {
            if (Random.BelowOrEqual(2) != 0)
                continue;
            var hardpoint = PositionChild(obj, Ships[obj].Weapons.GetHardpoint(weapon));
            short startTarget = Random.BelowOrEqual(lastTarget);
            short targetIndex = startTarget;
            do
            {
                short target = FormationMemberList[targetIndex];
                short targetHemisphere = Hemisphere(Objects[target].Position, Objects[obj].Position, hardpoint);
                if (Ships[obj].Weapons.GetWeaponType(weapon) != ObjectType.Turret)
                {
                    if (targetHemisphere > 50 && Random.BelowOrEqual(14) == 0)
                    {
                        Ships[obj].Target = unchecked((sbyte)target);
                        FireWeapon(obj, weapon);
                    }
                    break;
                }
                if (targetHemisphere >= 25)
                {
                    Ships[obj].Target = unchecked((sbyte)target);
                    PopFlack(obj, TargetListRange[targetIndex], hardpoint);
                    break;
                }
                targetIndex++;
                if (targetIndex > lastTarget)
                    targetIndex = 0;
            }
            while (targetIndex != startTarget);
        }
        return true;
    }

    /// <summary>
    /// Fires loadout slot <paramref name="weapon"/> of <paramref name="obj"/>: mines are dropped;
    /// missiles take a ship slot, get a 10-unit upward kick, leave the loadout and ignite after 5
    /// frames (dumb-fire: 1, aimed at the target's lead point; friend-or-foe: 15, no target); guns
    /// (turrets fire laser bolts) spend energy, aim at a point far ahead and set the refire delay.
    /// Returns the new object or -1.
    /// </summary>
    /// <remarks>C: fire_weapon (0x420C20, ship.c). The player's refire delay index is range-checked.</remarks>
    public short FireWeapon(short obj, short weapon)
    {
        short projectileSpeed = 10;
        var weaponType = Ships[obj].Weapons.GetWeaponType(weapon);
        short hardpoint = Ships[obj].Weapons.GetHardpoint(weapon);
        var weaponClass = ObjectTypeTable.Get(weaponType).ObjectClass;
        if (weaponType == ObjectType.Turret)
        {
            weaponClass = ObjectClass.Projectile;
            weaponType = ObjectType.LaserCannon;
        }
        if (weaponClass == ObjectClass.Mine)
            return DropMine(obj, (sbyte)weapon, weaponType, -1);
        short projectile = weaponClass == ObjectClass.Missile
            ? InitializeShip(weaponType, obj)
            : NewObject(weaponType, obj);
        if (projectile == -1)
            return projectile;

        var weaponData = ObjectTypeTable.Get(weaponType);
        CopyFrame(obj, projectile);
        ref var p = ref Objects[projectile];
        if (weaponClass == ObjectClass.Projectile)
        {
            p.AccumulatedDamage = weaponData.DamageCapacity;
            projectileSpeed = TypeDataOf(projectile).MaximumVelocity;
            Ships[obj].WeaponEnergy = unchecked((short)(Ships[obj].WeaponEnergy - weaponData.AnimationDelay));
        }
        ChildObject(hardpoint, projectile, obj);
        p.Counter = weaponData.Lifetime;
        p.Velocity = VectorMath.ComponentInDirection(Objects[obj].Velocity, p.Forward);
        if (weaponClass == ObjectClass.Projectile)
        {
            var aim = VectorMath.Scale(Objects[obj].Forward,
                unchecked((short)((weaponData.Lifetime + 5) * weaponData.MaximumVelocity)) << 8);
            aim = VectorMath.Add(Objects[obj].Position, aim);
            p.PointAt(aim);
            if (CockpitlessView != 0 && CockpitView == 3)
            {
                var cockpitOffset = VectorMath.Scale(Objects[obj].Up, 0x12200);
                aim = VectorMath.Add(cockpitOffset, aim);
                p.PointAt(aim);
            }
        }
        var launch = VectorMath.Scale(p.Forward, projectileSpeed << 8);
        p.Velocity = VectorMath.Add(launch, p.Velocity);

        if (weaponClass == ObjectClass.Missile)
        {
            var kick = VectorMath.Scale(Objects[obj].Up, 0xa00);
            p.Velocity = VectorMath.Add(kick, p.Velocity);
            if (obj == ObjectSlots.Player)
                RemovePlayerReleaseWeapon((sbyte)weapon);
            else
                RemoveWeapon(obj, weapon);
            p.CollisionGraceTicks = 20;
            ref var missile = ref Ships[projectile];
            missile.SpecialManeuver = SpecialManeuver.None;
            missile.Maneuver = ShipManeuver.None;
            missile.Tactic = ShipTactic.SitStill;
            p.Counter = 5;
            switch (weaponType)
            {
                case ObjectType.DumbFireMissile:
                    SteadyObject(projectile);
                    p.Counter = 1;
                    missile.Target = Ships[obj].Target;
                    p.Speed = GetShipMaxVelocity(projectile) << 8;
                    if (missile.Target != -1)
                    {
                        var vector = VectorMath.Delta(Objects[obj].Position, Objects[missile.Target].Position);
                        int range = vector.Magnitude();
                        vector = VectorMath.Scale(Objects[missile.Target].Velocity, range / GetShipMaxVelocity(projectile));
                        vector = VectorMath.Add(Objects[missile.Target].Position, vector);
                        p.PointAt(vector);
                    }
                    break;
                case ObjectType.HeatSeekingMissile:
                case ObjectType.ImageRecognitionMissile:
                    missile.Target = Ships[obj].Target;
                    break;
                case ObjectType.FriendOrFoeMissile:
                    missile.Target = -1;
                    p.Counter = 15;
                    break;
            }
        }

        if (obj == ObjectSlots.Player)
        {
            if (weaponClass == ObjectClass.Projectile)
            {
                int delayIndex = weaponType - ObjectType.LaserCannon;
                var delays = DamageTables.GunRefireDelay;
                Objects[obj].Counter = (uint)delayIndex < (uint)delays.Length ? delays[delayIndex] : (short)0;
            }
        }
        else
        {
            Objects[obj].Counter = 12;
        }

        int sound;
        switch (weaponType)
        {
            case ObjectType.LaserCannon:
            case ObjectType.NeutronParticleGun:
                sound = 8;
                break;
            case ObjectType.MassDriverCannon:
            case ObjectType.Turret:
                sound = 5;
                break;
            case ObjectType.DumbFireMissile:
            case ObjectType.HeatSeekingMissile:
            case ObjectType.FriendOrFoeMissile:
            case ObjectType.ImageRecognitionMissile:
                sound = 1;
                break;
            default:
                return projectile;
        }
        Events.PlaySoundEffect(sound, projectile);
        return projectile;
    }

    /// <summary>Fires the first missile slot of <paramref name="ship"/> (NPCs) or the first enabled
    /// missile slot of the player; the player's heat seekers and image recognition missiles need a
    /// lock ("NEED LOCK" while the weapon VDU shows). Returns the missile or -1.</summary>
    /// <remarks>C: fire_missile (0x421150, logic.c).</remarks>
    public short FireMissile(short ship)
    {
        sbyte weaponCount = Ships[ship].Weapons.Count;
        for (short weapon = 0; weapon < weaponCount; weapon++)
        {
            var type = Ships[ship].Weapons.GetWeaponType(weapon);
            if (ObjectTypeTable.Get(type).ObjectClass != ObjectClass.Missile)
                continue;
            if (ship != ObjectSlots.Player)
                return FireWeapon(ship, weapon);
            if (Ships[ship].Weapons.GetDisabled(weapon) == 0)
            {
                if ((type == ObjectType.HeatSeekingMissile || type == ObjectType.ImageRecognitionMissile) &&
                    TargetLockCountdown != 0)
                {
                    if (Cockpit.GetVduMode(0) == 1)
                        Events.ShowComponentHitHudMessage(SimulationHudMessage.NeedMissileLock, -1);
                    return -1;
                }
                return FireWeapon(0, weapon);
            }
        }
        return -1;
    }

    /// <summary>Fires every enabled gun of <paramref name="obj"/>; stops at the first gun that found
    /// no free slot.</summary>
    /// <remarks>C: fire_fixed_projectile_weapon (0x421220, logic.c); the original's return value is
    /// incidental and never used.</remarks>
    public void FireFixedProjectileWeapon(short obj)
    {
        if (Ships[obj].Weapons.Count <= 0)
            return;
        short weapon = 0;
        do
        {
            ref readonly var loadout = ref Ships[obj].Weapons;
            if (ObjectTypeTable.Get(loadout.GetWeaponType(weapon)).ObjectClass == ObjectClass.Projectile &&
                loadout.GetDisabled(weapon) == 0)
            {
                if (FireWeapon(obj, weapon) == -1)
                    return;
            }
            weapon++;
        }
        while (Ships[obj].Weapons.Count > weapon);
    }

    /// <summary>Drops a mine from loadout slot <paramref name="weapon"/>: it stays where it was
    /// dropped, ignores its owner for <paramref name="lifetime"/> frames (default 20) and explodes when
    /// its counter of the same value runs out. Returns the mine or -1.</summary>
    /// <remarks>C: drop_mine (0x4212A0, logic.c).</remarks>
    public short DropMine(short obj, sbyte weapon, ObjectType type, short lifetime)
    {
        short mine = NewObject(type, obj);
        if (mine == -1)
            return -1;
        CopyFrame(obj, mine);
        ChildObject(Ships[obj].Weapons.GetHardpoint(weapon), mine, obj);
        if (lifetime == -1)
            lifetime = 20;
        Objects[mine].CollisionGraceTicks = unchecked((sbyte)lifetime);
        Objects[mine].Counter = unchecked((sbyte)lifetime);
        if (obj == ObjectSlots.Player)
            RemovePlayerReleaseWeapon(weapon);
        else
            RemoveWeapon(obj, weapon);
        return mine;
    }

    /// <summary>Lights the afterburner for <paramref name="time"/> frames unless the ship already
    /// flies faster than 5 × its maximum speed (or a higher-priority special maneuver runs).</summary>
    /// <remarks>C: fire_afterburner (0x421350, logic.c).</remarks>
    public void FireAfterburner(short obj, short time)
    {
        int velocity = Objects[obj].Velocity.Magnitude();
        if (GetShipMaxVelocity(obj) * 0x500 > velocity)
        {
            SetSpecial(obj, SpecialManeuver.Afterburner);
            short timer = 0;
            if (Ships[obj].SpecialManeuver == SpecialManeuver.Afterburner)
                timer = time;
            Ships[obj].AfterburnerTimer = timer;
        }
    }

    /// <summary>Full brake for 10 frames.</summary>
    /// <remarks>C: fire_super_brake (0x4213B0, logic.c).</remarks>
    public void FireSuperBrake(short ship)
    {
        Ships[ship].AfterburnerTimer = 10;
        SetSpecial(ship, SpecialManeuver.SuperBrake);
    }

    /// <summary>Burns 5 fuel per frame while the engine runs.</summary>
    /// <remarks>C: housekeep_power_plant_and_fuel (0x421760, logic.c).</remarks>
    public void HousekeepPowerPlantAndFuel(short ship)
    {
        if (0 < Objects[ship].Speed)
            DrainFuel(ship, 5);
    }

    /// <summary>Shields recover one point each every <c>animationDelay</c> frames (the player only on
    /// every (power plant damage + 1)-th frame) and are clamped to their maximum.</summary>
    /// <remarks>C: replenish_shields (0x421780, logic.c). A type with an interval of 0 never
    /// recharges (the original would divide by zero; no ship type has 0).</remarks>
    public void ReplenishShields(short ship)
    {
        if (ship == ObjectSlots.Player && PlayerComponentDamage[1] > 0 &&
            SpaceFrame % (PlayerComponentDamage[1] + 1) != 0)
        {
            return;
        }
        ref var s = ref Ships[ship];
        short interval = TypeDataOf(ship).AnimationDelay;
        for (int shield = 0; shield <= 1; shield++)
        {
            short maximum = s.MaximumShield[shield];
            if (s.Shield[shield] > maximum)
                s.Shield[shield] = maximum;
            short current = s.Shield[shield];
            if (current < maximum && interval != 0 && SpaceFrame % interval == 0)
                s.Shield[shield] = unchecked((short)(current + 1));
        }
    }

    /// <summary>Gun energy recovers by 2 per frame (1 while the shields recharge) up to 100; a damaged
    /// power plant makes the player skip frames.</summary>
    /// <remarks>C: replenish_weapon_energy_bank (0x421830, logic.c).</remarks>
    public void ReplenishWeaponEnergyBank(short ship)
    {
        if (ship == ObjectSlots.Player && PlayerComponentDamage[1] != 0 &&
            (ushort)Random.InRange(0, 4) < PlayerComponentDamage[1])
        {
            return;
        }
        ref var s = ref Ships[ship];
        short energy = s.WeaponEnergy;
        if (energy >= 100)
            return;
        short shieldEnergy = unchecked((short)(s.Shield[ShieldValues.Aft] + s.Shield[ShieldValues.Fore]));
        short maximumShield = unchecked((short)(s.MaximumShield[ShieldValues.Aft] + s.MaximumShield[ShieldValues.Fore]));
        s.WeaponEnergy = shieldEnergy < maximumShield
            ? ScalarMath.MinShort(unchecked((short)(energy + 1)), 100)
            : ScalarMath.MinShort(unchecked((short)(energy + 2)), 100);
    }

    /// <summary>Enemies (other side, not dying) within <paramref name="range"/> of <paramref name="obj"/>
    /// into <see cref="FormationMemberList"/> / <see cref="TargetListRange"/> (-1 terminated); returns the count.</summary>
    /// <remarks>C: build_target_list (0x423440, logic.c).</remarks>
    public short BuildTargetList(short obj, short range)
    {
        short count = 0;
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (Objects[other].Class >= ObjectClass.Ship &&
                Ships[other].SpecialManeuver != SpecialManeuver.Unknown9 &&
                Ships[obj].Side != Ships[other].Side)
            {
                short distance = DistanceFromObject(obj, other);
                if (distance < range)
                {
                    FormationMemberList[count] = unchecked((sbyte)other);
                    TargetListRange[count] = distance;
                    count++;
                }
            }
        }
        FormationMemberList[count] = -1;
        return count;
    }

    /// <summary>Removes a launched missile or mine from the player's loadout, starts the weapon VDU's
    /// launch animation and selects the next release weapon (preferring the same type).</summary>
    /// <remarks>C: RemovePlayerReleaseWeapon (0x414CB0, cockpt.c); the display state goes to
    /// <see cref="ISimulationEvents.PlayerReleaseWeaponLaunched"/>.</remarks>
    public void RemovePlayerReleaseWeapon(sbyte weapon)
    {
        ref var loadout = ref Ships[ObjectSlots.Player].Weapons;
        var preferredType = loadout.GetWeaponType(weapon);
        Events.PlayerReleaseWeaponLaunched(preferredType, loadout.GetHardpoint(weapon));
        RemoveWeapon(0, weapon);
        SelectedReleaseWeaponIndex = -1;
        SelectNewReleaseWeapon(preferredType);
    }

    /// <summary>Drops the player's first enabled mine (20-frame fuse). Returns the mine or -1.</summary>
    /// <remarks>C: drop_player_mine (0x42ABD0, hudmsg.c).</remarks>
    public short DropPlayerMine(short obj)
    {
        sbyte weaponCount = Ships[obj].Weapons.Count;
        for (short weapon = 0; weaponCount > weapon; weapon++)
        {
            var type = Ships[obj].Weapons.GetWeaponType(weapon);
            if (ObjectTypeTable.Get(type).ObjectClass == ObjectClass.Mine && Ships[obj].Weapons.GetDisabled(weapon) == 0)
                return DropMine(obj, (sbyte)weapon, type, 20);
        }
        return -1;
    }
}
