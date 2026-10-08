using WingCommander.Simulation.Data;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// Weapon loadout helpers and the player's gun / release-weapon selection (disk.c, logic.c, hudmsg.c).
public sealed partial class SpaceSimulation
{
    /// <summary>Special <see cref="SelectedGunType"/> value: all guns selected ("full guns").</summary>
    public const ObjectType AllGuns = (ObjectType)0x80;

    /// <summary>True when any enabled slot holds a weapon of class <paramref name="objectClass"/>.</summary>
    /// <remarks>C: any_selected (0x41DFE0, disk.c).</remarks>
    public static bool AnySelected(in WeaponLoadout loadout, ObjectClass objectClass)
    {
        bool selected = false;
        for (int weapon = 0; weapon < loadout.Count; weapon++)
        {
            if (selected)
                break;
            if (ObjectTypeTable.Get(loadout.GetWeaponType(weapon)).ObjectClass == objectClass &&
                loadout.GetDisabled(weapon) == 0)
            {
                selected = true;
            }
        }
        return selected;
    }

    /// <summary>First loadout slot holding <paramref name="weaponType"/>, or -1.</summary>
    /// <remarks>C: find_weapon (0x421100, logic.c).</remarks>
    public short FindWeapon(short obj, ObjectType weaponType)
    {
        ref var loadout = ref Ships[obj].Weapons;
        for (short weapon = 0; weapon < loadout.Count; weapon++)
        {
            if (loadout.GetWeaponType(weapon) == weaponType)
                return weapon;
        }
        return -1;
    }

    /// <summary>
    /// Removes slot <paramref name="weapon"/>: later slots shift down, the count drops. For the
    /// player a new gun or release weapon is selected when nothing of that class stays enabled.
    /// </summary>
    /// <remarks>C: remove_weapon (0x41E040, disk.c). The original then writes 1 to byte
    /// <c>count * 7 + 7</c> (the disabled byte of slot <c>count</c>, one past the vacated slot);
    /// for a full ten-weapon loadout that is byte 77 of the 71-byte record, i.e. the next ship's
    /// first hardpoint. The port skips that out-of-range write (memory-safety fix).</remarks>
    public void RemoveWeapon(short obj, short weapon)
    {
        ref var loadout = ref Ships[obj].Weapons;
        var preferredType = loadout.GetWeaponType(weapon);
        var objectClass = ObjectTypeTable.Get(preferredType).ObjectClass;
        for (int current = weapon; current < loadout.Count - 1; current++)
            loadout.CopySlot(current + 1, current);
        int tail = loadout.Count * WeaponLoadout.SlotSize + 7;
        if (tail < WeaponLoadout.Size)
            loadout[tail] = 1;
        loadout.Count = unchecked((sbyte)(loadout.Count - 1));
        if (obj == ObjectSlots.Player)
        {
            if (!AnySelected(loadout, objectClass))
            {
                if (objectClass == ObjectClass.Projectile)
                {
                    SelectNewGun();
                }
                else
                {
                    SelectedReleaseWeaponIndex = -1;
                    SelectNewReleaseWeapon(preferredType);
                }
            }
            Events.WeaponSelectionChanged();
        }
    }

    /// <summary>Next gun type after <paramref name="currentGun"/> in loadout order; after the last
    /// type comes <see cref="AllGuns"/> (0x80); -1 when there are no guns.</summary>
    /// <remarks>C: find_next_gun (0x42AD00, hudmsg.c).</remarks>
    public short FindNextGun(short obj, ObjectType currentGun)
    {
        ref var loadout = ref Ships[obj].Weapons;
        bool foundCurrent = false;
        short firstGun = -1;
        short weaponCount = loadout.Count;
        for (short weapon = 0; weapon < weaponCount; weapon++)
        {
            var type = loadout.GetWeaponType(weapon);
            if (ObjectTypeTable.Get(type).ObjectClass == ObjectClass.Projectile)
            {
                if (firstGun == -1)
                    firstGun = (short)type;
                if (!foundCurrent)
                {
                    if (currentGun == type)
                        foundCurrent = true;
                }
                else if (currentGun != type)
                {
                    return (short)type;
                }
            }
        }
        if (foundCurrent && firstGun != (short)currentGun)
            firstGun = 0x80;
        return firstGun;
    }

    /// <summary>Enables the player's gun slots of <paramref name="selectedGun"/> (all guns for 0x80) and
    /// disables the others; returns the selection or -1 when no gun matched. Always works on
    /// slot 0's loadout, like the original.</summary>
    /// <remarks>C: select_guns (0x42ADA0, hudmsg.c).</remarks>
    public short SelectGuns(short obj, short selectedGun)
    {
        _ = obj;
        ref var loadout = ref Ships[ObjectSlots.Player].Weapons;
        short weaponCount = loadout.Count;
        bool found = false;
        int slot = 0;
        if (weaponCount > 0)
        {
            do
            {
                var type = loadout.GetWeaponType(slot);
                if (ObjectTypeTable.Get(type).ObjectClass == ObjectClass.Projectile)
                {
                    if (selectedGun == (short)type || selectedGun == 0x80)
                    {
                        loadout.SetDisabled(slot, 0);
                        found = true;
                    }
                    else
                    {
                        loadout.SetDisabled(slot, 1);
                    }
                }
                slot++;
                weaponCount--;
            }
            while (weaponCount != 0);
        }
        return found ? selectedGun : (short)-1;
    }

    /// <summary>Cycles the player's gun selection.</summary>
    /// <remarks>C: select_new_gun (0x42AE10, hudmsg.c).</remarks>
    public void SelectNewGun()
    {
        SelectedGunType = (ObjectType)SelectGuns(0, FindNextGun(0, SelectedGunType));
        Events.WeaponSelectionChanged();
    }

    /// <summary>
    /// Selects the player's next release weapon (missile/mine slot): with no current selection the
    /// first slot of <paramref name="preferredType"/>, else the first non-gun slot; otherwise the
    /// next non-gun slot of a different type after the current one (wrapping).
    /// </summary>
    /// <remarks>C: select_new_release_weapon (0x42AE50, hudmsg.c).</remarks>
    public void SelectNewReleaseWeapon(ObjectType preferredType)
    {
        ref var loadout = ref Ships[ObjectSlots.Player].Weapons;
        sbyte weaponCount = loadout.Count;
        int currentWeapon = SelectedReleaseWeaponIndex;
        sbyte weapon = unchecked((sbyte)(currentWeapon + 1));
        if (weaponCount <= weapon)
            weapon = 0;
        if (currentWeapon == -1)
        {
            if (preferredType != ObjectType.None)
            {
                weapon = 0;
                if (weaponCount > 0)
                {
                    for (; weapon < loadout.Count; weapon++)
                    {
                        if (loadout.GetWeaponType(weapon) == preferredType)
                        {
                            currentWeapon = weapon;
                            loadout.SetDisabled(currentWeapon, 0);
                            break;
                        }
                    }
                }
            }
            SelectedReleaseWeaponIndex = currentWeapon;
            if (currentWeapon == -1)
            {
                weapon = 0;
                if (weaponCount > 0)
                {
                    for (; weapon < weaponCount; weapon++)
                    {
                        if (ObjectTypeTable.Get(loadout.GetWeaponType(weapon)).ObjectClass != ObjectClass.Projectile)
                        {
                            currentWeapon = weapon;
                            SelectedReleaseWeaponIndex = currentWeapon;
                            loadout.SetDisabled(currentWeapon, 0);
                            break;
                        }
                    }
                }
            }
        }
        else
        {
            sbyte firstWeapon = weapon;
            do
            {
                if (currentWeapon == weapon)
                    break;
                var type = loadout.GetWeaponType(weapon);
                if (ObjectTypeTable.Get(type).ObjectClass != ObjectClass.Projectile &&
                    loadout.GetWeaponType(currentWeapon) != type)
                {
                    loadout.SetDisabled(currentWeapon, 1);
                    currentWeapon = weapon;
                    SelectedReleaseWeaponIndex = currentWeapon;
                    loadout.SetDisabled(currentWeapon, 0);
                    break;
                }
                weapon++;
                if (weaponCount <= weapon)
                    weapon = 0;
            }
            while (weapon != firstWeapon);
        }
        Events.WeaponSelectionChanged();
    }
}
