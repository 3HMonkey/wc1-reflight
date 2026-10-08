using WingCommander.Simulation.Data;

namespace WingCommander.Simulation.Tests.Data;

public class StaticTableTests
{
    [Fact]
    public void Object_type_table_has_58_entries_with_the_documented_classes()
    {
        Assert.Equal(58, ObjectTypeTable.All.Count);
        Assert.Equal(ObjectClass.Ship, ObjectTypeTable.Get(ObjectType.Hornet).ObjectClass);
        Assert.Equal(ObjectClass.CapitalShip, ObjectTypeTable.Get(ObjectType.TigersClaw).ObjectClass);
        Assert.Equal(ObjectClass.CapitalShip, ObjectTypeTable.Get(ObjectType.KilrathiBase).ObjectClass);
        Assert.Equal(ObjectClass.Null, ObjectTypeTable.Get(ObjectType.AsteroidField).ObjectClass);
        Assert.Equal(ObjectClass.Projectile, ObjectTypeTable.Get(ObjectType.Turret).ObjectClass);
        Assert.Equal(ObjectClass.Missile, ObjectTypeTable.Get(ObjectType.Torpedo).ObjectClass);
        Assert.Equal(ObjectClass.Mine, ObjectTypeTable.Get(ObjectType.SpaceMine).ObjectClass);
        Assert.Equal(ObjectClass.Debris, ObjectTypeTable.Get(ObjectType.RockChunk).ObjectClass);
        Assert.Equal(ObjectClass.FixedObject, ObjectTypeTable.Get(ObjectType.Thrusters).ObjectClass);
        Assert.Equal(ObjectClass.Explosion, ObjectTypeTable.Get(ObjectType.HyperspaceJumpFlash).ObjectClass);
        for (int i = 0; i < 22; i++)
            Assert.True(ObjectTypeTable.All[i].ObjectClass >= ObjectClass.Ship, $"type {i}");
    }

    [Theory]
    [InlineData(ObjectType.Hornet, 200000)]
    [InlineData(ObjectType.Rapier, 250000)]
    [InlineData(ObjectType.Scimitar, 280000)]
    [InlineData(ObjectType.Raptor, 300000)]
    [InlineData(ObjectType.Salthi, 200000)]
    [InlineData(ObjectType.TigersClaw, 200000)]
    public void Fuel_is_the_int_overlaying_lifetime_and_weapon_damage(ObjectType type, int fuel) =>
        Assert.Equal(fuel, ObjectTypeTable.Get(type).Fuel);

    [Fact]
    public void Hornet_record_matches_globals_c()
    {
        var hornet = ObjectTypeTable.Get(ObjectType.Hornet);
        Assert.Equal("Hornet", hornet.DisplayName);
        Assert.Equal(new[] { 100, 125, 1024, 5 }, new int[] { hornet.CollisionRadius, hornet.RadarRadius, hornet.Scale, hornet.AnimationDelay });
        Assert.Equal(new[] { 5, 4000, 42, 30 }, new int[] { hornet.DamageCapacity, hornet.ExplosionDamage, hornet.MaximumVelocity, hornet.CruiseVelocity });
        Assert.Equal(new[] { 819, 8, 9, 8, 900 }, new int[] { hornet.Acceleration, hornet.PitchRate, hornet.YawRate, hornet.RollRate, hornet.AfterburnerVelocity });
        Assert.Equal(new[] { 40, 40, 45, 40, 30, 30 }, new int[] { hornet.ShieldFore, hornet.ShieldAft, hornet.ArmorFront, hornet.ArmorRear, hornet.ArmorLeft, hornet.ArmorRight });
        var loadout = hornet.WeaponLoadout;
        Assert.Equal(5, loadout.Count);
        Assert.Equal(ObjectType.LaserCannon, loadout.GetWeaponType(0));
        Assert.Equal(ObjectType.DumbFireMissile, loadout.GetWeaponType(3));
        Assert.Equal(3, loadout.GetHardpoint(3));
        Assert.Equal(1, loadout.GetDisabled(3));
        Assert.Equal(ObjectType.HeatSeekingMissile, loadout.GetWeaponType(4));
        Assert.False(hornet.HasAnimationScript);
    }

    [Fact]
    public void Loadout_bytes_have_the_packed_seven_byte_layout()
    {
        var loadout = ObjectTypeTable.Get(ObjectType.Rapier).WeaponLoadout;
        ReadOnlySpan<byte> bytes = loadout;
        Assert.Equal(0x47, bytes.Length);
        // count 9, then {24,0,0,0, 14,0, 1}, {24,0,0,0, 18,0, 1}, {25,0,0,0, 12,0, 0} ...
        Assert.Equal(new byte[] { 9, 24, 0, 0, 0, 14, 0, 1, 24, 0, 0, 0, 18, 0, 1, 25, 0, 0, 0, 12, 0, 0 }, bytes[..22].ToArray());
        Assert.Equal(0, bytes[64]); // slot 9 is unused
    }

    [Fact]
    public void Display_names_follow_the_string_table_offsets()
    {
        Assert.Equal("Tiger's Claw", ObjectTypeTable.Get(ObjectType.TigersClaw).DisplayName);
        Assert.Equal("Star post", ObjectTypeTable.Get(ObjectType.KilrathiBase).DisplayName);
        Assert.Equal("Laser cannon", ObjectTypeTable.Get(ObjectType.LaserCannon).DisplayName);
        Assert.Equal("Spiculum IR", ObjectTypeTable.Get(ObjectType.ImageRecognitionMissile).DisplayName);
        Assert.Equal("Porcupine", ObjectTypeTable.Get(ObjectType.SpaceMine).DisplayName);
        Assert.Equal("", ObjectTypeTable.Get(ObjectType.Turret).DisplayName);
        Assert.Equal("", ObjectTypeTable.Get(ObjectType.Torpedo).DisplayName);
        Assert.Equal("", ObjectTypeTable.Get(ObjectType.Asteroid1).DisplayName);
    }

    [Fact]
    public void Effect_types_carry_their_animation_scripts()
    {
        var explosion = ObjectTypeTable.Get(ObjectType.Explosion1);
        Assert.True(explosion.HasAnimationScript);
        Assert.Equal(22, explosion.AnimationScript.Length);
        Assert.Equal(0x406u, explosion.AnimationScript[1]);
        Assert.Equal(0xa000u, explosion.AnimationScript[20]);
        var mine = ObjectTypeTable.Get(ObjectType.SpaceMine);
        Assert.Equal(2, mine.YawRate);
        Assert.Equal(2, mine.RollRate);
        Assert.Equal(0x41u, mine.AnimationScript[3]);
        Assert.False(ObjectTypeTable.Get(ObjectType.Thrusters).HasAnimationScript);
        Assert.False(ObjectTypeTable.Get(ObjectType.LaserCannon).HasAnimationScript);
        for (int i = (int)ObjectType.Asteroid1; i <= (int)ObjectType.HyperspaceJumpFlash; i++)
        {
            if (i != (int)ObjectType.Thrusters)
                Assert.True(ObjectTypeTable.All[i].HasAnimationScript, $"type {i}");
        }
    }

    [Fact]
    public void Geometry_tables_have_the_documented_sizes_and_values()
    {
        Assert.Equal(56, GeometryTables.ChildOffsets.Length);
        Assert.Equal(new ShortVector(120, 10, 20), GeometryTables.ChildOffsets[0]);
        Assert.Equal(new ShortVector(0, 0, 500), GeometryTables.ChildOffsets[33]);
        Assert.Equal(new ShortVector(0, 0, -300), GeometryTables.ChildOffsets[55]);
        Assert.Equal(40, GeometryTables.FormationPositions.Length);
        Assert.Equal(new ShortVector(750, 0, 0), GeometryTables.FormationPosition(0, 2));
        Assert.Equal(new ShortVector(3000, -300, -750), GeometryTables.FormationPosition(1, 7));
        Assert.Equal(new ShortVector(0, 500, -1250), GeometryTables.FormationPosition(4, 7));
        Assert.Equal(186, GeometryTables.DirectionShapeFrame.Length);
        Assert.Equal(186, GeometryTables.DirectionShapeFlip.Length);
        Assert.Equal(36, GeometryTables.DirectionShapeFrame[61]);
        Assert.Equal(16, GeometryTables.DirectionShapeFrame[185]);
    }

    [Fact]
    public void Ai_tables_match_globals_c()
    {
        Assert.Equal(new ManeuverChoice(70, 40, 30), AiTables.RatedManeuverChoice(0, 0, 0));
        Assert.Equal(new ManeuverChoice(100, 2, -1), AiTables.RatedManeuverChoice(0, 8, 2));
        Assert.Equal(new ManeuverChoice(0, -1, -1), AiTables.RatedManeuverChoice(8, 7, 0));
        Assert.Equal(new ManeuverChoice(97, 12, 22), AiTables.RatedManeuverChoice(10, 3, 0));
        Assert.Equal(new ManeuverChoice(100, 2, -1), AiTables.RatedManeuverChoice(12, 8, 2));
        Assert.Equal(new ManeuverChoice(70, 40, 45), AiTables.KilrathiManeuverChoice(0, 0, 0));
        Assert.Equal(new ManeuverChoice(10, 11, 46), AiTables.KilrathiManeuverChoice(4, 7, 0));
        Assert.Equal(new ManeuverChoice(100, 2, -1), AiTables.KilrathiManeuverChoice(4, 8, 2));
        Assert.Equal(new sbyte[] { 24, 34, 13, 14 }, AiTables.DefenseManeuvers(0).ToArray());
        Assert.Equal(8, AiTables.DefenseManeuvers(4).Length);
        Assert.Equal(18, AiTables.PilotTurnInterval.Length);
        Assert.Equal(1, AiTables.PilotTurnInterval[17]);
        Assert.Equal(24, AiTables.PilotAggression.Length);
        Assert.Equal(20, AiTables.PilotRecovery.Length);
        Assert.Equal(47, AiTables.ManeuverRerollChance.Length);
        Assert.Equal(5, AiTables.ManeuverRerollChance[(int)ShipManeuver.BuzzDebris]);
        Assert.Equal(3, AiTables.ManeuverRerollChance[(int)ShipManeuver.SitNFire]);
        Assert.Equal(3, AiTables.ManeuverRerollChance[(int)ShipManeuver.GetDistance]);
    }

    [Fact]
    public void Damage_tables_match_globals_c()
    {
        Assert.Equal(50, DamageTables.PlayerDamageSystemTable.Length);
        Assert.Equal(8, DamageTables.PlayerDamageSystemTable[1]);
        Assert.Equal(0, DamageTables.PlayerDamageSystemTable[49]);
        Assert.Equal(new sbyte[] { 6, 10, 4, 0 }, DamageTables.GunRefireDelay.ToArray());
        Assert.Equal(ObjectType.DebrisORing, DamageTables.ShipHitDebrisTypes[2]);
        Assert.Equal(ObjectType.DebrisGlass, DamageTables.ExplosionDebris(3)[6]);
        Assert.Equal(ObjectType.DebrisShipTubing, DamageTables.ExplosionDebris(2)[6]);
    }

    [Fact]
    public void Intro_records_reference_their_canned_sequences()
    {
        var record = IntroMissionData.CreateShipRecord(35);
        Assert.Equal(ObjectType.Hornet, record.Type);
        Assert.Equal(ShipMissionType.CannedSequence, record.MissionType);
        Assert.Equal(16, record.NavPoint);
        Assert.Equal(42, record.CannedSequence!.Length);
        var field = IntroMissionData.CreateShipRecord(36);
        Assert.Equal(ObjectType.AsteroidField, field.Type);
        Assert.Null(field.CannedSequence);
        var nav = IntroMissionData.CreateNavPoint(18);
        Assert.Equal(-15536, nav.ProximityRadius); // 50000 in a signed short
        Assert.Equal(ObjectType.Gratha, nav.PreloadObjectTypes[0]);
        Assert.Equal(41, nav.MissionShips[4]);
        Assert.Equal(-1, nav.MissionShips[5]);
        Assert.Equal(0, nav.Triggers[0]); // {0, 0} pairs: set nav 0's type to 0
    }
}
