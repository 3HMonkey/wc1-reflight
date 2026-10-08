using WingCommander.Core.Video;
using WingCommander.Graphics.Cursor;
using WingCommander.Graphics.Shapes;
using WingCommander.Tests;

namespace WingCommander.Graphics.Tests.Cursor;

public class MouseCursorTests
{
    [DataFact]
    public void Cursor_is_composited_and_removed_without_trace()
    {
        var framebuffer = new Framebuffer();
        new Random(9).NextBytes(framebuffer.Pixels);
        byte[] frame = framebuffer.Pixels.ToArray();
        var gfx = new GraphicsContext(framebuffer, new Palette(), null);
        ShapeTable arrow = ShapeTable.FromSection(GameData.Require().OpenPacket("ARROW.VGA"), 0);
        var cursor = new MouseCursorCompositor(gfx);

        foreach (int frameIndex in new[] { 0, 1, 2 })
        {
            foreach ((int x, int y) in new[] { (160, 100), (0, 0), (319, 199), (-3, 50) })
            {
                cursor.ResetDamage();
                cursor.CaptureBackground(gfx.Screen!, x, y, arrow, frameIndex);
                cursor.DrawCursor(gfx.Screen!, x, y, arrow, frameIndex);
                Assert.True(cursor.IsDrawn);
                cursor.RestoreBackground(gfx.Screen!, arrow, frameIndex);
                Assert.False(cursor.IsDrawn);
                Assert.Equal(frame, framebuffer.Pixels);
                Assert.Equal((x - 16, y - 16, x + 16, y + 16), (cursor.DamageLeft, cursor.DamageTop, cursor.DamageRight, cursor.DamageBottom));
                Assert.True(cursor.DamagePending);
            }
        }
    }

    [DataFact]
    public void Restore_without_capture_does_nothing()
    {
        var framebuffer = new Framebuffer();
        var gfx = new GraphicsContext(framebuffer, new Palette(), null);
        ShapeTable arrow = ShapeTable.FromSection(GameData.Require().OpenPacket("ARROW.VGA"), 0);
        var cursor = new MouseCursorCompositor(gfx);
        cursor.DrawCursor(gfx.Screen!, 50, 50, arrow, 0);
        byte[] drawn = framebuffer.Pixels.ToArray();
        cursor.RestoreBackground(gfx.Screen!, arrow, 0);
        Assert.Equal(drawn, framebuffer.Pixels);
    }
}
