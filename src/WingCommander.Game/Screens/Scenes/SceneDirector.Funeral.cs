using WingCommander.Audio.Director;
using WingCommander.Core.Resources;
using WingCommander.Game.Scenes;
using WingCommander.Graphics.Palettes;

namespace WingCommander.Game.Screens.Scenes;

public sealed partial class SceneDirector
{
    /// <summary>Where the seven puffs of a rifle volley start (relative to the guard's base position).</summary>
    /// <remarks>C: aFuneralParticleOrigins (0x00465B18).</remarks>
    private static readonly (short X, short Y)[] FuneralParticleOrigins =
        [(234, 83), (248, 85), (260, 80), (273, 78), (286, 75), (299, 76), (310, 74)];

    /// <remarks>C: szFuneralTheEnd (0x00465C04).</remarks>
    private static ReadOnlySpan<byte> FuneralTheEnd => "THE END"u8;

    // C: nFuneralCasketX/Y, nFuneralForegroundX/Y, nFuneralMainDistance, nFuneralParticleDistance,
    // nFuneralGuardFrame, nFuneralRifleFrame, nFuneralBaseX/Y, aFuneralParticles, bFuneralShowTheEnd,
    // nFrameSkipCounter (shared by the funeral loops).
    private short _casketX;
    private short _casketY;
    private short _foregroundX;
    private short _foregroundY;
    private short _mainDistance = 1;
    private short _particleDistance = 1;
    private short _guardFrame;
    private short _rifleFrame;
    private short _baseX;
    private short _baseY;
    private readonly (short X, short Y)[] _funeralParticles = new (short X, short Y)[7];
    private bool _showTheEnd;
    private int _funeralFrameSkipCounter;

    /// <summary>True while a funeral runs.</summary>
    /// <remarks>C: nFuneralSequenceActive.</remarks>
    public bool FuneralSequenceActive { get; private set; }

    /// <summary>
    /// The funeral in space: the opening conversation, the honour guard's commands ("Company...",
    /// "Atten-SHUN!", "Prepare arms!"), the follow-up conversation, three volleys, then the casket
    /// drifts away until the music ends (160 frames without music). For the player's own funeral
    /// the opening is chosen by series and "THE END" appears after 110 frames. Esc ends it.
    /// </summary>
    /// <param name="playerFuneral">True for the player's funeral (after death), false for a wingman's.</param>
    /// <remarks>C: funeral_sequence (0x408DE0, brains.c).</remarks>
    public async Task FuneralSequenceAsync(bool playerFuneral)
    {
        var stage = Stage;
        stage.PreloadMusicTrack(MusicTrack.Funeral);
        FuneralSequenceActive = true;
        stage.SpaceTrack(MusicTrack.Funeral, 1, 0);
        var file = BriefingFile.Load(_game.Directory, Session.CampaignDataSet);
        _showTheEnd = false;
        ConversationScript opening, followUp;
        if (playerFuneral)
        {
            int series = Session.State.CurrentSeries;
            var bySeries = BriefingFile.FuneralSceneBySeries;
            int pair = (uint)series < (uint)bySeries.Length ? bySeries[series] : 0;
            opening = file.GetFuneral(1 + pair);
            followUp = file.GetFuneral(0);
            _introFont = Shape(LogicalFile.TitleVga, 1);
        }
        else
        {
            opening = file.GetFuneral(6);
            followUp = file.GetFuneral(5);
        }

        stage.InitializeConversationViewport();
        _casketX = 180;
        _casketY = 70;
        _foregroundX = 30;
        _mainDistance = 112;
        _particleDistance = 16;
        _guardFrame = 2;
        _rifleFrame = 4;
        _baseY = 0;
        _baseX = 0;
        _foregroundY = 0;
        for (int i = 0; i < _funeralParticles.Length; i++)
            _funeralParticles[i].X = 0;
        stage.InitializeConversationText();
        _constellation ??= ConstellationField.Create(_game);
        _special = Shape(LogicalFile.BriefingVga, 9);
        Gfx.ClearViewport(Scene, PaletteColours.PrimaryViewBuffer);
        _constellation.Initialize(Scene, -1, 16);
        Events.EscapePressed = false;
        Events.PumpWindowMessages();
        await RunAsync(SceneType.Funeral, opening);
        if (!Events.EscapePressed)
            await FuneralCeremonyAsync(playerFuneral, followUp);

        _introFont = null;
        _special = null;
        _constellation = null;
        stage.ResetScreenClipToFullHeight();
        await stage.FadeToColourAsync(Black);
        await stage.Display.ClearViewportAsync(stage.Screen, Black);
        await stage.RestoreGamePaletteAsync();
        Events.EscapePressed = false;
        Events.ClearInputKeyStatePreservingModifiers();
        Events.FlushInputEvents();
        FuneralSequenceActive = false;
        stage.StopMusicUnlessSuppressed();
        stage.Music?.FreeInflightMusic();
    }

    /// <summary>The fixed choreography between and after the two conversations of the funeral.</summary>
    /// <remarks>C: the body of funeral_sequence (0x408DE0, brains.c) after the opening SceneDirector call.</remarks>
    private async Task FuneralCeremonyAsync(bool playerFuneral, ConversationScript followUp)
    {
        Gfx.ClearViewport(Scene, PaletteColours.PrimaryViewBuffer);
        Stage.ClearSubtitle();
        if (!await FuneralFramesAsync(10))
            return;
        Gfx.FormatTextBufferFromStart("%X%Y%FCompany...%P"u8, 0, ConversationStage.SubtitleY, PaletteColours.Blue);
        bool running = await FuneralFramesAsync(15);
        Stage.ClearSubtitle();
        if (!running)
            return;
        Gfx.FormatTextBufferFromStart("%X%YAtten-SHUN!%P"u8, 0, ConversationStage.SubtitleY);
        if (!await FuneralFramesAsync(10, () => Stage.PlaySfx(SoundEffectNumber.Funeral)))
            return;
        _guardFrame = 3;
        if (!await FuneralFramesAsync(10))
            return;
        Stage.ClearSubtitle();
        _funeralFrameSkipCounter = 1;
        Gfx.FormatTextBufferFromStart("%X%YPrepare arms!%P"u8, 0, ConversationStage.SubtitleY);
        for (int frame = 10; frame != 0; frame--)
        {
            Events.PumpWindowMessages();
            await FuneralPlayerFrameAsync();
        }
        _rifleFrame = 5;
        if (!await FuneralFramesAsync(10, () => Stage.PlaySfx(SoundEffectNumber.Sound31)))
            return;

        await RunAsync(SceneType.Funeral, followUp);
        Gfx.ClearViewport(Scene, PaletteColours.PrimaryViewBuffer);
        if (Events.EscapePressed)
            return;
        for (int volley = 0; volley < 3; volley++)
        {
            Stage.ClearSubtitle();
            Gfx.FormatTextBufferFromStart("%X%Y%FFire!%P"u8, 0, ConversationStage.SubtitleY, PaletteColours.Blue);
            if (volley == 1)
                Stage.PlaySfx(SoundEffectNumber.FuneralVolley);
            _funeralFrameSkipCounter = 1;
            for (int frame = 0; frame < 10; frame++)
            {
                Events.PumpWindowMessages();
                await FuneralPlayerFrameAsync();
                if (volley > 0)
                    DriftCasket();
                if (Events.EscapePressed)
                    break;
            }
            Stage.ClearSubtitle();
            if (Events.EscapePressed)
                break;
            _particleDistance = 16;
            for (int particle = 0; particle < _funeralParticles.Length; particle++)
            {
                _funeralParticles[particle] = ((short)(FuneralParticleOrigins[particle].X + _baseX),
                    (short)(FuneralParticleOrigins[particle].Y + _baseY));
            }
            Stage.PlaySfx(0x1d);
            _funeralFrameSkipCounter = 1;
            for (int frame = 0; frame < 24; frame++)
            {
                Events.PumpWindowMessages();
                await FuneralPlayerFrameAsync();
                if (volley > 0)
                    DriftCasket();
                _particleDistance++;
                if (Events.EscapePressed)
                    break;
            }
            if (Events.EscapePressed)
                break;
        }
        if (Events.EscapePressed)
            return;

        int drift = 0;
        _funeralFrameSkipCounter = 1;
        Stage.Music?.SetMusBreakpt(0, 0);
        while (!Events.EscapePressed)
        {
            Events.PumpWindowMessages();
            await FuneralPlayerFrameAsync();
            _casketX--;
            if (_casketX % 2 == 0)
                _casketY--;
            drift++;
            _mainDistance++;
            _baseX++;
            _foregroundX += 2;
            _particleDistance++;
            if (drift == 110 && playerFuneral)
                _showTheEnd = true;
            var music = Stage.Music;
            if (music is null || !music.IsMusicEnabled || !music.WaitForMusicEnabled)
            {
                if (drift > 160)
                    break;
            }
            else if (music.GetMusicMode() != 0)
            {
                break;
            }
        }
    }

    /// <summary>Runs <paramref name="frames"/> funeral frames (calling <paramref name="onFirstFrame"/> after the first); false when Esc ended them.</summary>
    private async Task<bool> FuneralFramesAsync(int frames, Action? onFirstFrame = null)
    {
        _funeralFrameSkipCounter = 1;
        for (int frame = 0; frame < frames; frame++)
        {
            Events.PumpWindowMessages();
            await FuneralPlayerFrameAsync();
            if (frame == 0)
                onFirstFrame?.Invoke();
            if (Events.EscapePressed)
                return false;
        }
        return true;
    }

    /// <summary>The casket drifts left and up and recedes; the honour guard slides right once it passes x 160.</summary>
    private void DriftCasket()
    {
        _casketX--;
        if (_casketX % 2 == 0)
            _casketY--;
        _mainDistance++;
        if (_casketX < 160)
        {
            _foregroundX += 2;
            _baseX++;
        }
    }

    /// <summary>
    /// One funeral frame: star field, the deck, the casket (scaled by its distance), the honour guard
    /// and rifles, the rising smoke puffs of a volley, the foreground and, for the player's funeral,
    /// "THE END".
    /// </summary>
    /// <remarks>C: funeral_player (0x408B90, brains.c).</remarks>
    private async Task FuneralPlayerFrameAsync()
    {
        _funeralFrameSkipCounter--;
        if (_funeralFrameSkipCounter >= 1)
            return;
        _funeralFrameSkipCounter = FrameSkip;
        _constellation?.Draw();
        short mainScale = unchecked((short)(0x7000 / Math.Max((int)_mainDistance, 1)));
        Gfx.DrawSpriteDefault(Scene, _baseX, _baseY, _special, 0);
        Gfx.DrawSpriteScaled(Scene, _casketX, _casketY, _special, 8, 0, mainScale, 0);
        Gfx.DrawSpriteDefault(Scene, _baseX, _baseY, _special, 1);
        Gfx.DrawSpriteDefault(Scene, _baseX, _baseY, _special, _guardFrame);
        Gfx.DrawSpriteDefault(Scene, _baseX, _baseY, _special, _rifleFrame);
        short particleScale = unchecked((short)(0x1000 / Math.Max((int)_particleDistance, 1)));
        for (int i = 0; i < _funeralParticles.Length; i++)
        {
            ref var particle = ref _funeralParticles[i];
            if (particle.X == 0)
                continue;
            Gfx.DrawSpriteScaled(Scene, particle.X, particle.Y, _special, 9, 0, particleScale, 0);
            particle.X = (short)(particle.X - 6);
            particle.Y = (short)(particle.Y - 6);
            if (Scene.Top > particle.Y)
                particle.X = 0;
        }
        Gfx.DrawSpriteDefault(Scene, _foregroundX, _foregroundY, _special, 7);
        Gfx.DrawSpriteDefault(Scene, _foregroundX + 180, _foregroundY, _special, 6);
        if (_showTheEnd && _introFont is not null)
            Gfx.PrintSubtitle(Scene, _introFont, FuneralTheEnd);
        await Stage.RefreshAsync();
        await Stage.PresentAsync();
    }

    /// <summary>Shot 17: the funeral picture animates under the subtitle for the record's duration.</summary>
    /// <remarks>C: funeral_wingman (0x408D50, brains.c).</remarks>
    private async Task FuneralWingmanAsync(string text, short duration)
    {
        ShowText(text);
        _funeralFrameSkipCounter = 1;
        _game.Timing.SetFrameTimerPeriod(duration);
        while (!_game.Timing.IsFrameTickElapsed())
        {
            Events.PumpWindowMessages();
            await FuneralPlayerFrameAsync();
            if (Events.EscapePressed)
                break;
            if (Events.CheckEscaped() != 0)
                break;
        }
    }

    /// <summary>
    /// Shots 9 and 12..15 (the Colonel's office): 9 is a still of backdrop frame 0; 12..15 show a
    /// window view (frame shot - 8 over frame 3, desk frame 8) with the star field for the
    /// record's duration.
    /// </summary>
    /// <remarks>C: DrawFuneralLongShot (0x439220, screens.c).</remarks>
    private async Task DrawFuneralLongShotAsync(short shot, string text, short duration)
    {
        ShowText(text);
        if (shot == 9)
        {
            Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, 0);
            await Stage.RefreshAsync();
            await Stage.PresentAsync();
            await Events.WaitForSceneAdvanceAsync(duration);
            return;
        }
        _game.Timing.SetFrameTimerPeriod(duration);
        while (true)
        {
            if (_game.Timing.IsFrameTickElapsed())
                return;
            if (_constellationEnabled)
                _constellation?.Draw();
            Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, 3);
            Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, shot - 8);
            Gfx.DrawSpriteDefault(Scene, 80, 127, _backdrop, 8);
            await Stage.RefreshAsync();
            await Stage.PresentAsync();
            if (Events.CheckEscaped() != 0)
                return;
        }
    }
}
