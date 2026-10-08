using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Ai;

public class ObjectiveTrackingTests
{
    /// <summary>Two nav objectives (0 at 10000 ahead, 1 at 20000 ahead) and the home base (2).</summary>
    private static (SpaceSimulation Sim, RecordingEvents Events) NavMission()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        sim.MissionNavPoints[1].Type = 1;
        sim.MissionNavPoints[2].Type = 1;
        sim.MissionObjectives[0] = new MissionObjective { Type = 0, Index = 1, Position = TestWorld.Units(0, 0, 10000), DisplayName = "", Name = "" };
        sim.MissionObjectives[1] = new MissionObjective { Type = 0, Index = 2, Position = TestWorld.Units(0, 0, 20000), DisplayName = "", Name = "" };
        sim.MissionObjectives[2] = new MissionObjective { Type = 1, Index = 0, Position = FixedVector.Zero, DisplayName = "", Name = "" };
        sim.MissionObjectives[3].Type = -1;
        sim.FlightPath[0] = 0;
        sim.FlightPath[1] = 1;
        sim.FlightPath[2] = 2;
        sim.FlightPath[3] = -1;
        sim.MissionObjectiveCount = 3;
        sim.CurrentNavPointIndex = 0;
        sim.CurrentObjective = 0;
        sim.Ships[0].MissionShip = -1;
        return (sim, events);
    }

    [Fact]
    public void Reaching_the_current_nav_point_flags_it_and_advances_the_destination()
    {
        var (sim, events) = NavMission();
        sim.Objects[0].Position = TestWorld.Units(0, 0, -7000);
        sim.UpdateObjectiveLocation(0);
        Assert.False(sim.Sighted(0)); // 17000 away: sighting needs 16000 (nav points have no object to see)
        sim.Objects[0].Position = TestWorld.Units(0, 0, 1000);
        sim.UpdateObjectiveLocation(0);
        Assert.True(sim.Sighted(0));
        Assert.False(sim.Visited(0));
        sim.Objects[0].Position = TestWorld.Units(0, 0, 8600); // within 1500
        sim.UpdateObjectiveLocation(0);
        Assert.True(sim.Visited(0));
        Assert.Contains("message ObjectiveReached None", events.Calls);
        Assert.Equal(1, sim.CurrentObjective);
        Assert.Contains("destination", events.Calls);
    }

    [Fact]
    public void A_visited_objective_reached_again_says_already_visited()
    {
        var (sim, events) = NavMission();
        sim.FlagObjective(0, MissionObjective.FlagVisited);
        sim.FlagReached(0, false);
        Assert.Contains("message AlreadyVisited None", events.Calls);
    }

    [Fact]
    public void An_escort_must_wait_for_its_ship()
    {
        var (sim, events) = NavMission();
        short drayman = TestWorld.AddShip(sim, ObjectType.Drayman, Side.Imperial, 0, 0, 3000);
        sim.Ships[drayman].MissionIndex = 5;
        sim.Ships[0].MissionType = ShipMissionType.Escort;
        sim.Ships[0].MissionShip = 5;
        sim.FlagReached(0, false);
        Assert.Contains($"message WaitFor {ObjectType.Drayman}", events.Calls);
        Assert.False(sim.Visited(0)); // the current objective stays
        Assert.Equal(0, sim.CurrentObjective);
        sim.FlagReached(0, true); // reached by a ship flying the path
        Assert.True(sim.Visited(0));
    }

    [Fact]
    public void Escort_and_destroy_objectives_count_as_reached_within_6000()
    {
        var (sim, _) = NavMission();
        short target = TestWorld.AddShip(sim, ObjectType.Fralthi, Side.Kilrathi, 0, 0, 5000);
        sim.Ships[target].MissionIndex = 6;
        sim.MissionObjectives[0] = new MissionObjective { Type = 4, Index = 6, DisplayName = "", Name = "" };
        sim.CheckVisit(0, 5000);
        Assert.True(sim.Visited(0));
        sim.MissionObjectives[1] = new MissionObjective { Type = 0, Index = 2, DisplayName = "", Name = "" };
        sim.CheckVisit(1, 5000);
        Assert.False(sim.Visited(1)); // nav points need 1500
    }

    [Fact]
    public void A_lost_objective_is_skipped_by_the_nav_computer()
    {
        var (sim, events) = NavMission();
        sim.MissionObjectives[0] = new MissionObjective { Type = 4, Index = 6, Position = TestWorld.Units(0, 0, 10000), DisplayName = "", Name = "" };
        sim.MissionShips[6].State = 3; // destroyed
        Assert.True(sim.ObjectiveLost(0));
        Assert.True(sim.CheckObjectives());
        Assert.Equal(1, sim.CurrentObjective);
        Assert.Contains("destination", events.Calls);
    }

    [Fact]
    public void The_record_before_the_objective_table_overlays_the_animation_indices()
    {
        var sim = TestWorld.Create();
        sim.Objects[59].AnimationIndex = 3;
        sim.Objects[60].AnimationIndex = 1;
        sim.Objects[ObjectSlots.Eye].AnimationIndex = 0x0105;
        var record = sim.ObjectiveRecord(-1);
        Assert.Equal(0x10003, record.Type);
        Assert.Equal(5, record.Index);
        Assert.Equal(1, record.Flags);
        Assert.True(sim.Visited(-1));
        sim.FlagObjective(-1, MissionObjective.FlagSighted);
        Assert.Equal(0x0505, sim.Objects[ObjectSlots.Eye].AnimationIndex);
    }

    [Fact]
    public void Find_objective_by_type_and_index()
    {
        var (sim, _) = NavMission();
        Assert.Equal(2, sim.FindObjective(1, -1));
        Assert.Equal(1, sim.FindObjective(0, 2));
        Assert.Equal(-1, sim.FindObjective(3, -1));
    }
}

public class CommTests
{
    private static (SpaceSimulation Sim, RecordingEvents Events, short Wingman, short Enemy) Flight(sbyte wingmanRating = 1)
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        short wingman = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 300, 0, -200, wingmanRating);
        sim.Ships[wingman].MissionType = ShipMissionType.Wingman;
        sim.Ships[wingman].Objective = ShipObjective.HoldFormation;
        sim.Ships[wingman].WingLeader = 0;
        sim.YourWingman = wingman;
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 6000);
        return (sim, events, wingman, enemy);
    }

    [Fact]
    public void Attack_my_target_engages_a_kilrathi_target_and_refuses_a_friend()
    {
        var (sim, _, wingman, enemy) = Flight();
        sim.Ships[0].Target = (sbyte)enemy;
        sim.Request(0, wingman, CommCommand.AttackTarget);
        Assert.Equal(ShipObjective.EngageEnemy, sim.Ships[wingman].Objective);
        Assert.Equal(enemy, sim.Ships[wingman].Target);
        Assert.Equal(0, sim.Ships[wingman].WingmanMessageState); // "affirmative"
        Assert.True(sim.EngageAllowed);

        sim.Ships[0].Target = (sbyte)wingman;
        sim.Request(0, wingman, CommCommand.AttackTarget);
        Assert.Equal(1, sim.Ships[wingman].WingmanMessageState); // "negative"
        sim.Ships[0].Target = -1;
        sim.Request(0, wingman, CommCommand.AttackTarget); // no target: aeShipSide[-1] reads Imperial
        Assert.Equal(1, sim.Ships[wingman].WingmanMessageState);
    }

    [Fact]
    public void Help_me_out_engages_whoever_targets_the_player_else_forms_up()
    {
        var (sim, _, wingman, enemy) = Flight();
        sim.Ships[enemy].Target = 0;
        sim.Request(0, wingman, CommCommand.HelpMeOut);
        Assert.Equal(enemy, sim.Ships[wingman].Target);
        Assert.Equal(ShipObjective.EngageEnemy, sim.Ships[wingman].Objective);

        sim.Ships[enemy].Target = -1;
        sim.Request(0, wingman, CommCommand.HelpMeOut);
        Assert.Equal(ShipObjective.HoldFormation, sim.Ships[wingman].Objective); // treated as "form on my wing"
        Assert.Equal(-150, sim.AutoEngageTimer);
        Assert.False(sim.EngageAllowed);
    }

    [Theory]
    [InlineData((sbyte)-1, true)] // generic: yes
    [InlineData((sbyte)2, false)] // Bossman: never
    [InlineData((sbyte)6, false)] // Maniac: never
    [InlineData((sbyte)3, false)] // Iceman: only in canned sequences
    public void Return_to_base_depends_on_the_pilot(sbyte rating, bool routs)
    {
        var (sim, _, wingman, _) = Flight(rating);
        sim.Request(0, wingman, CommCommand.ReturnToBase);
        Assert.Equal(routs, sim.Ships[wingman].MissionType == ShipMissionType.Rout);
    }

    [Fact]
    public void A_taunted_kilrathi_ace_always_answers_and_turns_on_the_player()
    {
        var (sim, _, _, enemy) = Flight();
        sim.Ships[enemy].Rating = 10; // Dakhath
        sim.Ships[enemy].PilotLevel = 15;
        sim.FlagAce(1, 8); // already greeted the player (else the greeting replaces the answer)
        sim.Ships[enemy].Target = -1;
        sim.Request(0, enemy, CommCommand.SlagOff);
        Assert.Equal(3, sim.Ships[enemy].WingmanMessageState); // command 5 - 2
        Assert.Equal(0, sim.Ships[enemy].Target);
        Assert.Equal(ShipObjective.EngageEnemy, sim.Ships[enemy].Objective);
    }

    [Fact]
    public void Break_and_attack_only_from_formation()
    {
        var (sim, _, wingman, _) = Flight();
        sim.Request(0, wingman, CommCommand.BreakAndAttack);
        Assert.Equal(ShipObjective.BreakFormation, sim.Ships[wingman].Objective);
        Assert.Equal(0, sim.Ships[wingman].WingmanMessageState);
        sim.Request(0, wingman, CommCommand.BreakAndAttack);
        Assert.Equal(1, sim.Ships[wingman].WingmanMessageState);
    }

    [Fact]
    public void Paladin_breaks_formation_while_an_enemy_sits_on_the_players_tail()
    {
        var (sim, _, wingman, enemy) = Flight(5); // Paladin
        sim.Objects[enemy].Position = TestWorld.Units(0, 0, -2000); // behind the player, facing it
        sim.Ships[enemy].Target = 0;
        sim.Request(0, wingman, CommCommand.KeepFormation);
        Assert.Equal(ShipObjective.BreakFormation, sim.Ships[wingman].Objective);
        Assert.Equal(1, sim.Ships[wingman].WingmanMessageState);

        sim.Ships[enemy].Target = -1;
        sim.Request(0, wingman, CommCommand.KeepFormation);
        Assert.Equal(-150, sim.AutoEngageTimer);
        Assert.Equal(0, sim.Ships[wingman].WingmanMessageState);
    }

    [Fact]
    public void Radio_silence_mutes_the_wingman_after_the_reply()
    {
        var (sim, _, wingman, _) = Flight();
        sim.Request(0, wingman, CommCommand.KeepRadioSilence);
        Assert.True(sim.RadioSilence);
        Assert.Equal(0, sim.Ships[wingman].WingmanMessageState); // the reply still comes
        sim.SendMessage(wingman, 4);
        Assert.Equal(-1, sim.Ships[wingman].WingmanMessageState);
        sim.Request(0, wingman, CommCommand.BroadcastFreely);
        Assert.False(sim.RadioSilence);
    }

    [Fact]
    public void Landing_needs_an_empty_sky_and_a_reason()
    {
        var (sim, _, _, enemy) = Flight();
        short claw = TestWorld.AddShip(sim, ObjectType.TigersClaw, Side.Imperial, 0, 0, -3000);
        sim.MissionObjectives[0].Type = -1;
        sim.MissionObjectiveCount = 0;
        sim.Request(0, claw, CommCommand.RequestLanding);
        Assert.False(sim.LandingAuthorized); // a Salthi within 20000
        Assert.Equal(9, sim.Ships[claw].WingmanMessageState);
        sim.RemoveObject(enemy);
        sim.Request(0, claw, CommCommand.RequestLanding);
        Assert.False(sim.LandingAuthorized); // healthy, no kill, plenty of fuel, nothing achieved
        sim.PlayerKillCount = 1;
        sim.Request(0, claw, CommCommand.RequestLanding);
        Assert.True(sim.LandingAuthorized);
        Assert.Equal(8, sim.Ships[claw].WingmanMessageState);
    }

    [Fact]
    public void Unknown_commands_and_recipients_are_ignored()
    {
        var (sim, _, wingman, _) = Flight();
        var before = sim.ComputeStateHash();
        sim.Request(0, wingman, (CommCommand)(-1)); // the comm menu's off-by-one
        sim.Request(0, wingman, (CommCommand)13);
        sim.Request(0, -1, CommCommand.AttackTarget);
        Assert.Equal(before, sim.ComputeStateHash());
    }
}

public class AutopilotTests
{
    private static (SpaceSimulation Sim, RecordingEvents Events, short Wingman) Patrol(int destinationZ = 40000)
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        sim.Ships[0].MissionIndex = 0;
        sim.PlayerMissionShipIndex = 0;
        short wingman = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 300, 0, -200, rating: 1);
        sim.Ships[wingman].MissionIndex = 1;
        sim.Ships[wingman].WingLeader = 0;
        sim.Ships[wingman].FormationOffset = new ShortVector(300, 0, -200);
        sim.InitialMissionShipIndices[0] = 1;
        sim.YourWingman = wingman;
        sim.MissionObjectives[0] = new MissionObjective { Type = 0, Index = 1, Position = TestWorld.Units(0, 0, destinationZ), DisplayName = "", Name = "" };
        sim.MissionObjectives[1].Type = -1;
        sim.FlightPath[0] = 0;
        sim.FlightPath[1] = -1;
        sim.MissionObjectiveCount = 1;
        sim.CurrentObjective = 0;
        sim.CurrentNavPointIndex = 0;
        return (sim, events, wingman);
    }

    [Fact]
    public void The_autopilot_refuses_near_the_objective_near_kilrathi_and_in_hazards()
    {
        var (sim, events, _) = Patrol(5000);
        Assert.False(sim.BeginAutopilot());
        Assert.Contains("message AlreadyNear None", events.Calls);

        (sim, events, _) = Patrol();
        TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 12000);
        Assert.False(sim.BeginAutopilot());
        Assert.Contains("message EnemyNear None", events.Calls);

        (sim, events, _) = Patrol();
        sim.ActiveHazardField = 0;
        Assert.False(sim.BeginAutopilot());
        Assert.Contains("message HazardNear None", events.Calls);
        Assert.False(sim.AutoPilotValid(false)); // the cockpit light's poll shows nothing
        Assert.Equal(1, events.Count("message "));
        Assert.Equal(0, sim.CannedSceneMode);
    }

    [Fact]
    public void The_autopilot_takes_the_team_to_the_destination()
    {
        var (sim, events, wingman) = Patrol();
        Assert.True(sim.BeginAutopilot());
        Assert.Equal(4, sim.CannedSceneMode); // the AI rests during the cinematic
        Assert.Contains("resetSound", events.Calls);
        Assert.Equal(60 << 8, sim.Objects[0].Speed);
        Assert.Equal(sim.Objects[0].Forward, sim.Objects[wingman].Forward); // flies in the player's frame
        for (int frame = 0; frame < 120; frame++)
            sim.Update3Space(); // the UI's cinematic
        sim.AutopilotTravel();
        Assert.Equal(0, sim.CannedSceneMode);
        sim.EndAutopilot();
        int distance = VectorMath.Delta(sim.Objects[0].Position, TestWorld.Units(0, 0, 40000)).Magnitude() >> 8;
        Assert.InRange(distance, 1000, 1500); // stopped short of the destination, one step back
        short cruise = ObjectTypeTable.Get(ObjectType.Hornet).CruiseVelocity;
        Assert.Equal(cruise << 8, sim.Objects[0].Speed);
        Assert.Equal(cruise << 8, sim.Objects[wingman].Speed);
        var expected = sim.PositionRelativeIjk(0, 300, 0, -200);
        Assert.True(VectorMath.Delta(expected, sim.Objects[wingman].Position).Magnitude() >> 8 < 100);
    }

    [Fact]
    public void Travellers_without_a_wingman_slot_line_up_behind_the_player()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short ship = TestWorld.AddShip(sim, ObjectType.Rapier, Side.Imperial, 0, 0, 0);
        sim.Ships[ship].WingLeader = 5;
        sim.AutopilotFormationShipCount = 2;
        short slot = 0;
        sim.AutoPosition(ship, ref slot);
        Assert.Equal(1, slot);
        Assert.Equal(sim.PositionRelativeIjk(0, 650, 0, -1800), sim.Objects[ship].Position);
        sim.AutoPosition(ship, ref slot); // the last one flies centred
        Assert.Equal(sim.PositionRelativeIjk(0, 0, 0, -1800), sim.Objects[ship].Position);
    }
}

public class WarpTests
{
    [Fact]
    public void A_jump_out_leaves_a_flash_and_the_ship_shrinks_away()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        short ship = TestWorld.AddShip(sim, ObjectType.Drayman, Side.Imperial, 0, 0, 5000);
        sim.Ships[ship].MissionIndex = 4;
        sim.Warp(ship);
        Assert.Contains("flash", events.Calls);
        Assert.Equal(ShipManeuver.WarpingOut, sim.Ships[ship].Maneuver);
        Assert.Equal(6, sim.Objects[ship].Counter);
        bool flash = false;
        for (short obj = ObjectSlots.FirstEffect; obj <= ObjectSlots.LastMoving; obj++)
            flash |= sim.Objects[obj].Type == ObjectType.HyperspaceJumpFlash;
        Assert.True(flash);
        short scale = sim.Objects[ship].Scale;
        sim.HouseKeepObjects();
        Assert.Equal(scale >> 1, sim.Objects[ship].Scale);
        for (int frame = 0; frame < 6; frame++)
            sim.HouseKeepObjects();
        Assert.Equal(ObjectClass.Null, sim.Objects[ship].Class);
        Assert.Equal(2, sim.MissionShips[4].State);
    }

    [Fact]
    public void An_arrival_flags_the_nav_point_and_heads_home()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        sim.CurrentNavPoint = 3;
        sim.MissionObjectives[0] = new MissionObjective { Type = 0, Index = 3, DisplayName = "", Name = "" };
        sim.MissionObjectives[1].Type = -1;
        sim.FlightPath[0] = 0;
        sim.FlightPath[1] = -1;
        sim.MissionObjectiveCount = 1;
        sim.CurrentObjective = -1;
        short ship = TestWorld.AddShip(sim, ObjectType.Exeter, Side.Imperial, 0, 0, 5000);
        sim.ArriveFromWarp(ship);
        Assert.True(sim.Visited(0));
        Assert.Equal(ShipMissionType.ComeHome, sim.Ships[ship].MissionType);
        Assert.Equal(sim.Ships[ship].MaximumSpeed << 8, sim.Objects[ship].Speed);
        Assert.Contains("flash", events.Calls);
    }
}
