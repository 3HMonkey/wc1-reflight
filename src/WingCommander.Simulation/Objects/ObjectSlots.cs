namespace WingCommander.Simulation.Objects;

/// <summary>
/// The fixed slot layout of the 64-entry object table. Slot numbers are semantic and stored in
/// other fields (owner, target, leader), so the port keeps integer slot ids.
/// </summary>
/// <remarks>C: SPACE_OBJECT_COUNT, SPACE_LAST_MOVING_OBJECT, EYE_OBJECT (include/wcdata.h) and the
/// slot ranges used by get_ship_slot, find_vacant_3d_object, borrow_dust, generate_stars.</remarks>
public static class ObjectSlots
{
    /// <summary>Total slots (SPACE_OBJECT_COUNT).</summary>
    public const int Count = 64;

    /// <summary>The player's ship.</summary>
    public const int Player = 0;

    /// <summary>Ship slots 0..9 (player, ships, capital ships, missiles, futurions); the size of the ship-only state.</summary>
    public const int ShipSlotCount = 10;

    /// <summary>First slot handed out by <c>get_ship_slot</c>.</summary>
    public const int FirstShip = 1;

    /// <summary>Last slot handed out by <c>get_ship_slot</c>.</summary>
    public const int LastShip = 9;

    /// <summary>First effect slot (<c>find_vacant_3d_object</c> scans 10..60).</summary>
    public const int FirstEffect = 10;

    /// <summary>Last simulated slot (SPACE_LAST_MOVING_OBJECT).</summary>
    public const int LastMoving = 60;

    /// <summary>First dust streak slot (dust occupies 34..41).</summary>
    public const int FirstDust = 34;

    /// <summary>One past the last dust slot.</summary>
    public const int DustEnd = 42;

    /// <summary>First background star slot (42..48).</summary>
    public const int FirstStar = 42;

    /// <summary>Last background star slot.</summary>
    public const int LastStar = 48;

    /// <summary>The camera (EYE_OBJECT); its collision radius is the near plane.</summary>
    public const int Eye = 61;

    /// <summary>Copy of the player's frame at flight start.</summary>
    public const int SavedPlayerFrame = 62;

    /// <summary>Scratch frame (planet/star rotation, hazard aiming, mine intercept point).</summary>
    public const int Scratch = 63;

    /// <summary><c>asObjectScreenX</c> value for "not visible" (0x8001 = -32767).</summary>
    public const short NotVisible = unchecked((short)0x8001);
}
