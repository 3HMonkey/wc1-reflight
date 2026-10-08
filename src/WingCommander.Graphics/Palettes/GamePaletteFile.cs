using WingCommander.Core.Resources;

namespace WingCommander.Graphics.Palettes;

/// <summary>
/// GAME.PAL: an IFF FORM/ILBM with a BMHD and a 768-byte CMAP chunk and no BODY. The game
/// ignores the IFF structure and reads the 768 bytes at offset 0x30 as 8-bit R,G,B triplets.
/// </summary>
/// <remarks>C: LoadPaletteTripletsFile (0x404610, cmpgn.c).</remarks>
public static class GamePaletteFile
{
    /// <summary>File name inside GAMEDAT.</summary>
    public const string FileName = "GAME.PAL";

    /// <summary>Offset of the CMAP payload.</summary>
    public const int TripletOffset = 0x30;

    /// <summary>Copies the 768 palette bytes of a GAME.PAL image into <paramref name="rgb"/>.</summary>
    public static void ReadTriplets(ReadOnlySpan<byte> file, Span<byte> rgb)
    {
        if (rgb.Length < 0x300)
            throw new ArgumentException("Destination needs 768 bytes.", nameof(rgb));
        if (file.Length < TripletOffset + 0x300)
            throw new GameDataException($"{FileName}: file has {file.Length} bytes, expected at least {TripletOffset + 0x300}.");
        file.Slice(TripletOffset, 0x300).CopyTo(rgb);
    }

    /// <summary>Reads GAME.PAL from the game directory and returns its 768 palette bytes.</summary>
    public static byte[] Load(GameDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var rgb = new byte[0x300];
        ReadTriplets(directory.ReadFile(FileName), rgb);
        return rgb;
    }
}
