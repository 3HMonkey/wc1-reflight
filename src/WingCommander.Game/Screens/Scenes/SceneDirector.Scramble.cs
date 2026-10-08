using WingCommander.Audio.Director;
using WingCommander.Core.Resources;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Scenes;

public sealed partial class SceneDirector
{
    /// <summary>Flight-gear overlays of the two walking pilots, by the random walker pair.</summary>
    /// <remarks>C: acScrambleWalkerOverlayFrames (0x00465770).</remarks>
    private static ReadOnlySpan<sbyte> ScrambleWalkerOverlayFrames => [3, 4, 5, 3, 4, 6, 6, 3];

    // C: cScrambleLeftWalkerFrame, cScrambleRightWalkerFrame, nScrambleLeftWalkerX, nScrambleRightWalkerX,
    // nScrambleBackgroundX, nScrambleWalkerY, cScrambleWalkerPair, pScrambleHangarShape.
    private sbyte _leftWalkerFrame = 7;
    private sbyte _rightWalkerFrame = 10;
    private short _leftWalkerX = 70;
    private short _rightWalkerX = 170;
    private short _scrambleBackgroundX;
    private short _walkerY;
    private sbyte _walkerPair;
    private ShapeTable? _hangar;

    /// <summary>
    /// The scramble: two pilots run through the hangar (24 frames), climb the ladder (24 frames)
    /// and run on (24 frames) while the klaxon sounds; music 27. Esc skips.
    /// </summary>
    /// <remarks>C: PlayScrambleHangarScene (0x4079C0, brains.c). The memory-configuration branch
    /// (SceneLeaveHook) is a DOS memory manager detail and is not ported.</remarks>
    public async Task PlayScrambleHangarSceneAsync()
    {
        Stage.PreloadMusicTrack(MusicTrack.Scramble);
        Stage.SpaceTrack(MusicTrack.Scramble, 1, -1);
        Stage.InitializeConversationViewport();
        _leftWalkerFrame = 7;
        _scrambleBackgroundX = 0;
        _rightWalkerFrame = 10;
        _leftWalkerX = 70;
        _rightWalkerX = 170;
        _hangar = Shape(LogicalFile.ScrambleVga, 0);
        _walkerPair = (sbyte)((unchecked((ushort)_game.Random.Next()) + 3) & 3);
        Stage.PlaySfx(SoundEffectNumber.ScrambleKlaxon);
        Events.EscapePressed = false;

        await AnimateScrambleWalkAsync(24);
        if (!Events.EscapePressed)
        {
            _leftWalkerFrame = 21;
            _rightWalkerFrame = 24;
            _leftWalkerX = 90;
            _rightWalkerX = 200;
            _walkerY = -14;
            int frameSkipCounter = 1;
            for (int ticks = 0; ticks < 24; ticks++)
            {
                Events.PumpWindowMessages();
                frameSkipCounter--;
                if (frameSkipCounter < 1)
                {
                    frameSkipCounter = FrameSkip;
                    Gfx.DrawSpriteDefault(Scene, _scrambleBackgroundX, 0, _hangar, 2);
                    Gfx.DrawSpriteDefault(Scene, _scrambleBackgroundX + 320, 0, _hangar, 2);
                    Gfx.DrawSpriteDefault(Scene, _scrambleBackgroundX + 640, 0, _hangar, 2);
                    Gfx.DrawSpriteDefault(Scene, _leftWalkerX, _walkerY, _hangar, _leftWalkerFrame);
                    Gfx.DrawSpriteDefault(Scene, _rightWalkerX, _walkerY, _hangar, _rightWalkerFrame);
                    await Stage.RefreshAsync();
                    await Stage.PresentAsync();
                    _leftWalkerFrame++;
                    if (_leftWalkerFrame > 26)
                        _leftWalkerFrame = 21;
                    _rightWalkerFrame++;
                    if (_rightWalkerFrame > 26)
                        _rightWalkerFrame = 21;
                }
                _scrambleBackgroundX = (short)(_scrambleBackgroundX - 12);
                _leftWalkerX = (short)(_leftWalkerX + 2);
                _rightWalkerX = (short)(_rightWalkerX + 3);
                if (Events.EscapePressed)
                    break;
            }
            if (!Events.EscapePressed)
            {
                _leftWalkerFrame = 7;
                _rightWalkerFrame = 10;
                await AnimateScrambleWalkAsync(24);
            }
        }
        Stage.FlushSoundEffects();
        _hangar = null;
        Stage.ResetScreenClipToFullHeight();
    }

    /// <summary>The two pilots running along the scrolling hangar for <paramref name="ticks"/> frames.</summary>
    /// <remarks>C: AnimateScrambleWalk (0x4078D0, brains.c).</remarks>
    private async Task AnimateScrambleWalkAsync(int ticks)
    {
        int frameSkipCounter = 1;
        for (int elapsed = 0; elapsed < ticks; elapsed++)
        {
            Events.PumpWindowMessages();
            frameSkipCounter--;
            if (frameSkipCounter < 1)
            {
                frameSkipCounter = FrameSkip;
                Gfx.DrawSpriteDefault(Scene, _scrambleBackgroundX, 0, _hangar, 0);
                Gfx.DrawSpriteDefault(Scene, _scrambleBackgroundX + 320, 0, _hangar, 1);
                Gfx.DrawSpriteDefault(Scene, _scrambleBackgroundX + 640, 0, _hangar, 0);
                Gfx.DrawSpriteDefault(Scene, _scrambleBackgroundX + 960, 0, _hangar, 1);
                _walkerY = (short)(127 - ShapeBounds.GetShapeFrameExtent(0, 0, _hangar, _leftWalkerFrame, 3));
                Gfx.DrawSpriteDefault(Scene, _leftWalkerX, _walkerY, _hangar, _leftWalkerFrame);
                Gfx.DrawSpriteDefault(Scene, _leftWalkerX, _walkerY, _hangar, ScrambleWalkerOverlayFrames[_walkerPair * 2]);
                _walkerY = (short)(137 - ShapeBounds.GetShapeFrameExtent(0, 0, _hangar, _rightWalkerFrame, 3));
                Gfx.DrawSpriteDefault(Scene, _rightWalkerX, _walkerY, _hangar, _rightWalkerFrame);
                Gfx.DrawSpriteDefault(Scene, _rightWalkerX, _walkerY, _hangar, ScrambleWalkerOverlayFrames[_walkerPair * 2 + 1]);
                await Stage.RefreshAsync();
                await Stage.PresentAsync();
                _leftWalkerFrame++;
                if (_leftWalkerFrame > 19)
                    _leftWalkerFrame = 7;
                _rightWalkerFrame++;
                if (_rightWalkerFrame > 19)
                    _rightWalkerFrame = 7;
            }
            _scrambleBackgroundX = (short)(_scrambleBackgroundX - 12);
            _rightWalkerX = (short)(_rightWalkerX + 3);
            _leftWalkerX = (short)(_leftWalkerX + 2);
            if (Events.EscapePressed)
                break;
        }
    }
}
