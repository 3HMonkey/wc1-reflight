using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Flight;

public class TargetingTests
{
    private static (SpaceSimulation Sim, RecordingEvents Events, short Enemy) RaptorBehindAnEnemy()
    {
        var events = new RecordingEvents();
        var sim = TestWorld.Create(events: events);
        TestWorld.AddPlayer(sim, ObjectType.Raptor); // a heat seeker is the selected release weapon
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 2000); // flying away
        sim.Objects[enemy].ScreenX = 0;
        sim.Objects[enemy].ScreenY = 0;
        sim.Ships[0].Target = (sbyte)enemy;
        sim.TargetLockCountdown = -1;
        return (sim, events, enemy);
    }

    [Fact]
    public void A_heat_seeker_locks_after_18_frames_on_a_target_flying_away()
    {
        var (sim, events, enemy) = RaptorBehindAnEnemy();
        Assert.Equal(ObjectType.HeatSeekingMissile, sim.Ships[0].Weapons.GetWeaponType(sim.SelectedReleaseWeaponIndex));
        sim.TargetLocking((sbyte)enemy);
        Assert.Equal(18, sim.TargetLockCountdown);
        Assert.InRange(sim.TargetLockMarkerAngle, 0, 0x167);
        for (int frame = 0; frame < 17; frame++)
            sim.TargetLocking((sbyte)enemy);
        Assert.Equal(1, sim.TargetLockCountdown);
        Assert.Equal(17, events.Count("sfx 21 "));
        sim.TargetLocking((sbyte)enemy);
        Assert.Equal(0, sim.TargetLockCountdown);
        Assert.True(sim.TargetLockAcquired);
        Assert.Contains("sfx 22 -1", events.Calls);
        Assert.Contains("locked", events.Calls);
    }

    [Fact]
    public void The_lock_is_lost_off_centre_or_when_the_target_turns()
    {
        var (sim, events, enemy) = RaptorBehindAnEnemy();
        sim.TargetLocking((sbyte)enemy);
        sim.Objects[enemy].ScreenX = 61; // outside the 60-pixel circle
        sim.TargetLocking((sbyte)enemy);
        Assert.Equal(-1, sim.TargetLockCountdown);
        Assert.True(sim.TargetLockReadoutDirty);

        sim.Objects[enemy].ScreenX = 0;
        sim.Objects[enemy].Forward = new WingCommander.Core.Numerics.FixedVector(0, 0, -0x100); // now facing us
        sim.TargetLocking((sbyte)enemy);
        Assert.Equal(-1, sim.TargetLockCountdown);
        Assert.True(events.Count("lockOff") >= 2);
    }

    [Fact]
    public void Image_recognition_locks_take_32_frames_from_any_side()
    {
        var (sim, _, enemy) = RaptorBehindAnEnemy();
        sim.SelectedReleaseWeaponIndex = 6; // image recognition
        sim.Objects[enemy].Forward = new WingCommander.Core.Numerics.FixedVector(0, 0, -0x100);
        sim.TargetLocking((sbyte)enemy);
        Assert.Equal(32, sim.TargetLockCountdown);
    }

    [Fact]
    public void A_damaged_tracker_blocks_the_lock()
    {
        var (sim, _, enemy) = RaptorBehindAnEnemy();
        sim.PlayerComponentDamage[5] = 4;
        sim.TargetLockCountdown = 5;
        sim.TargetLocking((sbyte)enemy);
        Assert.Equal(-1, sim.TargetLockCountdown);
    }

    [Fact]
    public void Automatic_targeting_picks_the_nearest_visible_enemy()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short friend = TestWorld.AddShip(sim, ObjectType.Hornet, Side.Imperial, 0, 0, 1000);
        short far = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 3000);
        short near = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 2000);
        short hidden = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 1500);
        SetSeen(sim, friend, 1000);
        SetSeen(sim, far, 3000);
        SetSeen(sim, near, 2000);
        sim.Objects[hidden].ScreenX = ObjectSlots.NotVisible;
        sim.CheckTarget();
        Assert.Equal((sbyte)near, sim.Ships[0].Target);

        // A visible hostile target is kept; the target key cycles to the next visible ship (enemies only).
        sim.CheckTarget();
        Assert.Equal((sbyte)near, sim.Ships[0].Target);
        sim.CycleOnscreenTargets();
        Assert.Equal((sbyte)far, sim.Ships[0].Target);
        sim.CycleOnscreenTargets();
        Assert.Equal((sbyte)near, sim.Ships[0].Target);

        // Dying targets are dropped.
        sim.Ships[near].SpecialManeuver = SpecialManeuver.Unknown9;
        sim.CheckTarget();
        Assert.Equal((sbyte)far, sim.Ships[0].Target);
    }

    [Fact]
    public void Lock_mode_keeps_the_target_off_screen()
    {
        var sim = TestWorld.Create();
        TestWorld.AddPlayer(sim);
        short enemy = TestWorld.AddShip(sim, ObjectType.Salthi, Side.Kilrathi, 0, 0, 2000);
        SetSeen(sim, enemy, 2000);
        sim.CheckTarget();
        sim.ToggleTargetLockMode();
        Assert.Equal(1, sim.TargetLockMode);
        sim.Objects[enemy].ScreenX = ObjectSlots.NotVisible;
        sim.RenderedSpaceFrame = 1; // no tracker malfunction roll
        sim.CheckTarget();
        Assert.Equal((sbyte)enemy, sim.Ships[0].Target);

        sim.ToggleTargetLockMode();
        sim.CheckTarget();
        Assert.Equal(-1, sim.Ships[0].Target);
    }

    private static void SetSeen(SpaceSimulation sim, short obj, short distance)
    {
        sim.Objects[obj].ScreenX = 0;
        sim.Objects[obj].Distance = distance;
    }
}
