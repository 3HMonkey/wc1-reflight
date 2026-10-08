using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Objects;

public class WorldInitialisationTests
{
    [Fact]
    public void New_world_matches_the_zero_initialised_c_globals()
    {
        var sim = FakeResources.CreateSimulation();
        Assert.Equal(64, sim.Objects.Length);
        Assert.Equal(10, sim.Ships.Length);
        Assert.All(sim.Objects, o => Assert.Equal(ObjectClass.Null, o.Class));
        Assert.Equal(-1, sim.YourWingman);
        Assert.Equal(-1, sim.CurrentWave);
        Assert.Equal(0x7fff, sim.EnemySighting);
        Assert.Equal(-1, sim.LastShipSlot);
        Assert.Equal(-1, sim.CurrentObjective);
        Assert.Equal(-1, sim.CameraViewMode);
        Assert.Equal(ObjectType.None, sim.SelectedGunType);
        Assert.All(sim.HazardObjects, h => Assert.Equal(-1, h));
        Assert.True(sim.PlayerVulnerable);
        Assert.Equal(-1, sim.StartNavPointOverride);
        // Built-in intro data.
        Assert.Equal(ObjectType.Dralthi, sim.MissionShips[32].Type);
        Assert.Equal(ObjectType.Scimitar, sim.MissionShips[45].Type);
        Assert.Equal(1, sim.MissionNavPoints[16].Type);
        Assert.Equal("", sim.MissionNavPoints[0].Name);
        Assert.True(sim.TypeResources[(int)ObjectType.Hornet].ShapeSet.IsNone);
    }

    [Fact]
    public void Direction_view_frames_cover_up_bands_and_down()
    {
        var sim = FakeResources.CreateSimulation();
        // Frame 0: pitch 90 (c = 0, s = 256): up -> (0, 0, -256), forward -> (0, 256, 0).
        Assert.Equal(new FixedVector(256, 0, 0), sim.DirectionViewRight[0]);
        Assert.Equal(new FixedVector(0, 0, -256), sim.DirectionViewUp[0]);
        Assert.Equal(new FixedVector(0, 256, 0), sim.DirectionViewForward[0]);
        // Frame 25: pitch 0, yaw 0 = identity.
        Assert.Equal(new FixedVector(256, 0, 0), sim.DirectionViewRight[25]);
        Assert.Equal(new FixedVector(0, 0, 256), sim.DirectionViewForward[25]);
        // Frame 28: pitch 0, yaw 90.
        Assert.Equal(new FixedVector(0, 0, 256), sim.DirectionViewRight[28]);
        Assert.Equal(new FixedVector(-256, 0, 0), sim.DirectionViewForward[28]);
        // Frame 61: pitch -90.
        Assert.Equal(new FixedVector(0, -256, 0), sim.DirectionViewForward[61]);
    }

    [Fact]
    public void Constellation_planets_are_placed_30000_units_out()
    {
        var resources = new FakeResources();
        var sim = new SpaceSimulation(new CRandom(1), resources)
        {
            ConstellationDefinitions = [new(2, 0, 0, 0), new(3, 150, 30, 0), new(-1, 0, 0, 0), new(-1, 0, 0, 0)],
        };
        sim.Init3SpaceObjects(1);
        Assert.Equal(new ShapeRef(12, 0), sim.ConstellationShape);
        short first = sim.ConstellationObjectIndices[0];
        short second = sim.ConstellationObjectIndices[1];
        Assert.Equal(10, first);
        Assert.Equal(11, second);
        Assert.Equal(-1, sim.ConstellationObjectIndices[2]);
        Assert.Equal(ObjectClass.Planet, sim.Objects[first].Class);
        Assert.Equal(new FixedVector(0, 0, 30000 * 256), sim.Objects[first].Position);
        Assert.Equal(new ShapeRef(12, 3), sim.Objects[first].Shape);
        Assert.Equal(0xff, sim.Objects[first].ScreenScale);
        Assert.InRange(sim.Objects[second].Position.Magnitude() >> 8, 29900, 30100);
        Assert.Equal(new ShapeRef(3, 0), sim.TypeResources[(int)ObjectType.Thrusters].ShapeSet);
        Assert.Equal(sim.TypeResources[(int)ObjectType.DebrisPipe].ShapeSet, sim.TypeResources[(int)ObjectType.DebrisGlass].ShapeSet);
        Assert.Equal(sim.TypeResources[(int)ObjectType.LaserCannon].ShapeSet, sim.TypeResources[(int)ObjectType.Turret].ShapeSet);
        Assert.Equal(new ShapeRef(51, 0), sim.TypeResources[(int)ObjectType.DumbFireMissile].ShapeSet);
        Assert.Equal(ObjectType.HeatSeekingMissile, sim.ResourceSlots[3].Type);

        sim.Free3Space();
        Assert.True(sim.ConstellationShape.IsNone);
        Assert.Equal(ObjectClass.Null, sim.Objects[first].Class);
        Assert.True(sim.TypeResources[(int)ObjectType.Thrusters].ShapeSet.IsNone);
        Assert.True(sim.TypeResources[(int)ObjectType.DumbFireMissile].ShapeSet.IsNone);
        // The turret alias is not in the descriptor list: it keeps the (dangling) laser pointer.
        Assert.False(sim.TypeResources[(int)ObjectType.Turret].ShapeSet.IsNone);
    }

    [Fact]
    public void Canned_scene_objects_are_static_planets()
    {
        var sim = FakeResources.CreateSimulation();
        short obj = sim.CreateCannedSceneObject(-180, 1000, new ShapeRef(9, 3), 2, 5, 0x100);
        Assert.Equal(10, obj);
        Assert.Equal(ObjectClass.Planet, sim.Objects[obj].Class);
        Assert.Equal(new FixedVector(0, 0, -1000 * 256), sim.Objects[obj].Position);
        Assert.Equal((ObjectType)5, sim.Objects[obj].Type);
        Assert.Equal(5, sim.Objects[obj].ScreenAngle);
        Assert.Equal(2, sim.Objects[obj].ViewFrame);
    }

    [Fact]
    public void Hazard_fields_are_capped_at_seven()
    {
        var sim = FakeResources.CreateSimulation();
        for (int i = 0; i < 9; i++)
            sim.AddHazardField(ObjectType.MineField, new FixedVector(i, 0, 0), 4000, 2);
        Assert.Equal(7, sim.HazardFieldCount);
        Assert.Equal(new FixedVector(6, 0, 0), sim.HazardFields[6].Center);
        sim.ActiveHazards = 3;
        sim.RemoveAllHazards();
        Assert.Equal(0, sim.ActiveHazards);
        Assert.Equal(-1, sim.ActiveHazardField);
    }

    [Fact]
    public void Constellation_table_parses_four_records_per_series()
    {
        byte[] bytes = [2, 0, 0, 0, 0, 0, 0, 0, 3, 0, 150, 0, 30, 0, 0, 0, 0xff, 0xff, 0, 0, 0, 0, 0, 0];
        var table = ConstellationObjectDefinition.ParseTable(bytes);
        Assert.Equal(3, table.Length);
        Assert.Equal(new ConstellationObjectDefinition(3, 150, 30, 0), table[1]);
        Assert.Equal(-1, table[2].ShapePacket);
    }
}
