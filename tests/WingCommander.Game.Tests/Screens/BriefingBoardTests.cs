using WingCommander.Core.Rendering;
using WingCommander.Game.Config;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens;

/// <summary>
/// Reflight's briefing board: with output-resolution text the wall screen behind the Colonel
/// shows this mission's nav map, its text drawn sharp at the board's size; otherwise the art.
/// </summary>
public sealed class BriefingBoardTests
{
    /// <summary>New game through the placeholder simulator and the rooms into the first briefing's podium shot.</summary>
    private static ScreenRig RunToPodium(bool highResolutionText, bool? sharpText = null)
    {
        var rig = new ScreenRig(options: new Wc1GameOptions
        {
            Audio = false,
            SkipIntro = true,
            HighResolutionText = highResolutionText,
            ReplacementFonts = BundledFonts.Load(),
            SharpTextOverride = sharpText,
        });
        rig.Key(1_000, 0x1f, 0x53);   // title: new game
        rig.Key(5_000, 0x39, 0x20);   // placeholder simulator
        rig.Key(11_500, 0x1c, 0x0d);  // name
        rig.Key(12_500, 0x1c, 0x0d);  // callsign
        rig.Key(14_000, 0x39, 0x20);  // ranking
        rig.Click(16_500, 300, 100);  // barracks
        rig.Click(18_500, 300, 60);   // hangar: briefing
        rig.Game.Start();
        Assert.False(rig.Runtime.RunHeadless(26_000));
        Assert.Null(rig.Runtime.Failure);
        return rig;
    }

    private static string ScaledText(TextLayer text, int font) =>
        string.Concat(text.Instances.ToArray().Where(i => i.ScaleX < 0.9f && i.Glyph.Font == font).Select(i => (char)i.Glyph.Character));

    [DataFact]
    public void The_board_shows_the_missions_map_with_sharp_text()
    {
        var rig = RunToPodium(highResolutionText: true);
        TextLayer text = rig.Game.Display.Text!;
        string readout = ScaledText(text, 1);
        Assert.Contains("BriefingNavMap", readout);
        Assert.Contains("Sector:VegaXR-231.3", readout);
        Assert.Contains("System:Enyo", readout);
        Assert.Contains("Tiger'sClaw", ScaledText(text, 2)); // map labels, an "i" the shrinking skips included
        foreach (var instance in text.Instances.ToArray().Where(i => i.ScaleX < 0.9f))
        {
            Assert.Equal(154f / 260, instance.ScaleX, 4);
            Assert.Equal(88f / 156, instance.ScaleY, 4);
        }
    }

    [DataFact]
    public void Without_sharp_text_the_board_keeps_the_art()
    {
        byte[] classic = (byte[])RunToPodium(highResolutionText: false).Front.Pixels.Clone();
        var off = RunToPodium(highResolutionText: true, sharpText: false);
        Assert.Equal(classic, off.Front.Pixels);
        Assert.DoesNotContain(off.Game.Display.Text!.Instances.ToArray(), i => i.ScaleX < 0.9f);

        var on = RunToPodium(highResolutionText: true);
        int changed = 0;
        for (int y = 11; y < 117; y++)
        {
            for (int x = 0; x < 212; x++)
            {
                if (classic[y * 320 + x] != on.Front.Pixels[y * 320 + x])
                    changed++;
            }
        }
        Assert.True(changed > 500, $"{changed} board pixels changed"); // the mission's map replaces the art
    }
}
