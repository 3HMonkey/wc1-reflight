using System.Text;
using WingCommander.Game.Scenes;
using WingCommander.Tests;

namespace WingCommander.Game.Tests.Scenes;

public class ConversationTests
{
    private sealed class FakeConditions : ISceneConditions
    {
        public int MissionScore { get; set; }
        public HashSet<int> DeadPilots { get; } = [];
        public int WingStatus(int personality) => DeadPilots.Contains(personality) ? 2 : 3;
        public int PlayerKills { get; set; }
        public int WingmanKills { get; set; }
        public bool OfficeVisitPending { get; set; }
        public bool Achieved(int objective) => objective == 0;
        public bool Sighted(int objective) => true;
        public int AceStatus(int ace, int bits) => bits & 1;
        public int MedalIndex { get; set; }
        public bool PromotionPending { get; set; }
        public bool PlayerEjected { get; set; }
        public int EjectionCount { get; set; }
        public bool PlayerShipTypeChanged { get; set; }
        public int PlayerShipType { get; set; }
        public int PreviousPlayerShipType { get; set; }
        public int PlayersMissionScore() => 3;
        public int FullMissionScore() => 6;
        public bool NoObjectivesAchieved() => false;
    }

    private sealed class FakeMacros : ITextMacroContext
    {
        public string MedalName => "Silver Star";
        public string Callsign => "MAVERICK";
        public string PlayerName => "BLAIR";
        public string RankName => "1ST LT.";
        public string SystemName => "Enyo";
        public (int Year, int Day) CurrentDate => (2654, 5);
        public (int Year, int Day) SavedDate => (2654, 110);
        public (int Hour, int Minute) Time => (6, 0);
        public int PlayerKills => 4;
        public int WingmanKills => 2;
        public string WingmanName(int personality) => personality == 0 ? "TANAKA" : "?";
    }

    [DataFact]
    public void Series_one_mission_zero_header_and_briefing_records()
    {
        var file = BriefingFile.Load(GameData.Require(), 0);
        var section = file.Packet.GetSection(BriefingFile.MissionSection(1, 0)).Span;
        uint[] header = new uint[10];
        for (int i = 0; i < 10; i++)
            header[i] = BitConverter.ToUInt32(section.Slice(i * 4, 4));
        Assert.Equal([40u, 365u, 2696u, 3112u, 6380u, 6497u, 7348u, 7452u, 8462u, 8605u], header);

        var mission = file.GetMission(1, 0);
        Assert.Equal(25, mission.Briefing.RecordCount);
        Assert.Equal(ConversationRecord.EndOfScene, mission.Briefing[24].Shot);
        Assert.False(string.IsNullOrEmpty(mission.Briefing.GetText(mission.Briefing[0].TextOffset)) &&
                     string.IsNullOrEmpty(mission.Briefing.GetText(mission.Briefing[1].TextOffset)));
    }

    [DataTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_conversation_parses_and_tests_stay_in_range(int campaign)
    {
        var file = BriefingFile.Load(GameData.Require(), campaign);
        var conditions = new FakeConditions();
        int scripts = 0;
        for (int series = 1; series <= 13; series++)
        {
            for (int mission = 0; mission < 4; mission++)
            {
                if (!file.HasMission(series, mission))
                    continue;
                var m = file.GetMission(series, mission);
                foreach (var script in new[] { m.Briefing, m.Debriefing }.Concat(m.RecRoom))
                {
                    CheckScript(script, conditions);
                    scripts++;
                }
            }
        }
        CheckScript(file.GetOffice(), conditions);
        CheckScript(file.GetMedalCeremony(), conditions);
        for (int pair = 0; pair < 7; pair++)
            CheckScript(file.GetFuneral(pair), conditions);
        Assert.True(scripts > 10);
    }

    private static void CheckScript(ConversationScript script, ISceneConditions conditions)
    {
        int count = script.RecordCount;
        Assert.True(count > 0);
        for (int i = 0; i < count; i++)
        {
            var r = script[i];
            if (r.Shot == ConversationRecord.EndOfScene)
                break;
            Assert.InRange(r.TextOffset, 0, script.TextBlock.Length);
            AnimationScripts.ParseMouth(script.GetBytes(r.MouthAnimationOffset));
            AnimationScripts.ParseFace(script.GetBytes(r.FaceAnimationOffset));
            if (r.TestsOffset != 0)
            {
                int target = script.EvaluateTests(i, conditions);
                Assert.InRange(target, 0, count - 1);
            }
        }
    }

    [Fact]
    public void Tests_branch_on_pilot_status_and_scores()
    {
        // Text block: a padding byte (offset 0 means "no tests"), then the test string
        // 0x04 "0,5," = if Spirit is dead goto 5; 0x24 "10," (test 36) = score < full -> 10.
        byte[] block = [0, 0x04, (byte)'0', (byte)',', (byte)'5', (byte)',', 0x24, (byte)'1', (byte)'0', (byte)',', 0];
        var section = new byte[2 * ConversationRecord.Size + block.Length];
        section[5] = 1;                                                   // record 0: testsOffset = 1
        section[ConversationRecord.Size] = unchecked((byte)ConversationRecord.EndOfScene);
        block.CopyTo(section, 2 * ConversationRecord.Size);
        var script = new ConversationScript(section, 0, 2 * ConversationRecord.Size);

        var conditions = new FakeConditions();
        Assert.Equal(10, script.EvaluateTests(0, conditions)); // Spirit alive, 3 < 6 -> record 10
        conditions.DeadPilots.Add(0);
        Assert.Equal(5, script.EvaluateTests(0, conditions));
    }

    [Fact]
    public void Mouth_script_maps_phonemes_and_durations()
    {
        var steps = AnimationScripts.ParseMouth(Encoding.ASCII.GetBytes("p10$100a"));
        Assert.Equal([new AnimationStep(5, 10), new AnimationStep(9, 100), new AnimationStep(0, 1)], steps);
    }

    [Fact]
    public void Face_script_has_loop_marker_and_hex_frames()
    {
        var steps = AnimationScripts.ParseFace(Encoding.ASCII.GetBytes("RA45,81,01,"));
        Assert.Equal([new AnimationStep(AnimationStep.LoopMarker, 0), new AnimationStep(10, 45), new AnimationStep(8, 1), new AnimationStep(0, 1)], steps);
    }

    [Fact]
    public void Macros_expand_like_AddPCName()
    {
        var m = new FakeMacros();
        Assert.Equal("Well done, MAVERICK.", TextMacros.Expand("Well done, $C.", m));
        Assert.Equal("Stardate 2654.005, 06:00", TextMacros.Expand("Stardate $D, $T", m));
        Assert.Equal("1ST LT. BLAIR", TextMacros.Expand("$R $N", m));
        Assert.Equal("1ST LT.", TextMacros.Expand("$R.", m));     // the rank's own '.' is dropped
        Assert.Equal("TANAKA got 2", TextMacros.Expand("$W0 got $L", m));
        Assert.Equal("Silver Star on 2654.110 in Enyo", TextMacros.Expand("$A on $E in $S", m));
    }
}
