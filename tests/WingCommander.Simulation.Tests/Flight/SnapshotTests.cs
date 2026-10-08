using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Flight;

public class SnapshotTests
{
    [Fact]
    public void The_snapshot_carries_the_camera_the_objects_and_the_draw_order()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        sim.ConstellationShape = new ShapeRef(12, 0);
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        sim.Objects[enemy].Speed = 12 << 8;
        sim.ForceView(0, 0);
        Assert.True(sim.PrepareSpaceView());

        var snapshot = new SpaceViewSnapshot();
        sim.CaptureSpaceView(snapshot);

        Assert.Equal(sim.RenderedSpaceFrame, snapshot.RenderedSpaceFrame);
        Assert.Equal(0, snapshot.CameraViewMode);
        Assert.Equal(sim.Objects[ObjectSlots.Eye].Position, snapshot.CameraPosition);
        Assert.Equal(sim.Objects[ObjectSlots.Eye].Forward, snapshot.CameraForward);
        Assert.Equal(100, snapshot.CameraNearRadius);
        Assert.Equal(320, snapshot.ScreenWidth);

        var objects = snapshot.ActiveObjects.ToArray();
        Assert.Equal(Enumerable.Range(0, ObjectSlots.LastMoving + 1).Count(i => sim.Objects[i].Class != ObjectClass.Null), objects.Length);
        var view = objects.Single(o => o.Slot == enemy);
        Assert.Equal(ObjectType.Salthi, view.Type);
        Assert.Equal(sim.Objects[enemy].Position, view.Position);
        Assert.Equal(sim.Objects[enemy].Forward, view.Forward);
        Assert.True(view.Visible);
        Assert.Equal(sim.Objects[enemy].ScreenScale, view.ScreenScale);
        Assert.Equal(sim.Objects[enemy].ViewFrame, view.ViewFrame);
        Assert.Equal(Side.Kilrathi, view.Side);
        Assert.Equal(12 << 8, view.Speed);

        var star = objects.First(o => o.Class == ObjectClass.Star);
        Assert.Equal(new ShapeRef(12, 0), star.DrawShape);
        var dust = objects.First(o => o.Class == ObjectClass.Dust);
        Assert.Equal(new ShapeRef(12, 0), dust.DrawShape);
        var pointer = objects.Single(o => o.IsNavPointer);
        Assert.Equal(sim.NavPointerObject, pointer.Slot);

        Assert.True(snapshot.DrawCount > 0);
        for (int i = 0; i < snapshot.DrawCount; i++)
            Assert.Equal(sim.SortedObjects[i], snapshot.DrawOrder[i]);
        Assert.Equal(-1, snapshot.DrawCount < ObjectSlots.Count ? sim.SortedObjects[snapshot.DrawCount] : -1);
    }

    [Fact]
    public void The_state_hash_changes_with_the_state()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        ulong before = sim.ComputeStateHash();
        Assert.Equal(before, sim.ComputeStateHash());
        sim.Objects[0].Position.X++;
        Assert.NotEqual(before, sim.ComputeStateHash());
        sim.Objects[0].Position.X--;
        Assert.Equal(before, sim.ComputeStateHash());
        sim.Random.Next();
        Assert.NotEqual(before, sim.ComputeStateHash());
    }
}
