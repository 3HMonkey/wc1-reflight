using System.Buffers.Binary;
using WingCommander.Core.Resources;
using WingCommander.Game.Scenes;
using WingCommander.Graphics.Raster;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Scenes;

public sealed partial class SceneDirector
{
    /// <summary>Upper bound of the no-draw fast-forward of a skipped animation (the original could spin forever on a script that never completes).</summary>
    private const int FastForwardLimit = 100_000;

    // C: pSceneAnimationPrimaryShape, pSceneAnimationSecondaryShape, pSceneAnimationDefinitions (parsed).
    private ShapeTable? _animationPrimary;
    private ShapeTable? _animationSecondary;
    private SceneAnimation? _animation;
    private SceneAnimationRenderer? _animationRenderer;

    /// <summary>
    /// "Meanwhile...": a MIDGAME cutscene between series. The word fades in, then the scene's
    /// captioned animations play (music 33 after a won series, 34 after a lost one) and the picture
    /// fades to black.
    /// </summary>
    /// <param name="scene">MIDGAME.V0n file (the series record's post-series sequence, 0..8).</param>
    /// <param name="seriesFailed">Selects the variant: false = series won, true = series lost.</param>
    /// <remarks>C: ShowMeanwhileTransition (0x425770, pilot.cpp). MIDGAME.V06-V08 (Secret Missions 2)
    /// are not in the DOS INSTALL.DAT; they are opened by file name, which also gives sequence 8 its
    /// file (the original's 8-entry table had no entry for it). A variant whose sections are empty
    /// in the data is skipped.</remarks>
    public async Task MeanwhileTransitionAsync(int scene, bool seriesFailed)
    {
        int variant = seriesFailed ? 1 : 0;
        int track = variant + 0x21;
        Stage.PreloadMusicTrack(track);
        Stage.SpaceTrack(track, 2, 1);
        Stage.InitializeConversationViewport();
        Stage.InitializeConversationText();
        var script = LoadSceneAnimationResources(scene, variant);
        if (script is null)
        {
            Stage.ResetScreenClipToFullHeight();
            ReleaseSceneAnimationResources();
            Stage.StopMusicUnlessSuppressed();
            return;
        }
        Stage.ClearSubtitle();
        Gfx.SetTextContext(Stage.Text);
        _introFont = Shape(LogicalFile.TitleVga, 1);
        if (_introFont is not null)
            Gfx.PrintSubtitle(Scene, _introFont, "Meanwhile..."u8);
        await Stage.PanToScreenAsync(Scene, Stage.Screen);
        _introFont = null;
        await Events.WaitForSceneAdvanceAsync(100);
        Gfx.ClearViewport(Stage.ModalSource, Black);
        await Stage.PresentAsync();
        await RunAsync(SceneType.Meanwhile, script);
        Gfx.Palette.SaveGamePalette();
        Stage.StopMusic(30);
        await Stage.FadeToColourAsync(Black);
        Gfx.ClearViewport(Stage.ModalSource, Black);
        await Stage.PresentAsync();
        await Stage.RestoreGamePaletteAsync();
        await Stage.PresentAsync();
        Stage.ResetScreenClipToFullHeight();
        ReleaseSceneAnimationResources();
        Stage.StopMusicUnlessSuppressed();
    }

    /// <summary>
    /// The packet of MIDGAME.V0n: logical file 63 + n when INSTALL.DAT lists a MIDGAME file there
    /// (asSceneAnimationLogicalFiles covers 0..7), else the file by name; null when absent.
    /// </summary>
    public static PacketFile? OpenMidgame(GameDirectory directory, int scene)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (scene < 0 || scene > 9)
            return null;
        if (scene < 8 && directory.InstallTable.TryGet(SceneAnimation.LogicalFileFor(scene), out var record) &&
            record.Name.StartsWith("MIDGAME", StringComparison.OrdinalIgnoreCase))
            return directory.OpenPacket(SceneAnimation.LogicalFileFor(scene));
        string name = $"MIDGAME.V0{scene}";
        return directory.Exists(name) ? directory.OpenPacket(name) : null;
    }

    /// <summary>
    /// Loads a MIDGAME scene's shapes (section 0 primary, 3 + variant secondary), animation
    /// definitions (1 + variant) and captions (5 + variant); null when the variant does not exist.
    /// </summary>
    /// <remarks>C: LoadSceneAnimationResources (0x424D00, logic.c).</remarks>
    private ConversationScript? LoadSceneAnimationResources(int scene, int variant)
    {
        var packet = OpenMidgame(_game.Directory, scene);
        if (packet is null || packet.SectionCount < 7)
            return null;
        if (packet.GetInfo(1 + variant).StoredSize == 0 || packet.GetInfo(5 + variant).StoredSize == 0)
            return null;
        _animationPrimary = ShapeOrNull(packet, 0);
        _animationSecondary = ShapeOrNull(packet, 3 + variant);
        _animation = SceneAnimation.Parse(packet.GetSection(1 + variant).Span);
        _animationRenderer = new SceneAnimationRenderer(this);
        var conversation = packet.GetSection(5 + variant);
        var header = conversation.Span;
        int sceneOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(header);
        int textOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        return new ConversationScript(conversation, sceneOffset, textOffset);
    }

    private static ShapeTable? ShapeOrNull(PacketFile packet, int section) =>
        section < packet.SectionCount && packet.GetInfo(section).StoredSize > 0 ? ShapeTable.FromSection(packet, section) : null;

    /// <remarks>C: ReleaseSceneAnimationResources (0x424DA0, logic.c).</remarks>
    private void ReleaseSceneAnimationResources()
    {
        _animationPrimary = null;
        _animationSecondary = null;
        _animation = null;
        _animationRenderer = null;
    }

    /// <summary>
    /// Shots 50..59: runs animation <paramref name="animation"/> of the loaded MIDGAME scene under
    /// the caption until an object completes or a 'W' countdown expires; without 'W' the caption is
    /// then held half the record's duration. A key fast-forwards the scripts without drawing.
    /// </summary>
    /// <remarks>C: PlaySceneAnimation (0x425500, logic.c). The fast-forward is bounded (see
    /// <see cref="FastForwardLimit"/>); the hold loop yields 1 ms per poll.</remarks>
    private async Task PlaySceneAnimationAsync(string text, short animation, short duration)
    {
        var sceneAnimation = _animation;
        ShowText(text);
        if (sceneAnimation is null || animation < 0 || animation >= sceneAnimation.SceneCount)
        {
            await Events.WaitForSceneAdvanceAsync(duration);
            return;
        }
        var objects = sceneAnimation.BindScene(animation).ToArray();
        bool complete = false;
        int frameSkipCounter = 1;
        Events.EscapePressed = false;
        Events.ClearInputKeyState();
        await Stage.PresentAsync();
        do
        {
            frameSkipCounter--;
            foreach (var o in objects)
                complete |= sceneAnimation.Update(o, frameSkipCounter, _animationRenderer);
            if (sceneAnimation.WaitFrames != -1)
            {
                if (sceneAnimation.WaitFrames == 0)
                    complete = true;
                else
                    sceneAnimation.WaitFrames--;
            }
            if (!complete)
            {
                await Stage.RefreshAsync();
                await Stage.PresentAsync();
            }
            if (frameSkipCounter == 0)
            {
                frameSkipCounter = FrameSkip;
                if (_game.Options.GraphicsVariant != 0)
                    frameSkipCounter++;
            }
            if ((!complete && Events.CheckEscaped() != 0) || Events.EscapePressed)
            {
                if (sceneAnimation.WaitFrames == -1)
                {
                    for (int step = 0; !complete && !sceneAnimation.WaitCommandUsed && step < FastForwardLimit; step++)
                    {
                        foreach (var o in objects)
                        {
                            frameSkipCounter = 2;
                            complete |= sceneAnimation.Update(o, frameSkipCounter, _animationRenderer);
                        }
                    }
                }
                sceneAnimation.WaitFrames = 0;
            }
        }
        while (!complete);

        if (sceneAnimation.WaitFrames == -1)
        {
            _game.Timing.SetFrameTimerPeriod(duration / 2);
            do
            {
                if (_game.Timing.IsFrameTickElapsed() || Events.CheckEscaped() != 0)
                    break;
                await Stage.Idle();
            }
            while (!Events.EscapePressed);
        }
    }

    /// <summary>Draws scene-animation sprites into the scene buffer (layer 0 primary shapes, else secondary).</summary>
    /// <remarks>C: the DrawSpriteScaled call of UpdateSceneAnimationObject (logic.c): the object's
    /// frame field is passed as the flip argument; values other than 0/0x10/0x20/0x30 were a fatal
    /// "bad flip" in the original and are drawn unflipped here.</remarks>
    private sealed class SceneAnimationRenderer(SceneDirector director) : ISceneAnimationRenderer
    {
        public void Draw(int layer, int x, int y, int frame, int rotation, int scale, int flags)
        {
            var shape = layer == 0 ? director._animationPrimary : director._animationSecondary;
            if (flags is not (0 or 0x10 or 0x20 or 0x30))
                flags = 0;
            Viewport scene = director.Scene;
            director.Gfx.DrawSpriteScaled(scene, x, y, shape, frame, rotation, scale, flags);
        }
    }
}
