using System.Diagnostics.CodeAnalysis;

namespace WingCommander.Core.Rendering;

/// <summary>
/// Decoded shape frames shared between the Game (producer: decodes each frame once, on first use,
/// with Graphics' <c>ShapeFrameDecoder</c>) and the renderer (consumer: uploads each frame once into
/// its sprite atlas). Long-lived; single-threaded like the rest of the game loop.
/// </summary>
public sealed class SpriteImageCache
{
    private readonly Dictionary<SpriteImageKey, SpriteImage> _images = [];

    /// <summary>
    /// Changes whenever images are removed or replaced; renderers then forget what they uploaded.
    /// Adding new images does not change it.
    /// </summary>
    public int Generation { get; private set; }

    public int Count => _images.Count;

    public bool TryGet(SpriteImageKey key, [NotNullWhen(true)] out SpriteImage? image) => _images.TryGetValue(key, out image);

    public bool Contains(SpriteImageKey key) => _images.ContainsKey(key);

    /// <summary>Adds or replaces the image of <paramref name="key"/>.</summary>
    public void Set(SpriteImageKey key, SpriteImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (_images.TryGetValue(key, out var existing))
        {
            if (ReferenceEquals(existing, image))
                return;
            Generation++;
        }
        _images[key] = image;
    }

    /// <summary>Removes every image (new mission, resources reloaded).</summary>
    public void Clear()
    {
        _images.Clear();
        Generation++;
    }
}
