using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Flight;

public class MissileTests
{
    [Fact]
    public void A_heat_seeker_ignites_after_five_frames_and_homes_in()
    {
        var sim = TestWorld.Create();
        short shooter = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 0);
        short target = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 300, 2500);
        TestWorld.MakeInert(sim, shooter); // a plain homing test: no evasion (the AI would turn away)
        TestWorld.MakeInert(sim, target);
        sim.Objects[target].Speed = 10 << 8;
        sim.Objects[target].Velocity = new FixedVector(0, 0, 10 << 8);
        sim.Ships[shooter].Target = (sbyte)target;
        short missile = sim.FireWeapon(shooter, 5);
        Assert.Equal(ObjectType.HeatSeekingMissile, sim.Objects[missile].Type);
        Assert.Equal((sbyte)target, sim.Ships[missile].Target);

        int previousDistance = int.MaxValue;
        int frames = 0;
        while (sim.Objects[missile].Class == ObjectClass.Missile && frames < 100)
        {
            sim.Update3Space();
            frames++;
            if (frames == 4)
                Assert.Equal(ShipTactic.SitStill, sim.Ships[missile].Tactic);
            if (frames == 5)
            {
                Assert.Equal(ShipTactic.Ram, sim.Ships[missile].Tactic);
                Assert.Equal(140, sim.Objects[missile].Counter); // the motor burns for the type's lifetime
            }
            if (sim.Objects[missile].Class != ObjectClass.Missile)
                break;
            int distance = VectorMath.Delta(sim.Objects[missile].Position, sim.Objects[target].Position).Magnitude();
            if (frames > 12)
                Assert.True(distance < previousDistance, $"frame {frames}: missile not closing in");
            previousDistance = distance;
        }
        // It hit the Hornet: the missile slot turned into the explosion and the blast took the shields.
        Assert.Equal(ObjectClass.Explosion, sim.Objects[missile].Class);
        Assert.InRange(frames, 20, 60);
        Assert.True(sim.Ships[target].Shield[ShieldValues.Fore] < 40 || sim.Ships[target].Shield[ShieldValues.Aft] < 40);
    }

    [Fact]
    public void A_heat_seeker_without_a_hot_target_ahead_self_destructs()
    {
        var sim = TestWorld.Create();
        short shooter = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 0);
        short missile = sim.FireWeapon(shooter, 5); // no target
        Assert.Equal(-1, sim.Ships[missile].Target);
        sim.SpaceFrame = 0;
        sim.ObjectIntelligence(missile);
        Assert.Equal(ObjectClass.Explosion, sim.Objects[missile].Class);
    }

    [Fact]
    public void A_heat_seeker_prefers_the_hottest_exhaust()
    {
        var sim = TestWorld.Create();
        short shooter = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 0);
        short near = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 1000);
        short far = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 2000);
        sim.Ships[near].ExhaustHeat = 2;
        sim.Ships[far].ExhaustHeat = 3; // afterburner
        short missile = sim.FireWeapon(shooter, 5);
        sim.FacingToTarget = -1; // lost its (non-existent) target: re-acquire
        sim.HeatSeekingMissileIntelligence(missile);
        Assert.Equal((sbyte)far, sim.Ships[missile].Target);
    }

    [Fact]
    public void A_friend_or_foe_missile_picks_the_nearest_non_friend()
    {
        var sim = TestWorld.Create();
        short shooter = TestWorld.AddShip(sim, ObjectType.Rapier, Side.Imperial, 0, 0, 0);
        TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 1500); // friend with a working communicator
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 2500);
        TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 500, 0, 4000);
        short missile = sim.FireWeapon(shooter, 5);
        Assert.Equal(ObjectType.FriendOrFoeMissile, sim.Objects[missile].Type);
        Assert.Equal(15, sim.Objects[missile].Counter);
        Assert.Equal(-1, sim.Ships[missile].Target);

        for (int frame = 0; frame < 24 && sim.Objects[missile].Class == ObjectClass.Missile; frame++)
            sim.Update3Space();
        Assert.Equal((sbyte)enemy, sim.Ships[missile].Target);
    }

    [Fact]
    public void An_image_recognition_missile_steers_at_its_target()
    {
        var sim = TestWorld.Create();
        short shooter = TestWorld.AddShip(sim, ObjectType.Fralthi, Side.Kilrathi, 0, 0, 0);
        short target = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 2000, 0, 2000);
        sim.Ships[shooter].Target = (sbyte)target;
        short missile = sim.FireWeapon(shooter, 0); // the Fralthi's first slot
        Assert.Equal(ObjectType.ImageRecognitionMissile, sim.Objects[missile].Type);
        sim.SpaceFrame = 0;
        sim.ObjectIntelligence(missile);
        // The target is 45 degrees to the right: a yaw goal toward it, full speed.
        Assert.True(sim.Ships[missile].YawGoal != 0);
        Assert.Equal((110 + 10) << 8, sim.Objects[missile].Speed);
    }

    [Fact]
    public void Mines_arm_and_wait_for_ships()
    {
        var sim = TestWorld.Create();
        short mine = sim.FindVacant3dObject();
        sim.SetObjectsData(mine, ObjectType.SpaceMine, -1);
        sim.Objects[mine].Counter = 0; // a hazard mine
        sim.HouseKeepObjects();
        Assert.Equal(-1, sim.Objects[mine].Counter); // armed
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 2000);
        sim.MineIntelligence(mine);
        Assert.Equal(ObjectClass.Mine, sim.Objects[mine].Class);
        // Within the type's collision radius (20) of the hull: boom.
        sim.Objects[ship].Position = TestWorld.Units(0, 0, 120 + 10);
        sim.MineIntelligence(mine);
        Assert.Equal(ObjectClass.Explosion, sim.Objects[mine].Class);
    }

    [Fact]
    public void A_dropped_mine_explodes_after_its_fuse()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim, ObjectType.Raptor);
        sim.Ships[0].Weapons.SetDisabled(9, 0);
        short mine = sim.DropPlayerMine(0);
        Assert.Equal(ObjectType.SpaceMine, sim.Objects[mine].Type);
        Assert.Equal(20, sim.Objects[mine].Counter);
        Assert.Equal(20, sim.Objects[mine].CollisionGraceTicks);
        Assert.Equal(9, sim.Ships[0].Weapons.Count);
        for (int frame = 0; frame < 19; frame++)
            sim.HouseKeepObjects();
        Assert.Equal(ObjectClass.Mine, sim.Objects[mine].Class);
        sim.HouseKeepObjects();
        Assert.Equal(ObjectClass.Explosion, sim.Objects[mine].Class);
    }
}
