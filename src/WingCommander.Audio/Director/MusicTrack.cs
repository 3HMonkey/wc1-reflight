namespace WingCommander.Audio.Director;

/// <summary>
/// Music track numbers = MUSIC.MID section indices (names from the SMF track-name events,
/// see docs/analysis/audio.md §2.5).
/// </summary>
public static class MusicTrack
{
    public const int None = -1;
    public const int RegularCombat = 0;
    public const int BeingTailed = 1;
    public const int TailingAnEnemy = 2;
    public const int MissileTrackingYou = 3;
    public const int SeverelyDamaged = 4;
    public const int IntenseCombat = 5;
    public const int TargetHit = 6;
    public const int AllyKilled = 7;
    public const int WingmanHit = 8;
    public const int EnemyAceKilled = 9;
    public const int OverallVictory = 10;
    public const int OverallDefeat = 11;
    public const int ReturningDefeated = 12;
    public const int ReturningNormal = 13;
    public const int ReturningTriumphant = 14;
    public const int FlyingToDogfight = 15;
    public const int DefendingTheClaw = 16;
    public const int StrikeMission = 17;
    public const int EscortMission = 18;
    public const int StartupIntro = 19;
    public const int ArcadeTheme = 20;
    public const int ArcadeVictory = 21;
    public const int ArcadeDeath = 22;
    public const int TitleFanfare = 23;
    public const int BriefingIntro = 24;
    public const int BriefingMiddle = 25;
    public const int BriefingEnd = 26;
    public const int Scramble = 27;
    public const int Landing = 28;
    public const int DamageAssessment = 29;
    public const int RecRoom = 30;
    public const int EjectImminentRescue = 31;
    public const int Funeral = 32;
    public const int DebriefingSuccessful = 33;
    public const int DebriefingUnsuccessful = 34;
    public const int Barracks = 35;
    public const int CommandersOffice = 36;
    public const int MedalCeremony = 37;
    public const int MedalPurpleHeart = 38;
    public const int MedalMinorBravery = 39;
    public const int MedalMajorBravery = 40;

    /// <summary>Number of sections in MUSIC.MID.</summary>
    public const int Count = 41;
}
