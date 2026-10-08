using WingCommander.Core.Rendering;
using WingCommander.Core.Video;

namespace WingCommander.Render.Vulkan.Tests;

/// <summary>The Core.Rendering space contract (plain data, no GPU).</summary>
public sealed class SpaceContractTests
{
    [Fact]
    public void WindowMask_RunsContinueIntoFollowingRows_AndBumpTheVersion()
    {
        var mask = new SpaceViewMask();
        Assert.False(mask[0, 0]);
        int version = mask.Version;

        mask.SetRun(300, 10, 40); // 20 pixels of row 10, 20 of row 11 (linear copy runs)
        Assert.True(mask[319, 10]);
        Assert.True(mask[0, 11]);
        Assert.True(mask[19, 11]);
        Assert.False(mask[20, 11]);
        Assert.False(mask[299, 10]);
        Assert.True(mask.Version > version);

        mask.SetRun(0, 199, 1000); // clipped at the end of the screen
        Assert.True(mask[319, 199]);
        mask.SetRect(new ScreenRect(-5, -5, 10, 10), true); // clipped at the top left
        Assert.True(mask[4, 4]);
        Assert.False(mask[5, 5]);
        mask.SetAll(false);
        Assert.Equal(0, mask.Mask.IndexOfAnyExcept((byte)0) + 1);
    }

    [Fact]
    public void SpriteDrawList_GrowsAndReusesItsStorage()
    {
        var list = new SpriteDrawList(capacity: 4);
        for (int i = 0; i < 10; i++)
        {
            ref SpriteInstance sprite = ref list.Add();
            Assert.Equal(1f, sprite.Scale);
            Assert.Equal(-1, sprite.ObjectSlot);
            sprite.X = i;
        }
        Assert.Equal(10, list.Count);
        Assert.Equal(9f, list.Items[9].X);
        list.Clear();
        Assert.Equal(0, list.Count);
        list.Add(new SpriteInstance(SpriteImageKey.Create(1, 2, 3), 5, 6));
        Assert.Equal(new SpriteImageKey(1, 2, 3), list.Items[0].Image);
        Assert.Equal(ScreenRect.Full, list.Clip);
    }

    [Fact]
    public void SpriteImageCache_GenerationChangesOnlyWhenImagesDisappearOrChange()
    {
        var cache = new SpriteImageCache();
        var image = new SpriteImage(2, 2, 0, 0, new byte[] { 1, 2, 3, 255 });
        int generation = cache.Generation;
        cache.Set(new SpriteImageKey(1, 0, 0), image);
        cache.Set(new SpriteImageKey(1, 0, 1), image);
        cache.Set(new SpriteImageKey(1, 0, 0), image); // same image again
        Assert.Equal(generation, cache.Generation);

        cache.Set(new SpriteImageKey(1, 0, 0), new SpriteImage(1, 1, 0, 0, new byte[] { 9 }));
        Assert.Equal(generation + 1, cache.Generation);
        cache.Clear();
        Assert.Equal(generation + 2, cache.Generation);
        Assert.Equal(0, cache.Count);
        Assert.Throws<ArgumentException>(() => new SpriteImage(3, 3, 0, 0, new byte[8]));
    }

    [Fact]
    public void SpaceViewState_CopyFromKeepsThePreviousTick()
    {
        var current = new SpaceViewState(capacity: 2);
        current.SpaceFrame = 7;
        current.Camera.FocalLength = 160;
        for (short slot = 0; slot < 5; slot++)
        {
            ref SpaceObjectState o = ref current.Add();
            o.Slot = (short)(slot * 3);
            o.Distance = 100 - slot;
            current.AddToDrawOrder(slot);
        }
        var previous = new SpaceViewState(capacity: 1);
        previous.CopyFrom(current);
        current.Clear();

        Assert.Equal(7, previous.SpaceFrame);
        Assert.Equal(160f, previous.Camera.FocalLength);
        Assert.Equal(5, previous.Count);
        Assert.Equal(new short[] { 0, 1, 2, 3, 4 }, previous.DrawOrder.ToArray());
        Assert.Equal(2, previous.IndexOfSlot(6));
        Assert.Equal(-1, previous.IndexOfSlot(5));
        Assert.Equal(-1, previous.Objects[0].Owner);
        Assert.Throws<ArgumentOutOfRangeException>(() => current.AddToDrawOrder(0));
    }

    [Fact]
    public void RenderFrame_HasNoSpaceViewUnlessTheGameSetsOne()
    {
        var frame = new RenderFrame(new ClassicLayer(new Framebuffer(), new Palette()));
        Assert.Null(frame.Space);
        var view = new SpaceView(new SpriteImageCache());
        Assert.Equal(0xBF, view.BackgroundIndex);
        Assert.Null(view.WindowMask);
        Assert.Equal(new ScreenRect(0, 0, 320, 200), ScreenRect.Full);
    }
}
