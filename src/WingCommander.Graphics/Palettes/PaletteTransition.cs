namespace WingCommander.Graphics.Palettes;

/// <summary>
/// State of the Bresenham-style palette fade. The first <see cref="Step"/> initialises: per
/// component the distance to the target and a direction of +4/-4; then each call moves
/// components by 4 so that all arrive together after <c>maxDelta / 4</c> steps. Components stop
/// up to 3 short of the target (callers never snap to it). The call that finds the countdown
/// exhausted resets the state and returns false.
/// </summary>
/// <remarks>C: StepPaletteTransition (0x41C510, barracks.c) with nPaletteTransitionInitialise,
/// pPaletteTransitionAccumulator/Delta/Direction, nPaletteTransitionMaxDelta/Countdown. The
/// original keeps one global state shared by its two fades, which never overlap; each fade
/// object here owns its own instance, which is equivalent for sequential fades.</remarks>
public sealed class PaletteTransition
{
    /// <summary>Largest component count (256 entries x RGB).</summary>
    public const int MaxComponents = 768;

    private readonly short[] _accumulator = new short[MaxComponents];
    private readonly short[] _delta = new short[MaxComponents];
    private readonly short[] _direction = new short[MaxComponents];
    private short _maxDelta;
    private short _countdown;
    private bool _initialise = true;

    /// <summary>True between the first step of a fade and the step that finishes it.</summary>
    public bool IsActive => !_initialise;

    /// <summary>Remaining steps of the active fade.</summary>
    public short Countdown => _countdown;

    /// <summary>Largest component distance of the active fade.</summary>
    public short MaxDelta => _maxDelta;

    /// <summary>
    /// Advances <paramref name="current"/> one step toward <paramref name="target"/> (same length,
    /// at most <see cref="MaxComponents"/>). Returns false when the fade is finished.
    /// </summary>
    /// <remarks>C: StepPaletteTransition (0x41C510, barracks.c).</remarks>
    public bool Step(Span<short> current, ReadOnlySpan<short> target)
    {
        int count = current.Length;
        if (count > MaxComponents || target.Length < count)
            throw new ArgumentException("Component count exceeds 768 or the target is too short.", nameof(current));
        if (_initialise)
        {
            _maxDelta = 0;
            for (int i = 0; i < count; i++)
            {
                short difference = unchecked((short)(current[i] - target[i]));
                if (difference < 0)
                {
                    difference = unchecked((short)-difference);
                    _direction[i] = 4;
                }
                else
                {
                    _direction[i] = -4;
                }
                _delta[i] = difference;
                if (_maxDelta < difference)
                    _maxDelta = difference;
            }
            for (int i = 0; i < count; i++)
                _accumulator[i] = (short)(_maxDelta / 4);
            _initialise = false;
            _countdown = (short)(_maxDelta / 4);
        }

        short previous = _countdown;
        _countdown--;
        if (previous == 0)
        {
            _initialise = true;
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            _accumulator[i] = unchecked((short)(_accumulator[i] + _delta[i]));
            if (_accumulator[i] > _maxDelta)
            {
                _accumulator[i] = unchecked((short)(_accumulator[i] - _maxDelta));
                current[i] = unchecked((short)(current[i] + _direction[i]));
            }
        }
        return true;
    }

    /// <summary>Abandons an active fade so the next <see cref="Step"/> initialises again.</summary>
    public void Reset() => _initialise = true;
}
