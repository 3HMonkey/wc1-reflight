using System.Runtime.CompilerServices;
using WingCommander.Core.Rendering;
using WingCommander.Core.Video;
using WingCommander.Graphics.Raster;

namespace WingCommander.Graphics.Text;

/// <summary>Told about every pixel run a raster copy is about to move (output-resolution text follows its glyphs).</summary>
public interface IRasterCopyObserver
{
    /// <summary>
    /// <paramref name="length"/> pixels are about to be copied from buffer index
    /// <paramref name="sourceIndex"/> of <paramref name="source"/> to <paramref name="destinationIndex"/>
    /// of <paramref name="destination"/> (linear runs; they may continue on the next row).
    /// </summary>
    void OnCopy(IndexedSurface source, int sourceIndex, IndexedSurface destination, int destinationIndex, int length);

    /// <summary>A scaled copy (port addition) has stretched a rectangle of <paramref name="source"/> into <paramref name="destination"/> (called after the pixels were written).</summary>
    void OnScaledCopy(IndexedSurface source, IndexedSurface destination, in ScaledCopy copy);
}

/// <summary>
/// Follows the font glyphs the game draws so a renderer can draw them at output resolution
/// (ADR-013). <see cref="GraphicsContext.DrawFontGlyph"/> reports every glyph before it touches a
/// surface, raster copies report the runs they move (<see cref="IRasterCopyObserver"/>), so text
/// drawn into the space buffer, a scene buffer or a saved background keeps being followed when it
/// reaches the screen. At present time <see cref="Publish"/> works out, pixel by pixel, which
/// glyphs are intact on the screen and fills a <see cref="TextLayer"/>: the frame without their
/// foreground, the per-pixel draw mask and the glyph instances.
/// </summary>
/// <remarks>
/// <para>Every followed pixel keeps a small stack (newest on top, at most <see cref="MaximumDepth"/>)
/// of the glyph cells drawn over it, each with the pixel value it overwrote. Whatever else changes
/// a pixel - sprites, fills, scrolling, other text - is detected by value: when the pixel no longer
/// holds what its top glyph left there, the whole stack of that pixel is dropped and the pixel
/// shows the classic frame. Because a stack is checked against the pixel before every push, a
/// valid top implies valid layers below it. A copy replaces the destination stacks by the source
/// stacks, with the glyphs moved by the copy offset.</para>
/// <para>A scaled copy (the briefing board) gives the glyphs a new size. A scaled glyph covers
/// every destination pixel its cell touches: where the pixel samples the glyph, the pixel maps to
/// a cell of the glyph; where it samples something else (the edge of a shrunk cell, or the whole
/// of an "i" or a full stop the sampling skipped) the glyph lies on top, hollow: it has no
/// classic pixel there but may be drawn at output resolution.</para>
/// <para>A glyph is drawn at output resolution only while <b>all</b> of its foreground pixels are
/// still in valid stacks (text drawn over it does not count as a change): text that was erased or
/// painted over, even partly, is shown by the classic frame, so no fragment of an old line survives
/// where only its background pixels are left (cleared subtitles, redrawn panels). A scaled glyph
/// left without foreground pixels needs all its pixels instead.</para>
/// <para>Publishing walks each screen stack from the top: every drawable layer whose pixel lets the
/// layers below show through (transparent pixels, foreground on a transparent background, hollow
/// pixels) continues the walk; the first layer that defines the pixel by itself (background, or
/// foreground on an opaque background) ends it. Layers that are not drawable are skipped where
/// they are transparent and end the walk where they wrote the pixel. The mask stores the lowest
/// list index of the walked layers + 1 (normally the deepest layer's; copies list clones in the
/// order they meet them), so the renderer draws the glyphs of the walked layers there, in painter
/// order.</para>
/// </remarks>
public sealed class TextLayerTracker : IRasterCopyObserver
{
    /// <summary>Glyph layers remembered per pixel (deeper ones are forgotten).</summary>
    public const int MaximumDepth = 4;

    private readonly SurfaceTrack _screen;
    private readonly ConditionalWeakTable<IndexedSurface, SurfaceTrack> _tracks = new();
    private readonly List<SurfaceTrack> _trackList = [];
    private readonly List<GlyphRecord?> _records = [];
    private readonly Stack<int> _free = new();
    private readonly List<int> _order = [];
    private readonly Dictionary<(int Slot, int Dx, int Dy, SurfaceTrack Track), int> _clones = [];
    private readonly Dictionary<int, int> _scaledClones = [];
    private int[] _runStacks = new int[320 * MaximumDepth];
    private byte[] _runDepths = new byte[320];

    public TextLayerTracker(IndexedSurface screen, GlyphImageSource glyphs)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(glyphs);
        if (screen.Width != Framebuffer.Width || screen.Height != Framebuffer.Height)
            throw new ArgumentException("The tracker publishes the 320x200 screen surface.", nameof(screen));
        _screen = new SurfaceTrack(screen);
        _tracks.Add(screen, _screen);
        _trackList.Add(_screen);
        Glyphs = glyphs;
    }

    /// <summary>Glyph images (with font replacements) shared with the renderer.</summary>
    public GlyphImageSource Glyphs { get; }

    /// <summary>Off: nothing is recorded and <see cref="Publish"/> passes the frame through.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Glyphs currently remembered on all surfaces (diagnostics, tests).</summary>
    public int LiveGlyphs => _order.Count;

    /// <summary>Forgets every glyph.</summary>
    public void Reset()
    {
        foreach (var (_, track) in _tracks)
            Array.Clear(track.Depth);
        _records.Clear();
        _free.Clear();
        _order.Clear();
        _clones.Clear();
    }

    /// <summary>
    /// Forgets the glyphs of a buffer that is no longer used (a temporary picture); buffers that
    /// are simply dropped are forgotten at the next <see cref="Publish"/> after they were collected.
    /// </summary>
    public void Forget(IndexedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (!_tracks.TryGetValue(surface, out SurfaceTrack? track) || ReferenceEquals(track, _screen))
            return;
        ClearAll(track);
        _tracks.Remove(surface);
        _trackList.Remove(track);
    }

    /// <summary>True when buffer pixel <paramref name="index"/> of <paramref name="surface"/> belongs to a followed glyph.</summary>
    public bool IsFollowed(IndexedSurface surface, int index) =>
        Enabled && _tracks.TryGetValue(surface, out SurfaceTrack? track) && (uint)index < (uint)track.Depth.Length && track.Depth[index] != 0;

    /// <summary>
    /// A glyph is about to be drawn into <paramref name="surface"/> with its cell's top-left pixel
    /// at buffer position (<paramref name="x"/>, <paramref name="y"/>) and the colours of
    /// <see cref="GraphicsContext.DrawFontGlyph"/>. Glyphs that would wrap around the buffer edge,
    /// have no foreground or no image are not followed (they count as ordinary drawing).
    /// </summary>
    public void RecordGlyph(IndexedSurface surface, BitmapFont font, byte character, int x, int y, int width, int height,
        byte colour, byte background)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(font);
        if (!Enabled || font.Index < 0 || width <= 0 || height <= 0 || x < 0 || y < 0
            || x + width > surface.Width || y + height > surface.Height)
            return;
        ReadOnlySpan<byte> glyph = font.GetGlyph(character);
        if (glyph.Length < width * height || height != font.Height || width != font.GetWidth(character))
            return;
        var record = new GlyphRecord(font, character, (byte)width, (byte)height, colour, background, new byte[width * height]);
        if (!record.HasForeground(glyph) || !Glyphs.Get(font, character).HasForeground)
            return;

        SurfaceTrack track = Track(surface);
        record.Track = track;
        record.X = (short)x;
        record.Y = (short)y;
        record.OriginX = x;
        record.OriginY = y;
        int slot = Allocate(record);
        byte[] pixels = surface.Pixels;
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                int p = (y + row) * track.Width + x + column;
                int cell = row * width + column;
                Prune(track, p, pixels[p]);
                int depth = track.Depth[p];
                if (depth > 0 && _records[track.Stack[p * MaximumDepth + depth - 1]] is { } top && top.SameDraw(record))
                {
                    // The same glyph drawn again over itself (menus and readouts redrawn every frame):
                    // it takes the place of the old one, so the renderer never draws it twice.
                    record.Under[cell] = top.Under[cell];
                    Release(top, p);
                    track.Depth[p] = (byte)(depth - 1);
                }
                else
                {
                    record.Under[cell] = pixels[p];
                }
                Push(track, p, slot);
            }
        }
        _order.Add(slot);
    }

    /// <summary>The destination pixels take the source pixels' glyph stacks, moved by the copy offset.</summary>
    public void OnCopy(IndexedSurface source, int sourceIndex, IndexedSurface destination, int destinationIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enabled || length <= 0)
            return;
        _tracks.TryGetValue(source, out SurfaceTrack? from);
        _tracks.TryGetValue(destination, out SurfaceTrack? to);
        if (from is null && to is null)
            return;
        if (from is not null && to is null)
        {
            if (!AnyStack(from, sourceIndex, length))
                return;
            to = Track(destination);
        }

        // Snapshot the source stacks first (copies within one surface may overlap).
        if (_runDepths.Length < length)
        {
            _runDepths = new byte[length];
            _runStacks = new int[length * MaximumDepth];
        }
        for (int i = 0; i < length; i++)
        {
            int s = sourceIndex + i;
            if (from is null || (uint)s >= (uint)from.Depth.Length)
            {
                _runDepths[i] = 0;
                continue;
            }
            Prune(from, s, source.Pixels[s]);
            int depth = from.Depth[s];
            _runDepths[i] = (byte)depth;
            Array.Copy(from.Stack, s * MaximumDepth, _runStacks, i * MaximumDepth, depth);
        }

        for (int i = 0; i < length; i++)
        {
            int d = destinationIndex + i;
            if ((uint)d >= (uint)to!.Depth.Length)
                continue;
            Clear(to, d);
            int depth = _runDepths[i];
            if (depth == 0)
                continue;
            int s = sourceIndex + i;
            int dx = d % to.Width - s % from!.Width;
            int dy = d / to.Width - s / from.Width;
            for (int level = 0; level < depth; level++)
                Push(to, d, Clone(_runStacks[i * MaximumDepth + level], to, dx, dy));
        }
    }

    /// <summary>
    /// The written destination pixels take the stacks of the source pixels they sample, with the
    /// glyphs scaled: one clone per glyph the copy reaches, covering every destination pixel its
    /// cell touches. Pixels where a clone is hollow (they sample something else) get it on top;
    /// intact glyphs that no destination pixel samples become hollow clones.
    /// </summary>
    public void OnScaledCopy(IndexedSurface source, IndexedSurface destination, in ScaledCopy copy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enabled || copy.IsEmpty)
            return;
        _tracks.TryGetValue(source, out SurfaceTrack? from);
        _tracks.TryGetValue(destination, out SurfaceTrack? to);
        if (from is null && to is null)
            return;
        to ??= Track(destination);

        // Copies within one surface read a snapshot of the source stacks.
        int[]? stacks = null;
        byte[]? depths = null;
        if (from is not null && ReferenceEquals(from, to))
        {
            stacks = (int[])from.Stack.Clone();
            depths = (byte[])from.Depth.Clone();
        }

        int recordsBefore = _order.Count;
        _scaledClones.Clear();
        for (int y = copy.ClipTop; y <= copy.ClipBottom; y++)
        {
            int sourceRow = copy.SourceY(y);
            for (int x = copy.ClipLeft; x <= copy.ClipRight; x++)
            {
                int d = y * to.Width + x;
                Clear(to, d);
                if (from is null)
                    continue;
                int s = sourceRow * from.Width + copy.SourceX(x);
                if (depths is null)
                    Prune(from, s, source.Pixels[s]);
                int depth = (depths ?? from.Depth)[s];
                for (int level = 0; level < depth; level++)
                {
                    int slot = (stacks ?? from.Stack)[s * MaximumDepth + level];
                    if (!_scaledClones.TryGetValue(slot, out int clone))
                    {
                        clone = ScaledClone(slot, to, in copy, destination.Pixels);
                        _scaledClones[slot] = clone;
                    }
                    if (clone >= 0)
                        Push(to, d, clone);
                }
            }
        }

        if (from is not null)
        {
            // Glyphs the sampling skipped entirely: hollow clones, when they are intact.
            for (int i = 0; i < recordsBefore; i++)
            {
                int slot = _order[i];
                GlyphRecord record = _records[slot]!;
                if (!ReferenceEquals(record.Track, from) || _scaledClones.ContainsKey(slot))
                    continue;
                PruneFootprint(record, source.Pixels);
                _scaledClones[slot] = record.Drawable ? ScaledClone(slot, to, in copy, destination.Pixels) : -1;
            }
            foreach (int clone in _scaledClones.Values)
            {
                if (clone >= 0)
                    PushHollowCells(clone, to);
            }
        }
        _scaledClones.Clear();
    }

    /// <summary>
    /// Fills <paramref name="layer"/> for the frame in <paramref name="working"/> (the screen as it
    /// is presented, without the cursor) and publishes it.
    /// </summary>
    public void Publish(ReadOnlySpan<byte> working, TextLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (working.Length < Framebuffer.PixelCount)
            throw new ArgumentException("The frame needs 320x200 pixels.", nameof(working));
        layer.Clear();
        byte[] output = layer.Pixels.Pixels;
        ushort[] mask = layer.Mask;
        working[..Framebuffer.PixelCount].CopyTo(output);
        Array.Clear(mask);
        if (!Enabled)
        {
            Reset();
            layer.Publish();
            return;
        }

        ReleaseCollectedTracks();
        foreach (var (surface, track) in _tracks)
        {
            byte[] pixels = ReferenceEquals(track, _screen) ? null! : surface.Pixels;
            for (int p = 0; p < track.Depth.Length; p++)
            {
                if (track.Depth[p] != 0)
                    Prune(track, p, pixels is null ? working[p] : pixels[p]);
            }
        }
        CollectUnused();
        _clones.Clear();
        foreach (int slot in _order)
        {
            GlyphRecord record = _records[slot]!;
            record.Index = ReferenceEquals(record.Track, _screen) && record.Drawable && layer.Count < ushort.MaxValue - 1
                ? layer.Add(new GlyphInstance(record.OriginX, record.OriginY, record.Key, record.Colour, record.ScaleX, record.ScaleY))
                : -1;
        }

        for (int p = 0; p < Framebuffer.PixelCount; p++)
        {
            int depth = _screen.Depth[p];
            if (depth == 0)
                continue;
            int lowest = int.MaxValue;
            byte value = working[p];
            for (int level = depth - 1; level >= 0; level--)
            {
                GlyphRecord record = _records[_screen.Stack[p * MaximumDepth + level]]!;
                int cell = record.Cell(p);
                if (record.Index < 0)
                {
                    // Not drawn: invisible where it left the pixel alone, classic where it wrote it.
                    if (record.Writes(cell))
                        break;
                    continue;
                }
                lowest = Math.Min(lowest, record.Index);
                value = record.Back(cell, out bool covers);
                if (covers)
                    break;
            }
            if (lowest == int.MaxValue)
                continue;
            output[p] = value;
            mask[p] = (ushort)(lowest + 1);
        }
        layer.Publish();
    }

    private SurfaceTrack Track(IndexedSurface surface)
    {
        if (!_tracks.TryGetValue(surface, out SurfaceTrack? track))
        {
            track = new SurfaceTrack(surface);
            _tracks.Add(surface, track);
            _trackList.Add(track);
        }
        return track;
    }

    /// <summary>Drops the glyphs of buffers the game no longer holds (their records would live on otherwise).</summary>
    private void ReleaseCollectedTracks()
    {
        for (int i = _trackList.Count - 1; i >= 0; i--)
        {
            SurfaceTrack track = _trackList[i];
            if (track.Surface.TryGetTarget(out _))
                continue;
            ClearAll(track);
            _trackList.RemoveAt(i);
        }
    }

    private void ClearAll(SurfaceTrack track)
    {
        for (int p = 0; p < track.Depth.Length; p++)
        {
            if (track.Depth[p] != 0)
                Clear(track, p);
        }
    }

    private static bool AnyStack(SurfaceTrack track, int start, int length)
    {
        int end = Math.Min(track.Depth.Length, start + length);
        for (int i = Math.Max(0, start); i < end; i++)
        {
            if (track.Depth[i] != 0)
                return true;
        }
        return false;
    }

    private int Allocate(GlyphRecord record)
    {
        if (_free.TryPop(out int slot))
        {
            _records[slot] = record;
            return slot;
        }
        _records.Add(record);
        return _records.Count - 1;
    }

    /// <summary>
    /// The copy of a source glyph on the destination surface: one per glyph, offset and surface
    /// between two publishes (rectangle copies report one run per row).
    /// </summary>
    private int Clone(int slot, SurfaceTrack track, int dx, int dy)
    {
        if (_clones.TryGetValue((slot, dx, dy, track), out int clone))
            return clone;
        GlyphRecord original = _records[slot]!;
        var copy = original.MovedTo(track, (short)(original.X + dx), (short)(original.Y + dy));
        clone = Allocate(copy);
        _order.Add(clone);
        _clones[(slot, dx, dy, track)] = clone;
        return clone;
    }

    /// <summary>The scaled copy of a source glyph, or -1 when the copy touches none of its pixels.</summary>
    private int ScaledClone(int slot, SurfaceTrack track, in ScaledCopy copy, byte[] destinationPixels)
    {
        if (_records[slot]!.ScaledTo(track, in copy, destinationPixels) is not { } scaled)
            return -1;
        int clone = Allocate(scaled);
        _order.Add(clone);
        return clone;
    }

    /// <summary>Puts a scaled glyph on top of the pixels where it is hollow.</summary>
    private void PushHollowCells(int slot, SurfaceTrack track)
    {
        GlyphRecord record = _records[slot]!;
        for (int row = 0; row < record.FootprintHeight; row++)
        {
            for (int column = 0; column < record.FootprintWidth; column++)
            {
                if (record.IsHollow(row * record.FootprintWidth + column))
                    Push(track, (record.Y + row) * track.Width + record.X + column, slot);
            }
        }
    }

    /// <summary>Validates the stacks of a glyph's footprint (pixels a copy did not sample).</summary>
    private void PruneFootprint(GlyphRecord record, byte[] pixels)
    {
        SurfaceTrack track = record.Track;
        for (int y = record.Y; y < record.Y + record.FootprintHeight; y++)
        {
            for (int x = record.X; x < record.X + record.FootprintWidth; x++)
            {
                int p = y * track.Width + x;
                if ((uint)p < (uint)track.Depth.Length && track.Depth[p] != 0)
                    Prune(track, p, pixels[p]);
            }
        }
    }

    /// <summary>Drops the stack of <paramref name="p"/> when its top glyph no longer explains the pixel.</summary>
    private void Prune(SurfaceTrack track, int p, byte value)
    {
        int depth = track.Depth[p];
        if (depth == 0)
            return;
        GlyphRecord top = _records[track.Stack[p * MaximumDepth + depth - 1]]!;
        if (top.Expected(top.Cell(p)) != value)
            Clear(track, p);
    }

    private void Clear(SurfaceTrack track, int p)
    {
        int depth = track.Depth[p];
        for (int level = 0; level < depth; level++)
            Release(_records[track.Stack[p * MaximumDepth + level]]!, p);
        track.Depth[p] = 0;
    }

    private void Push(SurfaceTrack track, int p, int slot)
    {
        int depth = track.Depth[p];
        int baseIndex = p * MaximumDepth;
        if (depth == MaximumDepth)
        {
            Release(_records[track.Stack[baseIndex]]!, p);
            Array.Copy(track.Stack, baseIndex + 1, track.Stack, baseIndex, MaximumDepth - 1);
            depth--;
        }
        track.Stack[baseIndex + depth] = slot;
        track.Depth[p] = (byte)(depth + 1);
        GlyphRecord record = _records[slot]!;
        record.References++;
        if (record.IsForeground(record.Cell(p)))
            record.ForegroundReferences++;
    }

    /// <summary>A stack entry of <paramref name="record"/> at pixel <paramref name="p"/> goes away.</summary>
    private static void Release(GlyphRecord record, int p)
    {
        record.References--;
        if (record.IsForeground(record.Cell(p)))
            record.ForegroundReferences--;
    }

    private void CollectUnused()
    {
        int kept = 0;
        for (int i = 0; i < _order.Count; i++)
        {
            int slot = _order[i];
            if (_records[slot]!.References > 0)
            {
                _order[kept++] = slot;
                continue;
            }
            _records[slot] = null;
            _free.Push(slot);
        }
        _order.RemoveRange(kept, _order.Count - kept);
    }

    /// <summary>The glyph stacks of one surface (buffer coordinates, row-major).</summary>
    private sealed class SurfaceTrack(IndexedSurface surface)
    {
        public int Width { get; } = surface.Width;

        public int[] Stack { get; } = new int[surface.Width * surface.Height * MaximumDepth];

        public byte[] Depth { get; } = new byte[surface.Width * surface.Height];

        /// <summary>The buffer, weakly: a track whose buffer was collected is released.</summary>
        public WeakReference<IndexedSurface> Surface { get; } = new(surface);
    }

    /// <summary>
    /// One followed glyph: what it drew, where, and what it covered. Its footprint is the
    /// rectangle of pixels it occupies on its surface; after a scaled copy every footprint column
    /// and row maps to a column and row of the glyph cell, or to none (<see cref="NoCell"/>):
    /// there the glyph is hollow (null maps = the cell itself).
    /// </summary>
    private sealed class GlyphRecord
    {
        private const byte NoCell = 0xFF;

        private readonly BitmapFont _font;
        private readonly bool _translate;
        private readonly byte[]? _columnCells;
        private readonly byte[]? _rowCells;

        public GlyphRecord(BitmapFont font, byte character, byte width, byte height, byte colour, byte background, byte[] under)
            : this(font, character, width, height, colour, background, under, width, height, null, null)
        {
        }

        private GlyphRecord(BitmapFont font, byte character, byte width, byte height, byte colour, byte background, byte[] under,
            int footprintWidth, int footprintHeight, byte[]? columnCells, byte[]? rowCells)
        {
            _font = font;
            Character = character;
            Width = width;
            Height = height;
            Colour = colour;
            Background = background;
            Under = under;
            FootprintWidth = footprintWidth;
            FootprintHeight = footprintHeight;
            _columnCells = columnCells;
            _rowCells = rowCells;
            _translate = font.InkIndex != colour || font.BackgroundIndex != background;
            Key = new GlyphKey((byte)font.Index, character);
            for (int cell = 0; cell < footprintWidth * footprintHeight; cell++)
            {
                if (IsForeground(cell))
                    ForegroundCount++;
            }
        }

        public SurfaceTrack Track { get; set; } = null!;

        /// <summary>Buffer column of the footprint's left edge.</summary>
        public short X { get; set; }

        /// <summary>Buffer row of the footprint's top edge.</summary>
        public short Y { get; set; }

        public int FootprintWidth { get; }

        public int FootprintHeight { get; }

        /// <summary>Where the glyph cell's top-left corner lies (buffer coordinates; fractional after scaled copies).</summary>
        public float OriginX { get; set; }

        public float OriginY { get; set; }

        /// <summary>Size of the cell relative to the font (1 unless scaled).</summary>
        public float ScaleX { get; set; } = 1f;

        public float ScaleY { get; set; } = 1f;

        public byte Character { get; }

        public byte Width { get; }

        public byte Height { get; }

        public byte Colour { get; }

        public byte Background { get; }

        public GlyphKey Key { get; }

        /// <summary>The pixels before the glyph was drawn (footprint order; shared with moved copies).</summary>
        public byte[] Under { get; }

        public int References { get; set; }

        /// <summary>Foreground pixels of the footprint (ink and fixed colours that are drawn).</summary>
        public int ForegroundCount { get; }

        /// <summary>Foreground pixels where the glyph is still in a valid stack.</summary>
        public int ForegroundReferences { get; set; }

        /// <summary>
        /// All foreground pixels are intact (or covered only by later text); a scaled glyph left
        /// without foreground pixels needs its whole footprint.
        /// </summary>
        public bool Drawable => ForegroundCount > 0
            ? ForegroundReferences == ForegroundCount
            : References > 0 && References == FootprintWidth * FootprintHeight;

        /// <summary>List index in the layer being published (-1 = not drawn).</summary>
        public int Index { get; set; }

        private bool IsScaled => _columnCells is not null;

        /// <summary>The footprint cell of buffer pixel <paramref name="p"/>.</summary>
        public int Cell(int p) => (p / Track.Width - Y) * FootprintWidth + p % Track.Width - X;

        /// <summary>True where the glyph has no classic pixel (a scaled copy sampled something else there).</summary>
        public bool IsHollow(int cell) =>
            _columnCells is not null && (_columnCells[cell % FootprintWidth] == NoCell || _rowCells![cell / FootprintWidth] == NoCell);

        /// <summary>The glyph byte a footprint cell shows (not for hollow cells).</summary>
        private byte Value(int cell) =>
            _font.GetGlyph(Character)[_columnCells is null ? cell : _rowCells![cell / FootprintWidth] * Width + _columnCells[cell % FootprintWidth]];

        public GlyphRecord MovedTo(SurfaceTrack track, short x, short y) =>
            new(_font, Character, Width, Height, Colour, Background, Under, FootprintWidth, FootprintHeight, _columnCells, _rowCells)
            {
                Track = track,
                X = x,
                Y = y,
                OriginX = OriginX + (x - X),
                OriginY = OriginY + (y - Y),
                ScaleX = ScaleX,
                ScaleY = ScaleY,
            };

        /// <summary>
        /// The glyph after a scaled copy: every destination pixel (inside the clip) whose covered
        /// source pixels meet its footprint; pixels that sample the footprint map to its cells,
        /// the others are hollow and hold what the copy wrote. Null when there is no such pixel.
        /// </summary>
        public GlyphRecord? ScaledTo(SurfaceTrack track, in ScaledCopy copy, byte[] destinationPixels)
        {
            int left = -1, right = -1;
            for (int x = copy.ClipLeft; x <= copy.ClipRight; x++)
            {
                var (start, end) = copy.CoveredColumns(x);
                if (end > X && start < X + FootprintWidth)
                {
                    if (left < 0)
                        left = x;
                    right = x;
                }
                else if (left >= 0)
                {
                    break;
                }
            }
            int top = -1, bottom = -1;
            for (int y = copy.ClipTop; y <= copy.ClipBottom; y++)
            {
                var (start, end) = copy.CoveredRows(y);
                if (end > Y && start < Y + FootprintHeight)
                {
                    if (top < 0)
                        top = y;
                    bottom = y;
                }
                else if (top >= 0)
                {
                    break;
                }
            }
            if (left < 0 || top < 0)
                return null;

            int width = right - left + 1, height = bottom - top + 1;
            var columns = new byte[width];
            var footprintColumns = new int[width];
            for (int i = 0; i < width; i++)
            {
                int column = copy.SourceX(left + i) - X;
                footprintColumns[i] = column;
                columns[i] = (uint)column >= (uint)FootprintWidth ? NoCell : _columnCells is null ? (byte)column : _columnCells[column];
            }
            var rows = new byte[height];
            var under = new byte[width * height];
            for (int j = 0; j < height; j++)
            {
                int row = copy.SourceY(top + j) - Y;
                rows[j] = (uint)row >= (uint)FootprintHeight ? NoCell : _rowCells is null ? (byte)row : _rowCells[row];
                for (int i = 0; i < width; i++)
                {
                    under[j * width + i] = rows[j] == NoCell || columns[i] == NoCell
                        ? destinationPixels[(top + j) * track.Width + left + i]
                        : Under[row * FootprintWidth + footprintColumns[i]];
                }
            }
            return new GlyphRecord(_font, Character, Width, Height, Colour, Background, under, width, height, columns, rows)
            {
                Track = track,
                X = (short)left,
                Y = (short)top,
                OriginX = copy.DestinationLeft + (OriginX - copy.SourceLeft) * copy.ScaleX,
                OriginY = copy.DestinationTop + (OriginY - copy.SourceTop) * copy.ScaleY,
                ScaleX = ScaleX * copy.ScaleX,
                ScaleY = ScaleY * copy.ScaleY,
            };
        }

        /// <summary>True when <paramref name="other"/> draws exactly the same pixels at the same place (unscaled glyphs only).</summary>
        public bool SameDraw(GlyphRecord other) =>
            !IsScaled && !other.IsScaled && ReferenceEquals(_font, other._font) && Character == other.Character
            && X == other.X && Y == other.Y && Colour == other.Colour && Background == other.Background;

        /// <summary>The value DrawFontGlyph writes for a glyph byte (0xFF = nothing).</summary>
        private byte Written(byte value)
        {
            if (_translate)
            {
                if (value == _font.BackgroundIndex)
                    return Background;
                if (value == _font.InkIndex)
                    return Colour;
            }
            return value;
        }

        private bool IsBackground(byte value) => value == _font.BackgroundIndex;

        /// <summary>The background value written around the foreground, or 0xFF when the background is transparent.</summary>
        private byte BackgroundValue => _translate ? Background : _font.BackgroundIndex;

        public bool HasForeground(ReadOnlySpan<byte> glyph)
        {
            foreach (byte value in glyph)
            {
                if (IsForegroundValue(value))
                    return true;
            }
            return false;
        }

        private bool IsForegroundValue(byte value) => value != 0xFF && !IsBackground(value) && Written(value) != 0xFF;

        public bool IsForeground(int cell) => !IsHollow(cell) && IsForegroundValue(Value(cell));

        /// <summary>True when the glyph writes the pixel at <paramref name="cell"/> (foreground or opaque background).</summary>
        public bool Writes(int cell) => !IsHollow(cell) && Written(Value(cell)) != 0xFF;

        /// <summary>The pixel the glyph left at <paramref name="cell"/>.</summary>
        public byte Expected(int cell)
        {
            if (IsHollow(cell))
                return Under[cell];
            byte written = Written(Value(cell));
            return written == 0xFF ? Under[cell] : written;
        }

        /// <summary>
        /// The pixel at <paramref name="cell"/> without this glyph's foreground; <paramref name="covers"/>
        /// tells whether it hides everything below (background or foreground on an opaque background).
        /// </summary>
        public byte Back(int cell, out bool covers)
        {
            if (IsHollow(cell))
            {
                covers = false;
                return Under[cell];
            }
            byte value = Value(cell);
            byte written = Written(value);
            if (written == 0xFF)
            {
                covers = false;
                return Under[cell];
            }
            if (IsBackground(value))
            {
                covers = true;
                return written;
            }
            byte background = BackgroundValue;
            covers = background != 0xFF;
            return covers ? background : Under[cell];
        }
    }
}
