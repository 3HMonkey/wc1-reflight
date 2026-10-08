using WingCommander.Core.Resources;

namespace WingCommander.Game.Config;

/// <summary>
/// Game switches from WINGCMDR.CFG, the command line and the cheater flag, interpreted
/// exactly like <c>GameMain</c> and <c>CheckLauncherAndConfig</c>. Most developer switches
/// only work after the token <c>Origin</c> unlocked them (or with the cheater flag).
/// </summary>
/// <remarks>C: main.c GameMain argument loop, winmain.c CheckLauncherAndConfig, sound.c LoadWingCmdrCfgFile.</remarks>
public sealed class StartupOptions
{
    /// <remarks>C: nOriginDevUnlock.</remarks>
    public bool OriginDevUnlock { get; set; }

    /// <remarks>C: nShowMemoryStatus (-m).</remarks>
    public bool ShowMemoryStatus { get; set; }

    /// <remarks>C: bPlayerCollisionResponse (cleared by -b, cfg b, cheater).</remarks>
    public bool PlayerCollisionResponse { get; set; } = true;

    /// <remarks>C: bShowFrameRate (-f, cfg f).</remarks>
    public bool ShowFrameRate { get; set; }

    /// <remarks>C: bPlayerVulnerable (cleared by -k, cfg k, cheater).</remarks>
    public bool PlayerVulnerable { get; set; } = true;

    /// <remarks>C: bCockpitEnabled (cleared by cfg c: cockpitless view).</remarks>
    public bool CockpitEnabled { get; set; } = true;

    /// <remarks>C: bShowKilrathiSagaCredits (cfg token $#SAGA.EXE).</remarks>
    public bool ShowKilrathiSagaCredits { get; set; }

    /// <remarks>C: nStartNavPointOverride (as&lt;n&gt;).</remarks>
    public short StartNavPointOverride { get; set; } = -1;

    /// <summary>1 = r, 2 = a (AdLib), 3 = p, 4 = default.</summary>
    /// <remarks>C: nMusicPlaybackMode.</remarks>
    public short MusicPlaybackMode { get; set; } = 4;

    /// <remarks>C: nArcadeStartupParameter (a&lt;n&gt;, e.g. a904 in the GOG cfg).</remarks>
    public short ArcadeStartupParameter { get; set; }

    /// <summary>Graphics variant: 0 = v (VGA), 1 = e (EGA), 3 = t (Tandy).</summary>
    /// <remarks>C: bSlowSceneAnimation (selects .V??/.E??/.T?? data files).</remarks>
    public byte GraphicsVariant { get; set; }

    /// <remarks>C: DAT_005a7d9c (set by z and unconditionally by GameMain; purpose unknown).</remarks>
    public bool UnknownZFlag { get; set; } = true;

    /// <summary>Dev: start directly in space (l).</summary>
    public bool LaunchMission { get; set; }

    /// <summary>Dev: mission number (m&lt;n&gt; / w&lt;n&gt;).</summary>
    public short Mission { get; set; }

    /// <summary>Dev: series number (s&lt;n&gt;), default 1.</summary>
    public short Series { get; set; } = 1;

    /// <remarks>C: bCampaignActive (s&lt;n&gt;).</remarks>
    public bool CampaignActive { get; set; }

    /// <summary>Dev: run the animation demo for mission w&lt;n&gt;.</summary>
    public bool AnimationDemo { get; set; }

    /// <remarks>C: bDirectDrawModeCascadeEnabled (-q / cfg q); no effect in the port.</remarks>
    public bool DirectDrawCascade { get; set; } = true;

    /// <summary>Messages the original printed ("Version %s.").</summary>
    public List<string> Messages { get; } = [];

    /// <summary>Reads WINGCMDR.CFG from the install root (whitespace-separated tokens), or an empty list.</summary>
    public static IReadOnlyList<string> ReadConfigTokens(GameDirectory directory, string fileName = "WINGCMDR.CFG")
    {
        string? path = FindFile(directory.RootPath, fileName) ?? FindFile(directory.DataPath, fileName);
        if (path is null)
            return [];
        // fscanf("%s") splits on C whitespace only; a NUL byte is read as part of a token and
        // ends the C string there. The GOG file ends with CR LF NUL, which yields a fourth,
        // empty token: the "count - 1" quirk of CombineArguments then drops exactly that one.
        string text = File.ReadAllText(path, System.Text.Encoding.Latin1);
        var tokens = new List<string>();
        foreach (string token in text.Split(CWhitespace, StringSplitOptions.RemoveEmptyEntries))
        {
            int nul = token.IndexOf((char)0);
            tokens.Add(nul >= 0 ? token[..nul] : token);
        }
        return tokens;
    }

    /// <summary>C isspace: space, tab, LF, VT, FF, CR.</summary>
    private static readonly char[] CWhitespace = [(char)32, (char)9, (char)10, (char)11, (char)12, (char)13];

    /// <summary>
    /// Combines config tokens and command-line arguments like <c>LoadWingCmdrCfgFile</c>, which
    /// returns <c>count - 1</c>: the last token is never processed (original quirk, kept).
    /// </summary>
    public static IReadOnlyList<string> CombineArguments(IReadOnlyList<string> configTokens, IReadOnlyList<string> arguments)
    {
        var all = new List<string>(configTokens.Count + arguments.Count);
        all.AddRange(configTokens);
        all.AddRange(arguments);
        if (all.Count > 0)
            all.RemoveAt(all.Count - 1);
        return all;
    }

    /// <summary>Applies the cheater flag and the config-file switches (CheckLauncherAndConfig).</summary>
    public void ApplyLauncherConfig(bool cheater, IReadOnlyList<string> configTokens)
    {
        if (cheater)
        {
            OriginDevUnlock = true;
            PlayerVulnerable = false;
            PlayerCollisionResponse = false;
        }
        foreach (string option in configTokens)
        {
            if (option.StartsWith("$#SAGA.EXE", StringComparison.Ordinal))
                ShowKilrathiSagaCredits = true;
            char command = option.Length > 0 && option[0] == '-' ? Char(option, 1) : Char(option, 0);
            switch (command)
            {
                case 'b':
                    PlayerCollisionResponse = false;
                    break;
                case 'c':
                    CockpitEnabled = false;
                    break;
                case 'f':
                    ShowFrameRate = true;
                    break;
                case 'k':
                    PlayerVulnerable = false;
                    break;
                case 'q':
                    DirectDrawCascade = false;
                    break;
            }
        }
    }

    /// <summary>Interprets the startup arguments exactly like the loop in GameMain.</summary>
    public void ApplyArguments(IReadOnlyList<string> arguments, string gameVersion = "")
    {
        foreach (string argument in arguments)
        {
            if (argument == "Origin")
                OriginDevUnlock = true;

            char first = Char(argument, 0);
            char second = Char(argument, 1);
            switch (first)
            {
                case '?':
                case '-':
                    if (first == '?')
                        Messages.Add($"Version {gameVersion}.");
                    // '?' falls through into '-' in the original.
                    if (second == 'm')
                        ShowMemoryStatus = true;
                    if (OriginDevUnlock)
                    {
                        switch (second)
                        {
                            case 'b':
                                PlayerCollisionResponse = false;
                                break;
                            case 'f':
                                ShowFrameRate = true;
                                break;
                            case 'k':
                                PlayerVulnerable = false;
                                break;
                            case 'q':
                                DirectDrawCascade = false;
                                break;
                        }
                    }
                    break;
                case 'A':
                case 'a':
                    if (second is 's' or 'S')
                    {
                        StartNavPointOverride = Atoi(argument, 2);
                    }
                    else
                    {
                        MusicPlaybackMode = 2;
                        ArcadeStartupParameter = Atoi(argument, 1);
                    }
                    break;
                case 'E':
                case 'e':
                    GraphicsVariant = 1;
                    break;
                case 'P':
                case 'p':
                    MusicPlaybackMode = 3;
                    break;
                case 'R':
                case 'r':
                    MusicPlaybackMode = 1;
                    break;
                case 'T':
                case 't':
                    GraphicsVariant = 3;
                    break;
                case 'V':
                case 'v':
                    GraphicsVariant = 0;
                    break;
                case 'Z':
                case 'z':
                    UnknownZFlag = true;
                    break;
                case 'l':
                    if (OriginDevUnlock)
                        LaunchMission = true;
                    break;
                case 'm':
                    if (OriginDevUnlock)
                        Mission = Atoi(argument, 1);
                    break;
                case 's':
                    if (OriginDevUnlock)
                    {
                        CampaignActive = true;
                        Series = Atoi(argument, 1);
                    }
                    break;
                case 'w':
                    if (OriginDevUnlock)
                    {
                        AnimationDemo = true;
                        Mission = Atoi(argument, 1);
                    }
                    break;
            }
        }
    }

    private static char Char(string s, int index) => index < s.Length ? s[index] : '\0';

    /// <summary>C atoi: optional whitespace and sign, then digits; 0 when none; truncated to short.</summary>
    public static short Atoi(string s, int start)
    {
        int i = start;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
        bool negative = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            negative = s[i] == '-';
            i++;
        }
        int value = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            value = unchecked(value * 10 + (s[i] - '0'));
            i++;
        }
        return unchecked((short)(negative ? -value : value));
    }

    private static string? FindFile(string directory, string name)
    {
        if (!Directory.Exists(directory))
            return null;
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }
}
