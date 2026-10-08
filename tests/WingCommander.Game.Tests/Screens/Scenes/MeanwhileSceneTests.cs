using WingCommander.Game.Screens.Scenes;
using WingCommander.Tests;
using Xunit.Abstractions;

namespace WingCommander.Game.Tests.Screens.Scenes;

/// <summary>The MIDGAME "Meanwhile..." cutscenes (MIDGAME.V00..V08, both variants where present).</summary>
[Collection(SceneRig.Collection)]
public class MeanwhileSceneTests(ITestOutputHelper output)
{
    public static TheoryData<int, bool> Scenes()
    {
        var data = new TheoryData<int, bool>();
        var directory = GameData.Directory;
        if (directory is null)
            return data;
        for (int scene = 0; scene <= 8; scene++)
        {
            var packet = SceneDirector.OpenMidgame(directory, scene);
            if (packet is null)
                continue;
            for (int variant = 0; variant < 2; variant++)
            {
                if (packet.GetInfo(1 + variant).StoredSize > 0 && packet.GetInfo(5 + variant).StoredSize > 0)
                    data.Add(scene, variant == 1);
            }
        }
        return data;
    }

    [DataFact]
    public void Every_midgame_file_is_found_including_the_secret_missions_2_scenes()
    {
        var directory = GameData.Require();
        for (int scene = 0; scene <= 8; scene++)
            Assert.NotNull(SceneDirector.OpenMidgame(directory, scene));
        Assert.Equal(16, Scenes().Count);
    }

    [DataTheory]
    [MemberData(nameof(Scenes))]
    public void Midgame_scene_plays_to_the_end(int scene, bool seriesFailed)
    {
        var rig = new SceneRig();
        rig.Start(director => director.MeanwhileTransitionAsync(scene, seriesFailed));
        string name = $"midgame-{scene}-{(seriesFailed ? 1 : 0)}";
        Assert.True(rig.RunWithSnapshots(name, 600_000, 1000), "the scene did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
        foreach (var record in rig.Director.PlayedRecords)
            output.WriteLine(record.ToString());
        Assert.True(rig.Director.RecordsPlayed >= 4);
        Assert.All(rig.Director.PlayedRecords, r => Assert.InRange(r.Shot, 50, 59));
        Assert.False(rig.Director.Stage.SceneBuffer.IsAllocated);
        Assert.Equal(0, ScreenRig.CountNonBlack(rig.Screen.Front, 0, 199));
    }

    [DataTheory]
    [MemberData(nameof(Scenes))]
    public void Keys_fast_forward_every_animation(int scene, bool seriesFailed)
    {
        var rig = new SceneRig();
        rig.Start(director => director.MeanwhileTransitionAsync(scene, seriesFailed));
        rig.PressSpaceEvery(2_000, 120_000, 400);
        Assert.True(rig.RunUntil(120_000), "the scene did not end");
        Assert.Null(rig.Screen.Runtime.Failure);
    }

    [DataFact]
    public void A_missing_variant_is_skipped()
    {
        // MIDGAME.V04 only has the "series failed" variant.
        var rig = new SceneRig();
        rig.Start(director => director.MeanwhileTransitionAsync(4, seriesFailed: false));
        Assert.True(rig.RunUntil(1_000));
        Assert.Null(rig.Screen.Runtime.Failure);
        Assert.Equal(0, rig.Director.RecordsPlayed);
    }
}
