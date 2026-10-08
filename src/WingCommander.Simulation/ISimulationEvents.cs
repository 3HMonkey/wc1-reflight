namespace WingCommander.Simulation;

/// <summary>
/// Outgoing calls of the simulation into audio, music, cockpit/HUD and flight-loop code that the
/// original made directly. Implemented by the Game project; <see cref="NullSimulationEvents"/>
/// ignores everything (tests, tools). Every call is made synchronously at the position of the
/// original statement, because some receivers consume the shared <c>CRandom</c> (the music
/// director, <c>SelectCockpitVduMode</c> through <see cref="SpaceSimulation.Malf"/>): keeping the
/// call order keeps the random sequence of the original.
/// </summary>
public interface ISimulationEvents
{
    /// <summary>Plays sound effect <paramref name="effect"/> positioned at object <paramref name="sourceObject"/>
    /// (-1 = player/cockpit sound).</summary>
    /// <remarks>C: PlaySfxWaveFileByNumber(effect, obj, 0) (music.c).</remarks>
    void PlaySoundEffect(int effect, int sourceObject);

    /// <summary>Releases the positional sound source bound to an object slot (hazard removed).</summary>
    /// <remarks>C: <c>aiSoundEffectSourceActive[obj + 1] = 0</c> in remove_hazard (winmain.c).</remarks>
    void ReleaseSoundSource(int sourceObject);

    /// <summary>The player's gun or release-weapon selection changed: refresh the weapon VDU if it shows.</summary>
    /// <remarks>C: <c>if (get_mode(0) == 1) InvalidateVduMode(0)</c> in remove_weapon, select_new_gun,
    /// select_new_release_weapon.</remarks>
    void WeaponSelectionChanged();

    /// <summary>The current nav destination changed: refresh the navigation VDU.</summary>
    /// <remarks>C: InvalidateVduMode(1) in set_next_destination (cockpt.c).</remarks>
    void DestinationChanged();

    /// <summary>Clears the HUD gun readouts (the cockpit-only part of <c>clean_up_cockpit</c>, which
    /// <c>set_up_action_sphere</c> calls when entering a nav sphere).</summary>
    /// <remarks>C: ClearHudGunReadouts (0x4141D0, cockpt.c).</remarks>
    void ClearHudGunReadouts();

    /// <summary>Loads the cockpit for a mission (4 = training simulator cockpit, else the player's ship type).</summary>
    /// <remarks>C: InitializeCockpitResources (0x4245B0, logic.c) called at the end of init_mission.</remarks>
    void InitializeCockpit(int cockpitMode);

    /// <summary>Training simulator wave bookkeeping at the start of <c>set_up_next_wave</c>: music
    /// cue 21, arcade bonus countdown (60, or 30 while a wave is active), arcade bonus and time.</summary>
    /// <remarks>C: the nTrainSimActive branch of set_up_next_wave (0x40C3C0, brains.c):
    /// spacetrack(21, 2, 0), nArcadeBonusCountdown, GetArcadeBonus, FigureArcadeTime.</remarks>
    void TrainSimWaveCleared(bool waveActive);

    /// <summary>
    /// Sets up the screen for a camera view: draws the cockpit backdrop of <paramref name="mode"/>
    /// (0..3 cockpit front/right/left/rear, 4 letterbox of the external views, 5 letterbox geometry
    /// without backdrop, 6 full screen (death view, fleet overview), 7 escape-pod interior)
    /// and selects the space viewport. The receiver must update <see cref="SpaceSimulation.ScreenWidth"/>,
    /// <see cref="SpaceSimulation.ScreenHeight"/>, <see cref="SpaceSimulation.ViewCenterX"/> and
    /// <see cref="SpaceSimulation.ViewCenterY"/> before returning (they drive the projection).
    /// </summary>
    /// <remarks>C: initialize_cockpit (0x423E90, logic.c) with set_up_screen_viewport (0x436740,
    /// eventmgr.c), called from new_view and SetFleetOverviewView.</remarks>
    void InitializeCockpitView(int mode);

    /// <summary>Once per simulation frame after the camera moved: the in-flight music and the
    /// proximity sounds (consumes random numbers in the music director).</summary>
    /// <remarks>C: servicetrack() in Update_3Space (0x427C50, main.c).</remarks>
    void ServiceTrack(short spaceFrame);

    /// <summary>A ship was destroyed by <paramref name="attacker"/>: the music may change (consumes
    /// random numbers in the music director).</summary>
    /// <remarks>C: new_space_music_changes(attacker, victim) in analyze_kill (0x41FB40, ship.c).</remarks>
    void NewSpaceMusicChanges(short attacker, short victim);

    /// <summary>The presentation half of <c>house_keep</c>: in the cockpit view (<paramref name="cameraViewMode"/>
    /// 0) the six hit-flash palette entries fade, in every other view the damage alarm is released.</summary>
    /// <remarks>C: house_keep (0x427D40, main.c): FadeFlightPaletteEntry/SetPaletteEntry(0xb9 + n) or
    /// FlushSoundEffectsAndLog(nDamageAlarmSfxHandle) and cockpit light 3.</remarks>
    void HouseKeepCockpit(int cameraViewMode);

    /// <summary>An afterburner ran out (any ship, as in the original): stop the afterburner sound if it is current.</summary>
    /// <remarks>C: <c>if (bAfterburnerSfxActive) FlushSoundEffectsAndLog()</c> in accelerate_and_move_object (0x4129A0, spc.c).</remarks>
    void AfterburnerExpired();

    /// <summary>The player fired the afterburner: re-trigger its sound at most every 6 frames.</summary>
    /// <remarks>C: the sound part of your_afterburner (0x421920, logic.c), nAfterburnerSoundDeadline.</remarks>
    void PlayerAfterburnerEngaged(short spaceFrame);

    /// <summary>The player took damage: red flash of the space view (only in views 0..3).</summary>
    /// <remarks>C: TriggerPlayerHitPaletteFlash (0x427C80, main.c) from inflict_damage.</remarks>
    void TriggerPlayerHitPaletteFlash();

    /// <summary>A projectile hit the player: flash the cockpit palette entry of the hit side
    /// (0 rear, 1 front, 2 top, 3 left, 4 bottom, 5 right).</summary>
    /// <remarks>C: <c>aPaletteFadeEntries[entry][0] = 0x38</c> in object_collision (0x4130D0, spc.c).</remarks>
    void FlashCockpitPaletteEntry(int entry);

    /// <summary>Shows cockpit damage picture <paramref name="damage"/> (0..3) when the cockpit is visible.</summary>
    /// <remarks>C: place_damage_on_cockpit (0x4178A0, cockpt.c) from your_internal_damage.</remarks>
    void PlaceDamageOnCockpit(short damage);

    /// <summary>Shows a component message in the left VDU's HUD line (the receiver applies the
    /// original's own test: not in the training simulator and the left VDU working).</summary>
    /// <remarks>C: ShowComponentHitHudMessage (0x414B70, cockpt.c); the callers' VDU-mode conditions
    /// are evaluated by the simulation.</remarks>
    void ShowComponentHitHudMessage(SimulationHudMessage message, int component);

    /// <summary>VDU <paramref name="vdu"/> breaks: static noise with sound <paramref name="sound"/>
    /// (in the cockpit view) and mode 0.</summary>
    /// <remarks>C: vdu_malf (0x414B20, cockpt.c).</remarks>
    void VduMalfunction(int vdu, int sound);

    /// <summary>Switches VDU <paramref name="vdu"/> to <paramref name="mode"/> like the VDU keys
    /// (may consume random numbers through <see cref="SpaceSimulation.Malf"/>).</summary>
    /// <remarks>C: SelectCockpitVduMode (0x417F60, cockpt.c), called by fire_players_lasers.</remarks>
    void SelectCockpitVduMode(int vdu, int mode);

    /// <summary>The missile lock was acquired: show "MISSILE LOCKED".</summary>
    /// <remarks>C: CockpitMessage(PTR_s_MISSILE_LOCKED, cRedColour, 2) in decrement_lock_time (0x416090, cockpt.c).</remarks>
    void ShowMissileLockedMessage();

    /// <summary>The missile lock was dropped: remove "MISSILE LOCKED" if it shows.</summary>
    /// <remarks>C: remove_message(PTR_s_MISSILE_LOCKED) in lock_off (0x416010, cockpt.c).</remarks>
    void RemoveMissileLockedMessage();

    /// <summary>The player launched a missile or mine from <paramref name="hardpoint"/>: start the
    /// weapon VDU's launch animation.</summary>
    /// <remarks>C: the display part of RemovePlayerReleaseWeapon (0x414CB0, cockpt.c).</remarks>
    void PlayerReleaseWeaponLaunched(Data.ObjectType weaponType, short hardpoint);

    // ------------------------------------------------------------------ phase 3 (2026-10-07)
    // The members below have empty default implementations so existing implementations keep
    // compiling; the flight UI overrides them.

    /// <summary>A ship jumped into or out of hyperspace: the space view's next frame starts from a
    /// buffer cleared to the viewport clear colour 0x0F (the white jump flash). No random numbers.</summary>
    /// <remarks>C: <c>ClearViewport(&amp;stSpaceBuffer, cViewportClearColour); bViewportDirty = 1;</c> at the
    /// start of warp (0x42AAF0) and unwarp (0x42AA10, hudmsg.c). Added 2026-10-07.</remarks>
    void SpaceBufferFlash()
    {
    }

    /// <summary>
    /// Shows a yellow message in the right HUD message slot. The autopilot refusals
    /// (<see cref="SimulationCockpitMessage.AlreadyNear"/>, <see cref="SimulationCockpitMessage.EnemyNear"/>,
    /// <see cref="SimulationCockpitMessage.HazardNear"/>) use <c>set_global_message(text, yellow, 3)</c>;
    /// the objective messages use <c>CockpitMessage(text, yellow, 4)</c>, which does not restart a message
    /// whose text already shows. <paramref name="shipType"/> is the ship type whose display name fills
    /// "Wait for %s" (<see cref="Data.ObjectType.None"/> for the other messages). No random numbers.
    /// </summary>
    /// <remarks>C: auto_pilot_valid (0x414380) and flag_reached (0x415530, cockpt.c). Added 2026-10-07.</remarks>
    void ShowCockpitMessage(SimulationCockpitMessage message, Data.ObjectType shipType)
    {
    }

    /// <summary>The autopilot starts: stop every sound effect (afterburner and damage alarm state
    /// reset). No random numbers.</summary>
    /// <remarks>C: ResetSoundState (0x42EE80, music.c) called by auto_pilot_sequence (0x404050, auto.c).
    /// Added 2026-10-07.</remarks>
    void ResetSoundState()
    {
    }
}
