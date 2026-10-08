using System.Buffers.Binary;
using System.Text;
using WingCommander.Core.Resources;

namespace WingCommander.Game.Scenes;

/// <summary>One packed 13-byte command of a conversation scene.</summary>
/// <remarks>C: ConversationSceneRecord (wcdata.h).</remarks>
public readonly record struct ConversationRecord(
    sbyte Shot,
    sbyte TextColour,
    sbyte Talker,
    short Duration,
    short TestsOffset,
    short TextOffset,
    short MouthAnimationOffset,
    short FaceAnimationOffset)
{
    public const int Size = 13;

    /// <summary>Shot value that ends the scene.</summary>
    public const sbyte EndOfScene = -2;

    /// <summary>Shot value that keeps the previous shot.</summary>
    public const sbyte KeepShot = -1;

    /// <summary>Bit of <see cref="Shot"/> that draws the comm overlay over a talking head.</summary>
    public const int OverlayFlag = 0x40;

    public static ConversationRecord Read(ReadOnlySpan<byte> d) => new(
        unchecked((sbyte)d[0]),
        unchecked((sbyte)d[1]),
        unchecked((sbyte)d[2]),
        BinaryPrimitives.ReadInt16LittleEndian(d[3..]),
        BinaryPrimitives.ReadInt16LittleEndian(d[5..]),
        BinaryPrimitives.ReadInt16LittleEndian(d[7..]),
        BinaryPrimitives.ReadInt16LittleEndian(d[9..]),
        BinaryPrimitives.ReadInt16LittleEndian(d[11..]));
}

/// <summary>
/// A conversation scene: an array of <see cref="ConversationRecord"/>s (addressed by index, tests
/// jump to <c>sceneData + n</c>) plus the text block that holds subtitles, test strings and
/// mouth/face scripts (all offsets relative to the text block).
/// </summary>
/// <remarks>C: the (scene, text) pointer pairs derived by LoadBriefingData and consumed by SceneDirector.</remarks>
public sealed class ConversationScript
{
    private readonly ReadOnlyMemory<byte> _section;
    private readonly int _sceneOffset;
    private readonly int _textOffset;

    public ConversationScript(ReadOnlyMemory<byte> section, int sceneOffset, int textOffset)
    {
        if ((uint)sceneOffset > (uint)section.Length || (uint)textOffset > (uint)section.Length)
            throw new GameDataException($"Conversation offsets {sceneOffset}/{textOffset} outside a {section.Length}-byte section.");
        _section = section;
        _sceneOffset = sceneOffset;
        _textOffset = textOffset;
    }

    /// <summary>Bytes from the text block start to the end of the section.</summary>
    public ReadOnlySpan<byte> TextBlock => _section.Span[_textOffset..];

    /// <summary>Number of records up to and including the terminating shot -2 (scan from the start).</summary>
    public int RecordCount
    {
        get
        {
            int count = 0;
            while (TryGetRecord(count, out var r))
            {
                count++;
                if (r.Shot == ConversationRecord.EndOfScene)
                    break;
            }
            return count;
        }
    }

    public ConversationRecord this[int index] =>
        TryGetRecord(index, out var record)
            ? record
            : throw new GameDataException($"Conversation record {index} is outside the scene data.");

    public bool TryGetRecord(int index, out ConversationRecord record)
    {
        int offset = _sceneOffset + index * ConversationRecord.Size;
        if (index < 0 || offset + ConversationRecord.Size > _section.Length)
        {
            record = default;
            return false;
        }
        record = ConversationRecord.Read(_section.Span.Slice(offset, ConversationRecord.Size));
        return true;
    }

    /// <summary>The NUL-terminated string at <paramref name="offset"/> of the text block.</summary>
    public string GetText(int offset) => ReadCString(TextBlock, offset);

    /// <summary>The raw bytes of a NUL-terminated entry (test strings are binary).</summary>
    public ReadOnlySpan<byte> GetBytes(int offset)
    {
        var block = TextBlock;
        if ((uint)offset >= (uint)block.Length)
            return [];
        var tail = block[offset..];
        int end = tail.IndexOf((byte)0);
        return end < 0 ? tail : tail[..end];
    }

    internal static string ReadCString(ReadOnlySpan<byte> block, int offset)
    {
        if ((uint)offset >= (uint)block.Length)
            return "";
        var tail = block[offset..];
        int end = tail.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? tail : tail[..end]);
    }

    /// <summary>
    /// Runs the record's test string: the first test that fires yields the record index to jump
    /// to; otherwise the record stays. Literal port including the original's two-switch layout,
    /// in which unknown codes consume no arguments.
    /// </summary>
    /// <remarks>C: ParseTests (0x438160) and int_value (0x438110), screens.c.</remarks>
    public int EvaluateTests(int recordIndex, ISceneConditions c)
    {
        var record = this[recordIndex];
        var test = new TestReader(TextBlock, record.TestsOffset);
        for (;;)
        {
            int code = test.ReadCode();
            if (code == 0)
                return recordIndex;
            short first, second;
            switch (code)
            {
                case 1:
                    return test.ReadInt();
                case 2:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.MissionScore < first)
                        return second;
                    break;
                case 3:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (first <= c.MissionScore)
                        return second;
                    break;
                case 4:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.WingStatus(first) != 3)
                        return second;
                    break;
                case 5:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.WingStatus(first) == 3)
                        return second;
                    break;
                case 6:
                    first = test.ReadInt();
                    if (c.PlayerKills == 0)
                        return first;
                    break;
                case 7:
                    first = test.ReadInt();
                    if (c.PlayerKills != 0)
                        return first;
                    break;
                case 8:
                    first = test.ReadInt();
                    if (c.WingmanKills == 0)
                        return first;
                    break;
                case 9:
                    first = test.ReadInt();
                    if (c.WingmanKills != 0)
                        return first;
                    break;
                case 10:
                    first = test.ReadInt();
                    if (!c.OfficeVisitPending)
                        return first;
                    break;
                case 11:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (!c.Achieved(first))
                        return second;
                    break;
                case 12:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.Achieved(first))
                        return second;
                    break;
                case 27:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.Sighted(first))
                        return second;
                    break;
                case 29:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.WingStatus(first) == 2)
                        return second;
                    break;
                case 30:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.WingStatus(first) == 1)
                        return second;
                    break;
                case 31:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.AceStatus(first, 1) == 0)
                        return second;
                    break;
                case 32:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.AceStatus(first, 1) != 0)
                        return second;
                    break;
                case 33:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.AceStatus(first, 2) == 0 && c.AceStatus(first, 1) == 0)
                        return second;
                    break;
                case 34:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (c.AceStatus(first, 2) != 0)
                        return second;
                    break;
            }
            switch (code)
            {
                case 13:
                    first = test.ReadInt();
                    if (c.MedalIndex == 4)
                        return first;
                    break;
                case 14:
                    first = test.ReadInt();
                    if (c.MedalIndex < 3)
                        return first;
                    break;
                case 15:
                    first = test.ReadInt();
                    if (c.MedalIndex == 3)
                        return first;
                    break;
                case 16:
                    first = test.ReadInt();
                    if (!c.PromotionPending)
                        return first;
                    break;
                case 17:
                    first = test.ReadInt();
                    if (!c.PlayerEjected)
                        return first;
                    break;
                case 18:
                    first = test.ReadInt();
                    if (c.PlayerEjected && c.EjectionCount == 1)
                        return first;
                    break;
                case 19:
                    first = test.ReadInt();
                    if (!c.PlayerShipTypeChanged)
                        return first;
                    break;
                case 20:
                    first = test.ReadInt();
                    if (c.PlayerShipType != 0)
                        return first;
                    break;
                case 21:
                    first = test.ReadInt();
                    if (c.PlayerShipType != 2)
                        return first;
                    break;
                case 22:
                    first = test.ReadInt();
                    if (c.PlayerShipType != 3)
                        return first;
                    break;
                case 23:
                    first = test.ReadInt();
                    if (c.PlayerShipType != 1)
                        return first;
                    break;
                case 24:
                    first = test.ReadInt();
                    if (c.PlayerShipType != 1 && c.PlayerShipType < c.PreviousPlayerShipType)
                        return first;
                    break;
                case 25:
                    first = test.ReadInt();
                    if (c.PlayerShipType == 1 || c.PlayerShipType >= c.PreviousPlayerShipType)
                        return first;
                    break;
                case 26:
                    first = test.ReadInt();
                    if (c.PlayerEjected && c.EjectionCount > 1)
                        return first;
                    break;
                case 28:
                    first = test.ReadInt();
                    second = test.ReadInt();
                    if (!c.Sighted(first))
                        return second;
                    break;
                case 35:
                    first = test.ReadInt();
                    if (c.PlayersMissionScore() == c.FullMissionScore())
                        return first;
                    break;
                case 36:
                    first = test.ReadInt();
                    if (c.PlayersMissionScore() < c.FullMissionScore())
                        return first;
                    break;
                case 37:
                    first = test.ReadInt();
                    if (c.NoObjectivesAchieved())
                        return first;
                    break;
                case 38:
                    first = test.ReadInt();
                    if (!c.NoObjectivesAchieved())
                        return first;
                    break;
            }
        }
    }

    /// <summary>Cursor over a test string: code bytes and comma/parenthesis terminated decimals.</summary>
    private ref struct TestReader(ReadOnlySpan<byte> block, int offset)
    {
        private readonly ReadOnlySpan<byte> _block = block;
        private int _position = offset;

        public int ReadCode() => _position < _block.Length ? unchecked((sbyte)_block[_position++]) : 0;

        /// <remarks>C: int_value — copies up to ',' or ')' (consumed) and applies atoi.</remarks>
        public short ReadInt()
        {
            int start = _position;
            while (_position < _block.Length && _block[_position] != ',' && _block[_position] != ')' && _block[_position] != 0)
                _position++;
            var digits = _block[start.._position];
            if (_position < _block.Length && _block[_position] != 0)
                _position++;
            return Atoi(digits);
        }

        private static short Atoi(ReadOnlySpan<byte> s)
        {
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t'))
                i++;
            bool negative = false;
            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
                negative = s[i++] == '-';
            int value = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                value = unchecked(value * 10 + (s[i++] - '0'));
            return unchecked((short)(negative ? -value : value));
        }
    }
}

/// <summary>Game state the conversation tests branch on (implemented by the game flow).</summary>
public interface ISceneConditions
{
    /// <remarks>C: stCampaignState.missionScore.</remarks>
    int MissionScore { get; }

    /// <summary>3 alive, 1 died this mission, 2 died earlier.</summary>
    int WingStatus(int personality);

    int PlayerKills { get; }

    int WingmanKills { get; }

    bool OfficeVisitPending { get; }

    bool Achieved(int objective);

    bool Sighted(int objective);

    /// <remarks>C: ace_status(ace, bits).</remarks>
    int AceStatus(int ace, int bits);

    /// <remarks>C: nConversationMedalIndex.</remarks>
    int MedalIndex { get; }

    bool PromotionPending { get; }

    bool PlayerEjected { get; }

    /// <summary>Number of ejections (stored in elapsedDate.year).</summary>
    int EjectionCount { get; }

    bool PlayerShipTypeChanged { get; }

    int PlayerShipType { get; }

    int PreviousPlayerShipType { get; }

    int PlayersMissionScore();

    int FullMissionScore();

    bool NoObjectivesAchieved();
}
