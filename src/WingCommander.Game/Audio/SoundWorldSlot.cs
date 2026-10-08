using WingCommander.Audio.Director;

namespace WingCommander.Game.Audio;

/// <summary>
/// The flight sound world the game-wide <see cref="SoundEffectManager"/> queries for positional
/// sounds. The flight layer sets <see cref="Current"/> while a mission runs; outside flight only
/// interface sounds (source -1) are played, which never query the world, and every query
/// answers "no object".
/// </summary>
public sealed class SoundWorldSlot : IFlightSoundWorld
{
    public IFlightSoundWorld? Current { get; set; }

    public int GetObjectClass(int obj) => Current?.GetObjectClass(obj) ?? 0;

    public short GetObjectDistance(int obj) => Current?.GetObjectDistance(obj) ?? short.MaxValue;

    public short GetPreviousObjectDistance(int obj) => Current?.GetPreviousObjectDistance(obj) ?? short.MaxValue;

    public short GetObjectScreenX(int obj) => Current?.GetObjectScreenX(obj) ?? MusicDirector.OffScreen;

    public int ComputePassingShipDot(int obj) => Current?.ComputePassingShipDot(obj) ?? 0;

    public SoundSourceGeometry GetSoundSourceGeometry(int sourceObject) =>
        Current?.GetSoundSourceGeometry(sourceObject) ?? default;
}
