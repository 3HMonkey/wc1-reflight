using WingCommander.Core.Resources;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens;

/// <summary>
/// The DOS startup intro: the Origin FX orchestra, the conductor's cue, the push past the
/// orchestra, the Wing Commander logo rising over the planet and the fireworks. Drawn into a
/// 320x128 buffer that is copied to screen rows 24..151 every frame (16 fps cinematic rate).
/// Any key or button skips the rest. With sequenced music the stages wait for the music's cue
/// points; without it they run on fixed frame counts.
/// </summary>
/// <remarks>C: SdlPlayDosStartupIntro (sdl/dos_intro.c), the SDL restoration of the DOS VROOMM
/// overlay that Kilrathi Saga omitted.</remarks>
public sealed class DosIntro
{
    private const int ActorCount = 10;
    private const int TitleSectionCount = 12;
    private const int FirstTitleSection = 6;
    private const int PlanetSection = 3;
    private const int FireworkSlotCount = 30;
    private const int InitialFireworkSlots = 5;
    private const int FireworkFrames = 8;
    private const byte Black = PaletteColours.Black;

    /// <summary>Position, push-in velocity and 32-frame animation of each orchestra section.</summary>
    /// <remarks>C: g_aSdlDosIntroActors (coordinates and strings from the DOS overlay).</remarks>
    private static readonly Actor[] Actors =
    [
        new(58, 94, -1, 1, "abcdefghijkaakkkkaaaalllllllmmll"),
        new(186, 94, 0, 1, "aaaaaaaabcddeeddccffgghhgghhiiih"),
        new(278, 94, 1, 1, "aaaaaaaaaabbccbbaaccccccccccaccc"),
        new(58, 102, -3, 2, "aaaabbbcccddeeddccffggaaggffcaaa"),
        new(186, 102, 0, 2, "aabbbaacccddddddccddeeeeeeddccca"),
        new(278, 102, 3, 2, "aaaaaabbbaaabbaaaabbbbaabbbbaccc"),
        new(58, 110, -5, 3, "aabbcdbbbeaaaaaaeeaaaaaaaaaabaaa"),
        new(186, 110, 0, 3, "abbbaacccdeeaaeeddaaaaaaaaaadacc"),
        new(278, 110, 5, 3, "aaaaaaaaabbbccbbbbccddddddccaaaa"),
        new(158, 74, 0, 4, "abccdefgghiijjiihhkkllmmllkkhnnn"),
    ];

    /// <remarks>C: g_szSdlDosIntroConductorFrames.</remarks>
    private const string ConductorFrames = "opoqopoqopoqopoqqrstrq";

    private readonly Wc1Game _game;
    private readonly ShapeTable[] _title = new ShapeTable[TitleSectionCount];
    private readonly ShapeTable _planet;
    private readonly Viewport _buffer;
    private readonly Viewport _destination;
    private readonly Firework[] _fireworks = new Firework[FireworkSlotCount];

    private DosIntro(Wc1Game game)
    {
        _game = game;
        for (int i = 0; i < TitleSectionCount; i++)
            _title[i] = game.Resources.GetShape(LogicalFile.TitleVga, FirstTitleSection + i);
        _planet = game.Resources.GetShape(LogicalFile.TitleVga, PlanetSection);
        _buffer = Viewport.Allocate(0, 0, 319, 127, Black);

        // WC.EXE 13be:064b clips the displayed viewport to screen rows 24..151.
        _destination = game.Graphics.Screen!.Clone();
        _destination.Top = 24;
        _destination.Bottom = 151;
    }

    /// <summary>Section 6: sky (frame 0) and the three logo pieces (frames 1..3).</summary>
    private ShapeTable Sky => _title[0];

    /// <summary>Section 17: 3 firework variants of 8 frames.</summary>
    private ShapeTable Fireworks => _title[11];

    private int MusicPosition => _game.IntroMusic?.SequencePosition ?? -1;

    /// <summary>Plays the intro (DOS data only).</summary>
    public static async Task PlayAsync(Wc1Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!game.Directory.IsDosData)
            return;
        await new DosIntro(game).RunAsync();
    }

    private async Task RunAsync()
    {
        var gfx = _game.Graphics;
        var events = _game.Events;
        var random = _game.Random;

        gfx.DrawFilledViewportRect(gfx.Screen!, 0, 0, 319, 199, Black);
        events.ClearInputKeyStatePreservingModifiers();
        events.FlushInputEvents();
        bool introMusic = _game.IntroMusic?.Begin() ?? false;
        bool synchronized = introMusic && MusicPosition >= 0;

        // 1. The orchestra plays.
        bool running = await DrawOrchestraAsync(0);
        if (synchronized)
        {
            int direction = 1;
            int frame = 0;
            int minimum = 0;
            int position = MusicPosition;
            while (running && position >= 0 && position < 1)
            {
                running = await DrawOrchestraAsync(frame);
                if (direction > 0)
                {
                    frame++;
                    if (frame == 32)
                    {
                        minimum = random.InRange(0, 13) + 9;
                        frame = 31;
                        direction = -1;
                    }
                }
                else
                {
                    frame--;
                    if (frame < minimum)
                    {
                        frame = minimum;
                        direction = 1;
                    }
                }
                position = MusicPosition;
            }
            if (position < 0)
                synchronized = false;
        }
        if (!synchronized)
        {
            for (int frame = 0; running && frame < 32; frame++)
                running = await DrawOrchestraAsync(frame);
            for (int frame = 31; running && frame >= 12; frame--)
                running = await DrawOrchestraAsync(frame);
            for (int frame = 12; running && frame < 32; frame++)
                running = await DrawOrchestraAsync(frame);
        }

        // 2. The conductor gives the cue.
        for (int cue = 0; running && cue < 20; cue++)
            running = await DrawConductorCueAsync(cue);
        if (running && synchronized)
        {
            (running, int position) = await WaitForMusicPositionAsync(2);
            if (position < 0)
                synchronized = false;
            else if (running)
                running = await DrawConductorCueAsync(20);
        }

        // 3. The camera pushes past the orchestra.
        for (int distance = 1; running && distance < 120; distance += distance / 4 + 1)
            running = await DrawOrchestraPushAsync(distance);

        // 4. The logo rises over the planet.
        short logoY = 59;
        for (int distance = 5000; running && distance >= 1000; distance -= 100)
        {
            logoY = (short)(distance > 3000 ? logoY - 2 : logoY + 2);
            running = await DrawLogoRevealAsync(logoY, distance);
        }
        if (running && synchronized)
        {
            (running, int position) = await WaitForMusicPositionAsync(3);
            if (position < 0)
                synchronized = false;
        }

        // 5. Fireworks, then a final burst.
        for (int i = 0; i < _fireworks.Length; i++)
            _fireworks[i].Frame = -1;
        int fireworkFrame = 0;
        bool finishing = false;
        while (running)
        {
            int position = synchronized ? MusicPosition : -1;
            if (synchronized && position < 0)
                synchronized = false;
            if (!finishing && (!synchronized || position >= 4 || random.InRange(0, 5) == 0))
                StartFirework(InitialFireworkSlots);
            running = await DrawFireworksAsync(logoY);
            fireworkFrame++;
            if (synchronized)
            {
                if (MusicPosition >= 5)
                    finishing = true;
            }
            else if (fireworkFrame > 10)
            {
                finishing = true;
            }
            if (finishing && AllIdle(InitialFireworkSlots))
                break;
        }
        if (running)
        {
            for (int i = 0; i < _fireworks.Length; i++)
                Launch(ref _fireworks[i]);
            for (int frame = 0; running && frame < FireworkFrames; frame++)
                running = await DrawFireworksAsync(logoY);
        }

        if (introMusic)
            _game.IntroMusic!.End();
        await _game.Display.ClearViewportAsync(gfx.Screen!, Black);
        events.ClearInputKeyStatePreservingModifiers();
        events.FlushInputEvents();
    }

    /// <summary>Waits in 1 ms polls until the music reaches <paramref name="target"/>; a key or button aborts.</summary>
    private async Task<(bool Running, int Position)> WaitForMusicPositionAsync(int target)
    {
        int position = MusicPosition;
        while (position >= 0 && position < target)
        {
            if (_game.Events.CheckEscaped() != 0)
                return (false, position);
            await _game.Scheduler.Delay(1);
            position = MusicPosition;
        }
        return (true, position);
    }

    /// <remarks>C: SdlDrawDosIntroSky.</remarks>
    private void DrawSky(int planetY)
    {
        var gfx = _game.Graphics;
        gfx.ClearViewport(_buffer, Black);
        gfx.DrawSpriteDefault(_buffer, 0, 0, Sky, 0);
        gfx.DrawSpriteDefault(_buffer, 160, planetY, _planet, 0);
    }

    /// <summary>The three logo pieces around x 162 (frame 2 is 118 px wide, origin 57 px from its left).</summary>
    /// <remarks>C: SdlDrawDosIntroLogo.</remarks>
    private void DrawLogo(short y, short scale)
    {
        var gfx = _game.Graphics;
        short left = (short)(161 - 57 * scale / 0x100);
        short right = (short)(162 + 61 * scale / 0x100);
        gfx.DrawSpriteScaled(_buffer, left, y, Sky, 1, 0, scale, 0);
        gfx.DrawSpriteScaled(_buffer, 162, y, Sky, 2, 0, scale, 0);
        gfx.DrawSpriteScaled(_buffer, right, y, Sky, 3, 0, scale, 0);
    }

    /// <summary>Copies the buffer to screen rows 24..151, presents and checks for a skip.</summary>
    /// <remarks>C: SdlPresentDosIntroFrame.</remarks>
    private async Task<bool> PresentAsync()
    {
        _game.Graphics.CopyViewportContents(_buffer, _destination);
        await _game.Display.PresentAsync();
        return _game.Events.CheckEscaped() == 0;
    }

    /// <remarks>C: SdlDrawDosIntroOrchestra.</remarks>
    private Task<bool> DrawOrchestraAsync(int sequenceFrame)
    {
        DrawSky(24);
        for (int i = 0; i < ActorCount; i++)
        {
            var actor = Actors[i];
            _game.Graphics.DrawSpriteDefault(_buffer, actor.X, actor.Y, _title[i + 1], actor.Frames[sequenceFrame] - 'a');
        }
        return PresentAsync();
    }

    /// <remarks>C: SdlDrawDosIntroConductorCue.</remarks>
    private Task<bool> DrawConductorCueAsync(int cueFrame)
    {
        DrawSky(24);
        for (int i = 0; i < ActorCount - 1; i++)
        {
            var actor = Actors[i];
            _game.Graphics.DrawSpriteDefault(_buffer, actor.X, actor.Y, _title[i + 1], actor.Frames[31] - 'a');
        }
        var conductor = Actors[ActorCount - 1];
        _game.Graphics.DrawSpriteDefault(_buffer, conductor.X, conductor.Y, _title[ActorCount], ConductorFrames[cueFrame] - 'a');
        return PresentAsync();
    }

    /// <remarks>C: SdlDrawDosIntroOrchestraPush.</remarks>
    private Task<bool> DrawOrchestraPushAsync(int distance)
    {
        DrawSky(24);
        for (int i = 0; i < ActorCount; i++)
        {
            var actor = Actors[i];
            int frame = i == ActorCount - 1 ? ConductorFrames[21] - 'a' : actor.Frames[31] - 'a';
            int x = actor.X + actor.VelocityX * distance;
            int y = actor.Y + actor.VelocityY * distance;
            int scale = 0x100 + actor.VelocityY * distance * 4;
            _game.Graphics.DrawSpriteTransformed(_buffer, x, y, _title[i + 1], frame, 0, scale, scale, 0, 0);
        }
        return PresentAsync();
    }

    /// <summary>The planet is drawn behind the logo while the logo is entirely above it.</summary>
    /// <remarks>C: SdlDrawDosIntroLogoReveal.</remarks>
    private Task<bool> DrawLogoRevealAsync(short logoY, int distance)
    {
        var gfx = _game.Graphics;
        int planetY = 120000 / distance;
        int scale = 256000 / distance;
        int logoBottom = logoY + 34 * scale / 0x100;
        gfx.ClearViewport(_buffer, Black);
        gfx.DrawSpriteDefault(_buffer, 0, 0, Sky, 0);
        if (logoBottom < planetY)
        {
            gfx.DrawSpriteDefault(_buffer, 160, planetY, _planet, 0);
            DrawLogo(logoY, (short)scale);
        }
        else
        {
            DrawLogo(logoY, (short)scale);
            gfx.DrawSpriteDefault(_buffer, 160, planetY, _planet, 0);
        }
        return PresentAsync();
    }

    /// <remarks>C: SdlDrawDosIntroFireworks.</remarks>
    private Task<bool> DrawFireworksAsync(short logoY)
    {
        var gfx = _game.Graphics;
        gfx.ClearViewport(_buffer, Black);
        gfx.DrawSpriteDefault(_buffer, 0, 0, Sky, 0);
        DrawLogo(logoY, 0x100);
        for (int i = 0; i < _fireworks.Length; i++)
        {
            ref var firework = ref _fireworks[i];
            if (firework.Frame < 0)
                continue;
            gfx.DrawSpriteDefault(_buffer, firework.X, firework.Y, Fireworks, firework.Frame + firework.Variant * FireworkFrames);
            firework.Frame++;
            if (firework.Frame == FireworkFrames)
                firework.Frame = -1;
        }
        return PresentAsync();
    }

    /// <summary>Starts a firework in the first idle slot among the first <paramref name="slots"/>.</summary>
    /// <remarks>C: SdlStartDosIntroFirework.</remarks>
    private void StartFirework(int slots)
    {
        for (int i = 0; i < slots; i++)
        {
            if (_fireworks[i].Frame == -1)
            {
                Launch(ref _fireworks[i]);
                return;
            }
        }
    }

    private void Launch(ref Firework firework)
    {
        var random = _game.Random;
        firework.Frame = 0;
        firework.X = random.InRange(0, 319);
        firework.Y = random.InRange(0, 127);
        firework.Variant = random.InRange(0, 2);
    }

    private bool AllIdle(int slots)
    {
        for (int i = 0; i < slots; i++)
        {
            if (_fireworks[i].Frame != -1)
                return false;
        }
        return true;
    }

    private readonly record struct Actor(short X, short Y, short VelocityX, short VelocityY, string Frames);

    private struct Firework
    {
        public short Frame;
        public short X;
        public short Y;
        public short Variant;
    }
}
