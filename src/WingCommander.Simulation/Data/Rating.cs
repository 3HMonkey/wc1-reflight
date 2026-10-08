namespace WingCommander.Simulation.Data;

/// <summary>
/// Pilot level / rating enum. As a pilot level (<c>aiPilotLevel</c>): 0..4 generic AI skill,
/// 5..12 named Confed wingmen, 13 the player, 14..17 Kilrathi aces. <c>acShipRating</c> stores
/// <c>level - 5</c> (or -1 for generic pilots).
/// </summary>
/// <remarks>C: enum Rating (include/wcdata.h). The four ace identifiers are source placeholders;
/// the displayed names are Bhurak, Dakhath, Khajja and Bakhtosh.</remarks>
public enum Rating
{
    Provincial = 0,
    Line = 1,
    Crack = 2,
    Elite = 3,
    Fanatical = 4,
    AceSpirit = 5,
    AceHunter = 6,
    AceBossman = 7,
    AceIceman = 8,
    AceAngel = 9,
    AcePaladin = 10,
    AceManiac = 11,
    AceKnight = 12,
    AcePlayer = 13,
    AceHewey = 14,
    AceLewey = 15,
    AceDewey = 16,
    AceDaffy = 17,
}
