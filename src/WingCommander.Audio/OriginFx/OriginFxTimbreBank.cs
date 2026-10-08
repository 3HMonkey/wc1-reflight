using WingCommander.Core.Resources;

namespace WingCommander.Audio.OriginFx;

/// <summary>
/// The AdLib timbre bank of WINGLDR.TIM (section 1): a count byte followed by 48-byte
/// records. Records are addressed by index; a program is resolved by a linear search of
/// byte 47, falling back to the first record. Immutable and thread-safe.
/// </summary>
/// <remarks>C: OriginFxLoadTimbres, OriginFxFindTimbre, OriginFxNextTimbre (src/sdl/originfx.cpp).</remarks>
public sealed class OriginFxTimbreBank
{
    /// <summary>Size of one timbre record.</summary>
    public const int TimbreSize = 48;

    /// <summary>Section of WINGLDR.TIM that holds the AdLib/OPL2 timbres.</summary>
    /// <remarks>C: SDL_PORT_ADLIB_TIMBRE_SECTION (src/sdl/music.c).</remarks>
    public const int AdLibSection = 1;

    /// <summary>Name of the timbre file inside GAMEDAT.</summary>
    public const string FileName = "WINGLDR.TIM";

    private readonly byte[] _data;
    private readonly int[] _programIndex;

    private OriginFxTimbreBank(byte[] data)
    {
        _data = data;
        Count = data[0];
        _programIndex = new int[256];
        for (int program = 0; program < _programIndex.Length; program++)
            _programIndex[program] = SearchProgram(program);
    }

    /// <summary>Number of records.</summary>
    public int Count { get; }

    /// <summary>The raw bank (count byte plus records), as kept by the reference player.</summary>
    public ReadOnlySpan<byte> RawData => _data;

    /// <summary>Returns a typed view of record <paramref name="index"/>.</summary>
    public OriginFxTimbre this[int index] => new(index, GetRecord(index).ToArray());

    /// <summary>
    /// Copies the bank out of a decoded section. Only the bytes covered by the count are kept,
    /// exactly like the reference.
    /// </summary>
    /// <remarks>C: OriginFxLoadTimbres.</remarks>
    public static OriginFxTimbreBank Parse(ReadOnlySpan<byte> section)
    {
        if (section.Length < TimbreSize + 1)
            throw new GameDataException($"OriginFX timbre bank too small ({section.Length} bytes).");
        int count = section[0];
        int requiredSize = 1 + count * TimbreSize;
        if (count == 0 || requiredSize > section.Length)
            throw new GameDataException($"OriginFX timbre bank declares {count} records but has {section.Length} bytes.");
        return new OriginFxTimbreBank(section[..requiredSize].ToArray());
    }

    /// <summary>Loads section 1 of WINGLDR.TIM from the game directory.</summary>
    /// <remarks>C: SdlInitializeOriginFxAudio (timbre part, src/sdl/music.c).</remarks>
    public static OriginFxTimbreBank Load(GameDirectory directory) =>
        Parse(directory.OpenPacket(FileName).GetSection(AdLibSection).Span);

    /// <summary>The 48 bytes of record <paramref name="index"/>.</summary>
    public ReadOnlySpan<byte> GetRecord(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _data.AsSpan(1 + index * TimbreSize, TimbreSize);
    }

    /// <summary>Program number (byte 47) of a record.</summary>
    public int ProgramOf(int index) => _data[1 + index * TimbreSize + 47];

    /// <summary>
    /// Index of the first record answering to <paramref name="program"/>; the first record
    /// when none does.
    /// </summary>
    /// <remarks>C: OriginFxFindTimbre.</remarks>
    public int FindTimbre(int program) => (uint)program < 256u ? _programIndex[program] : 0;

    /// <summary>
    /// The record layered on top of <paramref name="index"/> (byte 38 set: the next record),
    /// or -1.
    /// </summary>
    /// <remarks>C: OriginFxNextTimbre.</remarks>
    public int NextTimbre(int index)
    {
        if (_data[1 + index * TimbreSize + 38] == 0)
            return -1;
        int offset = 1 + index * TimbreSize;
        if (offset > _data.Length || _data.Length - offset < TimbreSize * 2)
            return -1;
        return index + 1;
    }

    /// <summary>Byte <paramref name="field"/> of record <paramref name="index"/> (hot path, no checks).</summary>
    internal byte Byte(int index, int field) => _data[1 + index * TimbreSize + field];

    /// <summary>Little-endian signed 16-bit field of a record.</summary>
    /// <remarks>C: OriginFxReadLittleEndian16.</remarks>
    internal short Int16(int index, int field)
    {
        int offset = 1 + index * TimbreSize + field;
        return (short)(_data[offset] | (_data[offset + 1] << 8));
    }

    private int SearchProgram(int program)
    {
        for (int index = 0; index < Count; index++)
            if (_data[1 + index * TimbreSize + 47] == program)
                return index;
        return 0;
    }
}
