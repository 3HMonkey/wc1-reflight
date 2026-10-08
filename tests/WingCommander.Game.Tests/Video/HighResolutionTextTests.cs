using WingCommander.Core.Platform;
using WingCommander.Core.Rendering;
using WingCommander.Game.Config;
using WingCommander.Game.Tests.Screens;
using WingCommander.Graphics.Text;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Video;

public class HighResolutionTextTests
{
    [DataFact]
    public void TextOnTheScreen_IsPublishedInTheRenderFrame()
    {
        var rig = new ScreenRig(highResolutionText: true);
        rig.Start(async game =>
        {
            game.Graphics.DrawTextAt(game.DefaultText, 20, 30, "HELLO", 0);
            await game.Display.PresentAsync();
        });
        rig.Runtime.RunHeadless(500);
        TextLayer? text = rig.Runtime.Frame.Text;
        Assert.NotNull(text);
        Assert.Same(rig.Game.Display.Text, text);
        Assert.Equal(5, text.Count);
        Assert.Equal((short)20, text.Instances[0].X);
        Assert.Equal((short)30, text.Instances[0].Y);
        Assert.True(text.Glyphs.TryGet(text.Instances[0].Glyph, out _));
        Assert.NotNull(rig.Runtime.Frame.KeyHelp);
    }

    [DataFact]
    public void Cursor_StaysOnTopOfText()
    {
        var rig = new ScreenRig(highResolutionText: true);
        rig.Start(async game =>
        {
            game.Graphics.DrawTextAt(game.DefaultText, 100, 100, "MMMMMMMMMMMMMMMM", 0);
            game.Events.Cursor.X = 110;
            game.Events.Cursor.Y = 102;
            game.Events.ShowCursor();
            await game.Display.PresentAsync();
        });
        rig.Runtime.RunHeadless(500);
        TextLayer text = rig.Runtime.Frame.Text!;
        byte[] front = rig.Front.Pixels;
        byte[] working = rig.Game.Display.Working.Pixels;
        int covered = 0;
        for (int p = 0; p < front.Length; p++)
        {
            if (front[p] == working[p])
                continue; // not a cursor pixel
            covered++;
            Assert.Equal(0, text.Mask[p]);
            Assert.Equal(front[p], text.Pixels.Pixels[p]);
        }
        Assert.True(covered > 0, "the cursor was not drawn over the text");
    }

    [DataFact]
    public void F10_TogglesTheKeyHelp_AndTheGameNeverSeesTheKey()
    {
        var rig = new ScreenRig(highResolutionText: true);
        bool sawF10 = false;
        rig.Start(async game =>
        {
            game.ShowKeyHelp("TEST", [new KeyHelpSection("KEYS", [new KeyHelpEntry("A", "Something")])]);
            var e = new WingCommander.Game.Input.InputEventState();
            for (int i = 0; i < 200; i++)
            {
                short type = game.Events.PollInputEvent(ref e);
                if (type != 0 && e.Value == 0x44)
                    sawF10 = true;
                await game.Display.PresentAsync();
            }
        });
        rig.Runtime.RunHeadless(100);
        Assert.True(rig.Game.KeyHelp!.Visible);
        rig.Key(200, 0x44);
        rig.Runtime.RunHeadless(1000);
        Assert.False(rig.Game.KeyHelp.Visible);
        Assert.False(rig.Game.Settings.KeyHelp);
        Assert.False(sawF10);
        rig.Game.HideKeyHelp();
        Assert.False(rig.Game.KeyHelp.Visible);
    }

    [Fact]
    public void KeyHelpSetting_IsOnlyWrittenWhenOff()
    {
        var settings = GameSettings.FromText("MusicVolume=7\nSFXVolume=12\nCheater=0\n");
        Assert.True(settings.KeyHelp);
        settings.KeyHelp = false;
        Assert.Equal("MusicVolume=7\nSFXVolume=12\nCheater=0\nKeyHelp=0\n", settings.ToText());
        Assert.False(GameSettings.FromText(settings.ToText()).KeyHelp);
    }

    [Fact]
    public void BundledFonts_AreCompiledIn_AndParse()
    {
        var fonts = BundledFonts.Load();
        Assert.Equal([0, 1, 2, 3], fonts.Keys.Order());
        Assert.Same(fonts[1], fonts[2]);
        foreach (var font in fonts.Values)
            Assert.True(Core.Fonts.TrueTypeFont.Load(font.Data.Span).GetGlyphForCodePoint('A') is { IsEmpty: false });
        Assert.Equal(File.ReadAllBytes(RepositoryAssets.Chawp), fonts[3].Data.ToArray());
    }
}
