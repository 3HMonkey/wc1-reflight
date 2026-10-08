using System.Globalization;
using System.Text;

namespace WingCommander.Game.Scenes;

/// <summary>Values the subtitle macros expand to.</summary>
public interface ITextMacroContext
{
    /// <summary>$A: name of the medal being awarded.</summary>
    string MedalName { get; }

    /// <summary>$C: player callsign.</summary>
    string Callsign { get; }

    /// <summary>$N / $P: player name.</summary>
    string PlayerName { get; }

    /// <summary>$R: rank name of the player.</summary>
    string RankName { get; }

    /// <summary>$S: name of the current system (MODULE section 5).</summary>
    string SystemName { get; }

    /// <summary>$D: current campaign date (year, day).</summary>
    (int Year, int Day) CurrentDate { get; }

    /// <summary>$E: date the pending medal was earned.</summary>
    (int Year, int Day) SavedDate { get; }

    /// <summary>$T: hour and minute bytes of the misnamed elapsed date.</summary>
    (int Hour, int Minute) Time { get; }

    /// <summary>$K / $L: kills of the player and of the wingman this mission.</summary>
    int PlayerKills { get; }

    int WingmanKills { get; }

    /// <summary>$W&lt;d&gt;: name of wingman personality d.</summary>
    string WingmanName(int personality);
}

/// <summary>Expands the '$' macros of conversation subtitles.</summary>
/// <remarks>C: AddPCName (0x404E10, cmpgn.c), formats "%03d.%03d" and "%02d:%02d".</remarks>
public static class TextMacros
{
    /// <remarks>C: apszMedalNames (0x0046E2E0).</remarks>
    public static readonly string[] MedalNames = ["Bronze Star", "Silver Star", "Gold Star", "Golden Sun", "Terran Medal of Valor"];

    /// <remarks>C: apszPilotRankNames (0x00470098).</remarks>
    public static readonly string[] RankNames = ["2ND LT.", "1ST LT.", "CAPTAIN", "MAJOR", "LT. COL."];

    /// <remarks>C: asConversationTextColours (0x004699F0).</remarks>
    public static ReadOnlySpan<byte> ConversationTextColours =>
    [
        0x25, 0xb6, 0x9a, 0x50, 0x94, 0x85, 0x27, 0xa6,
        0xfd, 0x47, 0xaa, 0x0b, 0x09, 0x0d, 0x03, 0x04,
        0x0b, 0x0c, 0x01, 0x0a, 0x06, 0x0e, 0x02, 0x07,
    ];

    public static string Expand(string text, ITextMacroContext context)
    {
        if (!text.Contains('$', StringComparison.Ordinal))
            return text;
        var sb = new StringBuilder(text.Length + 16);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i++];
            if (c != '$')
            {
                sb.Append(c);
                continue;
            }
            if (i >= text.Length)
                break; // the original would read past the terminator here
            char macro = text[i++];
            switch (macro)
            {
                case 'A':
                    sb.Append(context.MedalName);
                    break;
                case 'C':
                    sb.Append(context.Callsign);
                    break;
                case 'D':
                    AppendDate(sb, context.CurrentDate);
                    break;
                case 'E':
                    AppendDate(sb, context.SavedDate);
                    break;
                case 'K':
                    sb.Append(context.PlayerKills.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'L':
                    sb.Append(context.WingmanKills.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'N':
                case 'P':
                    sb.Append(context.PlayerName);
                    break;
                case 'R':
                    sb.Append(context.RankName);
                    if (sb.Length > 0 && sb[^1] == '.' && i < text.Length && text[i] == '.')
                        sb.Length--;
                    break;
                case 'S':
                    sb.Append(context.SystemName);
                    break;
                case 'T':
                {
                    var (hour, minute) = context.Time;
                    sb.Append(hour.ToString("00", CultureInfo.InvariantCulture)).Append(':')
                      .Append(minute.ToString("00", CultureInfo.InvariantCulture));
                    break;
                }
                case 'W':
                    if (i < text.Length)
                        sb.Append(context.WingmanName(text[i++] - '0'));
                    break;
            }
        }
        return sb.ToString();
    }

    private static void AppendDate(StringBuilder sb, (int Year, int Day) date) =>
        sb.Append(date.Year.ToString("000", CultureInfo.InvariantCulture)).Append('.')
          .Append(date.Day.ToString("000", CultureInfo.InvariantCulture));
}
