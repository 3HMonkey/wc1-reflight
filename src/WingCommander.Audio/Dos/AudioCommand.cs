using WingCommander.Audio.OriginFx;

namespace WingCommander.Audio.Dos;

/// <summary>Kinds of game-thread to audio-thread commands.</summary>
internal enum AudioCommandKind : byte
{
    PlaySoundEffect,
    StopSoundEffects,
    SetMusic,
    SetMusicGain,
    SetSoundGain,
}

/// <summary>
/// One message of the <see cref="AudioCommandQueue"/>: a plain struct so posting and
/// consuming never allocate. <see cref="Music"/> hands a fully initialised sequencer (or null
/// to stop) to the audio thread.
/// </summary>
internal struct AudioCommand
{
    public AudioCommandKind Kind;
    public int Arg0;
    public int Arg1;
    public int Arg2;
    public int Arg3;
    public int Arg4;
    public OriginFxSequencer? Music;
}
