using WingCommander.Core.Numerics;
using WingCommander.Core.Platform;
using WingCommander.Game.Runtime;
using WingCommander.Tests;

namespace WingCommander.Game.Flight.Tests;

public class FlightLayerTests
{
    [DataFact]
    public void The_layer_plugs_into_the_game()
    {
        var host = new HeadlessServices
        {
            UserDataDirectory = Path.Combine(Path.GetTempPath(), "wc1-tests", Guid.NewGuid().ToString("N")),
        };
        var runtime = new GameRuntime(host, new CRandom(1));
        var game = new Wc1Game(runtime, GameData.Require(), new Wc1GameOptions { Audio = false, SkipIntro = true });
        var layer = new FlightLayer(game);
        game.FlightLayer = layer;
        game.Start();

        // The title starts with the attract sequence, which runs until a key.
        Assert.False(runtime.RunHeadless(2_000), "the game ended");
        Assert.Null(runtime.Failure);
        Assert.Equal(0, game.Events.CursorShowCount);
        Assert.True(layer.Session.AttractFrames > 20, $"attract frames: {layer.Session.AttractFrames}");

        runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyDown, 0x39, ' ', 0, 0, 0, HostModifiers.None, false), 2_100);
        runtime.Events.EnqueueHostEvent(new HostInputEvent(HostInputKind.KeyUp, 0x39, ' ', 0, 0, 0, HostModifiers.None, false), 2_150);
        Assert.False(runtime.RunHeadless(4_000), "the game ended");
        Assert.Null(runtime.Failure);
        Assert.Equal(1, game.Events.CursorShowCount); // the title menu is up
    }
}
