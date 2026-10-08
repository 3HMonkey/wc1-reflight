using WingCommander.Core.Numerics;
using WingCommander.Core.Resources;
using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Scenes;

/// <summary>
/// The 2D star field seen through windows behind conversation backdrops and in the funeral: static
/// stars plus horizontally drifting particles (PLANETS.VGA section 0 sprites) over the space colour.
/// The conversations call <c>init_constellation(0)</c>, which only loads the star sprites (no 3D
/// planet objects), so no 3D rendering is involved.
/// </summary>
/// <remarks>C: init_constellation (0x4243E0) / free_constellation (0x424490), logic.c, for scene 0;
/// InitializeConstellationField (0x42D390) and DrawConstellationField (0x42D500), music.c;
/// pConstellationShape, pConstellationViewport, nConstellationDirection, aConstellationStars,
/// aConstellationParticles, asConstellationVelocity, asConstellationFrame.</remarks>
public sealed class ConstellationField
{
    /// <remarks>C: asConstellationVelocity (0x0046A8D8).</remarks>
    private static ReadOnlySpan<short> Velocities => [8, 8, 7, 7, 6, 6, 5, 5, 4, 4, 3, 3, 2, 2, 1, 1];

    /// <remarks>C: asConstellationFrame (0x0046A8F8).</remarks>
    private static ReadOnlySpan<short> Frames => [0, 16, 16, 0, 4, 4, 20, 20, 24, 8, 8, 24, 28, 12, 12, 28];

    private readonly GraphicsContext _graphics;
    private readonly CRandom _random;
    private readonly Star[] _stars = new Star[16];
    private readonly Particle[] _particles = new Particle[32];
    private Viewport _viewport = new();
    private short _direction = -1;
    private int _starCount;
    private int _particleCount;

    /// <param name="graphics">Raster library the field draws with.</param>
    /// <param name="random">The shared C rand() (consumed in the original order).</param>
    /// <param name="shape">PLANETS.VGA section 0 (pConstellationShape).</param>
    public ConstellationField(GraphicsContext graphics, CRandom random, ShapeTable? shape)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(random);
        _graphics = graphics;
        _random = random;
        Shape = shape;
    }

    /// <summary>The star sprites (PLANETS.VGA section 0).</summary>
    public ShapeTable? Shape { get; }

    /// <summary>The viewport the field draws into.</summary>
    public Viewport Viewport => _viewport;

    /// <summary>Loads the star sprites (the conversations' <c>init_constellation(0)</c>).</summary>
    public static ConstellationField Create(Wc1Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return new ConstellationField(game.Graphics, game.Random, game.Resources.GetShape(LogicalFile.PlanetsVga, 0));
    }

    /// <summary>
    /// Scatters <c>density * 10 / 16</c> stars and <c>density</c> particles over
    /// <paramref name="viewport"/>. Star positions are relative to (0, 0), not to the viewport
    /// (original behaviour; every conversation viewport starts at the origin). Consumes the shared
    /// random generator in the original order.
    /// </summary>
    /// <remarks>C: InitializeConstellationField (0x42D390, music.c). Like the original's
    /// pConstellationViewport the field keeps a reference to the viewport object, so a caller that
    /// passes the scene buffer itself sees later changes to it (a reallocated buffer).</remarks>
    public void Initialize(Viewport viewport, short direction, short density)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        _viewport = viewport;
        _direction = direction;
        short width = (short)(_viewport.Right - _viewport.Left);
        short height = (short)(_viewport.Bottom - _viewport.Top);
        _starCount = Math.Min(density * 10 / 16, _stars.Length);
        _particleCount = Math.Min(density * 16 / 16, _particles.Length);
        for (int i = 0; i < _starCount; i++)
        {
            _stars[i].X = _random.InRange(0, width);
            _stars[i].Y = _random.InRange(0, height);
            _stars[i].Frame = (short)(_random.InRange(0, 5) + 32);
        }
        for (int i = 0; i < _particleCount; i++)
        {
            short randomIndex = _random.InRange(0, 15);
            ref var particle = ref _particles[i];
            particle.X = (short)(_viewport.Left + _random.InRange(0, width));
            particle.Y = (short)(_viewport.Top + _random.InRange(0, height));
            particle.Velocity = (short)(Velocities[randomIndex] * _direction);
            particle.Frame = (short)(Frames[randomIndex] + _random.InRange(0, 3));
        }
    }

    /// <summary>
    /// Clears the field to the space colour, draws the stars and the particles, then moves the
    /// particles (cycling their 4-frame twinkle) and respawns those that left the field.
    /// </summary>
    /// <remarks>C: DrawConstellationField (0x42D500, music.c).</remarks>
    public void Draw()
    {
        short height = (short)(_viewport.Bottom - _viewport.Top);
        _graphics.ClearViewport(_viewport, PaletteColours.PrimaryViewBuffer);
        for (int i = 0; i < _starCount; i++)
            _graphics.DrawSpriteDefault(_viewport, _stars[i].X, _stars[i].Y, Shape, _stars[i].Frame);
        for (int i = 0; i < _particleCount; i++)
        {
            ref var particle = ref _particles[i];
            _graphics.DrawSpriteDefault(_viewport, particle.X, particle.Y, Shape, particle.Frame);
            particle.X = unchecked((short)(particle.X + particle.Velocity));
            particle.Frame = (short)((particle.Frame & 0xfc) + (particle.Frame + 1) % 4);
            if (_direction < 0)
            {
                if (particle.X < _viewport.Left)
                {
                    short randomIndex = _random.InRange(0, 15);
                    short speed = Velocities[randomIndex];
                    particle.X = (short)(_viewport.Right - _random.InRange(0, speed));
                    particle.Y = (short)(_viewport.Top + _random.InRange(0, height));
                    particle.Velocity = (short)-speed;
                }
            }
            else if (particle.X > _viewport.Right)
            {
                short randomIndex = _random.InRange(0, 15);
                short speed = Velocities[randomIndex];
                particle.Velocity = speed;
                particle.X = (short)(_viewport.Left + _random.InRange(0, speed));
                particle.Y = (short)(_viewport.Top + _random.InRange(0, height));
                particle.Frame = (short)(Frames[randomIndex] + _random.InRange(0, 3));
            }
        }
    }

    private struct Star
    {
        public short X;
        public short Y;
        public short Frame;
    }

    private struct Particle
    {
        public short X;
        public short Y;
        public short Velocity;
        public short Frame;
    }
}
