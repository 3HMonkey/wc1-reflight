namespace WingCommander.Simulation.Data;

/// <summary>Damage, debris and weapon timing tables (used by the phase-2 damage/weapons port).</summary>
/// <remarks>C: asPlayerDamageSystemTable (0x00469878), aeShipHitDebrisTypes (0x00469950),
/// acGunRefireDelay (0x0046995c) in globals.c; aaeExplosionDebris (0x004698e0) in ship.c.</remarks>
public static class DamageTables
{
    /// <summary>
    /// Player component hit table, 5 groups of 10: 0 projectile fore, 1 other fore, 2 projectile
    /// aft, 3 other aft, 4 ram. Values are system codes of <c>your_internal_damage</c>.
    /// </summary>
    /// <remarks>C: asPlayerDamageSystemTable[50].</remarks>
    public static ReadOnlySpan<short> PlayerDamageSystemTable =>
    [
        0, 8, 6, 5, 0, 3, 5, 5, 7, 6,
        0, 8, 6, 5, 4, 3, 4, 0, 4, 4,
        1, 2, 5, 2, 7, 3, 4, 7, 5, 1,
        1, 4, 1, 5, 2, 3, 4, 7, 2, 1,
        4, 4, 4, 4, 0, 8, 6, 5, 4, 0,
    ];

    /// <summary>Player gun refire delay in frames, indexed by <c>type - LaserCannon</c>
    /// (laser 6, neutron 10, mass driver 4, turret 0).</summary>
    /// <remarks>C: acGunRefireDelay[4].</remarks>
    public static ReadOnlySpan<sbyte> GunRefireDelay => [6, 10, 4, 0];

    /// <summary>Debris spawned by <c>Create_ship_hit_debris</c>.</summary>
    /// <remarks>C: aeShipHitDebrisTypes[3].</remarks>
    public static ReadOnlySpan<ObjectType> ShipHitDebrisTypes =>
    [
        ObjectType.DebrisShipGirderChunk, ObjectType.DebrisShipTubing, ObjectType.DebrisORing,
    ];

    /// <summary>Debris set <paramref name="set"/> (0..3) of <c>Create_explosion_debris</c>, seven types each.</summary>
    /// <remarks>C: aaeExplosionDebris[4][7] (ship.c).</remarks>
    public static ReadOnlySpan<ObjectType> ExplosionDebris(int set) => ExplosionDebrisTable.AsSpan(set * 7, 7);

    private static readonly ObjectType[] ExplosionDebrisTable =
    [
        ObjectType.DebrisPipe, ObjectType.DebrisORing, ObjectType.DebrisShipGirderChunk,
        ObjectType.DebrisShipTubing, ObjectType.DebrisMetalSheet, ObjectType.DebrisWing, ObjectType.DebrisGlass,

        ObjectType.DebrisORing, ObjectType.DebrisORing, ObjectType.DebrisShipGirderChunk,
        ObjectType.DebrisShipTubing, ObjectType.DebrisMetalSheet, ObjectType.DebrisShipGirderChunk, ObjectType.DebrisGlass,

        ObjectType.DebrisPipe, ObjectType.DebrisORing, ObjectType.DebrisMetalSheet,
        ObjectType.DebrisShipTubing, ObjectType.DebrisMetalSheet, ObjectType.DebrisWing, ObjectType.DebrisShipTubing,

        ObjectType.DebrisGlass, ObjectType.DebrisShipTubing, ObjectType.DebrisMetalSheet,
        ObjectType.DebrisWing, ObjectType.DebrisPipe, ObjectType.DebrisORing, ObjectType.DebrisGlass,
    ];
}
