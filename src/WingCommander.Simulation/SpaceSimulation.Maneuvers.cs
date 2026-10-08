using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// The 47 dogfight maneuvers of brains.c (the M* handlers) and perform_maneuver. Counters and steps
// count AI ticks (ShipState.Count / ShipState.Sequence). The handlers read the facing/range globals
// that perform_maneuver set with ship_vs_ship(obj, target) unless they query again.
public sealed partial class SpaceSimulation
{
    /// <summary>Points at the target and rolls 360 degrees (the completion test of the original can
    /// never be true: it compares a 0/1 result with 9).</summary>
    /// <remarks>C: Mline_up_drop (0x4060D0, brains.c), maneuver 42; also run without a target (-1, see
    /// <see cref="PositionOf"/>).</remarks>
    public void ManeuverLineUpDrop(short ship, short target)
    {
        if (NoGoal(ship))
        {
            PointShipAtPoint(ship, PositionOf(target));
            Ships[ship].RollGoal = 360;
        }
    }

    /// <summary>Twenty ticks of random yaw, pitch (±30) or roll (±50) at full speed.</summary>
    /// <remarks>C: Mwabble (0x406130, brains.c), maneuver 13.</remarks>
    public void ManeuverWabble(short ship)
    {
        ref var s = ref Ships[ship];
        s.Count++;
        if (s.Count > 20)
        {
            ManeuverComplete(ship);
            return;
        }
        ApproachFullSpeed(ship);
        if (NoGoal(ship))
        {
            switch (Random.BelowOrEqual(2))
            {
                case 0:
                    Ships[ship].YawGoal = unchecked((short)(Random.Signed(6) * 5));
                    break;
                case 1:
                    Ships[ship].PitchGoal = unchecked((short)(Random.Signed(6) * 5));
                    break;
                default:
                    Ships[ship].RollGoal = unchecked((short)(Random.Signed(10) * 5));
                    break;
            }
        }
    }

    /// <summary>Next step of the running maneuver (not for NONE).</summary>
    /// <remarks>C: advance (0x4061E0, brains.c).</remarks>
    public void Advance(short ship)
    {
        if (Ships[ship].Maneuver != ShipManeuver.None)
            Ships[ship].Sequence++;
    }

    /// <summary>
    /// Maneuver 35 (unnamed, "turn and fire"): wait until within 750 (or 10 ticks), veer 45, wait (up
    /// to 5 ticks, then start over) until the target looks away (facing below 75), then point at it at
    /// speed 5 and switch to TAIL_FIRE once lined up; give up beyond 1500 or when the target faces us.
    /// </summary>
    /// <remarks>C: ShipAiState35 (0x406200, brains.c).</remarks>
    public void ManeuverTurnAndFire(short ship, short target)
    {
        _ = target;
        switch (Ships[ship].Sequence)
        {
            case 0:
                if (TargetRange < 750 || ++Ships[ship].Count > 10)
                {
                    Advance(ship);
                    Ships[ship].Count = 0;
                }
                break;
            case 1:
                VeerRandom(ship, 45);
                Advance(ship);
                break;
            case 2:
                if (++Ships[ship].Count > 5)
                {
                    Ships[ship].Sequence = 0;
                    Ships[ship].Count = 0;
                }
                if (TargetFacing < 75)
                    Advance(ship);
                break;
            case 3:
                var toTarget = ToTarget;
                PointShip(ship, 0, toTarget);
                ApproachSpeed(ship, 0x500);
                if (FacingToTarget > 10)
                    ResetManeuver(ship, ShipManeuver.TailFire);
                if (TargetRange > 1500 || TargetFacing > 80)
                    ManeuverComplete(ship);
                break;
        }
    }

    /// <summary>Full speed while the counter runs down.</summary>
    /// <remarks>C: Mfull_ahead (0x406310, brains.c), maneuver 4.</remarks>
    public void ManeuverFullAhead(short ship)
    {
        ApproachFullSpeed(ship);
        short count = Ships[ship].Count;
        Ships[ship].Count = unchecked((short)(count - 1));
        if (count < 1)
            ManeuverComplete(ship);
    }

    /// <summary>Follows a point 900 ahead of the target; once behind it within 1000 the maneuver whose
    /// number is stored in the step counter resumes (SIT_N_SPIN stores 10, itself).</summary>
    /// <remarks>C: Mchill (0x406350, brains.c), maneuver 43.</remarks>
    public void ManeuverChill(short ship, short target)
    {
        var destination = GetFrontSpot(target, 900);
        ChaseLocation(ship, destination, target);
        if (CloseBehind(1000))
            ResetManeuver(ship, (ShipManeuver)Ships[ship].Sequence);
    }

    /// <summary>Drops a mine (when farther than 1500 and one is aboard) and runs at full speed.</summary>
    /// <remarks>C: Mdrop_a_mine (0x4063B0, brains.c), maneuver 22.</remarks>
    public void ManeuverDropAMine(short ship)
    {
        short weapon = -1;
        if (TargetRange > 1500)
            weapon = MineAvailable(ship);
        if (weapon != -1)
            FireWeapon(ship, weapon);
        ApproachFullSpeed(ship);
        ManeuverComplete(ship);
    }

    /// <summary>A tick at cruise speed.</summary>
    /// <remarks>C: Mthink (0x406400, brains.c), maneuver 5.</remarks>
    public void ManeuverThink(short ship)
    {
        ApproachCruiseSpeed(ship);
        if (Ships[ship].Count == 0)
            Ships[ship].Count = 2;
        if (--Ships[ship].Count <= 1)
            ManeuverComplete(ship);
    }

    /// <summary>Two half loops (pitch 180 twice) at cruise speed.</summary>
    /// <remarks>C: Mtight_loop (0x406440, brains.c), maneuver 8.</remarks>
    public void ManeuverTightLoop(short ship)
    {
        ApproachCruiseSpeed(ship);
        switch (Ships[ship].Sequence)
        {
            case 0:
                Ships[ship].PitchGoal = 180;
                Advance(ship);
                return;
            case 1:
                if (NoGoal(ship))
                    Advance(ship);
                ApproachCruiseSpeed(ship);
                break;
            case 2:
                Ships[ship].PitchGoal = 180;
                Advance(ship);
                return;
            case 3:
                if (NoGoal(ship))
                    ManeuverComplete(ship);
                break;
            default:
                ManeuverComplete(ship);
                break;
        }
    }

    /// <summary>Kills the engines for 3 ticks, then the super brake and 3 more ticks (the ship counts as
    /// inactive while hard braking).</summary>
    /// <remarks>C: Mhard_break (0x4064F0, brains.c), maneuver 9.</remarks>
    public void ManeuverHardBrake(short ship)
    {
        bool advanceSequence = true;
        switch (Ships[ship].Sequence)
        {
            case 0:
                SetSpecial(ship, SpecialManeuver.KillEngines);
                break;
            case 1:
                advanceSequence = ++Ships[ship].Count > 3;
                if (advanceSequence)
                    Ships[ship].Count = 0;
                break;
            case 2:
                FireSuperBrake(ship);
                break;
            case 3:
                advanceSequence = ++Ships[ship].Count > 3;
                if (advanceSequence)
                    ManeuverComplete(ship);
                break;
        }
        if (advanceSequence)
            Advance(ship);
    }

    /// <summary>
    /// The 11-step "sit and spin": match the target's speed while aiming just ahead of it, chill until
    /// behind it, match speeds, kill the engines, turn to it, fire for 6 ticks, veer 35 and roll over
    /// (the ROLL_OVER starts at step 1, so it only completes).
    /// </summary>
    /// <remarks>C: Msit_n_spin (0x4065A0, brains.c), maneuver 10. The aim point is the target's forward
    /// vector scaled by <c>range * 2</c> as a raw 24.8 factor, i.e. barely ahead of it (as in the original).</remarks>
    public void ManeuverSitNSpin(short ship, short target)
    {
        bool advanceSequence = true;
        switch (Ships[ship].Sequence)
        {
            case 0:
                if (++Ships[ship].Count < 4)
                {
                    ApproachSpeed(ship, Objects[target].Speed);
                    var destination = VectorMath.Scale(Objects[target].Forward, TargetRange * 2);
                    destination = VectorMath.Add(Objects[target].Position, destination);
                    advanceSequence = false;
                    PointShipAtPoint(ship, destination);
                }
                else
                {
                    Ships[ship].Count = 0;
                }
                break;
            case 1:
                SteadyObject(ship);
                if (!CloseBehind(1000))
                {
                    advanceSequence = false;
                    ResetManeuver(ship, ShipManeuver.Chill);
                    Ships[ship].Sequence = 10;
                }
                break;
            case 2:
                break;
            case 3:
                if (ScalarMath.AbsInt(Objects[ship].Speed - Objects[target].Speed) < 0x200)
                {
                    advanceSequence = false;
                    ApproachSpeed(ship, Objects[target].Speed);
                }
                break;
            case 4:
                SetSpecial(ship, SpecialManeuver.KillEngines);
                break;
            case 5:
                SteadyObject(ship);
                PointShipAtObject(ship, target);
                break;
            case 6:
                advanceSequence = NoGoal(ship);
                break;
            case 7:
                if (FacingToTarget > 85)
                    Fire(ship, target);
                if (++Ships[ship].Count < 6)
                    advanceSequence = false;
                break;
            case 8:
                VeerRandom(ship, 35);
                break;
            case 9:
                advanceSequence = NoGoal(ship);
                break;
            case 10:
                SetSpecial(ship, SpecialManeuver.None);
                ResetManeuver(ship, ShipManeuver.RollOver);
                break;
        }
        if (advanceSequence)
            Advance(ship);
    }

    /// <summary>Veer 90, wait 2 ticks, then (unless the target faces us, above 80) kill the engines and
    /// turn to the target.</summary>
    /// <remarks>C: Mturn_n_spin (0x4067A0, brains.c), maneuver 11.</remarks>
    public void ManeuverTurnNSpin(short ship, short target)
    {
        bool advanceSequence = true;
        switch (Ships[ship].Sequence)
        {
            case 0:
                VeerRandom(ship, 90);
                break;
            case 1:
                ++Ships[ship].Count;
                advanceSequence = Ships[ship].Count > 2;
                break;
            case 2:
                advanceSequence = TargetFacing <= 80;
                if (advanceSequence)
                {
                    SetSpecial(ship, SpecialManeuver.KillEngines);
                    PointShipAtObject(ship, target);
                }
                else
                {
                    ManeuverComplete(ship);
                }
                break;
            case 3:
                if (NoGoal(ship))
                    ManeuverComplete(ship);
                break;
        }
        if (advanceSequence)
            Advance(ship);
    }

    /// <summary>Afterburner for 10 frames, then a 180 degree turn.</summary>
    /// <remarks>C: Mburnout (0x406860, brains.c), maneuver 12.</remarks>
    public void ManeuverBurnout(short ship, short target)
    {
        _ = target;
        switch (Ships[ship].Sequence)
        {
            case 0:
                FireAfterburner(ship, 10);
                Advance(ship);
                break;
            case 1:
                if (Ships[ship].SpecialManeuver == SpecialManeuver.None)
                {
                    Ships[ship].YawGoal = 180;
                    Advance(ship);
                }
                break;
            default:
                if (NoGoal(ship))
                    ManeuverComplete(ship);
                break;
        }
    }

    /// <summary>Afterburner for 10 frames.</summary>
    /// <remarks>C: Mkickit (0x4068D0, brains.c), maneuver 19.</remarks>
    public void ManeuverKickit(short ship)
    {
        switch (Ships[ship].Sequence)
        {
            case 0:
                FireAfterburner(ship, 10);
                Advance(ship);
                break;
            default:
                if (Ships[ship].SpecialManeuver == SpecialManeuver.None)
                    ManeuverComplete(ship);
                break;
        }
    }

    /// <summary>Veer 90 at full speed, then afterburner for 10 frames.</summary>
    /// <remarks>C: Mturn_n_kick (0x406910, brains.c), maneuvers 7 and 20.</remarks>
    public void ManeuverTurnNKick(short ship)
    {
        switch (Ships[ship].Sequence)
        {
            case 0:
                VeerRandom(ship, 90);
                Advance(ship);
                break;
            case 1:
                if (NoGoal(ship))
                {
                    FireAfterburner(ship, 10);
                    Advance(ship);
                }
                else
                {
                    ApproachFullSpeed(ship);
                }
                break;
            case 2:
                if (Ships[ship].SpecialManeuver == SpecialManeuver.None)
                    ManeuverComplete(ship);
                break;
        }
    }

    /// <summary>Rolls -180, 180 or 540 degrees (<c>RandomBelowOrEqual(2) * 360 - 180</c>) at full speed.</summary>
    /// <remarks>C: Mroll_over (0x406990, brains.c), maneuver 14.</remarks>
    public void ManeuverRollOver(short ship)
    {
        if (Ships[ship].Sequence == 0)
        {
            Advance(ship);
            Ships[ship].RollGoal = unchecked((short)(Random.BelowOrEqual(2) * 360 - 180));
        }
        else if (Ships[ship].RollGoal == 0)
        {
            ManeuverComplete(ship);
        }
        else
        {
            ApproachFullSpeed(ship);
        }
    }

    /// <summary>Turns -180, 180 or 540 degrees of yaw at full speed.</summary>
    /// <remarks>C: Mhard_turn (0x4069F0, brains.c), maneuver 15.</remarks>
    public void ManeuverHardTurn(short ship)
    {
        ApproachFullSpeed(ship);
        switch (Ships[ship].Sequence)
        {
            case 0:
                Ships[ship].YawGoal = unchecked((short)(Random.BelowOrEqual(2) * 360 - 180));
                Advance(ship);
                break;
            default:
                if (NoGoal(ship))
                    ManeuverComplete(ship);
                break;
        }
    }

    /// <summary>Yaw ±120 (or 360) with a short afterburner, super brake and yaw ±45 (or 135), wait for
    /// cruise speed and the turn, afterburner again, done at normal speed.</summary>
    /// <remarks>C: Mfish_hook (0x406A50, brains.c), maneuver 16.</remarks>
    public void ManeuverFishHook(short ship, short target)
    {
        _ = target;
        bool advanceSequence = true;
        switch (Ships[ship].Sequence)
        {
            case 0:
                Ships[ship].YawGoal = unchecked((short)(Random.BelowOrEqual(2) * 240 - 120));
                FireAfterburner(ship, 5);
                break;
            case 1:
                advanceSequence = NoGoal(ship);
                ApproachFullSpeed(ship);
                break;
            case 2:
                FireSuperBrake(ship);
                Ships[ship].YawGoal = unchecked((short)(Random.BelowOrEqual(2) * 90 - 45));
                break;
            case 3:
                advanceSequence = TypeDataOf(ship).CruiseVelocity >= RealVelocity(ship);
                break;
            case 4:
                advanceSequence = NoGoal(ship);
                break;
            case 5:
                FireAfterburner(ship, 10);
                break;
            default:
                if (NormalSpeed(ship))
                    ManeuverComplete(ship);
                break;
        }
        if (advanceSequence)
            Advance(ship);
    }

    /// <summary>Chases the target at full speed, 4% of the ticks veering 5 degrees.</summary>
    /// <remarks>C: Mtry2tail (0x406B60, brains.c), maneuver 39.</remarks>
    public void ManeuverTry2Tail(short ship, short target)
    {
        if (!Unactive(target))
        {
            ApproachFullSpeed(ship);
            if (NoGoal(ship))
                PointShipAtObject(ship, target);
            if (Random.BelowOrEqual(100) < 4)
                VeerRandom(ship, 5);
        }
        else
        {
            ManeuverComplete(ship);
        }
    }

    /// <remarks>C: Msplit_left (0x406BD0, brains.c), maneuver 17: yaw 90.</remarks>
    public void ManeuverSplitLeft(short ship) => ManeuverSplit(ship, 90);

    /// <remarks>C: Msplit_right (0x406C20, brains.c), maneuver 23: yaw -90.</remarks>
    public void ManeuverSplitRight(short ship) => ManeuverSplit(ship, -90);

    private void ManeuverSplit(short ship, short yaw)
    {
        switch (Ships[ship].Sequence)
        {
            case 0:
                Ships[ship].YawGoal = yaw;
                Advance(ship);
                break;
            default:
                if (NoGoal(ship))
                    ManeuverComplete(ship);
                break;
        }
    }

    /// <summary>Engines off, then nodding (pitch +15 / -30, each step with a 50% chance) up to ten times.</summary>
    /// <remarks>C: Mgloat (0x406C70, brains.c), maneuver 25.</remarks>
    public void ManeuverGloat(short ship)
    {
        switch (Ships[ship].Sequence)
        {
            case 0:
                SetSpecial(ship, SpecialManeuver.KillEngines);
                Advance(ship);
                break;
            case 1:
                Ships[ship].PitchGoal = 15;
                if (Random.Below(100) < 50)
                    Advance(ship);
                break;
            default:
                Ships[ship].PitchGoal = -30;
                if (Random.Below(100) < 50)
                {
                    if (++Ships[ship].Count < 10)
                        Ships[ship].Sequence = 1;
                    else
                        ManeuverComplete(ship);
                }
                break;
        }
    }

    /// <summary>Points at the target, keeps <c>(r[target] + 6 r[ship]) / 2</c> behind it and fires.</summary>
    /// <remarks>C: Mtail_fire (0x406D20, brains.c), maneuver 26.</remarks>
    public void ManeuverTailFire(short ship, short target)
    {
        if (NoGoal(ship))
            PointShipAtObject(ship, target);
        ChaseSpeed(ship, unchecked((short)((Objects[target].CollisionRadius + Objects[ship].CollisionRadius * 6) >> 1)));
        FireWhenReady(ship, true);
    }

    /// <summary>Close behind the target (within its radius + 2000): tail fire; else full speed toward a
    /// point below it (when it faces us) or behind it.</summary>
    /// <remarks>C: Mzip_past (0x406D80, brains.c), maneuver 40.</remarks>
    public void ManeuverZipPast(short ship, short target)
    {
        if (Unactive(target))
        {
            ManeuverComplete(ship);
            return;
        }
        if (CloseBehind(unchecked((short)(Objects[target].CollisionRadius + 2000))))
        {
            ManeuverTailFire(ship, target);
            return;
        }
        ApproachFullSpeed(ship);
        if (NoGoal(ship))
        {
            if (TargetFacing > 80)
                PointShipBelowObject(ship, target);
            else
                PointShipBehindObject(ship, target);
        }
    }

    /// <summary>Lines up at cruise speed and fires a missile (1/6 per tick when aimed above 85, within
    /// 6000, target seen from the front or back); with one of its missiles already flying it strafes.</summary>
    /// <remarks>C: Mtarget_missile (0x406E10, brains.c), maneuver 28.</remarks>
    public void ManeuverTargetMissile(short ship, short target)
    {
        if (NoGoal(ship))
            PointShipAtObject(ship, target);
        ApproachCruiseSpeed(ship);
        for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Objects[obj].Owner == ship && Objects[obj].Class == ObjectClass.Missile)
            {
                ResetManeuver(ship, ShipManeuver.StrafeEnemy);
                return;
            }
        }
        if (FacingToTarget > 85 && TargetRange < 6000 &&
            (TargetFacing > 80 || TargetFacing < -80) &&
            Random.BelowOrEqual(5) == 0)
        {
            FireMissile(ship);
            ManeuverComplete(ship);
        }
    }

    /// <summary>Full speed at the target; fires a missile once aimed (above 75) within 6000.</summary>
    /// <remarks>C: Mram_missile (0x406EC0, brains.c), maneuver 6.</remarks>
    public void ManeuverRamMissile(short ship, short target)
    {
        ApproachFullSpeed(ship);
        if (NoGoal(ship))
            PointShipAtObject(ship, target);
        if (FacingToTarget > 75 && TargetRange < 6000)
        {
            FireMissile(ship);
            ManeuverComplete(ship);
        }
    }

    /// <summary>Veer 10, afterburner for 10 frames, veering again while still pointing at the target
    /// (facing above 95).</summary>
    /// <remarks>C: Mbuzz_debris (0x406F20, brains.c), maneuver 41.</remarks>
    public void ManeuverBuzzDebris(short ship)
    {
        switch (Ships[ship].Sequence)
        {
            case 0:
                VeerRandom(ship, 10);
                Advance(ship);
                break;
            case 1:
                if (NoGoal(ship))
                {
                    FireAfterburner(ship, 10);
                    Advance(ship);
                }
                else
                {
                    ApproachFullSpeed(ship);
                }
                break;
            default:
                if (Ships[ship].SpecialManeuver == SpecialManeuver.None)
                    ManeuverComplete(ship);
                else if (FacingToTarget > 95)
                    VeerRandom(ship, 10);
                break;
        }
    }

    /// <summary>Cruise speed; re-aims at the target whenever the previous turn is done, and fires.</summary>
    /// <remarks>C: Mstrafe_enemy (0x406FB0, brains.c), maneuver 29.</remarks>
    public void ManeuverStrafeEnemy(short ship, short target)
    {
        ApproachCruiseSpeed(ship);
        bool aimed = Ships[ship].PitchGoal == 0 && Ships[ship].YawGoal == 0;
        if (aimed)
        {
            ShipVsShip(ship, target);
            PointShipAtObject(ship, target);
        }
        FireWhenReady(ship, !aimed);
    }

    /// <summary>Strafes a target that does not face us (below 80), else zips past it.</summary>
    /// <remarks>C: Mbest_strafe (0x407030, brains.c), maneuvers 30, 36 and 45.</remarks>
    public void ManeuverBestStrafe(short ship, short target)
    {
        if (TargetFacing < 0x50)
        {
            ManeuverStrafeEnemy(ship, target);
            return;
        }
        ManeuverZipPast(ship, target);
    }

    /// <summary>Capital ship targets are strafed; others: point at the target, cruise beyond 3000 or stop,
    /// and fire.</summary>
    /// <remarks>C: Msit_n_fire (0x407060, brains.c), maneuver 18.</remarks>
    public void ManeuverSitNFire(short ship, short target)
    {
        if (Objects[target].Class == ObjectClass.CapitalShip)
        {
            ManeuverBestStrafe(ship, target);
            return;
        }
        if (NoGoal(ship))
            PointShipAtObject(ship, target);
        if (TargetRange > 3000)
            ApproachCruiseSpeed(ship);
        else
            ApproachZeroSpeed(ship);
        FireWhenReady(ship, true);
    }

    /// <summary>Rolls 45 degrees while the guns reload, else strafes.</summary>
    /// <remarks>C: Mstrafe_n_roll (0x4070D0, brains.c), maneuver 31.</remarks>
    public void ManeuverStrafeNRoll(short ship, short target)
    {
        if (Objects[ship].Counter > 0)
        {
            Ships[ship].RollGoal = 0x2d;
            return;
        }
        ManeuverStrafeEnemy(ship, target);
    }

    /// <summary>
    /// A missile is on our tail: when it is behind us, FISH_HOOK; to the side (facing below 80), BURNOUT;
    /// in front, point at it and fire at the target within 8000. The original measures against
    /// <c>nTargetShip</c>, the result of whatever scan ran last (missile_on_tail does not set it).
    /// </summary>
    /// <remarks>C: Mkill_missile (0x407100, brains.c), maneuver 32. A <c>nTargetShip</c> of -1 is measured
    /// like the original against object -1 (<see cref="PositionOf"/>).</remarks>
    public void ManeuverKillMissile(short ship, short target)
    {
        if (!MissileOnTail(ship))
        {
            ManeuverComplete(ship);
            return;
        }
        short missile = TargetShip;
        ShipVsShip(ship, missile);
        if (FacingToTarget < 0)
        {
            ResetManeuver(ship, ShipManeuver.FishHook);
            ManeuverFishHook(ship, target);
            return;
        }
        if (FacingToTarget < 80)
        {
            ResetManeuver(ship, ShipManeuver.Burnout);
            ManeuverBurnout(ship, target);
            return;
        }
        PointShipAtObject(ship, missile);
        if (TargetRange < 8000)
            Fire(ship, Ships[ship].Target);
    }

    /// <summary>Full speed at the target.</summary>
    /// <remarks>C: Msuicide_run (0x4071B0, brains.c), maneuver 33.</remarks>
    public void ManeuverSuicideRun(short ship, short target)
    {
        ApproachFullSpeed(ship);
        if (NoGoal(ship))
            PointShipAtObject(ship, target);
    }

    /// <summary>Opens the distance to 2000: afterburner when nearer than 700, steering away while the
    /// target is ahead.</summary>
    /// <remarks>C: Mget_distance (0x4071E0, brains.c), maneuver 37.</remarks>
    public void ManeuverGetDistance(short ship, short target)
    {
        if (TargetRange > 2000)
        {
            ManeuverComplete(ship);
            return;
        }
        if (TargetRange < 700 && NormalSpeed(ship))
            FireAfterburner(ship, 10);
        else
            ApproachFullSpeed(ship);
        if (FacingToTarget > 0 && NoGoal(ship))
        {
            short amount = ScalarMath.MinShort(20, FacingToTarget);
            SteerAwayFromObject(ship, target, amount);
        }
    }

    /// <summary>Zig-zag at full speed: yaw ∓35 with the given pitch, wait for the turn and 4 ticks, twelve
    /// steps in all.</summary>
    /// <remarks>C: general_zig (0x407270, brains.c); <paramref name="target"/> is unused.</remarks>
    public void GeneralZig(short ship, short target, short pitch)
    {
        _ = target;
        bool complete = true;
        ApproachFullSpeed(ship);
        switch (Ships[ship].Sequence % 6)
        {
            case 0:
                Ships[ship].YawGoal = -35;
                Ships[ship].PitchGoal = pitch;
                break;
            case 1:
            case 4:
                complete = NoGoal(ship);
                Ships[ship].Count = 0;
                break;
            case 2:
            case 5:
                complete = ++Ships[ship].Count >= 4;
                break;
            case 3:
                pitch = unchecked((short)-pitch);
                Ships[ship].YawGoal = 35;
                Ships[ship].PitchGoal = pitch;
                break;
        }
        if (Ships[ship].Sequence >= 12)
            ManeuverComplete(ship);
        if (complete)
            Advance(ship);
    }

    /// <remarks>C: Mzig_zag (0x407350, brains.c), maneuver 24.</remarks>
    public void ManeuverZigZag(short ship, short target) => GeneralZig(ship, target, 0);

    /// <remarks>C: Mzig_zag_pitch (0x407370, brains.c), maneuver 34 (SAFE_BRAKE).</remarks>
    public void ManeuverZigZagPitch(short ship, short target) => GeneralZig(ship, target, 0x23);

    /// <summary>Corkscrew at full speed: yaw -20, roll 20, yaw 20, roll 20 every 4 ticks, nine steps.</summary>
    /// <remarks>C: Mcorkscrew (0x407390, brains.c), maneuver 38 (INTERCEPT).</remarks>
    public void ManeuverCorkscrew(short ship)
    {
        ApproachFullSpeed(ship);
        if (NoGoal(ship) && --Ships[ship].Count <= 0)
        {
            switch (Ships[ship].Sequence % 4)
            {
                case 0:
                    Ships[ship].YawGoal = -20;
                    break;
                case 1:
                case 3:
                    Ships[ship].RollGoal = 20;
                    break;
                case 2:
                    Ships[ship].YawGoal = 20;
                    break;
            }
            Ships[ship].Count = 4;
            Advance(ship);
        }
        if (Ships[ship].Sequence > 8)
            ManeuverComplete(ship);
    }

    /// <summary>
    /// Breaks off when too close: first steer away (40 degrees when pointing at the target, else 10);
    /// done once farther than three target radii (with a random veer of 8); meanwhile keep steering away
    /// or veer, with the afterburner when inside the "too close" range or 10% of the ticks.
    /// </summary>
    /// <remarks>C: Mveer_away (0x407450, brains.c), maneuver 2; also run for a target that became
    /// inactive or without a target (-1, see <see cref="PositionOf"/>).</remarks>
    public void ManeuverVeerAway(short ship, short target)
    {
        if (Ships[ship].Sequence == 0)
        {
            SteerAwayFromObject(ship, target, FacingToTarget > 80 ? (short)40 : (short)10);
            Advance(ship);
            return;
        }
        if (CollisionRadiusOf(target) * 3 < TargetRange)
        {
            VeerRandom(ship, 8);
            ManeuverComplete(ship);
            return;
        }
        if (NoGoal(ship))
        {
            if (FacingToTarget > 80)
                SteerAwayFromObject(ship, target, 40);
            else if (FacingToTarget < -65 || Random.BelowOrEqual(100) < 4)
                VeerRandom(ship, 16);
        }
        if ((TooCloseRange >= TargetRange || Random.BelowOrEqual(100) < 10) && NormalSpeed(ship))
        {
            FireAfterburner(ship, 10);
            return;
        }
        ApproachFullSpeed(ship);
    }

    /// <summary>Calms the pilot (stress 0) and ends the maneuver.</summary>
    /// <remarks>C: ShipAiState44 (0x407560, brains.c), maneuver 44.</remarks>
    public void ManeuverResetStress(short ship)
    {
        Ships[ship].Stress = 0;
        ManeuverComplete(ship);
    }

    /// <remarks>C: Mtarget_laser (0x407580, brains.c), maneuver 27: best strafe.</remarks>
    public void ManeuverTargetLaser(short ship, short target) => ManeuverBestStrafe(ship, target);

    /// <remarks>C: Mrout_me (0x4075A0, brains.c), maneuver 21 (OUTA_HERE).</remarks>
    public void ManeuverRoutMe(short ship) => Try2Rout(ship);

    /// <summary>
    /// Runs the ship's maneuver against its target for one AI tick. The "too close" range
    /// (<see cref="TooCloseRange"/>: half of target radius + 4 (target facing away) or 6 own radii) is
    /// measured first; an inactive target only lets VEER_AWAY, GLOAT and LINE_UP_DROP go on. Afterwards a
    /// ship inside the too-close range veers away, and an unchanged maneuver ends with its re-roll chance
    /// (3% SIT_N_FIRE and 36, 5% BUZZ_DEBRIS).
    /// </summary>
    /// <remarks>C: perform_maneuver (0x4075D0, brains.c) with the dispatch table apShipAiManeuverHandlers
    /// (globals.c 0x004656a8), dispatched by number: the names of 30..38 in the maneuver enum do not match
    /// the handlers (see <see cref="ShipManeuver"/>). Without a target (-1) the Kilrathi Saga build measures
    /// against object -1 (<see cref="PositionOf"/>) and, when nothing changed, draws the re-roll number; the
    /// port does the same (the SDL port returns at once instead). The re-roll chance of NONE is the zero
    /// byte before the table.</remarks>
    public void PerformManeuver(short obj)
    {
        short target = Ships[obj].Target;
        var previous = Ships[obj].Maneuver;
        CurrentManeuverReroll = previous < ShipManeuver.WarpingIn || previous > ShipManeuver.Unknown46
            ? (byte)0
            : AiTables.ManeuverRerollChance[(int)previous];
        ShipVsShip(obj, target);
        short range = TargetRange;
        int maneuverWeight = TargetFacing < 0
            ? CollisionRadiusOf(target) + Objects[obj].CollisionRadius * 4
            : CollisionRadiusOf(target) + Objects[obj].CollisionRadius * 6;
        TooCloseRange = unchecked((short)(ushort)(maneuverWeight >> 1));

        if (Unactive(target))
        {
            switch (Ships[obj].Maneuver)
            {
                case ShipManeuver.VeerAway:
                    ManeuverVeerAway(obj, target);
                    break;
                case ShipManeuver.Gloat:
                    ManeuverGloat(obj);
                    break;
                case ShipManeuver.LineUpDrop:
                    ManeuverLineUpDrop(obj, target);
                    break;
                default:
                    ManeuverComplete(obj);
                    break;
            }
        }
        else if (Ships[obj].Maneuver >= ShipManeuver.WarpingIn && Ships[obj].Maneuver <= ShipManeuver.Unknown46)
        {
            DispatchManeuver(obj, target);
        }
        else
        {
            ManeuverComplete(obj);
        }

        if (range < TooCloseRange)
            Try2ResetManeuver(obj, ShipManeuver.VeerAway);
        else if (Ships[obj].Maneuver == previous && Random.BelowOrEqual(100) < CurrentManeuverReroll)
            ManeuverComplete(obj);
    }

    /// <summary>The handler table <c>apShipAiManeuverHandlers[47]</c>, by number.</summary>
    private void DispatchManeuver(short obj, short target)
    {
        switch ((int)Ships[obj].Maneuver)
        {
            case 0:
            case 1:
                break; // Mnone
            case 2:
                ManeuverVeerAway(obj, target);
                break;
            case 3:
            case 46:
                ManeuverComplete(obj); // Mreset
                break;
            case 4:
                ManeuverFullAhead(obj);
                break;
            case 5:
                ManeuverThink(obj);
                break;
            case 6:
                ManeuverRamMissile(obj, target);
                break;
            case 7:
            case 20:
                ManeuverTurnNKick(obj);
                break;
            case 8:
                ManeuverTightLoop(obj);
                break;
            case 9:
                ManeuverHardBrake(obj);
                break;
            case 10:
                ManeuverSitNSpin(obj, target);
                break;
            case 11:
                ManeuverTurnNSpin(obj, target);
                break;
            case 12:
                ManeuverBurnout(obj, target);
                break;
            case 13:
                ManeuverWabble(obj);
                break;
            case 14:
                ManeuverRollOver(obj);
                break;
            case 15:
                ManeuverHardTurn(obj);
                break;
            case 16:
                ManeuverFishHook(obj, target);
                break;
            case 17:
                ManeuverSplitLeft(obj);
                break;
            case 18:
                ManeuverSitNFire(obj, target);
                break;
            case 19:
                ManeuverKickit(obj);
                break;
            case 21:
                ManeuverRoutMe(obj);
                break;
            case 22:
                ManeuverDropAMine(obj);
                break;
            case 23:
                ManeuverSplitRight(obj);
                break;
            case 24:
                ManeuverZigZag(obj, target);
                break;
            case 25:
                ManeuverGloat(obj);
                break;
            case 26:
                ManeuverTailFire(obj, target);
                break;
            case 27:
                ManeuverTargetLaser(obj, target);
                break;
            case 28:
                ManeuverTargetMissile(obj, target);
                break;
            case 29:
                ManeuverStrafeEnemy(obj, target);
                break;
            case 30:
            case 36:
            case 45:
                ManeuverBestStrafe(obj, target);
                break;
            case 31:
                ManeuverStrafeNRoll(obj, target);
                break;
            case 32:
                ManeuverKillMissile(obj, target);
                break;
            case 33:
                ManeuverSuicideRun(obj, target);
                break;
            case 34:
                ManeuverZigZagPitch(obj, target);
                break;
            case 35:
                ManeuverTurnAndFire(obj, target);
                break;
            case 37:
                ManeuverGetDistance(obj, target);
                break;
            case 38:
                ManeuverCorkscrew(obj);
                break;
            case 39:
                ManeuverTry2Tail(obj, target);
                break;
            case 40:
                ManeuverZipPast(obj, target);
                break;
            case 41:
                ManeuverBuzzDebris(obj);
                break;
            case 42:
                ManeuverLineUpDrop(obj, target);
                break;
            case 43:
                ManeuverChill(obj, target);
                break;
            case 44:
                ManeuverResetStress(obj);
                break;
        }
    }
}
