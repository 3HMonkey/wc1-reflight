using WingCommander.Core.Platform;
using WingCommander.Game.Screens.Ui;
using WingCommander.Game.Tests.Screens;
using WingCommander.Graphics.Palettes;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Video;

/// <summary>The cursor follows the pointer between presents (host loop hook, port addition).</summary>
public sealed class CursorRefreshTests
{
    private static int ChangedPixels(byte[] a, byte[] b, int left, int top, int size)
    {
        int changed = 0;
        for (int y = top; y < Math.Min(200, top + size); y++)
        {
            for (int x = left; x < Math.Min(320, left + size); x++)
            {
                if (a[y * 320 + x] != b[y * 320 + x])
                    changed++;
            }
        }
        return changed;
    }

    [DataFact]
    public void The_cursor_moves_to_the_pointer_without_a_present()
    {
        var rig = new ScreenRig();
        rig.Start(async game =>
        {
            await game.Display.ClearViewportAsync(game.Graphics.Screen!, PaletteColours.DarkGrey);
            await game.ShowPauseMenuAsync(PauseMenuContext.Menu);
        });
        rig.Runtime.RunHeadless(500);
        var events = rig.Game.Events;
        int oldX = events.Cursor.X, oldY = events.Cursor.Y;
        byte[] before = (byte[])rig.Front.Pixels.Clone();
        long presents = rig.Game.Display.SlamCount;

        events.EnqueueHostEvent(new HostInputEvent(HostInputKind.MouseMove, 0, 0, 20, 160, 0, HostModifiers.None, false));
        rig.Runtime.AfterUpdate!.Invoke();
        byte[] after = (byte[])rig.Front.Pixels.Clone();

        Assert.Equal(presents, rig.Game.Display.SlamCount);         // no game present
        Assert.True(ChangedPixels(before, after, 20, 160, 12) > 10); // the arrow at the pointer
        Assert.True(ChangedPixels(before, after, oldX, oldY, 12) > 10); // and gone from the old place
        Assert.Equal(oldX, events.Cursor.X);                          // the game has not read the move yet

        // The game reads the move and presents with its own cursor at the same place.
        rig.Runtime.RunHeadless(1_000);
        Assert.Equal(20, events.Cursor.X);
        Assert.Equal(0, ChangedPixels(after, rig.Front.Pixels, 20, 160, 12));
    }

    [DataFact]
    public void Nothing_changes_while_the_cursor_is_hidden()
    {
        var rig = new ScreenRig();
        rig.Start(async game => await game.Display.ClearViewportAsync(game.Graphics.Screen!, PaletteColours.DarkGrey));
        rig.Runtime.RunHeadless(300);
        rig.Game.Events.CursorShowCount = 0;
        byte[] before = (byte[])rig.Front.Pixels.Clone();
        rig.Game.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.MouseMove, 0, 0, 100, 100, 0, HostModifiers.None, false));
        rig.Runtime.AfterUpdate!.Invoke();
        Assert.Equal(before, rig.Front.Pixels);
    }
}
