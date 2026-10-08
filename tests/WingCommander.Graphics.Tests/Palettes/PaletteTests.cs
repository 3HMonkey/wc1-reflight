using WingCommander.Core.Video;
using WingCommander.Graphics.Palettes;
using WingCommander.Tests;

namespace WingCommander.Graphics.Tests.Palettes;

public class PaletteTests
{
    [DataFact]
    public void Game_pal_is_read_from_offset_0x30_as_8_bit_triplets()
    {
        byte[] file = GameData.Require().ReadFile(GamePaletteFile.FileName);
        Assert.Equal(1120, file.Length);
        Assert.Equal("FORM", System.Text.Encoding.ASCII.GetString(file, 0, 4));
        Assert.Equal("CMAP", System.Text.Encoding.ASCII.GetString(file, 0x28, 4));
        var palette = new GamePalette(new Palette());
        palette.LoadPaletteTripletsFile(file);
        Assert.Equal(((byte)0, (byte)0, (byte)0), palette.GetPaletteEntry(0));
        Assert.Equal(((byte)255, (byte)255, (byte)255), palette.GetPaletteEntry(15));
        Assert.Equal(((byte)0x83, (byte)0xE3, (byte)0), palette.GetPaletteEntry(166));
        Assert.Equal(((byte)0xBF, (byte)0, (byte)0xBF), palette.GetPaletteEntry(185));
        Assert.Equal(((byte)0, (byte)0, (byte)0), palette.GetPaletteEntry(191));
        Assert.Equal(((byte)0xEB, (byte)0xD7, (byte)0xFF), palette.GetPaletteEntry(255));
        // whole-palette writes do not touch the saved mirror
        Assert.All(palette.Saved.ToArray(), b => Assert.Equal(0, b));
    }

    [DataFact]
    public void Startup_palette_overrides_the_cockpit_entries_and_saves()
    {
        var gfx = new GraphicsContext();
        gfx.LoadGamePaletteFile(GameData.Require().ReadFile(GamePaletteFile.FileName));
        for (int i = 185; i <= 190; i++)
            Assert.Equal(((byte)0, (byte)0, (byte)0), gfx.Palette.GetPaletteEntry(i));
        Assert.Equal(((byte)0, (byte)0, (byte)32), gfx.Palette.GetPaletteEntry(PaletteColours.PrimaryViewBuffer));
        Assert.Equal(gfx.Palette.Live.Rgb.ToArray(), gfx.Palette.Saved.ToArray());
    }

    [Fact]
    public void Single_entry_writes_mirror_into_the_saved_palette()
    {
        var palette = new GamePalette(new Palette());
        int version = palette.Live.Version;
        palette.SetPaletteEntry(3, 0, 0, 0); // unchanged: no write at all
        Assert.Equal(version, palette.Live.Version);
        palette.SetPaletteEntry(3, 10, 20, 0x130); // low byte stored
        Assert.Equal(((byte)10, (byte)20, (byte)0x30), palette.GetPaletteEntry(3));
        Assert.Equal(new byte[] { 10, 20, 0x30 }, palette.Saved.Slice(9, 3).ToArray());
        palette.SetPaletteEntry(3, 10, 20, 0x130); // short differs from the stored byte: written again
        Assert.True(palette.Live.Version > version + 1);
    }

    [Fact]
    public void Save_and_restore_round_trip_the_whole_palette()
    {
        var palette = new GamePalette(new Palette());
        var rgb = new byte[768];
        new Random(3).NextBytes(rgb);
        palette.SetWholePaletteFromTriplets(rgb);
        palette.SaveGamePalette();
        palette.SetWholePaletteFromTriplets(new byte[768]);
        Assert.Equal(((byte)0, (byte)0, (byte)0), palette.GetPaletteEntry(77));
        palette.RestoreGamePalette();
        Assert.Equal(rgb, palette.Live.Rgb.ToArray());
    }

    [Fact]
    public void Active_entries_are_the_non_black_ones()
    {
        var palette = new GamePalette(new Palette());
        palette.CachePaletteEntry(5, 1, 0, 0);
        palette.CachePaletteEntry(200, 0, 0, 9);
        Span<byte> indices = stackalloc byte[256];
        Assert.Equal(2, palette.CollectActivePaletteIndices(indices));
        Assert.Equal(new byte[] { 5, 200 }, indices[..2].ToArray());
    }

    [Fact]
    public void Player_hit_flash_fades_entry_191_by_four_per_frame()
    {
        var palette = new GamePalette(new Palette());
        var flight = new FlightPaletteEffects();
        flight.ResetCockpitPaletteEntries(palette);
        flight.TriggerPlayerHitPaletteFlash(cameraViewMode: 4);
        Assert.Equal(0, flight.DamageFlashColour[0]);
        flight.TriggerPlayerHitPaletteFlash(cameraViewMode: 0);
        Assert.Equal(0x30, flight.DamageFlashColour[0]);
        var reds = new List<int>();
        for (int frame = 0; frame < 14; frame++)
        {
            flight.UpdateSpacePaletteFade(palette);
            reds.Add(palette.GetPaletteEntry(PaletteColours.PrimaryViewBuffer).R);
        }
        Assert.Equal(new[] { 44, 40, 36, 32, 28, 24, 20, 16, 12, 8, 4, 0, 0, 0 }, reds);
        Assert.Equal(32, palette.GetPaletteEntry(PaletteColours.PrimaryViewBuffer).B);
    }

    [Fact]
    public void Cockpit_direction_lights_fade_and_keep_blue_when_red_is_gone()
    {
        var palette = new GamePalette(new Palette());
        var flight = new FlightPaletteEffects();
        flight.FlashCockpitEntry(1);
        Span<short> entry = flight.GetFadeEntry(1);
        entry[1] = 9;
        entry[2] = 9;
        flight.FadeCockpitFlashEntries(palette);
        Assert.Equal(((byte)0x34, (byte)0, (byte)0), palette.GetPaletteEntry(186));
        // FadeFlightPaletteEntry with R == 0 clears only G (original quirk)
        short[] rest = [0, 5, 6];
        FlightPaletteEffects.FadeFlightPaletteEntry(rest);
        Assert.Equal(new short[] { 0, 0, 6 }, rest);
    }
}
