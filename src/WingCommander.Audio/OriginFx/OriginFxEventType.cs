namespace WingCommander.Audio.OriginFx;

/// <summary>Kinds of scheduled sequence events.</summary>
/// <remarks>C: enum OriginFxEventType (src/sdl/originfx.cpp).</remarks>
public enum OriginFxEventType : byte
{
    /// <summary>MIDI channel message (0x80..0xEF).</summary>
    Channel = 0,

    /// <summary>Tempo meta event (FF 51); removed from the playback list after timing.</summary>
    Tempo = 1,

    /// <summary>OriginFX sequence cue (FE 03 len n): sets the sequence position.</summary>
    Sequence = 2,
}
