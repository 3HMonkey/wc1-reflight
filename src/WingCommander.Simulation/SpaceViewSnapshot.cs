using WingCommander.Core.Numerics;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

/// <summary>
/// A renderer-facing copy of the space view, filled by <see cref="SpaceSimulation.CaptureSpaceView"/>
/// after a prepared view frame (data only, no rendering). A modern renderer keeps the last two
/// snapshots and interpolates positions and bases by <c>RenderFrame.Interpolation</c>; the sprite
/// fields reproduce the original's 2D look (draw in <see cref="DrawOrder"/>, far to near). Reused
/// between frames: capture allocates nothing.
/// </summary>
public sealed class SpaceViewSnapshot
{
    /// <summary>Object slots that can appear (0..60; 61..63 are the eye and scratch frames).</summary>
    public const int MaxObjects = ObjectSlots.LastMoving + 1;

    /// <summary>nSpaceFrame of the captured state (20 Hz simulation frame counter).</summary>
    public short SpaceFrame;

    /// <summary>nRenderedSpaceFrame: increments once per prepared view frame.</summary>
    public short RenderedSpaceFrame;

    /// <summary>Camera view mode (0 cockpit front, 1 left, 2 right, 3 rear, 4 chase, ... 15 scripted).</summary>
    public int CameraViewMode;

    /// <summary>Cockpit graphics set (player ship type 0..3, 4 training simulator) and cockpitless flag.</summary>
    public sbyte CockpitView;

    public int CockpitlessView;

    /// <summary>The eye (object 61): world position, velocity per frame and orientation basis.</summary>
    public FixedVector CameraPosition;

    public FixedVector CameraVelocity;

    public FixedVector CameraRight;

    public FixedVector CameraUp;

    public FixedVector CameraForward;

    /// <summary>Near-plane distance in units (the eye's collision radius).</summary>
    public short CameraNearRadius;

    /// <summary>The original space viewport: width (the focal length is half of it), height, centre.</summary>
    public short ScreenWidth;

    public short ScreenHeight;

    public short ViewCenterX;

    public short ViewCenterY;

    /// <summary>Stars, dust specks (and planets in the DOS draw list) use frames of this shape.</summary>
    public ShapeRef ConstellationShape;

    /// <summary>The player's target slot (-1 none), missile lock countdown (-1 off, 0 locked) and the
    /// lock marker's start angle (HUD).</summary>
    public sbyte PlayerTarget;

    public short TargetLockCountdown;

    public short TargetLockMarkerAngle;

    /// <summary>Number of valid entries in <see cref="Objects"/> (every non-empty slot 0..60, in slot order).</summary>
    public int ObjectCount;

    /// <summary>The captured objects; entries beyond <see cref="ObjectCount"/> are stale.</summary>
    public SpaceObjectView[] Objects { get; } = new SpaceObjectView[MaxObjects];

    /// <summary>Number of valid entries in <see cref="DrawOrder"/>.</summary>
    public int DrawCount;

    /// <summary>The original painter's order (slots, far to near; the first entry can be an invisible object).</summary>
    public short[] DrawOrder { get; } = new short[ObjectSlots.Count];

    /// <summary>The captured objects.</summary>
    public ReadOnlySpan<SpaceObjectView> ActiveObjects => Objects.AsSpan(0, ObjectCount);
}
