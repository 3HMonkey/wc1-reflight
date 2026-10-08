using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Objects;

public class SlotAllocationTests
{
    [Fact]
    public void Ship_slots_are_handed_out_from_1_to_9()
    {
        var sim = FakeResources.CreateSimulation();
        for (short expected = 1; expected <= 9; expected++)
        {
            Assert.Equal(expected, sim.InitializeShip(ObjectType.Salthi, -1));
            Assert.Equal(expected, sim.LastShipSlot);
            Assert.Equal(Side.Neutral, sim.Ships[expected].Side);
        }
        Assert.Equal(-1, sim.InitializeShip(ObjectType.Salthi, -1));
        Assert.Equal(-1, sim.LastShipSlot);
        Assert.Equal(ObjectClass.Null, sim.Objects[0].Class);

        sim.RemoveObject(4);
        Assert.Equal(4, sim.GetShipSlot());
    }

    [Fact]
    public void Effect_slots_are_handed_out_from_10_to_60_and_marked_invisible()
    {
        var sim = FakeResources.CreateSimulation();
        for (short expected = 10; expected <= 60; expected++)
        {
            short obj = sim.NewObject(ObjectType.Explosion0, 3);
            Assert.Equal(expected, obj);
            Assert.Equal(ObjectSlots.NotVisible, sim.Objects[obj].ScreenX);
            Assert.Equal(3, sim.Objects[obj].Owner);
        }
        Assert.Equal(-1, sim.FindVacant3dObject());
        Assert.Equal(-1, sim.NewObject(ObjectType.LaserCannon, 2));
    }

    [Fact]
    public void Player_projectiles_borrow_dust_slots_when_full()
    {
        var sim = FakeResources.CreateSimulation();
        for (int i = 10; i <= 60; i++)
            sim.Objects[i].Class = ObjectClass.Explosion;
        Assert.Equal(-1, sim.BorrowDust());
        sim.SetObjectsData(36, ObjectType.SpaceDust, -1);
        sim.SetObjectsData(38, ObjectType.SpaceDust, -1);
        Assert.Equal(ObjectClass.Dust, sim.Objects[36].Class);
        Assert.Equal(36, sim.BorrowDust());
        Assert.Equal(-1, sim.NewObject(ObjectType.LaserCannon, 1)); // only the player borrows
        Assert.Equal(36, sim.NewObject(ObjectType.LaserCannon, 0));
        Assert.Equal(ObjectClass.Projectile, sim.Objects[36].Class);
        Assert.Equal(38, sim.BorrowDust());
    }

    [Fact]
    public void Initialize_object_zeroes_position_and_velocity()
    {
        var sim = FakeResources.CreateSimulation();
        sim.Objects[12].Position = new FixedVector(1, 2, 3);
        sim.Objects[12].Velocity = new FixedVector(4, 5, 6);
        Assert.Equal(12, sim.InitializeObject(12, ObjectType.RockChunk, 7));
        Assert.Equal(FixedVector.Zero, sim.Objects[12].Position);
        Assert.Equal(FixedVector.Zero, sim.Objects[12].Velocity);
        Assert.Equal(-1, sim.InitializeObject(-1, ObjectType.RockChunk, 7));
    }

    [Fact]
    public void Remove_object_clears_references_and_ship_identity()
    {
        var sim = FakeResources.CreateSimulation();
        short ship = sim.InitializeShip(ObjectType.TigersClaw, -1);
        sim.Ships[ship].Rating = 3;
        sim.Ships[ship].Side = Side.Imperial;
        sim.Ships[ship].Maneuver = ShipManeuver.Burnout;
        sim.Ships[ship].AlertFlags = 3;
        sim.Ships[ship].CollisionCountdown = 3;
        sim.Ships[ship].CollisionAlertTarget = 5;
        sim.Ships[ship].CapitalShipViewFrame = 12;
        sim.Ships[ship].Fuel = 777;
        sim.Objects[ship].Shape = new ShapeRef(30, 12);
        sim.YourWingman = ship;
        sim.NavPointerObject = ship;

        short hazard = sim.NewObject(ObjectType.Asteroid1, -1);
        sim.HazardObjects[5] = (sbyte)hazard;

        sim.RemoveObject(ship);
        ref var o = ref sim.Objects[ship];
        Assert.Equal(ObjectClass.Null, o.Class);
        Assert.True(o.Shape.IsNone);
        Assert.Equal(ObjectSlots.NotVisible, o.ScreenX);
        Assert.Equal(0, o.Distance);
        Assert.Equal(-1, sim.YourWingman);
        Assert.Equal(-1, sim.NavPointerObject);
        ref var s = ref sim.Ships[ship];
        Assert.Equal(-1, s.Rating);
        Assert.Equal(-1, s.WingmanMessageState);
        Assert.Equal(Side.Neutral, s.Side);
        Assert.Equal(ShipManeuver.None, s.Maneuver);
        Assert.Equal(0u, s.AlertFlags);
        Assert.Equal(0, s.CollisionCountdown);
        Assert.Equal(0xff, s.CollisionAlertTarget);
        Assert.Equal(-1, s.CapitalShipViewFrame);
        Assert.Equal(777, s.Fuel); // other ship fields stay stale

        sim.RemoveObject(hazard);
        Assert.Equal(-1, sim.HazardObjects[5]);
        sim.RemoveObject(-1); // no-op
    }

    [Fact]
    public void Child_objects_use_the_hardpoint_offsets_in_the_parent_frame()
    {
        var sim = FakeResources.CreateSimulation();
        short ship = sim.InitializeShip(ObjectType.Hornet, -1);
        sim.Objects[ship].Position = new FixedVector(1000 << 8, 0, 0);
        sim.AlterYaw(-180, ship); // right = -x, forward = -z
        short bolt = sim.NewObject(ObjectType.LaserCannon, ship);
        sim.ChildObject(0, bolt, ship); // hardpoint 0 = {120, 10, 20}
        Assert.Equal(new FixedVector((1000 - 120) << 8, 10 << 8, -20 << 8), sim.Objects[bolt].Position);
        Assert.Equal(ship, sim.Objects[bolt].Owner);
    }

    [Fact]
    public void Mission_setup_helpers_consume_no_randoms()
    {
        var sim = FakeResources.CreateSimulation(seed: 99);
        uint seed = sim.Random.Seed;
        short ship = sim.InitializeShip(ObjectType.Gratha, -1);
        sim.NewObject(ObjectType.Explosion1, ship);
        sim.RemoveObject(ship);
        Assert.Equal(seed, sim.Random.Seed);
    }
}
