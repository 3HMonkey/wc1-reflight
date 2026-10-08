using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Flight;

public class CameraProjectionTests
{
    private static SpaceSimulation CockpitView(RecordingEvents? events = null)
    {
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim);
        sim.ForceView(0, 0);
        return sim;
    }

    [Fact]
    public void The_cockpit_view_puts_the_eye_into_the_player()
    {
        var events = new RecordingEvents();
        var sim = CockpitView(events);
        ref readonly var eye = ref sim.Objects[ObjectSlots.Eye];
        Assert.Equal(0, sim.CameraViewMode);
        Assert.Contains("view 0", events.Calls);
        Assert.Equal(sim.Objects[0].Position, eye.Position);
        Assert.Equal(sim.Objects[0].Forward, eye.Forward);
        Assert.Equal(100, eye.CollisionRadius); // the Hornet's radius
        // new_view also scattered the dust (34..41) and the stars (42..48).
        for (int obj = ObjectSlots.FirstDust; obj < ObjectSlots.DustEnd; obj++)
        {
            Assert.Equal(ObjectClass.Dust, sim.Objects[obj].Class);
            Assert.InRange(sim.Objects[obj].Position.Z >> 8, -10, 1410);
        }
        for (int obj = ObjectSlots.FirstStar; obj <= ObjectSlots.LastStar; obj++)
        {
            Assert.Equal(ObjectClass.Star, sim.Objects[obj].Class);
            Assert.InRange(sim.Objects[obj].ViewFrame, 32, 37);
            Assert.True(sim.Objects[obj].Position.Z > 7000 << 8); // within ±45° yaw and pitch of the view axis
        }
    }

    [Theory]
    [InlineData(1, 0, 0, -0x100, 0x100, 0, 0)] // view 1 looks right (F3): eye forward = player right
    [InlineData(2, 0, 0, 0x100, -0x100, 0, 0)] // view 2 looks left (F2): eye forward = -player right
    [InlineData(3, -0x100, 0, 0, 0, 0, -0x100)] // rear: right and forward negated
    public void Side_and_rear_views_swap_the_axes(int view, int rightX, int rightY, int rightZ, int forwardX, int forwardY, int forwardZ)
    {
        var sim = CockpitView();
        sim.NewView(view, 0);
        Assert.Equal(new FixedVector(rightX, rightY, rightZ), sim.Objects[ObjectSlots.Eye].Right);
        Assert.Equal(new FixedVector(forwardX, forwardY, forwardZ), sim.Objects[ObjectSlots.Eye].Forward);
    }

    [Fact]
    public void The_chase_camera_trails_the_player()
    {
        var sim = CockpitView();
        sim.NewView(4, 0);
        // Placed 1200 units behind, then one step toward 700 behind: (1200 - 700) / 25 = 20 units.
        Assert.Equal(TestWorld.Units(0, 0, -1180), sim.Objects[ObjectSlots.Eye].Position);
        sim.NewView(4, 0); // pressing the chase key again toggles the close chase camera
        Assert.True(sim.AlternateChaseView);
    }

    [Fact]
    public void Objects_ahead_project_onto_the_screen()
    {
        var sim = CockpitView();
        short ahead = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        short right = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 100, 0, 1000);
        short behind = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, -1000);
        short wide = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 2000, 0, 1000);
        sim.TransformObjectsToYourView();

        ref readonly var a = ref sim.Objects[ahead];
        Assert.Equal(0, a.ScreenX);
        Assert.Equal(0, a.ScreenY);
        Assert.Equal(1000, a.Distance);
        // scale 1024 * (320/2 px) / (1000 - 120 units) in 8.8: 186.
        Assert.Equal(186, a.ScreenScale);
        // Focal length 160 px: 100 units at 1000 → 16 px.
        Assert.Equal(16, sim.Objects[right].ScreenX);
        Assert.Equal(ObjectSlots.NotVisible, sim.Objects[behind].ScreenX);
        Assert.Equal(0, sim.Objects[behind].Distance);
        Assert.Equal(ObjectSlots.NotVisible, sim.Objects[wide].ScreenX);
        Assert.True(sim.IsPointWithinEyeViewCone(sim.Objects[ahead].Position));
        Assert.False(sim.IsPointWithinEyeViewCone(sim.Objects[wide].Position));
    }

    [Fact]
    public void A_ship_seen_from_ahead_or_behind_shows_different_views()
    {
        var sim = CockpitView();
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        sim.TransformObjectsToYourView();
        short fromBehind = sim.Objects[ship].ViewFrame;
        // Looking at its tail: the eye is behind it (direction index 3 * 12 + 6 - 11 = 31).
        Assert.Equal(GeometryTables.DirectionShapeFrame[31], fromBehind);

        sim.Objects[ship].AlterYaw(180);
        sim.TransformObjectsToYourView();
        Assert.Equal(GeometryTables.DirectionShapeFrame[25], sim.Objects[ship].ViewFrame);
        Assert.NotEqual(fromBehind, sim.Objects[ship].ViewFrame);
    }

    [Fact]
    public void Capital_ships_use_one_shape_per_view()
    {
        var sim = CockpitView();
        short claw = TestWorld.AddShip(sim, ObjectType.TigersClaw, Side.Imperial, 0, 0, 5000);
        sim.Ships[claw].CapitalShipViewFrame = -1;
        sim.TransformObjectsToYourView();
        short frame = sim.Ships[claw].CapitalShipViewFrame;
        Assert.InRange(frame, 0, 36);
        Assert.Equal(0, sim.Objects[claw].ViewFrame);
        Assert.Equal(new ShapeRef(SpaceSimulation.ShipLogicalFile(ObjectType.TigersClaw), frame), sim.Objects[claw].Shape);
    }

    [Fact]
    public void The_draw_list_runs_far_to_near()
    {
        var sim = CockpitView();
        short near = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        short far = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 4000);
        Assert.True(sim.PrepareSpaceView());
        int nearIndex = Array.IndexOf(sim.SortedObjects, (int)near);
        int farIndex = Array.IndexOf(sim.SortedObjects, (int)far);
        Assert.True(farIndex >= 0 && nearIndex > farIndex);
        Assert.Equal(1, sim.RenderedSpaceFrame);
    }

    [Fact]
    public void Frame_skip_prepares_every_nth_view()
    {
        var sim = CockpitView();
        sim.FrameSkip = 3;
        sim.FrameSkipCounter = 1;
        Assert.True(sim.PrepareSpaceView());
        Assert.False(sim.PrepareSpaceView());
        Assert.False(sim.PrepareSpaceView());
        Assert.True(sim.PrepareSpaceView());
        Assert.Equal(2, sim.RenderedSpaceFrame);
    }

    [Fact]
    public void Engine_flames_follow_the_exhaust_table()
    {
        var sim = CockpitView();
        short ship = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1000);
        sim.Objects[ship].Speed = 10 << 8;
        // One flame record for every view: frame 1, scale 256, distance -1, angle 0, x 5, y -3.
        var table = new byte[37 * 2 + 12 + 2];
        for (int view = 0; view < 37; view++)
            BitConverter.TryWriteBytes(table.AsSpan(view * 2), (short)(37 * 2));
        short[] record = [1, 256, -1, 0, 5, -3, -1];
        for (int i = 0; i < record.Length; i++)
            BitConverter.TryWriteBytes(table.AsSpan(37 * 2 + i * 2), record[i]);
        sim.ExhaustTables[(int)ObjectType.Salthi] = new ExhaustTable(table);

        sim.TransformObjectsToYourView();
        var peek = TestWorld.Peek(sim);
        int scaleRoll = peek.InRange(0, 32);
        int frameRoll = peek.InRange(0, 1);
        sim.PlaceExhaustOnShips();
        short flame = Enumerable.Range(0, ObjectSlots.Count).Select(i => (short)i)
            .Single(i => sim.Objects[i].Type == ObjectType.Thrusters && sim.Objects[i].Class == ObjectClass.FixedObject);
        ref readonly var f = ref sim.Objects[flame];
        Assert.Equal(ship, f.Owner);
        Assert.Equal(256 - scaleRoll - 32, f.Scale); // cold engine: 32 smaller
        Assert.Equal(1 * 2 + 12 + frameRoll, f.ViewFrame);
        sim.RepositionFixedChildObjects();
        Assert.Equal(sim.Objects[ship].Distance - 1, sim.Objects[flame].Distance);

        // The flames are removed by the next house keeping.
        sim.HouseKeepObjects();
        Assert.Equal(ObjectClass.Null, sim.Objects[flame].Class);
    }

    [Fact]
    public void The_nav_pointer_takes_a_slot_while_the_navigation_display_shows()
    {
        var sim = CockpitView();
        sim.MissionObjectiveCount = 1;
        sim.CurrentObjective = 0;
        sim.MissionObjectives[0].Position = TestWorld.Units(0, 0, 20000);
        sim.TransformObjectsToYourView();
        short pointer = sim.NavPointerObject;
        Assert.NotEqual(-1, pointer);
        Assert.Equal(ObjectClass.Planet, sim.Objects[pointer].Class);
        Assert.Equal(0, sim.Objects[pointer].ScreenX);
        Assert.Equal(0x4a38, sim.Objects[pointer].Distance);

        ((DefaultCockpitState)sim.Cockpit).VduModes[1] = 3;
        sim.TransformObjectsToYourView();
        Assert.Equal(-1, sim.NavPointerObject);
        Assert.Equal(ObjectClass.Null, sim.Objects[pointer].Class);
    }

    [Fact]
    public void A_view_script_drives_the_eye()
    {
        var sim = CockpitView();
        // Place the eye at (0, 0, -500), speed 5, wait 3 frames, then end.
        short[] script = [0, 0, 0, -500, 2, 5, 14, 3, -1];
        sim.InitializeScriptedView(script);
        Assert.True(sim.ScriptedView);
        Assert.Equal(TestWorld.Units(0, 0, -500), sim.Objects[ObjectSlots.Eye].Position);
        Assert.Equal(new FixedVector(0, 0, 5 << 8), sim.Objects[ObjectSlots.Eye].Velocity);
        Assert.Equal(3, sim.Objects[ObjectSlots.Eye].Counter);
        sim.NewView(15, 0);
        for (int frame = 0; frame < 4; frame++)
            sim.SetEyeDirectionAndPosition();
        Assert.False(sim.ScriptedView);
        Assert.Equal(-1, sim.ScriptedViewObject);
    }
}
