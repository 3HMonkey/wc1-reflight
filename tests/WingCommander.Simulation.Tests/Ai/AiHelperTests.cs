using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Ai;

public class AiHelperTests
{
    private static FixedVector Units(int x, int y, int z) => TestWorld.Units(x, y, z);

    [Fact]
    public void Crash_time_is_0x7fff_beyond_1500_units_and_0x7fbc_without_relative_motion()
    {
        var sim = TestWorld.Create();
        short a = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 0);
        short b = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 5000);
        Assert.Equal(0x7fff, sim.RealCrashTime(a, b));
        sim.Objects[b].Position = Units(0, 0, 600);
        Assert.Equal(0x7fbc, sim.RealCrashTime(a, b));
    }

    [Fact]
    public void Head_on_ships_report_the_first_frame_they_touch()
    {
        var sim = TestWorld.Create();
        short a = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 0);
        short b = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        sim.Objects[a].Velocity = Units(0, 0, 25);
        sim.Objects[b].Velocity = Units(0, 0, -25);
        short time = sim.RealCrashTime(a, b);
        int touching = sim.Objects[a].CollisionRadius + sim.Objects[b].CollisionRadius + 30;
        Assert.InRange(time, 1, 20);
        Assert.True(1000 - 50 * time <= touching, $"apart {1000 - 50 * time} at frame {time}");
        // A pass that misses by more than the radii: 32000.
        sim.Objects[b].Position = Units(400, 0, 1000);
        Assert.Equal(32000, sim.RealCrashTime(a, b));
    }

    [Fact]
    public void Detect_collisions_caches_the_soonest_partner_and_ignores_missiles()
    {
        var sim = TestWorld.Create();
        short a = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 0);
        short near = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 500);
        short far = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1200);
        sim.Objects[near].Velocity = Units(0, 0, -40);
        sim.Objects[far].Velocity = Units(0, 0, -60);
        sim.ClearCrashCache();
        short partner = sim.DetectCollisions(a);
        Assert.Equal(near, partner);
        Assert.Equal(near, sim.Ships[a].CollisionPartner);
        Assert.Equal(sim.RealCrashTime(a, near), sim.Ships[a].CollisionTime);
        Assert.Equal(sim.Ships[a].CollisionTime, sim.CrashTime(near, a)); // the cache works both ways

        sim.Objects[near].Class = ObjectClass.Missile;
        sim.ClearCrashCache();
        Assert.Equal(far, sim.DetectCollisions(a));
        Assert.True(far != -1);
    }

    [Fact]
    public void Unactive_covers_no_target_non_ships_and_hard_braking_ships()
    {
        var sim = TestWorld.Create();
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        Assert.True(sim.Unactive(-1));
        Assert.True(sim.Unactive(20)); // an effect slot
        Assert.False(sim.Unactive(ship));
        sim.Ships[ship].Maneuver = ShipManeuver.HardBrake;
        Assert.True(sim.Unactive(ship));
    }

    [Fact]
    public void Scan_for_enemy_finds_the_nearest_living_enemy_within_range()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        TestWorld.AddShip(sim, ObjectType.Rapier, Side.Imperial, 0, 0, 500); // a friend
        short far = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 9000);
        short near = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 3000, 0, 3000);
        short dying = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        sim.Ships[dying].SpecialManeuver = SpecialManeuver.Unknown9;

        Assert.Equal(near, sim.ScanForEnemy(0, 16000));
        Assert.Equal(near, sim.TargetShip);
        Assert.Equal(sim.DistanceFromObject(0, near), sim.TargetRange);
        Assert.Equal(-1, sim.ScanForEnemy(0, 2000));
        Assert.Equal(-1, sim.TargetShip);
        _ = far;
    }

    [Fact]
    public void Attackers_and_danger_only_count_enemies_targeting_the_ship()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short idle = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 2000);
        short attacker = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 9000);
        Assert.False(sim.AttackerInRange(0, 12000));
        Assert.False(sim.InDanger(0));
        sim.Ships[attacker].Target = 0;
        Assert.True(sim.AttackerInRange(0, 12000));
        Assert.Equal(attacker, sim.TargetShip);
        Assert.False(sim.AttackerInRange(0, 5000));
        Assert.True(sim.InDanger(0)); // any range
        Assert.Equal(attacker, sim.TargetShip);
        _ = idle;
    }

    [Fact]
    public void A_ship_is_tailed_by_an_enemy_behind_it_that_faces_it()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim); // flying +z
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, -2000); // behind, facing +z
        Assert.True(sim.BeingTailed(0, enemy));
        Assert.False(sim.AnyEnemyTail(0)); // it does not target the player
        sim.Ships[enemy].Target = 0;
        Assert.True(sim.AnyEnemyTail(0));
        Assert.Equal(enemy, sim.DetectEnemyTail(0));
        sim.Objects[enemy].Position = Units(0, 0, -8000); // beyond 7000
        Assert.False(sim.BeingTailed(0, enemy));
    }

    [Fact]
    public void A_missile_on_the_tail_is_any_missile_targeting_the_ship()
    {
        var sim = TestWorld.Create();
        short shooter = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 0);
        short victim = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 3000);
        Assert.False(sim.MissileOnTail(victim));
        sim.Ships[shooter].Target = (sbyte)victim;
        short missile = sim.FireWeapon(shooter, 5);
        Assert.Equal(ObjectClass.Missile, sim.Objects[missile].Class);
        Assert.True(sim.MissileOnTail(victim));
    }

    [Fact]
    public void An_intact_ship_is_100_percent_healthy_and_armor_loss_lowers_it()
    {
        var sim = TestWorld.Create();
        short ship = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 0);
        Assert.Equal(100, sim.EvaluateDamage(ship));
        sim.Ships[ship].Armor[ArmorValues.Rear] = 0; // rear armor is worth 27
        Assert.Equal(73, sim.EvaluateDamage(ship));
        Assert.Equal(100, sim.EvaluateDamage(30)); // not a ship
    }

    [Fact]
    public void Squad_burst_sends_every_member_away_from_the_centre()
    {
        var sim = TestWorld.Create();
        short leader = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 0);
        short wing = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 100, 0, 0);
        sim.Ships[leader].WingLeader = -1;
        sim.Ships[wing].WingLeader = leader;
        sim.InitFormationBurst(leader);
        Assert.Equal(ShipObjective.BreakFormation, sim.Ships[leader].Objective);
        Assert.Equal(ShipObjective.BreakFormation, sim.Ships[wing].Objective);
        // centre at x = 50: offsets of -50 and +50 units, ten times further out
        Assert.Equal(Units(-500, 0, 0), sim.Ships[leader].Destination);
        Assert.Equal(Units(600, 0, 0), sim.Ships[wing].Destination);
    }

    [Fact]
    public void Engage_switches_the_objective_and_an_ace_greets_once()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        short ace = TestWorld.AddShip(sim, ObjectType.Gratha, Side.Kilrathi, 0, 0, 3000, rating: 9); // Bhurak
        sim.Ships[ace].PilotLevel = 14;
        sim.Engage(ace, 0, ShipObjective.EngageEnemy);
        Assert.Equal(ShipObjective.EngageEnemy, sim.Ships[ace].Objective);
        Assert.Equal(0, sim.Ships[ace].Target);
        Assert.Equal(0, sim.Ships[ace].WingmanMessageState); // "first meeting" greeting
        Assert.True(sim.AceStatus(0, 8));
        sim.Ships[ace].WingmanMessageState = -1;
        sim.Engage(ace, 0, ShipObjective.DestroyShip);
        Assert.Equal(-1, sim.Ships[ace].WingmanMessageState); // greeted already
    }

    [Fact]
    public void Get_follow_point_reads_the_flight_path_terminator_like_the_original()
    {
        var sim = TestWorld.Create();
        short ship = TestWorld.AddShip(sim, ObjectType.Rapier, Side.Imperial, 0, 0, 0);
        sim.MissionObjectives[0] = new() { Type = 0, Index = 1, Position = Units(1000, 0, 0), DisplayName = "", Name = "" };
        sim.MissionObjectives[1].Type = -1;
        sim.FlightPath[0] = 0;
        sim.FlightPath[1] = -1;
        sim.MissionObjectiveCount = 1;
        sim.Ships[ship].NavPointIndex = -1;
        var point = FixedVector.Zero;
        sim.GetFollowPoint(ship, ref point);
        Assert.Equal(Units(1000, 0, 0), point);
        Assert.Equal(0, sim.Ships[ship].NavPointIndex);
        // Past the end: objective -1 is the record before the table, a nav point at the origin while
        // the animation indices of slots 59 and 60 are zero.
        point = Units(5, 5, 5);
        sim.GetFollowPoint(ship, ref point);
        Assert.Equal(FixedVector.Zero, point);
        Assert.Equal(1, sim.Ships[ship].NavPointIndex);
        sim.Objects[59].AnimationIndex = 5; // a used effect slot changes the "type"
        Assert.Equal(5, sim.ObjectiveRecord(-1).Type);
    }

    [Fact]
    public void Object_minus_one_reads_the_globals_before_the_object_tables()
    {
        var sim = TestWorld.Create();
        sim.ViableTargetDistance[10] = 0x0100;
        sim.ViableTargetDistance[11] = 0x0002;
        sim.FlightPath[4] = 1;
        sim.FlightPath[7] = 2;
        Assert.Equal(new FixedVector(0x00020100, 0, 0), sim.PositionOf(-1));
        Assert.Equal(new FixedVector(0x02000001, 0, 0), sim.VelocityOf(-1));
        Assert.Equal(sim.Objects[ObjectSlots.Scratch].Up, sim.ForwardOf(-1));
        Assert.Equal(0, sim.CollisionRadiusOf(-1));
    }

    [Fact]
    public void Weighted_values_take_the_entry_whose_weight_covers_the_roll()
    {
        var sim = TestWorld.Create(seed: 77);
        var peek = TestWorld.Peek(sim);
        int roll = peek.BelowOrEqual(100) + 1;
        short[] choices = [30, 10, 40, 20, 30, 30, -1, 0];
        short expected = roll <= 30 ? (short)10 : roll <= 70 ? (short)20 : (short)30;
        Assert.Equal(expected, sim.SelectWeightedValue(choices));
    }
}
