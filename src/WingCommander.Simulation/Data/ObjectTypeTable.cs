using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation.Data;

/// <summary>
/// The 58 object type records compiled into the executable (Kilrathi Saga values, ADR-007).
/// Entries are transcribed in the field order of the C initializers so they can be checked
/// line by line against <c>globals.c</c>.
/// </summary>
/// <remarks>C: aObjectTypeData (0x00466458, globals.c), aszObjectTypeDisplayNames (0x004684d4).</remarks>
public static class ObjectTypeTable
{
    /// <summary>Number of table entries (OBJECT_TYPE_TYPES).</summary>
    public const int Count = 58;

    private const ObjectClass Ship = ObjectClass.Ship;
    private const ObjectClass Capital = ObjectClass.CapitalShip;

    private const ObjectType Laser = ObjectType.LaserCannon;
    private const ObjectType Neutron = ObjectType.NeutronParticleGun;
    private const ObjectType MassDriver = ObjectType.MassDriverCannon;
    private const ObjectType Turret = ObjectType.Turret;
    private const ObjectType DumbFire = ObjectType.DumbFireMissile;
    private const ObjectType HeatSeeker = ObjectType.HeatSeekingMissile;
    private const ObjectType FriendOrFoe = ObjectType.FriendOrFoeMissile;
    private const ObjectType ImageRec = ObjectType.ImageRecognitionMissile;
    private const ObjectType Mine = ObjectType.SpaceMine;

    private static readonly ObjectTypeData[] Entries = Build();

    /// <summary>All entries, indexed by <see cref="ObjectType"/> 0..57.</summary>
    public static IReadOnlyList<ObjectTypeData> All => Entries;

    /// <summary>The record of <paramref name="type"/> (0..57).</summary>
    public static ObjectTypeData Get(ObjectType type) => Entries[(int)type];

    private static ObjectTypeData[] Build() =>
    [
        // 0: Hornet
        T("Hornet", Ship, 100, 125, 1024, 5, 3392, 3, 5, 4000, 42, 30, null, 819, 8, 9, 8, 900,
            L(S(Laser, 0, 0), S(Laser, 1, 0), S(DumbFire, 2, 0), S(DumbFire, 3, 1), S(HeatSeeker, 4, 1)),
            40, 40, 45, 40, 30, 30),
        // 1: Rapier
        T("Rapier", Ship, 120, 135, 1024, 3, -12144, 3, 6, 6000, 45, 25, null, 1075, 10, 10, 10, 1000,
            L(S(Laser, 14, 1), S(Laser, 18, 1), S(Neutron, 12, 0), S(Neutron, 20, 0), S(ImageRec, 16, 1),
              S(FriendOrFoe, 15, 0), S(FriendOrFoe, 17, 1), S(DumbFire, 13, 1), S(DumbFire, 19, 1)),
            80, 75, 60, 55, 50, 50),
        // 2: Scimitar
        T("Scimitar", Ship, 165, 160, 1152, 6, 17856, 4, 7, 6000, 36, 15, null, 614, 6, 6, 7, 1300,
            L(S(MassDriver, 6, 0), S(MassDriver, 11, 0), S(DumbFire, 5, 0), S(DumbFire, 10, 1),
              S(HeatSeeker, 7, 1), S(HeatSeeker, 8, 1), S(HeatSeeker, 9, 1)),
            60, 50, 85, 80, 65, 65),
        // 3: Raptor
        T("Raptor", Ship, 180, 200, 1152, 3, -27680, 4, 8, 8000, 40, 25, null, 588, 6, 5, 6, 2000,
            L(S(Neutron, 23, 0), S(Neutron, 28, 0), S(MassDriver, 21, 1), S(MassDriver, 30, 1),
              S(HeatSeeker, 22, 0), S(HeatSeeker, 29, 1), S(ImageRec, 24, 1), S(ImageRec, 27, 1),
              S(FriendOrFoe, 26, 1), S(Mine, 25, 1)),
            70, 70, 100, 90, 80, 80),
        // 4: Venture
        T("Venture", Capital, 240, 400, 1024, 5, 3392, 3, 70, 20000, 25, 10, null, 256, 3, 3, 3, 4000,
            L(S(Turret, 51, 0), S(Turret, 50, 0)),
            150, 150, 110, 100, 100, 110),
        // 5: Dilligent
        T("Dilligent", Capital, 240, 400, 1024, 4, 3392, 3, 60, 20000, 15, 10, null, 128, 2, 2, 2, 10000,
            L(S(Turret, 54, 0)),
            120, 120, 80, 80, 60, 60),
        // 6: Drayman
        T("Drayman", Capital, 240, 400, 1024, 4, 3392, 3, 60, 10000, 15, 10, null, 128, 2, 2, 2, 20000,
            L(S(Turret, 54, 0)),
            120, 120, 80, 80, 60, 60),
        // 7: Exeter
        T("Exeter", Capital, 500, 5000, 2048, 2, 3392, 3, 200, 30000, 20, 15, null, 256, 2, 2, 2, 20000,
            L(S(ImageRec, 47, 0), S(Turret, 48, 0), S(Turret, 49, 0), S(Turret, 50, 0), S(Turret, 51, 0)),
            240, 240, 220, 200, 200, 200),
        // 8: Tiger's Claw
        T("Tiger's Claw", Capital, 700, 10000, 4096, 1, 3392, 3, 560, 30000, 0, 0, null, 256, 1, 1, 1, 20000,
            L(S(Turret, 33, 0), S(Turret, 34, 0), S(Turret, 35, 0), S(Turret, 36, 0),
              S(Turret, 37, 0), S(Turret, 38, 0), S(Turret, 39, 0), S(Turret, 40, 0)),
            300, 300, 240, 200, 250, 250),
        // 9: Salthi
        T("Salthi", Ship, 120, 120, 1024, 10, 3392, 3, 5, 4000, 48, 30, null, 972, 14, 12, 22, 1000,
            L(S(Laser, 0, 0), S(Laser, 1, 0), S(DumbFire, 31, 1)),
            35, 35, 30, 20, 15, 15),
        // 10: Dralthi
        T("Dralthi", Ship, 160, 140, 1024, 6, 3392, 3, 7, 6000, 40, 23, null, 768, 10, 14, 10, 1200,
            L(S(Laser, 0, 0), S(Laser, 1, 0), S(Mine, 32, 0), S(Mine, 32, 1), S(Mine, 32, 1),
              S(HeatSeeker, 31, 0), S(HeatSeeker, 31, 1)),
            50, 50, 45, 35, 30, 30),
        // 11: Krant
        T("Krant", Ship, 140, 126, 1024, 5, 3392, 3, 6, 6000, 36, 20, null, 716, 7, 10, 7, 1200,
            L(S(Laser, 0, 0), S(Laser, 1, 0), S(FriendOrFoe, 31, 1), S(HeatSeeker, 31, 0),
              S(HeatSeeker, 31, 1), S(HeatSeeker, 31, 1)),
            80, 80, 90, 100, 80, 80),
        // 12: Gratha
        T("Gratha", Ship, 140, 126, 1024, 4, 3392, 3, 7, 7000, 32, 20, null, 614, 6, 6, 14, 1400,
            L(S(Laser, 0, 0), S(Laser, 1, 0), S(MassDriver, 21, 0), S(MassDriver, 30, 0), S(ImageRec, 31, 1),
              S(HeatSeeker, 31, 0), S(HeatSeeker, 31, 1), S(Mine, 32, 1), S(Mine, 32, 1), S(Mine, 32, 1)),
            100, 95, 140, 120, 100, 100),
        // 13: Jalthi
        T("Jalthi", Ship, 160, 180, 1024, 7, 3392, 3, 7, 8000, 28, 20, null, 512, 5, 5, 5, 1600,
            L(S(Neutron, 41, 0), S(Neutron, 44, 0), S(Laser, 42, 0), S(Laser, 43, 0), S(Laser, 45, 0),
              S(Laser, 46, 0), S(FriendOrFoe, 31, 1), S(HeatSeeker, 31, 0)),
            160, 160, 200, 100, 170, 170),
        // 14: Spikeri
        T("Spikeri", Capital, 200, 200, 1536, 4, 3392, 3, 45, 12000, 15, 10, null, 460, 4, 4, 4, 4000,
            L(),
            70, 70, 80, 80, 60, 60),
        // 15: Dorkir
        T("Dorkir", Capital, 260, 400, 2048, 5, 3392, 3, 35, 24000, 15, 10, null, 204, 2, 2, 2, 5000,
            L(S(Turret, 54, 0), S(Mine, 55, 1), S(Mine, 55, 1), S(Mine, 55, 1)),
            170, 100, 90, 60, 90, 90),
        // 16: Lumbari
        T("Lumbari", Capital, 260, 400, 2048, 5, 3392, 3, 35, 16000, 15, 10, null, 204, 2, 2, 2, 5000,
            L(S(Turret, 54, 0), S(Mine, 55, 1), S(Mine, 55, 1), S(Mine, 55, 1)),
            70, 70, 80, 80, 60, 60),
        // 17: Ralari
        T("Ralari", Capital, 325, 3000, 4096, 3, 3392, 3, 90, 20000, 15, 10, null, 256, 2, 2, 2, 18000,
            L(S(ImageRec, 47, 0), S(Turret, 48, 0), S(Turret, 49, 0), S(Turret, 50, 0), S(Turret, 51, 0),
              S(Turret, 52, 0), S(Turret, 53, 0)),
            200, 120, 200, 90, 180, 180),
        // 18: Fralthi
        T("Fralthi", Capital, 450, 10000, 4096, 2, 3392, 3, 110, 30000, 15, 10, null, 256, 2, 2, 2, 10000,
            L(S(ImageRec, 47, 0), S(ImageRec, 47, 0), S(Turret, 48, 0), S(Turret, 49, 0), S(Turret, 50, 0),
              S(Turret, 51, 0), S(Turret, 52, 0), S(Turret, 53, 0)),
            270, 170, 280, 140, 260, 260),
        // 19: Snakeir
        T("Snakeir", Capital, 600, 10000, 2048, 1, 3392, 3, 320, 30000, 15, 10, null, 204, 1, 1, 1, 10000,
            L(),
            70, 70, 80, 80, 60, 60),
        // 20: Sivar
        T("Sivar", Capital, 400, 12000, 4096, 1, 3392, 3, 200, 32000, 20, 15, null, 179, 1, 1, 1, 15000,
            L(S(ImageRec, 47, 0), S(ImageRec, 47, 0), S(Turret, 48, 0), S(Turret, 49, 0), S(Turret, 50, 0),
              S(Turret, 51, 0), S(Turret, 52, 0), S(Turret, 53, 0)),
            270, 170, 280, 140, 260, 260),
        // 21: Kilrathi base ("Star post")
        T("Star post", Capital, 400, 20000, 2048, 4, 3392, 3, 120, 32000, 0, 0, null, 0, 0, 0, 0, 10000,
            L(S(Turret, 33, 0), S(Turret, 36, 0), S(Turret, 37, 0), S(Turret, 40, 0),
              S(FriendOrFoe, 33, 0), S(FriendOrFoe, 36, 0), S(FriendOrFoe, 37, 0), S(FriendOrFoe, 40, 0)),
            200, 200, 180, 180, 180, 180),
        // 22: asteroid field, 23: mine field (zero records; null display name)
        Zero(),
        Zero(),
        // 24: laser cannon bolt
        T("Laser cannon", ObjectClass.Projectile, 10, 0, 512, 7, 30, 0, 25, 0, 160, 0, null, 0, 0, 0, 0, 0, L(), 0, 0, 0, 0, 0, 0),
        // 25: neutron particle gun
        T("Neutron gun", ObjectClass.Projectile, 10, 1, 832, 14, 20, 0, 40, 0, 140, 0, null, 0, 0, 0, 0, 0, L(), 0, 0, 0, 0, 0, 0),
        // 26: mass driver cannon
        T("Mass driver", ObjectClass.Projectile, 10, 0, 512, 9, 25, 0, 30, 0, 120, 0, null, 0, 0, 0, 0, 0, L(), 0, 0, 0, 0, 0, 0),
        // 27: turret bolt (name pointer 0xec lands on padding: empty); resources alias the laser
        T("", ObjectClass.Projectile, 15, 0, 832, 15, 40, 0, 50, 1000, 150, 0, null, 0, 0, 0, 0, 0, L(), 0, 0, 0, 0, 0, 0),
        // 28..32: missiles (32 torpedo: name pointer 0x11c lands on padding)
        T("Dart DF", ObjectClass.Missile, 20, 5, 768, 500, 120, 0, 4, 14500, 130, 0, null, 1433, 15, 15, 15, 100, L(), 0, 0, 0, 0, 0, 0),
        T("Javelin HS", ObjectClass.Missile, 20, 5, 768, 400, 140, 0, 4, 13500, 110, 0, null, 1689, 11, 11, 11, 100, L(), 0, 0, 0, 0, 0, 0),
        T("Pilum FF", ObjectClass.Missile, 20, 5, 768, 400, 160, 0, 4, 10500, 90, 0, null, 1689, 11, 11, 11, 100, L(), 0, 0, 0, 0, 0, 0),
        T("Spiculum IR", ObjectClass.Missile, 20, 5, 768, 400, 110, 0, 4, 11500, 110, 0, null, 1689, 11, 11, 11, 100, L(), 0, 0, 0, 0, 0, 0),
        T("", ObjectClass.Missile, 25, 10, 768, 400, 200, 0, 4, 30000, 50, 0, null, 1280, 10, 10, 10, 100, L(), 0, 0, 0, 0, 0, 0),
        // 33: space mine
        T("Porcupine", ObjectClass.Mine, 20, 5, 768, 110, 120, 0, 4, 10000, 20, 20, AnimationScripts.Mine, 0, 0, 2, 2, 0, L(), 0, 0, 0, 0, 0, 0),
        // 34..39: asteroids (display names beyond the string table: empty)
        Effect(ObjectClass.Asteroid, 100, 300, 640, -1, 0, AnimationScripts.AsteroidForward, 1, 13),
        Effect(ObjectClass.Asteroid, 100, 300, 640, -1, 0, AnimationScripts.AsteroidShortForward, 1, 12),
        Effect(ObjectClass.Asteroid, 100, 300, 640, -1, 0, AnimationScripts.AsteroidReverse, 1, 13),
        Effect(ObjectClass.Asteroid, 100, 300, 640, -1, 0, AnimationScripts.AsteroidShortReverse, 1, 12),
        Effect(ObjectClass.Asteroid, 100, 300, 640, -1, 0, AnimationScripts.AsteroidForward, 2, 13),
        Effect(ObjectClass.Asteroid, 100, 300, 640, -1, 0, AnimationScripts.AsteroidShortForward, 2, 12),
        // 40: rock chunk
        Effect(ObjectClass.Debris, 10, 4, 192, -1, 0, AnimationScripts.AsteroidForward, 2, 13),
        // 41..47: debris girder, tubing, metal sheet, wing, glass, o-ring, pipe
        Effect(ObjectClass.Debris, 10, 1, 2048, 0, 0, AnimationScripts.Girder, 2, 5),
        Effect(ObjectClass.Debris, 10, 1, 2048, 0, 0, AnimationScripts.Tubing, 2, 5),
        Effect(ObjectClass.Debris, 20, 2, 1280, 0, 0, AnimationScripts.MetalSheet, 1, 11),
        Effect(ObjectClass.Debris, 20, 2, 1280, 0, 0, AnimationScripts.Wing, 1, 15),
        Effect(ObjectClass.Debris, 20, 2, 768, 0, 0, AnimationScripts.Glass, 1, 15),
        Effect(ObjectClass.Debris, 2, 1, 1792, 0, 0, AnimationScripts.ORing, 1, 5),
        Effect(ObjectClass.Debris, 6, 1, 1536, 0, 0, AnimationScripts.Pipe, 1, 5),
        // 48..50: explosions
        Effect(ObjectClass.Explosion, 0, 0, 768, -1, 6000, AnimationScripts.Explosion0, 1, 0),
        Effect(ObjectClass.Explosion, 0, 0, 256, -1, 6000, AnimationScripts.Explosion1, 1, 0),
        Effect(ObjectClass.Explosion, 0, 0, 256, -1, 6000, AnimationScripts.Explosion2, 1, 0),
        // 51..54: laser spark, red spark, blue spark, spark trail
        Effect(ObjectClass.Explosion, 0, 0, 256, 0, 0, AnimationScripts.LaserSpark, 1, 0),
        Effect(ObjectClass.Explosion, 0, 0, 256, 0, 0, AnimationScripts.RedSpark, 2, 3),
        Effect(ObjectClass.Explosion, 0, 0, 256, 0, 0, AnimationScripts.BlueSpark, 2, 3),
        Effect(ObjectClass.Explosion, 1, 1, 256, 0, 0, AnimationScripts.SparkTrail, 2, 3),
        // 55: thrusters
        Effect(ObjectClass.FixedObject, 0, 0, 256, 0, 0, null, 0, 0),
        // 56: ejected pilot
        Effect(ObjectClass.Debris, 6, 1, 512, 0, 0, AnimationScripts.EjectedPilot, 1, 12),
        // 57: hyperspace jump flash
        Effect(ObjectClass.Explosion, 0, 0, 1024, 0, 0, AnimationScripts.HyperspaceJumpFlash, 1, 0),
    ];

    private static ObjectTypeData T(
        string name, ObjectClass objectClass, short collisionRadius, short radarRadius, short scale,
        short animationDelay, short lifetime, short weaponDamage, short damageCapacity, short explosionDamage,
        short maximumVelocity, short cruiseVelocity, uint[]? animation, int acceleration, short pitchRate,
        short yawRate, short rollRate, short afterburnerVelocity, WeaponLoadout loadout, short shieldFore,
        short shieldAft, short armorFront, short armorRear, short armorLeft, short armorRight) =>
        new(name, objectClass, collisionRadius, radarRadius, scale, animationDelay, lifetime, weaponDamage,
            damageCapacity, explosionDamage, maximumVelocity, cruiseVelocity, animation, acceleration,
            pitchRate, yawRate, rollRate, afterburnerVelocity, loadout, shieldFore, shieldAft, armorFront,
            armorRear, armorLeft, armorRight);

    /// <summary>Effect records: <c>{name, class, cRadius, radarR, scale, 0, 0, 0, dmgCap, explDmg,
    /// 0, 0, animation, 0, 0, yawRate, rollRate}</c>.</summary>
    private static ObjectTypeData Effect(
        ObjectClass objectClass, short collisionRadius, short radarRadius, short scale, short damageCapacity,
        short explosionDamage, uint[]? animation, short yawRate, short rollRate) =>
        T("", objectClass, collisionRadius, radarRadius, scale, 0, 0, 0, damageCapacity, explosionDamage,
            0, 0, animation, 0, 0, yawRate, rollRate, 0, L(), 0, 0, 0, 0, 0, 0);

    private static ObjectTypeData Zero() =>
        T("", ObjectClass.Null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, 0, 0, 0, 0, 0, L(), 0, 0, 0, 0, 0, 0);

    private static WeaponLoadout L(params ReadOnlySpan<(ObjectType Type, short Hardpoint, sbyte Disabled)> slots) =>
        WeaponLoadout.Create(slots);

    /// <summary>One packed weapon slot initialiser <c>{type, 0, 0, 0, hardpoint, 0, disabled}</c>.</summary>
    private static (ObjectType Type, short Hardpoint, sbyte Disabled) S(ObjectType type, short hardpoint, sbyte disabled) =>
        (type, hardpoint, disabled);
}
