using System.Globalization;
using System.Text;
using WingCommander.Audio.OriginFx;
using WingCommander.Core.Resources;

namespace WingCommander.Tools;

internal static partial class Commands
{
    private const int AudioBlockFrames = 1470;

    static partial void RegisterAudioCommands()
    {
        Register("music", "<section 0-40> --out <wav> [--seconds N]  render a MUSIC.MID section (OPL2, 22050 Hz stereo)",
            RenderMusic);
        Register("sfx", "<sound 1-36> --out <wav> [--seconds N]  render a DOS sound effect (centre, full volume)", RenderSfx);
        Register("timbres", "list the AdLib timbre bank (WINGLDR.TIM section 1)", ListTimbres);
        Register("sequence", "<section 0-40> [--events]  dump a MUSIC.MID section: tempo map, cues, meta/FE events",
            DumpSequence);
    }

    private static int RenderMusic(ToolOptions o)
    {
        GameDirectory directory = RequireGameDirectory(o);
        int section = ParseAudioNumber(o.Positional(0));
        string output = o.Option("out") ?? throw new GameDataException("--out <wav> is required");
        int seconds = o.IntOption("seconds", 0);
        var bank = OriginFxTimbreBank.Load(directory);
        var sequence = OriginFxSequence.Load(directory, section);
        long maxFrames = seconds > 0 ? (long)seconds * OriginFxSynth.OutputRate : sequence.EndFrame;
        long capacity = (maxFrames + AudioBlockFrames - 1) / AudioBlockFrames * AudioBlockFrames;
        var samples = new short[capacity * 2];
        var player = new OriginFxSequencer(sequence, bank);
        var cues = new List<(int Position, long Frame)>();
        int lastPosition = player.SequencePosition;
        long rendered = 0;
        while (rendered < maxFrames && !player.Finished)
        {
            player.Render(samples.AsSpan((int)(rendered * 2), AudioBlockFrames * 2), 0x7fff);
            rendered += AudioBlockFrames;
            if (player.SequencePosition != lastPosition)
            {
                lastPosition = player.SequencePosition;
                cues.Add((lastPosition, Math.Min(rendered, player.CurrentFrame)));
            }
        }
        long frames = Math.Min(player.CurrentFrame, maxFrames);
        var pcm = samples.AsSpan(0, (int)(frames * 2));
        Wav.Write(output, pcm, OriginFxSynth.OutputRate, 2);
        var (peak, rmsDb, clipped) = Wav.Measure(pcm);
        Console.WriteLine(AudioText($"section {section} \"{sequence.Name}\": {frames} frames = {frames / (double)OriginFxSynth.OutputRate:0.00} s{(player.Finished ? " (complete)" : "")}"));
        Console.WriteLine(AudioText($"peak {peak}, RMS {rmsDb:0.0} dBFS, {clipped} samples at full scale"));
        foreach (var (position, frame) in cues)
            Console.WriteLine(AudioText($"cue {position} reached by frame {frame} ({frame / (double)OriginFxSynth.OutputRate:0.00} s)"));
        Console.WriteLine($"wrote {output}");
        return 0;
    }

    private static int RenderSfx(ToolOptions o)
    {
        GameDirectory directory = RequireGameDirectory(o);
        int soundNumber = ParseAudioNumber(o.Positional(0));
        if (soundNumber < 1 || soundNumber > OriginFxSoundRecords.Count)
            throw new GameDataException($"sound numbers are 1..{OriginFxSoundRecords.Count}");
        string output = o.Option("out") ?? throw new GameDataException("--out <wav> is required");
        var record = OriginFxSoundRecords.Get(soundNumber);
        bool endless = (record[0] & (OriginFxSoundRecords.FlagSustain | OriginFxSoundRecords.FlagRetrigger)) != 0;
        int seconds = o.IntOption("seconds", endless ? 3 : 10);
        long maxFrames = (long)seconds * OriginFxSynth.OutputRate;
        const long tailFrames = OriginFxSynth.OutputRate / 2;

        var engine = new OriginFxSoundEffectEngine(OriginFxTimbreBank.Load(directory));
        if (!engine.Play(soundNumber, 127, 64, -1, 0))
            throw new GameDataException($"sound {soundNumber} was not accepted");
        var samples = new List<short>();
        var block = new short[AudioBlockFrames * 2];
        long rendered = 0;
        long endedAt = -1;
        while (rendered < maxFrames)
        {
            Array.Clear(block);
            engine.Mix(block, 0x7fff);
            samples.AddRange(block);
            rendered += AudioBlockFrames;
            if (endedAt < 0 && engine.ActiveEffectCount == 0)
                endedAt = rendered;
            if (endedAt >= 0 && !o.Has("seconds") && rendered >= endedAt + tailFrames)
                break;
        }
        var pcm = samples.ToArray();
        Wav.Write(output, pcm, OriginFxSynth.OutputRate, 2);
        var (peak, rmsDb, clipped) = Wav.Measure(pcm);
        Console.WriteLine(AudioText($"sound {soundNumber} record {Convert.ToHexString(record)}: {rendered} frames = {rendered / (double)OriginFxSynth.OutputRate:0.00} s, effect {(endedAt >= 0 ? $"ended by {endedAt / (double)OriginFxSynth.OutputRate:0.00} s" : "still active")}"));
        Console.WriteLine(AudioText($"peak {peak}, RMS {rmsDb:0.0} dBFS, {clipped} samples at full scale"));
        Console.WriteLine($"wrote {output}");
        return 0;
    }

    private static int ListTimbres(ToolOptions o)
    {
        var bank = OriginFxTimbreBank.Load(RequireGameDirectory(o));
        Console.WriteLine($"{bank.Count} AdLib timbres in {OriginFxTimbreBank.FileName} section {OriginFxTimbreBank.AdLibSection} (48 bytes each)");
        Console.WriteLine("idx prog voice    link fbc  vel  bend vib(rt/dp) keytr detune  init  envelope A/B/C/rel (rate>target)                  mod 20 40 60 80 E0   car 20 40 60 80 E0   sfx");
        for (int i = 0; i < bank.Count; i++)
        {
            var t = bank[i];
            var sb = new StringBuilder();
            sb.Append(AudioText($"{i,3} {t.Program,4} {RhythmVoiceName(t.RhythmVoice),-8} {t.Link,4}  {t.FeedbackConnection:X2}  {t.CarrierVelocitySensitivity}/{t.ModulatorVelocitySensitivity} {t.PitchBendRange,5} {t.VibratoRate,4}/{t.VibratoDepth,-4} {t.KeyTracking,5} {t.Detune,6} {t.InitialPitch,5}  "));
            for (int stage = 0; stage < 4; stage++)
            {
                var (rate, target) = t.EnvelopeStage(stage);
                sb.Append(AudioText($"{rate,4}>{target,-6} "));
            }
            sb.Append("  ");
            foreach (byte b in t.ModulatorRegisters)
                sb.Append(AudioText($"{b:X2} "));
            sb.Append("  ");
            foreach (byte b in t.CarrierRegisters)
                sb.Append(AudioText($"{b:X2} "));
            sb.Append(' ');
            sb.Append(SoundsUsingProgram(t.Program));
            Console.WriteLine(sb.ToString().TrimEnd());
        }
        return 0;
    }

    private static int DumpSequence(ToolOptions o)
    {
        GameDirectory directory = RequireGameDirectory(o);
        int section = ParseAudioNumber(o.Positional(0));
        var sequence = OriginFxSequence.Load(directory, section);
        double rate = OriginFxSynth.OutputRate;
        Console.WriteLine(AudioText($"section {section} \"{sequence.Name}\": SMF format {sequence.Format}, {sequence.TrackCount} tracks, division {sequence.Division}"));
        Console.WriteLine(AudioText($"{sequence.Events.Length} playback events, last tick {sequence.MaximumTick}, end frame {sequence.EndFrame} = {sequence.DurationSeconds:0.00} s"));

        Console.WriteLine();
        Console.WriteLine("tempo map:");
        if (sequence.TempoEvents.Length == 0)
            Console.WriteLine(AudioText($"  (none, default {OriginFxSequence.DefaultTempo} us/quarter = 120 BPM)"));
        foreach (var e in sequence.TempoEvents)
            Console.WriteLine(AudioText($"  tick {e.Tick,6}: {e.Tempo} us/quarter = {60000000.0 / e.Tempo:0.##} BPM"));

        Console.WriteLine();
        Console.WriteLine("cues (FE 03):");
        var cues = sequence.GetCues();
        if (cues.Count == 0)
            Console.WriteLine("  (none)");
        int lastCue = -1;
        foreach (var cue in cues)
        {
            if (cue.Data1 == lastCue)
                continue;
            lastCue = cue.Data1;
            Console.WriteLine(AudioText($"  position {cue.Data1} at tick {cue.Tick,6}, frame {cue.Frame,8} ({cue.Frame / rate:0.000} s)"));
        }

        Console.WriteLine();
        Console.WriteLine("meta and OriginFX events:");
        foreach (var meta in sequence.MetaEvents)
            Console.WriteLine("  " + meta);

        if (o.Has("events"))
        {
            Console.WriteLine();
            Console.WriteLine("playback events (frame tick: message):");
            foreach (var e in sequence.Events)
                Console.WriteLine(AudioText($"  {e.Frame,8} {e.Tick,6}: {DescribeMidiEvent(e)}"));
        }
        return 0;
    }

    private static string DescribeMidiEvent(in OriginFxEvent e)
    {
        if (e.Type == OriginFxEventType.Sequence)
            return AudioText($"cue {e.Data1}");
        int channel = e.Channel + 1;
        return e.Command switch
        {
            0x80 => AudioText($"ch{channel,-2} note off {e.Data1} vel {e.Data2}"),
            0x90 => AudioText($"ch{channel,-2} note on  {e.Data1} vel {e.Data2}"),
            0xa0 => AudioText($"ch{channel,-2} key pressure {e.Data1} {e.Data2}"),
            0xb0 => AudioText($"ch{channel,-2} control {e.Data1} = {e.Data2}"),
            0xc0 => AudioText($"ch{channel,-2} program {e.Data1}"),
            0xd0 => AudioText($"ch{channel,-2} channel pressure {e.Data1}"),
            0xe0 => AudioText($"ch{channel,-2} pitch bend {e.Data1 | (e.Data2 << 7)}"),
            _ => AudioText($"status {e.Status:X2} {e.Data1} {e.Data2}"),
        };
    }

    private static string RhythmVoiceName(byte rhythmVoice) => rhythmVoice switch
    {
        0 => "melodic",
        6 => "bassdrum",
        7 => "snare",
        8 => "tom",
        9 => "cymbal",
        10 => "hihat",
        _ => AudioText($"voice{rhythmVoice}"),
    };

    private static string SoundsUsingProgram(int program)
    {
        var sounds = new List<string>();
        for (int n = 1; n <= OriginFxSoundRecords.Count; n++)
            if (OriginFxSoundRecords.Get(n)[1] - 1 == program)
                sounds.Add(n.ToString(CultureInfo.InvariantCulture));
        return sounds.Count == 0 ? "" : "sfx " + string.Join(',', sounds);
    }

    private static int ParseAudioNumber(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new GameDataException($"'{text}' is not a number");

    private static string AudioText(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
