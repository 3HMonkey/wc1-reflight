// C# port of ymfm (https://github.com/aaronsgiles/ymfm, commit 81aec25), YM3812 subset.
// Copyright (c) 2021, Aaron Giles. All rights reserved. BSD 3-Clause License, see LICENSE-ymfm.txt.

namespace WingCommander.Audio.Opl;

/// <summary>
/// Yamaha YM3812 (OPL2) emulator: a faithful C# port of ymfm's <c>ym3812</c> class including
/// the YM3014 DAC round trip, producing the same integer samples as the C++ original.
/// </summary>
/// <remarks>C++: ymfm::ym3812 (ymfm_opl.h, ymfm_opl.cpp).</remarks>
public sealed class Ym3812 : IOplChip
{
    /// <summary>The usual AdLib clock (NTSC colour burst), giving a native rate of 49715 Hz.</summary>
    public const uint AdLibClock = 3579545;

    private const uint AllChannels = OplRegisters.AllChannels;

    private readonly OplFmEngine _fm = new();
    private byte _address;

    /// <remarks>C++: ym3812::sample_rate.</remarks>
    public uint SampleRate(uint inputClock) => _fm.SampleRate(inputClock);

    /// <remarks>C++: ym3812::reset.</remarks>
    public void Reset() => _fm.Reset();

    /// <remarks>C++: ym3812::read_status.</remarks>
    public byte ReadStatus() => (byte)(_fm.Status() | 0x06);

    /// <remarks>C++: ym3812::read.</remarks>
    public byte Read(int offset) => (offset & 1) == 0 ? ReadStatus() : (byte)0xff;

    /// <remarks>C++: ym3812::write_address (the busy-time hint is ignored by the reference interface).</remarks>
    public void WriteAddress(byte data) => _address = data;

    /// <remarks>C++: ym3812::write_data.</remarks>
    public void WriteData(byte data) => _fm.Write(_address, data);

    /// <remarks>C++: ym3812::write.</remarks>
    public void Write(int offset, byte data)
    {
        if ((offset & 1) == 0)
            WriteAddress(data);
        else
            WriteData(data);
    }

    /// <inheritdoc />
    public void WriteRegister(byte register, byte data)
    {
        WriteAddress(register);
        WriteData(data);
    }

    /// <summary>Generates one native sample.</summary>
    /// <remarks>C++: ym3812::generate (numsamples = 1).</remarks>
    public int Generate()
    {
        // clock the system
        _fm.Clock(AllChannels);

        // update the FM content; mixing details for YM3812 need verification
        int output = 0;
        _fm.Output(ref output, 1, 32767, AllChannels);

        // YM3812 uses an external DAC (YM3014) with mantissa/exponent format
        // convert to 10.3 floating point value and back to simulate truncation
        return YmfmTables.RoundtripFp(output);
    }

    /// <summary>Generates <paramref name="output"/>.Length consecutive samples.</summary>
    public void Generate(Span<int> output)
    {
        for (int i = 0; i < output.Length; i++)
            output[i] = Generate();
    }
}
