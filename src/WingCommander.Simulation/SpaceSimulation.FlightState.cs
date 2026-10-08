using WingCommander.Core.Numerics;

namespace WingCommander.Simulation;

// Flight globals added with phase 2: frame pacing, space view geometry, camera and scripted view,
// star field, target lock display state, arcade score and the presentation-side query interfaces.
public sealed partial class SpaceSimulation
{
    /// <summary>Cockpit state queries (VDU modes); <see cref="DefaultCockpitState"/> without a Game.</summary>
    public ICockpitState Cockpit { get; set; } = new DefaultCockpitState();

    /// <summary>Sprite bounds of the space view (<c>easy2see</c>); <see cref="PointShapeBounds"/> without a Game.</summary>
    public IShapeBounds ShapeBounds { get; set; } = new PointShapeBounds();

    // ------------------------------------------------------------------ frame pacing

    /// <summary>nFrameSkip: the space view is prepared every n-th simulation frame (1..5, ctrl +/-).</summary>
    public short FrameSkip = 1;

    /// <summary>nFrameSkipCounter: counts down to the next prepared view frame.</summary>
    public short FrameSkipCounter = 1;

    // ------------------------------------------------------------------ space view geometry

    /// <summary>nScreenWidth: width of the space viewport in pixels (the projection's focal length is half of it).</summary>
    public short ScreenWidth = 320;

    /// <summary>nScreenHeight: height of the space viewport.</summary>
    public short ScreenHeight = 200;

    /// <summary>nViewCenterX: screen x of the view axis (set by the Game from the viewport geometry;
    /// 160 without a Game).</summary>
    public short ViewCenterX = 160;

    /// <summary>nViewCenterY: screen y of the view axis (100 without a Game).</summary>
    public short ViewCenterY = 100;

    /// <summary>bCockpitlessView: 0 cockpit, 1 full-screen view without cockpit, -2 temporarily
    /// during autopilot. Gameplay-visible: guns aim higher in the cockpitless rear view.</summary>
    public int CockpitlessView;

    // ------------------------------------------------------------------ camera

    /// <summary>nEyePitchGoal / nEyeYawGoal / nEyeRollGoal: rotation goals of the eye (object 61).</summary>
    public short EyePitchGoal;

    public short EyeYawGoal;

    public short EyeRollGoal;

    /// <summary>nEyePitchRate / nEyeYawRate / nEyeRollRate.</summary>
    public short EyePitchRate = 1;

    public short EyeYawRate = 1;

    public short EyeRollRate = 1;

    /// <summary>bAlternateChaseView: the close chase camera (500 instead of 700 units).</summary>
    public bool AlternateChaseView;

    /// <summary>nExternalViewAngle: frames since the tracked missile disappeared (missile camera).</summary>
    public short ExternalViewAngle;

    /// <summary>nExternalViewDistance: missile camera lag divisor.</summary>
    public short ExternalViewDistance;

    /// <summary>nCapitalShipViewDistance: distance of the capital ship chase camera (2000 units).</summary>
    public int CapitalShipViewDistance = 0x7d000;

    /// <summary>bScriptedView: the eye follows a view script (cinematics).</summary>
    public bool ScriptedView;

    /// <summary>nScriptedViewObject: object the view script refers to.</summary>
    public short ScriptedViewObject = -1;

    /// <summary>pViewScript: the running view script and the read position in it.</summary>
    private short[] _viewScript = [];

    private int _viewScriptPosition;

    // ------------------------------------------------------------------ star field

    /// <summary>nStarFieldIRotation / nStarFieldJRotation: angles used to place the last star.</summary>
    public short StarFieldIRotation;

    public short StarFieldJRotation;

    /// <summary>vStarFieldMotion / vPreviousStarFieldMotion.</summary>
    public FixedVector StarFieldMotion;

    public FixedVector PreviousStarFieldMotion;

    // ------------------------------------------------------------------ target lock display

    /// <summary>nTargetLockMarkerAngle: start angle of the lock marker spiral (random).</summary>
    public short TargetLockMarkerAngle;

    /// <summary>bTargetLockReadoutDirty: the lock readout must be redrawn.</summary>
    public bool TargetLockReadoutDirty;

    // ------------------------------------------------------------------ misc

    /// <summary>nArcadeScore: training simulator score (kills add ten times their mission score).</summary>
    public int ArcadeScore;

    /// <summary>
    /// Landing rule at the Tiger's Claw. True (default, SDL port): within 700 units and facing the
    /// Claw (&gt; 75 %). False (Kilrathi Saga original): the Claw's bow must also face the player
    /// (<c>nTargetFacing &gt; 70</c>).
    /// </summary>
    /// <remarks>C: the landing test of house_keep_objects (0x412430, spc.c), SDL_PORT branch.</remarks>
    public bool LandFromAnyBearing = true;
}
