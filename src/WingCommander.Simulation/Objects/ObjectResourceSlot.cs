using WingCommander.Simulation.Data;

namespace WingCommander.Simulation.Objects;

/// <summary>
/// One of four ship-shape cache entries: 0 player ship, 1..2 the current nav point's preload
/// types, 3 the heat-seeker (shared by all missiles).
/// </summary>
/// <remarks>C: ObjectResourceSlot (include/wcdata.h), aObjectResourceSlots[4] (0x0059ddf0).</remarks>
public struct ObjectResourceSlot
{
    /// <summary>Loaded type or <see cref="ObjectType.None"/> (stored as a signed byte in the original).</summary>
    public ObjectType Type;

    /// <summary>Archive section 0 (fighters/missiles) or the asteroid set.</summary>
    public ShapeRef ShapeSet;

    /// <summary>Archive section 2 (exhaust table).</summary>
    public ShapeRef Animation;

    /// <summary>Archive section 1 (fighters) or 0x25 (capital ships): the silhouette.</summary>
    public ShapeRef Shape;

    public static ObjectResourceSlot Empty => new()
    {
        Type = ObjectType.None,
        ShapeSet = ShapeRef.None,
        Animation = ShapeRef.None,
        Shape = ShapeRef.None,
    };
}
