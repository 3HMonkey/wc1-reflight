using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Flight;

public class WeaponTests
{
    [Fact]
    public void Player_lasers_spend_energy_and_respect_the_refire_delay()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);

        sim.FirePlayersLasers();
        // The Hornet's two lasers (slots 0 and 1) fire: bolts in effect slots 10 and 11.
        Assert.Equal(ObjectType.LaserCannon, sim.Objects[10].Type);
        Assert.Equal(ObjectType.LaserCannon, sim.Objects[11].Type);
        Assert.Equal(ObjectClass.Null, sim.Objects[12].Class);
        Assert.Equal(100 - 2 * 7, sim.Ships[0].WeaponEnergy);
        Assert.Equal(6, sim.Objects[0].Counter);
        ref var bolt = ref sim.Objects[10];
        Assert.Equal(ObjectClass.Projectile, bolt.Class);
        Assert.Equal(25, bolt.AccumulatedDamage);
        Assert.Equal(30, bolt.Counter);
        Assert.Equal(0, bolt.Owner);
        Assert.Equal(sim.PositionChild(0, 0), bolt.Position);
        Assert.InRange(bolt.Velocity.Magnitude() >> 8, 155, 160);
        Assert.True(bolt.Velocity.Z > 0x9000);
        Assert.Equal(new[] { "sfx 8 10", "sfx 8 11" }, events.Calls.Where(c => c.StartsWith("sfx", StringComparison.Ordinal)));

        sim.FirePlayersLasers();
        Assert.Equal(ObjectClass.Null, sim.Objects[12].Class);
        for (int frame = 0; frame < 7; frame++)
            sim.CountDown(0);
        Assert.Equal(-1, sim.Objects[0].Counter);
        sim.FirePlayersLasers();
        Assert.Equal(ObjectClass.Projectile, sim.Objects[12].Class);
        Assert.Equal(100 - 4 * 7, sim.Ships[0].WeaponEnergy);
    }

    [Fact]
    public void Lasers_need_energy_and_inherit_the_shooters_velocity()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        sim.Objects[0].Velocity = new FixedVector(0, 0, 20 << 8);
        sim.Ships[0].WeaponEnergy = 0;
        sim.FirePlayersLasers();
        Assert.Equal(ObjectClass.Null, sim.Objects[10].Class);

        sim.Ships[0].WeaponEnergy = 1;
        sim.FirePlayersLasers();
        Assert.Equal(1 - 2 * 7, sim.Ships[0].WeaponEnergy); // the energy check is made once before both guns
        Assert.InRange(sim.Objects[10].Velocity.Magnitude() >> 8, 175, 180);
    }

    [Theory]
    [InlineData(ObjectType.Scimitar, 4)] // mass drivers
    [InlineData(ObjectType.Rapier, 10)] // neutron guns (its lasers start disabled)
    public void Refire_delay_depends_on_the_gun(ObjectType playerType, short delay)
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim, playerType);
        sim.FirePlayersLasers();
        Assert.Equal(delay, sim.Objects[0].Counter);
    }

    [Fact]
    public void Npc_guns_wait_twelve_frames()
    {
        var sim = TestWorld.Create();
        short salthi = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 0);
        short bolt = sim.FireWeapon(salthi, 0);
        Assert.Equal(ObjectType.LaserCannon, sim.Objects[bolt].Type);
        Assert.Equal(salthi, sim.Objects[bolt].Owner);
        Assert.Equal(12, sim.Objects[salthi].Counter);
        Assert.Equal(100 - 7, sim.Ships[salthi].WeaponEnergy);
    }

    [Fact]
    public void Gun_energy_recharges_faster_with_full_shields()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        sim.Ships[0].WeaponEnergy = 50;
        sim.ReplenishWeaponEnergyBank(0);
        Assert.Equal(52, sim.Ships[0].WeaponEnergy);
        sim.Ships[0].Shield[0] = 10;
        sim.ReplenishWeaponEnergyBank(0);
        Assert.Equal(53, sim.Ships[0].WeaponEnergy);
        sim.Ships[0].WeaponEnergy = 99;
        sim.Ships[0].Shield[0] = 40;
        sim.ReplenishWeaponEnergyBank(0);
        Assert.Equal(100, sim.Ships[0].WeaponEnergy);

        // A damaged power plant skips frames: one random number per call decides.
        sim.Ships[0].WeaponEnergy = 50;
        sim.PlayerComponentDamage[1] = 2;
        var peek = TestWorld.Peek(sim);
        bool skip = (ushort)peek.InRange(0, 4) < 2;
        sim.ReplenishWeaponEnergyBank(0);
        Assert.Equal(skip ? 50 : 52, sim.Ships[0].WeaponEnergy);
        Assert.Equal(peek.Seed, sim.Random.Seed);
    }

    [Fact]
    public void Shields_recharge_one_point_per_type_interval()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim); // Hornet: interval 5
        sim.Ships[0].Shield[0] = 30;
        sim.Ships[0].Shield[1] = 50; // above the maximum of 40
        sim.SpaceFrame = 6;
        sim.ReplenishShields(0);
        Assert.Equal(30, sim.Ships[0].Shield[0]);
        Assert.Equal(40, sim.Ships[0].Shield[1]);
        sim.SpaceFrame = 10;
        sim.ReplenishShields(0);
        Assert.Equal(31, sim.Ships[0].Shield[0]);

        // A damaged power plant (level 1) only recharges on every second frame.
        sim.PlayerComponentDamage[1] = 1;
        sim.SpaceFrame = 15;
        sim.ReplenishShields(0);
        Assert.Equal(31, sim.Ships[0].Shield[0]);
        sim.SpaceFrame = 20;
        sim.ReplenishShields(0);
        Assert.Equal(32, sim.Ships[0].Shield[0]);
    }

    [Fact]
    public void Afterburner_burns_fuel_and_doubles_the_thrust()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim); // Hornet: Vmax 42, acceleration 819
        int fuel = sim.Ships[0].Fuel;
        sim.YourAfterburner();
        Assert.Equal(SpecialManeuver.Afterburner, sim.Ships[0].SpecialManeuver);
        Assert.Equal(8, sim.Ships[0].AfterburnerTimer);
        Assert.Contains("afterburner 0", events.Calls);

        sim.AccelerateAndMoveObject(0);
        Assert.Equal(7, sim.Ships[0].AfterburnerTimer);
        Assert.Equal(fuel - 200, sim.Ships[0].Fuel);
        Assert.Equal(3, sim.Ships[0].ExhaustHeat);
        // Target velocity (42 + 20) * 2 = 124 units/frame along the nose; the doubled acceleration 2 * 819,
        // scaled by (alignment + 2) = 3, halved and divided by the gap gives the fraction of the gap closed.
        int gap = 124 << 8;
        int acceleration = FixedMath.Multiply(819 * 2, FixedMath.Divide(gap, gap) + 0x200);
        int expected = FixedMath.Multiply(gap, FixedMath.Divide(acceleration >> 1, gap));
        Assert.Equal(2356, expected);
        Assert.Equal(expected, sim.Objects[0].Velocity.Z);
        Assert.Equal(sim.Objects[0].Velocity, sim.Objects[0].Position);

        // Re-pressing while more than 2 frames remain does nothing.
        sim.YourAfterburner();
        Assert.Equal(7, sim.Ships[0].AfterburnerTimer);
        for (int frame = 0; frame < 7; frame++)
            sim.AccelerateAndMoveObject(0);
        Assert.Equal(SpecialManeuver.None, sim.Ships[0].SpecialManeuver);
        Assert.Contains("afterburnerOut", events.Calls);
    }

    [Fact]
    public void Throttle_keys_change_the_commanded_speed()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        sim.Objects[0].Speed = 0;
        sim.Accelerate(10);
        Assert.Equal(10 << 8, sim.Objects[0].Speed);
        sim.Accelerate(9000);
        Assert.Equal(42 << 8, sim.Objects[0].Speed);
        sim.ZeroPlayerSpeed();
        Assert.Equal(0, sim.Objects[0].Speed);
    }

    [Fact]
    public void Player_missiles_leave_the_loadout_and_select_the_next_one()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        Assert.Equal(2, sim.SelectedReleaseWeaponIndex); // the first dumb-fire missile
        sim.TargetLockCountdown = -1;
        sim.PlayerReleaseWeapon();
        short missile = sim.ExternalViewShip;
        Assert.Equal(1, missile);
        ref var m = ref sim.Objects[missile];
        Assert.Equal(ObjectType.DumbFireMissile, m.Type);
        Assert.Equal(ObjectClass.Missile, m.Class);
        Assert.Equal(1, m.Counter);
        Assert.Equal(20, m.CollisionGraceTicks);
        Assert.Equal(ShipTactic.SitStill, sim.Ships[missile].Tactic);
        Assert.Equal(130 << 8, m.Speed);
        // Launched with 10 units/frame forward and a 10-unit kick upward.
        Assert.Equal(new FixedVector(0, 10 << 8, 10 << 8), m.Velocity);
        Assert.Equal(4, sim.Ships[0].Weapons.Count);
        Assert.Contains("launch DumbFireMissile 2", events.Calls);
        Assert.Contains("sfx 1 1", events.Calls);
        // The second dumb-fire missile (disabled until now) is selected next.
        Assert.Equal(ObjectType.DumbFireMissile, sim.Ships[0].Weapons.GetWeaponType(sim.SelectedReleaseWeaponIndex));

        // One missile at a time while the missile camera tracks one.
        sim.PlayerReleaseWeapon();
        Assert.Equal(4, sim.Ships[0].Weapons.Count);
    }

    [Fact]
    public void Heat_seekers_need_a_lock()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim, ObjectType.Raptor); // the first enabled missile is a heat seeker
        sim.TargetLockCountdown = 18;
        Assert.Equal(-1, sim.FireMissile(0));
        Assert.Contains("hud NeedMissileLock -1", events.Calls);
        sim.TargetLockCountdown = 0;
        short missile = sim.FireMissile(0);
        Assert.Equal(ObjectType.HeatSeekingMissile, sim.Objects[missile].Type);
        Assert.Equal(5, sim.Objects[missile].Counter);
    }

    [Fact]
    public void Dumb_fire_missiles_lead_their_target()
    {
        var sim = TestWorld.Create();
        short shooter = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 0);
        short target = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        sim.Objects[target].Velocity = new FixedVector(40 << 8, 0, 0);
        sim.Ships[shooter].Target = (sbyte)target;
        short missile = sim.FireWeapon(shooter, 2);
        // Lead time: 1000 units / 130 units per frame = 7.69 frames (24.8); 40 units/frame sideways.
        int leadTime = (1000 << 8) / 130;
        int lead = FixedMath.Multiply(40 << 8, leadTime);
        Assert.Equal(78760, lead); // 307.7 units
        ref var m = ref sim.Objects[missile];
        // Launched from hardpoint 2 (75 right, 45 up, 30 back): the nose points at the lead point
        // (about 13 degrees right), not at the target itself (4 degrees left).
        var leadPoint = new FixedVector(lead, 0, 1000 << 8);
        int dotLead = VectorMath.Dot(VectorMath.Normalized(VectorDelta(m.Position, leadPoint)), m.Forward);
        int dotTarget = VectorMath.Dot(VectorMath.Normalized(VectorDelta(m.Position, sim.Objects[target].Position)), m.Forward);
        Assert.True(dotLead >= 250 && dotLead > dotTarget + 4, $"forward {m.Forward}: lead {dotLead}, target {dotTarget}");
        Assert.InRange(m.Forward.X, 45, 65);
        Assert.Equal((sbyte)target, sim.Ships[missile].Target);
    }

    [Fact]
    public void Npc_guns_fire_only_when_aimed_and_in_range()
    {
        var sim = TestWorld.Create();
        short salthi = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 0);
        short hornet = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 1500);
        sim.Objects[salthi].Counter = 0;
        sim.Fire(salthi, hornet);
        // Both lasers are aimed (facing 100 > 70, 1500 units < 30 frames * 160 units): they fire.
        Assert.Equal(0, sim.Ships[salthi].Weapons.GetDisabled(0));
        Assert.Equal(0, sim.Ships[salthi].Weapons.GetDisabled(1));
        Assert.Equal(2, CountClass(sim, ObjectClass.Projectile));
        Assert.Equal(12, sim.Objects[salthi].Counter);

        // Behind the Salthi the guns are switched off (and the refire delay blocks anyway).
        sim.Objects[hornet].Position = TestWorld.Units(0, 0, -1500);
        sim.Objects[salthi].Counter = 0;
        sim.Fire(salthi, hornet);
        Assert.Equal(1, sim.Ships[salthi].Weapons.GetDisabled(0));
        Assert.Equal(1, sim.Ships[salthi].Weapons.GetDisabled(1));
        Assert.Equal(2, CountClass(sim, ObjectClass.Projectile));
    }

    [Fact]
    public void Capital_ship_turrets_pop_flak_at_enemies_in_range()
    {
        var sim = TestWorld.Create();
        short claw = TestWorld.AddShip(sim, ObjectType.TigersClaw, Side.Imperial, 0, 0, 0);
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 9000);
        Assert.False(sim.FireTurrets(claw)); // nothing within 5000
        sim.Objects[enemy].Position = TestWorld.Units(0, 0, 3000);
        for (int call = 0; call < 20; call++)
            Assert.True(sim.FireTurrets(claw));
        Assert.Equal((sbyte)enemy, sim.Ships[claw].Target);
        int flak = Enumerable.Range(0, ObjectSlots.Count).Count(i =>
            (sim.Objects[i].Class == ObjectClass.Explosion && sim.Objects[i].Type == ObjectType.Explosion0) ||
            (sim.Objects[i].Class == ObjectClass.Projectile && sim.Objects[i].Type == ObjectType.Turret));
        Assert.True(flak > 0);
    }

    private static int CountClass(SpaceSimulation sim, ObjectClass objectClass) =>
        Enumerable.Range(0, ObjectSlots.Count).Count(i => sim.Objects[i].Class == objectClass);

    private static FixedVector VectorDelta(FixedVector from, FixedVector to) => to - from;
}
