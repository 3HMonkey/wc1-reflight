namespace WingCommander.Audio.Director;

/// <summary>
/// Space-object state used for positional sound effects and the proximity sounds of
/// <c>servicetrack</c>. Implemented by Game on top of the simulation; the vector math stays in
/// the simulation, the audio code only applies the thresholds of the original.
/// </summary>
public interface IFlightSoundWorld
{
    /// <summary>C: aeObjectClass[obj] (enum ObjectClass: 9 asteroid, 12 ship, 13 capital ship).</summary>
    int GetObjectClass(int obj);

    /// <summary>C: asObjectDistance[obj].</summary>
    short GetObjectDistance(int obj);

    /// <summary>C: asPreviousObjectDistance[obj].</summary>
    short GetPreviousObjectDistance(int obj);

    /// <summary>C: asObjectScreenX[obj] (0x8001 = not on screen).</summary>
    short GetObjectScreenX(int obj);

    /// <summary>
    /// The "ship passing the eye" test value of <c>servicetrack</c>: with
    /// <c>travel = aShipVelocity[obj] * 0x1400</c> (ScaleFixedVector),
    /// <c>future = aShipPosition[obj] + travel</c>, returns
    /// <c>dot_product(ComputeVectorDelta(eye, future), ComputeVectorDelta(eye, aShipPosition[obj]))</c>
    /// where eye = object 61 (EYE_OBJECT) and <c>ComputeVectorDelta(from, to) = to - from</c>.
    /// </summary>
    int ComputePassingShipDot(int obj);

    /// <summary>
    /// Geometry of a positional sound: <c>delta = aShipPosition[source] - aShipPosition[eye]</c>,
    /// <c>Magnitude = Vector_magnitude(delta)</c>, then <c>NormalizeFixedVector(delta)</c> and
    /// <c>StereoOffset = dot_product(delta, aShipRightVector[eye])</c> (fixed point, 0x100 = 1).
    /// </summary>
    /// <remarks>C: SdlPlayGameSoundEffect geometry part (src/sdl/music.c).</remarks>
    SoundSourceGeometry GetSoundSourceGeometry(int sourceObject);
}
