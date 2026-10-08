namespace WingCommander.Audio.Director;

/// <summary>Distance and stereo position of a sound source relative to the eye.</summary>
/// <param name="Magnitude">Vector_magnitude of eye-to-source (24.8 fixed point).</param>
/// <param name="StereoOffset">dot_product of the normalised direction with the eye's right vector (0x100 = fully right).</param>
public readonly record struct SoundSourceGeometry(int Magnitude, int StereoOffset);
