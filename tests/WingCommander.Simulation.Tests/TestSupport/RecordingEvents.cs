using WingCommander.Simulation;
using WingCommander.Simulation.Data;

namespace WingCommander.Simulation.Tests.TestSupport;

/// <summary>An <see cref="ISimulationEvents"/> that records every call as a short text line.</summary>
internal sealed class RecordingEvents : ISimulationEvents
{
    public List<string> Calls { get; } = [];

    public int Count(string prefix) => Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));

    public void PlaySoundEffect(int effect, int sourceObject) => Calls.Add($"sfx {effect} {sourceObject}");

    public void ReleaseSoundSource(int sourceObject) => Calls.Add($"release {sourceObject}");

    public void WeaponSelectionChanged() => Calls.Add("weaponSelection");

    public void DestinationChanged() => Calls.Add("destination");

    public void ClearHudGunReadouts() => Calls.Add("clearGuns");

    public void InitializeCockpit(int cockpitMode) => Calls.Add($"cockpit {cockpitMode}");

    public void TrainSimWaveCleared(bool waveActive) => Calls.Add($"waveCleared {waveActive}");

    public void InitializeCockpitView(int mode) => Calls.Add($"view {mode}");

    public void ServiceTrack(short spaceFrame) => Calls.Add($"serviceTrack {spaceFrame}");

    public void NewSpaceMusicChanges(short attacker, short victim) => Calls.Add($"killMusic {attacker} {victim}");

    public void HouseKeepCockpit(int cameraViewMode) => Calls.Add($"houseKeep {cameraViewMode}");

    public void AfterburnerExpired() => Calls.Add("afterburnerOut");

    public void PlayerAfterburnerEngaged(short spaceFrame) => Calls.Add($"afterburner {spaceFrame}");

    public void TriggerPlayerHitPaletteFlash() => Calls.Add("playerHit");

    public void FlashCockpitPaletteEntry(int entry) => Calls.Add($"hitSide {entry}");

    public void PlaceDamageOnCockpit(short damage) => Calls.Add($"cockpitDamage {damage}");

    public void ShowComponentHitHudMessage(SimulationHudMessage message, int component) => Calls.Add($"hud {message} {component}");

    public void VduMalfunction(int vdu, int sound) => Calls.Add($"vduMalfunction {vdu} {sound}");

    public void SelectCockpitVduMode(int vdu, int mode) => Calls.Add($"selectVdu {vdu} {mode}");

    public void ShowMissileLockedMessage() => Calls.Add("locked");

    public void RemoveMissileLockedMessage() => Calls.Add("lockOff");

    public void PlayerReleaseWeaponLaunched(ObjectType weaponType, short hardpoint) => Calls.Add($"launch {weaponType} {hardpoint}");

    public void SpaceBufferFlash() => Calls.Add("flash");

    public void ShowCockpitMessage(SimulationCockpitMessage message, ObjectType shipType) => Calls.Add($"message {message} {shipType}");

    public void ResetSoundState() => Calls.Add("resetSound");
}
