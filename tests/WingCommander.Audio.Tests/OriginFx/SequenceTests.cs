using System.Buffers.Binary;
using WingCommander.Audio.Director;
using WingCommander.Audio.OriginFx;
using WingCommander.Core.Resources;
using WingCommander.Tests;

namespace WingCommander.Audio.Tests.OriginFx;

public class SequenceTests
{
    /// <summary>Lengths in seconds from docs/analysis/audio.md §2.5.</summary>
    private static readonly double[] DocumentedSeconds =
    [
        63.4, 10.6, 16.8, 19.1, 25.1, 21.5, 4.8, 14.3, 12.0, 5.9, 7.1, 19.1, 30.6, 28.9, 31.7, 49.4,
        32.1, 29.7, 30.7, 25.2, 40.0, 1.6, 1.7, 164.6, 16.7, 31.0, 11.2, 23.9, 14.1, 20.0, 96.5, 47.5,
        100.0, 52.5, 43.8, 126.3, 108.2, 23.9, 24.0, 16.7, 16.7,
    ];

    public static IEnumerable<object[]> Sections() => AudioTestData.AllSections();

    [DataFact]
    public void MusicMid_Has41Sections()
    {
        Assert.Equal(MusicTrack.Count, AudioTestData.Music.SectionCount);
        Assert.Equal(139707, AudioTestData.Music.DeclaredSize);
    }

    [DataTheory]
    [MemberData(nameof(Sections))]
    public void Section_IsAStandardMidiFile_AndParses(int section)
    {
        ReadOnlySpan<byte> raw = AudioTestData.Music.GetSection(section).Span;
        Assert.True(raw[..4].SequenceEqual("MThd"u8));
        Assert.Equal(1, AudioTestData.Music.GetInfo(section).Flags);

        var sequence = OriginFxSequence.Parse(raw);
        Assert.Equal(1, sequence.Format);
        Assert.Equal(480, sequence.Division);
        Assert.InRange(sequence.TrackCount, 5, 19);
        Assert.NotEmpty(sequence.Events.ToArray());

        var events = sequence.Events;
        for (int i = 1; i < events.Length; i++)
        {
            Assert.True(events[i].Tick >= events[i - 1].Tick);
            Assert.True(events[i].Frame >= events[i - 1].Frame);
            if (events[i].Tick == events[i - 1].Tick)
                Assert.True(events[i].Order > events[i - 1].Order);
        }
        Assert.True(sequence.EndFrame > events[^1].Frame);
        Assert.Equal(DocumentedSeconds[section], sequence.DurationSeconds, 0.06);
    }

    [DataFact]
    public void TrackNames_ComeFromTheFile()
    {
        Assert.Equal("Regular Combat", AudioTestData.Sequence(0).Name);
        Assert.Equal("Current", AudioTestData.Sequence(19).Name);
        Assert.Equal("Arcade Theme", AudioTestData.Sequence(20).Name);
        Assert.Equal("Fanfare", AudioTestData.Sequence(23).Name);
    }

    [DataFact]
    public void Section19_HasSevenCuesAtTheDocumentedTicks()
    {
        var sequence = AudioTestData.Sequence(19);
        var cues = sequence.GetCues().Select(c => (c.Tick, (int)c.Data1)).Distinct().ToList();
        Assert.Equal([(0L, 0), (5300L, 1), (9600L, 2), (18240L, 3), (21240L, 4), (23040L, 5), (24480L, 6)], cues);

        // reference timing (C++ harness): event count and end frame
        Assert.Equal(1396, sequence.Events.Length);
        Assert.Equal(555220, sequence.EndFrame);
        var cueFrames = sequence.GetCues().Select(c => c.Frame).Distinct().ToList();
        Assert.Equal([0L, 121734L, 220500L, 382898L, 466606L, 512442L, 555219L], cueFrames);

        // no other section carries cues
        for (int section = 0; section < MusicTrack.Count; section++)
            if (section != 19)
                Assert.Empty(AudioTestData.Sequence(section).GetCues());
    }

    [DataFact]
    public void Section19_PositionQueriesFollowTheDispatchRule()
    {
        var sequence = AudioTestData.Sequence(19);
        Assert.Equal(0, sequence.GetSequencePositionAfter(0));
        Assert.Equal(0, sequence.GetSequencePositionAfter(121734));
        Assert.Equal(1, sequence.GetSequencePositionAfter(121735));
        Assert.Equal(2, sequence.GetSequencePositionAfter(220501));
        Assert.Equal(5, sequence.GetSequencePositionAfter(555219));
        Assert.Equal(6, sequence.GetSequencePositionAfter(555220));
        Assert.False(sequence.IsFinishedAfter(555219));
        Assert.True(sequence.IsFinishedAfter(555220));
    }

    /// <summary>
    /// The reference accumulates the frame position in long double: 64-bit with MSVC (what the
    /// port reproduces, verified bit-exact against the MSVC-built reference), 80-bit with x86
    /// GCC. Exact rational arithmetic agrees for every event except 103 events in sections 14,
    /// 18, 19 and 35 that fall exactly on a half frame: the 64-bit accumulation lands just below
    /// .5 and rounds down, so those events are one frame (45 us) earlier than "exact"; an
    /// 80-bit build may round either way there.
    /// </summary>
    [DataFact]
    public void EventFrames_EqualExactRationalTiming_ExceptHalfFrameTies()
    {
        var ties = new Dictionary<int, int>();
        for (int section = 0; section < MusicTrack.Count; section++)
        {
            var sequence = AudioTestData.Sequence(section);
            var tempos = sequence.TempoEvents.ToArray();
            foreach (var e in sequence.Events)
            {
                var (exact, isTie) = ExactFrame(e.Tick, tempos, sequence.Division);
                if (exact == e.Frame)
                    continue;
                Assert.True(isTie, $"section {section} tick {e.Tick}: {e.Frame} vs exact {exact}");
                Assert.Equal(exact - 1, e.Frame);
                ties[section] = ties.GetValueOrDefault(section) + 1;
            }
            long end = ExactFrame(sequence.MaximumTick, tempos, sequence.Division).Frame;
            if (end <= sequence.Events[^1].Frame)
                end = sequence.Events[^1].Frame + 1;
            Assert.Equal(end, sequence.EndFrame);
        }
        Assert.Equal([14, 18, 19, 35], ties.Keys.Order());
        Assert.Equal(103, ties.Values.Sum());
    }

    [Fact]
    public void Parse_SimpleTrack_ComputesFramesAtTheDefaultTempo()
    {
        var sequence = OriginFxSequence.Parse(Smf([0x00, 0x90, 0x3c, 0x40, 0x83, 0x60, 0x80, 0x3c, 0x00, 0x00, 0xff, 0x2f, 0x00]));
        Assert.Equal(2, sequence.Events.Length);
        Assert.Equal(new OriginFxEvent(0, 0, 0, OriginFxEventType.Channel, 0x90, 0x3c, 0x40, 0), sequence.Events[0]);
        Assert.Equal(480, sequence.Events[1].Tick);
        Assert.Equal(11025, sequence.Events[1].Frame);
        Assert.Equal(480, sequence.MaximumTick);
        Assert.Equal(11026, sequence.EndFrame);
    }

    [Fact]
    public void Parse_RunningStatus()
    {
        var sequence = OriginFxSequence.Parse(Smf([0x00, 0x90, 0x3c, 0x40, 0x00, 0x3e, 0x41, 0x00, 0xff, 0x2f, 0x00]));
        Assert.Equal(2, sequence.Events.Length);
        Assert.Equal(0x90, sequence.Events[1].Status);
        Assert.Equal(0x3e, sequence.Events[1].Data1);
        Assert.Equal(0x41, sequence.Events[1].Data2);
    }

    [Fact]
    public void Parse_OriginFxEvents_CueIsScheduled_AndRunningStatusSurvives()
    {
        var sequence = OriginFxSequence.Parse(Smf([
            0x00, 0x90, 0x3c, 0x40,
            0x00, 0xfe, 0x03, 0x01, 0x05,
            0x00, 0xfe, 0x02, 0x02, 0x34, 0x12,
            0x00, 0x3e, 0x40,
            0x00, 0xff, 0x2f, 0x00]));
        Assert.Equal(3, sequence.Events.Length);
        Assert.Equal(OriginFxEventType.Sequence, sequence.Events[1].Type);
        Assert.Equal(5, sequence.Events[1].Data1);
        Assert.Equal(0x90, sequence.Events[2].Status);
        Assert.Contains(sequence.MetaEvents, m => m.Status == 0xfe && m.Type == 2 && m.Data.SequenceEqual(new byte[] { 0x34, 0x12 }));
    }

    [Fact]
    public void Parse_TempoEventChangesTheFrameRate()
    {
        var sequence = OriginFxSequence.Parse(Smf([
            0x00, 0xff, 0x51, 0x03, 0x0f, 0x42, 0x40,
            0x83, 0x60, 0x90, 0x3c, 0x40,
            0x00, 0xff, 0x2f, 0x00]));
        Assert.Single(sequence.TempoEvents.ToArray());
        Assert.Equal(1000000, sequence.TempoEvents[0].Tempo);
        Assert.Single(sequence.Events.ToArray());
        Assert.Equal(22050, sequence.Events[0].Frame);
    }

    [Fact]
    public void Parse_MergesTracksByTickThenOrder()
    {
        var sequence = OriginFxSequence.Parse(Smf(
            [0x00, 0x90, 0x3c, 0x40, 0x83, 0x60, 0x80, 0x3c, 0x00, 0x00, 0xff, 0x2f, 0x00],
            [0x00, 0xc0, 0x05, 0x00, 0xff, 0x2f, 0x00]));
        Assert.Equal([0x90, 0xc0, 0x80], sequence.Events.ToArray().Select(e => (int)e.Status));
        Assert.Equal([0, 2, 1], sequence.Events.ToArray().Select(e => e.Order));
    }

    [Theory]
    [InlineData("meta resets running status")]
    [InlineData("status byte inside message")]
    [InlineData("five byte delta")]
    [InlineData("zero tempo")]
    [InlineData("unsupported status")]
    [InlineData("truncated message")]
    public void Parse_RejectsMalformedTracks(string reason)
    {
        byte[] track = reason switch
        {
            "meta resets running status" => [0x00, 0x90, 0x3c, 0x40, 0x00, 0xff, 0x01, 0x00, 0x00, 0x3e, 0x40],
            "status byte inside message" => [0x00, 0x90, 0x3c, 0x85],
            "five byte delta" => [0x80, 0x80, 0x80, 0x80, 0x00, 0x90, 0x3c, 0x40],
            "zero tempo" => [0x00, 0xff, 0x51, 0x03, 0x00, 0x00, 0x00],
            "unsupported status" => [0x00, 0xf1, 0x00],
            _ => [0x00, 0x90, 0x3c],
        };
        Assert.Throws<GameDataException>(() => OriginFxSequence.Parse(Smf(track)));
    }

    [Fact]
    public void Parse_RejectsBadHeaders()
    {
        byte[] good = Smf([0x00, 0xff, 0x2f, 0x00]);
        Assert.Throws<GameDataException>(() => OriginFxSequence.Parse(good.AsSpan(0, 10)));

        byte[] notMidi = (byte[])good.Clone();
        notMidi[0] = (byte)'X';
        Assert.Throws<GameDataException>(() => OriginFxSequence.Parse(notMidi));

        byte[] format2 = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt16BigEndian(format2.AsSpan(8), 2);
        Assert.Throws<GameDataException>(() => OriginFxSequence.Parse(format2));

        byte[] smpte = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt16BigEndian(smpte.AsSpan(12), 0xe728);
        Assert.Throws<GameDataException>(() => OriginFxSequence.Parse(smpte));

        byte[] trailing = [.. good, 0x00];
        Assert.Throws<GameDataException>(() => OriginFxSequence.Parse(trailing));

        byte[] missingTrack = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt16BigEndian(missingTrack.AsSpan(10), 2);
        Assert.Throws<GameDataException>(() => OriginFxSequence.Parse(missingTrack));
    }

    private static (long Frame, bool IsTie) ExactFrame(long tick, OriginFxEvent[] tempos, int division)
    {
        Int128 numerator = 0;
        long previousTick = 0;
        long tempo = OriginFxSequence.DefaultTempo;
        foreach (var change in tempos)
        {
            if (change.Tick >= tick)
                break;
            numerator += (Int128)(change.Tick - previousTick) * tempo;
            previousTick = change.Tick;
            tempo = change.Tempo;
        }
        numerator += (Int128)(tick - previousTick) * tempo;
        Int128 scaled = numerator * OriginFxSequence.OutputRate;
        Int128 denominator = (Int128)division * 1000000;
        return ((long)((2 * scaled + denominator) / (2 * denominator)), 2 * scaled % (2 * denominator) == denominator);
    }

    private static byte[] Smf(params byte[][] tracks)
    {
        var bytes = new List<byte>();
        bytes.AddRange("MThd"u8.ToArray());
        bytes.AddRange([0, 0, 0, 6, 0, 1, (byte)(tracks.Length >> 8), (byte)tracks.Length, 0x01, 0xe0]);
        foreach (var track in tracks)
        {
            bytes.AddRange("MTrk"u8.ToArray());
            bytes.AddRange([(byte)(track.Length >> 24), (byte)(track.Length >> 16), (byte)(track.Length >> 8), (byte)track.Length]);
            bytes.AddRange(track);
        }
        return bytes.ToArray();
    }
}
