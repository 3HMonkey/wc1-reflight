using System.Globalization;
using WingCommander.Core.Imaging;
using WingCommander.Core.Resources;
using WingCommander.Core.Video;
using WingCommander.Graphics;
using WingCommander.Graphics.Cockpit;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;
using WingCommander.Graphics.Text;

namespace WingCommander.Tools;

internal static partial class Commands
{
    static partial void RegisterGraphicsCommands()
    {
        Register("export-shape",
            "<file> <sec[/sub]> [--out dir] [--frame N] [--scale N]  frames as PNG + contact sheet",
            ExportShape);
        Register("render-shape",
            "<file> <sec> <frame> <angle> <zoom8.8> [--hflip] [--vflip] [--out png]  rotated/scaled draw",
            RenderShape);
        Register("export-font", "<font 0-3> [--out png] [--scale N]  glyph sheet of all 256 codes", ExportFont);
        Register("render-text", "<font 0-3> \"<text>\" [--out png] [--centre] [--scale N]  wrapped text", RenderText);
        Register("font-metrics", "<font 0-3>  cell width and ink extents of every character", FontMetrics);
        Register("hd-text", "<font 0-3> [\"<text>\"] [--scale N] [--aspect] [--smooth s] [--colour i] [--ttf font.ttf] [--out png]  classic vs output-resolution glyphs", HdTextPreview);
        Register("export-palette", "[--out png] [--raw]  GAME.PAL as 16x16 swatches", ExportPalette);
        Register("export-view", "<PCSHIP.Vnn> [--out dir]  cockpit view masks composited over the cockpit art",
            ExportView);
    }

    /// <summary>The startup palette (GAME.PAL plus the cockpit overrides), or the raw file with --raw.</summary>
    private static GraphicsContext CreateGraphics(ToolOptions o, bool withFonts = false)
    {
        GameDirectory directory = RequireGameDirectory(o);
        string? paletteFile = o.Option("palette");
        byte[] pal = paletteFile is not null && File.Exists(paletteFile)
            ? File.ReadAllBytes(paletteFile)
            : directory.ReadFile(GamePaletteFile.FileName);
        var graphics = new GraphicsContext(null, new Palette(), withFonts ? FontCache.FromGameDirectory(directory) : null);
        if (o.Has("raw"))
            graphics.Palette.LoadPaletteTripletsFile(pal);
        else
            graphics.LoadGamePaletteFile(pal);
        return graphics;
    }

    private static int ExportShape(ToolOptions o)
    {
        PacketFile packet = OpenPacket(o, o.Positional(0));
        string path = o.Positional(1);
        string name = $"{packet.Name}[{path}]";
        if (!ShapeTable.TryParse(name, ResolveSectionPath(packet, path), out ShapeTable? shape))
            throw new GameDataException($"{name} is not a shape table.");
        string outDir = o.Option("out") ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);
        int scale = Math.Max(1, o.IntOption("scale", 1));
        int only = o.IntOption("frame", -1);
        GraphicsContext graphics = CreateGraphics(o);
        ReadOnlySpan<byte> palette = graphics.Palette.Live.Rgb;
        string stem = $"{Path.GetFileNameWithoutExtension(packet.Name)}_{Path.GetExtension(packet.Name).TrimStart('.')}_s{path.Replace('/', '-')}";

        int maxWidth = 1, maxHeight = 1;
        for (int f = 0; f < shape.FrameCount; f++)
        {
            ShapeExtents e = shape.GetExtents(f);
            maxWidth = Math.Max(maxWidth, e.Width);
            maxHeight = Math.Max(maxHeight, e.Height);
            if (only >= 0 && f != only)
                continue;
            IndexedSurface frame = RenderFrame(graphics, shape, f);
            string file = Path.Combine(outDir, $"{stem}_f{f:D2}.png");
            WriteScaled(file, frame.Width, frame.Height, frame.Pixels, palette, scale, transparent: 0xFF);
            Console.WriteLine($"frame {f,3}: {e.Width}x{e.Height} hotspot ({e.Left},{e.Top}) -> {file}");
        }

        if (only < 0)
        {
            const int Gap = 4;
            int columns = Math.Max(1, Math.Min(shape.FrameCount, Math.Max(1, 1600 / (maxWidth + Gap))));
            int rows = (shape.FrameCount + columns - 1) / columns;
            int sheetWidth = columns * (maxWidth + Gap) + Gap, sheetHeight = rows * (maxHeight + Gap) + Gap;
            var rgba = new byte[sheetWidth * sheetHeight * 4];
            FillChecker(rgba, sheetWidth, sheetHeight);
            for (int f = 0; f < shape.FrameCount; f++)
            {
                IndexedSurface frame = RenderFrame(graphics, shape, f);
                int cellX = Gap + f % columns * (maxWidth + Gap);
                int cellY = Gap + f / columns * (maxHeight + Gap);
                Composite(rgba, sheetWidth, cellX, cellY, frame, palette);
            }
            string sheet = Path.Combine(outDir, $"{stem}_sheet.png");
            Png.WriteRgba(sheet, sheetWidth, sheetHeight, rgba);
            Console.WriteLine($"{shape.FrameCount} frames, contact sheet {sheetWidth}x{sheetHeight} -> {sheet}");
        }
        return 0;
    }

    /// <summary>Draws one frame through the sprite renderer into a tight surface (0xFF = transparent).</summary>
    private static IndexedSurface RenderFrame(GraphicsContext graphics, ShapeTable shape, int frame)
    {
        ShapeExtents e = shape.GetExtents(frame);
        var surface = new IndexedSurface(Math.Max(1, e.Width), Math.Max(1, e.Height));
        surface.Clear(0xFF);
        var viewport = new Viewport(surface, 0, 0, surface.Width - 1, surface.Height - 1);
        graphics.DrawSpriteDefault(viewport, e.Left, e.Top, shape, frame);
        return surface;
    }

    private static int RenderShape(ToolOptions o)
    {
        PacketFile packet = OpenPacket(o, o.Positional(0));
        string path = o.Positional(1);
        int frame = int.Parse(o.Positional(2), CultureInfo.InvariantCulture);
        int angle = int.Parse(o.Positional(3), CultureInfo.InvariantCulture);
        int zoom = int.Parse(o.Positional(4), CultureInfo.InvariantCulture);
        string name = $"{packet.Name}[{path}]";
        if (!ShapeTable.TryParse(name, ResolveSectionPath(packet, path), out ShapeTable? shape))
            throw new GameDataException($"{name} is not a shape table.");
        int flip = (o.Has("hflip") ? GraphicsContext.FlipHorizontal : 0) | (o.Has("vflip") ? GraphicsContext.FlipVertical : 0);
        var framebuffer = new Framebuffer();
        GraphicsContext graphics = CreateGraphics(o);
        var screen = Viewport.InitializeDIBScreenViewport(framebuffer);
        graphics.ClearViewport(screen, PaletteColours.PrimaryViewBuffer);
        graphics.DrawViewportLine(screen, 150, 100, 170, 100, PaletteColours.DarkGrey);
        graphics.DrawViewportLine(screen, 160, 90, 160, 110, PaletteColours.DarkGrey);
        graphics.DrawSpriteTransformed(screen, 160, 100, shape, frame, angle, zoom, zoom, flip, 0);
        Span<short> bounds = stackalloc short[4];
        if (ShapeBounds.GetTransformedShapeBounds(screen, 160, 100, shape, frame, angle, zoom, flip, bounds) != 0)
            graphics.DrawViewportBorder(screen, bounds[0], bounds[1], bounds[2], bounds[3], PaletteColours.Yellow);
        string file = o.Option("out") ?? $"render_{Path.GetFileNameWithoutExtension(packet.Name)}_{frame}_{angle}_{zoom}.png";
        WriteScaled(file, Framebuffer.Width, Framebuffer.Height, framebuffer.Pixels, graphics.Palette.Live.Rgb,
            Math.Max(1, o.IntOption("scale", 1)), transparent: -1);
        Console.WriteLine($"{name} frame {frame} angle {angle} zoom {zoom} flip 0x{flip:x} -> {file}");
        return 0;
    }

    private static int ExportFont(ToolOptions o)
    {
        int fontIndex = int.Parse(o.Positional(0), CultureInfo.InvariantCulture);
        GraphicsContext graphics = CreateGraphics(o, withFonts: true);
        BitmapFont font = graphics.Fonts!.Get(fontIndex);
        int cellWidth = 2;
        for (int c = 0; c < 256; c++)
            cellWidth = Math.Max(cellWidth, font.GetWidth((byte)c) + 2);
        int cellHeight = font.Height + 2;
        var surface = new IndexedSurface(16 * cellWidth, 16 * cellHeight);
        surface.Clear(PaletteColours.Black);
        var viewport = new Viewport(surface, 0, 0, surface.Width - 1, surface.Height - 1);
        var context = new TextContext { Viewport = viewport, Font = font, Colour = font.InkIndex, BackgroundColour = 0xFF };
        for (int c = 0; c < 256; c++)
        {
            int cellX = c % 16 * cellWidth, cellY = c / 16 * cellHeight;
            graphics.DrawFilledViewportRect(viewport, cellX, cellY, cellX + cellWidth - 1, cellY + cellHeight - 1,
                (c / 16 + c % 16) % 2 == 0 ? PaletteColours.Black : PaletteColours.DarkBlue);
            context.CursorX = (short)(cellX + 1);
            context.CursorY = (short)(cellY + 1);
            graphics.DrawFontGlyph((byte)c, context, font.Height, font.GetWidth((byte)c), context.CursorY);
        }
        string file = o.Option("out") ?? $"font{fontIndex}.png";
        WriteScaled(file, surface.Width, surface.Height, surface.Pixels, graphics.Palette.Live.Rgb,
            Math.Max(1, o.IntOption("scale", 1)), transparent: -1);
        Console.WriteLine($"font {fontIndex}: height {font.Height}, ink {font.InkIndex}, background {font.BackgroundIndex}, " +
            $"{font.Data.Length} bytes -> {file}");
        return 0;
    }

    private static int RenderText(ToolOptions o)
    {
        int fontIndex = int.Parse(o.Positional(0), CultureInfo.InvariantCulture);
        string text = o.Positional(1).Replace("\\n", "\n", StringComparison.Ordinal);
        var framebuffer = new Framebuffer();
        GraphicsContext graphics = CreateGraphics(o, withFonts: true);
        var screen = Viewport.InitializeDIBScreenViewport(framebuffer);
        graphics.ClearViewport(screen, PaletteColours.Black);
        var panel = screen.Clone();
        panel.SetViewportRect(20, 20, 299, 179);
        graphics.DrawViewportBorder(screen, panel.Left - 1, panel.Top - 1, panel.Right + 1, panel.Bottom + 1,
            PaletteColours.DarkGrey);
        var context = new TextContext { Viewport = panel };
        graphics.InitializeTextContextFromFont(context, fontIndex, context.Font?.InkIndex ?? 0, 0xFF);
        context.Colour = context.Font!.InkIndex;
        context.Alignment = o.Has("centre") ? TextContext.AlignCentre : (byte)0;
        graphics.SetTextCursor(panel.Left, panel.Top);
        graphics.DrawTextString(text);
        string file = o.Option("out") ?? $"text{fontIndex}.png";
        WriteScaled(file, Framebuffer.Width, Framebuffer.Height, framebuffer.Pixels, graphics.Palette.Live.Rgb,
            Math.Max(1, o.IntOption("scale", 1)), transparent: -1);
        Console.WriteLine($"font {fontIndex}: cursor ended at ({context.CursorX},{context.CursorY}) -> {file}");
        return 0;
    }

    private static int ExportPalette(ToolOptions o)
    {
        GraphicsContext graphics = CreateGraphics(o);
        var pixels = new byte[256 * 256];
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
                pixels[y * 256 + x] = (byte)(y / 16 * 16 + x / 16);
        string file = o.Option("out") ?? "palette.png";
        Png.WriteIndexed(file, 256, 256, pixels, graphics.Palette.Live.Rgb);
        Console.WriteLine($"palette -> {file}");
        return 0;
    }

    private static int ExportView(ToolOptions o)
    {
        PacketFile packet = OpenPacket(o, o.Positional(0));
        var views = ViewGeometrySet.Parse(packet.Name, packet.GetSection(ViewGeometrySet.PcShipSection).Span);
        string outDir = o.Option("out") ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);
        GraphicsContext graphics = CreateGraphics(o);
        Console.WriteLine($"{packet.Name}: buffer size {views.BufferSize}, {views.Count} views");
        for (int view = 0; view < views.Count; view++)
        {
            ViewGeometry geometry = views[view];
            var framebuffer = new Framebuffer();
            var screen = Viewport.InitializeDIBScreenViewport(framebuffer);
            graphics.ClearViewport(screen, PaletteColours.Black);
            if (ShapeTable.TryParse($"{packet.Name}[{view}]", packet.GetSection(view), out ShapeTable? cockpit))
                graphics.DrawSpriteDefault(screen, 0, 0, cockpit, 0);
            var space = Viewport.Allocate(0, 0, geometry.Width - 1, geometry.Height - 1, PaletteColours.PrimaryViewBuffer);
            for (int y = 0; y < geometry.Height; y += 8)
                graphics.DrawViewportLine(space, 0, y, geometry.Width - 1, y, PaletteColours.Red);
            for (int x = 0; x < geometry.Width; x += 8)
                graphics.DrawViewportLine(space, x, 0, x, geometry.Height - 1, PaletteColours.Yellow);
            graphics.DrawViewportBorder(space, 0, 0, geometry.Width - 1, geometry.Height - 1, PaletteColours.ViewportClear);
            graphics.FizzleFade(space, screen, geometry);
            string file = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(packet.Name)}_{Path.GetExtension(packet.Name).TrimStart('.')}_view{view}.png");
            Png.WriteIndexed(file, Framebuffer.Width, Framebuffer.Height, framebuffer.Pixels, graphics.Palette.Live.Rgb);
            Console.WriteLine($"view {view}: {geometry.Width}x{geometry.Height} at ({geometry.OriginX},{geometry.OriginY}), " +
                $"{geometry.Runs.Length} runs -> {file}");
        }
        return 0;
    }

    private static void WriteScaled(string file, int width, int height, ReadOnlySpan<byte> pixels,
        ReadOnlySpan<byte> palette, int scale, int transparent)
    {
        if (scale == 1)
        {
            Png.WriteIndexed(file, width, height, pixels, palette, transparent);
            return;
        }
        var scaled = new byte[width * scale * height * scale];
        for (int y = 0; y < height * scale; y++)
            for (int x = 0; x < width * scale; x++)
                scaled[y * width * scale + x] = pixels[y / scale * width + x / scale];
        Png.WriteIndexed(file, width * scale, height * scale, scaled, palette, transparent);
    }

    private static void FillChecker(byte[] rgba, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = ((x >> 3) + (y >> 3)) % 2 == 0 ? (byte)0x50 : (byte)0x68;
                int o = (y * width + x) * 4;
                rgba[o] = v;
                rgba[o + 1] = v;
                rgba[o + 2] = (byte)(v + 0x18);
                rgba[o + 3] = 0xFF;
            }
        }
    }

    private static void Composite(byte[] rgba, int sheetWidth, int left, int top, IndexedSurface frame,
        ReadOnlySpan<byte> palette)
    {
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                byte index = frame.Pixels[y * frame.Width + x];
                if (index == 0xFF)
                    continue;
                int o = ((top + y) * sheetWidth + left + x) * 4;
                rgba[o] = palette[index * 3];
                rgba[o + 1] = palette[index * 3 + 1];
                rgba[o + 2] = palette[index * 3 + 2];
                rgba[o + 3] = 0xFF;
            }
        }
    }
}
