namespace WingCommander.Audio.OriginFx;

/// <summary>Snapshot of one active sound-effect slot (diagnostics and tests).</summary>
/// <param name="SoundNumber">1-based sound number of the current record (changes when chaining).</param>
/// <param name="Tag">Owner tag (source object, -1 for player/UI sounds).</param>
/// <param name="Priority">Priority used for channel stealing.</param>
/// <param name="Channel">MIDI channel 1..8.</param>
/// <param name="CurrentNote">Note currently sounding (glides step it).</param>
/// <param name="RemainingTicks">60 Hz ticks until the record expires.</param>
/// <param name="Volume">CC7 volume 0..127.</param>
/// <param name="Pan">CC10 pan 0..127 (64 = centre).</param>
/// <param name="Age">Allocation age (lower = older).</param>
/// <remarks>C: OriginFxSoundEffect (src/sdl/originfx.cpp).</remarks>
public readonly record struct OriginFxSoundEffectState(
    int SoundNumber,
    int Tag,
    int Priority,
    int Channel,
    int CurrentNote,
    int RemainingTicks,
    int Volume,
    int Pan,
    ulong Age);
