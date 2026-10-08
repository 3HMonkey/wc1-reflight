using WingCommander.Core.Numerics;
using WingCommander.Core.Resources;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Ship shape slots, common/mission effect shapes, constellation and 3-space init/teardown
// (brains.c load_ship..new_sphere_shapes, logic.c init_3Space_objects..free_3Space_objects).
public sealed partial class SpaceSimulation
{
    /// <summary>Logical file of a ship type's shapes (<c>type + 22</c>: SHIPTYPE.Vxx / SHIP.Vxx).</summary>
    /// <remarks>C: <c>cObjectResourceLogicalFile = type + 22</c> (load_ship) and
    /// <c>cCapitalShipLogicalFile</c> (get_right_shape).</remarks>
    public static int ShipLogicalFile(ObjectType type) => (int)type + 22;

    /// <summary>Section of a capital ship file holding the silhouette (0x25; sections 0..0x24 are view frames).</summary>
    public const int CapitalShipSilhouetteSection = 0x25;

    /// <summary>
    /// Common 3-space effect shapes in OBJECTS.VGA (logical file 3), loaded by
    /// <see cref="LoadCommon3SpaceObjects"/>.
    /// </summary>
    /// <remarks>C: aCommon3SpaceResources (0x00469bc0, globals.c).</remarks>
    private static readonly (ObjectType Type, int Section)[] Common3SpaceResources =
    [
        (ObjectType.Thrusters, 0),
        (ObjectType.Explosion0, 1),
        (ObjectType.LaserCannon, 6),
        (ObjectType.MassDriverCannon, 7),
        (ObjectType.NeutronParticleGun, 8),
        (ObjectType.LaserSpark, 9),
        (ObjectType.DebrisPipe, 4),
        (ObjectType.BlueSpark, 10),
        (ObjectType.RedSpark, 11),
        (ObjectType.SparkTrail, 12),
        (ObjectType.SpaceMine, 15),
    ];

    /// <summary>Mission effect shapes in OBJECTS.VGA loaded by <c>init_mission</c>.</summary>
    /// <remarks>C: aMissionResourceDescriptors (0x00469c20, globals.c).</remarks>
    private static readonly (ObjectType Type, int Section)[] MissionResources =
    [
        (ObjectType.HyperspaceJumpFlash, 14),
        (ObjectType.Explosion1, 2),
        (ObjectType.Explosion2, 3),
        (ObjectType.DebrisMetalSheet, 5),
    ];

    /// <summary>Loads a section as a shape reference (<see cref="ShapeRef.None"/> when it does not exist).</summary>
    /// <remarks>C: FetchDiskPacketRetrying(logicalFile, section, flags).</remarks>
    public ShapeRef FetchShape(int logicalFile, int section) =>
        Resources.SectionExists(logicalFile, section) ? new ShapeRef(logicalFile, section) : ShapeRef.None;

    /// <summary>
    /// Loads the shapes of a ship type into resource slot <paramref name="slot"/>: fighters and
    /// missiles get sprite set (section 0), exhaust table (2) and silhouette (1); capital ships only
    /// the silhouette (their per-view frames are resolved on demand from <c>type + 22</c>);
    /// ASTEROID_FIELD loads the asteroid and rock-chunk sets from OBJECTS.VGA.
    /// </summary>
    /// <remarks>C: load_ship (0x40B9F0, brains.c). The original also preloads the 37 capital frame
    /// packets into aapPacketReferences when expanded memory is available; that cache is a
    /// rendering concern (the frame is <c>ShapeRef(type + 22, frame)</c> either way).</remarks>
    public void LoadShip(ObjectType type, short slot)
    {
        if (type == ObjectType.None)
            return;
        ref var resource = ref ResourceSlots[slot];
        if (!resource.ShapeSet.IsNone)
            return;
        resource.Type = type;
        int logicalFile = ShipLogicalFile(type);
        if (type == ObjectType.AsteroidField)
        {
            TypeResources[(int)ObjectType.RockChunk].ShapeSet = FetchShape(LogicalFile.ObjectsVga, 13);
            TypeResources[(int)ObjectType.Asteroid5].ShapeSet = FetchShape(LogicalFile.ObjectsVga, 16);
            TypeResources[(int)ObjectType.Asteroid3].ShapeSet = TypeResources[(int)ObjectType.Asteroid5].ShapeSet;
            TypeResources[(int)ObjectType.Asteroid1].ShapeSet = TypeResources[(int)ObjectType.Asteroid5].ShapeSet;
            resource.ShapeSet = TypeResources[(int)ObjectType.Asteroid5].ShapeSet;
            if (MemoryConfiguration == 2)
            {
                TypeResources[(int)ObjectType.Asteroid6].ShapeSet = FetchShape(LogicalFile.ObjectsVga, 17);
                TypeResources[(int)ObjectType.Asteroid4].ShapeSet = TypeResources[(int)ObjectType.Asteroid6].ShapeSet;
                TypeResources[(int)ObjectType.Asteroid2].ShapeSet = TypeResources[(int)ObjectType.Asteroid6].ShapeSet;
            }
            for (short obj = ObjectSlots.FirstEffect; obj <= ObjectSlots.LastMoving; obj++)
            {
                if (Objects[obj].Class == ObjectClass.Asteroid)
                    Objects[obj].Shape = TypeResources[(int)Objects[obj].Type].ShapeSet;
            }
            return;
        }
        if (type == ObjectType.MineField)
            return;

        var objectClass = ObjectTypeTable.Get(type).ObjectClass;
        if (objectClass != ObjectClass.Ship && objectClass != ObjectClass.Missile)
        {
            resource.Shape = FetchShape(logicalFile, CapitalShipSilhouetteSection);
            TypeResources[(int)type].Shape = resource.Shape;
            for (short obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
            {
                if (Objects[obj].Type == type)
                {
                    Objects[obj].Shape = ShapeRef.None;
                    Ships[obj].CapitalShipViewFrame = -1;
                }
            }
            return;
        }

        resource.ShapeSet = FetchShape(logicalFile, 0);
        TypeResources[(int)type].ShapeSet = resource.ShapeSet;
        resource.Animation = FetchShape(logicalFile, 2);
        TypeResources[(int)type].Animation = resource.Animation;
        ExhaustTables[(int)type] = resource.Animation.IsNone
            ? null
            : new ExhaustTable(Resources.LoadSection(logicalFile, 2).Span);
        resource.Shape = FetchShape(logicalFile, 1);
        TypeResources[(int)type].Shape = resource.Shape;
        for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Objects[obj].Class >= ObjectClass.Missile && Objects[obj].Type == type)
                Objects[obj].Shape = resource.ShapeSet;
        }
    }

    /// <summary>Parsed engine flame tables of the loaded ship and missile types (the bytes behind
    /// <see cref="ObjectTypeResources.Animation"/>), indexed by type; null when not loaded.</summary>
    /// <remarks>C: <c>aObjectTypeData[type].animation</c> of ships and missiles (load_ship), read by
    /// place_exhaust_on_ships.</remarks>
    public ExhaustTable?[] ExhaustTables { get; } = new ExhaustTable?[ObjectTypeTable.Count];

    /// <summary>Releases the shapes of resource slot <paramref name="slot"/> (the slot keeps its type).</summary>
    /// <remarks>C: free_ship (0x40BC70, brains.c), with the SDL port's guard for empty slots.</remarks>
    public void FreeShip(short slot)
    {
        ref var resource = ref ResourceSlots[slot];
        if (resource.Type == ObjectType.None)
            return;
        var type = resource.Type;
        var objectClass = ObjectTypeTable.Get(type).ObjectClass;
        if (objectClass == ObjectClass.CapitalShip)
        {
            ReleaseCapitalShipShapes(type);
            resource.Shape = ShapeRef.None;
            TypeResources[(int)type].Shape = ShapeRef.None;
        }
        if (resource.ShapeSet.IsNone)
            return;
        resource.ShapeSet = ShapeRef.None;
        if (type == ObjectType.AsteroidField)
        {
            TypeResources[(int)ObjectType.RockChunk].ShapeSet = ShapeRef.None;
            TypeResources[(int)ObjectType.Asteroid2].ShapeSet = ShapeRef.None;
            TypeResources[(int)ObjectType.Asteroid6].ShapeSet = ShapeRef.None;
            TypeResources[(int)ObjectType.Asteroid5].ShapeSet = ShapeRef.None;
            TypeResources[(int)ObjectType.Asteroid4].ShapeSet = ShapeRef.None;
            TypeResources[(int)ObjectType.Asteroid3].ShapeSet = ShapeRef.None;
            TypeResources[(int)ObjectType.Asteroid1].ShapeSet = ShapeRef.None;
            for (short obj = ObjectSlots.FirstEffect; obj <= ObjectSlots.LastMoving; obj++)
            {
                if (Objects[obj].Type == ObjectType.RockChunk)
                    RemoveObject(obj);
                else if (Objects[obj].Class == ObjectClass.Asteroid)
                    Objects[obj].Shape = ShapeRef.None;
            }
            return;
        }
        if (type == ObjectType.MineField)
            return;
        TypeResources[(int)type].ShapeSet = ShapeRef.None;
        resource.Animation = ShapeRef.None;
        TypeResources[(int)type].Animation = ShapeRef.None;
        ExhaustTables[(int)type] = null;
        resource.Shape = ShapeRef.None;
        TypeResources[(int)type].Shape = ShapeRef.None;
        for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Objects[obj].Class >= ObjectClass.Missile && Objects[obj].Type == type)
                Objects[obj].Shape = ShapeRef.None;
        }
    }

    /// <summary>Drops the loaded view frame of every capital ship (slots 0..9).</summary>
    /// <remarks>C: release_all_capital_ship_shapes (0x40B940, brains.c).</remarks>
    public void ReleaseAllCapitalShipShapes()
    {
        for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Objects[obj].Class == ObjectClass.CapitalShip)
            {
                Objects[obj].Shape = ShapeRef.None;
                Ships[obj].CapitalShipViewFrame = -1;
            }
        }
    }

    /// <summary>Drops the loaded view frame of the capital ships of one type.</summary>
    /// <remarks>C: release_capital_ship_shapes (0x40B990, brains.c).</remarks>
    public void ReleaseCapitalShipShapes(ObjectType type)
    {
        if (ObjectTypeTable.Get(type).ObjectClass != ObjectClass.CapitalShip)
            return;
        for (short obj = 1; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Objects[obj].Type == type)
            {
                Objects[obj].Shape = ShapeRef.None;
                Ships[obj].CapitalShipViewFrame = -1;
            }
        }
    }

    /// <remarks>C: free_all_slots (0x40BE20, brains.c).</remarks>
    public void FreeAllSlots()
    {
        ReleaseAllCapitalShipShapes();
        for (short slot = 0; slot < 3; slot++)
        {
            if (ResourceSlots[slot].Type != ObjectType.None)
                FreeShip(slot);
        }
    }

    /// <remarks>C: load_all_slots (0x40BE60, brains.c).</remarks>
    public void LoadAllSlots()
    {
        ReleaseAllCapitalShipShapes();
        for (short slot = 0; slot < 3; slot++)
        {
            var type = ResourceSlots[slot].Type;
            if (type != ObjectType.None)
                LoadShip(type, slot);
        }
    }

    /// <summary>First resource slot without a type, or -1.</summary>
    /// <remarks>C: get_shape_slot (0x40BEC0, brains.c).</remarks>
    public short GetShapeSlot()
    {
        for (short slot = 0; slot < ResourceSlots.Length; slot++)
        {
            if (ResourceSlots[slot].Type == ObjectType.None)
                return slot;
        }
        return -1;
    }

    /// <remarks>C: shape_loaded (0x40BEF0, brains.c).</remarks>
    public bool ShapeLoaded(ObjectType type)
    {
        foreach (var slot in ResourceSlots)
        {
            if (slot.Type == type)
                return true;
        }
        return false;
    }

    /// <remarks>C: shape_needed (0x40BF20, brains.c).</remarks>
    public static bool ShapeNeeded(in MissionNavPoint navPoint, ObjectType type) =>
        type != ObjectType.None &&
        (navPoint.PreloadObjectTypes[0] == type || navPoint.PreloadObjectTypes[1] == type);

    /// <summary>Frees slots 1..2 whose type the nav point does not need and loads its preload types.</summary>
    /// <remarks>C: new_sphere_shapes (0x40BF50, brains.c).</remarks>
    public void NewSphereShapes(int navPoint)
    {
        ReleaseAllCapitalShipShapes();
        for (short slot = 1; slot < 3; slot++)
        {
            ref var resource = ref ResourceSlots[slot];
            if (resource.Type != ObjectType.None && !ShapeNeeded(MissionNavPoints[navPoint], resource.Type))
            {
                FreeShip(slot);
                resource.Type = ObjectType.None;
            }
        }
        for (int preload = 0; preload < 2; preload++)
        {
            var type = MissionNavPoints[navPoint].PreloadObjectTypes[preload];
            if (type != ObjectType.None && !ShapeLoaded(type))
            {
                short slot = GetShapeSlot();
                if (slot != -1)
                    LoadShip(type, slot);
            }
        }
    }

    /// <summary>
    /// Clears all objects and frame counters, resets the resource slots, places the series'
    /// constellation planets and loads the common effect shapes. No-op while active.
    /// </summary>
    /// <remarks>C: init_3Space_objects (0x424A80, logic.c); the camera/viewport resets
    /// (cScreenViewportMode, bScriptedView) belong to the renderer.</remarks>
    public void Init3SpaceObjects(short scene)
    {
        if (Space3DObjectsActive)
            return;
        Space3DObjectsActive = true;
        RemoveAll3dObjects();
        ExternalViewShip = -1;
        RenderedSpaceFrame = 0;
        SpaceFrame = 0;
        MissileCameraEnabled = false;
        ClosestVisibleObject = -1;
        PlayerCollisionObject = -1;
        for (int slot = 0; slot <= 3; slot++)
            ResourceSlots[slot].Type = ObjectType.None;
        InitConstellation(scene);
        LoadCommon3SpaceObjects();
    }

    /// <summary>Loads the common effect shapes and aliases debris, turret and missile shapes.</summary>
    /// <remarks>C: load_common_3Space_objects (0x424B00, logic.c).</remarks>
    public void LoadCommon3SpaceObjects()
    {
        LoadShapeSet(Common3SpaceResources);
        var pipe = TypeResources[(int)ObjectType.DebrisPipe].ShapeSet;
        TypeResources[(int)ObjectType.DebrisORing].ShapeSet = pipe;
        TypeResources[(int)ObjectType.DebrisGlass].ShapeSet = pipe;
        TypeResources[(int)ObjectType.DebrisShipTubing].ShapeSet = pipe;
        TypeResources[(int)ObjectType.DebrisShipGirderChunk].ShapeSet = pipe;
        TypeResources[(int)ObjectType.Turret].ShapeSet = TypeResources[(int)ObjectType.LaserCannon].ShapeSet;
        TypeResources[(int)ObjectType.Turret].Animation = TypeResources[(int)ObjectType.LaserCannon].Animation;

        LoadShip(ObjectType.HeatSeekingMissile, 3);
        var heatSeeker = TypeResources[(int)ObjectType.HeatSeekingMissile];
        foreach (var missile in (ReadOnlySpan<ObjectType>)[ObjectType.DumbFireMissile, ObjectType.ImageRecognitionMissile, ObjectType.FriendOrFoeMissile])
            TypeResources[(int)missile].ShapeSet = heatSeeker.ShapeSet;
        foreach (var missile in (ReadOnlySpan<ObjectType>)[ObjectType.DumbFireMissile, ObjectType.ImageRecognitionMissile, ObjectType.FriendOrFoeMissile])
        {
            TypeResources[(int)missile].Animation = heatSeeker.Animation;
            ExhaustTables[(int)missile] = ExhaustTables[(int)ObjectType.HeatSeekingMissile];
        }
    }

    /// <summary>Loads the mission effect shapes (jump flash, explosions 1/2, metal sheet), stopping at
    /// the first missing one.</summary>
    /// <remarks>C: LoadPacketResourceList(aMissionResourceDescriptors, 0, nAvailableGameMemory) in
    /// init_mission; the memory budget never limits the port.</remarks>
    public void LoadMissionResources() => LoadShapeSet(MissionResources);

    /// <summary>Tears down the 3-space objects; no-op while inactive.</summary>
    /// <remarks>C: free_3Space (0x424BA0, logic.c).</remarks>
    public void Free3Space()
    {
        if (!Space3DObjectsActive)
            return;
        Space3DObjectsActive = false;
        FreeConstellation();
        RemoveAllHazards();
        RemoveAll3dObjects();
        Free3SpaceObjects();
    }

    /// <summary>Releases the common and mission effect shapes and the missile slot.</summary>
    /// <remarks>C: free_3Space_objects (0x424BE0, logic.c).</remarks>
    public void Free3SpaceObjects()
    {
        foreach (var (type, _) in Common3SpaceResources)
            TypeResources[(int)type].ShapeSet = ShapeRef.None;
        foreach (var (type, _) in MissionResources)
            TypeResources[(int)type].ShapeSet = ShapeRef.None;
        FreeShip(3);
        foreach (var missile in (ReadOnlySpan<ObjectType>)[ObjectType.DumbFireMissile, ObjectType.ImageRecognitionMissile, ObjectType.FriendOrFoeMissile])
        {
            TypeResources[(int)missile].ShapeSet = ShapeRef.None;
            TypeResources[(int)missile].Animation = ShapeRef.None;
            ExhaustTables[(int)missile] = null;
        }
        var pipe = TypeResources[(int)ObjectType.DebrisPipe].ShapeSet;
        TypeResources[(int)ObjectType.DebrisORing].ShapeSet = pipe;
        TypeResources[(int)ObjectType.DebrisGlass].ShapeSet = pipe;
        TypeResources[(int)ObjectType.DebrisShipTubing].ShapeSet = pipe;
        TypeResources[(int)ObjectType.DebrisShipGirderChunk].ShapeSet = pipe;
        TypeResources[(int)ObjectType.DebrisWing].ShapeSet = TypeResources[(int)ObjectType.DebrisMetalSheet].ShapeSet;
    }

    /// <summary>
    /// Loads the constellation sprite packet and places the series' (scene - 1) background planets
    /// from <see cref="ConstellationDefinitions"/>. No planets in the training simulator.
    /// </summary>
    /// <remarks>C: init_constellation (0x4243E0, logic.c). Definitions beyond the provided table count as
    /// "none" (the Game sets the table from CAMP.xxx section 0).</remarks>
    public void InitConstellation(short scene)
    {
        if (!ConstellationShape.IsNone)
            return;
        scene--;
        ConstellationShape = FetchShape(LogicalFile.PlanetsVga, 0);
        if (TrainSimActive || scene < 0)
            return;
        int definitionBase = scene * 4;
        for (int slot = 0; slot < 4; slot++)
        {
            int index = definitionBase + slot;
            if (index < ConstellationDefinitions.Length && ConstellationDefinitions[index].ShapePacket != -1)
            {
                short obj = FindVacant3dObject();
                if (obj != -1)
                    InitializeConstellationObject(ConstellationDefinitions[index], obj);
                ConstellationObjectIndices[slot] = obj;
            }
            else
            {
                ConstellationObjectIndices[slot] = -1;
            }
        }
    }

    /// <summary>Places a background planet 30000 units out along the rotated scratch frame (slot 63).</summary>
    /// <remarks>C: InitializeConstellationObject (0x4242D0, logic.c).</remarks>
    public void InitializeConstellationObject(in ConstellationObjectDefinition definition, short obj)
    {
        Objects[obj].Class = ObjectClass.Planet;
        ref var scratch = ref Objects[ObjectSlots.Scratch];
        scratch.InitIjk();
        scratch.AlterYaw(unchecked((short)-definition.Yaw));
        scratch.AlterPitch(unchecked((short)-definition.Pitch));
        scratch.AlterRoll(definition.Roll);
        var position = VectorMath.Scale(scratch.Forward, 0x753000);
        ref var o = ref Objects[obj];
        o.Position = position;
        o.ScreenScale = 0xff;
        o.ScreenAngle = 0;
        o.ViewFrame = 0;
        o.Type = ObjectType.Hornet;
        o.Shape = FetchShape(LogicalFile.PlanetsVga, definition.ShapePacket + 1);
    }

    /// <remarks>C: FreeConstellationObject (0x4243B0, logic.c).</remarks>
    public void FreeConstellationObject(short obj)
    {
        Objects[obj].Shape = ShapeRef.None;
        RemoveObject(obj);
    }

    /// <remarks>C: free_constellation (0x424490, logic.c).</remarks>
    public void FreeConstellation()
    {
        ConstellationShape = ShapeRef.None;
        for (int slot = 0; slot < 4; slot++)
        {
            short obj = ConstellationObjectIndices[slot];
            if (obj != -1)
            {
                FreeConstellationObject(obj);
                ConstellationObjectIndices[slot] = -1;
            }
        }
    }

    /// <summary>Builds the 62 sprite view frames: straight up, five pitch bands (60..-60) of twelve
    /// yaw sectors (0..330), straight down.</summary>
    /// <remarks>C: initialize_direction_view_frames (0x421EF0, logic.c).</remarks>
    public void InitializeDirectionViewFrames()
    {
        int frame = 1;
        short pitch = 90;
        InitializeDirectionViewFrame(0, pitch, 0);
        for (int band = 0; band < 5; band++)
        {
            pitch -= 30;
            short yaw = 0;
            for (int sector = 0; sector < 12; sector++)
            {
                InitializeDirectionViewFrame(yaw, pitch, frame++);
                yaw += 30;
            }
        }
        InitializeDirectionViewFrame(0, -90, frame);
    }

    /// <remarks>C: initialize_direction_view_frame (0x421E20, logic.c).</remarks>
    private void InitializeDirectionViewFrame(short yaw, short pitch, int frame)
    {
        var right = new FixedVector(0x100, 0, 0);
        var up = new FixedVector(0, 0x100, 0);
        var forward = new FixedVector(0, 0, 0x100);
        VectorMath.RotateAboutJ(yaw, ref right, ref forward);
        VectorMath.RotateAboutI(pitch, ref up, ref forward);
        DirectionViewRight[frame] = right;
        DirectionViewUp[frame] = up;
        DirectionViewForward[frame] = forward;
    }

    /// <summary>Loads each descriptor whose shape is still missing; stops at the first section that
    /// cannot be loaded.</summary>
    /// <remarks>C: LoadShapeSet (0x423CE0, logic.c).</remarks>
    private bool LoadShapeSet(ReadOnlySpan<(ObjectType Type, int Section)> resources)
    {
        foreach (var (type, section) in resources)
        {
            if (!TypeResources[(int)type].ShapeSet.IsNone)
                continue;
            var shape = FetchShape(LogicalFile.ObjectsVga, section);
            TypeResources[(int)type].ShapeSet = shape;
            if (shape.IsNone)
                return false;
        }
        return true;
    }
}
