using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;
using WingCommander.Simulation.Tests.TestSupport;

namespace WingCommander.Simulation.Tests.Objects;

public class WeaponLoadoutTests
{
    [Fact]
    public void Remove_weapon_shifts_later_slots_down()
    {
        var sim = FakeResources.CreateSimulation();
        sim.SetObjectsData(1, ObjectType.Rapier, -1);
        ref var loadout = ref sim.Ships[1].Weapons;
        sim.RemoveWeapon(1, 2); // neutron@12
        Assert.Equal(8, loadout.Count);
        Assert.Equal(ObjectType.LaserCannon, loadout.GetWeaponType(1));
        Assert.Equal(ObjectType.NeutronParticleGun, loadout.GetWeaponType(2));
        Assert.Equal(20, loadout.GetHardpoint(2));
        Assert.Equal(ObjectType.ImageRecognitionMissile, loadout.GetWeaponType(3));
        Assert.Equal(ObjectType.DumbFireMissile, loadout.GetWeaponType(7));
        Assert.Equal(19, loadout.GetHardpoint(7));
        // The vacated slot 8 keeps a stale copy; the original marks slot 9 (= old count) disabled.
        Assert.Equal(ObjectType.DumbFireMissile, loadout.GetWeaponType(8));
        Assert.Equal(1, loadout.GetDisabled(9));
        Assert.Equal(-1, sim.FindWeapon(1, ObjectType.MassDriverCannon));
        Assert.Equal(4, sim.FindWeapon(1, ObjectType.FriendOrFoeMissile));
    }

    [Fact]
    public void Removing_from_a_full_loadout_does_not_touch_the_next_ship()
    {
        var sim = FakeResources.CreateSimulation();
        sim.SetObjectsData(1, ObjectType.Raptor, -1); // ten weapons
        sim.SetObjectsData(2, ObjectType.Hornet, -1);
        ReadOnlySpan<byte> before = sim.Ships[2].Weapons;
        byte[] snapshot = before.ToArray();
        sim.RemoveWeapon(1, 0);
        Assert.Equal(9, sim.Ships[1].Weapons.Count);
        ReadOnlySpan<byte> after = sim.Ships[2].Weapons;
        Assert.True(after.SequenceEqual(snapshot));
    }

    [Fact]
    public void Any_selected_looks_for_enabled_slots_of_a_class()
    {
        var loadout = ObjectTypeTable.Get(ObjectType.Hornet).WeaponLoadout;
        Assert.True(SpaceSimulation.AnySelected(loadout, ObjectClass.Projectile));
        Assert.True(SpaceSimulation.AnySelected(loadout, ObjectClass.Missile));
        Assert.False(SpaceSimulation.AnySelected(loadout, ObjectClass.Mine));
        loadout.SetDisabled(2, 1);
        Assert.False(SpaceSimulation.AnySelected(loadout, ObjectClass.Missile));
    }

    [Fact]
    public void Player_losing_the_selected_missile_selects_the_next_of_the_same_type()
    {
        var sim = FakeResources.CreateSimulation();
        sim.SetObjectsData(0, ObjectType.Hornet, -1);
        Assert.Equal(2, sim.SelectedReleaseWeaponIndex);
        sim.RemoveWeapon(0, 2); // fire the enabled DF: the next DF (now slot 2) is selected
        ref var loadout = ref sim.Ships[0].Weapons;
        Assert.Equal(4, loadout.Count);
        Assert.Equal(ObjectType.DumbFireMissile, loadout.GetWeaponType(2));
        Assert.Equal(2, sim.SelectedReleaseWeaponIndex);
        Assert.Equal(0, loadout.GetDisabled(2));
        sim.RemoveWeapon(0, 2); // last DF gone: falls back to the first non-gun slot (HS)
        Assert.Equal(ObjectType.HeatSeekingMissile, loadout.GetWeaponType(2));
        Assert.Equal(2, sim.SelectedReleaseWeaponIndex);
        Assert.Equal(0, loadout.GetDisabled(2));
    }

    [Fact]
    public void Player_losing_all_guns_of_the_selected_type_cycles_the_gun()
    {
        var sim = FakeResources.CreateSimulation();
        sim.SetObjectsData(0, ObjectType.Rapier, -1);
        Assert.Equal(ObjectType.NeutronParticleGun, sim.SelectedGunType);
        sim.RemoveWeapon(0, 2);
        Assert.Equal(ObjectType.NeutronParticleGun, sim.SelectedGunType); // one neutron still enabled
        sim.RemoveWeapon(0, 2);
        // No gun enabled any more: find_next_gun(neutron) -> no neutron left -> first gun (laser).
        Assert.Equal(ObjectType.LaserCannon, sim.SelectedGunType);
        ref var loadout = ref sim.Ships[0].Weapons;
        Assert.Equal(0, loadout.GetDisabled(0));
        Assert.Equal(0, loadout.GetDisabled(1));
    }

    [Fact]
    public void Gun_cycling_visits_each_type_then_all_guns()
    {
        var sim = FakeResources.CreateSimulation();
        sim.SetObjectsData(0, ObjectType.Jalthi, -1); // neutron x2, laser x4
        Assert.Equal(ObjectType.NeutronParticleGun, sim.SelectedGunType);
        sim.SelectNewGun();
        Assert.Equal(ObjectType.LaserCannon, sim.SelectedGunType);
        Assert.Equal(1, sim.Ships[0].Weapons.GetDisabled(0));
        Assert.Equal(0, sim.Ships[0].Weapons.GetDisabled(2));
        sim.SelectNewGun();
        Assert.Equal(SpaceSimulation.AllGuns, sim.SelectedGunType);
        for (int i = 0; i < 6; i++)
            Assert.Equal(0, sim.Ships[0].Weapons.GetDisabled(i));
        sim.SelectNewGun();
        Assert.Equal(ObjectType.NeutronParticleGun, sim.SelectedGunType);
    }

    [Fact]
    public void Release_weapon_cycling_skips_slots_of_the_same_type()
    {
        var sim = FakeResources.CreateSimulation();
        sim.SetObjectsData(0, ObjectType.Raptor, -1);
        Assert.Equal(4, sim.SelectedReleaseWeaponIndex); // HS@22
        sim.SelectNewReleaseWeapon(ObjectType.None);
        Assert.Equal(6, sim.SelectedReleaseWeaponIndex); // IR (slot 5 is another HS)
        Assert.Equal(1, sim.Ships[0].Weapons.GetDisabled(4));
        Assert.Equal(0, sim.Ships[0].Weapons.GetDisabled(6));
        sim.SelectNewReleaseWeapon(ObjectType.None);
        Assert.Equal(8, sim.SelectedReleaseWeaponIndex); // FF
        sim.SelectNewReleaseWeapon(ObjectType.None);
        Assert.Equal(9, sim.SelectedReleaseWeaponIndex); // mine
        sim.SelectNewReleaseWeapon(ObjectType.None);
        Assert.Equal(4, sim.SelectedReleaseWeaponIndex); // wraps past the guns to HS
    }
}
