using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Shapes;
using WingCommander.Simulation;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The flight layer as the simulation's presentation side: ISimulationEvents (callbacks at the
// original statement positions), ICockpitState (VDU modes, message line) and IShapeBounds
// (sprite bounds in the space buffer for easy2see).
internal sealed partial class FlightSession : ISimulationEvents, ICockpitState, IShapeBounds
{
    // ------------------------------------------------------------------ ISimulationEvents

    void ISimulationEvents.PlaySoundEffect(int effect, int sourceObject) => Audio.PlaySfx(effect, sourceObject);

    void ISimulationEvents.ReleaseSoundSource(int sourceObject)
    {
        if (sourceObject is >= -1 and < ObjectSlots.Count)
            Audio.Sfx.ClearSourceActive(sourceObject);
    }

    /// <remarks>C: <c>if (get_mode(0) == 1) InvalidateVduMode(0)</c>.</remarks>
    void ISimulationEvents.WeaponSelectionChanged()
    {
        if (GetVduMode(0) == 1)
            InvalidateVduMode(0);
    }

    void ISimulationEvents.DestinationChanged() => InvalidateVduMode(1);

    void ISimulationEvents.ClearHudGunReadouts() => ClearHudGunReadouts();

    void ISimulationEvents.InitializeCockpit(int cockpitMode) => InitializeCockpitResources(cockpitMode);

    void ISimulationEvents.TrainSimWaveCleared(bool waveActive) => TrainSimWaveCleared();

    void ISimulationEvents.InitializeCockpitView(int mode) => InitializeCockpit(mode);

    /// <remarks>C: servicetrack() in Update_3Space.</remarks>
    void ISimulationEvents.ServiceTrack(short spaceFrame) => Audio.Music.ServiceTrack(Audio, Audio, Audio.Sfx, spaceFrame);

    /// <remarks>C: new_space_music_changes(attacker, victim) (draws a random number for unrated kills).</remarks>
    void ISimulationEvents.NewSpaceMusicChanges(short attacker, short victim) =>
        Audio.Music.NewSpaceMusicChanges(Audio, attacker, victim);

    /// <summary>House keeping of the cockpit: the hit lights fade in the front view; elsewhere the damage
    /// alarm is released (never in the Kilrathi Saga, whose alarm handle is never stored).</summary>
    /// <remarks>C: the presentation half of house_keep (0x427D40, main.c).</remarks>
    void ISimulationEvents.HouseKeepCockpit(int cameraViewMode)
    {
        if (cameraViewMode == 0)
        {
            Gfx.FlightPalette.FadeCockpitFlashEntries(Gfx.Palette);
            return;
        }
        if (Audio.Sfx.ReleaseDamageAlarm())
            _cockpitLightGoal[3] = 0;
    }

    void ISimulationEvents.AfterburnerExpired() => Audio.Sfx.OnAfterburnerExpired();

    void ISimulationEvents.PlayerAfterburnerEngaged(short spaceFrame) => Audio.Sfx.ServiceAfterburnerSound(spaceFrame);

    void ISimulationEvents.TriggerPlayerHitPaletteFlash() => Gfx.FlightPalette.TriggerPlayerHitPaletteFlash(Sim.CameraViewMode);

    void ISimulationEvents.FlashCockpitPaletteEntry(int entry)
    {
        if ((uint)entry < FlightPaletteEffects.CockpitFlashEntryCount)
            Gfx.FlightPalette.FlashCockpitEntry(entry);
    }

    void ISimulationEvents.PlaceDamageOnCockpit(short damage) => PlaceDamageOnCockpit(damage);

    /// <remarks>C: the ShowComponentHitHudMessage call sites (cockpt.c, ship.c, logic.c).</remarks>
    void ISimulationEvents.ShowComponentHitHudMessage(SimulationHudMessage message, int component)
    {
        string name = (uint)component < (uint)Cockpit.CockpitTables.ComponentNames.Length
            ? Cockpit.CockpitTables.ComponentNames[component]
            : "";
        switch (message)
        {
            case SimulationHudMessage.ComponentHit:
                ShowComponentHitHudMessage(name + " HIT", PaletteColours.Red, 5);
                break;
            case SimulationHudMessage.ComponentRepaired:
                ShowComponentHitHudMessage(name + " FIXD", PaletteColours.Red, 8);
                break;
            case SimulationHudMessage.WeaponDestroyed:
                ShowComponentHitHudMessage("Weapon destroyed", PaletteColours.Red, 8);
                break;
            case SimulationHudMessage.FuelTanksHit:
                ShowComponentHitHudMessage("Fuel tanks hit", PaletteColours.Red, 8);
                break;
            case SimulationHudMessage.NeedMissileLock:
                ShowComponentHitHudMessage("Need Lock", PaletteColours.Yellow, 3);
                break;
        }
    }

    void ISimulationEvents.VduMalfunction(int vdu, int sound) => VduMalfunction(vdu, sound);

    void ISimulationEvents.SelectCockpitVduMode(int vdu, int mode) => SelectCockpitVduMode(vdu, mode);

    /// <remarks>C: CockpitMessage(PTR_s_MISSILE_LOCKED, cRedColour, 2) in decrement_lock_time.</remarks>
    void ISimulationEvents.ShowMissileLockedMessage() => CockpitMessage(MissileLockedText, PaletteColours.Red, 2);

    /// <remarks>C: remove_message(PTR_s_MISSILE_LOCKED) in lock_off.</remarks>
    void ISimulationEvents.RemoveMissileLockedMessage() => RemoveMessage(MissileLockedText);

    void ISimulationEvents.PlayerReleaseWeaponLaunched(ObjectType weaponType, short hardpoint) =>
        PlayerReleaseWeaponLaunched(weaponType, hardpoint);

    void ISimulationEvents.SpaceBufferFlash() => FlashSpaceBuffer();

    /// <remarks>C: set_global_message (yellow, 3) in auto_pilot_valid and CockpitMessage (yellow, 4) in flag_reached.</remarks>
    void ISimulationEvents.ShowCockpitMessage(SimulationCockpitMessage message, ObjectType shipType)
    {
        switch (message)
        {
            case SimulationCockpitMessage.AlreadyNear:
                SetGlobalMessage(AlreadyNearText, PaletteColours.Yellow, 3);
                break;
            case SimulationCockpitMessage.EnemyNear:
                SetGlobalMessage(EnemyNearText, PaletteColours.Yellow, 3);
                break;
            case SimulationCockpitMessage.HazardNear:
                SetGlobalMessage(HazardNearText, PaletteColours.Yellow, 3);
                break;
            case SimulationCockpitMessage.WaitFor:
                _objectiveStatusMessage.Value = "Wait for " +
                    (shipType == ObjectType.None ? "" : ObjectTypeTable.Get(shipType).DisplayName);
                CockpitMessage(_objectiveStatusMessage, PaletteColours.Yellow, 4);
                break;
            case SimulationCockpitMessage.ObjectiveReached:
                CockpitMessage(ObjectiveReachedText, PaletteColours.Yellow, 4);
                break;
            case SimulationCockpitMessage.AlreadyVisited:
                CockpitMessage(AlreadyVisitedText, PaletteColours.Yellow, 4);
                break;
        }
    }

    void ISimulationEvents.ResetSoundState() => Audio.Sfx.ResetSoundState();

    // ------------------------------------------------------------------ ICockpitState / IShapeBounds

    int ICockpitState.GetVduMode(int vdu) => GetVduMode(vdu);

    bool ICockpitState.MessageShowing() => MessageShowing();

    /// <remarks>C: GetTransformedShapeBounds(&amp;stSpaceBuffer, ...) (0x442050, gr.c).</remarks>
    int IShapeBounds.GetTransformedShapeBounds(int x, int y, ShapeRef shape, int frame, int angle, int scale, int flip,
        Span<short> bounds) =>
        ShapeBounds.GetTransformedShapeBounds(SpaceBuffer, x, y, Shapes.Get(shape), frame, angle, scale, flip, bounds);
}
