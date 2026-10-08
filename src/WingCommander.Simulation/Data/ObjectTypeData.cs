using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation.Data;

/// <summary>
/// Static statistics of one object type (one 0x87-byte record of the table compiled into the
/// executable). The graphics pointers of the original record (<c>shapeSet</c>, the loaded
/// exhaust <c>animation</c> of ships, <c>shape</c>) are runtime state and live in
/// <see cref="ObjectTypeResources"/>; only the compiled-in effect animation scripts are here.
/// Field meanings depend on the class, see docs/analysis/simulation.md §5.
/// </summary>
/// <remarks>C: ObjectTypeData (include/wcdata.h), aObjectTypeData (0x00466458, globals.c).</remarks>
public sealed class ObjectTypeData
{
    private readonly uint[]? _animationScript;
    private readonly WeaponLoadout _weaponLoadout;

    internal ObjectTypeData(
        string displayName, ObjectClass objectClass, short collisionRadius, short radarRadius,
        short scale, short animationDelay, short lifetime, short weaponDamage, short damageCapacity,
        short explosionDamage, short maximumVelocity, short cruiseVelocity, uint[]? animationScript,
        int acceleration, short pitchRate, short yawRate, short rollRate, short afterburnerVelocity,
        WeaponLoadout weaponLoadout, short shieldFore, short shieldAft, short armorFront,
        short armorRear, short armorLeft, short armorRight)
    {
        DisplayName = displayName;
        ObjectClass = objectClass;
        CollisionRadius = collisionRadius;
        RadarRadius = radarRadius;
        Scale = scale;
        AnimationDelay = animationDelay;
        Lifetime = lifetime;
        WeaponDamage = weaponDamage;
        DamageCapacity = damageCapacity;
        ExplosionDamage = explosionDamage;
        MaximumVelocity = maximumVelocity;
        CruiseVelocity = cruiseVelocity;
        _animationScript = animationScript;
        Acceleration = acceleration;
        PitchRate = pitchRate;
        YawRate = yawRate;
        RollRate = rollRate;
        AfterburnerVelocity = afterburnerVelocity;
        _weaponLoadout = weaponLoadout;
        ShieldFore = shieldFore;
        ShieldAft = shieldAft;
        ArmorFront = armorFront;
        ArmorRear = armorRear;
        ArmorLeft = armorLeft;
        ArmorRight = armorRight;
    }

    /// <summary>Name shown on the HUD/nav map (empty for most effect types).</summary>
    public string DisplayName { get; }

    /// <summary>+0x04 class of objects of this type.</summary>
    public ObjectClass ObjectClass { get; }

    /// <summary>+0x08 collision radius in units (also the eye's near plane for the viewed object).</summary>
    public short CollisionRadius { get; }

    /// <summary>+0x0A "radar radius": the mass in the collision/force model.</summary>
    public short RadarRadius { get; }

    /// <summary>+0x0C base sprite scale (0x100 = 1.0).</summary>
    public short Scale { get; }

    /// <summary>+0x0E ships: shield regeneration period; guns: energy cost.</summary>
    public short AnimationDelay { get; }

    /// <summary>+0x10 projectiles/missiles: lifetime in frames; ships: low word of the fuel.</summary>
    public short Lifetime { get; }

    /// <summary>+0x12 ships: high word of the fuel.</summary>
    public short WeaponDamage { get; }

    /// <summary>+0x14 ships: core hits before death; guns: damage dealt; -1 = indestructible.</summary>
    public short DamageCapacity { get; }

    /// <summary>+0x16 blast damage when destroyed.</summary>
    public short ExplosionDamage { get; }

    /// <summary>+0x18 maximum velocity (integer units per frame); guns: muzzle speed.</summary>
    public short MaximumVelocity { get; }

    /// <summary>+0x1A cruise velocity.</summary>
    public short CruiseVelocity { get; }

    /// <summary>+0x1C compiled-in animation command stream of effect types (empty when none).
    /// Each entry is a 32-bit record whose low word is the command.</summary>
    public ReadOnlySpan<uint> AnimationScript => _animationScript;

    /// <summary>True when the static record has an animation pointer (effect scripts).</summary>
    public bool HasAnimationScript => _animationScript is not null;

    /// <summary>+0x20 acceleration, 24.8 units per frame squared.</summary>
    public int Acceleration { get; }

    /// <summary>+0x24 turn rate in degrees/frame. Note: drives the YAW axis (field names are swapped).</summary>
    public short PitchRate { get; }

    /// <summary>+0x26 turn rate in degrees/frame. Drives the PITCH axis; effects: animation step delay
    /// or static frame.</summary>
    public short YawRate { get; }

    /// <summary>+0x28 roll rate in degrees/frame; effects: frame count / variant.</summary>
    public short RollRate { get; }

    /// <summary>+0x2A misnamed: rotational inertia constant of <c>apply_force_to_object</c>.</summary>
    public short AfterburnerVelocity { get; }

    /// <summary>+0x2C initial weapon loadout (copied into the ship on creation).</summary>
    public WeaponLoadout WeaponLoadout => _weaponLoadout;

    /// <summary>+0x73 fore shield.</summary>
    public short ShieldFore { get; }

    /// <summary>+0x75 aft shield.</summary>
    public short ShieldAft { get; }

    /// <summary>+0x77 front armor.</summary>
    public short ArmorFront { get; }

    /// <summary>+0x79 rear armor.</summary>
    public short ArmorRear { get; }

    /// <summary>+0x7B left armor.</summary>
    public short ArmorLeft { get; }

    /// <summary>+0x7D right armor.</summary>
    public short ArmorRight { get; }

    /// <summary>
    /// Ships: initial fuel, the 32-bit int overlaying <see cref="Lifetime"/> (low word) and
    /// <see cref="WeaponDamage"/> (high word).
    /// </summary>
    /// <remarks>C: <c>*(int *)&amp;typeData-&gt;lifetime</c> in set_objects_data.</remarks>
    public int Fuel => (ushort)Lifetime | (WeaponDamage << 16);
}
