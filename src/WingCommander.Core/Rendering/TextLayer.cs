using WingCommander.Core.Video;

namespace WingCommander.Core.Rendering;

/// <summary>
/// One glyph of the game's text that a renderer draws at output resolution: the top-left corner
/// of the glyph cell on the 320x200 screen, the glyph, the palette index that replaces the
/// glyph's ink (<see cref="GlyphImage.InkIndex"/>) and the cell's scale (1 for text drawn
/// directly; text the game copied smaller or larger, like the briefing board, has other scales
/// and fractional positions).
/// </summary>
public readonly record struct GlyphInstance(float X, float Y, GlyphKey Glyph, byte Colour, float ScaleX = 1f, float ScaleY = 1f);

/// <summary>
/// The game's font text for renderers that draw it at output resolution (ADR-013), published by
/// the game with every present. The game still draws all text into the classic frame; this layer
/// tells the renderer which glyphs are intact on screen, gives it the frame without their
/// foreground (<see cref="Pixels"/>), and says per pixel which glyphs may draw there
/// (<see cref="Mask"/>), so everything drawn over text later - menus, the cursor, cockpit art -
/// stays on top exactly as in the classic frame.
/// </summary>
/// <remarks>
/// <para>Renderer contract: while <see cref="RenderFrame.Text"/> is set, show <see cref="Pixels"/>
/// instead of <see cref="ClassicLayer.Pixels"/> (same live palette), then draw the instances in
/// list order (painter order: later = on top) as quads covering their glyph cell (width and
/// height times the instance's scale). Instance
/// <c>i</c> may colour pixel <c>p</c> only when <c>Mask[p] != 0 &amp;&amp; i &gt;= Mask[p] - 1</c>.
/// Colours are palette indices looked up in the live palette, so fades apply.</para>
/// <para>Renderers without text support ignore the layer and show the classic pixels.</para>
/// </remarks>
public sealed class TextLayer
{
    private GlyphInstance[] _instances = new GlyphInstance[256];

    public TextLayer(GlyphImageCache glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        Glyphs = glyphs;
    }

    /// <summary>Images of every glyph the instances refer to (long-lived, shared across frames).</summary>
    public GlyphImageCache Glyphs { get; }

    /// <summary>The presented frame without the foreground of the listed glyphs.</summary>
    public Framebuffer Pixels { get; } = new();

    /// <summary>
    /// Per screen pixel (row-major 320x200): 0 = no glyph draws here; n &gt; 0 = instances with list
    /// index &gt;= n - 1 whose cell covers the pixel draw here.
    /// </summary>
    public ushort[] Mask { get; } = new ushort[Framebuffer.PixelCount];

    public ReadOnlySpan<GlyphInstance> Instances => _instances.AsSpan(0, Count);

    public int Count { get; private set; }

    /// <summary>Changes with every update of pixels, mask and instances (renderers re-upload then).</summary>
    public int Version { get; private set; }

    /// <summary>Producer: starts a new frame (no instances).</summary>
    public void Clear() => Count = 0;

    /// <summary>Producer: appends an instance and returns its list index.</summary>
    public int Add(in GlyphInstance instance)
    {
        if (Count == _instances.Length)
            Array.Resize(ref _instances, _instances.Length * 2);
        _instances[Count] = instance;
        return Count++;
    }

    /// <summary>Producer: the pixels, mask and instances of a new frame are complete.</summary>
    public void Publish() => Version++;
}
