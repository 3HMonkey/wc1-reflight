using System.Buffers.Binary;
using WingCommander.Core.Resources;

namespace WingCommander.Game.Scenes;

/// <summary>The conversation scripts of one mission section.</summary>
/// <param name="Briefing">Briefing room scene.</param>
/// <param name="Debriefing">Debriefing scene.</param>
/// <param name="RecRoom">Rec-room talks: [0] Shotglass, [1] left pilot, [2] right pilot.</param>
public sealed record MissionConversations(ConversationScript Briefing, ConversationScript Debriefing, ConversationScript[] RecRoom);

/// <summary>
/// BRIEFING.000/.001/.002. Section 0 = funerals (7 scene/text pairs), 1 = Colonel's office,
/// 2 = medal ceremony, 3 = unused, 4 + (series - 1) * 4 + mission = one mission each
/// (<see cref="MissionConversations"/>, header of ten u32 offsets: briefing, debriefing,
/// rec-room 0, 2, 1). Empty sections mark missions that do not exist.
/// </summary>
/// <remarks>C: LoadBriefingData (0x405910), BriefingPacketHeader; asCampaignBriefingFiles = {10, 62, 73}.</remarks>
public sealed class BriefingFile
{
    private static readonly int[] LogicalFiles = [LogicalFile.Briefing000, LogicalFile.Briefing001, LogicalFile.Briefing002];

    private readonly PacketFile _packet;

    private BriefingFile(PacketFile packet)
    {
        _packet = packet;
    }

    public PacketFile Packet => _packet;

    public static int LogicalFileFor(int campaignIndex) => LogicalFiles[campaignIndex];

    public static BriefingFile Load(GameDirectory directory, int campaignIndex) =>
        new(directory.OpenPacket(LogicalFileFor(campaignIndex)));

    public static BriefingFile Parse(PacketFile packet) => new(packet);

    /// <summary>Section index of a mission: mission + series * 4.</summary>
    public static int MissionSection(int series, int mission) => mission + series * 4;

    public bool HasMission(int series, int mission)
    {
        int section = MissionSection(series, mission);
        return section < _packet.SectionCount && _packet.GetInfo(section).StoredSize > 0 && _packet.GetDecodedSize(section) >= 40;
    }

    /// <summary>The five scene/text pairs of a mission section.</summary>
    public MissionConversations GetMission(int series, int mission)
    {
        int section = MissionSection(series, mission);
        if (!HasMission(series, mission))
            throw new GameDataException($"{_packet.Name}: no conversations for series {series} mission {mission} (section {section}).");
        var data = _packet.GetSection(section);
        ConversationScript Pair(int i) => new(data,
            (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(i * 8)..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(i * 8 + 4)..]));
        // Header order: briefing, debriefing, recRoom0, recRoom2, recRoom1.
        return new MissionConversations(Pair(0), Pair(1), [Pair(2), Pair(4), Pair(3)]);
    }

    /// <summary>
    /// Funeral scripts: pair 0 = player eulogy follow-up, 1..4 = player openings chosen by series,
    /// 5 = wingman follow-up, 6 = wingman opening.
    /// </summary>
    public ConversationScript GetFuneral(int pair) => GetPair(0, pair);

    /// <summary>The Colonel's office (promotion, new ship, ejection).</summary>
    public ConversationScript GetOffice() => GetPair(1, 0);

    /// <summary>The medal ceremony.</summary>
    public ConversationScript GetMedalCeremony() => GetPair(2, 0);

    private ConversationScript GetPair(int section, int pair)
    {
        var data = _packet.GetSection(section);
        var h = data.Span;
        return new ConversationScript(data,
            (int)BinaryPrimitives.ReadUInt32LittleEndian(h[(pair * 8)..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(h[(pair * 8 + 4)..]));
    }

    /// <summary>Funeral opening pair by series (asFuneralSceneBySeries); pair index = 1 + value.</summary>
    /// <remarks>C: asFuneralSceneBySeries[15].</remarks>
    public static ReadOnlySpan<short> FuneralSceneBySeries => [0, 0, 1, 1, 1, 1, 1, 2, 3, 2, 3, 3, 2, 3, 0];
}
