using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Read-only views of the world for renderers, replays and tests: the renderer snapshot and a
// deterministic hash of the whole simulation state.
public sealed partial class SpaceSimulation
{
    /// <summary>Copies the camera and every non-empty slot 0..60 with its 3D state and its original
    /// sprite projection into <paramref name="snapshot"/> (call after <see cref="PrepareSpaceView"/>
    /// returned true). Allocates nothing.</summary>
    public void CaptureSpaceView(SpaceViewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ref readonly var eye = ref Objects[ObjectSlots.Eye];
        snapshot.SpaceFrame = SpaceFrame;
        snapshot.RenderedSpaceFrame = RenderedSpaceFrame;
        snapshot.CameraViewMode = CameraViewMode;
        snapshot.CockpitView = CockpitView;
        snapshot.CockpitlessView = CockpitlessView;
        snapshot.CameraPosition = eye.Position;
        snapshot.CameraVelocity = eye.Velocity;
        snapshot.CameraRight = eye.Right;
        snapshot.CameraUp = eye.Up;
        snapshot.CameraForward = eye.Forward;
        snapshot.CameraNearRadius = eye.CollisionRadius;
        snapshot.ScreenWidth = ScreenWidth;
        snapshot.ScreenHeight = ScreenHeight;
        snapshot.ViewCenterX = ViewCenterX;
        snapshot.ViewCenterY = ViewCenterY;
        snapshot.ConstellationShape = ConstellationShape;
        snapshot.PlayerTarget = Ships[ObjectSlots.Player].Target;
        snapshot.TargetLockCountdown = TargetLockCountdown;
        snapshot.TargetLockMarkerAngle = TargetLockMarkerAngle;

        int count = 0;
        for (short obj = 0; obj <= ObjectSlots.LastMoving; obj++)
        {
            ref readonly var o = ref Objects[obj];
            if (o.Class == ObjectClass.Null)
                continue;
            ref var view = ref snapshot.Objects[count++];
            view.Slot = obj;
            view.Type = o.Type;
            view.Class = o.Class;
            view.Owner = o.Owner;
            view.Position = o.Position;
            view.Velocity = o.Velocity;
            view.Right = o.Right;
            view.Up = o.Up;
            view.Forward = o.Forward;
            view.Scale = o.Scale;
            view.CollisionRadius = o.CollisionRadius;
            view.Shape = o.Shape;
            view.IsNavPointer = obj == NavPointerObject;
            view.DrawShape = !view.IsNavPointer && o.Class is ObjectClass.Star or ObjectClass.Dust
                ? ConstellationShape
                : o.Shape;
            view.ViewFrame = o.ViewFrame;
            view.Flip = o.Flip;
            view.ScreenAngle = o.ScreenAngle;
            view.ScreenScale = o.ScreenScale;
            view.ScreenX = o.ScreenX;
            view.ScreenY = o.ScreenY;
            view.Distance = o.Distance;
            view.ViewPosition = o.ViewPosition;
            if (obj < ObjectSlots.ShipSlotCount && o.Class >= ObjectClass.Missile)
            {
                ref readonly var ship = ref Ships[obj];
                view.Side = ship.Side;
                view.SpecialManeuver = ship.SpecialManeuver;
                view.ExhaustHeat = ship.ExhaustHeat;
                view.Speed = o.Speed;
                view.CapitalShipViewFrame = ship.CapitalShipViewFrame;
            }
            else
            {
                view.Side = Side.Neutral;
                view.SpecialManeuver = SpecialManeuver.None;
                view.ExhaustHeat = 0;
                view.Speed = 0;
                view.CapitalShipViewFrame = -1;
            }
        }
        snapshot.ObjectCount = count;

        int drawCount = 0;
        while (drawCount < ObjectSlots.Count && SortedObjects[drawCount] != -1)
        {
            snapshot.DrawOrder[drawCount] = (short)SortedObjects[drawCount];
            drawCount++;
        }
        snapshot.DrawCount = drawCount;
    }

    /// <summary>
    /// A 64-bit FNV-1a hash over the complete simulation state (all 64 object slots, the ship state
    /// of slots 0..9, flight, camera, target, hazard and mission globals, the AI scratch globals, the
    /// objective records and flight path, the autopilot state and the random generator's seed). Two runs with the same inputs and seed must produce the same hash
    /// after every frame; replays and save states can use it as a checksum.
    /// </summary>
    public ulong ComputeStateHash()
    {
        var hash = new StateHasher();
        hash.Add((int)Random.Seed);
        hash.Add(SpaceFrame);
        hash.Add(RenderedSpaceFrame);
        hash.Add(FrameSkipCounter);
        for (int obj = 0; obj < ObjectSlots.Count; obj++)
        {
            ref readonly var o = ref Objects[obj];
            hash.Add((int)o.Type);
            hash.Add((int)o.Class);
            hash.Add(o.Position);
            hash.Add(o.Velocity);
            hash.Add(o.Right);
            hash.Add(o.Up);
            hash.Add(o.Forward);
            hash.Add(o.PitchRotation);
            hash.Add(o.YawRotation);
            hash.Add(o.RollRotation);
            hash.Add(o.Speed);
            hash.Add(o.Counter);
            hash.Add(o.Owner);
            hash.Add(o.CollisionRadius);
            hash.Add(o.RadarRadius);
            hash.Add(o.AfterburnerVelocity);
            hash.Add(o.Scale);
            hash.Add(o.ScreenScale);
            hash.Add(o.ScreenX);
            hash.Add(o.ScreenY);
            hash.Add(o.Distance);
            hash.Add(o.PreviousDistance);
            hash.Add(o.ViewPosition);
            hash.Add(o.ScreenAngle);
            hash.Add(o.Flip);
            hash.Add(o.ViewFrame);
            hash.Add(o.Shape.LogicalFile);
            hash.Add(o.Shape.Section);
            hash.Add(o.AnimationDelay);
            hash.Add(o.AnimationIndex);
            hash.Add(o.LastCollisionObject);
            hash.Add(o.CollisionGraceTicks);
            hash.Add(o.AccumulatedDamage);
        }
        for (int obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            ref readonly var s = ref Ships[obj];
            hash.Add((int)s.Side);
            hash.Add(s.PilotLevel);
            hash.Add(s.Rating);
            hash.Add(s.Fuel);
            hash.Add(s.MaximumSpeed);
            hash.Add(s.AfterburnerTimer);
            hash.Add((int)s.SpecialManeuver);
            hash.Add((int)s.MissionType);
            hash.Add((int)s.Objective);
            hash.Add((int)s.Tactic);
            hash.Add((int)s.Maneuver);
            hash.Add(s.Count);
            hash.Add(s.Sequence);
            hash.Add(s.YawGoal);
            hash.Add(s.PitchGoal);
            hash.Add(s.RollGoal);
            hash.Add(s.PointingMode);
            hash.Add(s.Target);
            for (int i = 0; i < 2; i++)
            {
                hash.Add(s.Shield[i]);
                hash.Add(s.MaximumShield[i]);
            }
            for (int i = 0; i < 4; i++)
                hash.Add(s.Armor[i]);
            hash.Add(s.WeaponEnergy);
            for (int i = 0; i < WeaponLoadout.Size; i++)
                hash.Add(s.Weapons[i]);
            hash.Add(s.Damage);
            hash.Add(s.IonDriveDamage);
            hash.Add(s.DestroyedWeaponCount);
            hash.Add(s.Communicator);
            hash.Add(s.PilotHitPoints);
            hash.Add(s.LastAttacker);
            hash.Add(s.AiCooldown);
            hash.Add(s.Stress);
            hash.Add((int)s.AlertFlags);
            hash.Add(s.CollisionAlertTarget);
            hash.Add(s.CollisionCountdown);
            hash.Add(s.CollisionPartner);
            hash.Add(s.CollisionTime);
            hash.Add(s.TurnRegulator);
            hash.Add(s.TurnInterval);
            hash.Add(s.Turn);
            hash.Add(s.WingLeader);
            hash.Add(s.FormationOffset.X);
            hash.Add(s.FormationOffset.Y);
            hash.Add(s.FormationOffset.Z);
            hash.Add(s.MissionShip);
            hash.Add(s.MissionIndex);
            hash.Add(s.SpawnNavPoint);
            hash.Add(s.NavPointIndex);
            hash.Add(s.Destination);
            hash.Add(s.MissionSpot);
            hash.Add(s.ExhaustHeat);
            hash.Add(s.WingmanMessageState);
            hash.Add(s.CapitalShipViewFrame);
            hash.Add(s.CannedCommand);
            hash.Add(s.ActionCount);
            hash.Add(s.IntelligenceEvent);
        }

        hash.Add(YourWingman);
        hash.Add(NavPointerObject);
        hash.Add(PlayerCollisionObject);
        hash.Add(ClosestVisibleObject);
        hash.Add(ExternalViewShip);
        hash.Add(ArcadeState);
        hash.Add(PlayerDestroyed ? 1 : 0);
        hash.Add((int)SelectedGunType);
        hash.Add(SelectedReleaseWeaponIndex);
        hash.Add(TargetLockCountdown);
        hash.Add(TargetLockMode);
        hash.Add(TargetLockAcquired ? 1 : 0);
        hash.Add(TargetLockMarkerAngle);
        foreach (sbyte damage in PlayerComponentDamage)
            hash.Add(damage);
        hash.Add(CameraViewMode);
        hash.Add(ViewObject);
        hash.Add(AlternateChaseView ? 1 : 0);
        hash.Add(ExternalViewAngle);
        hash.Add(ExternalViewDistance);
        hash.Add(EyePitchGoal);
        hash.Add(EyeYawGoal);
        hash.Add(EyeRollGoal);
        hash.Add(EyePitchRate);
        hash.Add(EyeYawRate);
        hash.Add(EyeRollRate);
        hash.Add(StarFieldIRotation);
        hash.Add(StarFieldJRotation);
        hash.Add(StarFieldMotion);
        hash.Add(PreviousStarFieldMotion);
        hash.Add(ActiveHazardField);
        foreach (sbyte hazard in HazardObjects)
            hash.Add(hazard);
        hash.Add(ActiveHazards);
        hash.Add(HazardReferenceSpeed);
        hash.Add(CurrentNavPoint);
        hash.Add(CurrentObjective);
        hash.Add(CurrentWave);
        hash.Add(PlayerKillCount);
        hash.Add(WingmanKillCount);
        hash.Add(MissionMedalScore);
        hash.Add(ArcadeScore);
        hash.Add(Campaign.MissionScore);
        foreach (var record in MissionShips)
            hash.Add(record.State);
        foreach (int sorted in SortedObjects)
            hash.Add(sorted);
        hash.Add(TargetShip);
        hash.Add(TargetRange);
        hash.Add(FacingToTarget);
        hash.Add(TargetFacing);
        hash.Add(ToTarget);
        hash.Add(NormalizedToTarget);
        hash.Add(CollisionDelta);
        hash.Add(PlayerAcceleration);

        // AI scratch globals, objective tracking, comm orders and the autopilot (phase 3)
        hash.Add(TooCloseRange);
        hash.Add(CurrentManeuverReroll);
        hash.Add(LastShipSlot);
        hash.Add(LastFoundShip);
        foreach (short range in ViableTargetDistance)
            hash.Add(range);
        foreach (sbyte target in ViableTarget)
            hash.Add(target);
        hash.Add(ViableTargetCount);
        foreach (short range in TargetListRange)
            hash.Add(range);
        foreach (sbyte member in FormationMemberList)
            hash.Add(member);
        hash.Add(EngageAllowed ? 1 : 0);
        hash.Add(AutoEngageTimer);
        hash.Add(CannedSceneMode);
        hash.Add(LandingAuthorized ? 1 : 0);
        hash.Add(WingmanKilledThisMission ? 1 : 0);
        hash.Add(EnemySighting);
        hash.Add(MissionObjectiveCount);
        hash.Add(CurrentNavPointIndex);
        hash.Add(CurrentObjectiveRange);
        foreach (sbyte step in FlightPath)
            hash.Add(step);
        for (int i = 0; i < MissionObjectives.Length; i++)
        {
            ref readonly var objective = ref MissionObjectives[i];
            hash.Add(objective.Type);
            hash.Add(objective.Index);
            hash.Add(objective.Flags);
            hash.Add(objective.Position);
        }
        hash.Add(AutopilotFormationShipCount);
        hash.Add(_autopilotSavedCannedSceneMode);
        hash.Add(_autopilotDestination);
        hash.Add(_autopilotInitialDistance);
        hash.Add(_autopilotTravelStep);
        foreach (sbyte mode in _autopilotTravelMode)
            hash.Add(mode);
        return hash.Value;
    }

    /// <summary>64-bit FNV-1a over 32-bit words.</summary>
    private struct StateHasher
    {
        private ulong _hash;
        private bool _started;

        public readonly ulong Value => _started ? _hash : 14695981039346656037UL;

        public void Add(int value)
        {
            if (!_started)
            {
                _hash = 14695981039346656037UL;
                _started = true;
            }
            for (int i = 0; i < 4; i++)
            {
                _hash ^= (byte)(value >> (i * 8));
                _hash *= 1099511628211UL;
            }
        }

        public void Add(in FixedVector value)
        {
            Add(value.X);
            Add(value.Y);
            Add(value.Z);
        }
    }
}
