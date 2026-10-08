namespace WingCommander.Audio.OriginFx;

/// <summary>
/// One scheduled event of a parsed MUSIC.MID section: absolute tick, absolute output frame at
/// 22050 Hz, original parse order, and the message bytes.
/// </summary>
/// <remarks>C: OriginFxEvent (src/sdl/originfx.cpp).</remarks>
public readonly record struct OriginFxEvent(
    long Tick,
    long Frame,
    int Order,
    OriginFxEventType Type,
    byte Status,
    byte Data1,
    byte Data2,
    int Tempo)
{
    /// <summary>MIDI channel 0..15 of a channel event.</summary>
    public int Channel => Status & 0x0f;

    /// <summary>Message kind of a channel event (0x80, 0x90, 0xB0, 0xC0, 0xD0, 0xE0, 0xA0).</summary>
    public int Command => Status & 0xf0;
}
