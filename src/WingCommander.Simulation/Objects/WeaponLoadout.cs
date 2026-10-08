using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using WingCommander.Simulation.Data;

namespace WingCommander.Simulation.Objects;

/// <summary>
/// One ship's weapon loadout, byte-exact with the original 0x47-byte record: byte 0 is the
/// signed slot count, then up to ten packed 7-byte <c>ShipWeaponSlot</c> records
/// <c>{int type; short hardpoint; sbyte disabled}</c>. Kept as raw bytes because the original
/// copies, shifts and patches it with <c>memcpy</c>-style byte operations.
/// </summary>
/// <remarks>C: aShipWeapons[10][0x47] (globals.h), ObjectTypeData.weaponLoadout, ShipWeaponSlot (wcdata.h).</remarks>
[InlineArray(Size)]
public struct WeaponLoadout
{
    /// <summary>Record size in bytes (0x47).</summary>
    public const int Size = 0x47;

    /// <summary>Bytes per weapon slot.</summary>
    public const int SlotSize = 7;

    /// <summary>Slots that fit into the record.</summary>
    public const int MaxSlots = 10;

    private byte _element0;

    /// <summary>Number of weapons (signed byte at offset 0).</summary>
    public sbyte Count
    {
        readonly get => unchecked((sbyte)this[0]);
        set => this[0] = unchecked((byte)value);
    }

    /// <summary>Weapon object type of slot <paramref name="slot"/> (32-bit little-endian at 1 + 7*slot).</summary>
    public readonly ObjectType GetWeaponType(int slot)
    {
        ReadOnlySpan<byte> bytes = this;
        return (ObjectType)BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(1 + slot * SlotSize, 4));
    }

    public void SetWeaponType(int slot, ObjectType type)
    {
        Span<byte> bytes = this;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(1 + slot * SlotSize, 4), (int)type);
    }

    /// <summary>Hardpoint (index into the child offset table) of a slot.</summary>
    public readonly short GetHardpoint(int slot)
    {
        ReadOnlySpan<byte> bytes = this;
        return BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(5 + slot * SlotSize, 2));
    }

    public void SetHardpoint(int slot, short hardpoint)
    {
        Span<byte> bytes = this;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.Slice(5 + slot * SlotSize, 2), hardpoint);
    }

    /// <summary>The slot's <c>disabled</c> byte: 0 = selected/armed, 1 = not selected.</summary>
    public readonly sbyte GetDisabled(int slot) => unchecked((sbyte)this[7 + slot * SlotSize]);

    public void SetDisabled(int slot, sbyte disabled) => this[7 + slot * SlotSize] = unchecked((byte)disabled);

    /// <summary>Copies the 7-byte record of slot <paramref name="from"/> over slot <paramref name="to"/>.</summary>
    public void CopySlot(int from, int to)
    {
        Span<byte> bytes = this;
        bytes.Slice(1 + from * SlotSize, SlotSize).CopyTo(bytes.Slice(1 + to * SlotSize, SlotSize));
    }

    /// <summary>
    /// Builds a loadout record like the C initializers in <c>aObjectTypeData</c>: count byte,
    /// packed slots, remaining bytes zero.
    /// </summary>
    public static WeaponLoadout Create(params ReadOnlySpan<(ObjectType Type, short Hardpoint, sbyte Disabled)> slots)
    {
        if (slots.Length > MaxSlots)
            throw new ArgumentOutOfRangeException(nameof(slots), "A loadout holds at most ten weapons.");
        var loadout = default(WeaponLoadout);
        loadout.Count = (sbyte)slots.Length;
        for (int i = 0; i < slots.Length; i++)
        {
            loadout.SetWeaponType(i, slots[i].Type);
            loadout.SetHardpoint(i, slots[i].Hardpoint);
            loadout.SetDisabled(i, slots[i].Disabled);
        }
        return loadout;
    }
}
