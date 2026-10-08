namespace WingCommander.Core.Rendering;

/// <summary>How <see cref="KeyHelpLayout"/> placed the key help.</summary>
public enum KeyHelpPlacement
{
    /// <summary>Not shown (hidden, empty, or no room at all).</summary>
    None,

    /// <summary>Two columns in the black side margins next to the picture; nothing is covered.</summary>
    Margins,

    /// <summary>A translucent panel over the picture (the window has no usable margins).</summary>
    Panel,
}

/// <summary>
/// Lays out a <see cref="KeyHelpOverlay"/> for one render target (shared by all renderers, pure
/// arithmetic). The text uses the overlay font's glyph advances; the scale is chosen so that the
/// help fits, never larger than the game's own pixel scale.
/// </summary>
public static class KeyHelpLayout
{
    /// <summary>Smallest text scale (output pixels per font pixel) that is still readable.</summary>
    public const float MinimumScale = 1.25f;

    public static readonly uint TitleColour = OverlayColour.Rgba(255, 216, 96);
    public static readonly uint HeadingColour = OverlayColour.Rgba(232, 160, 56);
    public static readonly uint KeyColour = OverlayColour.Rgba(255, 255, 255);
    public static readonly uint ActionColour = OverlayColour.Rgba(124, 224, 124);
    public static readonly uint PanelColour = OverlayColour.Rgba(0, 0, 0, 184);

    private const float KeyGap = 6f;        // font pixels between the key and the action column
    private const float LineSpacing = 2f;   // font pixels between lines
    private const float SectionGap = 5f;    // font pixels before a heading

    /// <summary>
    /// Fills <paramref name="list"/> (cleared first) with the overlay laid out for a
    /// <paramref name="targetWidth"/> x <paramref name="targetHeight"/> target whose classic
    /// picture covers <paramref name="picture"/>. Margins are used when both side margins can
    /// hold their column at <see cref="MinimumScale"/> or more; otherwise a panel is drawn over
    /// the picture.
    /// </summary>
    public static KeyHelpPlacement Build(KeyHelpOverlay overlay, int targetWidth, int targetHeight, PresentationRect picture,
        OverlayDrawList list)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(list);
        list.Clear();
        list.Glyphs = overlay.Glyphs;
        if (!overlay.Visible || overlay.Sections.Count == 0 || targetWidth <= 0 || targetHeight <= 0 || picture.IsEmpty)
            return KeyHelpPlacement.None;

        var metrics = new Metrics(overlay);
        int split = BalancedSplit(overlay);
        var left = new Block(overlay, metrics, 0, split, withTitle: true);
        var right = new Block(overlay, metrics, split, overlay.Sections.Count, withTitle: false);
        float pixelScale = picture.Height / 200f; // the game's own magnification
        float maximumScale = MathF.Max(MinimumScale, pixelScale);

        // Margins: each column in its own margin.
        float leftMargin = picture.X;
        float rightMargin = targetWidth - (picture.X + picture.Width);
        float pad = MathF.Max(8f, MathF.Min(leftMargin, rightMargin) * 0.06f);
        float marginScale = MathF.Min(
            MathF.Min((leftMargin - 2 * pad) / left.Width, (rightMargin - 2 * pad) / right.Width),
            (targetHeight - 2 * pad) / MathF.Max(left.Height, right.Height));
        if (marginScale >= MinimumScale)
        {
            float scale = MathF.Min(marginScale, maximumScale);
            left.Emit(list, (leftMargin - left.Width * scale) / 2f, (targetHeight - left.Height * scale) / 2f, scale);
            right.Emit(list, picture.X + picture.Width + (rightMargin - right.Width * scale) / 2f,
                (targetHeight - right.Height * scale) / 2f, scale);
            return KeyHelpPlacement.Margins;
        }

        // Panel: both columns side by side over the picture.
        float gap = 12f;
        float contentWidth = left.Width + gap + right.Width;
        float contentHeight = MathF.Max(left.Height, right.Height);
        float border = 6f;
        float panelScale = MathF.Min(
            (picture.Width * 0.94f) / (contentWidth + 2 * border),
            (picture.Height * 0.94f) / (contentHeight + 2 * border));
        if (panelScale <= 0f)
            return KeyHelpPlacement.None;
        float s = MathF.Min(panelScale, maximumScale);
        float panelWidth = (contentWidth + 2 * border) * s;
        float panelHeight = (contentHeight + 2 * border) * s;
        float panelX = MathF.Round(picture.X + (picture.Width - panelWidth) / 2f);
        float panelY = MathF.Round(picture.Y + (picture.Height - panelHeight) / 2f);
        list.AddRectangle(panelX, panelY, MathF.Round(panelWidth), MathF.Round(panelHeight), PanelColour);
        left.Emit(list, panelX + border * s, panelY + border * s, s);
        right.Emit(list, panelX + (border + left.Width + gap) * s, panelY + border * s, s);
        return KeyHelpPlacement.Panel;
    }

    /// <summary>Index of the first section of the right column, balancing the line counts.</summary>
    private static int BalancedSplit(KeyHelpOverlay overlay)
    {
        var sections = overlay.Sections;
        int total = 1; // title
        for (int i = 0; i < sections.Count; i++)
            total += 1 + sections[i].Entries.Count;
        int best = sections.Count, bestCost = int.MaxValue, leftLines = 1;
        for (int split = 0; split <= sections.Count; split++)
        {
            int cost = Math.Max(leftLines, total - leftLines);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = split;
            }
            if (split < sections.Count)
                leftLines += 1 + sections[split].Entries.Count;
        }
        return Math.Clamp(best, 1, Math.Max(1, sections.Count - 1));
    }

    /// <summary>Glyph advances of the overlay font.</summary>
    private readonly struct Metrics(KeyHelpOverlay overlay)
    {
        private readonly GlyphImageCache _glyphs = overlay.Glyphs;
        private readonly byte _font = overlay.Font;

        public float LineHeight => FontHeight + LineSpacing;

        public float FontHeight => _glyphs.TryGet(new GlyphKey(_font, (byte)'A'), out var a) ? a.Height : 8f;

        public float Measure(string text)
        {
            float width = 0f;
            foreach (char c in text)
                width += Advance(c);
            return width;
        }

        public float Advance(char c) =>
            _glyphs.TryGet(new GlyphKey(_font, Code(c)), out var image) ? image.Advance : 0f;

        public GlyphImage? Image(char c) =>
            _glyphs.TryGet(new GlyphKey(_font, Code(c)), out var image) ? image : null;

        public GlyphKey Key(char c) => new(_font, Code(c));

        private static byte Code(char c) => c <= 0xFF ? (byte)c : (byte)'?';
    }

    /// <summary>One column: optional title, then headings with their key/action lines (font pixels).</summary>
    private readonly struct Block
    {
        private readonly KeyHelpOverlay _overlay;
        private readonly Metrics _metrics;
        private readonly int _first;
        private readonly int _end;
        private readonly bool _withTitle;
        private readonly float _keyColumn;

        public Block(KeyHelpOverlay overlay, Metrics metrics, int first, int end, bool withTitle)
        {
            _overlay = overlay;
            _metrics = metrics;
            _first = first;
            _end = end;
            _withTitle = withTitle;
            float keys = 0f, width = withTitle ? metrics.Measure(overlay.Title) : 0f;
            float height = withTitle ? metrics.LineHeight : 0f;
            for (int i = first; i < end; i++)
            {
                var section = overlay.Sections[i];
                width = MathF.Max(width, metrics.Measure(section.Heading));
                height += (height > 0f ? SectionGap : 0f) + metrics.LineHeight;
                for (int e = 0; e < section.Entries.Count; e++)
                {
                    keys = MathF.Max(keys, metrics.Measure(section.Entries[e].Keys));
                    height += metrics.LineHeight;
                }
            }
            for (int i = first; i < end; i++)
            {
                var entries = overlay.Sections[i].Entries;
                for (int e = 0; e < entries.Count; e++)
                    width = MathF.Max(width, keys + KeyGap + metrics.Measure(entries[e].Action));
            }
            _keyColumn = keys;
            Width = MathF.Max(width, 1f);
            Height = MathF.Max(height - LineSpacing, 1f);
        }

        public float Width { get; }

        public float Height { get; }

        public void Emit(OverlayDrawList list, float x, float y, float scale)
        {
            x = MathF.Round(x);
            float line = 0f;
            if (_withTitle)
            {
                Text(list, _overlay.Title, x, y + line * scale, scale, TitleColour);
                line += _metrics.LineHeight;
            }
            for (int i = _first; i < _end; i++)
            {
                var section = _overlay.Sections[i];
                if (line > 0f)
                    line += SectionGap;
                Text(list, section.Heading, x, y + line * scale, scale, HeadingColour);
                line += _metrics.LineHeight;
                for (int e = 0; e < section.Entries.Count; e++)
                {
                    KeyHelpEntry entry = section.Entries[e];
                    Text(list, entry.Keys, x, y + line * scale, scale, KeyColour);
                    Text(list, entry.Action, x + (_keyColumn + KeyGap) * scale, y + line * scale, scale, ActionColour);
                    line += _metrics.LineHeight;
                }
            }
        }

        private void Text(OverlayDrawList list, string text, float x, float y, float scale, uint colour)
        {
            y = MathF.Round(y);
            foreach (char c in text)
            {
                if (_metrics.Image(c) is { HasForeground: true } image)
                    list.AddGlyph(x, y, image.Width * scale, image.Height * scale, _metrics.Key(c), colour);
                x += _metrics.Advance(c) * scale;
            }
        }
    }
}
