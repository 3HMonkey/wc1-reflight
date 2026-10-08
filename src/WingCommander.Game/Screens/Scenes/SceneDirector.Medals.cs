using WingCommander.Audio.Director;
using WingCommander.Core.Resources;
using WingCommander.Game.Campaign;
using WingCommander.Game.Input;
using WingCommander.Game.Scenes;
using WingCommander.Graphics;
using WingCommander.Graphics.Palettes;

namespace WingCommander.Game.Screens.Scenes;

public sealed partial class SceneDirector
{
    /// <summary>X of each medal in the decorations display (the stars stack upward from there).</summary>
    /// <remarks>C: asMedalDisplayX (0x0046E2D0).</remarks>
    private static ReadOnlySpan<short> MedalDisplayX => [191, 199, 207, 216, 228];

    /// <remarks>C: szMedalsPilotSummary (0x0046E5DC).</remarks>
    public const string MedalsPilotSummary = "$R $N, aka $C.\n$S system, dateline $D.";

    /// <summary>
    /// The medal ceremony on the hangar deck: the medal is counted, then the ceremony scene plays
    /// (chest opening, citation, medal close-up, congratulations, pinning). A second Golden Sun is
    /// never awarded.
    /// </summary>
    /// <remarks>C: AwardCampaignMedal (0x436F50, screens.c).</remarks>
    public async Task AwardCampaignMedalAsync(int medal)
    {
        var medals = Session.State.Medals;
        if (medal == (int)Medal.GoldenSun && unchecked((sbyte)medals[3]) > 0)
            return;
        if ((uint)medal >= (uint)medals.Length)
            return;
        Stage.PreloadMusicTrack(MusicTrack.MedalCeremony);
        Stage.PreloadMusicTrack(MusicTrack.MedalPurpleHeart);
        Stage.PreloadMusicTrack(MusicTrack.MedalMinorBravery);
        Stage.PreloadMusicTrack(MusicTrack.MedalMajorBravery);
        switch (medal)
        {
            case 0:
            case 1:
                Stage.SpaceTrack(MusicTrack.MedalMinorBravery, 1, -1);
                break;
            case 2:
            case 4:
                Stage.SpaceTrack(MusicTrack.MedalMajorBravery, 1, -1);
                break;
            case 3:
                Stage.SpaceTrack(MusicTrack.MedalPurpleHeart, 1, -1);
                break;
        }
        var script = BriefingFile.Load(_game.Directory, Session.CampaignDataSet).GetMedalCeremony();
        Stage.InitializeConversationViewport();
        Stage.InitializeConversationText();
        _medalScene = Shape(LogicalFile.BriefingVga, 8);
        _backdrop = Shape(LogicalFile.BriefingVga, 10);
        Context.MedalIndex = medal;
        medals[medal]++;
        await RunAsync(SceneType.MedalCeremony, script);
        Events.EscapePressed = false;
        _backdrop = null;
        _medalScene = null;
        Stage.StopMusicUnlessSuppressed();
        Stage.ResetScreenClipToFullHeight();
    }

    /// <summary>
    /// The pilot's decorations (rank insignia, badges, medals) with the line "$R $N, aka $C. /
    /// $S system, dateline $D." until a key or button. Needs the conversation layout; when the
    /// caller has not set it up (the barracks), it is set up and taken down around the display.
    /// </summary>
    /// <remarks>C: ViewMedals (0x436E30, screens.c). The barracks calls LoadMissionData first so $S
    /// names the current system; call <see cref="LoadMissionData"/> for that.</remarks>
    public async Task ViewMedalsAsync()
    {
        bool ownLayout = !Scene.IsAllocated;
        short savedTop = Stage.Screen.Top, savedBottom = Stage.Screen.Bottom;
        if (ownLayout)
        {
            Scene.SetViewportRect(0, 0, 319, 127);
            Scene.AllocateViewport(Black);
            Stage.Screen.Top = ConversationStage.SceneTop;
            Stage.Screen.Bottom = ConversationStage.SceneBottom;
        }
        await ViewMedalsCoreAsync();
        if (ownLayout)
        {
            Scene.FreeViewport();
            Stage.Screen.Top = savedTop;
            Stage.Screen.Bottom = savedBottom;
        }
    }

    /// <remarks>C: ViewMedals (0x436E30, screens.c).</remarks>
    private async Task ViewMedalsCoreAsync()
    {
        var state = new InputEventState();
        bool clicked = false;
        _medalScene = Shape(LogicalFile.BriefingVga, 8);
        _backdrop = null;
        Stage.InitializeConversationText();
        Gfx.ClearViewport(Scene, Black);
        byte savedInputMode = Events.InputMode;
        Events.InputMode = 1;
        while (true)
        {
            Events.PumpWindowMessages();
            if (Events.PeekInputEvent(ref state, InputEventType.JoystickButton) ||
                Events.PeekInputEvent(ref state, InputEventType.ButtonDown) ||
                Events.PeekInputEvent(ref state, InputEventType.KeyDown))
                clicked = true;
            await DrawMedalsAsync();
            string summary = TextMacros.Expand(MedalsPilotSummary, Context);
            await Stage.RefreshAsync();
            Stage.ClearSubtitle();
            Gfx.FormatTextBufferFromStart(ConversationStage.SubtitleFormat, 0, ConversationStage.SubtitleY, PaletteColours.ViewportClear, summary);
            await Stage.PresentAsync();
            if (clicked)
            {
                _medalScene = null;
                await Events.WaitForInputKeyAsync();
                Events.ClearInputKeyStatePreservingModifiers();
                Events.InputMode = savedInputMode;
                Events.FlushInputEvents();
                return;
            }
        }
    }

    /// <summary>
    /// The decorations: chest background, rank insignia on both shoulders, earned badges in rows
    /// of four, medals (stars stacked by count). Presents (without copying the scene first).
    /// </summary>
    /// <remarks>C: DrawMedals (0x4375C0, screens.c).</remarks>
    private async Task DrawMedalsAsync()
    {
        short rowY = 78;
        short x = 188;
        var state = Session.State;
        int rank = Session.Player.Rank;
        Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, 1);
        Gfx.DrawSpriteDefault(Scene, 0, 0, _medalScene, 11);
        Gfx.DrawSpriteDefault(Scene, 253, 38, _medalScene, rank + 33);
        Gfx.DrawSpriteScaled(Scene, 67, 38, _medalScene, rank + 33, 0, 255, GraphicsContext.FlipHorizontal);
        for (int badge = 0; badge < 12; badge++)
        {
            if (state.Badges[badge] == 0)
                continue;
            if (x > 231)
            {
                rowY = (short)(rowY + 3);
                x = 188;
            }
            Gfx.DrawSpriteDefault(Scene, x, rowY, _medalScene, badge + 13);
            x = (short)(x + 11);
        }
        rowY = (short)(rowY + 5);
        for (int medal = 0; medal < 5; medal++)
        {
            if (state.Medals[medal] == 0)
                continue;
            x = MedalDisplayX[medal];
            short stack = rowY;
            if (medal < 3)
            {
                int count = unchecked((sbyte)state.Medals[medal]);
                for (int star = 0; star < count; star++)
                {
                    Gfx.DrawSpriteDefault(Scene, x, stack, _medalScene, medal + 25);
                    stack = (short)(stack + 2);
                }
            }
            Gfx.DrawSpriteDefault(Scene, x, stack, _medalScene, medal + 28);
        }
        await Stage.PresentAsync();
    }

    /// <summary>Shot 16: the medal case opens (81 frames), then the medal's fanfare (38/39/40).</summary>
    /// <remarks>C: DrawMedalChest (0x4370D0, screens.c).</remarks>
    private async Task DrawMedalChestAsync(string text, short duration)
    {
        ShowText(text);
        short offset = 0;
        do
        {
            Gfx.DrawSpriteDefault(Scene, 0, 0, _medalScene, 41);
            Gfx.DrawSpriteDefault(Scene, 92 - offset, 64, _medalScene, 43);
            Gfx.DrawSpriteDefault(Scene, 228 + offset, 64, _medalScene, 44);
            Gfx.DrawSpriteDefault(Scene, 0, 64, _medalScene, 42);
            Gfx.DrawSpriteScaled(Scene, 319, 64, _medalScene, 42, 0, 256, GraphicsContext.FlipHorizontal);
            await Stage.RefreshAsync();
            if (Events.CheckEscaped() != 0)
            {
                duration = -1;
                break;
            }
            offset = (short)(offset + 2);
            await Stage.PresentAsync();
        }
        while (offset < 162);
        await Events.WaitForSceneAdvanceAsync(duration);
        switch (Context.MedalIndex)
        {
            case 0:
            case 1:
                Stage.SpaceTrack(MusicTrack.MedalMinorBravery, 1, -1);
                break;
            case 2:
            case 4:
                Stage.SpaceTrack(MusicTrack.MedalMajorBravery, 1, -1);
                break;
            case 3:
                Stage.SpaceTrack(MusicTrack.MedalPurpleHeart, 1, -1);
                break;
        }
    }

    /// <summary>Shot 6: the hangar deck long shot; the mouth script animates the officer reading the citation.</summary>
    /// <remarks>C: DrawMedalLongShot (0x437250, screens.c).</remarks>
    private async Task DrawMedalLongShotAsync(short[] animation, string text, short duration)
    {
        short countdown = 0;
        ShowText(text);
        int cursor = 0;
        short frame = -1;
        if (At(animation, cursor) != -1)
        {
            while (true)
            {
                if (countdown-- == 0)
                {
                    if (At(animation, cursor) != -1)
                        cursor += 2;
                    if (At(animation, cursor) == -2)
                    {
                        cursor = 0;
                    }
                    else if (At(animation, cursor) == -1)
                    {
                        frame = -1;
                    }
                    else
                    {
                        frame = At(animation, cursor);
                        countdown = unchecked((short)(At(animation, cursor + 1) * 2));
                    }
                }
                Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, 0);
                Gfx.DrawSpriteDefault(Scene, 0, 0, _medalScene, 0);
                if (frame > -1)
                    Gfx.DrawSpriteDefault(Scene, 121, 8, _medalScene, frame + 1);
                await Stage.RefreshAsync();
                if (Events.CheckEscaped() != 0)
                {
                    duration = -1;
                    break;
                }
                await Stage.PresentAsync();
                if (At(animation, cursor) == -1)
                    break;
            }
        }
        Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, 0);
        Gfx.DrawSpriteDefault(Scene, 0, 0, _medalScene, 0);
        await Stage.RefreshAsync();
        await Stage.PresentAsync();
        await Events.WaitForSceneAdvanceAsync(duration);
    }

    /// <summary>Shot 7: zoom onto the awarded medal on the decorations display (32 frames).</summary>
    /// <remarks>C: MedalEstablish (0x4373E0, screens.c).</remarks>
    private async Task MedalEstablishAsync(string text, short duration)
    {
        int distance = 200;
        ShowText(text);
        short x = MedalDisplayX[Math.Clamp(Context.MedalIndex, 0, 4)];
        short y = 87;
        for (int frame = 0; frame < 32; frame++)
        {
            await DrawMedalsAsync();
            Gfx.DrawSpriteScaled(Scene, x, y, _medalScene, 12, 0, unchecked((short)(0xc800 / distance)), 0);
            distance--;
            x--;
            y = (short)(y + 2);
            await Stage.RefreshAsync();
            if (Events.CheckEscaped() != 0)
            {
                duration = -1;
                break;
            }
            await Stage.PresentAsync();
        }
        await Events.WaitForSceneAdvanceAsync(duration);
    }

    /// <summary>Shot 8: the pinning (frames 38..40 cycling) for the record's duration.</summary>
    /// <remarks>C: PinMedal (0x4374B0, screens.c).</remarks>
    private async Task PinMedalAsync(string text, short duration)
    {
        short frame = 0;
        ShowText(text);
        Gfx.ClearViewport(Scene, Black);
        _game.Timing.SetFrameTimerPeriod(duration);
        bool elapsed = _game.Timing.IsFrameTickElapsed();
        while (!elapsed)
        {
            frame++;
            Gfx.DrawSpriteDefault(Scene, 0, 0, _backdrop, 0);
            Gfx.DrawSpriteDefault(Scene, 0, 0, _medalScene, 0);
            Gfx.DrawSpriteDefault(Scene, 0, 0, _medalScene, frame % 3 + 38);
            await Stage.RefreshAsync();
            if (Events.CheckEscaped() != 0)
            {
                duration = -1;
                break;
            }
            await Stage.PresentAsync();
            elapsed = _game.Timing.IsFrameTickElapsed();
        }
        await Events.WaitForSceneAdvanceAsync(duration);
    }
}
