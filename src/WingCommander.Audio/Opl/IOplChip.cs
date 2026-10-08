namespace WingCommander.Audio.Opl;

/// <summary>
/// A Yamaha OPL2-compatible FM synthesiser. The API mirrors ymfm's chip classes (two-port
/// register writes, one sample per <see cref="Generate"/>) so the OriginFX driver code is
/// independent of the emulator behind it (ADR-005). Implementations are not thread-safe;
/// an instance belongs to exactly one player.
/// </summary>
public interface IOplChip
{
    /// <summary>Native output rate in Hz for a given input clock (clock / 72 for the YM3812).</summary>
    uint SampleRate(uint inputClock);

    /// <summary>Resets registers and voice state (power-on state).</summary>
    void Reset();

    /// <summary>
    /// Writes to a chip port: even <paramref name="offset"/> = address port, odd = data port.
    /// </summary>
    void Write(int offset, byte data);

    /// <summary>Convenience: writes <paramref name="data"/> to register <paramref name="register"/>.</summary>
    void WriteRegister(byte register, byte data);

    /// <summary>Reads the status port.</summary>
    byte ReadStatus();

    /// <summary>Clocks the chip one native sample and returns the mono output (16-bit range).</summary>
    int Generate();
}
