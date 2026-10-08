using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Graphics.Cursor;

/// <summary>
/// Software mouse cursor primitives: save the pixels under the cursor frame, draw the frame,
/// put the pixels back, and track the damaged screen rectangle (the cursor position ±16). The
/// event manager (Game) owns the cursor state (position, shape, show count) and drives these
/// around every presented frame, so the game's own frame never contains the cursor.
/// </summary>
/// <remarks>C: abCursorSaveArea[0x1000], nMouseCursorDrawnX/Y, bMouseCursorDrawn,
/// nMouseCursorDamage*, bMouseCursorDamagePending (eventmgr.c).</remarks>
public sealed class MouseCursorCompositor
{
    /// <summary>Size of the background save area.</summary>
    public const int SaveAreaSize = 0x1000;

    /// <summary>Half size of the damage square around the cursor position.</summary>
    public const int DamageMargin = 16;

    private readonly byte[] _saveArea = new byte[SaveAreaSize];
    private readonly GraphicsContext _graphics;

    public MouseCursorCompositor(GraphicsContext graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        _graphics = graphics;
        ResetDamage();
    }

    /// <summary>The saved background bytes.</summary>
    public ReadOnlySpan<byte> SaveArea => _saveArea;

    /// <summary>Position the background was captured at.</summary>
    public int DrawnX { get; private set; }

    /// <summary>Position the background was captured at.</summary>
    public int DrawnY { get; private set; }

    /// <summary>True between a capture and the matching restore.</summary>
    public bool IsDrawn { get; private set; }

    public int DamageLeft { get; private set; }

    public int DamageTop { get; private set; }

    public int DamageRight { get; private set; }

    public int DamageBottom { get; private set; }

    /// <summary>Set when the damage rectangle grew since the last reset.</summary>
    public bool DamagePending { get; private set; }

    /// <summary>Empties the damage rectangle (left 319, top 199, right 0, bottom 0) and the drawn flag.</summary>
    /// <remarks>C: the reset at the start of RefreshMouseCursorDisplay (0x436060, eventmgr.c).</remarks>
    public void ResetDamage()
    {
        DamageLeft = 319;
        DamageTop = 199;
        DamageRight = 0;
        DamageBottom = 0;
        DamagePending = false;
        IsDrawn = false;
    }

    /// <summary>Saves the pixels under the cursor frame at (x, y) and remembers the position.</summary>
    /// <remarks>C: CaptureMouseCursorBackground (0x435E20, eventmgr.c) without the show-count and
    /// null checks, which stay with the caller.</remarks>
    public void CaptureBackground(Viewport viewport, int x, int y, ShapeTable shape, int frame)
    {
        _graphics.CaptureSpriteBackground(viewport, _saveArea, x, y, shape, frame);
        AddDamage(x, y);
        DrawnX = x;
        DrawnY = y;
        IsDrawn = true;
    }

    /// <summary>Draws the cursor frame with its hot spot at (x, y).</summary>
    /// <remarks>C: DrawMouseCursor (0x435EF0, eventmgr.c).</remarks>
    public void DrawCursor(Viewport viewport, int x, int y, ShapeTable shape, int frame)
    {
        _graphics.DrawSpriteDefault(viewport, x, y, shape, frame);
        AddDamage(x, y);
    }

    /// <summary>Restores the pixels saved by the last <see cref="CaptureBackground"/> (no-op when not drawn).</summary>
    /// <remarks>C: RestoreMouseCursorBackground (0x435FA0, eventmgr.c).</remarks>
    public void RestoreBackground(Viewport viewport, ShapeTable shape, int frame)
    {
        if (!IsDrawn)
            return;
        _graphics.RestoreSpriteBackground(viewport, _saveArea, DrawnX, DrawnY, shape, frame);
        AddDamage(DrawnX, DrawnY);
        IsDrawn = false;
    }

    /// <summary>Restores the saved pixels at an explicit position (used when the shape changes).</summary>
    /// <remarks>C: the restore inside SetMouseCursorShape (0x4360F0, eventmgr.c).</remarks>
    public void RestoreBackgroundAt(Viewport viewport, int x, int y, ShapeTable shape, int frame) =>
        _graphics.RestoreSpriteBackground(viewport, _saveArea, x, y, shape, frame);

    private void AddDamage(int x, int y)
    {
        if (DamageLeft > x - DamageMargin)
            DamageLeft = x - DamageMargin;
        if (DamageRight < x + DamageMargin)
            DamageRight = x + DamageMargin;
        if (DamageTop > y - DamageMargin)
            DamageTop = y - DamageMargin;
        if (DamageBottom < y + DamageMargin)
            DamageBottom = y + DamageMargin;
        DamagePending = true;
    }
}
