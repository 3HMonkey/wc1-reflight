using WingCommander.Core.Resources;
using WingCommander.Game.Screens.Ui;

namespace WingCommander.Game.Screens.Rooms;

/// <summary>The text records of MODULE.xxx the rooms need (the system name of a series).</summary>
/// <remarks>C: the abSeriesAuxData part of LoadMissionData (0x4059B0, cmpgn.c); asMissionDataFiles = {15, 52, 72}.</remarks>
internal static class MissionText
{
    /// <summary>Size of a MODULE section 4/5 text record.</summary>
    private const int RecordSize = 0x28;

    private static readonly int[] MissionDataFiles = [LogicalFile.Module000, LogicalFile.Module001, LogicalFile.Module002];

    /// <summary>
    /// The system name of <paramref name="series"/> (MODULE section 5, record <c>series</c>), or
    /// null when the mission index is outside the 64-entry table (the SDL port's guard returns
    /// before loading anything) or the record is missing.
    /// </summary>
    public static string? LoadSystemName(Wc1Game game, int campaignDataSet, int series, int mission)
    {
        int missionIndex = mission + series * 4;
        if (missionIndex is < 0 or >= 64 || (uint)campaignDataSet >= (uint)MissionDataFiles.Length)
            return null;
        var section = game.Resources.GetSection(MissionDataFiles[campaignDataSet], 5).Span;
        int offset = series * RecordSize;
        if (offset < 0 || offset + RecordSize > section.Length)
            return null;
        return UiText.FromBytes(section.Slice(offset, RecordSize));
    }
}
