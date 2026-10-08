using WingCommander.Game.Campaign;
using WingCommander.Game.Screens;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Screens;

public class TitleSequenceTests
{
    private static (ScreenRig Rig, Func<int> Result) StartTitle(bool copySaves, Action<ScreenRig>? setup = null)
    {
        var rig = new ScreenRig(copySaves: copySaves);
        setup?.Invoke(rig); // before Start: the coroutine runs up to its first wait immediately
        int result = int.MinValue;
        rig.Start(async game => result = await game.Title.RunAsync());
        return (rig, () => result);
    }

    [DataFact]
    public void Menu_shows_new_game_and_waits_for_input()
    {
        var (rig, _) = StartTitle(copySaves: false);
        Assert.False(rig.Runtime.RunHeadless(3_000), "the menu returned without input");
        Assert.True(ScreenRig.CountNonBlack(rig.Front, 0, 199) > 1_000);
        Assert.Equal(1, rig.Game.Events.CursorShowCount);
    }

    [DataFact]
    public void S_starts_a_new_game()
    {
        var (rig, result) = StartTitle(copySaves: false);
        rig.Key(1_000, 0x1f, 0x53);
        Assert.True(rig.Runtime.RunHeadless(5_000), "the menu did not return");
        Assert.Equal(TitleSelection.NewGame, result());
        Assert.Equal(0, rig.Game.Events.CursorShowCount);
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Front, 0, 199));
    }

    [DataFact]
    public void Clicking_the_first_option_starts_a_new_game()
    {
        var (rig, result) = StartTitle(copySaves: false);
        rig.Click(1_000, 160, 60);
        Assert.True(rig.Runtime.RunHeadless(5_000), "the click did not select");
        Assert.Equal(TitleSelection.NewGame, result());
    }

    [DataFact]
    public void Continue_is_not_offered_without_saves()
    {
        var (rig, _) = StartTitle(copySaves: false);
        Assert.False(rig.Game.AnySavedGames());
        rig.Click(1_000, 160, 110);
        Assert.False(rig.Runtime.RunHeadless(4_000), "a hidden option was selected");
    }

    [DataFact]
    public void Clicking_outside_the_options_does_nothing()
    {
        var (rig, _) = StartTitle(copySaves: false);
        rig.Click(1_000, 5, 5);
        Assert.False(rig.Runtime.RunHeadless(4_000));
    }

    [DataFact]
    public void Continue_is_offered_and_selectable_when_a_save_exists()
    {
        var (rig, result) = StartTitle(copySaves: false, r =>
        {
            var slot = SaveGameSlot.CreateEmpty(3);
            slot.Occupied = true;
            SaveGameFile.Save(r.Game.SaveGamePath, 3, slot);
        });
        Assert.True(rig.Game.AnySavedGames());
        rig.Click(1_000, 160, 110);
        Assert.True(rig.Runtime.RunHeadless(5_000), "the click did not select");
        Assert.Equal(TitleSelection.Continue, result());
    }
}
