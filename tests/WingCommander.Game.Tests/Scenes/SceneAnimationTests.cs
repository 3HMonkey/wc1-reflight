using WingCommander.Game.Scenes;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Scenes;

public class SceneAnimationTests
{
    private sealed class RecordingRenderer : ISceneAnimationRenderer
    {
        public List<(int Layer, int X, int Y, int Frame)> Calls { get; } = [];

        public void Draw(int layer, int x, int y, int frame, int rotation, int scale, int flags) =>
            Calls.Add((layer, x, y, frame));
    }

    private static SceneAnimation LoadV00Variant0()
    {
        var packet = GameData.Require().OpenPacket(SceneAnimation.LogicalFileFor(0));
        return SceneAnimation.Parse(packet.GetSection(1).Span);
    }

    [DataFact]
    public void Midgame_v00_has_two_scenes_of_nine_objects()
    {
        var animation = LoadV00Variant0();
        Assert.Equal(9, animation.ObjectCount);
        Assert.Equal(2, animation.SceneCount);
    }

    [DataFact]
    public void Background_object_scrolls_two_pixels_per_tick_and_tiles()
    {
        // Scene 0 object 1: B 0; D [1,2]; A X -2; G 0 (docs/analysis/gameflow-screens.md §4.9).
        var animation = LoadV00Variant0();
        var objects = animation.BindScene(0);
        var o = objects[1];
        short startX = o.X;
        var renderer = new RecordingRenderer();

        Assert.False(animation.Update(o, 0, renderer));
        Assert.Equal([(0, startX, o.Y, 1), (0, startX + 320, o.Y, 2)], renderer.Calls);

        renderer.Calls.Clear();
        Assert.False(animation.Update(o, 0, renderer));
        Assert.Equal(startX - 2, renderer.Calls[0].X);

        animation.Update(o, 1, renderer);          // frame skipped: state advances, nothing drawn
        Assert.Equal(2, renderer.Calls.Count);
    }

    [DataFact]
    public void Goal_on_x_completes_the_scene()
    {
        // Scene 0 object 6: Q X -80; B 0; D [3,4]; A X -4; G 0.
        var animation = LoadV00Variant0();
        var objects = animation.BindScene(0);
        var o = objects[6];
        int ticks = 0;
        bool complete = false;
        while (!complete && ticks < 2000)
        {
            complete = animation.Update(o, 0, null);
            ticks++;
        }
        Assert.True(complete);
        Assert.True(o.X <= -80);
    }

    [DataTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Every_midgame_scene_runs_to_completion(int midgame)
    {
        var directory = GameData.Require();
        var packet = directory.OpenPacket(SceneAnimation.LogicalFileFor(midgame));
        for (int variant = 0; variant < 2; variant++)
        {
            if (packet.GetInfo(1 + variant).StoredSize == 0)
                continue;
            var animation = SceneAnimation.Parse(packet.GetSection(1 + variant).Span);
            for (int scene = 0; scene < animation.SceneCount; scene++)
            {
                var objects = animation.BindScene(scene);
                bool complete = false;
                for (int tick = 0; tick < 5000 && !complete; tick++)
                {
                    foreach (var o in objects)
                        complete |= animation.Update(o, 0, null);
                    if (animation.WaitFrames != -1)
                    {
                        if (animation.WaitFrames == 0)
                            complete = true;
                        else
                            animation.WaitFrames--;
                    }
                }
                Assert.True(complete, $"MIDGAME.V0{midgame} variant {variant} scene {scene}");
            }
        }
    }
}
