using System.Text;

namespace WingCommander.Audio.OriginFx;

/// <summary>
/// A non-playing event kept for inspection: SMF meta events (status 0xFF: names, markers,
/// time signatures, end of track) and OriginFX-specific events (status 0xFE subtype len data).
/// The player ignores all of them except FE 03 (cues) and FF 51 (tempo), which also appear
/// in <see cref="OriginFxSequence.Events"/>.
/// </summary>
/// <param name="Track">MTrk index.</param>
/// <param name="Tick">Absolute tick.</param>
/// <param name="Status">0xFF (SMF meta) or 0xFE (OriginFX).</param>
/// <param name="Type">Meta type or FE subtype.</param>
/// <param name="Data">Payload bytes.</param>
public sealed record OriginFxMetaEvent(int Track, long Tick, byte Status, byte Type, byte[] Data)
{
    /// <summary>True for text-like SMF meta events (types 1..7).</summary>
    public bool IsText => Status == 0xff && Type is >= 1 and <= 7;

    /// <summary>Payload as Latin-1 text.</summary>
    public string Text => Encoding.Latin1.GetString(Data);

    public override string ToString()
    {
        string payload = IsText ? $"\"{Text}\"" : Convert.ToHexString(Data);
        return $"track {Track,2} tick {Tick,6}: {Status:X2} {Type:X2} {payload}";
    }
}
