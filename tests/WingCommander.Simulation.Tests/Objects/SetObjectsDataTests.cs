using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Objects;

public class SetObjectsDataTests
{
    public static TheoryData<int> AllTypes()
    {
        var data = new TheoryData<int>();
        for (int i = 0; i < ObjectTypeTable.Count; i++)
            data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Every_type_initialises_the_documented_common_fields(int typeIndex)
    {
        var type = (ObjectType)typeIndex;
        var sim = FakeResources.CreateSimulation();
        // Load every shape so no substitution happens.
        for (int i = 0; i < ObjectTypeTable.Count; i++)
            sim.TypeResources[i].ShapeSet = new ShapeRef(3, i);
        var typeData = ObjectTypeTable.Get(type);
        short obj = typeData.ObjectClass >= ObjectClass.Missile ? (short)5 : (short)20;

        // Dirty the slot first: set_objects_data must overwrite these.
        ref var o = ref sim.Objects[obj];
        o.PitchRotation = 3;
        o.YawRotation = 4;
        o.RollRotation = 5;
        o.Right = new FixedVector(1, 2, 3);
        o.AccumulatedDamage = 99;
        o.Flip = 0x30;
        o.LastCollisionObject = 7;
        o.ScreenAngle = 45;
        o.ViewFrame = 9;

        sim.SetObjectsData(obj, type, 6);

        Assert.Equal(type, o.Type);
        Assert.Equal(typeData.ObjectClass, o.Class);
        var shapeType = type == ObjectType.RockChunk ? ObjectType.Asteroid1 : type; // rock chunks share the asteroid set
        Assert.Equal(sim.TypeResources[(int)shapeType].ShapeSet, o.Shape);
        Assert.Equal(new FixedVector(0x100, 0, 0), o.Right);
        Assert.Equal(new FixedVector(0, 0x100, 0), o.Up);
        Assert.Equal(new FixedVector(0, 0, 0x100), o.Forward);
        Assert.Equal(0, o.PitchRotation + o.YawRotation + o.RollRotation);
        Assert.Equal(typeData.CollisionRadius, o.CollisionRadius);
        Assert.Equal(typeData.RadarRadius, o.RadarRadius);
        Assert.Equal(typeData.AfterburnerVelocity, o.AfterburnerVelocity);
        Assert.Equal(6, o.Owner);
        Assert.Equal(0, o.AccumulatedDamage);
        Assert.Equal(-1, o.LastCollisionObject);
        Assert.Equal(0, o.ScreenAngle);

        if (typeData.ObjectClass >= ObjectClass.Missile)
        {
            Assert.Equal(typeData.Scale, o.Scale);
            Assert.Equal(0, o.ViewFrame);
            Assert.Equal(0, o.Flip);
            Assert.Equal(-1, sim.Ships[obj].Target);
        }
        else if (!typeData.HasAnimationScript)
        {
            Assert.Equal(typeData.Scale, o.Scale);
            Assert.Equal(typeData.YawRate, o.ViewFrame);
            Assert.Equal(0, o.Flip);
        }
        else
        {
            // The first animation step ran: delay reloaded with yawRate, index advanced.
            Assert.Equal(typeData.YawRate, o.AnimationDelay);
            Assert.Equal(1, o.AnimationIndex);
            Assert.Equal((short)(typeData.AnimationScript[0] & 0x3f), o.ViewFrame);
        }
    }

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void Ships_get_shields_armor_fuel_loadout_and_speed_limits(int typeIndex)
    {
        var type = (ObjectType)typeIndex;
        var typeData = ObjectTypeTable.Get(type);
        if (typeData.ObjectClass < ObjectClass.Ship)
            return;
        var sim = FakeResources.CreateSimulation();
        sim.Objects[3].Speed = 1_000_000; // stale speed is clamped by recalc_max_velocity
        sim.SetObjectsData(3, type, -1);
        ref var ship = ref sim.Ships[3];
        Assert.Equal(typeData.ShieldFore, ship.Shield[ShieldValues.Fore]);
        Assert.Equal(typeData.ShieldAft, ship.Shield[ShieldValues.Aft]);
        Assert.Equal(typeData.ShieldFore, ship.MaximumShield[ShieldValues.Fore]);
        Assert.Equal(typeData.ShieldAft, ship.MaximumShield[ShieldValues.Aft]);
        Assert.Equal(typeData.ArmorFront, ship.Armor[ArmorValues.Front]);
        Assert.Equal(typeData.ArmorRear, ship.Armor[ArmorValues.Rear]);
        Assert.Equal(typeData.ArmorLeft, ship.Armor[ArmorValues.Left]);
        Assert.Equal(typeData.ArmorRight, ship.Armor[ArmorValues.Right]);
        Assert.Equal(typeData.Fuel, ship.Fuel);
        Assert.Equal(typeData.MaximumVelocity, ship.MaximumSpeed);
        // recalc_max_velocity only re-clamps the speed when the maximum changed (0 -> Vmax).
        int expectedSpeed = typeData.MaximumVelocity != 0 ? typeData.MaximumVelocity << 8 : 1_000_000;
        Assert.Equal(expectedSpeed, sim.Objects[3].Speed);
        Assert.Equal(4, ship.PilotHitPoints);
        Assert.Equal(100, ship.WeaponEnergy);
        Assert.Equal(-1, ship.LastAttacker);
        Assert.Equal(0, ship.Damage);
        Assert.Equal(0, ship.IonDriveDamage);
        var template = typeData.WeaponLoadout;
        ReadOnlySpan<byte> expectedLoadout = template;
        ReadOnlySpan<byte> actualLoadout = ship.Weapons;
        Assert.True(expectedLoadout.SequenceEqual(actualLoadout));
    }

    [Fact]
    public void Player_weapon_selection_picks_the_lowest_enabled_gun_and_release_slot()
    {
        var sim = FakeResources.CreateSimulation();
        sim.SetObjectsData(0, ObjectType.Hornet, -1);
        Assert.Equal(ObjectType.LaserCannon, sim.SelectedGunType);
        Assert.Equal(2, sim.SelectedReleaseWeaponIndex);

        sim.SetObjectsData(0, ObjectType.Rapier, -1);
        Assert.Equal(ObjectType.NeutronParticleGun, sim.SelectedGunType);
        Assert.Equal(5, sim.SelectedReleaseWeaponIndex);

        sim.SetObjectsData(0, ObjectType.Scimitar, -1);
        Assert.Equal(ObjectType.MassDriverCannon, sim.SelectedGunType);
        Assert.Equal(2, sim.SelectedReleaseWeaponIndex);

        // NPC ships do not touch the player's selection.
        sim.SetObjectsData(1, ObjectType.Raptor, -1);
        Assert.Equal(ObjectType.MassDriverCannon, sim.SelectedGunType);
    }

    [Theory]
    [InlineData(ObjectType.Asteroid2, ObjectType.Asteroid1)]
    [InlineData(ObjectType.Asteroid4, ObjectType.Asteroid3)]
    [InlineData(ObjectType.Asteroid6, ObjectType.Asteroid5)]
    [InlineData(ObjectType.DebrisMetalSheet, ObjectType.DebrisShipGirderChunk)]
    [InlineData(ObjectType.DebrisWing, ObjectType.DebrisPipe)]
    [InlineData(ObjectType.Explosion1, ObjectType.Explosion0)]
    [InlineData(ObjectType.Explosion2, ObjectType.Explosion0)]
    [InlineData(ObjectType.Asteroid1, ObjectType.Asteroid1)]
    [InlineData(ObjectType.LaserSpark, ObjectType.LaserSpark)]
    public void Types_without_loaded_shapes_are_substituted(ObjectType requested, ObjectType expected)
    {
        var sim = FakeResources.CreateSimulation();
        sim.SetObjectsData(20, requested, -1);
        Assert.Equal(expected, sim.Objects[20].Type);
    }

    [Fact]
    public void Rock_chunks_use_the_asteroid_shape_set_and_space_dust_only_sets_class()
    {
        var sim = FakeResources.CreateSimulation();
        sim.TypeResources[(int)ObjectType.Asteroid1].ShapeSet = new ShapeRef(3, 16);
        sim.SetObjectsData(20, ObjectType.RockChunk, 4);
        Assert.Equal(new ShapeRef(3, 16), sim.Objects[20].Shape);

        sim.Objects[21].Owner = 9;
        sim.SetObjectsData(21, ObjectType.SpaceDust, -1);
        Assert.Equal(ObjectType.SpaceDust, sim.Objects[21].Type);
        Assert.Equal(ObjectClass.Dust, sim.Objects[21].Class);
        Assert.Equal(9, sim.Objects[21].Owner);
    }

    [Fact]
    public void Animation_script_steps_scale_frames_and_removal()
    {
        var sim = FakeResources.CreateSimulation();
        sim.TypeResources[(int)ObjectType.Explosion1].ShapeSet = new ShapeRef(3, 2);
        short obj = sim.NewObject(ObjectType.Explosion1, -1);
        ref var o = ref sim.Objects[obj];
        Assert.Equal(0, o.ViewFrame);
        Assert.Equal(256, o.Scale);
        sim.AnimateShape(obj); // 0x406: grow by 6 * (256 >> 6) = 24
        Assert.Equal(280, o.Scale);
        sim.AnimateShape(obj); // frame 1
        Assert.Equal(1, o.ViewFrame);
        for (int i = 0; i < 17; i++)
            sim.AnimateShape(obj);
        Assert.Equal(ObjectClass.Explosion, o.Class);
        sim.AnimateShape(obj); // 0xa000: remove
        Assert.Equal(ObjectClass.Null, o.Class);
    }

    [Fact]
    public void Animation_jump_loops_and_frame_commands_do_not_set_flip()
    {
        var sim = FakeResources.CreateSimulation();
        sim.TypeResources[(int)ObjectType.SpaceMine].ShapeSet = new ShapeRef(3, 15);
        short mine = sim.NewObject(ObjectType.SpaceMine, -1);
        ref var o = ref sim.Objects[mine];
        // Mine script 0, 1, 2, 0x41, 0x9000; yawRate 2 = two frames per step.
        Assert.Equal(0, o.ViewFrame);
        int[] expectedFrames = [0, 1, 1, 2, 2, 1, 1, 0, 0, 1];
        foreach (int frame in expectedFrames)
        {
            sim.AnimateShape(mine);
            Assert.Equal(frame, o.ViewFrame);
            Assert.Equal(0, o.Flip); // C masks the frame before extracting the flip bits
        }
    }
}
