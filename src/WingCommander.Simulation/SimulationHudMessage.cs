namespace WingCommander.Simulation;

/// <summary>The component messages the simulation shows in the left VDU's HUD line
/// (<see cref="ISimulationEvents.ShowComponentHitHudMessage"/>).</summary>
/// <remarks>C: the ShowComponentHitHudMessage call sites of cockpt.c, ship.c and logic.c.</remarks>
public enum SimulationHudMessage
{
    /// <summary><c>sprintf(szComponentHitFormat, apszComponentNames[component])</c>, red, 5 flashes.</summary>
    /// <remarks>C: damage_your_component (0x414BF0, cockpt.c).</remarks>
    ComponentHit,

    /// <summary><c>sprintf(szComponentFixedFormat, apszComponentNames[component])</c> ("%s FIXD"), red, 8 flashes.</summary>
    /// <remarks>C: ReportComponentRepaired (0x41F5F0, ship.c).</remarks>
    ComponentRepaired,

    /// <summary><c>szWeaponDestroyed</c>, red, 8 flashes.</summary>
    /// <remarks>C: your_internal_damage system 5 (0x41F220, ship.c).</remarks>
    WeaponDestroyed,

    /// <summary><c>szFuelTanksHit</c>, red, 8 flashes.</summary>
    /// <remarks>C: your_internal_damage system 7 (0x41F220, ship.c).</remarks>
    FuelTanksHit,

    /// <summary><c>szNeedLock</c>, yellow, 3 flashes.</summary>
    /// <remarks>C: fire_missile (0x421150, logic.c).</remarks>
    NeedMissileLock,
}
