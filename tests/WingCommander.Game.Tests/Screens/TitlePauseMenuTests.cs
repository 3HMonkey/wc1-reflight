using WingCommander.Graphics.Palettes;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens;

public sealed class TitlePauseMenuTests
{
    [DataFact]
    public void EscOnTheTitleMenu_OpensThePauseMenu()
    {
        var rig = new ScreenRig();
        rig.Start(async game => await game.Title.RunAsync());
        rig.Runtime.RunHeadless(5800);
        int before = rig.Front.Pixels.Count(p => p == PaletteColours.Blue);
        rig.Key(6000, 0x01);
        rig.Runtime.RunHeadless(6800);
        int during = rig.Front.Pixels.Count(p => p == PaletteColours.Blue);
        Assert.True(during > before + 200, $"blue pixels {before} -> {during}");
    }

    [DataFact]
    public void EscOnTheTitle_InGameMain_WithKeysQueuedBeforeTheStart()
    {
        var rig = new ScreenRig(options: new Wc1GameOptions { Audio = false, SkipIntro = true });
        rig.Key(6000, 0x01);
        rig.Game.Start();
        rig.Runtime.RunHeadless(5800);
        int before = rig.Front.Pixels.Count(p => p == PaletteColours.Blue);
        rig.Runtime.RunHeadless(6800);
        int during = rig.Front.Pixels.Count(p => p == PaletteColours.Blue);
        Assert.True(during > before + 200, $"blue pixels {before} -> {during}");
    }
}
