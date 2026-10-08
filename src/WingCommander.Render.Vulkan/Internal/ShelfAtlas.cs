namespace WingCommander.Render.Vulkan.Internal;

/// <summary>Where an uploaded image lives in an atlas (sprites: frame size and hot spot; glyphs: field size, no origin).</summary>
internal readonly record struct AtlasEntry(int X, int Y, int Width, int Height, int OriginX, int OriginY);

/// <summary>
/// Shelf packer for one square atlas image (the sprite atlas, keyed by <c>SpriteImageKey</c>, and
/// the glyph atlas, keyed by <c>GlyphImage</c> identity): images are placed left to right in rows
/// ("shelves") with a 1 texel gap; when nothing fits any more the caller resets it and re-uploads
/// what the current frame needs. Lookups do not allocate.
/// </summary>
internal sealed class ShelfAtlas<TKey>(int size)
    where TKey : notnull
{
    private const int Gap = 1;
    private readonly Dictionary<TKey, AtlasEntry> _entries = new(256);
    private int _shelfX;
    private int _shelfY;
    private int _shelfHeight;

    public int Size { get; } = size;

    public int Count => _entries.Count;

    public bool TryGet(TKey key, out AtlasEntry entry) => _entries.TryGetValue(key, out entry);

    /// <summary>Reserves a <paramref name="width"/> x <paramref name="height"/> area; false when the atlas is full.</summary>
    public bool TryAllocate(int width, int height, out int x, out int y)
    {
        x = y = 0;
        if (width <= 0 || height <= 0 || width > Size || height > Size)
            return false;
        if (_shelfX + width > Size)
        {
            _shelfY += _shelfHeight;
            _shelfX = 0;
            _shelfHeight = 0;
        }
        if (_shelfY + height > Size)
            return false;
        x = _shelfX;
        y = _shelfY;
        _shelfX += width + Gap;
        _shelfHeight = Math.Max(_shelfHeight, height + Gap);
        return true;
    }

    public void Add(TKey key, AtlasEntry entry) => _entries[key] = entry;

    /// <summary>Forgets every image (the atlas texels are simply overwritten by later uploads).</summary>
    public void Reset()
    {
        _entries.Clear();
        _shelfX = 0;
        _shelfY = 0;
        _shelfHeight = 0;
    }
}
