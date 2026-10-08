namespace WingCommander.Simulation.Objects;

/// <summary>
/// The mutable graphics pointers of one object type record: the sprite set, the loaded
/// animation packet (ships/missiles: the exhaust table, section 2 of the ship file) and the
/// target-VDU silhouette. Effect animation scripts are compiled in and not stored here.
/// </summary>
/// <remarks>C: ObjectTypeData.shapeSet (+0x7F), .animation (+0x1C, when loaded from disk), .shape (+0x83).</remarks>
public struct ObjectTypeResources
{
    /// <summary>Sprite set; <see cref="ShapeRef.None"/> means "not loaded" (set_objects_data substitutes).</summary>
    public ShapeRef ShapeSet;

    /// <summary>Loaded exhaust/animation table of ships and missiles.</summary>
    public ShapeRef Animation;

    /// <summary>Target VDU silhouette.</summary>
    public ShapeRef Shape;

    public static ObjectTypeResources Empty => new() { ShapeSet = ShapeRef.None, Animation = ShapeRef.None, Shape = ShapeRef.None };
}
