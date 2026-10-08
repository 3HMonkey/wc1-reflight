using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Damage, kills, explosions, debris and scoring (ship.c inflict_damage .. explode), the player's
// component damage (cockpt.c malf, damage_your_component; hudmsg.c calculate_damage_level,
// personality_killed) and the AI-side helpers they need (send_message, any_enemy, kilrathi_near).
public sealed partial class SpaceSimulation
{
    /// <summary><c>acShipRating</c> of the player persona (pilot 13; the C code compares with
    /// RATING_ACE_ICEMAN because the ship rating is <c>pilot - 5</c>).</summary>
    private const sbyte RatingPlayerPersona = 8;

    /// <summary><c>acShipRating</c> of the first Kilrathi ace (pilot 14; C: RATING_ACE_ANGEL);
    /// the ace index is <c>rating - 9</c>.</summary>
    private const sbyte RatingFirstKilrathiAce = 9;

    /// <summary>After a kill: the player's wingman may cheer (50 %) when he killed a Kilrathi; a
    /// Kilrathi that killed the wingman taunts.</summary>
    /// <remarks>C: send_appropriate_message (0x41E900, ship.c), with the SDL port's guard for an
    /// unowned attacker; the owner's side is read through <see cref="SideOf"/>. An attacker of -1 (a
    /// kill without a culprit) sends nothing; the original read the class table at index -1.</remarks>
    public void SendAppropriateMessage(short attacker, short victim)
    {
        if ((uint)attacker >= ObjectSlots.Count || Objects[attacker].Class < ObjectClass.Ship)
            return;
        sbyte owner = Objects[attacker].Owner;
        if (YourWingman != -1 && owner == YourWingman && YourWingman != attacker && Ships[victim].Side == Side.Kilrathi)
        {
            if (Random.BelowOrEqual(100) < 50 && Ships[attacker].SpecialManeuver != SpecialManeuver.Unknown9)
                SendMessage(YourWingman, 5);
        }
        else if (owner != -1 && SideOf(owner) == Side.Kilrathi && YourWingman == victim)
        {
            SendMessage(owner, 5);
        }
    }

    /// <summary>
    /// Applies <paramref name="damage"/> from <paramref name="attacker"/> hitting from
    /// <paramref name="impactDirection"/>: missiles and mines accumulate up to their capacity; ships
    /// lose the fore/aft shield first, then the armor of the hit side, then take internal damage
    /// (1 % instant kill by an NPC fighter). Returns true when the victim was destroyed.
    /// </summary>
    /// <remarks>C: inflict_damage (0x41E9B0, ship.c). The original's <c>destroyed</c> is
    /// uninitialised when the 1 % roll hits but the attacker is not an NPC fighter: false here
    /// (callers ignore the result; send_appropriate_message is a no-op for such attackers). Hit
    /// debris for an attacker of -1 is placed at the origin (the original read before the table).</remarks>
    public bool InflictDamage(short attacker, short victim, short damage, FixedVector impactDirection)
    {
        if (!PlayerVulnerable && victim == ObjectSlots.Player)
            return false;
        if (damage == 0 ||
            (victim < ObjectSlots.ShipSlotCount && Ships[victim].SpecialManeuver == SpecialManeuver.Unknown9) ||
            Objects[victim].Class < ObjectClass.Missile)
        {
            return false;
        }

        ref var o = ref Objects[victim];
        if (o.Class < ObjectClass.Ship)
        {
            o.AccumulatedDamage = unchecked((short)(o.AccumulatedDamage + damage));
            short capacity = TypeDataOf(victim).DamageCapacity;
            if (capacity == -1)
                return false;
            if (capacity <= o.AccumulatedDamage)
                return Explode(attacker, victim);
            return false;
        }

        if (victim == ObjectSlots.Player)
            Events.TriggerPlayerHitPaletteFlash();
        if (attacker != -1 && YourWingman == victim && Objects[attacker].Owner == 0)
            SendMessage(victim, 10);

        ref var ship = ref Ships[victim];
        int quadrant = VectorMath.Dot(impactDirection, o.Forward) > 0 ? 1 : 0;
        damage = unchecked((short)(damage - ship.Shield[quadrant]));
        if (damage > 0)
        {
            ship.Shield[quadrant] = 0;
            if (attacker != -1 && Objects[attacker].Class == ObjectClass.Projectile)
                Events.PlaySoundEffect(9, victim);
            int sideDot = VectorMath.Dot(impactDirection, o.Right);
            if (sideDot > 0xb5)
                quadrant = 3;
            else if (sideDot < -0xb5)
                quadrant = 2;
            damage = unchecked((short)(damage - ship.Armor[quadrant]));
            if (damage > 0)
            {
                ship.Armor[quadrant] = 0;
                if (o.ScreenX != ObjectSlots.NotVisible && o.Class != ObjectClass.CapitalShip &&
                    Random.BelowOrEqual(1) == 0)
                {
                    CreateShipHitDebris(attacker, 1);
                }
                bool destroyed = false;
                if (Random.BelowOrEqual(99) == 0)
                {
                    if (attacker > 0 && attacker != YourWingman && Objects[attacker].Class == ObjectClass.Ship)
                    {
                        if (Ships[attacker].Side == Side.Kilrathi)
                            SendMessage(attacker, 6);
                        destroyed = Explode(attacker, victim);
                    }
                }
                else
                {
                    destroyed = InternalDamage(attacker, victim, damage, (short)quadrant);
                }
                if (destroyed)
                    SendAppropriateMessage(attacker, victim);
                return destroyed;
            }
            ship.Armor[quadrant] = unchecked((short)-damage);
        }
        else
        {
            ship.Shield[quadrant] = unchecked((short)-damage);
            if (attacker != -1 && Objects[attacker].Class == ObjectClass.Projectile)
                Events.PlaySoundEffect(10, victim);
        }
        return false;
    }

    /// <summary>The pilot loses a hit point: the player dies at 0 (arcade state 4); a wounded NPC
    /// failing a skill check tumbles for 30..50 frames. Returns the remaining hit points (-1 for a
    /// dead player).</summary>
    /// <remarks>C: pilot_hit (0x41EC60, ship.c). The skill check (one random number) is also rolled
    /// for the player, as in the original.</remarks>
    public short PilotHit(short obj)
    {
        ref var ship = ref Ships[obj];
        if (ship.PilotHitPoints > 0)
        {
            ship.PilotHitPoints--;
            if (ship.PilotHitPoints == 0)
            {
                if (obj == ObjectSlots.Player)
                {
                    if (PlayerVulnerable)
                        ArcadeState = 4;
                    return -1;
                }
            }
            else if (!SkillCheck(obj, 9) && obj != ObjectSlots.Player)
            {
                Objects[obj].Counter = unchecked((short)(Random.BelowOrEqual(20) + 30));
                SetSpecial(obj, SpecialManeuver.BlowingUp);
            }
        }
        return ship.PilotHitPoints;
    }

    /// <summary>A small explosion (EXPLOSION2 at four times scale, 6 frames) somewhere inside
    /// <paramref name="obj"/>'s hull. Returns false when no slot was free.</summary>
    /// <remarks>C: onboard_explosion (0x41ECE0, ship.c).</remarks>
    public bool OnboardExplosion(short obj)
    {
        short debris = FindVacant3dObject();
        if (debris != -1)
        {
            SetObjectsData(debris, ObjectType.Explosion2, obj);
            ref var d = ref Objects[debris];
            d.Scale = unchecked((short)(d.Scale << 2));
            d.Counter = 6;
            d.Velocity = Objects[obj].Velocity;
            short radius = Objects[obj].CollisionRadius;
            var offset = RandomVectors.MakeRandomVectorFixed(Random, (short)(radius >> 2), (short)(radius >> 1));
            d.Position = VectorMath.Add(Objects[obj].Position, offset);
        }
        return debris != -1;
    }

    /// <summary>Each other-side ship (50 % each) turns on <paramref name="obj"/>.</summary>
    /// <remarks>C: call_enemy (0x41EDB0, ship.c).</remarks>
    public void CallEnemy(short obj)
    {
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (Objects[other].Class >= ObjectClass.Ship &&
                Ships[other].SpecialManeuver != SpecialManeuver.Unknown9 &&
                Ships[obj].Side != Ships[other].Side &&
                Random.Below(100) < 50)
            {
                Ships[other].Target = unchecked((sbyte)obj);
            }
        }
    }

    /// <summary>
    /// Damage that got through the armor. Capital ships count damage events against their capacity
    /// (and show onboard explosions); fighters roll a system per event: pilot, ion drive, instant
    /// kill (aft), shields, core damage, a weapon, the weapon count, fuel, the communicator. The
    /// player goes through <see cref="YourInternalDamage"/>. Returns true when destroyed.
    /// </summary>
    /// <remarks>C: internal_damage (0x41EE20, ship.c).</remarks>
    public bool InternalDamage(short attacker, short victim, short damage, short quadrant)
    {
        if (victim == ObjectSlots.Player)
            return YourInternalDamage(attacker, damage, quadrant);
        ref var o = ref Objects[victim];
        ref var ship = ref Ships[victim];
        var typeData = TypeDataOf(victim);
        short damageCapacity = typeData.DamageCapacity;
        short events;
        if (o.Class == ObjectClass.CapitalShip)
        {
            if (ship.Side == Side.Kilrathi)
            {
                events = ScalarMath.MaxShort(1, (short)(damage >> 3));
                o.AccumulatedDamage = unchecked((short)(o.AccumulatedDamage + events));
                if (attacker != -1 && attacker < ObjectSlots.ShipSlotCount && !AnyEnemy(attacker, 10000))
                    CallEnemy(attacker);
            }
            else
            {
                events = ScalarMath.MaxShort(1, (short)(damage / 10));
                o.AccumulatedDamage = unchecked((short)(o.AccumulatedDamage + events));
                if (Random.BelowOrEqual(1000) < 35 && attacker != 0)
                    SendMessage(victim, 4);
            }
            if (o.AccumulatedDamage >= damageCapacity)
                return Explode(attacker, victim);
            OnboardExplosion(victim);
            return false;
        }

        if (ship.Rating != -1)
        {
            events = ScalarMath.MaxShort(1, (short)(damage / 40));
            events = ScalarMath.MinShort(Random.InRange(3, 4), events);
        }
        else
        {
            events = ScalarMath.MaxShort(1, (short)(damage / 6));
        }
        o.AccumulatedDamage = unchecked((short)(o.AccumulatedDamage + events));

        while (events > 0)
        {
            short system = events == 1 && ship.Rating != -1 ? (short)4 : Random.BelowOrEqual(9);
            switch (system)
            {
                case 0:
                    events--;
                    PilotHit(victim);
                    break;
                case 1:
                    if (quadrant == 1)
                    {
                        events--;
                        DamageIonDrive(victim, 1, 3);
                    }
                    break;
                case 2:
                    if (quadrant == 1)
                        return Explode(attacker, victim);
                    break;
                case 3:
                    ship.Shield[ShieldValues.Fore] = 0;
                    ship.Shield[ShieldValues.Aft] = 0;
                    ship.MaximumShield[ShieldValues.Fore] = 0;
                    ship.MaximumShield[ShieldValues.Aft] = 0;
                    break;
                case 4:
                    events--;
                    ship.Damage++;
                    if (ship.Damage > damageCapacity)
                        return Explode(attacker, victim);
                    break;
                case 5:
                    if (quadrant == 0)
                    {
                        short weaponCount = ship.Weapons.Count;
                        if (weaponCount > 0)
                        {
                            events--;
                            RemoveWeapon(victim, Random.BelowOrEqual((short)(weaponCount - 1)));
                        }
                    }
                    break;
                case 6:
                    if (quadrant == 0 && ship.DestroyedWeaponCount < 5)
                    {
                        events--;
                        ship.DestroyedWeaponCount++;
                    }
                    break;
                case 7:
                    if (quadrant == 1)
                    {
                        events--;
                        DrainFuel(victim, unchecked((short)(typeData.Fuel / 4)));
                        if (Random.BelowOrEqual(1) != 0 || ship.Fuel < 0)
                            return Explode(attacker, victim);
                    }
                    break;
                case 8:
                    if (quadrant == 0 && ship.Communicator != -1)
                    {
                        ship.Communicator = -1;
                        events--;
                    }
                    break;
            }
        }
        return false;
    }

    /// <summary>A hit shield generator: the fore maximum drops by a quarter of the type's fore shield;
    /// the aft maximum becomes the new fore maximum minus a quarter of the type's aft shield (sic).</summary>
    /// <remarks>C: revise_shields (0x41F1A0, ship.c).</remarks>
    public void ReviseShields(short obj)
    {
        ref var ship = ref Ships[obj];
        var typeData = TypeDataOf(obj);
        ship.MaximumShield[ShieldValues.Fore] = ScalarMath.MaxShort(0,
            unchecked((short)(ship.MaximumShield[ShieldValues.Fore] - (typeData.ShieldFore >> 2))));
        short maximum = ship.MaximumShield[ShieldValues.Fore];
        ship.MaximumShield[ShieldValues.Aft] = ScalarMath.MaxShort(0,
            unchecked((short)(maximum - (typeData.ShieldAft >> 2))));
    }

    /// <summary>
    /// The player's internal damage: the attacker class selects a group of the system table
    /// (projectile fore/aft, other fore/aft, ram) and the number of events; a severity roll picks
    /// between alternative components; systems that do not apply to the hit side re-roll.
    /// </summary>
    /// <remarks>C: your_internal_damage (0x41F220, ship.c). The original falls off the end without
    /// a return value; false here. An attacker of -1 counts as "other" (class NULL; the original read
    /// before the class table).</remarks>
    public bool YourInternalDamage(short attacker, short damage, short quadrant)
    {
        var attackerClass = (uint)attacker < ObjectSlots.Count ? Objects[attacker].Class : ObjectClass.Null;
        int tableGroup;
        short events;
        if (attackerClass == ObjectClass.Projectile)
        {
            tableGroup = quadrant == 1 ? 2 : 0;
            events = ScalarMath.MaxShort(1, (short)(damage >> 4));
        }
        else if (attackerClass == ObjectClass.Asteroid || attackerClass >= ObjectClass.Ship)
        {
            tableGroup = 4;
            events = ScalarMath.MaxShort(1, (short)(damage >> 7));
        }
        else
        {
            tableGroup = (quadrant == 1 ? 2 : 0) + 1;
            events = ScalarMath.MaxShort(1, (short)(damage >> 5));
        }
        var playerType = Objects[ObjectSlots.Player].Type;
        sbyte severity = unchecked((sbyte)Random.BelowOrEqual(10));
        ref var player = ref Objects[ObjectSlots.Player];
        ref var ship = ref Ships[ObjectSlots.Player];
        player.AccumulatedDamage = unchecked((short)(player.AccumulatedDamage + events));
        if (events > 1)
            Events.PlaceDamageOnCockpit(Random.BelowOrEqual(3));

        while (events > 0)
        {
            events--;
            short system = unchecked((sbyte)DamageTables.PlayerDamageSystemTable[tableGroup * 10 + Random.BelowOrEqual(9)]);
            int component = -1;
            int amount = 0;
            switch (system)
            {
                case 0:
                    if (severity < 4)
                    {
                        PilotHit(0);
                    }
                    else if (severity < 7)
                    {
                        amount = 2;
                        component = 7;
                    }
                    else
                    {
                        amount = 4;
                        component = 6;
                    }
                    break;
                case 1:
                    if (quadrant == 1)
                    {
                        DamageYourComponent(0, 1, 3);
                        DamageIonDrive(0, 1, 3);
                    }
                    else
                    {
                        events++;
                    }
                    break;
                case 2:
                    if (quadrant == 1)
                    {
                        if (Random.BelowOrEqual(3) == 0)
                            return Explode(attacker, 0);
                        if (DamageYourComponent(1, 1, 4) == 4)
                            return Explode(attacker, 0);
                    }
                    else
                    {
                        events++;
                    }
                    break;
                case 3:
                    if (severity > 8)
                    {
                        amount = 2;
                        component = 8;
                    }
                    else
                    {
                        DamageYourComponent(2, 1, 4);
                        ReviseShields(0);
                    }
                    break;
                case 4:
                    ship.Damage++;
                    if (ship.Damage == 1)
                    {
                        PilotHit(0);
                        if (events > 0)
                            events--;
                    }
                    else if (ship.Damage > ObjectTypeTable.Get(playerType).DamageCapacity)
                    {
                        return Explode(attacker, 0);
                    }
                    break;
                case 5:
                    if (quadrant != 0)
                    {
                        events++;
                    }
                    else
                    {
                        short weaponCount = ship.Weapons.Count;
                        if (weaponCount > 0)
                        {
                            RemoveWeapon(0, Random.BelowOrEqual((short)(weaponCount - 1)));
                            Events.ShowComponentHitHudMessage(SimulationHudMessage.WeaponDestroyed, -1);
                        }
                    }
                    break;
                case 6:
                    if (quadrant != 0)
                    {
                        events++;
                    }
                    else if (ship.DestroyedWeaponCount < 5)
                    {
                        ship.DestroyedWeaponCount++;
                        CheckComputerDamage();
                    }
                    break;
                case 7:
                    DrainFuel(0, unchecked((short)(ObjectTypeTable.Get(playerType).Fuel / 4)));
                    if (Random.BelowOrEqual(1) != 0 || ship.Fuel < 0)
                        return Explode(attacker, 0);
                    Events.ShowComponentHitHudMessage(SimulationHudMessage.FuelTanksHit, -1);
                    break;
                case 8:
                    if (quadrant != 0)
                    {
                        events++;
                    }
                    else if (severity > 6)
                    {
                        amount = 4;
                        component = 5;
                    }
                    else
                    {
                        DamageYourComponent(4, 2, 3);
                        if (PlayerComponentDamage[4] > 3)
                            ship.Communicator = -1;
                    }
                    break;
            }
            if (component != -1)
                DamageYourComponent(component, amount, 4);
        }
        return false;
    }

    /// <summary>Damages the targeting computer.</summary>
    /// <remarks>C: check_computer_damage (0x41F5D0, ship.c).</remarks>
    public void CheckComputerDamage() => DamageYourComponent(3, 1, 3);

    /// <summary>Repairs one level of component <paramref name="component"/> if its damage exceeds
    /// <paramref name="minimumDamage"/> and reports it ("%s FIXD").</summary>
    /// <remarks>C: ReportComponentRepaired (0x41F5F0, ship.c).</remarks>
    public bool ReportComponentRepaired(int component, int minimumDamage)
    {
        if (minimumDamage < PlayerComponentDamage[component])
        {
            PlayerComponentDamage[component]--;
            Events.ShowComponentHitHudMessage(SimulationHudMessage.ComponentRepaired, component);
            return true;
        }
        return false;
    }

    /// <summary>Repair systems: a 2/501 chance per frame to fix the shield generator, the ion drive
    /// or one level of core damage near the capacity.</summary>
    /// <remarks>C: repair_internal_damage (0x41F660, ship.c), SDL port version (each case guards its
    /// own component; the Win32 build tested an uninitialised component first).</remarks>
    public void RepairInternalDamage()
    {
        if (Random.BelowOrEqual(500) >= 2)
            return;
        switch (Random.BelowOrEqual(2))
        {
            case 0:
                if (PlayerComponentDamage[2] >= 4)
                    break;
                ReportComponentRepaired(2, 1);
                break;
            case 1:
                if (PlayerComponentDamage[0] >= 4)
                    break;
                if (ReportComponentRepaired(0, 2))
                    DamageIonDrive(0, -1, 3);
                break;
            case 2:
                if (TypeDataOf(0).DamageCapacity - 3 < Ships[0].Damage)
                    Ships[0].Damage--;
                break;
        }
    }

    /// <summary>Throws <paramref name="count"/> pieces of hull debris (girder, tubing, o-ring; 40
    /// frames) from <paramref name="obj"/>.</summary>
    /// <remarks>C: Create_ship_hit_debris (0x41F700, ship.c). An object index outside the table
    /// places the debris at the origin (the original read outside the position table).</remarks>
    public void CreateShipHitDebris(short obj, short count)
    {
        for (short created = 0; created < count; created++)
        {
            short debris = FindVacant3dObject();
            if (debris == -1)
                return;
            SetObjectsData(debris, DamageTables.ShipHitDebrisTypes[Random.BelowOrEqual(2)], -1);
            ref var d = ref Objects[debris];
            d.Counter = 40;
            var offset = RandomVectors.FillFixedVectorWithRandomComponents(Random, 10);
            var origin = (uint)obj < ObjectSlots.Count ? Objects[obj].Position : FixedVector.Zero;
            d.Position = VectorMath.Add(origin, offset);
            d.Velocity = RandomVectors.FillFixedVectorWithRandomComponents(Random, 6);
        }
    }

    /// <summary>Removes a destroyed ship and scatters seven debris pieces of a random set plus eight
    /// debris dust specks, all drifting with half the ship's velocity; may start the next wave.</summary>
    /// <remarks>C: Create_explosion_debris (0x41F800, ship.c).</remarks>
    public void CreateExplosionDebris(short obj)
    {
        RemoveObject(obj);
        CheckNextWave();
        int set = Random.BelowOrEqual(3);
        for (int index = 0; index < 7; index++)
        {
            short debris = FindVacant3dObject();
            if (debris == -1)
                break;
            SetObjectsData(debris, DamageTables.ExplosionDebris(set)[index], -1);
            ref var d = ref Objects[debris];
            d.Counter = 40;
            var vector = RandomVectors.FillFixedVectorWithRandomComponents(Random, 50);
            d.Position = VectorMath.Add(Objects[obj].Position, vector);
            d.Velocity = RandomVectors.FillFixedVectorWithRandomComponents(Random, 25);
            vector = VectorMath.Divide(Objects[obj].Velocity, 0x200);
            d.Velocity = VectorMath.Add(vector, d.Velocity);
        }
        for (int index = 0; index < 8; index++)
        {
            short debris = FindVacant3dObject();
            if (debris == -1)
                break;
            ref var d = ref Objects[debris];
            var vector = RandomVectors.FillFixedVectorWithRandomComponents(Random, 50);
            d.Position = VectorMath.Add(Objects[obj].Position, vector);
            d.Velocity = RandomVectors.FillFixedVectorWithRandomComponents(Random, 25);
            vector = VectorMath.Divide(Objects[obj].Velocity, 0x200);
            d.Velocity = VectorMath.Add(vector, d.Velocity);
            d.ScreenAngle = (short)(Random.BelowOrEqual(3) + 0x10);
            d.Counter = 40;
            d.Class = ObjectClass.Dust;
            d.Type = ObjectType.DebrisDust;
        }
    }

    /// <summary>Adds the points of mission event <paramref name="event"/> to the mission score; the
    /// player's points also count for medals and ten times for the arcade score.</summary>
    /// <remarks>C: affect_mission_score (0x41F9E0, ship.c).</remarks>
    public void AffectMissionScore(short pilot, int @event, short amount)
    {
        short score = @event switch
        {
            0 => amount,
            1 => 7,
            2 => 10,
            3 or 4 => 15,
            5 or 6 => 25,
            7 => 50,
            8 => 75,
            9 or 10 or 11 => 25,
            12 => unchecked((short)(amount * 2)),
            _ => amount,
        };
        Campaign.MissionScore = unchecked((short)(Campaign.MissionScore + score));
        if (pilot == ObjectSlots.Player)
        {
            MissionMedalScore = unchecked((short)(MissionMedalScore + score));
            ArcadeScore += score * 10;
        }
    }

    /// <summary>Scores a Kilrathi kill by type (Salthi 7 ... Sivar/starbase 75 points).</summary>
    /// <remarks>C: score_for_kill (0x41FA90, ship.c). For a Kilrathi ship of an unlisted type the
    /// original passes an uninitialised event; the port uses the default case (amount -1).</remarks>
    public void ScoreForKill(short pilot, short victim)
    {
        if (Ships[victim].Side != Side.Kilrathi)
            return;
        int @event = Objects[victim].Type switch
        {
            ObjectType.Salthi => 1,
            ObjectType.Dralthi or ObjectType.Krant => 2,
            ObjectType.Gratha or ObjectType.Jalthi => 3,
            ObjectType.Spikeri or ObjectType.Ralari => 6,
            ObjectType.Dorkir or ObjectType.Lumbari => 4,
            ObjectType.Fralthi or ObjectType.Snakeir => 7,
            ObjectType.Sivar or ObjectType.KilrathiBase => 8,
            _ => -1,
        };
        AffectMissionScore(pilot, @event, -1);
    }

    /// <summary>Bookkeeping of a kill: music change, the killer's message, score and kill counters
    /// when the victim was on the other side.</summary>
    /// <remarks>C: analyze_kill (0x41FB40, ship.c). A creator outside the ship slots (an unowned
    /// asteroid, hazard mine or rock chunk) has no side; the original read past the side table, the
    /// port uses <see cref="Side.Neutral"/>.</remarks>
    public void AnalyzeKill(short attacker, short victim)
    {
        bool enemy = Ships[victim].Side != SideOf(attacker);
        Events.NewSpaceMusicChanges(attacker, victim);
        if (enemy)
        {
            SendMessage(attacker, 5);
            ScoreForKill(attacker, victim);
            if (attacker == ObjectSlots.Player)
                PlayerKillCount++;
            else if (YourWingman == attacker)
                WingmanKillCount++;
        }
    }

    /// <summary>The big explosion of a fighter (EXPLOSION1 scaled by the ship's scale, moving with
    /// it); with no free slot the ship's own slot becomes the explosion.</summary>
    /// <remarks>C: ShipExplosion (0x41FBC0, ship.c).</remarks>
    public short ShipExplosion(short obj)
    {
        ushort originalScale = (ushort)Objects[obj].Scale;
        short explosion = FindVacant3dObject();
        if (explosion == -1)
        {
            if (Objects[obj].Class == ObjectClass.CapitalShip)
                Objects[obj].Shape = ShapeRef.None;
            Ships[obj].CapitalShipViewFrame = -1;
            explosion = obj;
        }
        else
        {
            CopyFrame(obj, explosion);
            Objects[explosion].Position = Objects[obj].Position;
            Objects[explosion].Velocity = Objects[obj].Velocity;
            Objects[explosion].Owner = unchecked((sbyte)obj);
        }
        SetObjectsData(explosion, ObjectType.Explosion1, Objects[explosion].Owner);
        Objects[explosion].Scale = unchecked((short)((ushort)Objects[explosion].Scale * originalScale >> 8));
        return explosion;
    }

    /// <summary>
    /// Turns <paramref name="obj"/> into its explosion. Ships: death message, personality/ace
    /// bookkeeping, wingman loss, dying state (special 9, 8 frames; capital ships count
    /// <c>damageCapacity/4 + 8</c> frames with onboard explosions), mission record state 3, the
    /// fighter explosion. Other objects become EXPLOSION2 (turret shells and asteroids EXPLOSION0).
    /// Everything but capital ships sends a shock wave with the explosion damage of its (new) type.
    /// </summary>
    /// <remarks>C: Explosion (0x41FCD0, ship.c). As in the original, <c>find_ship_index</c> is called
    /// with the slot number, and the asteroid scale 0x380 is never applied (the class test follows
    /// set_objects_data).</remarks>
    public short Explosion(short obj)
    {
        var objectClass = Objects[obj].Class;
        short explosion = obj;
        if (objectClass >= ObjectClass.Ship)
        {
            short missionShip = -1;
            if (objectClass == ObjectClass.CapitalShip)
                missionShip = FindShipIndex(obj);
            if (obj < ObjectSlots.ShipSlotCount &&
                (Ships[obj].Rating != -1 ||
                 (missionShip != -1 && Ships[obj].MissionShip == missionShip) ||
                 Random.Below(100) <= 2))
            {
                SendMessage(obj, 7);
            }
            sbyte rating = Ships[obj].Rating;
            if (rating != -1 && rating != RatingPlayerPersona)
                PersonalityKilled(rating);
            if (YourWingman == obj)
            {
                WingmanKilledThisMission = true;
                for (missionShip = 0; missionShip < ObjectSlots.ShipSlotCount; missionShip++)
                {
                    if (Ships[missionShip].Rating > RatingPlayerPersona)
                        break;
                }
                if (missionShip < ObjectSlots.ShipSlotCount)
                    SendMessage(missionShip, 5);
                YourWingman = -1;
            }
            SetSpecial(obj, SpecialManeuver.Unknown9);
            Objects[obj].Counter = 8;
            SetMissionShipState(Ships[obj].MissionIndex, 3);
            if (Objects[obj].Class == ObjectClass.CapitalShip)
            {
                for (int count = 4; count != 0; count--)
                    OnboardExplosion(obj);
                Objects[obj].Counter = unchecked((short)((TypeDataOf(obj).DamageCapacity >> 2) + 8));
            }
            else
            {
                explosion = ShipExplosion(obj);
            }
        }
        else
        {
            var explosionType = ObjectType.Explosion2;
            if (Objects[obj].Type == ObjectType.Turret || objectClass == ObjectClass.Asteroid)
                explosionType = ObjectType.Explosion0;
            SetObjectsData(obj, explosionType, Objects[obj].Owner);
            if (Objects[obj].Class == ObjectClass.Asteroid)
                Objects[obj].Scale = 0x380;
        }
        if (objectClass != ObjectClass.CapitalShip)
            ExplosionShockWave(obj, TypeDataOf(obj).ExplosionDamage);
        if (Objects[obj].ScreenX != ObjectSlots.NotVisible)
            Events.PlaySoundEffect(4, obj);
        return explosion;
    }

    /// <summary>The object that ultimately caused <paramref name="obj"/>: follows the owner chain to an
    /// object that owns itself or has no owner. -1 for -1.</summary>
    /// <remarks>C: the_creator (0x41FEB0, ship.c). The walk is limited to 64 steps (an owner cycle
    /// would hang the original; none is created by the game).</remarks>
    public short TheCreator(short obj)
    {
        for (int step = 0; step < ObjectSlots.Count; step++)
        {
            if (obj == -1)
                return -1;
            sbyte owner = Objects[obj].Owner;
            if (obj == owner || owner == -1)
                return obj;
            obj = owner;
        }
        return obj;
    }

    /// <summary>
    /// Blast of <paramref name="blastDamage"/> at <paramref name="obj"/>: every other ship within
    /// 1000 units of its hull takes <c>blast / d / d</c> (d = 40 beyond 750, 30 beyond 500, 8..25 closer;
    /// the player three quarters, at least 1), is pushed away and damaged when that exceeds 1.
    /// </summary>
    /// <remarks>C: explosion_shock_wave (0x41FEE0, ship.c).</remarks>
    public void ExplosionShockWave(short obj, short blastDamage)
    {
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (other == obj || Objects[other].Class < ObjectClass.Ship)
                continue;
            var delta = VectorMath.Delta(Objects[obj].Position, Objects[other].Position);
            short distance = FixedMath.ToShortSaturating(delta.Magnitude());
            distance = ScalarMath.MaxShort(0, unchecked((short)(distance - Objects[other].CollisionRadius)));
            short damage = ShockWaveDamage(distance, blastDamage, other == ObjectSlots.Player);
            if (damage > 1)
            {
                VectorMath.Normalize(ref delta);
                var force = VectorMath.Scale(delta, damage << 8);
                ApplyForceToObjectsCenter(force, other);
                short attacker = TheCreator(obj);
                InflictDamage(attacker, other, ScalarMath.MinShort(100, damage), delta);
            }
        }
    }

    /// <summary>Blast damage at <paramref name="distance"/> units from the hull: 0 beyond 1000, else
    /// <c>blast / d / d</c> with d = 40 beyond 750, 30 beyond 500 and 8..25 closer; the player takes
    /// three quarters (at least 1).</summary>
    /// <remarks>C: the damage part of explosion_shock_wave (0x41FEE0, ship.c).</remarks>
    public static short ShockWaveDamage(short distance, short blastDamage, bool player)
    {
        if (distance > 1000)
            return 0;
        short divisor;
        if (distance > 750)
        {
            divisor = 40;
        }
        else
        {
            divisor = 30;
            if (distance <= 500)
                divisor = ScalarMath.FindRatio(0, 500, distance, 8, 25);
        }
        short damage = unchecked((short)(blastDamage / divisor / divisor));
        if (player)
            damage = ScalarMath.MaxShort(1, unchecked((short)(damage * 3 >> 2)));
        return damage;
    }

    /// <summary>
    /// Destroys <paramref name="victim"/>: named pilots survive half the time (a Kilrathi ace with flag
    /// 0x20 escapes once instead), the player ends the flight (arcade state 4), anything else scores
    /// the kill for the attacker's creator and explodes. Returns true when the victim exploded.
    /// </summary>
    /// <remarks>C: explode (0x420040, ship.c), with the SDL port's guard on the ship-only state.</remarks>
    public bool Explode(short attacker, short victim)
    {
        if (victim >= 0 && victim < ObjectSlots.ShipSlotCount)
        {
            sbyte rating = Ships[victim].Rating;
            if (rating != -1 && rating != RatingPlayerPersona)
            {
                if (rating > RatingPlayerPersona)
                {
                    short ace = (short)(rating - RatingFirstKilrathiAce);
                    if (AceStatus(ace, 0x20))
                    {
                        UnflagAce(ace, 0x20);
                        Ships[victim].Stress = -25;
                        ResetManeuver(victim, ShipManeuver.OutaHere);
                        Ships[victim].Damage = (sbyte)(Ships[victim].Damage / 2);
                        SendMessage(victim, 6);
                        return false;
                    }
                    if (Random.BelowOrEqual(1) == 0)
                        return false;
                }
                else if (Random.BelowOrEqual(1) == 0)
                {
                    return false;
                }
            }
            if (Ships[victim].SpecialManeuver == SpecialManeuver.Unknown9 && Objects[victim].Class >= ObjectClass.Ship)
                return false;
        }

        if (victim == ObjectSlots.Player)
        {
            if (!PlayerVulnerable)
                return false;
            PlayerDestroyed = true;
            ArcadeState = 4;
            return true;
        }

        if (ExternalViewShip == victim)
            ExternalViewShip = -1;
        short creator = TheCreator(attacker);
        if (creator != -1 && Objects[victim].Class >= ObjectClass.Ship)
            AnalyzeKill(creator, victim);
        Explosion(victim);
        return true;
    }

    /// <summary>A named Confed pilot (0..7) died: record the mission of death, promotion score -1.
    /// A Kilrathi ace (9..12) died: ace flags, promotion score +1, mission score +25.</summary>
    /// <remarks>C: personality_killed (0x42AC50, hudmsg.c).</remarks>
    public void PersonalityKilled(short personality)
    {
        if (personality < 8)
        {
            Campaign.SetPersonalityDeathMission(personality, Campaign.CurrentMission + Campaign.CurrentSeries * 4);
            Campaign.PromotionScore = ScalarMath.MaxShort(0, unchecked((short)(Campaign.PromotionScore - 1)));
            return;
        }
        KillAce((short)(personality - RatingFirstKilrathiAce));
        Campaign.PromotionScore = unchecked((short)(Campaign.PromotionScore + 1));
        Campaign.MissionScore = unchecked((short)(Campaign.MissionScore + 25));
    }

    /// <summary>
    /// Queues comm message <paramref name="message"/> from <paramref name="obj"/> (shown later by the
    /// cockpit's <c>npc_communication</c>): only named pilots, the Tiger's Claw, the player's escort
    /// charge and Kilrathi ships talk; nothing in the training simulator or canned scenes; the
    /// wingman stays silent under radio silence.
    /// </summary>
    /// <remarks>C: send_message (0x417420, cockpt.c).</remarks>
    public void SendMessage(short obj, sbyte message)
    {
        if ((uint)obj >= ObjectSlots.Count || TrainSimActive || Objects[obj].Class == ObjectClass.Null || CannedSceneMode != 0)
            return;
        if (YourWingman != -1 && YourWingman == obj && RadioSilence)
        {
            Ships[obj].WingmanMessageState = -1;
            return;
        }
        if (obj < ObjectSlots.ShipSlotCount && Objects[obj].Class >= ObjectClass.Ship)
        {
            ref var ship = ref Ships[obj];
            if (ship.Rating != -1)
            {
                ship.WingmanMessageState = message;
                return;
            }
            if (Objects[obj].Type == ObjectType.TigersClaw || ship.MissionIndex == Ships[ObjectSlots.Player].MissionShip)
                ship.WingmanMessageState = message;
            else if (ship.Side == Side.Kilrathi)
                ship.WingmanMessageState = message;
        }
    }

    /// <summary>Whether an other-side, not dying ship is closer than <paramref name="range"/> to
    /// <paramref name="obj"/> (the first one found is left in <see cref="TargetShip"/>/<see cref="TargetRange"/>).</summary>
    /// <remarks>C: any_enemy (0x423070, logic.c).</remarks>
    public bool AnyEnemy(short obj, short range)
    {
        TargetShip = -1;
        for (short other = 0; other < ObjectSlots.ShipSlotCount; other++)
        {
            if (Objects[other].Class >= ObjectClass.Ship &&
                Ships[other].SpecialManeuver != SpecialManeuver.Unknown9 &&
                Ships[obj].Side != Ships[other].Side)
            {
                TargetRange = DistanceFromObject(obj, other);
                if (TargetRange < range)
                {
                    TargetShip = other;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Whether a Kilrathi ship is within <paramref name="range"/> units of <paramref name="obj"/>.</summary>
    /// <remarks>C: kilrathi_near (0x414300, cockpt.c).</remarks>
    public bool KilrathiNear(short obj, short range)
    {
        for (short ship = 0; ship < ObjectSlots.ShipSlotCount; ship++)
        {
            if (Objects[ship].Class >= ObjectClass.Ship &&
                Ships[ship].Side == Side.Kilrathi &&
                VectorMath.IsPointWithinRange(Objects[obj].Position, Objects[ship].Position, range))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Malfunction roll of player component <paramref name="component"/>: true with probability
    /// <c>damage² / 16</c> (one random number).</summary>
    /// <remarks>C: malf (0x414AF0, cockpt.c).</remarks>
    public bool Malf(int component)
    {
        int damage = PlayerComponentDamage[component];
        return (ushort)Random.InRange(0, 15) < damage * damage;
    }

    /// <summary>Adds <paramref name="amount"/> damage levels (capped at <paramref name="maximum"/>) to a
    /// player component; a malfunctioning computer (3) breaks both VDUs; the hit is reported while the
    /// left VDU shows weapons or damage. Returns the new level.</summary>
    /// <remarks>C: damage_your_component (0x414BF0, cockpt.c).</remarks>
    public short DamageYourComponent(int component, int amount, int maximum)
    {
        PlayerComponentDamage[component] = (sbyte)ScalarMath.MinShort(
            unchecked((short)(PlayerComponentDamage[component] + amount)), (short)maximum);
        if (Malf(component) && component == 3)
        {
            Events.VduMalfunction(0, 0x18);
            Events.VduMalfunction(1, 0x18);
        }
        int leftVduMode = Cockpit.GetVduMode(0);
        if (leftVduMode == 2 || leftVduMode == 1)
            Events.ShowComponentHitHudMessage(SimulationHudMessage.ComponentHit, component);
        return PlayerComponentDamage[component];
    }

    /// <summary>Damage level 0..3 of the player's ship for the landing scene: armor losses, core
    /// damage and accumulated damage events.</summary>
    /// <remarks>C: calculate_damage_level (0x42A520, hudmsg.c).</remarks>
    public int CalculateDamageLevel()
    {
        var typeData = TypeDataOf(0);
        ref var ship = ref Ships[ObjectSlots.Player];
        short damage = unchecked((short)((typeData.ArmorLeft - ship.Armor[ArmorValues.Left]) * 4 / typeData.ArmorLeft));
        damage = unchecked((short)(damage + (typeData.ArmorRear - ship.Armor[ArmorValues.Rear]) * 4 / typeData.ArmorRear));
        damage = unchecked((short)(damage + (typeData.ArmorRight - ship.Armor[ArmorValues.Right]) * 4 / typeData.ArmorRight));
        damage = unchecked((short)(damage + (typeData.ArmorFront - ship.Armor[ArmorValues.Front]) * 4 / typeData.ArmorFront));
        damage = unchecked((short)(ship.Damage * 30 / typeData.DamageCapacity + damage * 2));
        damage = unchecked((short)(damage + Objects[ObjectSlots.Player].AccumulatedDamage * 5));
        if (damage < 5)
            return 0;
        if (damage < 40)
            return 1;
        if (damage < 70)
            return 2;
        return 3;
    }
}
