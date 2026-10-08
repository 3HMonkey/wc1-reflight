using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Flight;

public class CollisionTests
{
    [Fact]
    public void A_bolt_hit_damages_pushes_and_becomes_a_spark()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        short salthi = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        short bolt = sim.NewObject(ObjectType.LaserCannon, 0);
        ref var b = ref sim.Objects[bolt];
        b.Position = TestWorld.Units(0, 0, 890); // radii 10 + 120
        b.Velocity = new FixedVector(0, 0, 160 << 8);
        b.AccumulatedDamage = 25;
        b.Counter = 10; // older bolts hit softer: 25 - 10 / 2 = 20

        sim.ObjectCollision(bolt);

        // 20 damage on the aft shield (the bolt travels along the Salthi's forward axis).
        Assert.Equal(35 - 20, sim.Ships[salthi].Shield[ShieldValues.Aft]);
        Assert.Equal(0, sim.Ships[salthi].LastAttacker);
        Assert.Equal(4, sim.Ships[salthi].AiCooldown);
        // Push at the contact point (one unit behind the centre): translation only.
        int push = FixedMath.Divide(FixedMath.Multiply(0x16a, 20 << 8), FixedMath.Multiply(0x16a, 120 << 8));
        Assert.Equal(new FixedVector(0, 0, push), sim.Objects[salthi].Velocity);
        Assert.Equal(0, sim.Objects[salthi].PitchRotation);
        // The bolt became a laser spark at twice the bolt's scale, moving with the ship.
        Assert.Equal(ObjectType.LaserSpark, b.Type);
        Assert.Equal(ObjectClass.Explosion, b.Class);
        Assert.Equal(512 * 2, b.Scale);
        Assert.Equal(sim.Objects[salthi].Velocity, b.Velocity);
        Assert.Contains($"sfx 10 {salthi}", events.Calls);
    }

    [Fact]
    public void Bolts_ignore_their_owner()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short bolt = sim.NewObject(ObjectType.LaserCannon, 0);
        sim.Objects[bolt].Position = TestWorld.Units(0, 0, 50);
        sim.ObjectCollision(bolt);
        Assert.Equal(ObjectClass.Projectile, sim.Objects[bolt].Class);
        Assert.Equal(40, sim.Ships[0].Shield[ShieldValues.Aft]);
    }

    [Fact]
    public void Bolts_hitting_the_player_flash_the_cockpit_side()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 3000);
        short bolt = sim.NewObject(ObjectType.LaserCannon, enemy);
        sim.Objects[bolt].Position = TestWorld.Units(-60, 0, 60);
        sim.Objects[bolt].ViewPosition = new FixedVector(-60 << 8, 0, 60 << 8); // ahead-left of the eye: 45 degrees
        sim.Objects[bolt].AccumulatedDamage = 25;
        sim.ObjectCollision(bolt);
        Assert.Contains("hitSide 3", events.Calls);
        Assert.Contains("playerHit", events.Calls);
    }

    [Fact]
    public void Ships_bounce_apart_and_exchange_momentum()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        short a = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 0);
        short b = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 100); // closer than (120 + 120) / 2
        sim.Objects[a].Velocity = new FixedVector(0, 0, 20 << 8);
        // Dying ships take no damage and cannot lose control: the response is free of random numbers.
        sim.Ships[a].SpecialManeuver = SpecialManeuver.Unknown9;
        sim.Ships[b].SpecialManeuver = SpecialManeuver.Unknown9;
        uint seed = sim.Random.Seed;

        sim.ObjectCollision(a);

        // The partner is moved out to the full radius sum along the contact normal.
        Assert.Equal(TestWorld.Units(0, 0, 240), sim.Objects[b].Position);
        // Equal masses: the mover keeps its velocity plus a quarter of the normal difference, the
        // partner receives the whole difference.
        Assert.Equal(new FixedVector(0, 0, 25 << 8), sim.Objects[a].Velocity);
        Assert.Equal(new FixedVector(0, 0, 20 << 8), sim.Objects[b].Velocity);
        Assert.Equal(b, sim.Objects[a].LastCollisionObject);
        Assert.Equal(a, sim.Objects[b].LastCollisionObject);
        Assert.Contains($"sfx 28 {a}", events.Calls);
        Assert.Equal(seed, sim.Random.Seed);

        // Once apart the contact is over.
        sim.ObjectCollision(a);
        Assert.Equal(-1, sim.Objects[a].LastCollisionObject);
    }

    [Fact]
    public void Collision_damage_is_half_the_square_of_the_closing_speed()
    {
        var sim = TestWorld.Create();
        short a = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 0);
        short b = TestWorld.AddShip(sim, ObjectType.Gratha, Side.Kilrathi, 0, 0, 120);
        sim.Objects[a].Velocity = new FixedVector(0, 0, 20 << 8);
        sim.Ships[a].SpecialManeuver = SpecialManeuver.Unknown9;
        sim.Ships[b].Shield[ShieldValues.Aft] = 1000;
        sim.ObjectCollision(a);
        Assert.Equal(1000 - 20 * 20 / 2, sim.Ships[b].Shield[ShieldValues.Aft]);
    }

    [Fact]
    public void Ramming_a_capital_ship_stops_the_fighter()
    {
        var sim = TestWorld.Create();
        short fighter = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 0);
        short carrier = TestWorld.AddShip(sim, ObjectType.TigersClaw, Side.Imperial, 0, 0, 400);
        sim.Objects[fighter].Velocity = new FixedVector(0, 0, 30 << 8);
        sim.Objects[fighter].Speed = 30 << 8;
        sim.Objects[carrier].Velocity = new FixedVector(0, 1 << 8, 0);
        sim.Ships[fighter].SpecialManeuver = SpecialManeuver.Unknown9;
        sim.Ships[carrier].SpecialManeuver = SpecialManeuver.Unknown9;
        sim.ObjectCollision(fighter);
        Assert.Equal(0, sim.Objects[fighter].Speed);
        Assert.Equal(sim.Objects[carrier].Velocity, sim.Objects[fighter].Velocity);
        // Rewound one step of its post-collision velocity: 30 + (30 - 0) / 4 = 37.5 units.
        Assert.Equal(new FixedVector(0, 0, -9600), sim.Objects[fighter].Position);
    }

    [Fact]
    public void Missiles_explode_on_contact()
    {
        var sim = TestWorld.Create();
        short shooter = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, -5000);
        short target = TestWorld.AddShip(sim, ObjectType.Fralthi, Side.Imperial, 0, 0, 400); // big shields
        short missile = sim.InitializeShip(ObjectType.HeatSeekingMissile, shooter);
        sim.Objects[missile].Position = TestWorld.Units(0, 0, 0);
        sim.Objects[missile].Velocity = new FixedVector(0, 0, 120 << 8);
        sim.Objects[missile].CollisionGraceTicks = 0;

        sim.ObjectCollision(missile);

        Assert.Equal(ObjectClass.Explosion, sim.Objects[missile].Class);
        Assert.Equal(ObjectType.Explosion2, sim.Objects[missile].Type);
        Assert.Equal(FixedVector.Zero, sim.Objects[missile].Velocity);
        // The blast (EXPLOSION2: 6000) at 0 units from the hull: 6000 / 8 / 8 = 93 on the aft shield
        // (the missile came from behind the Fralthi).
        Assert.Equal(170 - 93, sim.Ships[target].Shield[ShieldValues.Aft]);
        Assert.Equal(270, sim.Ships[target].Shield[ShieldValues.Fore]);
    }

    [Fact]
    public void Missiles_ignore_their_launcher_during_the_grace_period()
    {
        var sim = TestWorld.Create();
        short shooter = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 0);
        short missile = sim.InitializeShip(ObjectType.HeatSeekingMissile, shooter);
        sim.Objects[missile].Position = TestWorld.Units(0, 0, 50);
        sim.Objects[missile].CollisionGraceTicks = 20;
        sim.ObjectCollision(missile);
        Assert.Equal(ObjectClass.Missile, sim.Objects[missile].Class);
    }

    [Fact]
    public void Invisible_hazards_vanish_instead_of_hitting_a_ship()
    {
        var sim = TestWorld.Create();
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 0);
        short rock = sim.FindVacant3dObject();
        sim.SetObjectsData(rock, ObjectType.Asteroid1, -1);
        sim.Objects[rock].Position = TestWorld.Units(0, 0, 150);
        Assert.Equal(ObjectSlots.NotVisible, sim.Objects[rock].ScreenX);
        sim.ObjectCollision(ship);
        Assert.Equal(ObjectClass.Null, sim.Objects[rock].Class);
        Assert.Equal(-1, sim.Objects[ship].LastCollisionObject);
    }

    [Fact]
    public void Forces_off_centre_spin_the_object()
    {
        var sim = TestWorld.Create();
        short ship = TestWorld.AddShip(sim, ObjectType.Fralthi, Side.Kilrathi, 0, 0, 0); // capital: no loss of control
        // A push along +z applied one unit to the right of the centre turns the nose to the left (yaw).
        sim.ApplyForceToObject(new FixedVector(0x100, 0, 0), new FixedVector(0, 0, 0x10000), ship);
        ref var o = ref sim.Objects[ship];
        int inertia = FixedMath.Divide(10000 << 8, 450 << 8);
        int yaw = FixedMath.Divide(FixedMath.Multiply(0x100, 0x10000), inertia) >> 8;
        Assert.Equal(Math.Clamp(yaw, -30, 30), o.YawRotation);
        Assert.Equal(0, o.PitchRotation);
        Assert.Equal(0, o.RollRotation);
        int planar = FixedMath.PlanarMagnitude(0x100, 0);
        int translation = FixedMath.Divide(FixedMath.Multiply(0x16a - planar, 0x10000), FixedMath.Multiply(0x16a, 10000 << 8));
        Assert.Equal(new FixedVector(0, 0, translation), o.Velocity);
    }
}
