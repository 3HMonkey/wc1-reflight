using System.Text;
using WingCommander.Game.Campaign;
using WingCommander.Game.Scenes;

namespace WingCommander.Tools;

internal static partial class Commands
{
    static partial void RegisterGameCommands()
    {
        Register("campaign", "[0|1|2]  series tree, thresholds, branches and medal rows of CAMP.00x", CampaignCommand);
        Register("briefing", "<series> <mission> [--campaign N] [--part briefing|debriefing|rec0|rec1|rec2]  conversation script", BriefingCommand);
        Register("saves", "list the SAVEGAME.WLD bunks", SavesCommand);
        Register("snap", "--at ms[,ms...] [--input \"ms key|up|click a b; ...\"] [--audio] [--skip-intro] [--args \"p ...\"] [--hd WxH] [--gpu WxH [--filter f] [--square-pixels] [--integer]] [--original-fonts] [--classic-text] [--classic-space] [--out dir]  run the game headless, save PNGs (CPU text reference with --hd, Vulkan offscreen with --gpu)", SnapCommand);
    }

    private static readonly string[] ShipNames = ["Hornet", "Rapier", "Scimitar", "Raptor"];

    private static string Ship(int type) => (uint)type < (uint)ShipNames.Length ? ShipNames[type] : type.ToString();

    private static int CampaignCommand(ToolOptions o)
    {
        int campaign = int.Parse(o.PositionalOrNull(0) ?? "0");
        var directory = RequireGameDirectory(o);
        var camp = CampaignFile.Load(directory, campaign);
        Console.WriteLine($"CAMP.00{campaign}: series tree");
        Console.WriteLine("series missions threshold  win->(series,ship)     lose->(series,ship)    post  debrief");
        for (int s = 1; s <= CampaignFile.SeriesCount; s++)
        {
            var r = camp.GetSeries(s);
            if (r.MissionCount == 0)
                continue;
            string post = r.PostSeriesSequence switch
            {
                -1 => "-",
                0x40 => "victory",
                0x41 => "escape",
                var v => $"MIDGAME.V0{v}",
            };
            Console.WriteLine($"{s,6} {r.MissionCount,8} {r.ScoreThreshold,9}  {r.WinNextSeries,3} {Ship(r.WinShipType),-12}     {r.LoseNextSeries,3} {Ship(r.LoseShipType),-12}  {post,-12} {r.DebriefPersonality}");
            for (int m = 0; m < r.MissionCount; m++)
            {
                var mission = r.Missions[m];
                string scores = string.Join(",", mission.ObjectiveScores.TakeWhile((_, i) => i < 16).Select(v => v.ToString()));
                Console.WriteLine($"         mission {m}: medal {mission.MedalIndex} at {mission.MedalThreshold}, objective points [{scores}]");
            }
        }
        return 0;
    }

    private static int BriefingCommand(ToolOptions o)
    {
        int series = int.Parse(o.Positional(0));
        int mission = int.Parse(o.Positional(1));
        int campaign = o.IntOption("campaign", 0);
        string part = o.Option("part") ?? "briefing";
        var directory = RequireGameDirectory(o);
        var file = BriefingFile.Load(directory, campaign);
        if (!file.HasMission(series, mission))
        {
            Console.Error.WriteLine($"no conversations for series {series} mission {mission}");
            return 1;
        }
        var m = file.GetMission(series, mission);
        var script = part switch
        {
            "debriefing" => m.Debriefing,
            "rec0" => m.RecRoom[0],
            "rec1" => m.RecRoom[1],
            "rec2" => m.RecRoom[2],
            _ => m.Briefing,
        };
        Console.WriteLine($"BRIEFING.00{campaign} series {series} mission {mission}: {part}, {script.RecordCount} records");
        for (int i = 0; i < script.RecordCount; i++)
        {
            var r = script[i];
            if (r.Shot == ConversationRecord.EndOfScene)
            {
                Console.WriteLine($"[{i,2}] end");
                break;
            }
            var line = new StringBuilder($"[{i,2}] shot {r.Shot,3} colour {r.TextColour,3} talker {r.Talker,3} hold {r.Duration,4}");
            if (r.TestsOffset != 0)
                line.Append(" tests ").Append(DescribeTests(script.GetBytes(r.TestsOffset)));
            Console.WriteLine(line.ToString());
            string text = script.GetText(r.TextOffset);
            if (text.Length != 0)
                Console.WriteLine($"       \"{text.Replace("\n", "\\n", StringComparison.Ordinal)}\"");
        }
        return 0;
    }

    private static string DescribeTests(ReadOnlySpan<byte> test)
    {
        var sb = new StringBuilder();
        foreach (byte b in test)
        {
            if (b < 0x20)
                sb.Append('<').Append(b).Append('>');
            else
                sb.Append((char)b);
        }
        return sb.ToString();
    }

    private static int SavesCommand(ToolOptions o)
    {
        var directory = RequireGameDirectory(o);
        string path = directory.Resolve(SaveGameFile.FileName);
        for (int i = 0; i < SaveGameFile.SlotCount; i++)
        {
            var slot = SaveGameFile.ReadSlot(path, i);
            if (slot is null)
            {
                Console.WriteLine($"bunk {i}: unreadable");
                continue;
            }
            if (!slot.Occupied)
            {
                Console.WriteLine($"bunk {i}: empty (\"{slot.Description}\")");
                continue;
            }
            var c = slot.Campaign;
            var player = slot.Pilots[CampaignState.PlayerPilotIndex];
            Console.WriteLine($"bunk {i}: \"{slot.Description}\" campaign {c.CampaignIndex} series {c.CurrentSeries} mission {c.CurrentMission}, " +
                              $"{player.Name} \"{player.Callsign}\" {TextMacros.RankNames[Math.Clamp((int)player.Rank, 0, 4)]}, " +
                              $"{player.Missions} missions, {player.Kills} kills, date {c.CurrentDate.Year}.{c.CurrentDate.Day:000}");
        }
        return 0;
    }
}
