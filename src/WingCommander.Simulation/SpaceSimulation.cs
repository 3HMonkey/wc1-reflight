using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

/// <summary>
/// The 3D space world ("3Space"): the 64 object slots, the ship-only state of slots 0..9, the
/// mission tables, the shared random generator and the scratch globals that the original
/// routines pass results through (<see cref="TargetRange"/>, <see cref="FacingToTarget"/>, ...).
/// One instance per game; the original's globals are its fields. The class is split into
/// partial files by original compilation unit.
/// </summary>
/// <remarks>C: the simulation globals of include/globals.h (see docs/analysis/simulation.md §4 and §10.1).</remarks>
public sealed partial class SpaceSimulation
{
    /// <summary>Size of the runtime nav point table (16 loaded + 4 intro).</summary>
    public const int NavPointTableSize = 20;

    /// <summary>Nav points loaded per mission.</summary>
    public const int ActiveNavPointCount = 16;

    /// <summary>Size of the runtime mission ship table (32 loaded + 14 intro + 2 spare).</summary>
    public const int MissionShipTableSize = 48;

    /// <summary>Mission ship records loaded per mission.</summary>
    public const int ActiveMissionShipCount = 32;

    /// <summary>Objective records per mission.</summary>
    public const int ObjectiveCount = 16;

    /// <summary>Hazard field slots.</summary>
    public const int HazardFieldSlots = 7;

    /// <summary>Hazard object slots.</summary>
    public const int HazardObjectSlots = 20;

    public SpaceSimulation(
        CRandom random,
        ISimulationResources resources,
        ISimulationEvents? events = null,
        ICampaignState? campaign = null)
    {
        Random = random ?? throw new ArgumentNullException(nameof(random));
        Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        Events = events ?? NullSimulationEvents.Instance;
        Campaign = campaign ?? new SimulationCampaignState();

        for (int i = 0; i < MissionNavPoints.Length; i++)
            MissionNavPoints[i] = i >= IntroMissionData.FirstNavPoint
                ? IntroMissionData.CreateNavPoint(i)
                : new MissionNavPoint { Name = "" };
        for (int i = 0; i < MissionShips.Length; i++)
        {
            if (i >= IntroMissionData.FirstShipRecord && i < IntroMissionData.FirstShipRecord + IntroMissionData.ShipRecordCount)
                MissionShips[i] = IntroMissionData.CreateShipRecord(i);
        }
        for (int i = 0; i < MissionObjectiveSources.Length; i++)
            MissionObjectiveSources[i].Description = "";
        for (int i = 0; i < MissionObjectives.Length; i++)
        {
            MissionObjectives[i].DisplayName = "";
            MissionObjectives[i].Name = "";
        }
        Array.Fill(InitialMissionShipIndices, (short)-1);
        Array.Fill(HazardObjects, (sbyte)-1);
        Array.Fill(ConstellationObjectIndices, (short)-1);
        for (int i = 0; i < TypeResources.Length; i++)
            TypeResources[i] = ObjectTypeResources.Empty;
        InitializeDirectionViewFrames();
    }

    /// <summary>The game's random generator (shared with rendering, cockpit and campaign code).</summary>
    public CRandom Random { get; }

    public ISimulationResources Resources { get; }

    public ISimulationEvents Events { get; set; }

    public ICampaignState Campaign { get; set; }

    // ------------------------------------------------------------------ object tables

    /// <summary>The 64 object slots (array of structs, slot index = identity).</summary>
    public SpaceObject[] Objects { get; } = new SpaceObject[ObjectSlots.Count];

    /// <summary>Ship-only state of slots 0..9.</summary>
    public ShipState[] Ships { get; } = new ShipState[ObjectSlots.ShipSlotCount];

    /// <summary>Runtime graphics pointers of the 58 object types (shapeSet/animation/shape).</summary>
    public ObjectTypeResources[] TypeResources { get; } = new ObjectTypeResources[ObjectTypeTable.Count];

    /// <summary>aObjectResourceSlots[4]: 0 player ship, 1..2 nav preloads, 3 missiles.</summary>
    public ObjectResourceSlot[] ResourceSlots { get; } = new ObjectResourceSlot[4];

    // ------------------------------------------------------------------ frame counters

    /// <summary>nSpaceFrame: simulation frame counter (16-bit, wraps).</summary>
    public short SpaceFrame;

    /// <summary>nRenderedSpaceFrame: rendered frame counter (ship sparks, hazard slot scheduling).</summary>
    public short RenderedSpaceFrame;

    // ------------------------------------------------------------------ scratch globals (§10.1)

    /// <summary>nTargetShip: result of target scans.</summary>
    public short TargetShip;

    /// <summary>nTargetRange: range of the last facing/range query or scan (units, radii subtracted).</summary>
    public short TargetRange;

    /// <summary>nFacingToTarget: percent cosine between our forward and the target direction.</summary>
    public short FacingToTarget;

    /// <summary>nTargetFacing: percent cosine between the target's forward and the direction to us.</summary>
    public short TargetFacing;

    /// <summary>vToTarget: delta of the last <c>distance_from_point</c>.</summary>
    public FixedVector ToTarget;

    /// <summary>vNormalizedToTarget.</summary>
    public FixedVector NormalizedToTarget;

    /// <summary>vCollisionDelta: delta of the last collision test.</summary>
    public FixedVector CollisionDelta;

    /// <summary>vPlayerAcceleration: last acceleration applied to the player.</summary>
    public FixedVector PlayerAcceleration;

    /// <summary>DAT_00475e78: the maneuver "too close" range of perform_maneuver.</summary>
    public short TooCloseRange;

    /// <summary>bCurrentManeuverReroll: re-roll chance of the running maneuver.</summary>
    public byte CurrentManeuverReroll;

    /// <summary>DAT_0046c010: last slot returned by <c>get_ship_slot</c> (-1 when full).</summary>
    public short LastShipSlot = -1;

    /// <summary>nLastFoundShip: last result of <c>find_ship_index</c>.</summary>
    public short LastFoundShip;

    /// <summary>asViableTargetDistance[16] (scratch target list).</summary>
    public short[] ViableTargetDistance { get; } = new short[16];

    /// <summary>acViableTarget[16].</summary>
    public sbyte[] ViableTarget { get; } = new sbyte[16];

    /// <summary>cViableTargetCount.</summary>
    public sbyte ViableTargetCount;

    /// <summary>asTargetListRange[16] (build_target_list scratch).</summary>
    public short[] TargetListRange { get; } = new short[16];

    /// <summary>acFormationMemberList[16] (build_squad_list scratch, -1 terminated).</summary>
    public sbyte[] FormationMemberList { get; } = new sbyte[16];

    /// <summary>anSortedObject[64]: render order (far first), -1 terminated.</summary>
    public int[] SortedObjects { get; } = new int[ObjectSlots.Count];

    /// <summary>aDirectionView{Right,Up,Forward}Vector[62]: the sprite view frames.</summary>
    public FixedVector[] DirectionViewRight { get; } = new FixedVector[GeometryTables.DirectionViewCount];

    public FixedVector[] DirectionViewUp { get; } = new FixedVector[GeometryTables.DirectionViewCount];

    public FixedVector[] DirectionViewForward { get; } = new FixedVector[GeometryTables.DirectionViewCount];

    // ------------------------------------------------------------------ player / flight state

    /// <summary>nYourWingman: the player's wingman slot or -1.</summary>
    public short YourWingman = -1;

    /// <summary>nNavPointerObject: the nav pointer pseudo-object or -1.</summary>
    public short NavPointerObject = -1;

    /// <summary>nEjectedPilotObject.</summary>
    public short EjectedPilotObject;

    /// <summary>nPlayerCollisionObject.</summary>
    public short PlayerCollisionObject = -1;

    /// <summary>nClosestVisibleObject.</summary>
    public short ClosestVisibleObject = -1;

    /// <summary>nExternalViewShip: missile tracked by the missile camera.</summary>
    public short ExternalViewShip = -1;

    /// <summary>nArcadeState: 0 flying, 1 landed, 2 ejected, 3 stranded, 4 dead, 5 quit.</summary>
    public int ArcadeState;

    /// <summary>bEngageAllowed: the player let the wingman engage.</summary>
    public bool EngageAllowed;

    /// <summary>nAutoEngageTimer (-1 idle).</summary>
    public short AutoEngageTimer = -1;

    /// <summary>eSelectedGunType: selected gun type or 0x80 = all guns, -1 none.</summary>
    public ObjectType SelectedGunType = ObjectType.None;

    /// <summary>nSelectedReleaseWeaponIndex: selected missile/mine slot or -1.</summary>
    public int SelectedReleaseWeaponIndex = -1;

    /// <summary>nTargetLockCountdown: -1 off, &gt; 0 counting, 0 locked, &lt; -1 malfunction cooldown.</summary>
    public short TargetLockCountdown;

    /// <summary>nTargetLockMode: keep the current target (T key).</summary>
    public short TargetLockMode;

    /// <summary>bTargetLockAcquired.</summary>
    public bool TargetLockAcquired;

    /// <summary>bMissileCameraEnabled.</summary>
    public bool MissileCameraEnabled;

    /// <summary>acPlayerComponentDamage[9]: 0 ion drive, 1 power plant, 2 shield generator, 3 computer,
    /// 4 intercom, 5 target tracking, 6 acceleration absorbers, 7 ejector, 8 repair systems.</summary>
    public sbyte[] PlayerComponentDamage { get; } = new sbyte[9];

    /// <summary>nCameraViewMode (0 cockpit front ... 15 scripted; -1 before the first view).</summary>
    public int CameraViewMode = -1;

    /// <summary>cViewObject: object the chase camera follows.</summary>
    public sbyte ViewObject = -1;

    /// <summary>cCockpitView: cockpit graphics set (player ship type, 4 = training simulator).</summary>
    public sbyte CockpitView;

    /// <summary>nCannedSceneMode: 0 normal, 1 cinematic, 2 canned-sequence AI, 4 autopilot.</summary>
    public int CannedSceneMode;

    /// <summary>nTrainSimActive: training simulator session.</summary>
    public bool TrainSimActive;

    /// <summary>bIntroSecondaryScene: attract-mode asteroid scene (hazards follow the eye).</summary>
    public bool IntroSecondaryScene;

    /// <summary>bPlayerVulnerable (debug/cheat).</summary>
    public bool PlayerVulnerable = true;

    /// <summary>bPlayerCollisionsEnabled (debug/cheat).</summary>
    public bool PlayerCollisionsEnabled = true;

    /// <summary>bPlayerCollisionResponse (debug/cheat).</summary>
    public bool PlayerCollisionResponse = true;

    /// <summary>nStartNavPointOverride (developer option, -1 none).</summary>
    public short StartNavPointOverride = -1;

    /// <summary>nCampaignDataSet: 0 original, 1 Secret Missions, 2 Secret Missions 2.</summary>
    public short CampaignDataSet;

    /// <summary>bLandingAuthorized.</summary>
    public bool LandingAuthorized;

    /// <summary>bRadioSilence.</summary>
    public bool RadioSilence;

    /// <summary>nMemoryConfiguration: 2 = expanded memory fully used (loads the second asteroid set);
    /// the SDL port always ends up with 2.</summary>
    public int MemoryConfiguration = 2;

    /// <summary>b3SpaceObjectsActive.</summary>
    public bool Space3DObjectsActive;

    // ------------------------------------------------------------------ mission state

    /// <summary>aMissionNavPoints[20].</summary>
    public MissionNavPoint[] MissionNavPoints { get; } = new MissionNavPoint[NavPointTableSize];

    /// <summary>aMissionShips[48].</summary>
    public MissionShipRecord[] MissionShips { get; } = new MissionShipRecord[MissionShipTableSize];

    /// <summary>aMissionObjectiveSources[16].</summary>
    public MissionObjectiveSource[] MissionObjectiveSources { get; } = new MissionObjectiveSource[ObjectiveCount];

    /// <summary>aMissionObjectives[16] plus one terminator slot (the original writes and reads index
    /// <c>count</c>, which is out of bounds when all 16 objectives are used).</summary>
    public MissionObjective[] MissionObjectives { get; } = new MissionObjective[ObjectiveCount + 1];

    /// <summary>abFlightPath[16] plus one terminator slot: objective indices in flight order, -1 terminated.</summary>
    public sbyte[] FlightPath { get; } = new sbyte[ObjectiveCount + 1];

    /// <summary>cMissionObjectiveCount.</summary>
    public sbyte MissionObjectiveCount;

    /// <summary>cCurrentNavPointIndex: index into <see cref="FlightPath"/>.</summary>
    public sbyte CurrentNavPointIndex;

    /// <summary>cCurrentObjective: current objective index.</summary>
    public sbyte CurrentObjective = -1;

    /// <summary>nCurrentObjectiveRange: distance to the current objective in whole units
    /// (<c>spherical.radius &gt;&gt; 8</c>).</summary>
    public int CurrentObjectiveRange;

    /// <summary>nMissionEntryNavPoint.</summary>
    public short MissionEntryNavPoint;

    /// <summary>nHomeMissionShipIndex (-1 none).</summary>
    public short HomeMissionShipIndex;

    /// <summary>nPlayerMissionShipIndex.</summary>
    public short PlayerMissionShipIndex;

    /// <summary>nInitialMissionShipIndices[8]: the player's team.</summary>
    public short[] InitialMissionShipIndices { get; } = new short[8];

    /// <summary>DAT_005a86a6: header field +0x16 (unused).</summary>
    public short MissionHeaderField16;

    /// <summary>nCarrierMissionShipIndex: first Tiger's Claw record.</summary>
    public short CarrierMissionShipIndex;

    /// <summary>abMissionAuxData[0x28]: mission name text.</summary>
    public byte[] MissionAuxData { get; } = new byte[MissionModule.AuxSize];

    /// <summary>abSeriesAuxData[0x28]: system name text.</summary>
    public byte[] SeriesAuxData { get; } = new byte[MissionModule.AuxSize];

    /// <summary>nCurrentNavPoint: nav sphere the player is in.</summary>
    public short CurrentNavPoint;

    /// <summary>nCurrentWave: next follow-up wave number or -1.</summary>
    public short CurrentWave = -1;

    /// <summary>nEnemySighting.</summary>
    public short EnemySighting = 0x7fff;

    /// <summary>bInitialFormationSetup: set while <c>prepare_mission</c> spawns the team.</summary>
    public bool InitialFormationSetup;

    /// <summary>nPlayerKillCount.</summary>
    public short PlayerKillCount;

    /// <summary>nWingmanKillCount.</summary>
    public short WingmanKillCount;

    /// <summary>nWingmanKilledThisMission.</summary>
    public bool WingmanKilledThisMission;

    /// <summary>bPlayerDestroyed.</summary>
    public bool PlayerDestroyed;

    /// <summary>nMissionMedalScore.</summary>
    public short MissionMedalScore;

    // ------------------------------------------------------------------ hazards

    /// <summary>aHazardFields[7].</summary>
    public HazardField[] HazardFields { get; } = new HazardField[HazardFieldSlots];

    /// <summary>nHazardFieldCount.</summary>
    public short HazardFieldCount;

    /// <summary>pActiveHazardField as an index into <see cref="HazardFields"/> (-1 = null).</summary>
    public int ActiveHazardField = -1;

    /// <summary>abHazardObjects[20]: object slots of active hazards (-1 empty).</summary>
    public sbyte[] HazardObjects { get; } = new sbyte[HazardObjectSlots];

    /// <summary>nActiveHazards.</summary>
    public short ActiveHazards;

    /// <summary>nHazardReferenceSpeed.</summary>
    public short HazardReferenceSpeed;

    // ------------------------------------------------------------------ constellation

    /// <summary>pConstellationDefinitions: CAMP section 0 (4 per series, series 1 first); empty until set.</summary>
    public ConstellationObjectDefinition[] ConstellationDefinitions { get; set; } = [];

    /// <summary>pConstellationShape: PLANETS.VGA section 0 (stars and dust frames).</summary>
    public ShapeRef ConstellationShape;

    /// <summary>asConstellationObjectIndices[4]: planet object slots or -1.</summary>
    public short[] ConstellationObjectIndices { get; } = new short[4];

    // ------------------------------------------------------------------ nav map scaling

    /// <summary>nNavMapCoordinateScaling.</summary>
    public bool NavMapCoordinateScaling;

    /// <summary>nNavMapScale.</summary>
    public short NavMapScale = 1;

    /// <summary>nNavMapCentreX.</summary>
    public short NavMapCentreX;

    /// <summary>nNavMapCentreY.</summary>
    public short NavMapCentreY;

    /// <summary>nNavMapMinimumX / MaximumX / MinimumY / MaximumY.</summary>
    public short NavMapMinimumX;

    public short NavMapMaximumX;

    public short NavMapMinimumY;

    public short NavMapMaximumY;

    /// <summary>The slot of the player's ship state.</summary>
    public ref SpaceObject Player => ref Objects[ObjectSlots.Player];

    /// <summary>Static data of the type of object <paramref name="obj"/>.</summary>
    public ObjectTypeData TypeDataOf(int obj) => ObjectTypeTable.Get(Objects[obj].Type);
}
