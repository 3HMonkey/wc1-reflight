using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Rotation goals, speed control and ship-state helpers (disk.c, geom.c, logic.c).
public sealed partial class SpaceSimulation
{
    /// <summary>
    /// Moves a rotation rate toward its goal by at most <c>max(1, |rot - goal| * rate / totalError)</c>
    /// degrees, then subtracts the applied rotation from the goal so goals count down to zero.
    /// </summary>
    /// <remarks>C: match_rotation_goal (0x41E400, disk.c).</remarks>
    public static void MatchRotationGoal(ref short rotation, ref short goal, short totalError, short rate)
    {
        if (totalError != 0)
        {
            if (goal > 180)
                goal = unchecked((short)(goal - 360));
            if (goal < -180)
                goal = unchecked((short)(goal + 360));
            short step = ScalarMath.MaxShort(1, unchecked((short)(System.Math.Abs(rotation - goal) * rate / totalError)));
            if (goal != rotation || step < System.Math.Abs((int)rotation))
            {
                short negativeStep = unchecked((short)-step);
                if (goal < 1)
                {
                    rotation = unchecked((short)(rotation + ScalarMath.MinShort(
                        ScalarMath.MaxShort(unchecked((short)(ScalarMath.MaxShort(goal, negativeStep) - rotation)), negativeStep),
                        step)));
                }
                else
                {
                    rotation = unchecked((short)(rotation + ScalarMath.MaxShort(
                        ScalarMath.MinShort(unchecked((short)(ScalarMath.MinShort(goal, step) - rotation)), step),
                        negativeStep)));
                }
            }
        }
        if (goal != 0)
        {
            goal = goal > 0
                ? ScalarMath.MaxShort(unchecked((short)(goal - rotation)), 0)
                : ScalarMath.MinShort(unchecked((short)(goal - rotation)), 0);
        }
    }

    /// <summary>
    /// Steers ships and missiles toward their yaw/pitch/roll goals. The type's <c>yawRate</c>
    /// drives pitch and <c>pitchRate</c> drives yaw (swapped field names). A tumbling ship
    /// (<see cref="SpecialManeuver.BlowingUp"/>) only recovers through a collision alert or a
    /// skill check once its counter expired.
    /// </summary>
    /// <remarks>C: rotate_object_to_goal (0x41E520, disk.c).</remarks>
    public void RotateObjectToGoal(short obj)
    {
        var typeData = TypeDataOf(obj);
        ref var o = ref Objects[obj];
        ref var ship = ref Ships[obj];
        if (ship.SpecialManeuver == SpecialManeuver.BlowingUp)
        {
            if (AlertFlag(obj, 1))
            {
                SetSpecial(obj, SpecialManeuver.None);
            }
            else
            {
                if (o.Counter == -1 && SkillCheck(obj, 7))
                    ship.SpecialManeuver = SpecialManeuver.None;
                return;
            }
        }
        short totalError = unchecked((short)(
            System.Math.Abs(o.YawRotation - ship.YawGoal) +
            System.Math.Abs(o.PitchRotation - ship.PitchGoal) +
            System.Math.Abs(o.RollRotation - ship.RollGoal)));
        MatchRotationGoal(ref o.PitchRotation, ref ship.PitchGoal, totalError, typeData.YawRate);
        MatchRotationGoal(ref o.YawRotation, ref ship.YawGoal, totalError, typeData.PitchRate);
        MatchRotationGoal(ref o.RollRotation, ref ship.RollGoal, totalError, typeData.RollRate);
    }

    /// <summary>Adds <paramref name="delta"/> (24.8) to the commanded speed, clamped to [0, max &lt;&lt; 8].</summary>
    /// <remarks>C: celerate (0x41E710, disk.c).</remarks>
    public void Celerate(short ship, int delta)
    {
        int maximumSpeed = Ships[ship].MaximumSpeed << 8;
        ref int speed = ref Objects[ship].Speed;
        speed = unchecked(speed + delta);
        if (speed > maximumSpeed)
            speed = maximumSpeed;
        if (speed < 0)
            speed = 0;
    }

    /// <summary>Moves the commanded speed toward <paramref name="targetSpeed"/> by at most the
    /// acceleration rate (doubled under a collision alert).</summary>
    /// <remarks>C: approach_speed (0x41E750, disk.c).</remarks>
    public void ApproachSpeed(short ship, int targetSpeed)
    {
        int acceleration = GetShipAccelerationRate(ship);
        int delta = unchecked(targetSpeed - Objects[ship].Speed);
        if (AlertFlag(ship, 1))
            acceleration = unchecked(acceleration + acceleration);
        if ((delta < 0 ? unchecked(-delta) : delta) > acceleration)
            delta = FixedMath.Multiply(FixedMath.Sign(delta), acceleration);
        Celerate(ship, delta);
    }

    /// <remarks>C: approach_zero_speed (0x422DD0, logic.c).</remarks>
    public void ApproachZeroSpeed(short ship) => ApproachSpeed(ship, 0);

    /// <summary>Approach 5 units/frame.</summary>
    /// <remarks>C: approach_min_speed (0x422DF0, logic.c).</remarks>
    public void ApproachMinSpeed(short obj) => ApproachSpeed(obj, 0x500);

    /// <summary>Approach half the cruise speed (<c>(cruise &amp; ~1) &lt;&lt; 7</c>).</summary>
    /// <remarks>C: approach_half_speed (0x422E10, logic.c).</remarks>
    public void ApproachHalfSpeed(short obj)
    {
        short speed = TypeDataOf(obj).CruiseVelocity;
        ApproachSpeed(obj, unchecked((short)(speed & 0xfffe)) << 7);
    }

    /// <remarks>C: approach_cruise_speed (0x422E50, logic.c).</remarks>
    public void ApproachCruiseSpeed(short ship) => ApproachSpeed(ship, TypeDataOf(ship).CruiseVelocity << 8);

    /// <remarks>C: approach_full_speed (0x422E80, logic.c).</remarks>
    public void ApproachFullSpeed(short ship) => ApproachSpeed(ship, Ships[ship].MaximumSpeed << 8);

    /// <remarks>C: approach_ship_speed (0x422EA0, logic.c).</remarks>
    public void ApproachShipSpeed(short obj, short other) => ApproachSpeed(obj, Objects[other].Speed);

    /// <summary>Zeroes the three rotation goals.</summary>
    /// <remarks>C: steady_object (0x41E7C0, disk.c).</remarks>
    public void SteadyObject(short ship)
    {
        ref var s = ref Ships[ship];
        s.YawGoal = 0;
        s.PitchGoal = 0;
        s.RollGoal = 0;
    }

    /// <remarks>C: real_velocity (0x41E7F0, disk.c).</remarks>
    public short RealVelocity(short obj) => Objects[obj].RealVelocity();

    /// <remarks>C: fix_velocity (0x41E820, disk.c).</remarks>
    public void FixVelocity(short obj) => Objects[obj].FixVelocity();

    /// <summary>Type maximum velocity, +1/3 for Kilrathi aces (rating &gt; 8).</summary>
    /// <remarks>C: get_ship_max_velocity (0x4181C0, geom.c).</remarks>
    public short GetShipMaxVelocity(short obj)
    {
        short velocity = TypeDataOf(obj).MaximumVelocity;
        if (obj < ObjectSlots.ShipSlotCount && Ships[obj].Rating > 8)
            return unchecked((short)(velocity + velocity / 3));
        return velocity;
    }

    /// <summary>Max speed 5 without fuel, else scaled by the ion drive damage; re-clamps the speed when it changed.</summary>
    /// <remarks>C: recalc_max_velocity (0x418210, geom.c).</remarks>
    public void RecalcMaxVelocity(short ship)
    {
        ref var s = ref Ships[ship];
        short oldVelocity = s.MaximumSpeed;
        if (s.Fuel <= 0)
        {
            s.MaximumSpeed = 5;
        }
        else
        {
            short maximumVelocity = GetShipMaxVelocity(ship);
            s.MaximumSpeed = unchecked((short)((maximumVelocity * (4 - s.IonDriveDamage)) >> 2));
        }
        if (s.MaximumSpeed != oldVelocity)
            Celerate(ship, 0);
    }

    /// <summary>Subtracts fuel. The original's exhaustion check compares the array address with 0 and
    /// never fires; the 5-unit speed cap only applies after the next <see cref="RecalcMaxVelocity"/>.</summary>
    /// <remarks>C: drain_fuel (0x418280, geom.c).</remarks>
    public void DrainFuel(short ship, short amount) => Ships[ship].Fuel = unchecked(Ships[ship].Fuel - amount);

    /// <summary>Adds ion drive damage clamped to [0, maximum] and recomputes the max speed.</summary>
    /// <remarks>C: damage_ion_drive (0x4182B0, geom.c).</remarks>
    public void DamageIonDrive(short ship, short amount, short maximum)
    {
        int damage = Ships[ship].IonDriveDamage + amount;
        if (damage >= maximum)
            damage = maximum;
        if (damage <= 0)
            damage = 0;
        Ships[ship].IonDriveDamage = unchecked((sbyte)damage);
        RecalcMaxVelocity(ship);
    }

    /// <summary>Type acceleration, +1/3 for Kilrathi aces.</summary>
    /// <remarks>C: GetShipAccelerationRate (0x4182F0, geom.c).</remarks>
    public int GetShipAccelerationRate(short ship)
    {
        int acceleration = TypeDataOf(ship).Acceleration;
        if (ship < ObjectSlots.ShipSlotCount && Ships[ship].Rating > (int)Rating.AceIceman)
            return acceleration + acceleration / 3;
        return acceleration;
    }

    /// <summary>Sets the special maneuver unless a higher-priority one (≥ LOST_CONTROL) is running;
    /// tumbling is cancelled while a collision alert is active.</summary>
    /// <remarks>C: set_special (0x422D90, logic.c).</remarks>
    public void SetSpecial(short ship, SpecialManeuver special)
    {
        ref var s = ref Ships[ship];
        var current = s.SpecialManeuver;
        if (current < SpecialManeuver.LostControl || special > current)
            s.SpecialManeuver = special;
        if (s.SpecialManeuver == SpecialManeuver.BlowingUp && AlertFlag(ship, 1))
            s.SpecialManeuver = SpecialManeuver.None;
    }

    /// <remarks>C: alert_flag (0x422110, logic.c).</remarks>
    public bool AlertFlag(short ship, uint bits) => (Ships[ship].AlertFlags & bits) != 0;

    /// <remarks>C: set_alert (0x422140, logic.c).</remarks>
    public void SetAlert(short ship, uint bits) => Ships[ship].AlertFlags |= bits;

    /// <remarks>C: clear_alert (0x422160, logic.c).</remarks>
    public void ClearAlert(short ship)
    {
        ref var s = ref Ships[ship];
        s.CollisionCountdown = 0;
        s.AlertFlags = 0;
        s.CollisionAlertTarget = 0xff;
    }

    /// <summary>Skill 2..7: generic max(2, level), player 5, wingmen 4..7, aces 4..7.</summary>
    /// <remarks>C: skill_rating (0x423670, logic.c).</remarks>
    public short SkillRating(short obj)
    {
        int rating = Ships[obj].PilotLevel;
        if (rating <= 4)
            return ScalarMath.MaxShort(2, unchecked((short)rating));
        if (rating == 13)
            return 5;
        if (rating < 14)
            return unchecked((short)(((rating - 5) >> 1) + 4));
        return unchecked((short)(rating - 10));
    }

    /// <summary><c>skill_rating &gt; RandomBelowOrEqual(min(8, difficulty))</c> (one rand() call).</summary>
    /// <remarks>C: skill_check (0x4236B0, logic.c).</remarks>
    public bool SkillCheck(short obj, short difficulty)
    {
        short roll = Random.BelowOrEqual(ScalarMath.MinShort(8, difficulty));
        return SkillRating(obj) > roll;
    }

    /// <summary>Not afterburning and actual speed within the maximum.</summary>
    /// <remarks>C: normal_speed (0x422220, logic.c).</remarks>
    public bool NormalSpeed(short obj) =>
        Ships[obj].SpecialManeuver != SpecialManeuver.Afterburner && RealVelocity(obj) <= Ships[obj].MaximumSpeed;

    /// <summary>Clamps the yaw and pitch goals to ±<paramref name="amount"/>.</summary>
    /// <remarks>C: trim_goals (0x4225C0, logic.c).</remarks>
    public void TrimGoals(short obj, short amount)
    {
        ref var s = ref Ships[obj];
        if (amount < s.YawGoal)
            s.YawGoal = amount;
        else if (s.YawGoal < -amount)
            s.YawGoal = unchecked((short)-amount);
        if (amount < s.PitchGoal)
            s.PitchGoal = amount;
        else if (s.PitchGoal < -amount)
            s.PitchGoal = unchecked((short)-amount);
    }

    /// <summary>All three rotation goals are zero.</summary>
    /// <remarks>C: no_goal (0x422830, logic.c).</remarks>
    public bool NoGoal(short ship)
    {
        ref var s = ref Ships[ship];
        return s.YawGoal == 0 && s.PitchGoal == 0 && s.RollGoal == 0;
    }

    /// <summary>Point <paramref name="distance"/> units ahead of the object.</summary>
    /// <remarks>C: get_front_spot (0x422EC0, logic.c).</remarks>
    public FixedVector GetFrontSpot(short obj, ushort distance) =>
        VectorMath.Add(Objects[obj].Position, VectorMath.Scale(Objects[obj].Forward, distance << 8));

    /// <summary>Point <paramref name="distance"/> units behind the object.</summary>
    /// <remarks>C: get_rear_spot (0x422F10, logic.c).</remarks>
    public FixedVector GetRearSpot(short obj, ushort distance) =>
        VectorMath.Add(Objects[obj].Position, VectorMath.Scale(Objects[obj].Forward, -(distance << 8)));
}
