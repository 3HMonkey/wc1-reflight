namespace WingCommander.Core.Resources;

/// <summary>
/// Raised when a game data file is missing, truncated or structurally invalid.
/// The original game printed a message and exited; the port throws instead.
/// </summary>
public sealed class GameDataException : Exception
{
    public GameDataException(string message) : base(message)
    {
    }

    public GameDataException(string message, Exception inner) : base(message, inner)
    {
    }
}
