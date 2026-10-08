using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Flight;

public class HazardTests
{
    [Fact]
    public void Approaching_an_asteroid_field_spawns_asteroids_ahead()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        sim.LoadShip(ObjectType.AsteroidField, 1);
        sim.AddHazardField(ObjectType.AsteroidField, TestWorld.Units(0, 0, 6000), 5000, 3);
        sim.CameraViewMode = 0;

        sim.CheckHazards();

        Assert.Equal(0, sim.ActiveHazardField);
        Assert.True(sim.ActiveHazards >= 1);
        for (int slot = 1; slot <= 3; slot++)
        {
            short hazard = sim.HazardObjects[slot];
            if (hazard == -1)
                continue; // the spot fell outside the field
            ref readonly var rock = ref sim.Objects[hazard];
            Assert.Equal(ObjectClass.Asteroid, rock.Class);
            Assert.InRange((int)rock.Type, (int)ObjectType.Asteroid1, (int)ObjectType.Asteroid6);
            Assert.Equal(0, rock.Counter);
            // 3050 units out, then possibly moved back along its heading (still a sensible distance).
            int distance = rock.Position.Magnitude() >> 8;
            Assert.InRange(distance, 1500, 5000);
        }
        Assert.Equal(-1, sim.HazardObjects[0]);

        // Far from the field the hazards are cleared.
        sim.Objects[0].Position = TestWorld.Units(0, 0, -20000);
        sim.CheckHazards();
        Assert.Equal(-1, sim.ActiveHazardField);
        Assert.All(sim.HazardObjects, h => Assert.Equal(-1, h));
        Assert.Equal(0, sim.ActiveHazards);
    }

    [Fact]
    public void Mines_of_a_mine_field_face_the_player_on_a_200_unit_grid()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        sim.AddHazardField(ObjectType.MineField, TestWorld.Units(0, 0, 6000), 5000, 3);
        sim.StartHazardField(0);
        int mines = 0;
        foreach (sbyte hazard in sim.HazardObjects)
        {
            if (hazard == -1)
                continue;
            mines++;
            ref readonly var mine = ref sim.Objects[hazard];
            Assert.Equal(ObjectType.SpaceMine, mine.Type);
            Assert.Equal(0, (short)mine.Position.X % 200);
            Assert.Equal(0, (short)mine.Position.Y % 200);
            Assert.Equal(0, (short)mine.Position.Z % 200);
            Assert.InRange(mine.Velocity.Magnitude() >> 8, 1, 2); // drifting at 2 units/frame
        }
        Assert.True(mines >= 1);
    }

    [Theory]
    [InlineData(0x12345678, 0x123455F0)] // low word 22136 → 22000
    [InlineData(0x1234F000, 0x1234F060)] // low word -4096 → -4000 (truncation toward zero)
    [InlineData(-0x10000, -0x10000)]
    public void Align_changes_only_the_low_word(int value, int expected)
    {
        int component = value;
        SpaceSimulation.Align(ref component, 200);
        Assert.Equal(expected, component);
    }

    [Fact]
    public void Hazard_management_recycles_hazards_left_behind()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        sim.LoadShip(ObjectType.AsteroidField, 1);
        sim.AddHazardField(ObjectType.AsteroidField, TestWorld.Units(0, 0, 0), 30000, 3);
        sim.ActiveHazardField = 0;
        short rock = sim.FindVacant3dObject();
        sim.SetObjectsData(rock, ObjectType.Asteroid1, -1);
        sim.Objects[rock].Position = TestWorld.Units(0, 0, -6000); // beyond the 4300-unit rear sphere
        sim.HazardObjects[5] = (sbyte)rock;
        sim.ActiveHazards = 1;
        sim.RenderedSpaceFrame = 5; // slot 5's turn
        sim.ManageHazard(rock, 5);
        Assert.Equal(ObjectClass.Null, sim.Objects[rock].Class);
        Assert.Equal(-1, sim.HazardObjects[5]);
        Assert.Equal(0, sim.ActiveHazards);
    }

    [Fact]
    public void Asteroids_shatter_into_rock_chunks()
    {
        var sim = TestWorld.Create();
        sim.LoadShip(ObjectType.AsteroidField, 1);
        short rock = sim.FindVacant3dObject();
        sim.SetObjectsData(rock, ObjectType.Asteroid1, -1);
        sim.Objects[rock].Velocity = new FixedVector(0, 0, 5 << 8);
        var peek = TestWorld.Peek(sim);
        int fragments = peek.BelowOrEqual(1) + 2;
        sim.HitAsteroid(rock, 0); // chance 0: always shatters
        int chunks = Enumerable.Range(0, ObjectSlots.Count).Count(i => sim.Objects[i].Type == ObjectType.RockChunk && sim.Objects[i].Class == ObjectClass.Debris);
        Assert.Equal(fragments, chunks);
        Assert.Equal(ObjectClass.Explosion, sim.Objects[rock].Class);
        Assert.Equal(ObjectType.Explosion0, sim.Objects[rock].Type);
    }
}
