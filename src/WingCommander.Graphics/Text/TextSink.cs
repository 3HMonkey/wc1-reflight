namespace WingCommander.Graphics.Text;

/// <summary>Where <c>GraphicsContext.FormatTextTokens</c> sends its characters.</summary>
public enum TextSink
{
    /// <summary>Draw each character at the text cursor (C: DrawTextCharacter).</summary>
    Draw,

    /// <summary>Append to the context's string-builder buffer (C: AppendTextCharacter).</summary>
    Append,
}
