using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// The player's target selection and missile lock (cockpt.c start_lock .. check_target).
public sealed partial class SpaceSimulation
{
    /// <summary>Starts the lock countdown with a random marker start angle.</summary>
    /// <remarks>C: start_lock (0x415FC0, cockpt.c).</remarks>
    public void StartLock(short countdown)
    {
        TargetLockReadoutDirty = false;
        TargetLockCountdown = countdown;
        TargetLockMarkerAngle = Random.BelowOrEqual(0x167);
    }

    /// <summary>Starts the lock when none is running; true when it started.</summary>
    /// <remarks>C: starting_lock (0x415FF0, cockpt.c).</remarks>
    public bool StartingLock(short countdown)
    {
        if (TargetLockCountdown != -1)
            return false;
        StartLock(countdown);
        return true;
    }

    /// <summary>Drops the missile lock.</summary>
    /// <remarks>C: lock_off (0x416010, cockpt.c).</remarks>
    public void LockOff()
    {
        if (TargetLockCountdown > -1)
            TargetLockReadoutDirty = true;
        Events.RemoveMissileLockedMessage();
        TargetLockCountdown = -1;
    }

    /// <summary>A damaged target tracker may lose the lock and block it for 10..40 frames.</summary>
    /// <remarks>C: CheckTargetLockMalfunction (0x416040, cockpt.c).</remarks>
    public bool CheckTargetLockMalfunction()
    {
        if (!Malf(5))
            return false;
        short countdown = -10;
        LockOff();
        countdown = unchecked((short)(countdown - Random.BelowOrEqual(30)));
        TargetLockCountdown = countdown;
        Events.PlaySoundEffect(7, -1);
        return true;
    }

    /// <summary>One step of the lock countdown (skipped on a tracker malfunction roll); at 0 the lock
    /// is acquired ("MISSILE LOCKED", which the original shows even when the final malfunction check
    /// dropped the lock). Returns true while a countdown ran.</summary>
    /// <remarks>C: decrement_lock_time (0x416090, cockpt.c).</remarks>
    public bool DecrementLockTime()
    {
        if (TargetLockCountdown <= 0)
            return false;
        if (!Malf(5))
        {
            TargetLockCountdown--;
            TargetLockAcquired = TargetLockCountdown == 0;
            if (TargetLockAcquired)
            {
                if (!CheckTargetLockMalfunction())
                    Events.PlaySoundEffect(0x16, -1);
                Events.ShowMissileLockedMessage();
                return true;
            }
            Events.PlaySoundEffect(0x15, -1);
        }
        return true;
    }

    /// <summary>
    /// The missile lock on <paramref name="target"/>, run once per prepared cockpit view: an enemy
    /// on screen within 60 pixels of the centre, a working tracker, a heat seeker (target must fly
    /// away, 18 frames) or image recognition missile (32 frames) selected.
    /// </summary>
    /// <remarks>C: target_locking (0x416120, cockpt.c), with the SDL port's guard for no selected
    /// release weapon.</remarks>
    public void TargetLocking(sbyte target)
    {
        if (target != -1 && Ships[target].Side != Ships[ObjectSlots.Player].Side && PlayerComponentDamage[5] < 4)
        {
            short x = Objects[target].ScreenX;
            if (x == -0x7fff)
                return;
            short y = Objects[target].ScreenY;
            if (TargetLockCountdown < -1)
            {
                TargetLockCountdown++;
                return;
            }
            if (x * x + y * y > 0xe10)
            {
                LockOff();
                return;
            }
            if (SelectedReleaseWeaponIndex == -1)
            {
                LockOff();
                return;
            }
            var weaponType = Ships[ObjectSlots.Player].Weapons.GetWeaponType(SelectedReleaseWeaponIndex);
            if (weaponType == ObjectType.HeatSeekingMissile)
            {
                GetFacingRangeFromObject(0, target);
                if (TargetFacing > -0x41)
                {
                    LockOff();
                    return;
                }
                if (!StartingLock(0x12))
                    DecrementLockTime();
                return;
            }
            if (weaponType != ObjectType.ImageRecognitionMissile)
            {
                LockOff();
                return;
            }
            if (StartingLock(0x20))
                return;
            DecrementLockTime();
            return;
        }
        LockOff();
    }

    /// <summary>Visible ships 1..9 (not dying) closer than 12000 into the viable target list, nearest
    /// first; <paramref name="hasEnemy"/> tells whether one of them is hostile.</summary>
    /// <remarks>C: build_your_target_list (0x416E90, cockpt.c).</remarks>
    public void BuildYourTargetList(out bool hasEnemy)
    {
        hasEnemy = false;
        ViableTargetCount = 0;
        for (sbyte obj = ObjectSlots.FirstShip; obj <= ObjectSlots.LastShip; obj++)
        {
            ref readonly var o = ref Objects[obj];
            if (o.Class >= ObjectClass.Ship &&
                Ships[obj].SpecialManeuver != SpecialManeuver.Unknown9 &&
                o.ScreenX != ObjectSlots.NotVisible &&
                (ushort)o.Distance < 12000)
            {
                sbyte index = ViableTargetCount;
                ViableTargetDistance[index] = o.Distance;
                ViableTarget[index] = obj;
                ViableTargetCount++;
                if (Ships[obj].Side != Ships[ObjectSlots.Player].Side)
                    hasEnemy = true;
            }
        }
        if (ViableTargetCount > 1)
            SortViableTargetList();
    }

    /// <summary>The target key in the target VDU: steps to the next visible ship, skipping friends
    /// while an enemy is visible; a new target cancels the lock.</summary>
    /// <remarks>C: cycle_onscreen_targets (0x416F30, cockpt.c).</remarks>
    public void CycleOnscreenTargets()
    {
        sbyte previousTarget = Ships[ObjectSlots.Player].Target;
        BuildYourTargetList(out bool hasEnemy);
        if (ViableTargetCount == 0)
        {
            Ships[ObjectSlots.Player].Target = -1;
        }
        else
        {
            sbyte index = 0;
            while (index < ViableTargetCount && ViableTarget[index] != Ships[ObjectSlots.Player].Target)
                index++;
            do
            {
                index = (sbyte)((index + 1) % ViableTargetCount);
                Ships[ObjectSlots.Player].Target = ViableTarget[index];
                if (!hasEnemy)
                    break;
            }
            while (Ships[Ships[ObjectSlots.Player].Target].Side == Ships[ObjectSlots.Player].Side);
        }
        if (Ships[ObjectSlots.Player].Target != previousTarget)
            TargetLockCountdown = -1;
    }

    /// <summary>
    /// Automatic targeting, every frame: forgets dying targets; a damaged tracker may drop the target
    /// lock mode (every 8th view frame); keeps a locked or visible hostile target; otherwise picks the
    /// nearest visible enemy (else the nearest friend, or keeps a visible/locked friend when no enemy
    /// is visible). A changed target cancels the missile lock.
    /// </summary>
    /// <remarks>C: check_target (0x416FD0, cockpt.c).</remarks>
    public void CheckTarget()
    {
        bool selectNewTarget = true;
        ref var player = ref Ships[ObjectSlots.Player];
        short oldTarget = player.Target;
        if (oldTarget != -1 && Ships[oldTarget].SpecialManeuver == SpecialManeuver.Unknown9)
        {
            player.Target = -1;
            oldTarget = -1;
        }
        if (TargetLockMode != 0 && (short)(RenderedSpaceFrame % 8) == 0 && Malf(5))
        {
            TargetLockMode = 0;
            Events.PlaySoundEffect(0x1f, -1);
        }
        if (oldTarget != -1 &&
            (TargetLockMode != 0 ||
             (Objects[oldTarget].ScreenX != ObjectSlots.NotVisible &&
              (TargetLockMode != 0 || Ships[oldTarget].Side != player.Side))))
        {
            return;
        }
        if (oldTarget == -1)
            TargetLockMode = 0;

        BuildYourTargetList(out bool hasEnemy);
        if (ViableTargetCount == 0)
        {
            player.Target = TargetLockMode != 0 ? unchecked((sbyte)oldTarget) : (sbyte)-1;
        }
        else
        {
            if (!hasEnemy && oldTarget != -1 && Ships[oldTarget].Side == player.Side &&
                (TargetLockMode != 0 || Objects[oldTarget].ScreenX != ObjectSlots.NotVisible))
            {
                selectNewTarget = false;
                player.Target = unchecked((sbyte)oldTarget);
            }
            if (selectNewTarget)
            {
                short targetIndex = 0;
                while (targetIndex < ViableTargetCount && Ships[ViableTarget[targetIndex]].Side == player.Side)
                    targetIndex++;
                player.Target = ViableTarget[targetIndex % ViableTargetCount];
            }
        }
        if (player.Target != oldTarget)
        {
            if (oldTarget != -1 && player.Target == -1)
                TargetLockMode = 0;
            TargetLockCountdown = -1;
        }
    }
}
