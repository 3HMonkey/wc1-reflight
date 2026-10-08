using System.Buffers.Binary;
using WingCommander.Core.Resources;

namespace WingCommander.Audio.OriginFx;

/// <summary>
/// One parsed MUSIC.MID section (a Standard MIDI File with OriginFX extensions): every track
/// merged into one list of channel and cue events sorted by (tick, parse order), with
/// absolute output frames at 22050 Hz computed from the tempo map. Immutable and thread-safe,
/// so it can be parsed once and shared by any number of <see cref="OriginFxSequencer"/>s.
/// </summary>
/// <remarks>C: OriginFxLoadMidi, OriginFxParseTrack, OriginFxReadVariableLength (src/sdl/originfx.cpp).</remarks>
public sealed class OriginFxSequence
{
    /// <summary>Output rate the event frames refer to.</summary>
    public const int OutputRate = 22050;

    /// <summary>SMF default tempo (microseconds per quarter note) until the first tempo event.</summary>
    public const int DefaultTempo = 500000;

    /// <summary>Name of the music file inside GAMEDAT.</summary>
    public const string FileName = "MUSIC.MID";

    private readonly OriginFxEvent[] _events;
    private readonly OriginFxEvent[] _tempoEvents;
    private readonly OriginFxMetaEvent[] _metaEvents;
    private readonly long[] _cueFrames;
    private readonly byte[] _cueValues;

    private OriginFxSequence(int format, int trackCount, int division, long maximumTick, long endFrame,
        OriginFxEvent[] events, OriginFxEvent[] tempoEvents, OriginFxMetaEvent[] metaEvents)
    {
        Format = format;
        TrackCount = trackCount;
        Division = division;
        MaximumTick = maximumTick;
        EndFrame = endFrame;
        _events = events;
        _tempoEvents = tempoEvents;
        _metaEvents = metaEvents;
        var cueFrames = new List<long>();
        var cueValues = new List<byte>();
        foreach (var e in events)
        {
            if (e.Type != OriginFxEventType.Sequence)
                continue;
            cueFrames.Add(e.Frame);
            cueValues.Add(e.Data1);
        }
        _cueFrames = cueFrames.ToArray();
        _cueValues = cueValues.ToArray();
    }

    /// <summary>SMF format (0 or 1).</summary>
    public int Format { get; }

    /// <summary>Number of MTrk chunks.</summary>
    public int TrackCount { get; }

    /// <summary>Ticks per quarter note.</summary>
    public int Division { get; }

    /// <summary>Largest end-of-track tick over all tracks.</summary>
    public long MaximumTick { get; }

    /// <summary>Output frame at which the sequence is finished.</summary>
    public long EndFrame { get; }

    /// <summary>Length in seconds at 22050 Hz.</summary>
    public double DurationSeconds => EndFrame / (double)OutputRate;

    /// <summary>Playback events (channel messages and cues) in dispatch order.</summary>
    public ReadOnlySpan<OriginFxEvent> Events => _events;

    /// <summary>Tempo changes (frames are not assigned to these, as in the reference).</summary>
    public ReadOnlySpan<OriginFxEvent> TempoEvents => _tempoEvents;

    /// <summary>SMF meta and OriginFX FE events in file order (for inspection only).</summary>
    public IReadOnlyList<OriginFxMetaEvent> MetaEvents => _metaEvents;

    /// <summary>The sequence name: the first track-name meta event of track 0, if any.</summary>
    public string? Name
    {
        get
        {
            foreach (var meta in _metaEvents)
                if (meta.Track == 0 && meta.Status == 0xff && meta.Type == 3)
                    return meta.Text;
            return null;
        }
    }

    internal OriginFxEvent[] EventArray => _events;

    /// <summary>Loads and parses section <paramref name="section"/> of MUSIC.MID.</summary>
    public static OriginFxSequence Load(GameDirectory directory, int section) =>
        Parse(directory.OpenPacket(FileName).GetSection(section).Span);

    /// <summary>
    /// The sequence position a player reports after <paramref name="playedFrames"/> output
    /// frames: the value of the last cue dispatched so far (events are dispatched before the
    /// frame they are due in is generated), or 0 before the first cue.
    /// </summary>
    /// <remarks>Mirrors OriginFxProcessDueEvents + SdlOriginFxPlayerSequencePosition without rendering.</remarks>
    public int GetSequencePositionAfter(long playedFrames)
    {
        // events with Frame <= playedFrames - 1 have been dispatched
        int low = 0;
        int high = _cueFrames.Length;
        while (low < high)
        {
            int mid = (low + high) >>> 1;
            if (_cueFrames[mid] <= playedFrames - 1)
                low = mid + 1;
            else
                high = mid;
        }
        return low == 0 ? 0 : _cueValues[low - 1];
    }

    /// <summary>True once a player has rendered <paramref name="playedFrames"/> frames and stopped.</summary>
    /// <remarks>Mirrors the end test of SdlMixOriginFxPlayer (all events dispatched and EndFrame reached).</remarks>
    public bool IsFinishedAfter(long playedFrames) => playedFrames >= EndFrame;

    /// <summary>Returns the cue events (OriginFX FE 03) in order.</summary>
    public List<OriginFxEvent> GetCues()
    {
        var cues = new List<OriginFxEvent>();
        foreach (var e in _events)
            if (e.Type == OriginFxEventType.Sequence)
                cues.Add(e);
        return cues;
    }

    /// <summary>Parses a Standard MIDI File exactly like the OriginFX replayer.</summary>
    /// <exception cref="GameDataException">The data is not a valid sequence (the reference returns 0).</exception>
    /// <remarks>C: OriginFxLoadMidi.</remarks>
    public static OriginFxSequence Parse(ReadOnlySpan<byte> midi)
    {
        if (midi.Length < 14 || !midi[..4].SequenceEqual("MThd"u8))
            throw new GameDataException("OriginFX sequence: missing MThd header.");
        uint headerLength = BinaryPrimitives.ReadUInt32BigEndian(midi[4..]);
        long headerSize = (long)headerLength + 8;
        if (headerLength < 6 || headerSize > midi.Length)
            throw new GameDataException("OriginFX sequence: invalid header length.");
        int format = BinaryPrimitives.ReadUInt16BigEndian(midi[8..]);
        int trackCount = BinaryPrimitives.ReadUInt16BigEndian(midi[10..]);
        int division = BinaryPrimitives.ReadUInt16BigEndian(midi[12..]);
        if (format > 1 || trackCount == 0 || (division & 0x8000) != 0 || division == 0)
            throw new GameDataException($"OriginFX sequence: unsupported format {format}, {trackCount} tracks, division {division}.");

        var events = new List<OriginFxEvent>(1024);
        var metaEvents = new List<OriginFxMetaEvent>();
        int cursor = (int)headerSize;
        int eventOrder = 0;
        long maximumTick = 0;
        for (int trackIndex = 0; trackIndex < trackCount; trackIndex++)
        {
            if (midi.Length - cursor < 8 || !midi.Slice(cursor, 4).SequenceEqual("MTrk"u8))
                throw new GameDataException($"OriginFX sequence: track {trackIndex} has no MTrk header.");
            uint trackLength = BinaryPrimitives.ReadUInt32BigEndian(midi[(cursor + 4)..]);
            cursor += 8;
            if ((uint)(midi.Length - cursor) < trackLength)
                throw new GameDataException($"OriginFX sequence: track {trackIndex} is truncated.");
            ParseTrack(midi.Slice(cursor, (int)trackLength), trackIndex, events, metaEvents, ref eventOrder, ref maximumTick);
            cursor += (int)trackLength;
        }
        if (cursor != midi.Length)
            throw new GameDataException("OriginFX sequence: trailing bytes after the last track.");

        // qsort by (tick, order); the order is unique, so any sort gives the same result
        var sorted = events.ToArray();
        Array.Sort(sorted, static (a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Order.CompareTo(b.Order));

        // convert ticks to output frames along the tempo map; tempo events are dropped from playback
        double exactFrame = 0;
        long previousTick = 0;
        int tempo = DefaultTempo;
        var playback = new List<OriginFxEvent>(sorted.Length);
        var tempoEvents = new List<OriginFxEvent>();
        foreach (var e in sorted)
        {
            exactFrame += (double)(e.Tick - previousTick) * OutputRate * tempo / ((double)division * 1000000.0);
            previousTick = e.Tick;
            if (e.Type == OriginFxEventType.Tempo)
            {
                tempo = e.Tempo;
                tempoEvents.Add(e);
            }
            else
            {
                playback.Add(e with { Frame = (long)(ulong)(exactFrame + 0.5) });
            }
        }
        exactFrame += (double)(maximumTick - previousTick) * OutputRate * tempo / ((double)division * 1000000.0);
        long endFrame = (long)(ulong)(exactFrame + 0.5);
        if (playback.Count != 0 && endFrame <= playback[^1].Frame)
            endFrame = playback[^1].Frame + 1;
        if (endFrame == 0)
            endFrame = 1;

        return new OriginFxSequence(format, trackCount, division, maximumTick, endFrame,
            playback.ToArray(), tempoEvents.ToArray(), metaEvents.ToArray());
    }

    /// <remarks>C: OriginFxParseTrack.</remarks>
    private static void ParseTrack(ReadOnlySpan<byte> track, int trackIndex, List<OriginFxEvent> events,
        List<OriginFxMetaEvent> metaEvents, ref int eventOrder, ref long maximumTick)
    {
        int cursor = 0;
        int end = track.Length;
        long tick = 0;
        byte runningStatus = 0;
        while (cursor < end)
        {
            if (!ReadVariableLength(track, ref cursor, out uint delta))
                throw Error(trackIndex, tick, "bad delta time");
            tick += delta;
            if (cursor >= end)
                throw Error(trackIndex, tick, "event missing after delta time");
            byte status;
            if (track[cursor] >= 0x80)
            {
                status = track[cursor++];
            }
            else
            {
                if (runningStatus == 0)
                    throw Error(trackIndex, tick, "data byte without running status");
                status = runningStatus;
            }

            if (status is >= 0x80 and <= 0xef)
            {
                int dataSize = (status & 0xe0) == 0xc0 ? 1 : 2;
                if (end - cursor < dataSize)
                    throw Error(trackIndex, tick, "truncated channel message");
                for (int index = 0; index < dataSize; index++)
                    if (track[cursor + index] >= 0x80)
                        throw Error(trackIndex, tick, "status byte inside channel message");
                events.Add(new OriginFxEvent(tick, 0, eventOrder++, OriginFxEventType.Channel, status,
                    track[cursor], dataSize == 2 ? track[cursor + 1] : (byte)0, 0));
                cursor += dataSize;
                runningStatus = status;
                continue;
            }

            if (status == 0xfe)
            {
                // OriginFX extension: FE subtype length(1 byte) payload; running status survives
                if (end - cursor < 2)
                    throw Error(trackIndex, tick, "truncated OriginFX event");
                byte subtype = track[cursor++];
                int eventLength = track[cursor++];
                if (end - cursor < eventLength)
                    throw Error(trackIndex, tick, "truncated OriginFX payload");
                if (subtype == 3 && eventLength != 0)
                    events.Add(new OriginFxEvent(tick, 0, eventOrder++, OriginFxEventType.Sequence, 0, track[cursor], 0, 0));
                metaEvents.Add(new OriginFxMetaEvent(trackIndex, tick, 0xfe, subtype, track.Slice(cursor, eventLength).ToArray()));
                cursor += eventLength;
                continue;
            }

            if (status == 0xff)
            {
                if (cursor >= end)
                    throw Error(trackIndex, tick, "truncated meta event");
                byte subtype = track[cursor++];
                if (!ReadVariableLength(track, ref cursor, out uint eventLength) || (uint)(end - cursor) < eventLength)
                    throw Error(trackIndex, tick, "truncated meta payload");
                if (subtype == 0x51 && eventLength == 3)
                {
                    int tempo = (track[cursor] << 16) | (track[cursor + 1] << 8) | track[cursor + 2];
                    if (tempo == 0)
                        throw Error(trackIndex, tick, "zero tempo");
                    events.Add(new OriginFxEvent(tick, 0, eventOrder++, OriginFxEventType.Tempo, 0, 0, 0, tempo));
                }
                metaEvents.Add(new OriginFxMetaEvent(trackIndex, tick, 0xff, subtype, track.Slice(cursor, (int)eventLength).ToArray()));
                cursor += (int)eventLength;
                runningStatus = 0;
                continue;
            }

            if (status == 0xf0 || status == 0xf7)
            {
                if (!ReadVariableLength(track, ref cursor, out uint eventLength) || (uint)(end - cursor) < eventLength)
                    throw Error(trackIndex, tick, "truncated sysex");
                cursor += (int)eventLength;
                runningStatus = 0;
                continue;
            }

            throw Error(trackIndex, tick, $"unsupported status 0x{status:X2}");
        }
        if (tick > maximumTick)
            maximumTick = tick;
    }

    /// <summary>Reads a MIDI variable-length quantity of at most four bytes.</summary>
    /// <remarks>C: OriginFxReadVariableLength.</remarks>
    private static bool ReadVariableLength(ReadOnlySpan<byte> data, ref int cursor, out uint value)
    {
        uint result = 0;
        int count = 0;
        byte b;
        do
        {
            if (cursor >= data.Length || count == 4)
            {
                value = 0;
                return false;
            }
            b = data[cursor++];
            result = (result << 7) | (uint)(b & 0x7f);
            count++;
        }
        while ((b & 0x80) != 0);
        value = result;
        return true;
    }

    private static GameDataException Error(int track, long tick, string message) =>
        new($"OriginFX sequence: track {track}, tick {tick}: {message}.");
}
