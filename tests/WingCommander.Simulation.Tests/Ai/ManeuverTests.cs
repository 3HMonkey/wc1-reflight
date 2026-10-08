using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Ai;

public class ManeuverTests
{
    private static (SpaceSimulation Sim, short Ship, short Target) Duel(ObjectType type = ObjectType.Salthi, sbyte rating = -1)
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short ship = TestWorld.AddShip(sim, type, Side.Kilrathi, 0, 0, 3000, rating);
        sim.Ships[ship].Target = 0;
        return (sim, ship, 0);
    }

    [Theory]
    [InlineData(14, 0)]
    [InlineData(15, 1)]
    [InlineData(29, 1)]
    [InlineData(30, 2)]
    public void Morale_follows_stress(sbyte stress, short morale)
    {
        var (sim, ship, _) = Duel();
        sim.Ships[ship].Stress = stress;
        Assert.Equal(morale, sim.StressMorale(ship));
    }

    [Fact]
    public void Stress_grows_under_pressure_and_is_capped_by_health()
    {
        var (sim, ship, _) = Duel(); // generic level 2: aggression 3, recovery 8
        sim.HandleStress(ship, 3); // being tailed
        Assert.Equal(3, sim.Ships[ship].Stress);
        sim.Ships[ship].Stress = 20;
        sim.HandleStress(ship, 6); // missile on the tail: +6, but an intact ship stays calm (at most 7)
        Assert.Equal(7, sim.Ships[ship].Stress);
        sim.Ships[ship].Armor[ArmorValues.Rear] = 0;
        sim.Ships[ship].Armor[ArmorValues.Front] = 0; // health 50: + aggression, at most 28
        sim.Ships[ship].Stress = 27;
        sim.HandleStress(ship, 0);
        Assert.Equal(28, sim.Ships[ship].Stress);
        sim.HandleStress(ship, -1); // quiet tick: recovery 8, then + 3
        Assert.Equal(23, sim.Ships[ship].Stress);
        sim.Ships[ship].Stress = 0;
        sim.HandleStress(ship, 2);
        Assert.Equal(0, sim.Ships[ship].Stress); // -8, then +3 for the damage: -5, floored at 0 last
    }

    [Fact]
    public void Panic_makes_a_generic_confed_pilot_leave()
    {
        var sim = TestWorld.Create();
        short ship = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 0);
        sim.Ships[ship].Stress = 30;
        Assert.Equal(ShipManeuver.OutaHere, sim.PickRegularManeuver(ship, 0));
    }

    [Fact]
    public void Any_defense_can_draw_the_list_terminator()
    {
        var (sim, ship, _) = Duel(); // level 2: nine defense maneuvers
        int count = AiTables.DefenseManeuvers(2).Length;
        uint seed = 1;
        while (new CRandom(seed).BelowOrEqual(count) != count)
            seed++;
        sim.Random.Seed = seed;
        Assert.Equal(ShipManeuver.None, sim.AnyDefense(ship));
    }

    [Fact]
    public void A_rated_pilot_takes_its_own_table()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short iceman = TestWorld.AddShip(sim, ObjectType.Rapier, Side.Imperial, 0, 0, 3000, rating: 3);
        sim.Ships[iceman].Maneuver = ShipManeuver.None;
        var peek = TestWorld.Peek(sim);
        // Iceman, event 8 (target dying), calm: {100, VEER_AWAY, NONE}; nothing runs, so one roll decides.
        var expected = peek.BelowOrEqual(100) >= 100 ? ShipManeuver.None : ShipManeuver.VeerAway;
        sim.ProcessManeuverNode(iceman, 8);
        Assert.Equal(expected, sim.Ships[iceman].Maneuver);
    }

    [Fact]
    public void Hard_brake_kills_the_engines_and_counts_as_out_of_the_fight()
    {
        var (sim, ship, _) = Duel();
        sim.ResetManeuver(ship, ShipManeuver.HardBrake);
        sim.ManeuverHardBrake(ship);
        Assert.Equal(SpecialManeuver.KillEngines, sim.Ships[ship].SpecialManeuver);
        Assert.Equal(1, sim.Ships[ship].Sequence);
        Assert.True(sim.Unactive(ship));
        for (int tick = 0; tick < 4; tick++)
            sim.ManeuverHardBrake(ship);
        Assert.Equal(2, sim.Ships[ship].Sequence);
        sim.ManeuverHardBrake(ship);
        Assert.Equal(SpecialManeuver.SuperBrake, sim.Ships[ship].SpecialManeuver);
        for (int tick = 0; tick < 4; tick++)
            sim.ManeuverHardBrake(ship);
        Assert.Equal(ShipManeuver.None, sim.Ships[ship].Maneuver);
    }

    [Fact]
    public void Tight_loop_pitches_half_a_loop_twice()
    {
        var (sim, ship, _) = Duel();
        sim.ResetManeuver(ship, ShipManeuver.TightLoop);
        sim.ManeuverTightLoop(ship);
        Assert.Equal(180, sim.Ships[ship].PitchGoal);
        sim.ManeuverTightLoop(ship); // still turning
        Assert.Equal(1, sim.Ships[ship].Sequence);
        sim.Ships[ship].PitchGoal = 0;
        sim.ManeuverTightLoop(ship);
        sim.ManeuverTightLoop(ship);
        Assert.Equal(180, sim.Ships[ship].PitchGoal);
        sim.Ships[ship].PitchGoal = 0;
        sim.ManeuverTightLoop(ship);
        Assert.Equal(ShipManeuver.None, sim.Ships[ship].Maneuver);
    }

    [Fact]
    public void Roll_over_rolls_minus_180_180_or_540_degrees()
    {
        var (sim, ship, _) = Duel();
        var seen = new HashSet<short>();
        for (int i = 0; i < 40; i++)
        {
            sim.ResetManeuver(ship, ShipManeuver.RollOver);
            sim.ManeuverRollOver(ship);
            seen.Add(sim.Ships[ship].RollGoal);
        }
        Assert.Equal(new HashSet<short> { -180, 180, 540 }, seen);
    }

    [Fact]
    public void Maneuvers_are_dispatched_by_number_not_by_enum_name()
    {
        var (sim, ship, target) = Duel();
        // 31 is named KILL_MISSILE but runs Mstrafe_n_roll: with the guns reloading it rolls 45 degrees.
        sim.ResetManeuver(ship, ShipManeuver.KillMissile);
        sim.Objects[ship].Counter = 5;
        sim.PerformManeuver(ship);
        Assert.Equal(45, sim.Ships[ship].RollGoal);
        Assert.Equal(target, sim.Ships[ship].Target);
    }

    [Fact]
    public void Too_close_turns_into_veer_away()
    {
        var (sim, ship, _) = Duel();
        sim.Objects[ship].Position = TestWorld.Units(0, 0, 40); // inside the "too close" range
        sim.ResetManeuver(ship, ShipManeuver.FullAhead);
        sim.Ships[ship].Count = 10;
        sim.PerformManeuver(ship);
        Assert.Equal(ShipManeuver.VeerAway, sim.Ships[ship].Maneuver);
    }

    [Fact]
    public void Without_a_target_the_maneuver_completes_and_the_reroll_number_is_drawn()
    {
        var (sim, ship, _) = Duel();
        sim.Ships[ship].Target = -1;
        sim.ResetManeuver(ship, ShipManeuver.None);
        var expected = TestWorld.Peek(sim);
        expected.Next(); // RandomBelowOrEqual(100) < 0 of the unchanged maneuver (Kilrathi Saga behaviour)
        sim.PerformManeuver(ship);
        Assert.Equal(ShipManeuver.None, sim.Ships[ship].Maneuver);
        Assert.Equal(expected.Seed, sim.Random.Seed);
    }

    [Fact]
    public void Chill_resumes_the_maneuver_stored_in_its_step()
    {
        var (sim, ship, target) = Duel();
        sim.Objects[target].Position = TestWorld.Units(0, 0, 3500);
        sim.Objects[ship].Position = TestWorld.Units(0, 0, 3450); // just behind the player
        sim.ResetManeuver(ship, ShipManeuver.Chill);
        sim.Ships[ship].Sequence = (sbyte)ShipManeuver.SitNSpin;
        sim.ShipVsShip(ship, target); // behind the player within 1000
        sim.ManeuverChill(ship, target);
        Assert.Equal(ShipManeuver.SitNSpin, sim.Ships[ship].Maneuver);
    }

    [Fact]
    public void The_dogfight_tick_classifies_a_head_on_pass()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim); // facing +z
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 3000);
        sim.Objects[ship].AlterYaw(180); // facing the player
        sim.Ships[ship].Target = 0;
        sim.Maneuvering(ship, 0);
        Assert.Equal(4, sim.Ships[ship].IntelligenceEvent); // head on
        sim.Ships[ship].AiCooldown = 3;
        sim.Maneuvering(ship, 0);
        Assert.Equal(7, sim.Ships[ship].IntelligenceEvent); // just hit
    }

    [Fact]
    public void A_dying_target_is_event_8_and_a_gone_target_is_replaced()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 3000);
        short other = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 2000, 0, 3000);
        sim.Ships[other].SpecialManeuver = SpecialManeuver.Unknown9;
        sim.Ships[ship].Target = (sbyte)other;
        sim.IntelligenceEvents(ship);
        Assert.Equal(8, sim.Ships[ship].IntelligenceEvent);
        sim.RemoveObject(other);
        sim.IntelligenceEvents(ship);
        Assert.Equal(-1, sim.Ships[ship].IntelligenceEvent);
        Assert.Equal(0, sim.Ships[ship].Target); // the player, the only enemy left
    }
}
