using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;
using WingCommander.Tests;

namespace WingCommander.Simulation.Tests.Missions;

public class MissionSetupTests
{
    public static TheoryData<int> DataSets => new() { 0, 1, 2 };

    private static SpaceSimulation CreateSimulation(int dataSet = 0) =>
        new(new CRandom(1), new GameDirectoryResources(GameData.Require())) { CampaignDataSet = (short)dataSet };

    [DataTheory]
    [MemberData(nameof(DataSets))]
    public void Every_mission_sets_up_and_walks_its_nav_spheres(int dataSet)
    {
        var sim = CreateSimulation(dataSet);
        var module = sim.MissionModule;
        int checkedMissions = 0;
        for (short series = 0; series < MissionModule.SeriesSlots; series++)
        {
            for (short mission = 0; mission < MissionModule.MissionsPerSeries; mission++)
            {
                var data = module.GetMission(series, mission);
                if (data is null)
                    continue;
                sim.TrainSimActive = series == 0;
                CheckMission(sim, data);
                checkedMissions++;
            }
        }
        Assert.True(checkedMissions >= 22);
    }

    private static void CheckMission(SpaceSimulation sim, MissionData data)
    {
        string where = $"set {sim.CampaignDataSet} series {data.Series} mission {data.Mission}";
        sim.Free3Space();
        uint seed = sim.Random.Seed;
        Assert.True(sim.InitMission((short)data.Series, (short)data.Mission), where);

        var header = data.Header;
        var playerRecord = data.Ships[header.PlayerMissionShip];
        ref var player = ref sim.Objects[0];
        Assert.Equal(ObjectClass.Ship, player.Class);
        Assert.Equal(playerRecord.Type, player.Type);
        Assert.Equal(playerRecord.Type, sim.Campaign.PlayerShipType);
        Assert.Equal(header.PlayerMissionShip, sim.Ships[0].MissionIndex);
        Assert.Equal(-1, sim.Ships[0].SpawnNavPoint);
        Assert.Equal(1, sim.Ships[0].PointingMode);
        var expectedPosition = VectorMath.Add(data.NavPoints[header.EntryNavPoint].Position, playerRecord.Position);
        Assert.Equal(expectedPosition, player.Position);
        Assert.Equal(playerRecord.Speed << 8, player.Speed);
        Assert.False(sim.Resources.SectionExists(-5, 0));

        // The player's team is spawned with spawn nav -1.
        foreach (short initial in header.InitialMissionShips)
        {
            if (initial == -1 || sim.FindShipsSphere(initial) != -1)
                continue;
            short slot = sim.FindShipIndex(initial);
            Assert.True(slot > 0, $"{where}: team record {initial} not spawned");
            Assert.Equal(-1, sim.Ships[slot].SpawnNavPoint);
        }

        // Objectives and the flight path.
        Assert.Equal(data.ObjectiveCount, sim.MissionObjectiveCount);
        Assert.Equal(-1, sim.MissionObjectives[sim.MissionObjectiveCount].Type);
        if (sim.MissionObjectiveCount > 0)
        {
            Assert.InRange(sim.CurrentObjective, 0, sim.MissionObjectiveCount - 1);
            Assert.False(sim.HiddenObjective(sim.CurrentObjective), where);
            Assert.Equal((ShipObjective)sim.MissionObjectives[sim.CurrentObjective].Type, sim.Ships[0].Objective);
        }

        // Enter the entry sphere like RunSpaceFlight, then every other active sphere.
        EnterSphere(sim, header.EntryNavPoint, where);
        for (short nav = 0; nav < SpaceSimulation.ActiveNavPointCount; nav++)
        {
            if (nav != header.EntryNavPoint && sim.MissionNavPoints[nav].Type == 1)
                EnterSphere(sim, nav, where);
        }

        // Nav map scaling over the objectives.
        if (sim.MissionObjectiveCount > 0)
        {
            sim.SetScale();
            Assert.True(sim.NavMapScale > 0);
            sim.SetNextDestination();
        }

        // Mission setup is deterministic and consumes no random numbers.
        Assert.Equal(seed, sim.Random.Seed);
    }

    private static void EnterSphere(SpaceSimulation sim, short nav, string where)
    {
        // explode() is phase 2: let the previous sphere's ships count as off-screen (they are removed).
        var alreadyPresent = new HashSet<short> { sim.Ships[0].MissionIndex };
        for (int obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            sim.Objects[obj].ScreenX = ObjectSlots.NotVisible;
            if (sim.Objects[obj].Class != ObjectClass.Null && sim.Ships[obj].SpawnNavPoint == -1)
                alreadyPresent.Add(sim.Ships[obj].MissionIndex);
        }
        sim.SetUpActionSphere(nav);
        Assert.Equal(nav, sim.CurrentNavPoint);

        var navPoint = sim.MissionNavPoints[nav];
        int hazards = 0;
        for (int entry = 0; entry < 10; entry++)
        {
            short record = navPoint.MissionShips[entry];
            if (record == -1)
                continue;
            ref var r = ref sim.MissionShips[record];
            if (r.Type is ObjectType.AsteroidField or ObjectType.MineField)
            {
                hazards++;
                continue;
            }
            short slot = sim.FindShipIndex(record);
            if (slot == -1)
            {
                // Only skipped when no ship slot was free or the record is done / its pilot dead.
                Assert.True(r.State != 0 || !sim.IsAlive(r.Pilot) || CountShips(sim) == 9, $"{where}: nav {nav} record {record} not spawned");
                continue;
            }
            Assert.Equal(r.Type, sim.Objects[slot].Type);
            Assert.Equal(r.Side, sim.Ships[slot].Side);
            if (alreadyPresent.Contains(record))
                continue; // team members survive sphere changes and are not re-initialised
            Assert.Equal(nav, r.NavPoint);
            Assert.Equal(sim.IsTeamMember(record) ? -1 : nav, sim.Ships[slot].SpawnNavPoint);
            if (r.MissionType == ShipMissionType.WarpArrive)
                Assert.Equal(ObjectClass.Futurion, sim.Objects[slot].Class);
            else
                Assert.True(sim.Objects[slot].Class >= ObjectClass.Ship, where);
            Assert.InRange(sim.Ships[slot].TurnRegulator, 1, sim.Ships[slot].TurnInterval + 1);
        }
        Assert.Equal(System.Math.Min(hazards, SpaceSimulation.HazardFieldSlots), sim.HazardFieldCount);
        Assert.Equal(-1, sim.Ships[0].Target);
    }

    private static int CountShips(SpaceSimulation sim)
    {
        int count = 0;
        for (int obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (sim.Objects[obj].Class != ObjectClass.Null)
                count++;
        }
        return count;
    }

    [DataFact]
    public void Enyo_alpha_wing_starts_in_formation_at_the_tigers_claw()
    {
        var sim = CreateSimulation();
        Assert.True(sim.InitMission(1, 0));

        ref var player = ref sim.Objects[0];
        Assert.Equal(ObjectType.Hornet, player.Type);
        Assert.Equal(new FixedVector(0, 0, -1500 * 256), player.Position);
        Assert.Equal(new FixedVector(-256, 0, 0), player.Right); // record "pitch" 180 is applied as yaw
        Assert.Equal(new FixedVector(0, 0, -256), player.Forward);
        Assert.Equal(20 << 8, player.Speed);
        Assert.Equal(ShipMissionType.Patrol, sim.Ships[0].MissionType);
        Assert.Equal(13, sim.Ships[0].PilotLevel);
        Assert.Equal(8, sim.Ships[0].Rating);
        Assert.Equal(ObjectType.Hornet, sim.ResourceSlots[0].Type);
        Assert.Equal(new ShapeRef(22, 0), player.Shape);
        Assert.Equal(ObjectType.LaserCannon, sim.SelectedGunType);

        // Spirit: record 2, formation 0 spot 2 = 750 units along the player's right (-x).
        Assert.Equal(1, sim.YourWingman);
        ref var spirit = ref sim.Objects[1];
        Assert.Equal(ObjectType.Hornet, spirit.Type);
        Assert.Equal(new FixedVector(-750 * 256, 0, -1500 * 256), spirit.Position);
        Assert.Equal(player.Forward, spirit.Forward);
        Assert.Equal(20 << 8, spirit.Speed);
        ref var spiritShip = ref sim.Ships[1];
        Assert.Equal(ShipMissionType.Wingman, spiritShip.MissionType);
        Assert.Equal(0, spiritShip.WingLeader);
        Assert.Equal(new ShortVector(750, 0, 0), spiritShip.FormationOffset);
        Assert.Equal(5, spiritShip.PilotLevel);
        Assert.Equal(0, spiritShip.Rating);
        Assert.Equal(1, spiritShip.MissionShip);
        Assert.Equal(-1, spiritShip.SpawnNavPoint);
        Assert.Equal((sbyte)AiTables.PilotTurnInterval[5], spiritShip.TurnInterval);

        // Objectives: Nav 1..3 then the carrier.
        Assert.Equal(4, sim.MissionObjectiveCount);
        Assert.Equal(new sbyte[] { 0, 1, 2, 3, -1 }, sim.FlightPath[..5]);
        Assert.Equal("Nav 1", sim.MissionObjectives[0].DisplayName);
        Assert.Equal("Proceed to Nav Point 1", sim.MissionObjectives[0].Name);
        Assert.Equal((short)300, sim.MissionObjectives[0].MapX); // (30000*256/100) >> 8
        Assert.Equal((short)150, sim.MissionObjectives[0].MapY);
        Assert.Equal("Tiger's Claw", sim.MissionObjectives[3].DisplayName);
        Assert.Equal(0, sim.CurrentObjective);
        Assert.Equal(ShipObjective.NavPoint, sim.Ships[0].Objective);
        // |(30000, -5000, 15000) - (0, 0, -1500)| = 34601 units.
        Assert.InRange(sim.CurrentObjectiveRange, 34595, 34605);
        Assert.Equal(0, sim.CarrierMissionShipIndex);

        // RunSpaceFlight enters the entry sphere: the carrier and its two Scimitars.
        sim.SetUpActionSphere(0);
        Assert.Equal(ObjectType.TigersClaw, sim.Objects[2].Type);
        Assert.Equal(ObjectClass.CapitalShip, sim.Objects[2].Class);
        Assert.Equal(FixedVector.Zero, sim.Objects[2].Position);
        Assert.Equal(0, sim.Ships[2].SpawnNavPoint);
        Assert.Equal(ObjectType.Scimitar, sim.Objects[3].Type);
        Assert.Equal(new FixedVector(0, 2500 * 256, 0), sim.Objects[3].Position);
        Assert.Equal(ShipMissionType.Defend, sim.Ships[3].MissionType);
        Assert.Equal(ObjectType.Scimitar, sim.Objects[4].Type);
        Assert.Equal(new FixedVector(-750 * 256, 2500 * 256, 0), sim.Objects[4].Position);
        Assert.Equal(3, sim.Ships[4].WingLeader);
        Assert.Equal(10 << 8, sim.Objects[4].Speed);
        Assert.Equal(ObjectType.TigersClaw, sim.ResourceSlots[1].Type);
        Assert.Equal(ObjectType.Scimitar, sim.ResourceSlots[2].Type);
        Assert.Equal(new ShapeRef(30, SpaceSimulation.CapitalShipSilhouetteSection), sim.TypeResources[(int)ObjectType.TigersClaw].Shape);
        Assert.Equal(new ShapeRef(24, 0), sim.Objects[3].Shape);
        Assert.Equal(-1, sim.CurrentWave);
        Assert.Equal(0, sim.HazardFieldCount);

        // Flying to Nav 1 replaces the sphere ships with three Dralthi in formation 2.
        for (int obj = 2; obj <= 4; obj++)
            sim.Objects[obj].ScreenX = ObjectSlots.NotVisible;
        sim.Objects[0].Position = sim.MissionNavPoints[1].Position;
        sim.ReleaseStaleNavTarget();
        Assert.Equal(1, sim.CurrentNavPoint);
        for (int obj = 2; obj <= 4; obj++)
        {
            Assert.Equal(ObjectType.Dralthi, sim.Objects[obj].Type);
            Assert.Equal(Side.Kilrathi, sim.Ships[obj].Side);
            Assert.Equal(1, sim.Ships[obj].SpawnNavPoint);
        }
        Assert.Equal(new FixedVector(25000 * 256, -4000 * 256, 20000 * 256), sim.Objects[2].Position);
        int separation = VectorMath.Delta(sim.Objects[2].Position, sim.Objects[3].Position).Magnitude() >> 8;
        Assert.InRange(separation, 899, 902); // |(750, 0, -500)| = 901
        Assert.Equal(sim.Objects[2].Forward, sim.Objects[3].Forward);
        Assert.True(sim.ShapeLoaded(ObjectType.Dralthi));
        Assert.False(sim.ShapeLoaded(ObjectType.Scimitar));

        // Nav 2 holds an asteroid field record.
        sim.Objects[0].Position = sim.MissionNavPoints[2].Position;
        for (int obj = 2; obj <= 4; obj++)
            sim.Objects[obj].ScreenX = ObjectSlots.NotVisible;
        sim.ReleaseStaleNavTarget();
        Assert.Equal(2, sim.CurrentNavPoint);
        Assert.Equal(1, sim.HazardFieldCount);
        Assert.Equal(ObjectType.AsteroidField, sim.HazardFields[0].Type);
        Assert.Equal(10500, sim.HazardFields[0].InnerRadius);
        Assert.Equal(new FixedVector(45000 * 256, 7000 * 256, -15000 * 256), sim.HazardFields[0].Center);
        Assert.Equal(new ShapeRef(3, 16), sim.TypeResources[(int)ObjectType.Asteroid1].ShapeSet);
        Assert.Equal(new ShapeRef(3, 17), sim.TypeResources[(int)ObjectType.Asteroid2].ShapeSet);
        Assert.Equal(new ShapeRef(3, 13), sim.TypeResources[(int)ObjectType.RockChunk].ShapeSet);
    }

    [DataFact]
    public void Epsilon_wing_trigger_reveals_the_hidden_asteroid_nav()
    {
        var sim = CreateSimulation();
        Assert.True(sim.InitMission(1, 1));
        Assert.Equal(0, sim.MissionNavPoints[3].Type);
        Assert.Equal(".Asteroid Field", sim.MissionNavPoints[3].Name);
        sim.SetUpActionSphere(0);
        Assert.Equal(ShipMissionType.GotoWarp, sim.Ships[sim.FindShipIndex(6)].MissionType);
        // The Drayman's GOTO_WARP spot is its target nav (2).
        Assert.Equal(sim.MissionNavPoints[2].Position, sim.Ships[sim.FindShipIndex(6)].MissionSpot);
        for (int obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
            sim.Objects[obj].ScreenX = ObjectSlots.NotVisible;
        sim.SetUpActionSphere(2);
        Assert.Equal(1, sim.MissionNavPoints[3].Type);
    }

    [DataFact]
    public void Training_simulator_waves_spawn_from_the_following_nav_records()
    {
        var sim = CreateSimulation();
        sim.TrainSimActive = true;
        Assert.True(sim.InitMission(0, 0));
        Assert.Equal(0, sim.MissionObjectiveCount);
        sim.SetUpActionSphere(0);
        Assert.Equal(2, sim.CurrentWave);
        short first = sim.FindShipIndex(1);
        Assert.Equal(ObjectType.Salthi, sim.Objects[first].Type);

        // No Kilrathi fighter left: the next wave (nav record 1) spawns.
        sim.RemoveObject(first);
        sim.CheckNextWave();
        Assert.Equal(3, sim.CurrentWave);
        Assert.Equal(-1, sim.MissionNavPoints[1].Type);
        Assert.NotEqual(-1, sim.FindShipIndex(2));
        Assert.NotEqual(-1, sim.FindShipIndex(3));
        Assert.Equal(0, sim.Ships[sim.FindShipIndex(2)].SpawnNavPoint);

        // Kilrathi still present: nothing happens.
        sim.CheckNextWave();
        Assert.Equal(3, sim.CurrentWave);
    }

    [DataFact]
    public void Intro_scene_spawns_the_canned_dogfight()
    {
        var sim = CreateSimulation();
        sim.Init3SpaceObjects(0);
        sim.MissionNavPoints[0].Type = 1;
        sim.SetUpActionSphere(16);
        Assert.True(sim.ShapeLoaded(ObjectType.Dralthi));
        Assert.Equal(0, sim.MissionNavPoints[0].Type); // the intro nav's {0, 0} triggers
        for (short record = 32; record <= 35; record++)
        {
            short slot = sim.FindShipIndex(record);
            Assert.True(slot > 0);
            ref var ship = ref sim.Ships[slot];
            Assert.Equal(ShipMissionType.CannedSequence, ship.MissionType);
            Assert.Equal(2, ship.PilotLevel);
            Assert.Equal(-1, ship.Rating);
            Assert.Equal(0, ship.CannedCommand);
            Assert.Equal(record == 35 ? 17 : 20, ship.ActionCount);
            Assert.Equal(2, ship.CannedSequence.Position);
            Assert.Equal(16, sim.MissionShips[record].NavPoint);
        }
        Assert.Equal(40 << 8, sim.Objects[sim.FindShipIndex(32)].Speed);
        Assert.Equal(new FixedVector(-154521, -25600, -232012), sim.Objects[sim.FindShipIndex(33)].Position);
    }
}
