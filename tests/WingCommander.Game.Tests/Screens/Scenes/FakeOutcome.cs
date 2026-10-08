using WingCommander.Game.Scenes;

namespace WingCommander.Game.Tests.Screens.Scenes;

/// <summary>Mission results for branch tests.</summary>
internal sealed class FakeOutcome : IMissionOutcome
{
    public int PlayerKills { get; set; }

    public int WingmanKills { get; set; }

    public HashSet<int> AchievedObjectives { get; } = [];

    public HashSet<int> SightedObjectives { get; } = [];

    public bool Achieved(int objective) => AchievedObjectives.Contains(objective);

    public bool Sighted(int objective) => SightedObjectives.Contains(objective);
}
