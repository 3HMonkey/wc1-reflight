using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Object slot allocation, initialisation and removal (disk.c tail, geom.c, spc.c animate_shape).
public sealed partial class SpaceSimulation
{
    /// <summary>First free slot 1..9 (class NULL); remembers it in <see cref="LastShipSlot"/>; -1 when full.</summary>
    /// <remarks>C: get_ship_slot (0x419B70, geom.c).</remarks>
    public short GetShipSlot()
    {
        for (short slot = ObjectSlots.FirstShip; slot <= ObjectSlots.LastShip; slot++)
        {
            if (Objects[slot].Class == ObjectClass.Null)
            {
                LastShipSlot = slot;
                return slot;
            }
        }
        LastShipSlot = -1;
        return -1;
    }

    /// <summary>First free effect slot 10..60, marked not visible; -1 when full.</summary>
    /// <remarks>C: find_vacant_3d_object (0x419BA0, geom.c).</remarks>
    public short FindVacant3dObject()
    {
        for (short i = ObjectSlots.FirstEffect; i <= ObjectSlots.LastMoving; i++)
        {
            if (Objects[i].Class == ObjectClass.Null)
            {
                Objects[i].ScreenX = ObjectSlots.NotVisible;
                return i;
            }
        }
        return -1;
    }

    /// <summary>First dust streak slot 34..41 that can be reused for a player projectile; -1 when none.</summary>
    /// <remarks>C: borrow_dust (0x41DF40, disk.c).</remarks>
    public short BorrowDust()
    {
        for (short i = ObjectSlots.FirstDust; i < ObjectSlots.DustEnd; i++)
        {
            if (Objects[i].Class == ObjectClass.Dust)
                return i;
        }
        return -1;
    }

    /// <summary><see cref="SetObjectsData"/> plus zero position and velocity; passes -1 through.</summary>
    /// <remarks>C: initialize_object (0x41DEE0, disk.c).</remarks>
    public short InitializeObject(short obj, ObjectType type, short owner)
    {
        if (obj != -1)
        {
            SetObjectsData(obj, type, owner);
            Objects[obj].Position = FixedVector.Zero;
            Objects[obj].Velocity = FixedVector.Zero;
        }
        return obj;
    }

    /// <summary>Creates an effect/projectile object in slots 10..60 (the player may borrow a dust slot).</summary>
    /// <remarks>C: new_object (0x41DF70, disk.c).</remarks>
    public short NewObject(ObjectType type, short owner)
    {
        short obj = FindVacant3dObject();
        if (obj == -1 && owner == 0)
            obj = BorrowDust();
        return InitializeObject(obj, type, owner);
    }

    /// <summary>Creates a ship or missile in slots 1..9, side neutral.</summary>
    /// <remarks>C: initialize_ship (0x41DFA0, disk.c).</remarks>
    public short InitializeShip(ObjectType type, short owner)
    {
        short obj = GetShipSlot();
        if (obj != -1)
        {
            InitializeObject(obj, type, owner);
            Ships[obj].Side = Side.Neutral;
        }
        return obj;
    }

    /// <summary>
    /// The universal object initialiser from the type table: substitutes types whose shapes are
    /// not loaded, sets class/shape/basis/radii/scale/owner, initialises shields, armor, fuel,
    /// loadout and weapon selection of ships, and starts the animation of effects.
    /// </summary>
    /// <remarks>C: set_objects_data (0x41E120, disk.c).</remarks>
    public void SetObjectsData(short obj, ObjectType type, short owner)
    {
        ref var o = ref Objects[obj];
        if (type == ObjectType.SpaceDust)
        {
            o.Type = type;
            o.Class = ObjectClass.Dust;
            return;
        }
        if (TypeResources[(int)type].ShapeSet.IsNone)
        {
            type = type switch
            {
                ObjectType.Asteroid2 => ObjectType.Asteroid1,
                ObjectType.Asteroid4 => ObjectType.Asteroid3,
                ObjectType.Asteroid6 => ObjectType.Asteroid5,
                ObjectType.DebrisMetalSheet => ObjectType.DebrisShipGirderChunk,
                ObjectType.DebrisWing => ObjectType.DebrisPipe,
                ObjectType.Explosion1 or ObjectType.Explosion2 => ObjectType.Explosion0,
                _ => type,
            };
        }
        var typeData = ObjectTypeTable.Get(type);
        o.Type = type;
        o.Class = typeData.ObjectClass;
        o.Shape = type == ObjectType.RockChunk
            ? TypeResources[(int)ObjectType.Asteroid1].ShapeSet
            : TypeResources[(int)type].ShapeSet;
        o.InitIjk();
        o.CollisionRadius = typeData.CollisionRadius;
        o.RadarRadius = typeData.RadarRadius;
        o.Scale = typeData.Scale;
        o.AfterburnerVelocity = typeData.AfterburnerVelocity;
        o.Owner = unchecked((sbyte)owner);
        o.AccumulatedDamage = 0;
        var objectClass = o.Class;
        o.Flip = 0;
        o.LastCollisionObject = -1;
        o.ScreenAngle = 0;

        if (objectClass >= ObjectClass.Missile)
        {
            o.ViewFrame = 0;
            // Missiles and ships only ever live in slots 0..9 (initialize_ship / prepare_mission).
            if (obj >= ObjectSlots.ShipSlotCount)
                return;
            ref var ship = ref Ships[obj];
            ship.Target = -1;
            if (objectClass >= ObjectClass.Ship)
            {
                short value = typeData.ShieldFore;
                ship.Shield[ShieldValues.Fore] = value;
                ship.MaximumShield[ShieldValues.Fore] = value;
                value = typeData.ShieldAft;
                ship.Shield[ShieldValues.Aft] = value;
                ship.MaximumShield[ShieldValues.Aft] = value;
                ship.Armor[ArmorValues.Front] = typeData.ArmorFront;
                ship.Armor[ArmorValues.Left] = typeData.ArmorLeft;
                ship.Armor[ArmorValues.Right] = typeData.ArmorRight;
                ship.Armor[ArmorValues.Rear] = typeData.ArmorRear;
                ship.Fuel = typeData.Fuel;
                ship.IonDriveDamage = 0;
                ship.Damage = 0;
                RecalcMaxVelocity(obj);
                ship.PilotHitPoints = 4;
                ship.Weapons = typeData.WeaponLoadout;

                if (obj == ObjectSlots.Player)
                {
                    SelectedReleaseWeaponIndex = -1;
                    SelectedGunType = ObjectType.None;
                    for (int weapon = ship.Weapons.Count; weapon-- > 0;)
                    {
                        if (ship.Weapons.GetDisabled(weapon) == 0)
                        {
                            var weaponType = ship.Weapons.GetWeaponType(weapon);
                            if (ObjectTypeTable.Get(weaponType).ObjectClass == ObjectClass.Projectile)
                                SelectedGunType = weaponType;
                            else
                                SelectedReleaseWeaponIndex = weapon;
                        }
                    }
                }
                ship.LastAttacker = -1;
                ship.WeaponEnergy = 100;
            }
            return;
        }

        if (!typeData.HasAnimationScript)
        {
            o.ViewFrame = typeData.YawRate;
            return;
        }
        o.AnimationDelay = 1;
        o.AnimationIndex = 0;
        AnimateShape(obj);
    }

    /// <summary>
    /// Steps the compiled-in animation script of an effect: every <c>yawRate</c> frames reads the
    /// next command (frame, grow/shrink scale, jump, remove).
    /// </summary>
    /// <remarks>C: animate_shape (0x412CD0, spc.c). As in the C code the frame mask is applied before
    /// the flip bits are extracted, so frame commands never set <see cref="SpaceObject.Flip"/>.</remarks>
    public void AnimateShape(short obj)
    {
        ref var o = ref Objects[obj];
        var type = o.Type;
        var typeData = ObjectTypeTable.Get(type);
        if (!typeData.HasAnimationScript)
            return;
        var animation = typeData.AnimationScript;
        o.AnimationDelay = unchecked((short)(o.AnimationDelay - 1));
        if (o.AnimationDelay > 0)
            return;
        o.AnimationDelay = typeData.YawRate;
        short command = unchecked((short)(ushort)animation[o.AnimationIndex]);
        switch (command & 0xf000)
        {
            case 0x9000:
                command &= 0x0fff;
                o.AnimationIndex = command;
                command = unchecked((short)(ushort)animation[command]);
                if (o.ScreenX != ObjectSlots.NotVisible &&
                    (type == ObjectType.DebrisWing || type == ObjectType.DebrisMetalSheet))
                {
                    Events.PlaySoundEffect(13, obj);
                }
                break;
            case 0xa000:
                RemoveObject(obj);
                return;
        }

        if ((command & 0x0c00) == 0x0400)
        {
            o.Scale = unchecked((short)(o.Scale + (command & 0x3f) * (o.Scale >> 6)));
        }
        else if ((command & 0x0c00) == 0x0800)
        {
            o.Scale = unchecked((short)(o.Scale - (command & 0x3f) * (o.Scale >> 6)));
        }
        else
        {
            command &= 0x3f;
            o.ViewFrame = command;
        }
        o.Flip = (short)((command & 0xc0) >> 2);
        o.AnimationIndex = unchecked((short)(o.AnimationIndex + 1));
    }

    /// <summary>
    /// Frees a slot: not visible, clears nav pointer/wingman/hazard references and the ship
    /// AI identity fields (other ship fields stay stale, as in the original).
    /// </summary>
    /// <remarks>C: remove_object (0x419BD0, geom.c).</remarks>
    public void RemoveObject(short obj)
    {
        if (obj == -1)
            return;
        ref var o = ref Objects[obj];
        o.ScreenX = ObjectSlots.NotVisible;
        o.Distance = 0;
        if (obj == NavPointerObject)
            NavPointerObject = -1;
        if (obj == YourWingman)
            YourWingman = -1;
        for (int slot = 0; slot < HazardObjectSlots; slot++)
        {
            if (HazardObjects[slot] == obj)
            {
                HazardObjects[slot] = -1;
                break;
            }
        }
        if (obj < ObjectSlots.ShipSlotCount)
        {
            if (o.Class == ObjectClass.CapitalShip)
                o.Shape = ShapeRef.None;
            ref var ship = ref Ships[obj];
            ship.Rating = -1;
            ship.WingmanMessageState = -1;
            ship.Side = Side.Neutral;
            ship.Maneuver = ShipManeuver.None;
            ClearAlert(obj);
            ship.CapitalShipViewFrame = -1;
        }
        o.Class = ObjectClass.Null;
        o.Shape = ShapeRef.None;
    }

    /// <remarks>C: remove_all_3d_objects (0x424B80, logic.c).</remarks>
    public void RemoveAll3dObjects()
    {
        for (short i = 0; i < ObjectSlots.Count; i++)
            RemoveObject(i);
    }

    /// <summary>Removes slots 0..9.</summary>
    /// <remarks>C: remove_nav_point_objects (0x40BEA0, brains.c).</remarks>
    public void RemoveNavPointObjects()
    {
        for (short i = 0; i < ObjectSlots.ShipSlotCount; i++)
            RemoveObject(i);
    }

    /// <summary>
    /// Creates a static "planet" sprite object for the end-game cutscenes: placed
    /// <paramref name="distance"/> units out along a yawed scratch frame; the sprite type is stored in
    /// both the screen angle and the object type, as in the original. Returns the slot or -1.
    /// </summary>
    /// <remarks>C: CreateCannedSceneObject (0x42FB40, screen.c); the unused pitch argument is dropped.</remarks>
    public short CreateCannedSceneObject(short yaw, short distance, ShapeRef shape, short frame, short type, short scale)
    {
        short obj = FindVacant3dObject();
        if (obj == -1)
            return -1;
        ref var o = ref Objects[obj];
        o.Class = ObjectClass.Planet;
        ref var scratch = ref Objects[ObjectSlots.Scratch];
        scratch.InitIjk();
        scratch.AlterYaw(yaw);
        o.Position = VectorMath.Scale(scratch.Forward, distance << 8);
        o.ViewFrame = frame;
        o.ScreenAngle = type;
        o.Type = (ObjectType)o.ScreenAngle;
        o.ScreenScale = scale;
        o.Shape = shape;
        return obj;
    }

    /// <summary>World position of hardpoint <paramref name="hardpoint"/> of <paramref name="parent"/>:
    /// the unit basis (8.8) times the integer offset is already 24.8.</summary>
    /// <remarks>C: position_child (0x419A70, geom.c).</remarks>
    public FixedVector PositionChild(short parent, short hardpoint)
    {
        var offset = GeometryTables.ChildOffsets[hardpoint];
        ref readonly var p = ref Objects[parent];
        return new FixedVector(
            unchecked(p.Forward.X * offset.Z + p.Up.X * offset.Y + p.Right.X * offset.X + p.Position.X),
            unchecked(p.Forward.Y * offset.Z + p.Up.Y * offset.Y + p.Right.Y * offset.X + p.Position.Y),
            unchecked(p.Forward.Z * offset.Z + p.Up.Z * offset.Y + p.Right.Z * offset.X + p.Position.Z));
    }

    /// <summary>Places <paramref name="child"/> at a hardpoint of <paramref name="parent"/> and makes the parent its owner.</summary>
    /// <remarks>C: child_object (0x419B40, geom.c).</remarks>
    public void ChildObject(short hardpoint, short child, short parent)
    {
        Objects[child].Position = PositionChild(parent, hardpoint);
        Objects[child].Owner = unchecked((sbyte)parent);
    }
}
