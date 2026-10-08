namespace WingCommander.Simulation;

/// <summary>An <see cref="ISimulationEvents"/> that ignores every call (tests and tools).</summary>
public sealed class NullSimulationEvents : ISimulationEvents
{
    public static readonly NullSimulationEvents Instance = new();

    public void PlaySoundEffect(int effect, int sourceObject)
    {
    }

    public void ReleaseSoundSource(int sourceObject)
    {
    }

    public void WeaponSelectionChanged()
    {
    }

    public void DestinationChanged()
    {
    }

    public void ClearHudGunReadouts()
    {
    }

    public void InitializeCockpit(int cockpitMode)
    {
    }

    public void TrainSimWaveCleared(bool waveActive)
    {
    }

    public void InitializeCockpitView(int mode)
    {
    }

    public void ServiceTrack(short spaceFrame)
    {
    }

    public void NewSpaceMusicChanges(short attacker, short victim)
    {
    }

    public void HouseKeepCockpit(int cameraViewMode)
    {
    }

    public void AfterburnerExpired()
    {
    }

    public void PlayerAfterburnerEngaged(short spaceFrame)
    {
    }

    public void TriggerPlayerHitPaletteFlash()
    {
    }

    public void FlashCockpitPaletteEntry(int entry)
    {
    }

    public void PlaceDamageOnCockpit(short damage)
    {
    }

    public void ShowComponentHitHudMessage(SimulationHudMessage message, int component)
    {
    }

    public void VduMalfunction(int vdu, int sound)
    {
    }

    public void SelectCockpitVduMode(int vdu, int mode)
    {
    }

    public void ShowMissileLockedMessage()
    {
    }

    public void RemoveMissileLockedMessage()
    {
    }

    public void PlayerReleaseWeaponLaunched(Data.ObjectType weaponType, short hardpoint)
    {
    }

    public void SpaceBufferFlash()
    {
    }

    public void ShowCockpitMessage(SimulationCockpitMessage message, Data.ObjectType shipType)
    {
    }

    public void ResetSoundState()
    {
    }
}
