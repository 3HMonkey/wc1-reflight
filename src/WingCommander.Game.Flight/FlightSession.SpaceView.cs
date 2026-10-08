using WingCommander.Graphics.Cockpit;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The space buffer and the classic CPU space view: main.c GetScreenUpdateFlag,
// initialize_view_buffer, dump_buffer_to_screen, clear_view_buffer; eventmgr.c
// draw_sorted_objects_to_buffer, intro_drawbackgroundships, set_up_screen_viewport.
internal sealed partial class FlightSession
{
    /// <summary>The off-screen space view (its rectangle is (0,0)-(W-1,H-1) in its own coordinates).</summary>
    /// <remarks>C: stSpaceBuffer.</remarks>
    public Viewport SpaceBuffer { get; } = new(null, 0, 0, 319, 199);

    /// <remarks>C: bViewBufferEnabled.</remarks>
    private bool _viewBufferEnabled;

    /// <summary>The view geometry index selected last: 0..3 cockpit views, 4 letterbox, 5 full screen, -1 forces a set-up.</summary>
    /// <remarks>C: cScreenViewportMode.</remarks>
    public sbyte ScreenViewportMode { get; set; } = -1;

    /// <remarks>C: pScreenViewportGeometry.</remarks>
    public ViewGeometry? ScreenViewportGeometry { get; private set; }

    /// <summary>The view geometries of the current cockpit (PCSHIP section 6, or COCKPIT.VGA 8:8 during the ejection).</summary>
    /// <remarks>C: pScreenViewportPacket.</remarks>
    public ViewGeometrySet? ScreenViewportPacket { get; set; }

    /// <remarks>C: nViewportOriginX.</remarks>
    public short ViewportOriginX { get; private set; }

    /// <remarks>C: nViewportOriginY.</remarks>
    public short ViewportOriginY { get; private set; }

    /// <summary>Counts the sprites drawn by the last <see cref="DrawSortedObjectsToBuffer"/> (tests).</summary>
    public int DrawnObjectCount { get; private set; }

    /// <summary>Ends a showing message and frees the space buffer.</summary>
    /// <remarks>C: GetScreenUpdateFlag (0x4279D0, main.c).</remarks>
    public void GetScreenUpdateFlag()
    {
        if (MessageShowing())
            EndCommMenu();
        if (SpaceBuffer.IsAllocated)
            SpaceBuffer.FreeViewport();
    }

    /// <summary>Allocates the space buffer for its rectangle, cleared to the space colour.</summary>
    /// <remarks>C: initialize_view_buffer (0x427A00, main.c); the allocation cannot fail here.</remarks>
    public void InitializeViewBuffer()
    {
        if (_viewBufferEnabled && !SpaceBuffer.IsAllocated &&
            !SpaceBuffer.AllocateViewport(PaletteColours.PrimaryViewBuffer))
            throw new InvalidOperationException("ERROR: Out of memory for SPACE BUFFER");
    }

    /// <remarks>C: clear_view_buffer (0x427B00, main.c).</remarks>
    public void ClearViewBuffer() => Gfx.ClearViewport(SpaceBuffer, PaletteColours.PrimaryViewBuffer);

    /// <summary>Sets the space buffer rectangle (0,0)-(width-1,height-1).</summary>
    /// <remarks>C: SetViewportRect(&amp;stSpaceBuffer, 0, 0, w - 1, h - 1).</remarks>
    private void SetSpaceBufferSize(int width, int height) => SpaceBuffer.SetViewportRect(0, 0, width - 1, height - 1);

    /// <summary>
    /// Space buffer re-allocation around a view change in cockpitless mode (the original's "dance"):
    /// the buffer gets the geometry size while the view changes and 320x200 afterwards.
    /// </summary>
    private void BeginCockpitlessViewChange()
    {
        GetScreenUpdateFlag();
        SetSpaceBufferSize(Sim.ScreenWidth, Sim.ScreenHeight);
        InitializeViewBuffer();
    }

    /// <summary>Second half of <see cref="BeginCockpitlessViewChange"/>: back to the 320x200 buffer.</summary>
    /// <remarks>C: the restore_normal_viewport tail of HandleSpaceFlightControls (hudmsg.c).</remarks>
    private void EndCockpitlessViewChange()
    {
        GetScreenUpdateFlag();
        SetSpaceBufferSize(320, 200);
        InitializeViewBuffer();
    }

    /// <summary>Changes the camera like <c>new_view</c>, with the cockpitless buffer dance.</summary>
    public void NewViewWithBuffer(int view, short obj)
    {
        if (Sim.CockpitlessView == 0)
        {
            Sim.NewView(view, obj);
            return;
        }
        BeginCockpitlessViewChange();
        Sim.NewView(view, obj);
        EndCockpitlessViewChange();
    }

    /// <summary>Forces a camera view like <c>force_view</c>, with the cockpitless buffer dance.</summary>
    public void ForceViewWithBuffer(int view, short obj)
    {
        if (Sim.CockpitlessView == 0)
        {
            Sim.ForceView(view, obj);
            return;
        }
        BeginCockpitlessViewChange();
        Sim.ForceView(view, obj);
        EndCockpitlessViewChange();
    }

    /// <summary>
    /// Selects the view geometry: the built-in letterbox (4) and full-screen (5) records or a
    /// geometry of the cockpit's packet, and the projection values the simulation reads. In
    /// cockpitless mode the buffer is 320x200 and the view centre follows the cockpit's aim point.
    /// </summary>
    /// <remarks>C: set_up_screen_viewport (0x436740, eventmgr.c). A geometry index the packet does
    /// not have (the single-view simulator cockpit) uses geometry 0; the original read past the
    /// offset table.</remarks>
    public void SetUpScreenViewport(sbyte mode)
    {
        ScreenViewportMode = mode;
        ViewGeometry geometry;
        if (mode == 4)
            geometry = ViewGeometry.Mode4;
        else if (mode == 5)
            geometry = ViewGeometry.Mode5;
        else if (ScreenViewportPacket is { } packet && packet.Count > 0)
            geometry = packet[(uint)mode < (uint)packet.Count ? mode : 0];
        else
            geometry = ViewGeometry.Mode5;
        ScreenViewportGeometry = geometry;

        var sim = Sim;
        sim.ScreenWidth = geometry.Width;
        sim.ViewCenterX = (short)(geometry.Width / 2);
        sim.ScreenHeight = geometry.Height;
        sim.ViewCenterY = (short)(geometry.Height / 2);
        ViewportOriginX = geometry.OriginX;
        ViewportOriginY = geometry.OriginY;
        if (sim.CockpitlessView != 0 && sim.CockpitlessView != -2)
        {
            switch (sim.CockpitView)
            {
                case 0:
                    ViewportOriginY += 10;
                    sim.ViewCenterY += 10;
                    break;
                case 1:
                    ViewportOriginY += 25;
                    sim.ViewCenterY += 25;
                    break;
                case 2:
                    ViewportOriginY += 50;
                    sim.ViewCenterY += 50;
                    break;
            }
            sim.ScreenWidth = 320;
            sim.ScreenHeight = 200;
        }
    }

    /// <summary>
    /// Copies the space buffer onto the screen: the whole 320x200 buffer in cockpitless mode, the
    /// letterbox buffer onto rows 24..151, the full-screen buffer as is, and otherwise through the
    /// cockpit window's run list.
    /// </summary>
    /// <remarks>C: dump_buffer_to_screen (0x427A40, main.c). An unallocated buffer (the original's
    /// fatal "bad viewport") is skipped.</remarks>
    public void DumpBufferToScreen()
    {
        CompleteSpaceSpriteFrame();
        if (!SpaceBuffer.IsAllocated)
            return;
        if (Sim.CockpitlessView > 0)
        {
            Gfx.CopyViewportContents(SpaceBuffer, Screen);
            return;
        }
        switch (ScreenViewportMode)
        {
            case 4:
            {
                var letterbox = Screen.Clone();
                letterbox.Top = 24;
                letterbox.Bottom = 152;
                Gfx.CopyViewportContents(SpaceBuffer, letterbox);
                break;
            }
            case 5:
                Gfx.CopyViewportContents(SpaceBuffer, Screen);
                break;
            default:
                Gfx.FizzleFade(SpaceBuffer, Screen, ScreenViewportGeometry ?? ViewGeometry.Mode5);
                break;
        }
    }

    /// <summary>
    /// Draws the sorted objects far to near into the space buffer: stars and dust (and the nav
    /// pointer) unscaled from the constellation shape, everything else rotated and scaled. Writes
    /// the draw positions back into the objects.
    /// </summary>
    /// <remarks>C: draw_sorted_objects_to_buffer (0x436520, eventmgr.c). Planets take the scaled
    /// path (SDL port) unless <see cref="FlightOptions.DrawPlanets"/> is off (Kilrathi Saga: a dust
    /// dot). The simulation leaves the nav pointer's shape empty; it is the cockpit's target-lock
    /// shape (COCKPIT.VGA 8:0).</remarks>
    public void DrawSortedObjectsToBuffer()
    {
        var sim = Sim;
        DrawnObjectCount = 0;
        ShapeTable? constellation = Shapes.Get(sim.ConstellationShape);
        ShapeTable? navPointer = TargetLockShape;
        bool planetsAsDust = !Options.DrawPlanets;
        for (int index = 0; index < sim.SortedObjects.Length; index++)
        {
            int obj = sim.SortedObjects[index];
            if (obj < 0)
                return;
            ref var o = ref sim.Objects[obj];
            if ((int)o.Type < 0)
                return;
            var objectClass = o.Class;
            if (objectClass == ObjectClass.Null)
                continue;
            short x = unchecked((short)(o.ScreenX + sim.ViewCenterX));
            short y = unchecked((short)(o.ScreenY + sim.ViewCenterY));
            o.DrawX = x;
            o.DrawY = y;
            if (objectClass is ObjectClass.Star or ObjectClass.Dust ||
                (objectClass == ObjectClass.Planet && planetsAsDust))
            {
                ShapeTable? shape = obj == sim.NavPointerObject ? navPointer : constellation;
                if (!TryRecordObjectSprite(shape, obj, o.ViewFrame, 0, 0x100, 0))
                    Gfx.DrawSpriteDefault(SpaceBuffer, x, y, shape, o.ViewFrame);
                DrawnObjectCount++;
                continue;
            }
            ShapeTable? sprite = obj == sim.NavPointerObject ? navPointer : Shapes.Get(o.Shape);
            if (sprite is null)
                continue;
            if (!TryRecordObjectSprite(sprite, obj, o.ViewFrame, o.ScreenAngle, o.ScreenScale, o.Flip))
                Gfx.DrawSpriteScaled(SpaceBuffer, x, y, sprite, o.ViewFrame, o.ScreenAngle, o.ScreenScale, o.Flip);
            DrawnObjectCount++;
        }
    }

    /// <summary>
    /// The attract mode erases last frame's sprites instead of clearing the buffer, so a subtitle
    /// drawn into the same buffer survives: every object is redrawn as a silhouette in the space
    /// colour at its last draw position.
    /// </summary>
    /// <remarks>C: intro_drawbackgroundships (0x436650, eventmgr.c).</remarks>
    public void IntroDrawBackgroundShips()
    {
        var sim = Sim;
        ShapeTable? constellation = Shapes.Get(sim.ConstellationShape);
        bool planetsAsDust = !Options.DrawPlanets;
        for (int obj = 0; obj < sim.Objects.Length; obj++)
        {
            ref var o = ref sim.Objects[obj];
            if ((int)o.Type < 0)
                return;
            var objectClass = o.Class;
            if (objectClass == ObjectClass.Null)
                continue;
            if (objectClass is ObjectClass.Star or ObjectClass.Dust ||
                (objectClass == ObjectClass.Planet && planetsAsDust))
            {
                ShapeTable? shape = obj == sim.NavPointerObject ? TargetLockShape : constellation;
                Gfx.DrawSolidColourSprite(SpaceBuffer, o.DrawX, o.DrawY, shape, o.ViewFrame,
                    PaletteColours.PrimaryViewBuffer);
                continue;
            }
            ShapeTable? sprite = obj == sim.NavPointerObject ? TargetLockShape : Shapes.Get(o.Shape);
            if (sprite is not null)
            {
                Gfx.DrawSolidColourSpriteScaled(SpaceBuffer, o.DrawX, o.DrawY, sprite, o.ViewFrame, o.ScreenAngle,
                    o.ScreenScale, o.Flip, PaletteColours.PrimaryViewBuffer);
            }
        }
    }

    /// <summary>The white hyperspace flash: the next space frame is drawn on a white buffer.</summary>
    /// <remarks>C: <c>ClearViewport(&amp;stSpaceBuffer, cViewportClearColour); bViewportDirty = 1;</c> in
    /// warp/unwarp (hudmsg.c) and death_sequence (screens.c).</remarks>
    public void FlashSpaceBuffer()
    {
        Gfx.ClearViewport(SpaceBuffer, PaletteColours.ViewportClear);
        SpaceBufferBackground = PaletteColours.ViewportClear;
    }
}
