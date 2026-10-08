using WingCommander.Core.Numerics;
using WingCommander.Core.Rendering;
using WingCommander.Core.Resources;
using WingCommander.Core.Video;
using WingCommander.Graphics.Cockpit;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Simulation;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// R2 (ADR-010, docs/analysis/rendering.md §3.1, §4.4): the space-view sprites go to a renderer that
// draws them at output resolution under the classic cockpit and HUD. The SDL port's recorder
// (sdl/gl_renderer.c: SdlBeginSpaceFrame, SdlRecordSpaceSprite, SdlCompleteSpaceFrame,
// SdlCancelSpaceFrame, the presented-frame snapshot) ported onto Core.Rendering: a recorded sprite
// is not drawn by the CPU; the classic frame keeps the space colour there, and the renderer
// replaces exactly those pixels inside the window mask. Without an R2 renderer (or with
// FlightOptions.SpriteSpaceView off) nothing is recorded and the CPU draws everything.
internal sealed partial class FlightSession
{
    private enum SpaceSpriteState
    {
        Idle,
        Recording,
        Complete,
    }

    /// <summary>One recorded space frame: the renderer's view plus what the CPU path needs to redraw it.</summary>
    internal sealed class SpaceSpriteFrame(SpriteImageCache images, SpaceViewMask mask)
    {
        public SpaceView View { get; } = new(images) { WindowMask = mask };

        /// <summary>The 3D state of this tick and of the tick before (R2b interpolation, R3 meshes).</summary>
        public SpaceViewState Current { get; } = new();

        public SpaceViewState Previous { get; } = new();

        /// <summary>The integer space-buffer position the CPU would have drawn each sprite at.</summary>
        public List<(short X, short Y)> BufferPositions { get; } = [];

        /// <summary>Screen position of the space buffer's (0, 0) (the geometry origin, or 0 for full-screen copies).</summary>
        public int OffsetX { get; set; }

        public int OffsetY { get; set; }

        /// <summary>Size of the space buffer the sprites were recorded for.</summary>
        public int BufferWidth { get; set; }

        public int BufferHeight { get; set; }

        /// <summary>The cockpit window: the geometry's run list, or the whole screen.</summary>
        public ViewGeometry? MaskGeometry { get; set; }

        public bool MaskFull { get; set; }

        public void Clear()
        {
            View.Sprites.Clear();
            BufferPositions.Clear();
        }
    }

    /// <summary>The colour the space buffer was cleared to before this frame's objects (0xBF, or 0x0F
    /// on a hyperspace flash frame); the R2 occlusion test uses it.</summary>
    public byte SpaceBufferBackground { get; private set; } = PaletteColours.PrimaryViewBuffer;

    /// <summary>Decoded sprite frames shared with the renderer (decoded once, on first use).</summary>
    private readonly SpriteImageCache _spriteImages = new();

    /// <summary>The cockpit window of the published frame (shared by both frames, rebuilt on publish).</summary>
    private readonly SpaceViewMask _windowMask = new();

    /// <summary>The frame being recorded and the frame the renderer shows (swapped on publish).</summary>
    private SpaceSpriteFrame? _pendingSpaceFrame;
    private SpaceSpriteFrame? _publishedSpaceFrame;

    private SpaceSpriteState _spaceSpriteState;
    private bool _softwareForRestOfFrame;

    /// <summary>What <see cref="_windowMask"/> currently shows.</summary>
    private ViewGeometry? _windowMaskGeometry;
    private bool _windowMaskFull;
    private bool _windowMaskValid;

    /// <summary>The simulation's view of the last recorded tick, and its renderer copy (the next frame's Previous).</summary>
    private readonly SpaceViewSnapshot _spaceSnapshot = new();
    private readonly SpaceViewState _lastSpaceState = new();

    /// <summary>The cursor-free classic frame the published sprites belong to.</summary>
    private readonly byte[] _publishedBase = new byte[Framebuffer.PixelCount];
    private bool _presentHookInstalled;

    /// <summary>Whether space sprites go to the renderer (the option and the host's renderer).</summary>
    public bool SpaceSpritesActive => Options.SpriteSpaceView && Options.RendererSupportsSpaceSprites;

    /// <summary>The frame in <c>RenderFrame.Space</c> (or the last one published), for tests.</summary>
    internal SpaceSpriteFrame? PublishedSpaceFrame => _publishedSpaceFrame;

    /// <summary>Space frames handed to the renderer so far (tests).</summary>
    internal int PublishedSpaceFrames { get; private set; }

    /// <summary>
    /// Starts recording the sprites of a space frame: sprite positions are space-buffer coordinates
    /// plus the geometry origin (none for the cockpitless and full-screen views), the window is the
    /// geometry's run list (or the whole screen), sprites replace the buffer's clear colour.
    /// </summary>
    /// <remarks>C: SdlBeginSpaceFrame (sdl/gl_renderer.c), called after sort_object_depth in
    /// Draw_3Space_Frame and ShowCarrierLaunchSequence; BuildSpaceViewMask.</remarks>
    private void BeginSpaceSpriteFrame()
    {
        if (!SpaceSpritesActive)
        {
            _spaceSpriteState = SpaceSpriteState.Idle;
            return;
        }
        EnsurePresentHook();
        var frame = _pendingSpaceFrame ??= new SpaceSpriteFrame(_spriteImages, _windowMask);
        frame.Clear();
        _softwareForRestOfFrame = false;
        _spaceSpriteState = SpaceSpriteState.Recording;

        var geometry = ScreenViewportGeometry;
        bool full = Sim.CockpitlessView > 0 || ScreenViewportMode == 5 || geometry is null;
        frame.OffsetX = full ? 0 : geometry!.OriginX;
        frame.OffsetY = full ? 0 : geometry!.OriginY;
        frame.MaskGeometry = full ? null : geometry;
        frame.MaskFull = full;
        var buffer = SpaceBuffer;
        frame.BufferWidth = buffer.Right - buffer.Left + 1;
        frame.BufferHeight = buffer.Bottom - buffer.Top + 1;
        frame.View.BackgroundIndex = SpaceBufferBackground;
        frame.View.Sprites.Clip = new ScreenRect(buffer.Left + frame.OffsetX, buffer.Top + frame.OffsetY,
            frame.BufferWidth, frame.BufferHeight);
        CaptureSpaceViewState(frame);
    }

    /// <summary>
    /// The renderer copy of the simulation's view of this tick (float world units, absolute screen
    /// positions, the sprite of the draw list) and of the tick before.
    /// </summary>
    /// <remarks>docs/analysis/rendering.md §3.2 (mapping table).</remarks>
    private void CaptureSpaceViewState(SpaceSpriteFrame frame)
    {
        var sim = Sim;
        var snapshot = _spaceSnapshot;
        sim.CaptureSpaceView(snapshot);
        frame.Previous.CopyFrom(_lastSpaceState);
        var state = frame.Current;
        state.Clear();
        state.SpaceFrame = snapshot.SpaceFrame;
        state.CameraViewMode = snapshot.CameraViewMode;
        float centreX = frame.OffsetX + snapshot.ViewCenterX;
        float centreY = frame.OffsetY + snapshot.ViewCenterY;
        state.Camera = new SpaceCamera
        {
            Position = WorldUnits(snapshot.CameraPosition),
            Velocity = WorldUnits(snapshot.CameraVelocity),
            Right = WorldUnits(snapshot.CameraRight),
            Up = WorldUnits(snapshot.CameraUp),
            Forward = WorldUnits(snapshot.CameraForward),
            NearRadius = snapshot.CameraNearRadius,
            FocalLength = (snapshot.ScreenWidth & ~1) / 2,
            CenterX = centreX,
            CenterY = centreY,
            Viewport = frame.View.Sprites.Clip,
        };
        var constellation = Shapes.Get(sim.ConstellationShape);
        foreach (ref readonly var o in snapshot.ActiveObjects)
        {
            ref var s = ref state.Add();
            s.Slot = o.Slot;
            s.Type = (short)o.Type;
            s.Class = (short)o.Class;
            s.Owner = o.Owner;
            s.Visible = o.Visible;
            s.IsNavPointer = o.IsNavPointer;
            s.IsSkyObject = o.Class is ObjectClass.Star or ObjectClass.Planet;
            s.Position = WorldUnits(o.Position);
            s.Velocity = WorldUnits(o.Velocity);
            s.Right = WorldUnits(o.Right);
            s.Up = WorldUnits(o.Up);
            s.Forward = WorldUnits(o.Forward);
            s.ViewPosition = WorldUnits(o.ViewPosition);
            s.Scale = o.Scale / 256f;
            s.CollisionRadius = o.CollisionRadius;
            // The sprite this port draws: stars, dust (and planets without DrawPlanets) from the
            // constellation, the nav pointer with the target-lock shape, the rest with its own shape.
            var shape = o.IsNavPointer ? TargetLockShape
                : o.Class is ObjectClass.Star or ObjectClass.Dust || (o.Class == ObjectClass.Planet && !Options.DrawPlanets) ? constellation
                : Shapes.Get(o.DrawShape);
            if (shape is not null && Shapes.TryGetOrigin(shape, out int logicalFile, out int section))
                s.Sprite = SpriteImageKey.Create(logicalFile, section, o.ViewFrame);
            s.Flip = (SpriteFlip)(o.Flip & 0x30);
            s.ScreenAngle = o.ScreenAngle;
            s.ScreenScale = o.ScreenScale / 256f;
            s.ScreenX = centreX + o.ScreenX;
            s.ScreenY = centreY + o.ScreenY;
            s.Distance = o.Distance;
            s.ExhaustHeat = o.ExhaustHeat;
        }
        for (int i = 0; i < snapshot.DrawCount; i++)
        {
            int index = state.IndexOfSlot(snapshot.DrawOrder[i]);
            if (index >= 0)
                state.AddToDrawOrder(index);
        }
        _lastSpaceState.CopyFrom(state);
        frame.View.Current = state;
        frame.View.Previous = frame.Previous;
    }

    private static System.Numerics.Vector3 WorldUnits(in FixedVector v) => new(v.X / 256f, v.Y / 256f, v.Z / 256f);

    /// <summary>Records a sprite at an integer space-buffer position; false means the CPU draws it.</summary>
    private bool TryRecordSpaceSprite(ShapeTable? shape, int frame, int x, int y, int angle, int scale, int flip, int slot) =>
        TryRecordSpaceSprite(shape, frame, x, y, x, y, angle, scale, flip, slot);

    /// <summary>Records an object's sprite at its sub-pixel position.</summary>
    private bool TryRecordObjectSprite(ShapeTable? shape, int obj, int frame, int angle, int scale, int flip)
    {
        if (_spaceSpriteState != SpaceSpriteState.Recording || _softwareForRestOfFrame)
            return false;
        var sim = Sim;
        ref readonly var o = ref sim.Objects[obj];
        var (subX, subY) = SubPixelScreenPosition(obj);
        return TryRecordSpaceSprite(shape, frame, unchecked((short)(o.ScreenX + sim.ViewCenterX)),
            unchecked((short)(o.ScreenY + sim.ViewCenterY)), subX, subY, angle, scale, flip, obj);
    }

    /// <summary>
    /// Records a sprite for the renderer; false means the CPU draws it. Refused: no recording frame,
    /// an earlier fallback in this frame (painter order), no shape or frame, scale 0, an unknown
    /// flip, and rotated/scaled/flipped frames above 0xFA00 pixels (the CPU draws nothing for those
    /// either). An image that cannot be decoded sends this and every later sprite of the frame to
    /// the CPU.
    /// </summary>
    /// <remarks>C: SdlRecordSpaceSprite / SdlGlRendererRecordSpaceSprite (sdl/gl_renderer.c).</remarks>
    private bool TryRecordSpaceSprite(ShapeTable? shape, int frame, int x, int y, float subX, float subY, int angle,
        int scale, int flip, int slot)
    {
        if (_spaceSpriteState != SpaceSpriteState.Recording || _softwareForRestOfFrame)
            return false;
        if (shape is null || frame < 0 || scale == 0 || !shape.HasFrame(frame))
            return false;
        if (flip is not (0 or 0x10 or 0x20 or 0x30))
            return false;
        ShapeExtents extents;
        try
        {
            extents = shape.GetExtents(frame);
        }
        catch (GameDataException)
        {
            return false;
        }
        int width = extents.Width;
        int height = extents.Height;
        if (width <= 0 || height <= 0)
            return false;
        long pixelCount = (long)(ushort)width * (ushort)height;
        if (pixelCount > RleRenderer.TransformScratchSize && !(angle == 0 && scale == 0x100 && flip == 0))
            return false;
        if (!Shapes.TryGetOrigin(shape, out int logicalFile, out int section) ||
            !TryCacheSpriteImage(SpriteImageKey.Create(logicalFile, section, frame), shape, frame, extents))
        {
            _softwareForRestOfFrame = true;
            return false;
        }

        var recorded = _pendingSpaceFrame!;
        ref var sprite = ref recorded.View.Sprites.Add();
        sprite.Image = SpriteImageKey.Create(logicalFile, section, frame);
        sprite.X = subX + recorded.OffsetX;
        sprite.Y = subY + recorded.OffsetY;
        sprite.Angle = angle;
        sprite.Scale = scale / 256f;
        sprite.Flip = (SpriteFlip)flip;
        sprite.ObjectSlot = (short)slot;
        recorded.BufferPositions.Add((unchecked((short)x), unchecked((short)y)));
        return true;
    }

    /// <summary>Decodes a shape frame into the image cache on first use (255 = transparent, origin = left/top extents).</summary>
    private bool TryCacheSpriteImage(SpriteImageKey key, ShapeTable shape, int frame, ShapeExtents extents)
    {
        if (_spriteImages.Contains(key))
            return true;
        int width = extents.Width;
        int height = extents.Height;
        var pixels = new byte[width * height];
        pixels.AsSpan().Fill(SpriteImage.TransparentIndex);
        try
        {
            ShapeFrameDecoder.DecodeShapeFrame(shape, frame, pixels, width, height, extents.Left, extents.Top);
        }
        catch (GameDataException)
        {
            return false;
        }
        _spriteImages.Set(key, new SpriteImage(width, height, extents.Left, extents.Top, pixels));
        return true;
    }

    /// <summary>The recorded sprites belong to the next present.</summary>
    /// <remarks>C: SdlCompleteSpaceFrame, at the start of dump_buffer_to_screen.</remarks>
    private void CompleteSpaceSpriteFrame()
    {
        if (_spaceSpriteState == SpaceSpriteState.Recording)
            _spaceSpriteState = SpaceSpriteState.Complete;
    }

    /// <summary>
    /// Drops a frame that was recorded but not presented (flight exit, nav map, end of a sequence).
    /// The published frame stays until the classic frame changes, so the last flight frame keeps its
    /// sprites while it is still on screen.
    /// </summary>
    /// <remarks>C: SdlCancelSpaceFrame (the reference also drops the shown sprites at once).</remarks>
    private void CancelSpaceSpriteFrame()
    {
        _pendingSpaceFrame?.Clear();
        _spaceSpriteState = SpaceSpriteState.Idle;
        _softwareForRestOfFrame = false;
    }

    /// <summary>The next frame starts on the normal space colour again.</summary>
    private void ResetSpaceBufferBackground() => SpaceBufferBackground = PaletteColours.PrimaryViewBuffer;

    private void EnsurePresentHook()
    {
        if (_presentHookInstalled)
            return;
        _presentHookInstalled = true;
        var display = Display;
        var previous = display.Presented;
        display.Presented = () =>
        {
            previous?.Invoke();
            OnClassicFramePresented();
        };
    }

    /// <summary>
    /// After every present: a completed frame is published (<c>RenderFrame.Space</c>) together with
    /// a copy of the cursor-free classic frame; re-presents of that same frame (cursor moves,
    /// palette changes) keep the sprites, any other frame removes them.
    /// </summary>
    /// <remarks>C: SdlGlRendererPresent (sdl/gl_renderer.c): frame state COMPLETE → snapshot, IDLE with a
    /// snapshot → compare.</remarks>
    private void OnClassicFramePresented()
    {
        var renderFrame = Game.Runtime.Frame;
        var working = Display.Working.Pixels;
        if (_spaceSpriteState == SpaceSpriteState.Complete && _pendingSpaceFrame is { } pending)
        {
            (_pendingSpaceFrame, _publishedSpaceFrame) = (_publishedSpaceFrame, pending);
            UpdateWindowMask(pending.MaskGeometry, pending.MaskFull);
            working.AsSpan().CopyTo(_publishedBase);
            renderFrame.Space = pending.View;
            _spaceSpriteState = SpaceSpriteState.Idle;
            PublishedSpaceFrames++;
            return;
        }
        if (renderFrame.Space is { } shown && ReferenceEquals(shown, _publishedSpaceFrame?.View) &&
            !working.AsSpan().SequenceEqual(_publishedBase))
        {
            renderFrame.Space = null;
        }
    }

    /// <summary>The window mask from the geometry runs (byte runs that may span rows), or the whole screen.</summary>
    /// <remarks>C: BuildSpaceViewMask (sdl/gl_renderer.c).</remarks>
    private void UpdateWindowMask(ViewGeometry? geometry, bool full)
    {
        if (_windowMaskValid && ReferenceEquals(geometry, _windowMaskGeometry) && full == _windowMaskFull)
            return;
        _windowMaskValid = true;
        _windowMaskGeometry = geometry;
        _windowMaskFull = full;
        if (full || geometry is null)
        {
            _windowMask.SetAll(true);
            return;
        }
        _windowMask.SetAll(false);
        foreach (var run in geometry.Runs)
            _windowMask.SetRun(run.DestinationX, run.ScreenY, run.Length);
    }

    /// <summary>
    /// Before a flight-owned modal panel covers a frame whose sprites the renderer draws: the CPU
    /// draws them into the classic frame, so the paused picture keeps its objects (at classic
    /// resolution) once the panel replaces the frame.
    /// </summary>
    private void BakeShownSpaceSprites()
    {
        var renderFrame = Game.Runtime.Frame;
        if (_publishedSpaceFrame is not { } shown || !ReferenceEquals(renderFrame.Space, shown.View))
            return;
        var working = Display.Working.Pixels;
        if (!working.AsSpan().SequenceEqual(_publishedBase))
            return;
        ComposeSpaceSprites(shown, working);
    }

    /// <summary>
    /// Draws the sprites of a recorded frame with the CPU into a classic frame, the way the CPU
    /// path would have: at the integer positions, in painter order, clipped to the space buffer,
    /// only inside the window and only over pixels that show the frame's background colour.
    /// </summary>
    internal void ComposeSpaceSprites(SpaceSpriteFrame frame, Span<byte> target)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (target.Length < Framebuffer.PixelCount)
            throw new ArgumentException("A 320x200 frame is needed.", nameof(target));
        int width = frame.BufferWidth;
        int height = frame.BufferHeight;
        if (width <= 0 || height <= 0 || frame.View.Sprites.Count == 0)
            return;
        // Two scratch buffers on different clear colours: a pixel some sprite covers holds that
        // sprite's colour in both, an uncovered one keeps 0 and 255.
        var low = Viewport.Allocate(0, 0, width - 1, height - 1, 0x00);
        var high = Viewport.Allocate(0, 0, width - 1, height - 1, 0xFF);
        var sprites = frame.View.Sprites.Items;
        for (int i = 0; i < sprites.Length; i++)
        {
            ref readonly var sprite = ref sprites[i];
            var shape = Shapes.Get(sprite.Image.LogicalFile, sprite.Image.Section);
            var (x, y) = frame.BufferPositions[i];
            int scale = (int)MathF.Round(sprite.Scale * 256f);
            Gfx.DrawSpriteScaled(low, x, y, shape, sprite.Image.Frame, (int)sprite.Angle, scale, (int)sprite.Flip);
            Gfx.DrawSpriteScaled(high, x, y, shape, sprite.Image.Frame, (int)sprite.Angle, scale, (int)sprite.Flip);
        }
        var lowPixels = low.Surface!.Pixels;
        var highPixels = high.Surface!.Pixels;
        var mask = frame.View.WindowMask;
        byte background = frame.View.BackgroundIndex;
        for (int row = 0; row < height; row++)
        {
            int screenY = row + frame.OffsetY;
            if ((uint)screenY >= Framebuffer.Height)
                continue;
            for (int column = 0; column < width; column++)
            {
                int k = row * width + column;
                if (lowPixels[k] != highPixels[k])
                    continue;
                int screenX = column + frame.OffsetX;
                if ((uint)screenX >= Framebuffer.Width || (mask is not null && !mask[screenX, screenY]))
                    continue;
                int s = screenY * Framebuffer.Width + screenX;
                if (target[s] == background)
                    target[s] = lowPixels[k];
            }
        }
    }

    /// <summary>
    /// The sub-pixel screen position of an object (absolute in the space buffer): the float
    /// projection when it agrees with the integer one; attachments (engine flames, turrets) keep
    /// their offset to the parent's sub-pixel position.
    /// </summary>
    /// <remarks>C: the enhancedScreenX/Y block of draw_sorted_objects_to_buffer (eventmgr.c, SDL_PORT). The
    /// SDL port anchors only engine flames (SdlGetThrusterScreenPosition, from its own placement code); the
    /// port derives every attachment from its parent.</remarks>
    private (float X, float Y) SubPixelScreenPosition(int obj)
    {
        var sim = Sim;
        ref readonly var o = ref sim.Objects[obj];
        float x = unchecked((short)(o.ScreenX + sim.ViewCenterX));
        float y = unchecked((short)(o.ScreenY + sim.ViewCenterY));
        if (o.Class == ObjectClass.FixedObject)
        {
            int parent = o.Owner;
            if ((uint)parent < ObjectSlots.Count && parent != obj &&
                sim.Objects[parent].Class is not (ObjectClass.Null or ObjectClass.FixedObject))
            {
                ref readonly var p = ref sim.Objects[parent];
                var (parentX, parentY) = ProjectedSubPixel(parent);
                x += parentX - unchecked((short)(p.ScreenX + sim.ViewCenterX));
                y += parentY - unchecked((short)(p.ScreenY + sim.ViewCenterY));
            }
            return (x, y);
        }
        if (o.Class == ObjectClass.Null || obj == sim.NavPointerObject)
            return (x, y);
        return ProjectedSubPixel(obj);
    }

    private (float X, float Y) ProjectedSubPixel(int obj)
    {
        var sim = Sim;
        ref readonly var o = ref sim.Objects[obj];
        float x = unchecked((short)(o.ScreenX + sim.ViewCenterX));
        float y = unchecked((short)(o.ScreenY + sim.ViewCenterY));
        var view = o.ViewPosition;
        if (view.Z == 0 || obj == sim.NavPointerObject)
            return (x, y);
        int width = (short)(sim.ScreenWidth & ~1);
        short projectedX = unchecked((short)(FixedMath.Divide(FixedMath.Multiply(width << 7, view.X), view.Z) >> 8));
        short projectedY = unchecked((short)(FixedMath.Divide(FixedMath.Multiply(width << 7, view.Y), view.Z) >> 8));
        if (projectedX != o.ScreenX || projectedY != o.ScreenY)
            return (x, y);
        return ((float)sim.ViewCenterX + (float)(width * 0.5 * view.X / view.Z),
            (float)sim.ViewCenterY + (float)(width * 0.5 * view.Y / view.Z));
    }
}
