using WingCommander.Audio.Opl;

namespace WingCommander.Audio.Tests.Opl;

public class Ym3812Tests
{
    /// <summary>
    /// Random register scripts (every register except the timer counters, including rhythm
    /// mode, LFO depths, waveforms, KSL, feedback and key-ons) rendered sample by sample.
    /// The golden hashes and ranges were produced by the vendored ymfm C++ sources compiled
    /// with MSVC (scratch harness, see docs/progress/audio.md), so these tests pin the C#
    /// port to bit-exact ymfm output.
    /// </summary>
    [Theory]
    [InlineData(12345u, 4000, 64, 0xfbce11ab0e64280dUL, -5776, 11744)]
    [InlineData(1u, 1000, 256, 0x6d4b473d13782ef9UL, -6896, 6912)]
    public void RandomRegisterScript_MatchesYmfmReference(uint seed, int steps, int samplesPerStep, ulong expectedHash,
        int expectedMin, int expectedMax)
    {
        uint lcg = seed;
        uint NextRandom()
        {
            lcg = unchecked(lcg * 1664525u + 1013904223u);
            return lcg >> 8;
        }

        var chip = new Ym3812();
        chip.Reset();
        chip.Write(0, 0x01);
        chip.Write(1, 0x20);
        var hash = new Fnv1a();
        int min = 0, max = 0;
        for (int step = 0; step < steps; step++)
        {
            int writes = (int)(NextRandom() & 7);
            for (int w = 0; w < writes; w++)
            {
                uint a = NextRandom();
                byte register = (byte)(a & 0xff);
                byte value = (byte)((a >> 8) & 0xff);
                if (register == 0x02 || register == 0x03)
                    continue;
                chip.Write(0, register);
                chip.Write(1, value);
            }
            for (int s = 0; s < samplesPerStep; s++)
            {
                int sample = chip.Generate();
                hash.Add(sample);
                min = Math.Min(min, sample);
                max = Math.Max(max, sample);
            }
        }
        Assert.Equal(expectedMin, min);
        Assert.Equal(expectedMax, max);
        Assert.Equal(expectedHash, hash.Value);
    }

    [Fact]
    public void SampleRate_IsClockOver72()
    {
        var chip = new Ym3812();
        Assert.Equal(49715u, chip.SampleRate(Ym3812.AdLibClock));
    }

    [Fact]
    public void ResetChip_IsSilent_AndKeyOnProducesSound()
    {
        var chip = new Ym3812();
        chip.Reset();
        for (int i = 0; i < 1000; i++)
            Assert.Equal(0, chip.Generate());

        // a plain sustained sine on channel 0: silent modulator, carrier full level, A4-ish
        chip.WriteRegister(0x20, 0x21);
        chip.WriteRegister(0x23, 0x21);
        chip.WriteRegister(0x40, 0x3f);
        chip.WriteRegister(0x43, 0x00);
        chip.WriteRegister(0x60, 0xf0);
        chip.WriteRegister(0x63, 0xf0);
        chip.WriteRegister(0x80, 0x00);
        chip.WriteRegister(0x83, 0x00);
        chip.WriteRegister(0xa0, 0x41);
        chip.WriteRegister(0xb0, 0x32);
        int peak = 0;
        for (int i = 0; i < 5000; i++)
            peak = Math.Max(peak, Math.Abs(chip.Generate()));
        Assert.InRange(peak, 2000, 8191);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(511, 511)]
    [InlineData(1023, 1022)]
    [InlineData(4095, 4088)]
    [InlineData(-4096, -4096)]
    [InlineData(32767, 32704)]
    [InlineData(40000, 32767)]
    [InlineData(-40000, -32768)]
    public void RoundtripFp_TruncatesLikeTheYm3014(int input, int expected) =>
        Assert.Equal(expected, YmfmTables.RoundtripFp(input));

    [Fact]
    public void StatusPort_HasOpl2IdentificationBits() =>
        Assert.Equal(0x06, new Ym3812().ReadStatus() & 0x06);

    [Fact]
    public void Tables_HaveTheDieValuesAtTheEnds()
    {
        Assert.Equal(0x859u, YmfmTables.AbsSinAttenuation(0));
        Assert.Equal(0u, YmfmTables.AbsSinAttenuation(255));
        Assert.Equal(0x859u, YmfmTables.AbsSinAttenuation(511));
        Assert.Equal((uint)((0x3fa | 0x400) << 2), YmfmTables.AttenuationToVolume(0));
        Assert.Equal((uint)(0x400 << 2) >> 3, YmfmTables.AttenuationToVolume(0x3ff));
        Assert.Equal(8u, YmfmTables.AttenuationIncrement(63, 0));
        Assert.Equal(0u, YmfmTables.AttenuationIncrement(0, 5));
        Assert.Equal(0u, YmfmTables.OplKeyScaleAtten(0, 15));
        Assert.Equal(56u, YmfmTables.OplKeyScaleAtten(7, 15));

        // waveform 1 (half sine) is silent in the second half, waveform 3 in every second quarter
        ushort[] silent = [YmfmTables.Waveforms[0][0]];
        Assert.Equal(silent[0], YmfmTables.Waveforms[1][0x200]);
        Assert.Equal(silent[0], YmfmTables.Waveforms[3][0x100]);
        Assert.Equal(YmfmTables.Waveforms[0][0x280] & 0x7fff, YmfmTables.Waveforms[2][0x280]);
    }
}
