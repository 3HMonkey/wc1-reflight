namespace WingCommander.Audio.Director;

/// <summary>
/// 1-based game sound numbers as passed to <see cref="SoundEffectManager.PlaySfx"/> (catalogue
/// in docs/analysis/audio.md §2.2). Numbers without a known purpose keep a neutral name.
/// </summary>
public static class SoundEffectNumber
{
    public const int MissileLaunch = 1;
    public const int ShipPassing = 2;
    public const int EngineMalfunction = 3;
    public const int Explosion = 4;
    public const int MassDriver = 5;
    public const int AsteroidPassing = 6;
    public const int Sparks = 7;
    public const int Laser = 8;
    public const int ArmourHit = 9;
    public const int ShieldHit = 10;
    public const int LaunchCatapult = 11;
    public const int Afterburner = 12;
    public const int Debris = 13;
    public const int ScrambleKlaxon = 14;
    public const int Landing15 = 15;
    public const int Landing16 = 16;
    public const int Landing17 = 17;
    public const int LandingApproach = 18;
    public const int Launch = 19;
    public const int LaunchDoors = 20;
    public const int Cockpit21 = 21;
    public const int Cockpit22 = 22;
    public const int VduStatic = 23;
    public const int Cockpit24 = 24;
    public const int VduSelect = 25;
    public const int Unused26 = 26;
    public const int Scramble27 = 27;
    public const int Collision = 28;
    public const int Landing29 = 29;
    public const int FuneralVolley = 30;
    public const int Sound31 = 31;
    public const int DamageAlarm = 32;
    public const int Ejection33 = 33;
    public const int Ejection34 = 34;
    public const int Barracks = 35;
    public const int Funeral = 36;

    /// <summary>Highest valid number.</summary>
    public const int Count = 36;
}
