using System.Buffers.Binary;
using WingCommander.Core.Resources;

namespace WingCommander.Game.Scenes;

/// <summary>
/// One object of a MIDGAME scene (0x36-byte record in the definitions section). The record is
/// mutable runtime state: scripts change position, rotation, scale and frame, and the 'R'
/// opcode copies the current values of another record.
/// </summary>
/// <remarks>C: SceneAnimationObject (wcdata.h). Pointer fields become offsets into the definitions section.</remarks>
public sealed class SceneAnimationObject
{
    public const int Size = 0x36;

    /// <summary>0 primary shape set, tiled horizontally; 1 secondary shape set; 2 secondary but not drawn.</summary>
    public short Layer;
    public short ScriptOffset;
    public int ScriptStart;
    public int ScriptCursor;
    public int RepeatCursor;

    /// <summary>Goal bits: 1 rotation, 2 scale, 4 x, 8 y, 0x10 frame.</summary>
    public ushort GoalFlags;
    public short Delay;
    public short X, Y, Rotation, Scale, Frame;
    public short DeltaX, DeltaY, DeltaRotation, DeltaScale, DeltaFrame;
    public short GoalX, GoalY, GoalRotation, GoalScale, GoalFrame;

    public static SceneAnimationObject Read(ReadOnlySpan<byte> d) => new()
    {
        Layer = BinaryPrimitives.ReadInt16LittleEndian(d),
        ScriptOffset = BinaryPrimitives.ReadInt16LittleEndian(d[0x02..]),
        GoalFlags = BinaryPrimitives.ReadUInt16LittleEndian(d[0x10..]),
        Delay = BinaryPrimitives.ReadInt16LittleEndian(d[0x12..]),
        X = BinaryPrimitives.ReadInt16LittleEndian(d[0x18..]),
        Y = BinaryPrimitives.ReadInt16LittleEndian(d[0x1a..]),
        Rotation = BinaryPrimitives.ReadInt16LittleEndian(d[0x1c..]),
        Scale = BinaryPrimitives.ReadInt16LittleEndian(d[0x1e..]),
        Frame = BinaryPrimitives.ReadInt16LittleEndian(d[0x20..]),
        DeltaX = BinaryPrimitives.ReadInt16LittleEndian(d[0x22..]),
        DeltaY = BinaryPrimitives.ReadInt16LittleEndian(d[0x24..]),
        DeltaRotation = BinaryPrimitives.ReadInt16LittleEndian(d[0x26..]),
        DeltaScale = BinaryPrimitives.ReadInt16LittleEndian(d[0x28..]),
        DeltaFrame = BinaryPrimitives.ReadInt16LittleEndian(d[0x2a..]),
        GoalX = BinaryPrimitives.ReadInt16LittleEndian(d[0x2c..]),
        GoalY = BinaryPrimitives.ReadInt16LittleEndian(d[0x2e..]),
        GoalRotation = BinaryPrimitives.ReadInt16LittleEndian(d[0x30..]),
        GoalScale = BinaryPrimitives.ReadInt16LittleEndian(d[0x32..]),
        GoalFrame = BinaryPrimitives.ReadInt16LittleEndian(d[0x34..]),
    };
}

/// <summary>Draws one scene-animation sprite (implemented with the Graphics library by the screen code).</summary>
public interface ISceneAnimationRenderer
{
    /// <summary>
    /// C: DrawSpriteScaled(viewport, x, y, shape, frame, rotation, scale, flags) with the shape
    /// chosen by <paramref name="layer"/> (0 primary, otherwise secondary) and the object's
    /// <c>frame</c> field passed as the last argument.
    /// </summary>
    void Draw(int layer, int x, int y, int frame, int rotation, int scale, int flags);
}

/// <summary>
/// The scene-animation interpreter of the MIDGAME "Meanwhile..." cutscenes: definitions section
/// (u16 object count, scenes x count records, then the scripts) plus the per-tick bytecode
/// executor. Opcodes: A/L/Q property ops, X set-all, R copy from record, B label, G goto,
/// J jump-and-yield, D draw (repeat point), E draw-and-complete, P pause, W global wait, S skip.
/// </summary>
/// <remarks>C: LoadSceneAnimationResources, FindSceneAnimationCommand, SceneAnimationGoalReached,
/// UpdateSceneAnimationObject (logic.c 0x424D00-0x4254FF).</remarks>
public sealed class SceneAnimation
{
    private readonly byte[] _definitions;

    private SceneAnimation(byte[] definitions, short objectCount, SceneAnimationObject[] objects)
    {
        _definitions = definitions;
        ObjectCount = objectCount;
        Objects = objects;
    }

    /// <summary>Objects per scene.</summary>
    public short ObjectCount { get; }

    /// <summary>All records of all scenes (scene s, object o at s * ObjectCount + o).</summary>
    public SceneAnimationObject[] Objects { get; }

    /// <summary>Frames left of a global 'W' wait, -1 when none is active (nSceneAnimationWaitFrames).</summary>
    public short WaitFrames { get; set; } = -1;

    /// <summary>Set when any script executed 'W' (bSceneAnimationWaitCommand).</summary>
    public bool WaitCommandUsed { get; set; }

    public int SceneCount => ObjectCount == 0 ? 0 : Objects.Length / ObjectCount;

    /// <summary>MIDGAME.V00..V07 logical files (asSceneAnimationLogicalFiles).</summary>
    public static int LogicalFileFor(int scene) => LogicalFile.MidgameV00 + scene;

    /// <summary>Parses definitions (section 1 + variant). The record count is derived from the first script offset.</summary>
    public static SceneAnimation Parse(ReadOnlySpan<byte> definitions)
    {
        if (definitions.Length < 2)
            throw new GameDataException("Scene animation definitions are empty.");
        short count = BinaryPrimitives.ReadInt16LittleEndian(definitions);
        if (count <= 0)
            throw new GameDataException($"Scene animation definitions declare {count} objects.");
        // Records run from offset 2 up to the lowest script offset.
        int firstScript = definitions.Length;
        int records = 0;
        while (2 + (records + 1) * SceneAnimationObject.Size <= firstScript)
        {
            var r = definitions.Slice(2 + records * SceneAnimationObject.Size, SceneAnimationObject.Size);
            int scriptOffset = BinaryPrimitives.ReadInt16LittleEndian(r[2..]);
            if (scriptOffset > 0 && scriptOffset < firstScript)
                firstScript = scriptOffset;
            records++;
        }
        records -= records % count;
        var objects = new SceneAnimationObject[records];
        for (int i = 0; i < records; i++)
            objects[i] = SceneAnimationObject.Read(definitions.Slice(2 + i * SceneAnimationObject.Size, SceneAnimationObject.Size));
        return new SceneAnimation(definitions.ToArray(), count, objects);
    }

    /// <summary>Binds the objects of <paramref name="scene"/> to their scripts (PlaySceneAnimation's setup).</summary>
    public Span<SceneAnimationObject> BindScene(int scene)
    {
        var span = Objects.AsSpan(scene * ObjectCount, ObjectCount);
        foreach (var o in span)
        {
            o.ScriptStart = o.ScriptOffset;
            o.ScriptCursor = o.ScriptStart;
        }
        WaitFrames = -1;
        WaitCommandUsed = false;
        return span;
    }

    private sbyte Op(int position) => (uint)position < (uint)_definitions.Length ? unchecked((sbyte)_definitions[position]) : (sbyte)0;

    private short Word(int position) =>
        position + 1 < _definitions.Length ? BinaryPrimitives.ReadInt16LittleEndian(_definitions.AsSpan(position)) : (short)0;

    /// <summary>Returns the position of the next <paramref name="command"/> opcode at or after <paramref name="position"/>, or -1.</summary>
    /// <remarks>C: FindSceneAnimationCommand (0x424DE0).</remarks>
    public int FindCommand(int position, sbyte command)
    {
        int p = position;
        while (Op(p) != 0)
        {
            sbyte opcode = Op(p++);
            if (opcode == command)
                return p - 1;
            switch ((char)opcode)
            {
                case 'A':
                case 'L':
                case 'Q':
                    p += 3;
                    break;
                case 'B':
                case 'G':
                case 'J':
                case 'R':
                case 'W':
                    p += 2;
                    break;
                case 'D':
                    while (p < _definitions.Length && Op(p++) != -1)
                    {
                    }
                    break;
                case 'E':
                case 'P':
                case 'S':
                    p++;
                    break;
                case 'X':
                    p += 10;
                    break;
            }
        }
        return -1;
    }

    /// <remarks>C: SceneAnimationGoalReached (0x424EA0).</remarks>
    public static bool GoalReached(short delta, short current, short goal) =>
        delta < 0 ? current <= goal : delta > 0 ? current >= goal : current == goal;

    private static short WrapRotation(short rotation)
    {
        if (rotation < 0)
            rotation = unchecked((short)((ushort)(0x167 - rotation) / 0x168 * 0x168 + rotation));
        if (rotation > 0x167)
            rotation = unchecked((short)(rotation - (ushort)rotation / 0x168 * 0x168));
        return rotation;
    }

    private static short ClampScale(short scale) => scale < 0x40 ? (short)0x40 : scale > 0x1fff ? (short)0x1fff : scale;

    /// <summary>
    /// Executes one tick of an object's script. Drawing happens only when
    /// <paramref name="frameSkipCounter"/> &lt; 1 (nFrameSkipCounter). Returns true when the object
    /// completed (E opcode or a reached goal).
    /// </summary>
    /// <remarks>C: UpdateSceneAnimationObject (0x424EF0).</remarks>
    public bool Update(SceneAnimationObject o, int frameSkipCounter, ISceneAnimationRenderer? renderer)
    {
        bool complete = false;
        short delay = o.Delay;
        bool stop = false;
        int cursor = delay != 0 ? o.RepeatCursor : o.ScriptCursor;

        while (Op(cursor) != 0 && !stop)
        {
            char opcode = (char)Op(cursor++);
            switch (opcode)
            {
                case 'A':
                {
                    char property = (char)Op(cursor++);
                    short value = Word(cursor);
                    cursor += 2;
                    switch (property)
                    {
                        case 'F':
                            o.Frame = unchecked((short)(o.Frame + value));
                            o.DeltaFrame = value;
                            break;
                        case 'R':
                            o.Rotation = WrapRotation(unchecked((short)(o.Rotation + value)));
                            o.DeltaRotation = value;
                            break;
                        case 'S':
                            o.Scale = ClampScale(unchecked((short)(o.Scale + value)));
                            o.DeltaScale = value;
                            break;
                        case 'T':
                            delay = unchecked((short)(delay + value));
                            break;
                        case 'X':
                            o.X = unchecked((short)(o.X + value));
                            o.DeltaX = value;
                            break;
                        case 'Y':
                            o.Y = unchecked((short)(o.Y + value));
                            o.DeltaY = value;
                            break;
                    }
                    break;
                }
                case 'B':
                    cursor += 2;
                    break;
                case 'D':
                {
                    o.RepeatCursor = cursor - 1;
                    int xOffset = 0;
                    short frame = Op(cursor++);
                    while (frame != -1)
                    {
                        if (o.Layer != 2 && frameSkipCounter < 1)
                            renderer?.Draw(o.Layer, unchecked((short)(o.X + xOffset)), o.Y, frame, o.Rotation, o.Scale, o.Frame);
                        if (o.Layer == 0)
                            xOffset += 320;
                        frame = Op(cursor++);
                    }
                    stop = true;
                    break;
                }
                case 'E':
                {
                    int commandStart = cursor - 1;
                    int xOffset = 0;
                    complete = true;
                    short frame = Op(cursor++);
                    while (frame != -1)
                    {
                        if (o.Layer != 2 && frameSkipCounter < 1)
                            renderer?.Draw(o.Layer, unchecked((short)(o.X + xOffset)), o.Y, frame, o.Rotation, o.Scale, o.Frame);
                        xOffset += 320;
                        frame = Op(cursor++);
                    }
                    cursor = commandStart;
                    stop = true;
                    break;
                }
                case 'G':
                case 'J':
                {
                    if (opcode == 'J')
                        stop = true;
                    short labelNumber = Word(cursor);
                    int label = o.ScriptStart;
                    short value;
                    do
                    {
                        label = FindCommand(label, (sbyte)'B');
                        if (label < 0)
                            throw new GameDataException($"Scene animation label {labelNumber} not found.");
                        cursor = label + 3;
                        value = Word(label + 1);
                        label = cursor;
                    }
                    while (value != labelNumber);
                    break;
                }
                case 'L':
                {
                    char property = (char)Op(cursor++);
                    short value = Word(cursor);
                    cursor += 2;
                    switch (property)
                    {
                        case 'F':
                            o.Frame = value;
                            break;
                        case 'R':
                            o.Rotation = WrapRotation(value);
                            break;
                        case 'S':
                            o.Scale = ClampScale(value);
                            break;
                        case 'T':
                            delay = value;
                            break;
                        case 'X':
                            o.X = value;
                            break;
                        case 'Y':
                            o.Y = value;
                            break;
                    }
                    break;
                }
                case 'P':
                    stop = true;
                    break;
                case 'Q':
                {
                    char property = (char)Op(cursor++);
                    short value = Word(cursor);
                    cursor += 2;
                    switch (property)
                    {
                        case 'F':
                            o.GoalFlags |= 0x10;
                            o.GoalFrame = value;
                            break;
                        case 'R':
                            o.GoalFlags |= 1;
                            o.GoalRotation = value;
                            break;
                        case 'S':
                            o.GoalFlags |= 2;
                            o.GoalScale = value;
                            break;
                        case 'X':
                            o.GoalFlags |= 4;
                            o.GoalX = value;
                            break;
                        case 'Y':
                            o.GoalFlags |= 8;
                            o.GoalY = value;
                            break;
                    }
                    break;
                }
                case 'R':
                {
                    int index = unchecked((short)(ObjectCount * Op(cursor++)));
                    index = unchecked((short)(index + Op(cursor++)));
                    var source = Objects[index];
                    o.X = source.X;
                    o.Y = source.Y;
                    o.Rotation = source.Rotation;
                    o.Scale = source.Scale;
                    o.Frame = source.Frame;
                    break;
                }
                case 'W':
                    WaitFrames = Word(cursor);
                    cursor += 2;
                    WaitCommandUsed = true;
                    break;
                case 'X':
                    o.X = Word(cursor);
                    o.Y = Word(cursor + 2);
                    o.Rotation = Word(cursor + 4);
                    o.Scale = Word(cursor + 6);
                    o.Frame = Word(cursor + 8);
                    cursor += 10;
                    break;
            }
        }

        if (o.Delay != 0)
        {
            o.Delay--;
            return false;
        }

        o.ScriptCursor = cursor;
        ushort goalFlags = o.GoalFlags;
        o.Delay = delay;
        if (!complete && goalFlags != 0)
        {
            if ((goalFlags & 0x10) != 0)
                complete = GoalReached(o.DeltaFrame, o.Frame, o.GoalFrame);
            if ((goalFlags & 4) != 0)
                complete |= GoalReached(o.DeltaX, o.X, o.GoalX);
            if ((goalFlags & 8) != 0)
                complete |= GoalReached(o.DeltaY, o.Y, o.GoalY);
            if ((goalFlags & 2) != 0)
                complete |= GoalReached(o.DeltaScale, o.Scale, o.GoalScale);
            if ((goalFlags & 1) != 0)
                complete |= GoalReached(o.DeltaRotation, o.Rotation, o.GoalRotation);
        }
        return complete;
    }
}
