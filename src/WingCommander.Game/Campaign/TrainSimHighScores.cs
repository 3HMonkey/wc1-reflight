using WingCommander.Core.Numerics;

namespace WingCommander.Game.Campaign;

/// <summary>One line of the simulator ranking: pilot (0..7 wingmen, 8 player, 9..14 built-in names) and score.</summary>
/// <remarks>C: HighScoreEntry (5 packed bytes).</remarks>
public struct HighScoreEntry
{
    public sbyte PilotIndex;
    public uint Score;
}

/// <summary>
/// The TrainSim ranking shown in the rec room. Not saved with the campaign: it is regenerated
/// randomly for every new campaign and grows a little after each mission.
/// </summary>
/// <remarks>C: aHighScoreEntries (0x005A7C30) and pilot.cpp 0x425DF0-0x4260DF.</remarks>
public sealed class TrainSimHighScores
{
    public const int EntryCount = 6;

    /// <remarks>C: aszBuiltInHighScores (0x00469E38), pilot indices 9..14.</remarks>
    public static readonly string[] BuiltInNames = ["BISHOP", "GOBLIN", "JEFFTEP", "MANGLER", "THE MAN", "MONGO"];

    public HighScoreEntry[] Entries { get; } = new HighScoreEntry[EntryCount];

    /// <summary>Name shown for entry <paramref name="index"/>.</summary>
    /// <remarks>C: GetHighScoreEntry (0x425DF0).</remarks>
    public string GetName(int index, IReadOnlyList<PilotRecord> pilots)
    {
        int k = Entries[index].PilotIndex;
        return k > 8 ? BuiltInNames[k - 9] : pilots[k].Callsign;
    }

    /// <remarks>C: SetHighScoreEntry (0x425E30).</remarks>
    public void Set(int index, int pilot, uint score)
    {
        Entries[index].PilotIndex = unchecked((sbyte)pilot);
        Entries[index].Score = score;
    }

    /// <summary>Descending sort by signed score (exchange sort as in the original).</summary>
    /// <remarks>C: SortTrainSimHighScores (0x425E50).</remarks>
    public void Sort()
    {
        for (int outer = 0; outer < EntryCount; outer++)
        {
            for (int inner = outer + 1; inner < EntryCount; inner++)
            {
                if ((int)Entries[outer].Score < (int)Entries[inner].Score)
                    (Entries[outer], Entries[inner]) = (Entries[inner], Entries[outer]);
            }
        }
    }

    /// <summary>Index of the pilot's entry, scanning from the bottom; -1 when absent.</summary>
    /// <remarks>C: FindTrainSimHighScore (0x425ED0).</remarks>
    public int Find(int pilot)
    {
        for (int index = EntryCount - 1; index >= 0; index--)
        {
            if (Entries[index].PilotIndex == pilot)
                return index;
        }
        return -1;
    }

    /// <summary>Updates the pilot's entry (or replaces the last line), re-sorts and returns the new rank.</summary>
    /// <remarks>C: InsertTrainSimHighScore (0x425EF0).</remarks>
    public int Insert(int pilot, uint score)
    {
        int existing = Find(pilot);
        Set(existing != -1 ? existing : EntryCount - 1, pilot, score);
        Sort();
        return Find(pilot);
    }

    /// <summary>Five random distinct pilots (not the player) with descending scores below ~12000, the player last with 0.</summary>
    /// <remarks>C: InitializeTrainSimHighScores (0x425F40).</remarks>
    public void Initialize(CRandom random)
    {
        int score = random.BelowOrEqual(2000) + 10000;
        for (int slot = 0; slot < 5; slot++)
        {
            int candidate;
            do
            {
                do
                {
                    candidate = random.InRange(0, 14);
                }
                while (candidate == 8);
                for (int previous = 0; previous < slot; previous++)
                {
                    if (Entries[previous].PilotIndex == candidate)
                        candidate = -1;
                }
            }
            while (candidate == -1);
            score -= random.BelowOrEqual(1500) + 100;
            Set(slot, candidate, unchecked((uint)score));
        }
        Set(5, 8, 0);
    }

    /// <summary>After each mission three living pilots improve their scores.</summary>
    /// <remarks>C: AddRandomTrainSimHighScores (0x426000).</remarks>
    public void AddRandomScores(CRandom random, CampaignState campaign)
    {
        short scale = 1;
        for (int remaining = 3; remaining != 0; remaining--)
        {
            int pilot;
            do
            {
                pilot = random.InRange(0, 14);
            }
            while (pilot == 8 ||
                   (pilot < 9 && campaign.PersonalityDeathMission[pilot] != 0) ||
                   (Find(pilot) == -1 && random.Below(100) > 20));
            int slot = Find(pilot);
            if (slot == -1)
                slot = random.InRange(0, 5);
            int baseScore = (int)Entries[slot].Score;
            int scoreRange = (int)Entries[0].Score - baseScore + 2000;
            while (scoreRange / scale > 30000)
                scale = unchecked((short)(scale * 2));
            Insert(pilot, unchecked((uint)(random.BelowOrEqual(unchecked((short)(scoreRange / scale))) * scale + baseScore + 50)));
        }
    }
}
