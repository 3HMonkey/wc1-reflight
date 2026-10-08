using WingCommander.Core.Platform;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;
using WingCommander.Game.Flow;
using WingCommander.Graphics.Raster;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Tests;

namespace WingCommander.Game.Flight.Tests;

/// <summary>R2 (M7): the space sprites recorded for an output-resolution renderer.</summary>
public class SpaceSpriteTests
{
    private static FlightOptions R2Options => new() { RendererSupportsSpaceSprites = true };

    private static void PressAt(FlightRig rig, int scanCode, int virtualKey, double now)
    {
        var events = rig.Runtime.Events;
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), now + 1);
        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, scanCode, virtualKey, 0, 0, 0, HostModifiers.None, false), now + 60);
    }

    /// <summary>Flies series 1 mission 0, presses <paramref name="scanCode"/> after frame 10 (0: none) and captures frame <paramref name="frame"/>.</summary>
    private static (byte[] Working, FlightSession.SpaceSpriteFrame? Published, SpaceView? Shown, FlightRig Rig) Capture(
        FlightOptions? options, int seed, int frame, int scanCode = 0, int virtualKey = 0)
    {
        var rig = new FlightRig(seed, options);
        byte[] working = [];
        FlightSession.SpaceSpriteFrame? published = null;
        SpaceView? shown = null;
        rig.Start(async r =>
        {
            r.StopAfter(frame, (session, count) =>
            {
                if (count == 10 && scanCode != 0)
                    PressAt(r, scanCode, virtualKey, r.Runtime.Scheduler.Now);
                if (count == frame)
                {
                    working = (byte[])r.Game.Display.Working.Pixels.Clone();
                    published = session.PublishedSpaceFrame;
                    shown = r.Game.Runtime.Frame.Space;
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        return (working, published, shown, rig);
    }

    [DataFact]
    public void Without_an_R2_renderer_the_cpu_draws_everything()
    {
        var (_, published, shown, rig) = Capture(null, 5, 30);
        Assert.Null(published);
        Assert.Null(shown);
        Assert.Equal(0, rig.Session.PublishedSpaceFrames);

        var off = Capture(new FlightOptions { RendererSupportsSpaceSprites = true, SpriteSpaceView = false }, 5, 30);
        Assert.Null(off.Published);
        Assert.Null(off.Shown);
    }

    [DataFact]
    public void Recorded_sprites_are_the_drawn_objects_in_painter_order()
    {
        var rig = new FlightRig(7, R2Options);
        var expectedSlots = new List<short>();
        var recordedSlots = new List<short>();
        var positionErrors = new List<string>();
        ScreenRect clip = default;
        bool shownIsPublished = false;
        rig.Start(async r =>
        {
            r.StopAfter(60, (session, count) =>
            {
                if (count != 60)
                    return;
                var sim = session.Sim;
                foreach (int obj in sim.SortedObjects)
                {
                    if (obj < 0 || (int)sim.Objects[obj].Type < 0)
                        break;
                    ref readonly var o = ref sim.Objects[obj];
                    if (o.Class == ObjectClass.Null)
                        continue;
                    var shape = obj == sim.NavPointerObject ? session.TargetLockShape
                        : o.Class is ObjectClass.Star or ObjectClass.Dust ? session.Shapes.Get(sim.ConstellationShape)
                        : session.Shapes.Get(o.Shape);
                    if (shape is null || !shape.HasFrame(o.ViewFrame))
                        continue;
                    expectedSlots.Add((short)obj);
                }
                var frame = session.PublishedSpaceFrame!;
                shownIsPublished = ReferenceEquals(r.Game.Runtime.Frame.Space, frame.View);
                clip = frame.View.Sprites.Clip;
                var items = frame.View.Sprites.Items;
                for (int i = 0; i < items.Length; i++)
                {
                    recordedSlots.Add(items[i].ObjectSlot);
                    var (x, y) = frame.BufferPositions[i];
                    float dx = items[i].X - (x + frame.OffsetX), dy = items[i].Y - (y + frame.OffsetY);
                    if (Math.Abs(dx) > 1f || Math.Abs(dy) > 1f)
                        positionErrors.Add($"slot {items[i].ObjectSlot}: ({items[i].X}, {items[i].Y}) vs ({x}, {y})");
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        Assert.True(shownIsPublished);
        Assert.True(expectedSlots.Count > 7, $"objects: {expectedSlots.Count}");
        Assert.Equal(expectedSlots, recordedSlots);
        Assert.Empty(positionErrors);
        // Hornet front view: the 320x105 space buffer at (0, 10).
        Assert.Equal(new ScreenRect(0, 10, 320, 105), clip);
        // The 25 frames of the launch, then the flight.
        Assert.Equal(25 + 60, rig.Session.PublishedSpaceFrames);
    }

    public static TheoryData<string, int, int> Views => new()
    {
        { "front", 0, 0 },
        { "rear", 0x3e, 0x73 },
        { "chase", 0x3f, 0x74 },
        { "overview", 0x40, 0x75 },
        { "cockpitless", 0x3b, 0x70 },
    };

    [DataTheory]
    [MemberData(nameof(Views))]
    public void Composing_the_recorded_sprites_gives_the_cpu_frame(string name, int scanCode, int virtualKey)
    {
        const int Frame = 40;
        var classic = Capture(null, 13, Frame, scanCode, virtualKey);
        var r2 = Capture(R2Options, 13, Frame, scanCode, virtualKey);
        Assert.Equal(classic.Rig.Layer.Simulation.ComputeStateHash(), r2.Rig.Layer.Simulation.ComputeStateHash());
        var published = r2.Published;
        Assert.NotNull(published);
        Assert.Same(published!.View, r2.Shown);
        Assert.True(published.View.Sprites.Count > 3, $"{name}: {published.View.Sprites.Count} sprites");

        // Without its sprites the frame shows the space colour where the CPU drew them, nothing else differs.
        byte background = published.View.BackgroundIndex;
        int removed = 0;
        for (int i = 0; i < Framebuffer.PixelCount; i++)
        {
            if (classic.Working[i] == r2.Working[i])
                continue;
            Assert.Equal(background, r2.Working[i]);
            removed++;
        }
        Assert.True(removed > 20, $"{name}: {removed} sprite pixels");

        var composed = (byte[])r2.Working.Clone();
        r2.Rig.Session.ComposeSpaceSprites(published, composed);
        int mismatches = 0;
        for (int i = 0; i < Framebuffer.PixelCount; i++)
        {
            if (composed[i] != classic.Working[i])
                mismatches++;
        }
        Assert.Equal(0, mismatches);
    }

    [DataFact]
    public void Images_are_decoded_once_with_the_hot_spot_as_origin()
    {
        var (_, published, _, rig) = Capture(R2Options, 17, 30);
        Assert.NotNull(published);
        var session = rig.Session;
        var images = published!.View.Images;
        var keys = new HashSet<SpriteImageKey>();
        foreach (var sprite in published.View.Sprites.Items)
            keys.Add(sprite.Image);
        Assert.True(keys.Count > 1);
        foreach (var key in keys)
        {
            Assert.True(images.TryGet(key, out var image), key.ToString());
            var shape = session.Shapes.Get(key.LogicalFile, key.Section);
            Assert.NotNull(shape);
            var extents = shape!.GetExtents(key.Frame);
            Assert.Equal(extents.Width, image.Width);
            Assert.Equal(extents.Height, image.Height);
            Assert.Equal(extents.Left, image.OriginX);
            Assert.Equal(extents.Top, image.OriginY);
            // The CPU's unscaled draw at the hot spot covers exactly the opaque image pixels.
            var low = Viewport.Allocate(0, 0, image.Width - 1, image.Height - 1, 0x00);
            var high = Viewport.Allocate(0, 0, image.Width - 1, image.Height - 1, 0xFF);
            rig.Game.Graphics.DrawSpriteDefault(low, image.OriginX, image.OriginY, shape, key.Frame);
            rig.Game.Graphics.DrawSpriteDefault(high, image.OriginX, image.OriginY, shape, key.Frame);
            var pixels = image.Pixels.Span;
            for (int i = 0; i < pixels.Length; i++)
            {
                bool covered = low.Surface!.Pixels[i] == high.Surface!.Pixels[i];
                byte expected = covered ? low.Surface.Pixels[i] : SpriteImage.TransparentIndex;
                Assert.Equal(expected, pixels[i]);
            }
        }
        int count = images.Count;
        int generation = images.Generation;
        // Another 30 frames of the same scene add new frames at most, never replace one.
        Assert.True(count >= keys.Count);
        Assert.Equal(0, generation);
    }

    [DataFact]
    public void The_shown_sprites_follow_the_classic_frame()
    {
        var rig = new FlightRig(19, R2Options);
        bool keptOnRepresent = false;
        bool shownAfterFlight = true;
        bool shownAfterClear = true;
        rig.Start(async r =>
        {
            var display = r.Game.Display;
            r.StopAfter(30, (session, count) =>
            {
                if (count == 20)
                {
                    // A re-present of the same picture (cursor or palette update) keeps the sprites.
                    display.Update();
                    display.PaletteChanged();
                    keptOnRepresent = ReferenceEquals(r.Game.Runtime.Frame.Space, session.PublishedSpaceFrame!.View);
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
            // The last flight frame is still on screen.
            shownAfterFlight = r.Game.Runtime.Frame.Space is not null;
            await display.ClearViewportAsync(r.Game.Graphics.Screen!, 0);
            shownAfterClear = r.Game.Runtime.Frame.Space is not null;
        });
        rig.Run();
        Assert.True(keptOnRepresent);
        Assert.True(shownAfterFlight);
        Assert.False(shownAfterClear);
    }

    [DataFact]
    public void A_paused_frame_keeps_its_objects()
    {
        static (byte[] Front, bool Shown, int Hash) Paused(FlightOptions? options)
        {
            var rig = new FlightRig(23, options);
            double pauseTime = 0;
            rig.Start(async r =>
            {
                r.StopAfter(200, (session, count) =>
                {
                    if (count == 30)
                    {
                        pauseTime = r.Runtime.Scheduler.Now;
                        PressAt(r, 0x19, 'P', pauseTime);
                    }
                });
                await r.Layer.FlyMissionAsync(1, 0);
            });
            rig.Runtime.RunHeadless(4_000);
            Assert.Null(rig.Runtime.Failure);
            Assert.True(rig.Runtime.Scheduler.Now > pauseTime + 500);
            var front = (byte[])rig.Front.Pixels.Clone();
            return (front, rig.Game.Runtime.Frame.Space is not null, rig.Session.PresentedSpaceFrames);
        }

        var classic = Paused(null);
        var r2 = Paused(R2Options);
        Assert.Equal(classic.Hash, r2.Hash);
        Assert.False(r2.Shown);
        Assert.Equal(classic.Front, r2.Front);
    }

    [DataFact]
    public void The_3d_state_of_the_tick_and_the_tick_before_comes_with_the_sprites()
    {
        var rig = new FlightRig(41, R2Options);
        SpaceViewState? current = null, previous = null;
        int objects = 0;
        System.Numerics.Vector3 eye = default;
        var drawnSlots = new List<short>();
        var spriteSlots = new List<short>();
        bool screenAgrees = true;
        rig.Start(async r =>
        {
            r.StopAfter(30, (session, count) =>
            {
                if (count != 30)
                    return;
                var view = r.Game.Runtime.Frame.Space!;
                current = view.Current;
                previous = view.Previous;
                var sim = session.Sim;
                for (int obj = 0; obj <= ObjectSlots.LastMoving; obj++)
                {
                    if (sim.Objects[obj].Class != ObjectClass.Null)
                        objects++;
                }
                var e = sim.Objects[ObjectSlots.Eye].Position;
                eye = new System.Numerics.Vector3(e.X / 256f, e.Y / 256f, e.Z / 256f);
                foreach (short index in current!.DrawOrder)
                    drawnSlots.Add(current.Objects[index].Slot);
                var frame = session.PublishedSpaceFrame!;
                var items = frame.View.Sprites.Items;
                for (int i = 0; i < items.Length; i++)
                {
                    spriteSlots.Add(items[i].ObjectSlot);
                    int index = current.IndexOfSlot(items[i].ObjectSlot);
                    var (x, y) = frame.BufferPositions[i];
                    if (index < 0 || current.Objects[index].ScreenX != x + frame.OffsetX || current.Objects[index].ScreenY != y + frame.OffsetY ||
                        current.Objects[index].Sprite != items[i].Image)
                        screenAgrees = false;
                }
            });
            await r.Layer.FlyMissionAsync(1, 0);
        });
        rig.Run();
        Assert.NotNull(current);
        Assert.NotNull(previous);
        Assert.Equal(current!.SpaceFrame - 1, previous!.SpaceFrame);
        Assert.Equal(objects, current.Count);
        Assert.Equal(eye, current.Camera.Position);
        Assert.Equal(160f, current.Camera.FocalLength);
        Assert.Equal(new ScreenRect(0, 10, 320, 105), current.Camera.Viewport);
        // Every sprite is an object of the draw order, at the same place with the same image.
        Assert.True(screenAgrees);
        Assert.All(spriteSlots, slot => Assert.Contains(slot, drawnSlots));
    }

    [DataFact]
    public void Attract_mode_and_canned_scenes_publish_their_sprites()
    {
        var rig = new FlightRig(29, R2Options);
        int attractFrames = 0, attractPublished = 0, victoryPublished = 0;
        bool shownAfterMenu = true;
        rig.Start(async r =>
        {
            r.Key(r.Runtime.Scheduler.Now + 3_000, 0x39, ' ');
            await r.Layer.PlayAttractSequenceAsync();
            attractFrames = r.Session.AttractFrames;
            attractPublished = r.Session.PublishedSpaceFrames;
            await r.Game.Display.ClearViewportAsync(r.Game.Graphics.Screen!, 0);
            shownAfterMenu = r.Game.Runtime.Frame.Space is not null;
            using (var scene = r.Layer.BeginCannedScene(CannedScene.CampaignVictory))
            {
                for (int frame = 0; frame < 30; frame++)
                {
                    scene!.Step();
                    await r.Game.Display.PresentAsync();
                }
            }
            victoryPublished = r.Session.PublishedSpaceFrames - attractPublished;
        });
        rig.Run(60_000);
        Assert.True(attractFrames > 30);
        Assert.Equal(attractFrames, attractPublished);
        Assert.False(shownAfterMenu);
        Assert.Equal(30, victoryPublished);
    }
}
