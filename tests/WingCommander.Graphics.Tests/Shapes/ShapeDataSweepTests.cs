using WingCommander.Core.Resources;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Tests;

namespace WingCommander.Graphics.Tests.Shapes;

public class ShapeDataSweepTests
{
    private static readonly string[] Patterns = ["*.VGA", "SHIP.V*", "PCSHIP.V*", "SHIPTYPE.V*", "MIDGAME.V*", "INTRO1.DAT"];

    public static TheoryData<string> GraphicsFiles()
    {
        var data = new TheoryData<string>();
        GameDirectory? directory = GameData.Directory;
        if (directory is null)
        {
            data.Add("ARROW.VGA");
            return data;
        }
        foreach (string pattern in Patterns)
            foreach (string path in Directory.EnumerateFiles(directory.DataPath, pattern).Order(StringComparer.OrdinalIgnoreCase))
                data.Add(Path.GetFileName(path));
        return data;
    }

    /// <summary>Raw (non-shape) sections of the graphics files, as documented in resources.md / graphics.md.</summary>
    private static bool IsDocumentedRawSection(string file, int section) =>
        (file.Equals("COCKPIT.VGA", StringComparison.OrdinalIgnoreCase) && section == 8)
        || (file.Equals("INTRO1.DAT", StringComparison.OrdinalIgnoreCase) && section == 0)
        || (file.StartsWith("PCSHIP.", StringComparison.OrdinalIgnoreCase) && section == 6)
        || (file.StartsWith("SHIPTYPE.", StringComparison.OrdinalIgnoreCase) && section == 2)
        || (file.StartsWith("MIDGAME.", StringComparison.OrdinalIgnoreCase) && section is 1 or 2 or 5 or 6);

    [DataTheory]
    [MemberData(nameof(GraphicsFiles))]
    public void Every_frame_decodes_and_draws_centred_unrotated_and_rotated(string file)
    {
        PacketFile packet = GameData.Require().OpenPacket(file);
        var gfx = new GraphicsContext();
        var surface = new IndexedSurface(320, 200);
        var screen = new Viewport(surface, 0, 0, 319, 199);
        int frames = 0;
        for (int section = 0; section < packet.SectionCount; section++)
        {
            ReadOnlyMemory<byte> bytes = packet.GetSection(section);
            if (bytes.Length == 0)
                continue;
            if (!ShapeTable.TryParse($"{file}[{section}]", bytes, out ShapeTable? shape))
            {
                Assert.True(IsDocumentedRawSection(file, section), $"{file}[{section}] is neither a shape table nor a documented raw table");
                continue;
            }
            for (int f = 0; f < shape.FrameCount; f++)
            {
                ShapeExtents e = shape.GetExtents(f);
                Assert.True(e.Width * e.Height <= RleRenderer.TransformScratchSize, $"{shape.Name} frame {f} exceeds 64000 pixels");
                PreparedFrame prepared = shape.GetPreparedFrame(f);
                Assert.Equal(e.Height, prepared.Height);
                Assert.Equal(e.Width, prepared.Width);

                // prepared form and raw decoder agree pixel for pixel (0xFF stays transparent)
                var raw = new byte[e.Width * e.Height];
                var fromOps = new byte[e.Width * e.Height];
                Array.Fill(raw, (byte)0xFF);
                Array.Fill(fromOps, (byte)0xFF);
                ShapeFrameDecoder.DecodeShapeFrame(shape, f, raw, e.Width, e.Height, e.Left, e.Top);
                prepared.DecodeTo(fromOps, default);
                Assert.True(raw.AsSpan().SequenceEqual(fromOps), $"{shape.Name} frame {f}: prepared != raw decode");

                // drawn centred: exactly the opaque pixels inside the screen change
                surface.Clear(0xFF);
                int x = 160 - (e.Width / 2) + e.Left, y = 100 - (e.Height / 2) + e.Top;
                gfx.DrawSpriteDefault(screen, x, y, shape, f);
                int expectedOpaque = 0;
                for (int r = 0; r < e.Height; r++)
                {
                    for (int c = 0; c < e.Width; c++)
                    {
                        int sx = x - e.Left + c, sy = y - e.Top + r;
                        if (fromOps[r * e.Width + c] != 0xFF && sx is >= 0 and < 320 && sy is >= 0 and < 200)
                            expectedOpaque++;
                    }
                }
                Assert.Equal(expectedOpaque, surface.Pixels.Count(p => p != 0xFF));

                gfx.DrawSpriteScaled(screen, 160, 100, shape, f, 37, 0x180, GraphicsContext.FlipHorizontal);
                gfx.DrawSpriteScaled(screen, 10, 190, shape, f, -123, 0x40, 0);
                frames++;
            }
        }
        Assert.True(frames > 0 || file.Equals("INTRO1.DAT", StringComparison.OrdinalIgnoreCase), $"{file} has no frames");
    }

    [DataFact]
    public void Census_matches_the_analysis()
    {
        GameDirectory directory = GameData.Require();
        int tables = 0, frames = 0, withTransparentSpanPixels = 0;
        foreach (string pattern in Patterns)
        {
            foreach (string path in Directory.EnumerateFiles(directory.DataPath, pattern))
            {
                PacketFile packet = directory.OpenPacket(Path.GetFileName(path));
                for (int section = 0; section < packet.SectionCount; section++)
                {
                    if (!ShapeTable.TryParse(path, packet.GetSection(section), out ShapeTable? shape))
                        continue;
                    tables++;
                    frames += shape.FrameCount;
                    for (int f = 0; f < shape.FrameCount; f++)
                    {
                        ShapeExtents e = shape.GetExtents(f);
                        var raw = new byte[e.Width * e.Height];
                        ShapeFrameDecoder.DecodeShapeFrame(shape, f, raw, e.Width, e.Height, e.Left, e.Top);
                        int spanPixels = SpriteBackground.MeasureShapeFrameStorage(shape, f);
                        var prepared = new byte[raw.Length];
                        Array.Fill(prepared, (byte)0xFF);
                        shape.GetPreparedFrame(f).DecodeTo(prepared, default);
                        if (prepared.Count(p => p != 0xFF) < spanPixels)
                            withTransparentSpanPixels++;
                    }
                }
            }
        }
        Assert.Equal(670, tables);
        Assert.Equal(2945, frames);
        Assert.Equal(65, withTransparentSpanPixels); // frames whose spans contain the value 0xFF
    }
}
