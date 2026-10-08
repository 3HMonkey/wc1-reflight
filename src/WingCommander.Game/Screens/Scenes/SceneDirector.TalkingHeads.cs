using WingCommander.Core.Resources;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Shapes;

namespace WingCommander.Game.Screens.Scenes;

public sealed partial class SceneDirector
{
    /// <summary>Face and mouth overlay origins of the eleven TALKING.VGA heads.</summary>
    /// <remarks>C: aTalkingHeadOrigins (0x0046E190): {faceX, faceY, mouthX, mouthY}.</remarks>
    private static readonly (short FaceX, short FaceY, short MouthX, short MouthY)[] TalkingHeadOrigins =
    [
        (161, 60, 161, 90),
        (161, 60, 161, 87),
        (160, 60, 159, 90),
        (161, 60, 161, 90),
        (161, 60, 161, 90),
        (161, 60, 161, 90),
        (161, 60, 161, 90),
        (161, 60, 161, 90),
        (161, 60, 161, 90),
        (161, 60, 161, 90),
        (160, 53, 160, 88),
    ];

    /// <summary>
    /// Selects the backdrop frame (and star field) of the current talker, loads head
    /// <paramref name="face"/> (TALKING.VGA section 0..10) and the comm overlay (section 11), and
    /// draws the head with closed mouth and no face overlay.
    /// </summary>
    /// <remarks>C: LoadFace (0x4050B0, cmpgn.c).</remarks>
    private async Task LoadFaceAsync(short face)
    {
        switch (_character)
        {
            case 0:
                _backdropFrame = 4;
                break;
            case 1:
                _backdropFrame = 5;
                break;
            case 2:
                _backdropFrame = 0;
                _constellation ??= ConstellationField.Create(_game);
                _constellationViewport.CopyFrom(Scene);
                _constellationViewport.Bottom = 76;
                _constellation.Initialize(_constellationViewport, -1, 16);
                _constellationEnabled = true;
                break;
            case 4:
                _backdropFrame = 2;
                break;
            case 8:
                _backdropFrame = 1;
                _constellation ??= ConstellationField.Create(_game);
                _constellation.Initialize(Scene, -1, 16);
                _constellationEnabled = true;
                break;
            case 9:
                _backdropFrame = 2;
                break;
            case 10:
            case 12:
                _backdropFrame = 0;
                break;
            case 3:
            case 11:
            case 13:
                _backdropFrame = 1;
                break;
            default:
                _backdropFrame = -1;
                break;
        }
        if (face != _talkingHeadFace && _talkingHead is not null)
            _talkingHead = null;
        _talkingHead ??= Shape(LogicalFile.TalkingVga, face);
        _talkingHeadFace = face;
        _overlay ??= Shape(LogicalFile.TalkingVga, 11);
        var origin = TalkingHeadOrigins[Math.Clamp((int)face, 0, TalkingHeadOrigins.Length - 1)];
        (_faceX, _faceY, _mouthX, _mouthY) = origin;
        await CloseTalkAsync(_talkingHead, -1, -1);
    }

    /// <summary>
    /// Draws a talking head into the scene buffer: star field (if active), backdrop frame (or the
    /// scene type's clear colour), head, face overlay, mouth, comm overlay and the funeral overlays
    /// of talkers 5 and 6, then copies the scene to the screen.
    /// </summary>
    /// <remarks>C: CloseTalk (0x4054B0, cmpgn.c).</remarks>
    private async Task CloseTalkAsync(ShapeTable? talker, short mouthFrame, short faceFrame)
    {
        if (_constellationEnabled)
            _constellation?.Draw();
        switch (_sceneType)
        {
            case SceneType.Briefing:
            case SceneType.Debriefing:
            case SceneType.RecRoom:
            case SceneType.Office:
            case SceneType.MedalCeremony:
                if (_backdropFrame != -1)
                {
                    Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, _backdropFrame);
                    break;
                }
                Gfx.ClearViewport(Scene, Black);
                break;
            case SceneType.Funeral:
                Gfx.ClearViewport(Scene, PaletteColours.PrimaryViewBuffer);
                break;
            default:
                Gfx.ClearViewport(Scene, Black);
                break;
        }
        Gfx.DrawSpriteDefault(Scene, 0, 0, talker, 0);
        if (faceFrame > -1)
            Gfx.DrawSpriteDefault(Scene, _faceX, _faceY, talker, faceFrame + 11);
        if (mouthFrame > -1)
            Gfx.DrawSpriteDefault(Scene, _mouthX, _mouthY, talker, mouthFrame + 1);
        if (_overlayEnabled)
            Gfx.DrawSpriteDefault(Scene, 0, 0, _overlay, Math.Min(_talkingHeadFace, (short)1));
        switch (_character)
        {
            case 5:
                Gfx.DrawSpriteDefault(Scene, 0, 0, _special, 10);
                break;
            case 6:
                Gfx.DrawSpriteDefault(Scene, 0, 0, _special, 10);
                Gfx.DrawSpriteDefault(Scene, 0, 0, _special, 11);
                break;
        }
        await Stage.RefreshAsync();
    }

    /// <summary>
    /// A talking head speaking a subtitle: the mouth and face scripts advance on their own
    /// countdowns (two loop iterations per script tick, one frame each). When the mouth script ends
    /// the record's duration starts; the talk ends when it elapses, when both scripts have ended,
    /// or at a key press (the queue is then drained).
    /// </summary>
    /// <remarks>C: LongTalk (0x405290, cmpgn.c). Kept quirks: the first entry of each script is
    /// skipped (the cursor advances before it is read); an 'R' loop marker restarts the script at
    /// its first entry; when the mouth ends after the face, the duration wait is cut short.</remarks>
    private async Task LongTalkAsync(ShapeTable? talker, string text, short[] mouthCommands, short[] faceCommands, short duration)
    {
        bool waiting = false;
        ShowText(text);
        int face = 0;
        int mouth = 0;
        short faceFrame = 0;
        short mouthFrame = 0;
        short faceCountdown = 0;
        short mouthCountdown = 0;
        int frameSkipCounter = 1;
        while (true)
        {
            if (At(mouthCommands, mouth) == -1 && At(faceCommands, face) == -1)
            {
                if (!waiting)
                {
                    await CloseTalkAsync(talker, -1, -1);
                    await Stage.PresentAsync();
                    await Events.WaitForSceneAdvanceAsync(duration);
                    return;
                }
                _game.Timing.IsFrameTickElapsed();
                return;
            }
            if (mouthCountdown-- == 0)
            {
                if (At(mouthCommands, mouth) != -1)
                    mouth += 2;
                switch (At(mouthCommands, mouth))
                {
                    case -1:
                        mouthFrame = -1;
                        if (!waiting)
                        {
                            waiting = true;
                            _game.Timing.SetFrameTimerPeriod(duration);
                        }
                        break;
                    case -2:
                        mouth = 0;
                        mouthFrame = At(mouthCommands, mouth);
                        mouthCountdown = unchecked((short)(At(mouthCommands, mouth + 1) * 2));
                        break;
                    default:
                        mouthFrame = At(mouthCommands, mouth);
                        mouthCountdown = unchecked((short)(At(mouthCommands, mouth + 1) * 2));
                        break;
                }
            }
            if (faceCountdown-- == 0)
            {
                if (At(faceCommands, face) != -1)
                    face += 2;
                switch (At(faceCommands, face))
                {
                    case -1:
                        faceFrame = -1;
                        break;
                    default:
                        if (At(faceCommands, face) == -2)
                            face = 0;
                        faceFrame = At(faceCommands, face);
                        if (faceFrame == 10)
                            faceFrame = -1;
                        faceCountdown = unchecked((short)(At(faceCommands, face + 1) * 2));
                        break;
                }
            }
            frameSkipCounter--;
            if (frameSkipCounter < 1)
            {
                frameSkipCounter = FrameSkip;
                await CloseTalkAsync(talker, mouthFrame, faceFrame);
                await Stage.PresentAsync();
            }
            if (Events.CheckEscaped() != 0)
                break;
            if (waiting && _game.Timing.IsFrameTickElapsed())
                return;
        }
        while (Events.CheckEscaped() != 0)
            await Stage.Idle();
    }

    /// <summary>Redraw divider of the animation loops (1 = draw every iteration).</summary>
    /// <remarks>C: nFrameSkip, set to 1 at start-up; only the in-flight frame-skip keys change it.</remarks>
    private const int FrameSkip = 1;
}
