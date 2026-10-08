using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Mission loading and setup, nav spheres and waves (cmpgn.c LoadMissionData, brains.c, logic.c, hudmsg.c).
public sealed partial class SpaceSimulation
{
    private MissionModule? _missionModule;
    private int _missionModuleDataSet = -1;

    /// <summary>The MODULE file of the current <see cref="CampaignDataSet"/> (loaded on first use).</summary>
    public MissionModule MissionModule
    {
        get
        {
            if (_missionModule is null || _missionModuleDataSet != CampaignDataSet)
            {
                _missionModule = MissionModule.Load(Resources, MissionModule.LogicalFileForCampaign(CampaignDataSet));
                _missionModuleDataSet = CampaignDataSet;
            }
            return _missionModule;
        }
    }

    /// <summary>
    /// Loads mission <c>mission + series * 4</c> of the current campaign data set into the runtime
    /// tables. Returns false (nothing changed) for an unused slot, like the SDL port's guard.
    /// </summary>
    /// <remarks>C: LoadMissionData (0x4059B0, cmpgn.c).</remarks>
    public bool LoadMissionData(short series, short mission)
    {
        var data = MissionModule.GetMission(series, mission);
        if (data is null)
            return false;
        LoadMissionData(data);
        return true;
    }

    /// <summary>Copies a parsed mission into the runtime tables (records 0..15 / 0..31).</summary>
    /// <remarks>C: LoadMissionData (0x4059B0, cmpgn.c), the copy loops.</remarks>
    public void LoadMissionData(MissionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        MissionEntryNavPoint = data.Header.EntryNavPoint;
        HomeMissionShipIndex = data.Header.HomeMissionShip;
        PlayerMissionShipIndex = data.Header.PlayerMissionShip;
        for (int i = 0; i < InitialMissionShipIndices.Length; i++)
            InitialMissionShipIndices[i] = data.Header.InitialMissionShips[i];
        MissionHeaderField16 = data.Header.Field16;
        for (int i = 0; i < ActiveNavPointCount; i++)
            MissionNavPoints[i] = data.NavPoints[i];
        for (int i = 0; i < ObjectiveCount; i++)
            MissionObjectiveSources[i] = data.ObjectiveSources[i];
        for (int i = 0; i < ActiveMissionShipCount; i++)
            MissionShips[i] = data.Ships[i];
        for (int i = 0; i < MissionAuxData.Length; i++)
            MissionAuxData[i] = data.MissionAux[i];
        for (int i = 0; i < SeriesAuxData.Length; i++)
            SeriesAuxData[i] = data.SeriesAux[i];
    }

    /// <summary>
    /// Loads and sets up a mission: mission data, 3-space objects and constellation, mission effect
    /// shapes, <see cref="PrepareMission"/>, then the cockpit (via <see cref="ISimulationEvents"/>).
    /// Returns false when the mission slot is unused.
    /// </summary>
    /// <remarks>C: init_mission (0x40B730, brains.c).</remarks>
    public bool InitMission(short series, short mission)
    {
        if (!LoadMissionData(series, mission))
            return false;
        Init3SpaceObjects(series);
        LoadMissionResources();
        TypeResources[(int)ObjectType.DebrisWing].ShapeSet = TypeResources[(int)ObjectType.DebrisMetalSheet].ShapeSet;
        PrepareMission();
        Events.InitializeCockpit(series == 0 ? 4 : (int)Campaign.PlayerShipType);
        return true;
    }

    /// <summary>
    /// Resets the mission counters, creates the player's ship (slot 0) at the entry nav point,
    /// spawns the player's team, builds the objective list and finds the carrier record.
    /// </summary>
    /// <remarks>C: prepare_mission (0x40B7A0, brains.c).</remarks>
    public void PrepareMission()
    {
        Campaign.MissionScore = 0;
        WingmanKilledThisMission = false;
        PlayerDestroyed = false;
        WingmanKillCount = 0;
        MissionMedalScore = 0;
        PlayerKillCount = 0;

        ref var playerRecord = ref MissionShips[PlayerMissionShipIndex];
        Campaign.PlayerShipType = playerRecord.Type;
        LoadShip(Campaign.PlayerShipType, 0);
        SetObjectsData(0, Campaign.PlayerShipType, -1);
        playerRecord.NavPoint = unchecked((sbyte)MissionEntryNavPoint);
        if (!TrainSimActive && StartNavPointOverride != -1)
            playerRecord.NavPoint = unchecked((sbyte)StartNavPointOverride);
        SetUpShipInfo(0, PlayerMissionShipIndex, -1);

        Array.Clear(PlayerComponentDamage);
        InitialFormationSetup = true;
        YourWingman = -1;
        for (int initial = 0; initial < InitialMissionShipIndices.Length; initial++)
        {
            short missionShip = InitialMissionShipIndices[initial];
            if (missionShip == -1)
                continue;
            int pilot = MissionShips[missionShip].Pilot;
            if (IsAlive(pilot) && FindShipsSphere(missionShip) == -1)
            {
                InitShip(missionShip, MissionEntryNavPoint);
                if (YourWingman == -1 && pilot > 4 && pilot < 14)
                    YourWingman = LastShipSlot;
            }
        }
        InitialFormationSetup = false;

        BuildObjectiveList();
        short carrier = 0;
        CarrierMissionShipIndex = carrier;
        // The original scans to 64; the SDL port stops at the 48-record storage boundary.
        while (carrier < MissionShipTableSize && MissionShips[carrier].Type != ObjectType.TigersClaw)
        {
            carrier++;
            CarrierMissionShipIndex = carrier;
        }
        TargetLockMode = 0;
        LandingAuthorized = false;
    }

    /// <summary>World position of a mission record: its nav point's position plus the record's offset.</summary>
    /// <remarks>C: set_sphere_point (0x40B670, brains.c).</remarks>
    public FixedVector SetSpherePoint(in MissionShipRecord record) =>
        VectorMath.Add(MissionNavPoints[record.NavPoint].Position, record.Position);

    /// <summary>Generic pilots always; the player unless dead; wingmen unless their campaign death is
    /// recorded; Kilrathi aces while ace flag 1 is set.</summary>
    /// <remarks>C: is_alive (0x40B6A0, brains.c).</remarks>
    public bool IsAlive(int pilot)
    {
        if (pilot <= 4)
            return true;
        if (pilot == 13)
            return ArcadeState != 4;
        if (pilot >= 5 && pilot <= 12)
            return Campaign.GetPersonalityDeathMission(pilot - 5) == 0;
        if (pilot >= 14 && pilot <= 17)
            return AceStatus((short)(pilot - 14), 1);
        return false;
    }

    /// <summary>Ace flag test: all <paramref name="bits"/> set.</summary>
    /// <remarks>C: ace_status (0x422010, logic.c).</remarks>
    public bool AceStatus(short ace, byte bits) => (Campaign.GetAceFlags(ace) & bits) == bits;

    /// <remarks>C: unflag_ace (0x422030, logic.c).</remarks>
    public void UnflagAce(short ace, byte bits) => Campaign.SetAceFlags(ace, (byte)(Campaign.GetAceFlags(ace) & ~bits));

    /// <remarks>C: flag_ace (0x422050, logic.c).</remarks>
    public void FlagAce(short ace, byte bits) => Campaign.SetAceFlags(ace, (byte)(Campaign.GetAceFlags(ace) | bits));

    /// <summary>An ace in play (flag 1) is marked killed (flag 2).</summary>
    /// <remarks>C: kill_ace (0x422060, logic.c).</remarks>
    public void KillAce(short ace)
    {
        if (AceStatus(ace, 1))
        {
            UnflagAce(ace, 1);
            FlagAce(ace, 2);
        }
    }

    /// <summary>Clears flags 0x1a and sets 0x20 ("survives once") for an ace flying this mission.</summary>
    /// <remarks>C: prepare_ace (0x4220D0, logic.c).</remarks>
    public void PrepareAce(short ace)
    {
        UnflagAce(ace, 0x1a);
        FlagAce(ace, 0x20);
    }

    /// <summary>Hides a WARP_ARRIVE ship as a FUTURION until it "arrives"; its class is parked in the counter.</summary>
    /// <remarks>C: check_futurion (0x40B700, brains.c).</remarks>
    public void CheckFuturion(short i)
    {
        if (Ships[i].MissionType == ShipMissionType.WarpArrive)
        {
            var previous = Objects[i].Class;
            Objects[i].Class = ObjectClass.Futurion;
            Objects[i].Counter = (short)previous;
        }
    }

    /// <summary>
    /// Spawns mission record <paramref name="missionShip"/> at <paramref name="navPoint"/>: hazard
    /// records add a hazard field; already spawned, finished or dead-pilot records are skipped
    /// (dead pilots 9 and up are replaced by a generic level-3 pilot). Team members get spawn nav
    /// -1. Returns the slot or -1.
    /// </summary>
    /// <remarks>C: init_ship (0x40C800, brains.c).</remarks>
    public short InitShip(short missionShip, short navPoint)
    {
        if (missionShip == -1)
            return -1;
        ref var record = ref MissionShips[missionShip];
        if (record.Type == ObjectType.AsteroidField || record.Type == ObjectType.MineField)
        {
            var center = VectorMath.Add(MissionNavPoints[navPoint].Position, record.Position);
            AddHazardField(record.Type, center, unchecked((short)(record.Speed + 3000)), unchecked((short)record.Pilot));
            return -1;
        }
        short obj = FindShipIndex(missionShip);
        if (obj != -1 || record.State != 0)
            return -1;
        if (record.MissionType != ShipMissionType.CannedSequence && !IsAlive(record.Pilot))
        {
            if (record.Pilot < 9)
                return -1;
            record.Pilot = 3;
        }
        record.NavPoint = unchecked((sbyte)navPoint);
        if (IsTeamMember(missionShip))
            navPoint = -1;
        obj = InitializeShip(record.Type, -1);
        if (obj != -1)
        {
            SetUpShipInfo(obj, missionShip, unchecked((sbyte)navPoint));
            FindNextShipTurnSlot(obj);
            CheckFuturion(obj);
        }
        return obj;
    }

    /// <summary>
    /// Initialises a spawned ship from its mission record: position at the sphere point, orientation
    /// (<c>alter_yaw(-pitch)</c>, <c>alter_pitch(-yaw)</c>, <c>alter_roll(roll)</c>: note the swapped
    /// names), side, speed, pilot, mission type, leader, formation and AI data.
    /// </summary>
    /// <remarks>C: Set_up_ship_info (0x40C5E0, brains.c).</remarks>
    public void SetUpShipInfo(short obj, short missionShip, sbyte navPoint)
    {
        ref var record = ref MissionShips[missionShip];
        ref var ship = ref Ships[obj];
        ref var o = ref Objects[obj];
        ship.CapitalShipViewFrame = -1;
        ship.WingmanMessageState = -1;
        ship.LastAttacker = -1;
        ship.ActionCount = 0;
        ship.ExhaustHeat = 0;
        o.AccumulatedDamage = 0;
        ship.Damage = 0;
        ship.CannedCommand = 0;
        ship.IonDriveDamage = 0;
        ship.DestroyedWeaponCount = 0;
        ship.Communicator = 0;
        ship.CannedSequence = default;
        ship.SpawnNavPoint = navPoint;
        ship.MissionIndex = missionShip;
        ship.PointingMode = 1;

        o.Position = SetSpherePoint(record);
        o.AlterYaw(unchecked((short)-record.Pitch));
        o.AlterPitch(unchecked((short)-record.Yaw));
        o.AlterRoll(record.Roll);
        ship.Side = record.Side;
        o.Speed = record.Speed << 8;
        ship.PilotLevel = record.Pilot;
        ResetMissionType(obj, record.MissionType);
        ship.MissionShip = record.TargetMissionIndex;
        ship.WingLeader = FindShipIndex(record.LeaderMissionIndex);
        SetFormationPosition(obj, missionShip);
        o.Velocity = FixedVector.Zero;
        InitIntelligenceData(obj);
    }

    /// <summary>
    /// Computes the ship's formation offset relative to the root leader of its leader chain and,
    /// while spawning the team (or for leaders other than the player), places it in formation:
    /// leader frame, leader sphere point plus offset, leader speed.
    /// </summary>
    /// <remarks>C: set_formation_position (0x40C4E0, brains.c). When the root leader is not spawned
    /// (<c>find_ship_index</c> = -1) the original copies the frame of slot -1 (an out-of-bounds
    /// read); the port keeps the ship's own frame instead (does not occur in the shipped data).</remarks>
    public void SetFormationPosition(short obj, short missionShip)
    {
        ref var record = ref MissionShips[missionShip];
        if (record.FormationIndex == -1)
            return;
        short source = obj;
        int leaderIndex = missionShip;
        while (MissionShips[leaderIndex].LeaderMissionIndex != -1)
        {
            source = FindShipIndex(MissionShips[leaderIndex].LeaderMissionIndex);
            leaderIndex = MissionShips[leaderIndex].LeaderMissionIndex;
        }
        ref var leaderRecord = ref MissionShips[leaderIndex];
        Ships[obj].FormationOffset = ShortVector.Subtract(
            GeometryTables.FormationPosition(record.FormationIndex, record.FormationSpot),
            GeometryTables.FormationPosition(leaderRecord.FormationIndex, leaderRecord.FormationSpot));
        if (source == 0 && !InitialFormationSetup)
            return;
        if (source >= 0)
            CopyFrame(source, obj);
        Objects[obj].Position = SetSpherePoint(leaderRecord);
        Objects[obj].Position = Objects[obj].OffsetLocation(Ships[obj].FormationOffset);
        Objects[obj].Speed = leaderRecord.Speed << 8;
    }

    /// <summary>The player record or one of the initial (team) records.</summary>
    /// <remarks>C: is_team_member (0x40C740, brains.c).</remarks>
    public bool IsTeamMember(short missionShip)
    {
        if (PlayerMissionShipIndex == missionShip)
            return true;
        foreach (short index in InitialMissionShipIndices)
        {
            if (index == missionShip)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Schedules the ship's AI ticks: interval from the pilot level, phase (regulator) bumped until
    /// no other fighter with the same interval shares it.
    /// </summary>
    /// <remarks>C: find_next_ship_turn_slot (0x40C780, brains.c). Like the original, a collision
    /// restarts the scan at slot 2 (<c>other = 1; other++</c>).</remarks>
    public void FindNextShipTurnSlot(short obj)
    {
        ref var ship = ref Ships[obj];
        ship.TurnRegulator = 1;
        sbyte interval = (sbyte)AiTables.PilotTurnInterval[ship.PilotLevel];
        ship.TurnInterval = interval;
        short other = 1;
        do
        {
            if (Objects[other].Class == ObjectClass.Ship && other != obj &&
                Ships[other].TurnRegulator == ship.TurnRegulator &&
                Ships[other].TurnInterval == interval)
            {
                other = 1;
                ship.TurnRegulator++;
                if (interval < ship.TurnRegulator)
                    break;
            }
            other++;
        }
        while (other <= ObjectSlots.LastShip);
    }

    /// <summary>
    /// Initial AI state: alerts cleared, special NONE, mission spot = current nav point (GOTO_WARP:
    /// the target nav, COME_HOME and Confed WARP_ARRIVE: the home ship), WARP_ARRIVE starts warping
    /// in, canned ships load their script; rating from the pilot level, stress 0.
    /// </summary>
    /// <remarks>C: init_intelligence_data (0x40C950, brains.c). A GOTO_WARP target outside the nav
    /// table (an out-of-bounds read in the original) keeps the current nav point as the spot.</remarks>
    public void InitIntelligenceData(short obj)
    {
        ref var ship = ref Ships[obj];
        ship.Turn = 0;
        ClearAlert(obj);
        short missionTarget = ship.MissionShip;
        ship.SpecialManeuver = SpecialManeuver.None;
        ship.MissionSpot = MissionNavPoints[CurrentNavPoint].Position;

        switch (ship.MissionType)
        {
            case ShipMissionType.Escort:
            case ShipMissionType.Strike:
            case ShipMissionType.Defend:
            case ShipMissionType.Wingman:
                ship.MissionShip = missionTarget;
                break;
            case ShipMissionType.GotoWarp:
                if (missionTarget >= 0 && missionTarget < NavPointTableSize)
                    ship.MissionSpot = MissionNavPoints[missionTarget].Position;
                break;
            case ShipMissionType.WarpArrive:
                ship.Tactic = ShipTactic.WarpIn;
                ship.Maneuver = ShipManeuver.WarpingIn;
                if (ship.Side != Side.Kilrathi)
                    LocateShip(HomeMissionShipIndex, ref ship.MissionSpot);
                break;
            case ShipMissionType.ComeHome:
                LocateShip(HomeMissionShipIndex, ref ship.MissionSpot);
                break;
            case ShipMissionType.CannedSequence:
                ship.CannedSequence = new CannedSequenceCursor(MissionShips[ship.MissionIndex].CannedSequence);
                ship.PilotLevel = 2;
                AdvanceCannedSequence(obj);
                break;
        }

        ship.Rating = ship.PilotLevel < 5 ? (sbyte)-1 : unchecked((sbyte)(ship.PilotLevel - (int)Rating.AceSpirit));
        ship.Stress = 0;
    }

    /// <summary>
    /// Enters nav sphere <paramref name="navPoint"/>: removes (or explodes, when visible) the ships of
    /// the previous sphere, clears hazards, swaps the preloaded ship shapes, spawns the nav's ships,
    /// applies its triggers and relocates mobile objectives.
    /// </summary>
    /// <remarks>C: set_up_action_sphere (0x40BFF0, brains.c). For the last table entry the original
    /// reads the "next nav" type past the end of the table; the port treats it as no wave.</remarks>
    public void SetUpActionSphere(short navPoint)
    {
        CurrentNavPoint = navPoint;
        int next = navPoint + 1;
        CurrentWave = next < NavPointTableSize && MissionNavPoints[next].Type == 2 ? (short)2 : (short)-1;
        EnemySighting = 0x7fff;

        for (short obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            ref var o = ref Objects[obj];
            if (o.Class == ObjectClass.Null || Ships[obj].SpawnNavPoint == -1)
                continue;
            if (o.Class >= ObjectClass.Ship && Ships[obj].MissionType == ShipMissionType.Rout)
                MissionShips[Ships[obj].MissionIndex].State = 3;
            if (o.Class == ObjectClass.CapitalShip)
                o.Shape = ShapeRef.None;
            if (o.ScreenX != ObjectSlots.NotVisible)
                Explode(-1, obj);
            else
                RemoveObject(obj);
        }
        RemoveAllHazards();
        HazardFieldCount = 0;
        NewSphereShapes(navPoint);

        for (int entry = 0; entry < 10; entry++)
        {
            short missionShip = MissionNavPoints[navPoint].MissionShips[entry];
            if (missionShip != -1)
                InitShip(missionShip, navPoint);
        }

        for (int trigger = 0; trigger < 8; trigger += 2)
        {
            sbyte triggerType = MissionNavPoints[navPoint].Triggers[trigger];
            if (triggerType != -1)
                MissionNavPoints[MissionNavPoints[navPoint].Triggers[trigger + 1]].Type = triggerType;
        }

        for (short objective = 0; objective < MissionObjectiveCount; objective++)
            LocateMobileObjective(objective);
        CleanUpCockpit();
        LandingAuthorized = false;
    }

    /// <summary>Clears the player's target and target lock and pulls the wingman back into formation.</summary>
    /// <remarks>C: clean_up_cockpit (0x42ACC0, hudmsg.c); the HUD part goes to
    /// <see cref="ISimulationEvents.ClearHudGunReadouts"/>.</remarks>
    public void CleanUpCockpit()
    {
        short wingman = YourWingman;
        Ships[0].Target = -1;
        TargetLockMode = 0;
        if (wingman != -1)
        {
            AutoEngageTimer = -1;
            Ships[wingman].Target = -1;
            ResetObjective(wingman, ShipObjective.HoldFormation);
        }
        Events.ClearHudGunReadouts();
    }

    /// <summary>
    /// Spawns the next follow-up wave (stored in the nav record after the current one) if its type
    /// matches the wave number, else ends the waves.
    /// </summary>
    /// <remarks>C: set_up_next_wave (0x40C3C0, brains.c); the training-simulator bookkeeping goes to
    /// <see cref="ISimulationEvents.TrainSimWaveCleared"/>.</remarks>
    public void SetUpNextWave()
    {
        if (TrainSimActive)
            Events.TrainSimWaveCleared(CurrentWave != -1);
        if (CurrentWave == -1 || CannedSceneMode != 0)
            return;
        // The original indexes from a base biased one record before the table: wave 2 lives in
        // the record right after the current nav point.
        int waveNav = CurrentNavPoint + CurrentWave - 1;
        short previousWave = CurrentWave;
        CurrentWave++;
        if (MissionNavPoints[waveNav].Type == unchecked((sbyte)previousWave))
        {
            NewSphereShapes(waveNav);
            MissionNavPoints[waveNav].Type = -1;
            for (int entry = 0; entry < 10; entry++)
                ApproveXyz(InitShip(MissionNavPoints[waveNav].MissionShips[entry], CurrentNavPoint), 5000, 10000);
            return;
        }
        CurrentWave = -1;
    }

    /// <summary>Always true in the shipped game.</summary>
    /// <remarks>C: room_for_me (0x40C350, brains.c).</remarks>
    public static bool RoomForMe(short obj, short minimum)
    {
        _ = obj;
        _ = minimum;
        return true;
    }

    /// <summary>Would re-roll a spawn position near the player until there is room; a no-op because
    /// <see cref="RoomForMe"/> always succeeds (no randoms are consumed).</summary>
    /// <remarks>C: approve_xyz (0x40C360, brains.c).</remarks>
    public void ApproveXyz(short obj, short minimum, short maximum)
    {
        if (obj == -1 || RoomForMe(obj, minimum))
            return;
        do
        {
            Objects[obj].Position = RandomVectors.RandomRadial(Random, Objects[0].Position, maximum);
        }
        while (!RoomForMe(obj, minimum));
    }

    /// <summary>Slot 0..9 of the live ship (or futurion) spawned from <paramref name="missionShip"/>, or -1;
    /// also stored in <see cref="LastFoundShip"/>.</summary>
    /// <remarks>C: find_ship_index (0x422710, logic.c).</remarks>
    public short FindShipIndex(short missionShip)
    {
        LastFoundShip = -1;
        if (missionShip != -1)
        {
            for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
            {
                var objectClass = Objects[obj].Class;
                if (((objectClass >= ObjectClass.Ship && Ships[obj].SpecialManeuver != SpecialManeuver.Unknown9) ||
                     objectClass == ObjectClass.Futurion) &&
                    Ships[obj].MissionIndex == missionShip)
                {
                    LastFoundShip = obj;
                    return obj;
                }
            }
        }
        return -1;
    }

    /// <summary>
    /// Nav sphere that spawns <paramref name="missionShip"/>: an active (type 1) nav listing it, else
    /// the active nav before the first wave record listing it; -1 when no nav lists it.
    /// </summary>
    /// <remarks>C: find_ships_sphere (0x4236F0, logic.c). The backward scan stops at record 0 (the
    /// original would step below the table).</remarks>
    public short FindShipsSphere(short missionShip)
    {
        short fallback = -1;
        for (short navIndex = 0; navIndex < ActiveNavPointCount; navIndex++)
        {
            for (int shipIndex = 0; shipIndex < 10; shipIndex++)
            {
                if (MissionNavPoints[navIndex].MissionShips[shipIndex] == missionShip)
                {
                    if (MissionNavPoints[navIndex].Type == 1)
                        return navIndex;
                    if (fallback == -1)
                        fallback = navIndex;
                }
            }
        }
        if (fallback == -1)
            return -1;
        short nav = fallback;
        while (nav > 0 && MissionNavPoints[nav].Type > 1)
            nav--;
        return nav;
    }

    /// <summary>
    /// Current or expected position of a mission ship: the live ship, else (if not destroyed) its
    /// nav sphere point. Returns false when unknown; a destroyed ship leaves <paramref name="point"/>
    /// untouched, an unplaced one zeroes it.
    /// </summary>
    /// <remarks>C: locate_ship (0x423780, logic.c).</remarks>
    public bool LocateShip(short missionShip, ref FixedVector point)
    {
        short obj = FindShipIndex(missionShip);
        if (obj != -1)
        {
            point = Objects[obj].Position;
            return true;
        }
        if (DeadShip(missionShip))
            return false;
        short navPoint = FindShipsSphere(missionShip);
        if (navPoint != -1)
        {
            point = VectorMath.Add(MissionNavPoints[navPoint].Position, MissionShips[missionShip].Position);
            return true;
        }
        point = FixedVector.Zero;
        return false;
    }

    /// <summary>Record -1 or destroyed (state 3).</summary>
    /// <remarks>C: dead_ship (0x423610, logic.c).</remarks>
    public bool DeadShip(short missionShip) => missionShip == -1 || MissionShips[missionShip].State == 3;

    /// <summary>Record -1, destroyed or warped out.</summary>
    /// <remarks>C: gone_ship (0x423640, logic.c).</remarks>
    public bool GoneShip(short missionShip) =>
        missionShip == -1 || MissionShips[missionShip].State == 3 || MissionShips[missionShip].State == 2;

    /// <summary>Distance in whole units from a ship to a nav point (saturated).</summary>
    /// <remarks>C: GetShipDistanceToNavPoint (0x42A0E0, hudmsg.c).</remarks>
    public short GetShipDistanceToNavPoint(short ship, int navPoint) =>
        FixedMath.ToShortSaturating(VectorMath.Delta(Objects[ship].Position, MissionNavPoints[navPoint].Position).Magnitude());

    /// <summary>First active nav point (0..15) whose sphere contains the ship, else the current one.</summary>
    /// <remarks>C: FindNearestNavPoint (0x42A120, hudmsg.c).</remarks>
    public short FindNearestNavPoint(short ship)
    {
        for (short navPoint = 0; navPoint < ActiveNavPointCount; navPoint++)
        {
            if (MissionNavPoints[navPoint].Type == 1 &&
                GetShipDistanceToNavPoint(ship, navPoint) < MissionNavPoints[navPoint].ProximityRadius)
                return navPoint;
        }
        return CurrentNavPoint;
    }

    /// <summary>Switches the action sphere when the player entered another nav sphere.</summary>
    /// <remarks>C: ReleaseStaleNavTarget (0x42A170, hudmsg.c).</remarks>
    public void ReleaseStaleNavTarget()
    {
        short navPoint = FindNearestNavPoint(0);
        if (CurrentNavPoint != navPoint)
            SetUpActionSphere(navPoint);
    }
}
