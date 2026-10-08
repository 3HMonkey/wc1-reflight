namespace WingCommander.Game.Scenes;

/// <summary>One step of a talking-head animation: frame (or <see cref="LoopMarker"/>) and ticks.</summary>
public readonly record struct AnimationStep(short Frame, short Ticks)
{
    /// <summary>Face scripts: "R" restarts the script; <see cref="Ticks"/> then holds the step index.</summary>
    public const short LoopMarker = -2;
}

/// <summary>Parsers for the mouth and face scripts embedded in conversation text blocks.</summary>
/// <remarks>C: ParseMouthAnimation (0x404D70), ParseFaceAnimation (0x404CD0), cmpgn.c.</remarks>
public static class AnimationScripts
{
    /// <summary>Mouth frame for each lowercase phoneme letter a..z; '$' = frame 9.</summary>
    /// <remarks>C: asMouthFramesByPhoneme (0x004655F0).</remarks>
    public static ReadOnlySpan<short> MouthFramesByPhoneme =>
        [0, 5, 4, 4, 1, 8, 4, 7, 0, 4, 4, 7, 5, 4, 2, 5, 6, 4, 4, 4, 3, 4, 6, 4, 4, 4];

    /// <summary>
    /// "wevgotalotof..." / "p10$100": each lowercase letter (or '$') becomes a mouth frame held for
    /// the following decimal digits (default 1 tick); every other character is ignored.
    /// </summary>
    public static List<AnimationStep> ParseMouth(ReadOnlySpan<byte> text)
    {
        var steps = new List<AnimationStep>();
        int i = 0;
        while (i < text.Length && text[i] != 0)
        {
            byte c = text[i++];
            short frame;
            if (c == '$')
                frame = 9;
            else if (c >= 'a' && c <= 'z')
                frame = MouthFramesByPhoneme[c - 'a'];
            else
                continue;
            short ticks = 1;
            int value = 0;
            bool digits = false;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                value = value * 10 + (text[i] - '0');
                digits = true;
                i++;
            }
            if (digits)
                ticks = unchecked((short)value);
            steps.Add(new AnimationStep(frame, ticks));
        }
        return steps;
    }

    /// <summary>
    /// "RA45,81,01,A35,..." : 'R' marks the loop point, otherwise one hex digit frame (0-9, A-F)
    /// followed by a decimal duration terminated by ','. Frame 10 (A) means "no overlay".
    /// </summary>
    public static List<AnimationStep> ParseFace(ReadOnlySpan<byte> text)
    {
        var steps = new List<AnimationStep>();
        short sequenceIndex = 0;
        int i = 0;
        while (i < text.Length && text[i] != 0)
        {
            byte c = text[i++];
            if (c == 'R')
            {
                steps.Add(new AnimationStep(AnimationStep.LoopMarker, sequenceIndex));
                continue;
            }
            short frame = c >= 'A' && c <= 'F' ? (short)(c - 'A' + 10) : (short)(c - '0');
            int value = 0;
            int start = i;
            while (i < text.Length && text[i] != ',' && text[i] != 0)
                i++;
            for (int k = start; k < i; k++)
            {
                if (text[k] < '0' || text[k] > '9')
                    break;
                value = value * 10 + (text[k] - '0');
            }
            if (i < text.Length && text[i] == ',')
                i++;
            steps.Add(new AnimationStep(frame, unchecked((short)value)));
            sequenceIndex++;
        }
        return steps;
    }
}
