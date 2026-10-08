using WingCommander.Simulation.Data;
using WingCommander.Simulation.Missions;
using WingCommander.Tests;

namespace WingCommander.Simulation.Tests.Missions;

public class MissionModuleTests
{
    public static TheoryData<string, int> Modules => new()
    {
        { "MODULE.000", 44 },
        { "MODULE.001", 22 },
        { "MODULE.002", 22 },
    };

    private static MissionModule Open(string file) => MissionModule.Parse(GameData.Require().OpenPacket(file));

    private static IEnumerable<MissionData> AllMissions(MissionModule module)
    {
        for (int series = 0; series < MissionModule.SeriesSlots; series++)
        {
            for (int mission = 0; mission < MissionModule.MissionsPerSeries; mission++)
            {
                var data = module.GetMission(series, mission);
                if (data is not null)
                    yield return data;
            }
        }
    }

    [DataTheory]
    [MemberData(nameof(Modules))]
    public void Every_mission_parses_with_plausible_records(string file, int expectedMissions)
    {
        var module = Open(file);
        var missions = AllMissions(module).ToList();
        Assert.Equal(expectedMissions, missions.Count);
        foreach (var m in missions)
            CheckMission(m);
    }

    private static void CheckMission(MissionData m)
    {
        string where = $"series {m.Series} mission {m.Mission}";
        var h = m.Header;
        Assert.InRange(h.EntryNavPoint, 0, 15);
        Assert.InRange(h.PlayerMissionShip, 0, 31);
        Assert.InRange(h.HomeMissionShip, -1, 31);
        Assert.Equal(1, m.NavPoints[h.EntryNavPoint].Type);
        var player = m.Ships[h.PlayerMissionShip];
        // Usually Hornet..Raptor; Secret Missions 2 lets the player fly a captured Dralthi.
        Assert.Equal(ObjectClass.Ship, ObjectTypeTable.Get(player.Type).ObjectClass);
        Assert.Equal(Side.Imperial, player.Side);
        Assert.Equal(13, player.Pilot);
        foreach (short initial in h.InitialMissionShips)
        {
            Assert.InRange(initial, -1, 31);
            if (initial != -1)
                Assert.Equal(Side.Imperial, m.Ships[initial].Side);
        }
        Assert.False(string.IsNullOrWhiteSpace(m.MissionName), where);
        Assert.False(string.IsNullOrWhiteSpace(m.SeriesName), where);

        for (int n = 0; n < m.NavPoints.Count; n++)
        {
            var nav = m.NavPoints[n];
            Assert.InRange(nav.Type, -1, 5);
            Assert.True(IsReadableAscii(nav.Name), $"{where} nav {n} name '{nav.Name}'");
            if (nav.Type == 0 && nav.Name.Length == 0)
                continue;
            CheckCoordinate(nav.Position.X, 200_000, where);
            CheckCoordinate(nav.Position.Y, 200_000, where);
            CheckCoordinate(nav.Position.Z, 200_000, where);
            Assert.Equal(0, nav.Position.X & 0xff); // whole units on disk
            for (int p = 0; p < 2; p++)
            {
                var preload = nav.PreloadObjectTypes[p];
                Assert.True(preload == ObjectType.None || ((int)preload >= 0 && (int)preload <= (int)ObjectType.MineField),
                    $"{where} nav {n} preload {preload}");
            }
            for (int s = 0; s < 10; s++)
            {
                short ship = nav.MissionShips[s];
                Assert.InRange(ship, -1, 31);
                if (ship != -1)
                    Assert.NotEqual(ObjectType.None, m.Ships[ship].Type);
            }
            for (int t = 0; t < 8; t += 2)
            {
                if (nav.Triggers[t] != -1)
                {
                    Assert.InRange(nav.Triggers[t], 0, 5);
                    Assert.InRange(nav.Triggers[t + 1], 0, 15);
                }
            }
        }

        for (int i = 0; i < m.Ships.Count; i++)
        {
            var ship = m.Ships[i];
            if (ship.Type == ObjectType.None)
                continue;
            string shipWhere = $"{where} ship {i}";
            Assert.True((int)ship.Type >= 0 && (int)ship.Type <= (int)ObjectType.MineField, $"{shipWhere} type {ship.Type}");
            Assert.True(ship.Side is Side.Imperial or Side.Kilrathi or Side.Neutral, shipWhere);
            CheckCoordinate(ship.Position.X, 100_000, shipWhere);
            CheckCoordinate(ship.Position.Y, 100_000, shipWhere);
            CheckCoordinate(ship.Position.Z, 100_000, shipWhere);
            if (ship.Type is ObjectType.AsteroidField or ObjectType.MineField)
                continue;
            Assert.True((int)ship.MissionType >= -1 && (int)ship.MissionType <= (int)ShipMissionType.ComeHome, $"{shipWhere} mission {ship.MissionType}");
            Assert.InRange(ship.Pilot, 0, 17);
            Assert.InRange(ship.LeaderMissionIndex, -1, 31);
            Assert.InRange(ship.FormationIndex, -1, 4);
            if (ship.FormationIndex != -1)
                Assert.InRange(ship.FormationSpot, 0, 7);
            Assert.InRange(ship.Speed, 0, 100);
            if (ship.LeaderMissionIndex != -1)
            {
                var leader = m.Ships[ship.LeaderMissionIndex];
                Assert.NotEqual(ObjectType.None, leader.Type);
                // set_formation_position indexes the formation table with the ROOT leader's
                // formation; -1 there would be an out-of-bounds read in the original.
                var root = leader;
                for (int depth = 0; root.LeaderMissionIndex != -1 && depth < 32; depth++)
                    root = m.Ships[root.LeaderMissionIndex];
                if (ship.FormationIndex != -1)
                {
                    Assert.InRange(root.FormationIndex, 0, 4);
                    Assert.InRange(root.FormationSpot, 0, 7);
                }
            }
            switch (ship.MissionType)
            {
                case ShipMissionType.Escort or ShipMissionType.Strike or ShipMissionType.Defend or ShipMissionType.Wingman or ShipMissionType.Rendezvous:
                    Assert.InRange(ship.TargetMissionIndex, 0, 31);
                    break;
                case ShipMissionType.GotoWarp:
                    Assert.InRange(ship.TargetMissionIndex, 0, 15);
                    break;
            }
        }

        int objectives = m.ObjectiveCount;
        Assert.InRange(objectives, 0, 15);
        for (int i = 0; i < objectives; i++)
        {
            var source = m.ObjectiveSources[i];
            Assert.InRange(source.Type, 0, 4);
            Assert.True(IsReadableAscii(source.Description), $"{where} objective {i}");
            if (source.Type == 0)
            {
                Assert.InRange(source.Index, 0, 15);
                var nav = m.NavPoints[source.Index];
                Assert.False(nav.Type == 0 && nav.Name.Length == 0, $"{where} objective {i} references an empty nav");
            }
            else
            {
                Assert.InRange(source.Index, 0, 31);
                Assert.NotEqual(ObjectType.None, m.Ships[source.Index].Type);
            }
        }
    }

    private static void CheckCoordinate(int fixedValue, int limitUnits, string where) =>
        Assert.True(Math.Abs(fixedValue) <= limitUnits * 256, $"{where}: coordinate {fixedValue / 256.0} out of range");

    private static bool IsReadableAscii(string text)
    {
        foreach (char c in text)
        {
            if (c < 0x20 || c > 0x7e)
                return false;
        }
        return true;
    }

    [DataFact]
    public void Enyo_first_mission_matches_the_hand_decoded_bytes()
    {
        // wc1tool hex MODULE.000 1/3: mission index 4 starts at 0x1340 (section 1) / 0x1500 (section 3).
        var m = Open("MODULE.000").GetMission(1, 0)!;
        Assert.Equal("Enyo", m.SeriesName);
        Assert.Equal("Alpha Wing", m.MissionName);
        Assert.Equal(4, m.MissionIndex);
        Assert.Equal(0, m.Header.EntryNavPoint);
        Assert.Equal(0, m.Header.HomeMissionShip);
        Assert.Equal(1, m.Header.PlayerMissionShip);
        Assert.Equal(new short[] { 2, -1, -1, -1, -1, -1, -1, -1 }, m.Header.InitialMissionShips);

        var claw = m.NavPoints[0];
        Assert.Equal("Tiger's Claw", claw.Name);
        Assert.Equal(1, claw.Type);
        Assert.Equal(15000, claw.ProximityRadius); // bytes 98 3a at +43
        Assert.Equal(ObjectType.TigersClaw, claw.PreloadObjectTypes[0]);
        Assert.Equal(ObjectType.Scimitar, claw.PreloadObjectTypes[1]);
        Assert.Equal(new short[] { 0, 3, 4, -1 }, new[] { claw.MissionShips[0], claw.MissionShips[1], claw.MissionShips[2], claw.MissionShips[3] });
        Assert.Equal(-1, claw.Triggers[0]);

        var nav1 = m.NavPoints[1];
        Assert.Equal("Nav 1", nav1.Name);
        Assert.Equal(0x753000, nav1.Position.X); // bytes 00 30 75 00 at +31: 30000.0 in 24.8
        Assert.Equal(-1280000, nav1.Position.Y); // ffec7800 = -5000.0
        Assert.Equal(0x3a9800, nav1.Position.Z); // 15000.0
        Assert.Equal(ObjectType.Dralthi, nav1.PreloadObjectTypes[0]);
        Assert.Equal(ObjectType.None, nav1.PreloadObjectTypes[1]);
        Assert.Equal(5, nav1.MissionShips[0]);
        Assert.Equal(ObjectType.AsteroidField, m.NavPoints[2].PreloadObjectTypes[0]);
        Assert.Equal(0, m.NavPoints[4].Type);

        var claw0 = m.Ships[0];
        Assert.Equal(ObjectType.TigersClaw, claw0.Type);
        Assert.Equal(ShipMissionType.None, claw0.MissionType);
        Assert.Equal(180, claw0.Pitch);
        Assert.Equal(-1, claw0.FormationIndex);

        var player = m.Ships[1];
        Assert.Equal(ObjectType.Hornet, player.Type);
        Assert.Equal(-1500 * 256, player.Position.Z); // 00 24 fa ff
        Assert.Equal(20, player.Speed);
        Assert.Equal(4, player.Rating);
        Assert.Equal(13, player.Pilot);
        Assert.Equal(0, player.FormationIndex);

        var spirit = m.Ships[2];
        Assert.Equal(ShipMissionType.Wingman, spirit.MissionType);
        Assert.Equal((int)Rating.AceSpirit, spirit.Pilot);
        Assert.Equal(1, spirit.Leader);
        Assert.Equal(1, spirit.LeaderMissionIndex);
        Assert.Equal(2, spirit.FormationSpot);
        Assert.Equal(1, spirit.TargetMissionIndex);

        var dralthi = m.Ships[5];
        Assert.Equal(ObjectType.Dralthi, dralthi.Type);
        Assert.Equal(Side.Kilrathi, dralthi.Side);
        Assert.Equal(ShipMissionType.Patrol, dralthi.MissionType);
        Assert.Equal(100, dralthi.Pitch);
        Assert.Equal(1000 * 256, dralthi.Position.Y);

        var field = m.Ships[11];
        Assert.Equal(ObjectType.AsteroidField, field.Type);
        Assert.Equal(7500, field.Speed); // radius 10500 - 3000

        Assert.Equal(4, m.ObjectiveCount);
        Assert.Equal(0, m.ObjectiveSources[0].Type);
        Assert.Equal(1, m.ObjectiveSources[0].Index);
        Assert.Equal("Proceed to Nav Point 1", m.ObjectiveSources[0].Description);
        Assert.Equal(1, m.ObjectiveSources[3].Type);
        Assert.Equal("Return to Tiger's Claw", m.ObjectiveSources[3].Description);
        Assert.Equal(-1, m.ObjectiveSources[4].Type);
    }

    [DataFact]
    public void Campaign_series_names_follow_the_vega_campaign()
    {
        var original = Open("MODULE.000");
        string[] expected =
        [
            "Squadron", "Enyo", "McAuliffe", "Gateway", "Gimle", "Brimstone", "Chengdu", "Dakota",
            "Port Hedland", "Kurasawa", "Rostov", "Hubble's Star", "Venice", "Hell's Kitchen",
        ];
        for (int series = 0; series < expected.Length; series++)
            Assert.Equal(expected[series], original.GetSeriesName(series));
        Assert.Equal("Goddard", Open("MODULE.001").GetSeriesName(1));
        Assert.Equal("Firekka", Open("MODULE.002").GetSeriesName(1));
        Assert.Equal("Beginner", original.GetMissionName(0, 0));
        Assert.Equal("Omega Wing", original.GetMissionName(12, 3));
        Assert.False(original.HasMission(1, 2)); // Enyo has two missions
        Assert.Null(original.GetMission(1, 3));
        Assert.Null(original.GetMission(16, 0));
    }

    [DataFact]
    public void Training_simulator_missions_use_follow_up_waves()
    {
        var m = Open("MODULE.000").GetMission(0, 0)!;
        Assert.Equal(-1, m.Header.HomeMissionShip);
        Assert.Equal(0, m.ObjectiveCount);
        Assert.Equal(new sbyte[] { 1, 2, 3, 4, 0 }, new[] { m.NavPoints[0].Type, m.NavPoints[1].Type, m.NavPoints[2].Type, m.NavPoints[3].Type, m.NavPoints[4].Type });
        Assert.Equal(ObjectType.Salthi, m.Ships[1].Type);
    }
}
