using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Objects;

public class MotionTests
{
    [Fact]
    public void Match_rotation_goal_moves_the_rate_and_counts_the_goal_down()
    {
        short rotation = 0;
        short goal = 90;
        SpaceSimulation.MatchRotationGoal(ref rotation, ref goal, 90, 8);
        Expect(rotation, goal, 8, 82);
        SpaceSimulation.MatchRotationGoal(ref rotation, ref goal, 74, 8);
        Expect(rotation, goal, 8, 74);

        rotation = 0;
        goal = -30;
        SpaceSimulation.MatchRotationGoal(ref rotation, ref goal, 30, 5);
        Expect(rotation, goal, -5, -25);

        // Goals beyond ±180 are wrapped first.
        rotation = 0;
        goal = 270;
        SpaceSimulation.MatchRotationGoal(ref rotation, ref goal, 90, 9);
        Expect(rotation, goal, -9, -81);

        // The step never drops below one degree.
        rotation = 0;
        goal = 2;
        SpaceSimulation.MatchRotationGoal(ref rotation, ref goal, 200, 1);
        Expect(rotation, goal, 1, 1);

        // No total error: only the goal bookkeeping runs.
        rotation = 3;
        goal = 5;
        SpaceSimulation.MatchRotationGoal(ref rotation, ref goal, 0, 1);
        Expect(rotation, goal, 3, 2);
    }

    [Fact]
    public void Rotate_object_to_goal_uses_the_swapped_rate_columns()
    {
        var sim = FakeResources.CreateSimulation();
        short ship = sim.InitializeShip(ObjectType.Salthi, -1); // pitchRate 14, yawRate 12, rollRate 22
        sim.Ships[ship].YawGoal = 90;
        sim.Ships[ship].PitchGoal = 90;
        sim.Ships[ship].RollGoal = 90;
        sim.RotateObjectToGoal(ship);
        // totalError 270: pitch step = 90*12/270 = 4, yaw step = 90*14/270 = 4, roll step = 90*22/270 = 7.
        Assert.Equal(4, sim.Objects[ship].PitchRotation);
        Assert.Equal(4, sim.Objects[ship].YawRotation);
        Assert.Equal(7, sim.Objects[ship].RollRotation);
        Assert.Equal(86, sim.Ships[ship].PitchGoal);
        Assert.Equal(86, sim.Ships[ship].YawGoal);
        Assert.Equal(83, sim.Ships[ship].RollGoal);
    }

    [Fact]
    public void Tumbling_ships_ignore_goals_until_a_skill_check_passes()
    {
        var sim = FakeResources.CreateSimulation(seed: 1);
        short ship = sim.InitializeShip(ObjectType.Salthi, -1);
        sim.Ships[ship].PilotLevel = 4; // skill 4
        sim.Ships[ship].SpecialManeuver = SpecialManeuver.BlowingUp;
        sim.Ships[ship].YawGoal = 45;
        sim.Objects[ship].Counter = 5;
        uint seed = sim.Random.Seed;
        sim.RotateObjectToGoal(ship);
        Assert.Equal(seed, sim.Random.Seed); // counter still running: no skill check
        Assert.Equal(0, sim.Objects[ship].YawRotation);

        sim.Objects[ship].Counter = -1;
        var expected = new CRandom(sim.Random.Seed);
        bool passes = 4 > expected.BelowOrEqual(7);
        sim.RotateObjectToGoal(ship);
        Assert.Equal(passes ? SpecialManeuver.None : SpecialManeuver.BlowingUp, sim.Ships[ship].SpecialManeuver);
        Assert.Equal(0, sim.Objects[ship].YawRotation); // returns before steering either way

        sim.Ships[ship].SpecialManeuver = SpecialManeuver.BlowingUp;
        sim.SetAlert(ship, 1);
        sim.RotateObjectToGoal(ship); // collision alert cancels the tumble and steers
        Assert.Equal(SpecialManeuver.None, sim.Ships[ship].SpecialManeuver);
        Assert.NotEqual(0, sim.Objects[ship].YawRotation);
    }

    [Fact]
    public void Speed_control_clamps_and_accelerates_by_the_type_rate()
    {
        var sim = FakeResources.CreateSimulation();
        short ship = sim.InitializeShip(ObjectType.Hornet, -1); // Vmax 42, cruise 30, acceleration 819
        sim.Objects[ship].Speed = 0;
        sim.ApproachFullSpeed(ship);
        Assert.Equal(819, sim.Objects[ship].Speed);
        sim.Celerate(ship, 100 << 8);
        Assert.Equal(42 << 8, sim.Objects[ship].Speed);
        sim.Celerate(ship, -(1000 << 8));
        Assert.Equal(0, sim.Objects[ship].Speed);
        sim.ApproachHalfSpeed(ship);
        Assert.Equal(819, sim.Objects[ship].Speed);
        sim.Objects[ship].Speed = (15 << 8) - 100;
        sim.ApproachHalfSpeed(ship); // (30 & ~1) << 7 = 15 << 8: closer than one step
        Assert.Equal(15 << 8, sim.Objects[ship].Speed);
        sim.SetAlert(ship, 1); // collision alert doubles the rate
        sim.Objects[ship].Speed = 0;
        sim.ApproachCruiseSpeed(ship);
        Assert.Equal(1638, sim.Objects[ship].Speed);
        sim.ApproachZeroSpeed(ship);
        Assert.Equal(0, sim.Objects[ship].Speed);
        sim.ApproachMinSpeed(ship);
        Assert.Equal(0x500, sim.Objects[ship].Speed);
    }

    [Fact]
    public void Aces_fly_faster_and_fuel_or_ion_drive_damage_caps_the_speed()
    {
        var sim = FakeResources.CreateSimulation();
        short ship = sim.InitializeShip(ObjectType.Dralthi, -1); // Vmax 40, acceleration 768
        Assert.Equal(40, sim.GetShipMaxVelocity(ship));
        sim.Ships[ship].Rating = 9;
        Assert.Equal(53, sim.GetShipMaxVelocity(ship));
        Assert.Equal(1024, sim.GetShipAccelerationRate(ship));
        sim.Ships[ship].Rating = -1;

        sim.Objects[ship].Speed = 40 << 8;
        sim.DamageIonDrive(ship, 2, 3);
        Assert.Equal(2, sim.Ships[ship].IonDriveDamage);
        Assert.Equal(20, sim.Ships[ship].MaximumSpeed);
        Assert.Equal(20 << 8, sim.Objects[ship].Speed);
        sim.DamageIonDrive(ship, 5, 3);
        Assert.Equal(3, sim.Ships[ship].IonDriveDamage);
        Assert.Equal(10, sim.Ships[ship].MaximumSpeed);
        sim.DamageIonDrive(ship, -9, 3);
        Assert.Equal(0, sim.Ships[ship].IonDriveDamage);

        sim.DrainFuel(ship, short.MaxValue);
        sim.Ships[ship].Fuel = 0;
        Assert.Equal(40, sim.Ships[ship].MaximumSpeed); // drain_fuel never re-checks
        sim.RecalcMaxVelocity(ship);
        Assert.Equal(5, sim.Ships[ship].MaximumSpeed);
        Assert.Equal(5 << 8, sim.Objects[ship].Speed);
    }

    [Fact]
    public void Special_maneuver_priorities()
    {
        var sim = FakeResources.CreateSimulation();
        short ship = sim.InitializeShip(ObjectType.Krant, -1);
        sim.Ships[ship].SpecialManeuver = SpecialManeuver.None;
        sim.SetSpecial(ship, SpecialManeuver.Afterburner);
        Assert.Equal(SpecialManeuver.Afterburner, sim.Ships[ship].SpecialManeuver);
        sim.SetSpecial(ship, SpecialManeuver.SuperBrake);
        Assert.Equal(SpecialManeuver.SuperBrake, sim.Ships[ship].SpecialManeuver);
        sim.SetSpecial(ship, SpecialManeuver.Unknown9);
        sim.SetSpecial(ship, SpecialManeuver.Afterburner); // dying wins
        Assert.Equal(SpecialManeuver.Unknown9, sim.Ships[ship].SpecialManeuver);
        sim.Ships[ship].SpecialManeuver = SpecialManeuver.None;
        sim.SetAlert(ship, 1);
        sim.SetSpecial(ship, SpecialManeuver.BlowingUp); // cancelled under a collision alert
        Assert.Equal(SpecialManeuver.None, sim.Ships[ship].SpecialManeuver);
        sim.ClearAlert(ship);
        Assert.False(sim.AlertFlag(ship, 1));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(8, 5)]
    [InlineData(12, 7)]
    [InlineData(13, 5)]
    [InlineData(14, 4)]
    [InlineData(17, 7)]
    public void Skill_rating_by_pilot_level(int pilotLevel, short expected)
    {
        var sim = FakeResources.CreateSimulation();
        sim.Ships[1].PilotLevel = pilotLevel;
        Assert.Equal(expected, sim.SkillRating(1));
    }

    [Fact]
    public void Real_velocity_and_fix_velocity()
    {
        var sim = FakeResources.CreateSimulation();
        short ship = sim.InitializeShip(ObjectType.Hornet, -1);
        sim.AlterYaw(90, ship);
        sim.Objects[ship].Speed = 20 << 8;
        sim.FixVelocity(ship);
        Assert.Equal(20, sim.RealVelocity(ship));
        Assert.Equal(VectorMathScale(sim.Objects[ship].Forward, 20 << 8), sim.Objects[ship].Velocity);
        sim.Objects[ship].Velocity = new FixedVector(0x7fffffff, 0, 0);
        Assert.Equal(0x7fff, sim.RealVelocity(ship));
    }

    [Fact]
    public void Trim_goals_and_no_goal()
    {
        var sim = FakeResources.CreateSimulation();
        sim.Ships[2].YawGoal = 50;
        sim.Ships[2].PitchGoal = -40;
        sim.TrimGoals(2, 10);
        Expect(sim.Ships[2].YawGoal, sim.Ships[2].PitchGoal, 10, -10);
        Assert.False(sim.NoGoal(2));
        sim.SteadyObject(2);
        Assert.True(sim.NoGoal(2));
    }

    private static void Expect(short first, short second, int expectedFirst, int expectedSecond)
    {
        Assert.Equal(expectedFirst, first);
        Assert.Equal(expectedSecond, second);
    }

    private static FixedVector VectorMathScale(FixedVector v, int scale) =>
        new(FixedMath.Multiply(v.X, scale), FixedMath.Multiply(v.Y, scale), FixedMath.Multiply(v.Z, scale));
}
