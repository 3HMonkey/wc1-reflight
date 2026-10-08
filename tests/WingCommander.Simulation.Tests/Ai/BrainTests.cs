using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Ai;

public class BrainTests
{
    /// <summary>One AI tick of <paramref name="ship"/> as in a frame: the crash cache is cleared (every
    /// frame does that first) and the turn regulator is made due (new ships start with a staggered
    /// regulator, so their first ticks fall on different frames).</summary>
    private static void Tick(SpaceSimulation sim, short ship)
    {
        sim.ClearCrashCache();
        sim.Ships[ship].TurnRegulator = 1;
        sim.ShipIntelligence(ship);
    }

    [Fact]
    public void The_turn_regulator_gives_one_ai_tick_per_interval()
    {
        var sim = TestWorld.Create();
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 0);
        sim.Ships[ship].PilotLevel = 0; // interval 5
        sim.FindNextShipTurnSlot(ship);
        Assert.Equal(5, sim.Ships[ship].TurnInterval);
        int ticks = 0;
        for (int frame = 0; frame < 20; frame++)
        {
            if (!sim.RegulateTurn(ship))
                ticks++;
        }
        Assert.Equal(4, ticks);
        Assert.Equal(4, sim.Ships[ship].Turn);
        sim.Ships[ship].SpecialManeuver = SpecialManeuver.Unknown9; // dying ships do not think
        Assert.True(sim.RegulateTurn(ship));
    }

    [Fact]
    public void A_kilrathi_patrol_finds_the_player_breaks_formation_and_attacks()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 8000);
        sim.Ships[ship].MissionType = ShipMissionType.Patrol;
        sim.Ships[ship].Objective = ShipObjective.None;
        sim.Ships[ship].WingLeader = -1;
        Tick(sim, ship);
        Assert.Equal(ShipObjective.Wander, sim.Ships[ship].Objective);
        Assert.Equal(ShipTactic.ApproachTarget, sim.Ships[ship].Tactic);
        Tick(sim, ship);
        Assert.Equal(0, sim.Ships[ship].Target); // scanned 14000
        Tick(sim, ship);
        Assert.Equal(ShipObjective.BreakFormation, sim.Ships[ship].Objective); // within 10000
        for (int tick = 0; tick < 10; tick++)
            Tick(sim, ship);
        Assert.Equal(ShipObjective.EngageEnemy, sim.Ships[ship].Objective);
        Tick(sim, ship); // the dogfight tick picks the player again
        Assert.Equal(0, sim.Ships[ship].Target);
    }

    [Fact]
    public void The_players_wingman_asks_before_engaging_and_hunter_goes_after_40_ticks()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short wingman = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 300, 0, -200, rating: 1); // Hunter
        sim.Ships[wingman].MissionType = ShipMissionType.Wingman;
        sim.Ships[wingman].Objective = ShipObjective.None;
        sim.Ships[wingman].WingLeader = 0;
        sim.Ships[wingman].FormationOffset = new ShortVector(300, 0, -200);
        sim.YourWingman = wingman;
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 5000);
        sim.Ships[enemy].Target = 0;

        Tick(sim, wingman);
        Assert.Equal(ShipObjective.HoldFormation, sim.Ships[wingman].Objective);
        Tick(sim, wingman);
        Assert.Equal(3, sim.Ships[wingman].WingmanMessageState); // "request permission to engage"
        Assert.Equal(40, sim.AutoEngageTimer);
        for (int tick = 0; tick < 39; tick++)
            Tick(sim, wingman);
        Assert.Equal(ShipObjective.HoldFormation, sim.Ships[wingman].Objective);
        Tick(sim, wingman);
        Assert.True(sim.EngageAllowed); // Hunter does not wait for the answer
        Assert.Equal(ShipObjective.EngageEnemy, sim.Ships[wingman].Objective);
        Assert.Equal(enemy, sim.Ships[wingman].Target);
    }

    [Fact]
    public void The_players_wingman_reports_the_first_enemy_of_a_wave()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short wingman = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 300, 0, -200, rating: 4); // Angel
        sim.Ships[wingman].MissionType = ShipMissionType.Wingman;
        sim.Ships[wingman].Objective = ShipObjective.HoldFormation;
        sim.Ships[wingman].WingLeader = 0;
        sim.YourWingman = wingman;
        sim.CameraViewMode = 0;
        sim.CurrentWave = 2;
        TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 12000);
        Tick(sim, wingman);
        Assert.Equal(2, sim.Ships[wingman].WingmanMessageState); // "enemy sighted"
        Assert.Equal(2, sim.EnemySighting);

        sim.Ships[wingman].WingmanMessageState = -1;
        sim.EnemySighting = 0x7fff;
        ((DefaultCockpitState)sim.Cockpit).MessageActive = true; // the message line is busy
        Tick(sim, wingman);
        Assert.Equal(-1, sim.Ships[wingman].WingmanMessageState);
    }

    [Fact]
    public void A_confed_capital_ship_defends_itself_with_its_turrets()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short exeter = TestWorld.AddShip(sim, ObjectType.Exeter, Side.Imperial, 0, 0, 0);
        sim.Ships[exeter].MissionType = ShipMissionType.Patrol;
        sim.Ships[exeter].Tactic = ShipTactic.None;
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 9000);
        sim.CapitalShipIntelligence(exeter);
        Assert.Equal(ShipTactic.SelfDefense, sim.Ships[exeter].Tactic);
        Assert.Equal(enemy, sim.Ships[exeter].Target);
        sim.RemoveObject(enemy);
        sim.CapitalShipIntelligence(exeter);
        Assert.Equal(ShipTactic.None, sim.Ships[exeter].Tactic); // nobody left
    }

    [Fact]
    public void The_kilrathi_starbase_turns_and_fires()
    {
        var sim = TestWorld.Create();
        short starbase = TestWorld.AddShip(sim, ObjectType.KilrathiBase, Side.Kilrathi, 0, 0, 0);
        sim.Ships[starbase].MissionType = ShipMissionType.None;
        sim.CapitalShipIntelligence(starbase);
        Assert.Equal(4, sim.Objects[starbase].YawRotation);
    }

    [Fact]
    public void A_routing_kilrathi_climbs_away_and_vanishes_beyond_16000()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 15000);
        sim.Ships[ship].MissionType = ShipMissionType.Rout;
        sim.Ships[ship].WingLeader = -1;
        Tick(sim, ship);
        Assert.NotEqual(0, sim.Ships[ship].PitchGoal); // straight up (slot parity decides the direction)
        sim.Objects[ship].Position = TestWorld.Units(0, 0, 17000);
        Tick(sim, ship);
        Assert.Equal(ObjectClass.Null, sim.Objects[ship].Class);
    }

    [Fact]
    public void A_strike_ship_attacks_its_goal_within_5000()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short exeter = TestWorld.AddShip(sim, ObjectType.Exeter, Side.Imperial, 0, 0, 4000);
        sim.Ships[exeter].MissionIndex = 7;
        short striker = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 7000);
        sim.Ships[striker].MissionType = ShipMissionType.Strike;
        sim.Ships[striker].MissionShip = 7;
        sim.Ships[striker].Objective = ShipObjective.None;
        Tick(sim, striker);
        Assert.Equal(ShipObjective.HomeBase, sim.Ships[striker].Objective);
        Tick(sim, striker);
        Assert.Equal(ShipObjective.DestroyShip, sim.Ships[striker].Objective);
        Assert.Equal(exeter, sim.Ships[striker].Target);
    }

    [Fact]
    public void A_strike_without_its_goal_patrols_or_routs()
    {
        var sim = TestWorld.Create();
        short striker = TestWorld.AddShip(sim, ObjectType.Dralthi, Side.Kilrathi, 0, 0, 7000);
        sim.Ships[striker].MissionType = ShipMissionType.Strike;
        sim.Ships[striker].MissionShip = 3;
        Tick(sim, striker);
        Assert.Equal(ShipMissionType.Patrol, sim.Ships[striker].MissionType);
        sim.Ships[striker].MissionType = ShipMissionType.Strike;
        sim.MissionShips[3].State = 3; // destroyed
        Tick(sim, striker);
        Assert.Equal(ShipMissionType.Rout, sim.Ships[striker].MissionType);
    }

    [Fact]
    public void An_escort_returns_to_its_buddy_and_flies_alongside()
    {
        var sim = TestWorld.Create();
        short buddy = TestWorld.AddShip(sim, ObjectType.Drayman, Side.Imperial, 0, 0, 0);
        sim.Ships[buddy].MissionIndex = 4;
        short escort = TestWorld.AddShip(sim, ObjectType.Rapier, Side.Imperial, 0, 0, 7000);
        sim.Ships[escort].MissionType = ShipMissionType.Escort;
        sim.Ships[escort].MissionShip = 4;
        sim.Ships[escort].Objective = ShipObjective.None;
        Tick(sim, escort);
        Assert.Equal(ShipObjective.Wander, sim.Ships[escort].Objective);
        sim.Ships[escort].Turn = 3; // the next tick is phase 4: distance check
        Tick(sim, escort);
        Assert.Equal(ShipObjective.HomeBase, sim.Ships[escort].Objective);
        sim.Objects[escort].Position = TestWorld.Units(0, 0, 800);
        Tick(sim, escort);
        Assert.Equal(ShipObjective.Wander, sim.Ships[escort].Objective);
        sim.RemoveObject(buddy);
        Tick(sim, escort);
        Assert.Equal(ShipMissionType.Patrol, sim.Ships[escort].MissionType);
    }

    [Fact]
    public void A_kilrathi_ship_jumps_out_at_its_warp_point()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        sim.Objects[0].Position = TestWorld.Units(0, 0, -20000);
        sim.MissionNavPoints[2].Position = TestWorld.Units(0, 0, 5000);
        short ship = TestWorld.AddShip(sim, ObjectType.Dorkir, Side.Kilrathi, 0, 0, 5000);
        sim.Ships[ship].MissionIndex = 9;
        sim.Ships[ship].MissionType = ShipMissionType.GotoWarp;
        sim.Ships[ship].MissionShip = 2;
        sim.InitIntelligenceData(ship);
        sim.Ships[ship].Objective = ShipObjective.None;
        sim.Ships[ship].Tactic = ShipTactic.None;
        int frame = 0;
        while (sim.Objects[ship].Class != ObjectClass.Null && frame < 1200)
        {
            sim.Update3Space();
            frame++;
        }
        Assert.Equal(ObjectClass.Null, sim.Objects[ship].Class);
        Assert.Equal(2, sim.MissionShips[9].State); // left by warp
        Assert.InRange(frame, 250, 1000);
        Assert.Contains("flash", events.Calls);
    }
}
