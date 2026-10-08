using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// The flight loop entry points for the Game (hudmsg.c RunSpaceFlight/HandleSpaceFlightControls/
// Draw_3Space_Frame, main.c players_flight_dynamics/fire_players_lasers, logic.c accelerate/
// your_afterburner, cockpt.c update_cockpit/check_stranded). One flight frame is:
//
//   Game input -> PlayersFlightDynamics(...) and the key actions below
//   Update3Space()                       (every frame; strictly tick-based)
//   if (PrepareSpaceView()) Game draws   (simulation half of Draw_3Space_Frame)
//   UpdateCockpitSimulation()            (simulation half of update_cockpit; then the Game's
//                                         cockpit view block, then CheckStranded())
public sealed partial class SpaceSimulation
{
    /// <summary>Converts the stick input (−8..8 per axis in the original's units) into the player's
    /// rotation rates; while tumbling (BLOWING_UP) the input only fights the spin until it is slow.</summary>
    /// <remarks>C: players_flight_dynamics (0x4284D0, main.c); nPitchInput/nYawInput/nRollInput are
    /// the parameters. The type's yawRate scales pitch and pitchRate scales yaw (swapped names).</remarks>
    public void PlayersFlightDynamics(short pitchInput, short yawInput, short rollInput)
    {
        ref var o = ref Objects[ObjectSlots.Player];
        var typeData = ObjectTypeTable.Get(Campaign.PlayerShipType);
        if (Ships[ObjectSlots.Player].SpecialManeuver == SpecialManeuver.BlowingUp)
        {
            if (o.Counter == -1)
            {
                if (o.YawRotation < typeData.PitchRate &&
                    o.PitchRotation < typeData.YawRate &&
                    o.RollRotation < typeData.RollRate)
                {
                    Ships[ObjectSlots.Player].SpecialManeuver = SpecialManeuver.None;
                }
                else
                {
                    o.YawRotation = unchecked((short)(o.YawRotation - yawInput));
                    o.PitchRotation = unchecked((short)(o.PitchRotation - pitchInput));
                }
            }
            return;
        }
        o.PitchRotation = unchecked((short)(typeData.YawRate * pitchInput / 8));
        o.YawRotation = unchecked((short)-(typeData.PitchRate * yawInput / 8));
        o.RollRotation = unchecked((short)-(typeData.RollRate * rollInput / 8));
    }

    /// <summary>Fire key: all enabled guns when the refire delay is over and energy is left; with a
    /// target, the navigation VDU switches to the target display.</summary>
    /// <remarks>C: fire_players_lasers (0x428480, main.c).</remarks>
    public void FirePlayersLasers()
    {
        if (Objects[ObjectSlots.Player].Counter != -1 || Ships[ObjectSlots.Player].WeaponEnergy <= 0)
            return;
        FireFixedProjectileWeapon(0);
        if (Ships[ObjectSlots.Player].Target != -1 && Cockpit.GetVduMode(1) == 5)
            Events.SelectCockpitVduMode(1, 3);
    }

    /// <summary>Throttle keys: changes the commanded speed by <paramref name="amount"/> units (2 less
    /// with a malfunctioning ion drive, which also clicks every third frame).</summary>
    /// <remarks>C: accelerate (0x4218D0, logic.c).</remarks>
    public void Accelerate(short amount)
    {
        if (Malf(0))
        {
            amount = unchecked((short)(amount - 2));
            if (SpaceFrame % 3 == 0)
                Events.PlaySoundEffect(3, -1);
        }
        Celerate(0, amount << 8);
    }

    /// <summary>Backspace: throttle to zero.</summary>
    /// <remarks>C: <c>anShipSpeed[0] = 0</c> (key 0x0e of HandleSpaceFlightControls, hudmsg.c).</remarks>
    public void ZeroPlayerSpeed() => Objects[ObjectSlots.Player].Speed = 0;

    /// <summary>Afterburner key: lights (8 frames) or extends (2 frames, when nearly out) the
    /// afterburner while fuel lasts; a damaged ion drive may refuse.</summary>
    /// <remarks>C: your_afterburner (0x421920, logic.c); its sound part is
    /// <see cref="ISimulationEvents.PlayerAfterburnerEngaged"/>.</remarks>
    public void YourAfterburner()
    {
        ref var ship = ref Ships[ObjectSlots.Player];
        if (ship.Fuel <= 0)
            return;
        if (Malf(0))
        {
            Events.PlaySoundEffect(3, -1);
            return;
        }
        short time;
        if (ship.SpecialManeuver != SpecialManeuver.Afterburner || ship.AfterburnerTimer == 0)
        {
            time = 8;
        }
        else
        {
            if (ship.AfterburnerTimer > 2)
                return;
            time = 2;
        }
        FireAfterburner(0, time);
        Events.PlayerAfterburnerEngaged(SpaceFrame);
    }

    /// <summary>Release key: drops a mine when a mine is selected, else launches the selected missile
    /// (one at a time while the missile camera tracks one) and switches to the missile camera when it
    /// is enabled.</summary>
    /// <remarks>C: key 0x1c of HandleSpaceFlightControls (0x429160, hudmsg.c), after its key-repeat test.</remarks>
    public void PlayerReleaseWeapon()
    {
        if (SelectedReleaseWeaponIndex == -1)
            return;
        if (Ships[ObjectSlots.Player].Weapons.GetWeaponType(SelectedReleaseWeaponIndex) == ObjectType.SpaceMine)
        {
            DropPlayerMine(0);
            return;
        }
        if (ExternalViewShip != -1)
            return;
        ExternalViewShip = FireMissile(0);
        if (MissileCameraEnabled && ExternalViewShip != -1)
            NewView(6, ExternalViewShip);
    }

    /// <summary>Target lock key: toggles keeping the current target.</summary>
    /// <remarks>C: key 0x26 of HandleSpaceFlightControls (hudmsg.c); the target VDU refresh
    /// (<c>if (get_mode(1) == 3) InvalidateVduMode(1)</c>) stays with the Game.</remarks>
    public void ToggleTargetLockMode()
    {
        TargetLockMode = (short)(TargetLockMode == 0 ? 1 : 0);
        Events.PlaySoundEffect(0x19, -1);
    }

    /// <summary>Eject key: unless the ejector is destroyed, <c>RandomInRange(0, damage) == 0</c> ejects
    /// (arcade state 2), else the malfunction sound. With an intact ejector this is a 50 % chance,
    /// because RandomInRange(0, 0) returns 0 or 1.</summary>
    /// <remarks>C: key 0x12 of HandleSpaceFlightControls (hudmsg.c), after its key/modifier test.</remarks>
    public void TryEject()
    {
        if (PlayerComponentDamage[7] == 4)
            return;
        if (Random.InRange(0, PlayerComponentDamage[7]) == 0)
            ArcadeState = 2;
        else
            Events.PlaySoundEffect(0x1f, -1);
    }

    /// <summary>
    /// The simulation half of <c>Draw_3Space_Frame</c>: counts the frame skip down and on a view
    /// frame increments <see cref="RenderedSpaceFrame"/>, projects all objects, maintains the star
    /// field (and the hazards), places engine flames and child objects, sorts the draw list and, in
    /// the cockpit view, runs the missile lock (the first step of <c>overlay_head_up_display</c>).
    /// Returns false on skipped frames. The Game calls its palette fade (UpdateSpacePaletteFade)
    /// before, and draws <see cref="SortedObjects"/> after this call.
    /// </summary>
    /// <remarks>C: Draw_3Space_Frame (0x429DD0, hudmsg.c) without UpdateSpacePaletteFade and the
    /// drawing; target_locking from overlay_head_up_display (0x416AC0, cockpt.c). Several of these
    /// steps consume random numbers (dust, stars, flames, hazards, lock).</remarks>
    public bool PrepareSpaceView()
    {
        FrameSkipCounter = unchecked((short)(FrameSkipCounter - 1));
        if (FrameSkipCounter > 0)
            return false;
        FrameSkipCounter = FrameSkip;
        RenderedSpaceFrame = unchecked((short)(RenderedSpaceFrame + 1));
        TransformObjectsToYourView();
        UpdateStarField();
        PlaceExhaustOnShips();
        RepositionFixedChildObjects();
        SortObjectDepth();
        if (CameraViewMode == 0)
            TargetLocking(Ships[ObjectSlots.Player].Target);
        return true;
    }

    /// <summary>
    /// The simulation half of <c>update_cockpit</c>, run every flight frame after the view: automatic
    /// targeting, the repair systems and the round-robin objective check (objective
    /// <c>SpaceFrame % count</c>). The UI then runs the cockpit-view block (lights, missile warning,
    /// scanner, VDUs incl. <see cref="CheckObjectives"/>, pilot, cockpit explosion, then its own
    /// npc_communication) and finally <see cref="CheckStranded"/>.
    /// </summary>
    /// <remarks>C: update_cockpit (0x417E70, cockpt.c): check_target, repair_internal_damage,
    /// update_objective_location.</remarks>
    public void UpdateCockpitSimulation()
    {
        CheckTarget();
        RepairInternalDamage();
        if (MissionObjectiveCount != 0)
        {
            // nSpaceFrame is a short that turns negative after 32767 frames of one mission; the
            // original then reads objectives before the table. The port skips those frames.
            short objective = (short)(SpaceFrame % MissionObjectiveCount);
            if (objective >= 0)
                UpdateObjectiveLocation(objective);
        }
    }

    /// <summary>The carrier was destroyed and no enemy is within 30000: the player is stranded
    /// (arcade state 3).</summary>
    /// <remarks>C: check_stranded (0x417B30, cockpt.c). Without a carrier record (index outside the
    /// table) the test is false.</remarks>
    public void CheckStranded()
    {
        if (TrainSimActive || (uint)CarrierMissionShipIndex >= (uint)MissionShips.Length)
            return;
        if (MissionShips[CarrierMissionShipIndex].State == 3 && !AnyEnemy(0, 30000))
            ArcadeState = 3;
    }
}
