using WingCommander.Simulation.Data;
using WingCommander.Simulation.Missions;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// The simulation side of the comm menu (screen.c): orders to the wingman and other ships, the
// landing request with the objective clean-up, and the wingman's permission to engage (brains.c).
// The menu itself, npc_communication and the transmissions are UI (ADR-012).
public sealed partial class SpaceSimulation
{
    /// <summary>The wingman may engage on his own; the auto-engage timer goes idle.</summary>
    /// <remarks>C: allow_engage (0x409CF0, brains.c).</remarks>
    public void AllowEngage()
    {
        EngageAllowed = true;
        AutoEngageTimer = -1;
    }

    /// <remarks>C: disallow_engage (0x409CE0, brains.c).</remarks>
    public void DisallowEngage() => EngageAllowed = false;

    /// <summary>
    /// The wingman asked to engage and nobody answered: generic pilots, Iceman (8), Maniac (11) and
    /// Hunter (6) go ahead, Spirit (5) half of the time; the others wait 40 more ticks.
    /// </summary>
    /// <remarks>C: try2allow_engage (0x409D10, brains.c).</remarks>
    public void Try2AllowEngage(int pilotLevel)
    {
        if (pilotLevel <= 4 || pilotLevel == 8 || pilotLevel == 11 || pilotLevel == 6)
        {
            AllowEngage();
            return;
        }
        if (pilotLevel == 5 && Random.BelowOrEqual(100) < 50)
        {
            AllowEngage();
            return;
        }
        AutoEngageTimer = -40;
    }

    /// <summary>
    /// Converts the in-flight objective flags into achievements before landing: visited nav points,
    /// home base and escort targets (types 0, 1, 3); escort objectives (type 2) whose ship warped out
    /// (GOTO_WARP), came home within the entry nav point's radius of the home base (WARP_ARRIVE /
    /// COME_HOME, the record is marked home), or stayed alive and was sighted, each scoring mission
    /// event 5 (Hornet, Drayman) or 9; destroyed targets (type 4).
    /// </summary>
    /// <remarks>C: cleanup_objectives (0x42EFC0, screen.c). The loop runs to the -1 type terminator; the
    /// port also stops at the end of the table (the original has no bound).</remarks>
    public void CleanupObjectives()
    {
        short home = FindShipIndex(HomeMissionShipIndex);
        short proximity = MissionNavPoints[MissionEntryNavPoint].ProximityRadius;
        for (short objective = 0; objective < MissionObjectives.Length && MissionObjectives[objective].Type != -1; objective++)
        {
            if (Achieved(objective))
                continue;
            short index = MissionObjectives[objective].Index;
            switch (MissionObjectives[objective].Type)
            {
                case 0:
                case 1:
                case 3:
                    if (Visited(objective))
                        FlagObjective(objective, MissionObjective.FlagAchieved);
                    break;
                case 2:
                    CleanupEscortObjective(objective, index, home, proximity);
                    break;
                case 4:
                    if (MissionShips[index].State == 3)
                        FlagObjective(objective, MissionObjective.FlagAchieved);
                    break;
            }
        }
    }

    /// <summary>The type-2 case of <c>cleanup_objectives</c>.</summary>
    private void CleanupEscortObjective(short objective, short index, short home, short proximity)
    {
        ref var record = ref MissionShips[index];
        if (record.MissionType == ShipMissionType.GotoWarp)
        {
            if (record.State == 2)
                AchieveEscortObjective(objective, record.Type);
        }
        else if (record.MissionType is ShipMissionType.WarpArrive or ShipMissionType.ComeHome)
        {
            short obj = FindShipIndex(index);
            if (obj != -1 && home != -1 && DistanceFromObject(obj, home) < proximity)
                record.State = 1;
            if (record.State == 1 && Sighted(objective))
                AchieveEscortObjective(objective, record.Type);
        }
        else if (record.State == 0 && Sighted(objective))
        {
            AchieveEscortObjective(objective, record.Type);
        }
    }

    private void AchieveEscortObjective(short objective, ObjectType type)
    {
        FlagObjective(objective, MissionObjective.FlagAchieved);
        AffectMissionScore(0, type is ObjectType.Hornet or ObjectType.Drayman ? 5 : 9, -1);
    }

    /// <summary>A routing ship ignores orders.</summary>
    /// <remarks>C: too_busy (0x42F1F0, screen.c).</remarks>
    public bool TooBusy(short ship) => Ships[ship].MissionType == ShipMissionType.Rout;

    /// <summary>Answers an order: line 0 (yes) or 1 (no).</summary>
    /// <remarks>C: reply (0x42F210, screen.c).</remarks>
    public void Reply(short ship, bool accepted) => SendMessage(ship, accepted ? (sbyte)0 : (sbyte)1);

    /// <summary>Paladin (10) refuses formation while an enemy sits on the player's tail, Maniac (11) while
    /// any Kilrathi is around.</summary>
    /// <remarks>C: disobey_formation (0x42F240, screen.c).</remarks>
    public bool DisobeyFormation(short ship) => Ships[ship].PilotLevel switch
    {
        10 => AnyEnemyTail(0),
        11 => ReportKilrathiRout(0),
        _ => false,
    };

    /// <summary>The target is the ship itself, on its own side, or the ship is routing.</summary>
    /// <remarks>C: bad_target (0x42F270, screen.c). Without a player target (-1) the original reads
    /// <c>aeShipSide[-1]</c>, which in the Kilrathi Saga image (0x0059d64c) is <c>anRollGoal[14..15]</c>,
    /// never written: Imperial. So -1 is a bad target for Confed ships and a good one for Kilrathi (the
    /// latter then "engage" target -1). Other targets outside the ship slots count as bad.</remarks>
    public bool BadTarget(short ship, short target)
    {
        if (target == ship)
            return true;
        Side targetSide;
        if (target == -1)
            targetSide = Side.Imperial;
        else if ((uint)target >= ObjectSlots.ShipSlotCount)
            return true;
        else
            targetSide = Ships[target].Side;
        return targetSide == Ships[ship].Side || TooBusy(ship);
    }

    /// <summary>
    /// Landing clearance: no enemy within 20000 and either the player is hurt (health below 50), has a
    /// kill or is low on fuel (below 1000), or any objective other than the home base is achieved
    /// (or visited, except escort objectives).
    /// </summary>
    /// <remarks>C: can_land (0x42F2B0, screen.c). One qualifying objective is enough.</remarks>
    public bool CanLand()
    {
        bool result = false;
        if (!AnyEnemy(0, 20000))
        {
            if (EvaluateDamage(0) < 50 || PlayerKillCount > 0 || Ships[ObjectSlots.Player].Fuel < 1000)
                result = true;
            for (short index = 0; index < MissionObjectiveCount; index++)
            {
                int type = MissionObjectives[index].Type;
                if (type == 1)
                    continue;
                if (!Achieved(index) && (!Visited(index) || type == 2))
                    continue;
                result = true;
            }
        }
        return result;
    }

    /// <summary>
    /// Will the ship accept "Return to base"? Generic pilots yes; Hunter (6) without enemies within
    /// 5000; Bossman (7) and Maniac (11) never; Iceman (8) only in a canned sequence; Angel (9) when the
    /// player's mission succeeded; Paladin (10) without enemies within 10000 of the player; others yes.
    /// </summary>
    /// <remarks>C: i_wanna_rout (0x42F350, screen.c).</remarks>
    public bool IWannaRout(short ship, int pilot)
    {
        if (pilot <= 4)
            return true;
        return pilot switch
        {
            6 => !AnyEnemy(ship, 5000),
            7 or 11 => false,
            8 => Ships[ship].MissionType == ShipMissionType.CannedSequence,
            9 => Triumph(0),
            10 => !AnyEnemy(0, 10000),
            _ => true,
        };
    }

    /// <summary>
    /// An order of <paramref name="requester"/> (the player, 0) to <paramref name="ship"/>: attack my
    /// target, help me (engage whoever targets me, else form on my wing), return to base, taunts to a
    /// Kilrathi (it may answer and turn on the requester), break and attack, keep formation, form on my
    /// wing, radio silence on/off, request landing. The ship answers with line 0/1 (8/9 for landing).
    /// Any value is accepted: -1 (the comm menu's off-by-one) and unknown commands do nothing.
    /// </summary>
    /// <remarks>C: request (0x42F3F0, screen.c), called by the comm menu (Chosen_communicate_option) and
    /// the keys H (form on my wing, 9) and B (break and attack, 7). A recipient outside the ship slots
    /// (the target died while the menu was open) is ignored; the original reads the -1 entries.</remarks>
    public void Request(short requester, short ship, CommCommand command)
    {
        if ((uint)ship >= ObjectSlots.ShipSlotCount)
            return;
        while (true)
        {
            short target = Ships[requester].Target;
            switch (command)
            {
                case CommCommand.AttackTarget:
                    AllowEngage();
                    if (!BadTarget(ship, target))
                    {
                        Engage(ship, target, ShipObjective.EngageEnemy);
                        Reply(ship, true);
                        return;
                    }
                    Reply(ship, false);
                    return;

                case CommCommand.HelpMeOut:
                    AllowEngage();
                    target = -1;
                    for (short obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
                    {
                        if (Objects[obj].Class >= ObjectClass.Ship &&
                            Ships[obj].SpecialManeuver != SpecialManeuver.Unknown9 &&
                            Ships[ship].Side != Ships[obj].Side &&
                            Ships[obj].Target == requester)
                        {
                            target = obj;
                            break;
                        }
                    }
                    if (target == -1)
                    {
                        command = CommCommand.FormOnMyWing;
                        continue;
                    }
                    Engage(ship, target, ShipObjective.EngageEnemy);
                    Reply(ship, true);
                    return;

                case CommCommand.ReturnToBase:
                    if (IWannaRout(ship, Ships[ship].PilotLevel) && Try2Rout(ship))
                    {
                        EngageAllowed = false;
                        Reply(ship, true);
                        return;
                    }
                    Reply(ship, false);
                    return;

                case CommCommand.DieFurball:
                case CommCommand.SlagOff:
                case CommCommand.BiteItCatFace:
                    if (Random.Below(100) < 70 || (Ships[ship].Rating > 8 && Ships[ship].Rating < 13))
                        SendMessage(ship, unchecked((sbyte)(command - 2)));
                    if (Ships[ship].Target != requester && !TooBusy(ship))
                        Engage(ship, requester, ShipObjective.EngageEnemy);
                    return;

                case CommCommand.BreakAndAttack:
                    AllowEngage();
                    if (Ships[ship].Objective == ShipObjective.HoldFormation)
                    {
                        ResetObjective(ship, ShipObjective.BreakFormation);
                        Reply(ship, true);
                        return;
                    }
                    Reply(ship, false);
                    return;

                case CommCommand.KeepFormation:
                    DisallowEngage();
                    if (DisobeyFormation(ship))
                    {
                        AlterObjective(ship, ShipObjective.BreakFormation);
                        Reply(ship, false);
                        return;
                    }
                    AutoEngageTimer = -150;
                    Reply(ship, true);
                    return;

                case CommCommand.FormOnMyWing:
                    DisallowEngage();
                    if (DisobeyFormation(ship))
                    {
                        Reply(ship, false);
                        return;
                    }
                    ResetObjective(ship, ShipObjective.HoldFormation);
                    AutoEngageTimer = -150;
                    Reply(ship, true);
                    return;

                case CommCommand.KeepRadioSilence:
                case CommCommand.BroadcastFreely:
                    RadioSilence = false;
                    Reply(ship, true);
                    RadioSilence = command == CommCommand.KeepRadioSilence;
                    return;

                case CommCommand.RequestLanding:
                    CleanupObjectives();
                    if (CanLand())
                    {
                        LandingAuthorized = true;
                        SendMessage(ship, 8);
                        return;
                    }
                    SendMessage(ship, 9);
                    return;

                default:
                    return;
            }
        }
    }

    /// <remarks>C: wingman_dead (0x430E10, screen.c).</remarks>
    public bool WingmanDead() => YourWingman == -1;

    /// <summary>The player has an active target.</summary>
    /// <remarks>C: have_target (0x430E30, screen.c).</remarks>
    public bool HaveTarget() => !Unactive(Ships[ObjectSlots.Player].Target);
}
