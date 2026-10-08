using WingCommander.Game.Input;
using WingCommander.Graphics;
using WingCommander.Graphics.Cursor;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Video;

/// <summary>Live pointer bounds backed by a Graphics viewport (its rectangle can change at runtime).</summary>
public sealed class ViewportPointerBounds(Viewport viewport) : IPointerBounds
{
    public Viewport Viewport { get; } = viewport;

    public short Left => Viewport.Left;

    public short Top => Viewport.Top;

    public short Right => Viewport.Right;

    public short Bottom => Viewport.Bottom;
}

/// <summary>
/// The game's software mouse cursor: shape/frame selection, the viewport it is clamped to and
/// drawn into, and the composite around every present (capture, draw, present, restore).
/// Position and show count live in <see cref="EventManager"/>.
/// </summary>
/// <remarks>C: stMouseCursorState.shape/frame/viewport, SetMouseCursorShape (0x4360F0),
/// CaptureMouseCursorBackground (0x435E20), DrawMouseCursor (0x435EF0),
/// RestoreMouseCursorBackground (0x435FA0), eventmgr.c.</remarks>
public sealed class SoftwareCursor : ISoftwareCursor
{
    private readonly GraphicsContext _graphics;
    private readonly EventManager _events;
    private readonly MouseCursorCompositor _compositor;
    private Viewport? _viewport;

    public SoftwareCursor(GraphicsContext graphics, EventManager events)
    {
        _graphics = graphics;
        _events = events;
        _compositor = new MouseCursorCompositor(graphics);
    }

    /// <summary>Cursor sprite set (ARROW.VGA by default).</summary>
    public ShapeTable? Shape { get; private set; }

    /// <summary>
    /// The viewport the cursor is clamped to and drawn into (stMouseCursorState.viewport). Setting
    /// it also makes it the event manager's pointer bounds.
    /// </summary>
    public Viewport? Viewport
    {
        get => _viewport;
        set
        {
            _viewport = value;
            if (value is not null)
                _events.Cursor.Bounds = new ViewportPointerBounds(value);
        }
    }

    public bool IsOnScreen =>
        _events.CursorShowCount != 0 && _viewport is not null && Shape is not null && _graphics.IsScreenSurface(_viewport);

    /// <summary>Selects the cursor sprite and frame.</summary>
    /// <remarks>C: SetMouseCursorShape (0x4360F0). Its background restore is guarded by
    /// pDrawnMouseCursorShape, which nothing ever sets, so only the state change is ported.</remarks>
    public void SetShape(ShapeTable? shape, short frame)
    {
        _events.Cursor.ShapeChanged = true;
        _events.Cursor.Frame = frame;
        Shape = shape;
    }

    /// <summary>Changes the frame of the current shape.</summary>
    public void SetFrame(short frame) => SetShape(Shape, frame);

    public void CaptureAndDraw()
    {
        if (Shape is not { } shape || _viewport is not { } viewport)
            return;
        _compositor.CaptureBackground(viewport, _events.Cursor.X, _events.Cursor.Y, shape, _events.Cursor.Frame);
        _compositor.DrawCursor(viewport, _events.Cursor.X, _events.Cursor.Y, shape, _events.Cursor.Frame);
    }

    public void DrawInto(IndexedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (Shape is not { } shape || _viewport is not { } viewport)
            return;
        Viewport target = viewport.Clone();
        target.Surface = surface;
        _graphics.DrawSpriteDefault(target, _events.Cursor.X, _events.Cursor.Y, shape, _events.Cursor.Frame);
    }

    public void Restore()
    {
        if (_events.CursorShowCount != 0 && Shape is { } shape && _viewport is { } viewport)
            _compositor.RestoreBackground(viewport, shape, _events.Cursor.Frame);
    }
}
