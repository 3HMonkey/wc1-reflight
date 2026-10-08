using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Physical collisions and forces (geom.c check_for_collision .. rotational_acceleration, ship.c
// check_for_lost_control, spc.c object_collision).
public sealed partial class SpaceSimulation
{
    /// <summary>
    /// First object of class PROJECTILE or higher in slots 0..60 whose sphere touches
    /// <paramref name="obj"/>'s (radius sum, halved between two fighters); the delta to it is left
    /// in <see cref="CollisionDelta"/>. -1 when none.
    /// </summary>
    /// <remarks>C: check_for_collision (0x4199C0, geom.c).</remarks>
    public short CheckForCollision(short obj)
    {
        ref readonly var o = ref Objects[obj];
        for (short other = 0; other <= ObjectSlots.LastMoving; other++)
        {
            ref readonly var candidate = ref Objects[other];
            if (other == obj || candidate.Class < ObjectClass.Projectile)
                continue;
            CollisionDelta = VectorMath.Delta(o.Position, candidate.Position);
            short range = unchecked((short)(candidate.CollisionRadius + o.CollisionRadius));
            if (candidate.Class == ObjectClass.Ship && o.Class == ObjectClass.Ship)
                range >>= 1;
            if (VectorMath.IsVectorWithinRange(CollisionDelta, range))
                return other;
        }
        return -1;
    }

    /// <summary>Pushes <paramref name="obj"/> by <paramref name="force"/> divided by its mass (radar radius).</summary>
    /// <remarks>C: apply_force_to_objects_center (0x419CC0, geom.c).</remarks>
    public void ApplyForceToObjectsCenter(FixedVector force, short obj)
    {
        ref var o = ref Objects[obj];
        var acceleration = VectorMath.Divide(force, (ushort)o.RadarRadius << 8);
        o.Velocity = VectorMath.Add(o.Velocity, acceleration);
    }

    /// <summary>
    /// A force at a point of the object: torque terms (local point x local force) divided by the
    /// rotational inertia <c>afterburnerVelocity / collisionRadius</c> are added to the roll, yaw and
    /// pitch rates (clamped to ±30), and a translational part scaled by how central the point is
    /// (<c>(1.414 - planar distance) / (1.414 * mass)</c>) to the velocity; a fighter may lose control.
    /// </summary>
    /// <remarks>C: apply_force_to_object (0x419D10, geom.c).</remarks>
    public void ApplyForceToObject(FixedVector point, FixedVector force, short obj)
    {
        ref var o = ref Objects[obj];
        var localForce = o.TransformToObjectsFrame(force);
        var localPoint = o.TransformToObjectsFrame(point);
        int rotationalMass = FixedMath.Divide((ushort)o.AfterburnerVelocity << 8, o.CollisionRadius << 8);
        AddTorque(ref o, localPoint, localForce, rotationalMass);

        int mass = (ushort)o.RadarRadius << 8;
        int denominator = FixedMath.Multiply(0x16a, mass);
        var acceleration = new FixedVector(
            FixedMath.Divide(
                FixedMath.Multiply(0x16a - FixedMath.PlanarMagnitude(localPoint.Y, localPoint.Z), localForce.X),
                denominator),
            FixedMath.Divide(
                FixedMath.Multiply(0x16a - FixedMath.PlanarMagnitude(localPoint.X, localPoint.Z), localForce.Y),
                denominator),
            FixedMath.Divide(
                FixedMath.Multiply(0x16a - FixedMath.PlanarMagnitude(localPoint.X, localPoint.Y), localForce.Z),
                denominator));
        o.Velocity = VectorMath.Add(o.Velocity, acceleration);
        if (o.Class == ObjectClass.Ship)
            CheckForLostControl(obj);
    }

    /// <summary>The torque-only variant of <see cref="ApplyForceToObject"/> used for collision
    /// tangents, with the inertia <c>afterburnerVelocity / (collisionRadius * 18.23)</c>.</summary>
    /// <remarks>C: rotational_acceleration (0x419F70, geom.c).</remarks>
    public void RotationalAcceleration(FixedVector point, FixedVector force, short obj)
    {
        ref var o = ref Objects[obj];
        var localForce = o.TransformToObjectsFrame(force);
        var localPoint = o.TransformToObjectsFrame(point);
        int denominator = FixedMath.Divide(
            (ushort)o.AfterburnerVelocity << 8,
            FixedMath.Multiply(o.CollisionRadius << 8, 0x123c));
        AddTorque(ref o, localPoint, localForce, denominator);
        if (o.Class == ObjectClass.Ship)
            CheckForLostControl(obj);
    }

    /// <summary>The shared roll/yaw/pitch update of apply_force_to_object and rotational_acceleration.</summary>
    private static void AddTorque(ref SpaceObject o, in FixedVector localPoint, in FixedVector localForce, int inertia)
    {
        int value = FixedMath.Divide(
            FixedMath.Multiply(localPoint.X, localForce.Y) - FixedMath.Multiply(localPoint.Y, localForce.X),
            inertia);
        o.RollRotation = unchecked((short)(o.RollRotation + (short)(value >> 8)));
        value = FixedMath.Divide(
            FixedMath.Multiply(localPoint.X, localForce.Z) - FixedMath.Multiply(localPoint.Z, localForce.X),
            inertia);
        o.YawRotation = unchecked((short)(o.YawRotation + (short)(value >> 8)));
        value = FixedMath.Divide(
            FixedMath.Multiply(localPoint.Z, localForce.Y) - FixedMath.Multiply(localPoint.Y, localForce.Z),
            inertia);
        o.PitchRotation = unchecked((short)(o.PitchRotation + (short)(value >> 8)));
        ScalarMath.ClampTo30(ref o.PitchRotation);
        ScalarMath.ClampTo30(ref o.YawRotation);
        ScalarMath.ClampTo30(ref o.RollRotation);
    }

    /// <summary>After a push, an NPC fighter fails a skill check against its spin (sum of |rates|
    /// over the sum of its type's turn rates) and tumbles (BLOWING_UP) for 5..11 frames.</summary>
    /// <remarks>C: check_for_lost_control (0x41E650, ship.c). A type whose turn rates sum to 0 is
    /// skipped (the original would divide by zero; no fighter has such rates).</remarks>
    public void CheckForLostControl(short obj)
    {
        if (obj == ObjectSlots.Player || Ships[obj].SpecialManeuver == SpecialManeuver.Unknown9)
            return;
        ref readonly var o = ref Objects[obj];
        var typeData = TypeDataOf(obj);
        short spin = unchecked((short)(System.Math.Abs((int)o.RollRotation) + System.Math.Abs((int)o.YawRotation) +
                                       System.Math.Abs((int)o.PitchRotation)));
        short rates = unchecked((short)(typeData.RollRate + typeData.YawRate + typeData.PitchRate));
        if (rates == 0)
            return;
        if (!SkillCheck(obj, unchecked((short)(spin / rates))))
        {
            SetSpecial(obj, SpecialManeuver.BlowingUp);
            Objects[obj].Counter = unchecked((short)(Random.BelowOrEqual(6) + 5));
        }
    }

    /// <summary>
    /// Collision response of <paramref name="obj"/> with the first object it touches: projectiles
    /// damage and push what they hit and turn into a spark, asteroids shatter each other, mines and
    /// missiles explode, ships bounce off asteroids, ships and capital ships with an exchange of
    /// normal velocity by mass, a spin from the tangential motion and damage of
    /// <c>speed² / 2</c>. Off-screen asteroids and unowned mines are removed instead of hitting a ship.
    /// </summary>
    /// <remarks>C: object_collision (0x4130D0, spc.c).</remarks>
    public void ObjectCollision(short obj)
    {
        short partner = CheckForCollision(obj);
        if (partner == -1)
        {
            Objects[obj].LastCollisionObject = -1;
            return;
        }
        if (!PlayerCollisionResponse && (obj == ObjectSlots.Player || partner == ObjectSlots.Player))
            return;

        VectorMath.Normalize(ref CollisionDelta);
        var relativeVelocity = VectorMath.Delta(Objects[partner].Velocity, Objects[obj].Velocity);
        VectorMath.Normalize(ref relativeVelocity);
        sbyte owner = Objects[obj].Owner;
        ObjectClass partnerClass;
        switch (Objects[obj].Class)
        {
            case ObjectClass.Projectile:
            {
                if (owner == partner)
                    break;
                partnerClass = Objects[partner].Class;
                if (partnerClass > ObjectClass.Mine)
                {
                    if (partner == ObjectSlots.Player)
                        FlashCockpitHitSide(obj);
                    Ships[partner].LastAttacker = owner;
                    Ships[partner].AiCooldown = unchecked((sbyte)(Ships[partner].AiCooldown + 4));
                    short damage = unchecked((short)(Objects[obj].AccumulatedDamage - Objects[obj].Counter / 2));
                    var force = Objects[obj].Velocity;
                    VectorMath.Normalize(ref force);
                    force = VectorMath.Scale(force, damage << 8);
                    CollisionDelta = VectorMath.Negate(CollisionDelta);
                    ApplyForceToObject(CollisionDelta, force, partner);
                    InflictDamage(obj, partner, damage, relativeVelocity);
                }
                short savedScale = Objects[obj].Scale;
                SetObjectsData(obj, ObjectType.LaserSpark, owner);
                Objects[obj].Scale = unchecked((short)(savedScale * 2));
                Objects[obj].Velocity = Objects[partner].Velocity;
                if (partnerClass == ObjectClass.Asteroid)
                    HitAsteroid(partner, 3);
                return;
            }
            case ObjectClass.Asteroid:
                if (Objects[partner].Class == ObjectClass.Asteroid)
                {
                    if (Objects[obj].ScreenX == ObjectSlots.NotVisible)
                    {
                        RemoveObject(obj);
                        return;
                    }
                    HitAsteroid(obj, 0);
                    return;
                }
                break;
            case ObjectClass.Mine:
                if (owner == partner || Objects[obj].CollisionGraceTicks > 0)
                    return;
                if (!Easy2See(obj) && (CameraViewMode == 0 || partner != ObjectSlots.Player))
                {
                    RemoveObject(obj);
                    return;
                }
                Explode(obj, obj);
                return;
            case ObjectClass.Missile:
                if (owner != partner || Objects[obj].CollisionGraceTicks < 1)
                {
                    var force = VectorMath.Scale(Objects[obj].Velocity, (ushort)Objects[obj].RadarRadius << 8);
                    CollisionDelta = VectorMath.Negate(CollisionDelta);
                    ApplyForceToObject(CollisionDelta, force, partner);
                    Explode(obj, obj);
                    Objects[obj].Velocity = FixedVector.Zero;
                    return;
                }
                break;
            case ObjectClass.Ship:
            case ObjectClass.CapitalShip:
                partnerClass = Objects[partner].Class;
                if ((partnerClass == ObjectClass.Asteroid ||
                     (partnerClass == ObjectClass.Mine && Objects[partner].Owner == -1)) &&
                    !Easy2See(partner) &&
                    (CameraViewMode == 0 || obj != ObjectSlots.Player))
                {
                    RemoveObject(partner);
                }
                partnerClass = Objects[partner].Class;
                if ((partnerClass is ObjectClass.Asteroid or ObjectClass.Ship or ObjectClass.CapitalShip) &&
                    Objects[obj].LastCollisionObject != partner)
                {
                    ShipCollisionResponse(obj, partner, partnerClass, relativeVelocity);
                }
                break;
        }
    }

    /// <summary>The bounce of a ship or capital ship off an asteroid, ship or capital ship.</summary>
    private void ShipCollisionResponse(short obj, short partner, ObjectClass partnerClass, FixedVector relativeVelocity)
    {
        Events.PlaySoundEffect(0x1c, obj);
        Objects[obj].LastCollisionObject = unchecked((sbyte)partner);
        Objects[partner].LastCollisionObject = unchecked((sbyte)obj);

        int separationScale = FixedMath.Divide(
            (Objects[obj].CollisionRadius + Objects[partner].CollisionRadius) << 8,
            CollisionDelta.Magnitude());
        separationScale = ScalarMath.MinInt(separationScale, 0x7d000);
        var separation = VectorMath.Scale(CollisionDelta, separationScale);
        Objects[partner].Position = VectorMath.Add(Objects[obj].Position, separation);

        var objectComponent = VectorMath.ComponentInDirection(Objects[obj].Velocity, CollisionDelta);
        var tangent = VectorMath.Subtract(Objects[obj].Velocity, objectComponent);
        var partnerComponent = VectorMath.ComponentInDirection(Objects[partner].Velocity, CollisionDelta);
        var componentDelta = VectorMath.Delta(partnerComponent, objectComponent);
        int collisionSpeed = unchecked((short)((uint)componentDelta.Magnitude() >> 8));
        short damage = unchecked((short)((collisionSpeed * collisionSpeed) >> 1));

        int objectMass = (ushort)Objects[obj].RadarRadius;
        int partnerMass = (ushort)Objects[partner].RadarRadius;
        int totalMass = objectMass + partnerMass;
        // Massless pairs do not occur in the data; the original would divide by zero.
        int responseScale = totalMass == 0 ? 0 : (objectMass - partnerMass) * 256 / totalMass;
        responseScale = ScalarMath.MaxInt(0x40, responseScale);
        responseScale = ScalarMath.MinInt(responseScale, 0x400);
        var impulse = VectorMath.Scale(componentDelta, responseScale);
        impulse = VectorMath.Add(impulse, partnerComponent);
        int forceMagnitude = FixedMath.Multiply(objectMass * 0x600, impulse.Magnitude()) + 0xa00;
        Objects[obj].Velocity = VectorMath.Add(impulse, Objects[obj].Velocity);

        if (Objects[obj].Class == ObjectClass.Ship)
        {
            VectorMath.Normalize(ref tangent);
            tangent = VectorMath.Negate(tangent);
            tangent = VectorMath.Scale(tangent, forceMagnitude);
            RotationalAcceleration(CollisionDelta, tangent, obj);
            relativeVelocity = VectorMath.Negate(relativeVelocity);
            InflictDamage(partner, obj, damage, relativeVelocity);
            relativeVelocity = VectorMath.Negate(relativeVelocity);
        }

        var partnerTangent = VectorMath.Subtract(Objects[partner].Velocity, partnerComponent);
        responseScale = totalMass == 0 ? 0 : (objectMass << 9) / totalMass;
        responseScale = ScalarMath.MaxInt(0x40, responseScale);
        responseScale = ScalarMath.MinInt(responseScale, 0x400);
        impulse = VectorMath.Scale(componentDelta, responseScale);
        impulse = VectorMath.Add(impulse, partnerComponent);
        Objects[partner].Velocity = VectorMath.Add(impulse, Objects[partner].Velocity);
        if (partnerClass == ObjectClass.Ship)
        {
            VectorMath.Normalize(ref partnerTangent);
            partnerTangent = VectorMath.Negate(partnerTangent);
            partnerTangent = VectorMath.Scale(partnerTangent, forceMagnitude);
            CollisionDelta = VectorMath.Negate(CollisionDelta);
            RotationalAcceleration(CollisionDelta, partnerTangent, partner);
            InflictDamage(obj, partner, damage, relativeVelocity);
        }
        if (partnerClass == ObjectClass.CapitalShip)
        {
            Objects[obj].Position = VectorMath.Subtract(Objects[obj].Position, Objects[obj].Velocity);
            Objects[obj].Speed = 0;
            Objects[obj].Velocity = Objects[partner].Velocity;
        }
    }

    /// <summary>Flashes the cockpit palette entry of the side a projectile hit the player from: its
    /// eye-space position as yaw/pitch selects front (1), left (3), right (5), rear (0), top (2) or
    /// bottom (4).</summary>
    /// <remarks>C: the <c>partner == 0</c> block of object_collision's PROJECTILE case.</remarks>
    private void FlashCockpitHitSide(short projectile)
    {
        var impact = default(SphericalVector);
        VectorMath.RectangularToSpherical(Objects[projectile].ViewPosition, ref impact);
        int entry;
        if (System.Math.Abs((int)impact.Pitch) < 45)
        {
            int yaw = System.Math.Abs((int)impact.Yaw);
            if (yaw < 45)
                entry = 1;
            else if (yaw < 136)
                entry = impact.Yaw < 0 ? 3 : 5;
            else
                entry = 0;
        }
        else
        {
            entry = impact.Pitch < 0 ? 2 : 4;
        }
        Events.FlashCockpitPaletteEntry(entry);
    }
}
