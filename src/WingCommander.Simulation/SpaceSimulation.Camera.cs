using WingCommander.Core.Numerics;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Simulation;

// The eye (object 61): camera views (spc.c SetFleetOverviewView .. new_view), the scripted camera
// of the cinematics (music.c parse_view_script .. initialize_scripted_view), the dust specks and
// stars around the eye (start_dust, generate_stars, update_star_field).
public sealed partial class SpaceSimulation
{
    /// <summary>Class of slot <paramref name="obj"/>, <see cref="ObjectClass.Null"/> outside the table.</summary>
    private ObjectClass ClassOf(int obj) => (uint)obj < ObjectSlots.Count ? Objects[obj].Class : ObjectClass.Null;

    /// <summary>Whether <see cref="ViewObject"/> names a slot (the original indexed with it unchecked).</summary>
    private bool ViewObjectValid => (uint)ViewObject < ObjectSlots.Count;

    /// <summary>
    /// Camera 14: a fixed eye far off the centre of all ships (or 400 units off the player when he is
    /// alone) looking at the centre, or at the player when the ships are spread over 10000 units.
    /// </summary>
    /// <remarks>C: SetFleetOverviewView (0x410740, spc.c). The original chooses cockpit mode 4 when
    /// less than 66000 bytes of memory are free; every port reports more, so mode 6 is used.</remarks>
    public void SetFleetOverviewView(bool initializeCockpit)
    {
        var orientation = new FixedVector(0xff, 0xff, 0xff);
        int shipCount = 0;
        ref var eye = ref Objects[ObjectSlots.Eye];
        if (initializeCockpit)
        {
            Events.InitializeCockpitView(6);
            eye.Velocity = FixedVector.Zero;
        }
        for (int obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
        {
            if (Objects[obj].Class >= ObjectClass.Ship)
                shipCount++;
        }

        FixedVector centre;
        int maximumRange;
        int playerRange;
        if (shipCount > 1)
        {
            centre = FixedVector.Zero;
            for (int obj = 0; obj < ObjectSlots.ShipSlotCount; obj++)
            {
                if (Objects[obj].Class < ObjectClass.Ship)
                    continue;
                ref readonly var position = ref Objects[obj].Position;
                centre.X += position.X / shipCount;
                centre.Y += position.Y / shipCount;
                centre.Z += position.Z / shipCount;
            }
            maximumRange = 0x4b000;
            playerRange = 0xff;
            for (int obj = ObjectSlots.LastShip; obj >= 0; obj--)
            {
                if (Objects[obj].Class < ObjectClass.Ship)
                    continue;
                int range = VectorMath.Delta(centre, Objects[obj].Position).Magnitude();
                if (maximumRange < range)
                    maximumRange = range;
                if (obj == 0)
                    playerRange = range;
            }
            if (maximumRange <= 0x1f4000)
                playerRange = maximumRange;
        }
        else
        {
            maximumRange = 0x4b000;
            playerRange = 0x4b000;
            centre = PositionRelativeIjk(0, 400, 400, 400);
        }

        int cameraDistance = (playerRange >> 3) * 9 + 0x2bc00;
        ref var scratch = ref Objects[ObjectSlots.Scratch];
        scratch.Position = centre;
        scratch.Right = orientation;
        scratch.Up = orientation;
        scratch.PointAt(Objects[ObjectSlots.Player].Position);
        scratch.Position = VectorMath.Add(scratch.Position, VectorMath.Scale(scratch.Right, cameraDistance >> 2));
        scratch.Position = VectorMath.Add(scratch.Position, VectorMath.Scale(scratch.Up, 0x9600));
        scratch.Position = VectorMath.Add(scratch.Position, VectorMath.Scale(scratch.Forward, cameraDistance));

        eye.Position = scratch.Position;
        eye.Right = orientation;
        eye.Up = orientation;
        if (maximumRange < 0x271000)
            eye.PointAt(centre);
        else
            eye.PointAt(Objects[ObjectSlots.Player].Position);
    }

    /// <summary>Steers the eye's rotation rates toward the eye goals.</summary>
    /// <remarks>C: rotate_eye_to_goal (0x410A30, spc.c).</remarks>
    public void RotateEyeToGoal()
    {
        ref var eye = ref Objects[ObjectSlots.Eye];
        short totalError = unchecked((short)(
            System.Math.Abs(eye.PitchRotation - EyePitchGoal) +
            System.Math.Abs(eye.YawRotation - EyeYawGoal) +
            System.Math.Abs(eye.RollRotation - EyeRollGoal)));
        MatchRotationGoal(ref eye.PitchRotation, ref EyePitchGoal, totalError, EyePitchRate);
        MatchRotationGoal(ref eye.YawRotation, ref EyeYawGoal, totalError, EyeYawRate);
        MatchRotationGoal(ref eye.RollRotation, ref EyeRollGoal, totalError, EyeRollRate);
    }

    /// <summary>Length of a vector in whole units (saturated).</summary>
    /// <remarks>C: GetVectorMagnitude (0x410AD0, spc.c).</remarks>
    public static short GetVectorMagnitude(in FixedVector vector) => FixedMath.ToShortSaturating(vector.Magnitude());

    /// <summary>
    /// Moves the eye for the current camera view: cockpit views copy the player's frame (with axis
    /// swaps for left/right/rear), the chase camera lags behind its object, the missile camera
    /// follows the tracked missile and falls back to the cockpit 20 frames after it is gone, the
    /// target view looks past the player at the target, and so on (16 views, see
    /// docs/analysis/simulation.md §3.6). A running view script is advanced first.
    /// </summary>
    /// <remarks>C: set_eye_direction_and_position (0x410AF0, spc.c). The viewport switching of the
    /// cockpitless variants is presentation (Game); a view object outside the table counts as empty.</remarks>
    public void SetEyeDirectionAndPosition()
    {
        if (ScriptedView)
            UpdateScriptedView();

        ref var eye = ref Objects[ObjectSlots.Eye];
        ref readonly var player = ref Objects[ObjectSlots.Player];
        FixedVector vector;
        switch (CameraViewMode)
        {
            case 0:
                eye.CopyFrameFrom(player);
                eye.Velocity = player.Velocity;
                eye.Position = player.Position;
                return;
            case 1:
                eye.Right = VectorMath.Negate(player.Forward);
                eye.Up = player.Up;
                eye.Forward = player.Right;
                eye.Velocity = player.Velocity;
                eye.Position = player.Position;
                return;
            case 2:
                eye.Right = player.Forward;
                eye.Up = player.Up;
                eye.Forward = VectorMath.Negate(player.Right);
                eye.Velocity = player.Velocity;
                eye.Position = player.Position;
                return;
            case 3:
            case 9:
                eye.CopyFrameFrom(player);
                eye.Right = VectorMath.Negate(eye.Right);
                eye.Forward = VectorMath.Negate(eye.Forward);
                eye.Velocity = player.Velocity;
                eye.Position = player.Position;
                return;
            case 4:
                ChaseView();
                eye.Position = VectorMath.Add(eye.Position, eye.Velocity);
                return;
            case 5:
            {
                if (!ViewObjectValid)
                    return;
                ref readonly var viewObject = ref Objects[ViewObject];
                eye.Velocity = FixedVector.Zero;
                vector = VectorMath.Delta(eye.Position, viewObject.Position);
                if (vector.Magnitude() < 0x7d001)
                {
                    eye.Forward = vector;
                    eye.FixIjk();
                    return;
                }
                vector = viewObject.Velocity;
                VectorMath.Normalize(ref vector);
                PlaceStrafeView(viewObject, vector);
                GenerateStars();
                return;
            }
            case 6:
                MissileView();
                eye.Position = VectorMath.Add(eye.Position, eye.Velocity);
                return;
            case 7:
            {
                sbyte target = Ships[ObjectSlots.Player].Target;
                if (target != -1)
                {
                    vector = VectorMath.Scale(player.Right, 0x12c00);
                    eye.Position = VectorMath.Add(player.Position, vector);
                    vector = VectorMath.Delta(eye.Position, Objects[target].Position);
                    VectorMath.Normalize(ref vector);
                    eye.Forward = vector;
                    vector = VectorMath.Scale(vector, -0x25800);
                    eye.Position = VectorMath.Add(eye.Position, vector);
                    eye.FixIjk();
                    return;
                }
                if (CockpitlessView != 0)
                {
                    CockpitlessView = 0;
                    NewView(0, 0);
                    CockpitlessView = 1;
                    return;
                }
                NewView(0, 0);
                return;
            }
            case 8:
                if (!ViewObjectValid)
                    return;
                vector = VectorMath.Scale(eye.Forward, CapitalShipViewDistance);
                eye.Position = VectorMath.Subtract(Objects[ViewObject].Position, vector);
                if (eye.Velocity.Magnitude() != 0)
                    eye.Position = VectorMath.Add(eye.Position, eye.Velocity);
                return;
            case 10:
            {
                if (!ViewObjectValid)
                    return;
                ref readonly var viewObject = ref Objects[ViewObject];
                eye.CopyFrameFrom(viewObject);
                eye.Velocity = viewObject.Velocity;
                eye.Position = viewObject.Position;
                return;
            }
            case 11:
            {
                if (!ViewObjectValid)
                    return;
                vector = VectorMath.Delta(eye.Position, Objects[ViewObject].Position);
                if (vector.Magnitude() < 0x25800)
                {
                    var adjustment = vector;
                    VectorMath.Normalize(ref adjustment);
                    adjustment = VectorMath.Scale(adjustment, -0x25800);
                    adjustment = VectorMath.Add(vector, adjustment);
                    eye.Position = VectorMath.Add(eye.Position, adjustment);
                }
                eye.Forward = vector;
                eye.FixIjk();
                eye.Position = VectorMath.Add(eye.Position, eye.Velocity);
                return;
            }
            case 12:
                vector = VectorMath.Scale(eye.Right, -0xa00);
                eye.Position = VectorMath.Add(eye.Position, vector);
                eye.Position = VectorMath.Add(eye.Position, eye.Velocity);
                LookAt(0);
                return;
            case 13:
                eye.Position = VectorMath.Add(eye.Position, eye.Velocity);
                LookAt(0);
                return;
            case 14:
                SetFleetOverviewView(false);
                return;
            case 15:
                eye.Position = VectorMath.Add(eye.Position, eye.Velocity);
                RotateObject(ObjectSlots.Eye);
                RotateEyeToGoal();
                return;
        }
    }

    /// <summary>Camera 4: lag 700 (alternate: 500) units behind the view object along the player's
    /// forward axis, closing 1/25 (1/7) of the gap per frame, rolling with the object.</summary>
    private void ChaseView()
    {
        ref var eye = ref Objects[ObjectSlots.Eye];
        if (ClassOf(ViewObject) == ObjectClass.Null)
            return;
        ref readonly var viewObject = ref Objects[ViewObject];
        var viewDirection = VectorMath.Delta(eye.Position, viewObject.Position);
        var desiredPosition = Objects[ObjectSlots.Player].Forward;
        VectorMath.SetLength(ref desiredPosition, AlternateChaseView ? (short)-500 : (short)-700);
        desiredPosition = VectorMath.Add(desiredPosition, viewObject.Position);
        var positionDelta = VectorMath.Delta(eye.Position, desiredPosition);
        eye.Velocity = VectorMath.Divide(positionDelta, (AlternateChaseView ? 7 : 25) << 8);
        eye.Forward = viewDirection;
        VectorMath.ShrinkVector(ref eye.Forward);
        eye.FixIjk();
        EyeRollGoal = MatchRollOrientation(ObjectSlots.Eye, ViewObject);
        if (EyeRollGoal == 0)
            return;
        if (System.Math.Abs((int)EyeRollGoal) < 5)
        {
            eye.Up = Objects[ObjectSlots.Player].Up;
            EyeRollGoal = 0;
        }
        else
        {
            EyeRollRate = 4;
            RotateEyeToGoal();
            RotateObject(ObjectSlots.Eye);
        }
    }

    /// <summary>Camera 6: follows the missile camera's missile (closing in fast while it is near,
    /// slowly otherwise); 20 frames after the missile is gone the cockpit view returns.</summary>
    private void MissileView()
    {
        ref var eye = ref Objects[ObjectSlots.Eye];
        if (ExternalViewShip == -1)
        {
            eye.Velocity = FixedVector.Zero;
            if (ExternalViewAngle++ > 20)
                NewView(0, 0);
            return;
        }
        var vector = VectorMath.Delta(eye.Position, Objects[ExternalViewShip].Position);
        if (Ships[ExternalViewShip].Tactic != ShipTactic.Cruise)
        {
            if (vector.Magnitude() < 0xfa01)
            {
                var adjustment = vector;
                VectorMath.Normalize(ref adjustment);
                adjustment = VectorMath.Scale(adjustment, -64000);
                eye.Velocity = VectorMath.Add(vector, adjustment);
            }
            else
            {
                eye.Velocity = VectorMath.Divide(vector, (short)(ExternalViewDistance & 0xfffe) << 7);
                ExternalViewDistance = ScalarMath.MaxShort(unchecked((short)(ExternalViewDistance - 1)), 8);
            }
        }
        eye.Forward = vector;
        eye.FixIjk();
    }

    /// <summary>The fixed strafe view of camera 5: 293 units back along <paramref name="direction"/>,
    /// 400 to the right and 100 up, looking at the object.</summary>
    private void PlaceStrafeView(in SpaceObject viewObject, FixedVector direction)
    {
        ref var eye = ref Objects[ObjectSlots.Eye];
        eye.Up = viewObject.Up;
        if (VectorMath.AreEqual(direction, eye.Up))
            eye.Up = viewObject.Right;
        direction = VectorMath.Scale(direction, -0x12430);
        eye.Position = VectorMath.Add(viewObject.Position, direction);
        eye.Forward = direction;
        eye.FixIjk();
        eye.Position = VectorMath.Add(eye.Position, VectorMath.Scale(eye.Right, 0x19000));
        eye.Position = VectorMath.Add(eye.Position, VectorMath.Scale(eye.Up, 0x6400));
        eye.Forward = VectorMath.Delta(eye.Position, viewObject.Position);
        eye.FixIjk();
    }

    /// <summary>Switches to camera <paramref name="view"/> even when it is the current one.</summary>
    /// <remarks>C: force_view (0x4117B0, spc.c).</remarks>
    public void ForceView(int view, short obj)
    {
        CameraViewMode = -1;
        NewView(view, obj);
    }

    /// <summary>
    /// Switches the camera to <paramref name="view"/> looking at <paramref name="obj"/>: re-selecting
    /// the chase view toggles its distance; the missile camera needs a tracked missile; the chase
    /// camera of a capital ship becomes view 8. Sets up the cockpit picture, places the eye, runs the
    /// first camera step and regenerates the stars.
    /// </summary>
    /// <remarks>C: new_view (0x4117D0, spc.c).</remarks>
    public void NewView(int view, short obj)
    {
        if (CameraViewMode == view)
        {
            if (view == 4)
                AlternateChaseView = !AlternateChaseView;
            return;
        }
        if (view == 6 && ExternalViewShip == -1)
            return;

        ViewObject = unchecked((sbyte)obj);
        if (view == 4 && ClassOf(ViewObject) == ObjectClass.CapitalShip)
            view = 8;
        CameraViewMode = view;
        ref var eye = ref Objects[ObjectSlots.Eye];
        eye.CollisionRadius = obj != -1 ? ScalarMath.MaxShort(10, Objects[obj].CollisionRadius) : (short)10;
        FixedVector vector;
        switch (view)
        {
            case 0:
            case 1:
            case 2:
            case 3:
                Events.InitializeCockpitView(view);
                break;
            case 4:
                Events.InitializeCockpitView(4);
                if (!ScriptedView && ViewObjectValid)
                {
                    ref readonly var viewObject = ref Objects[ViewObject];
                    vector = VectorMath.Scale(viewObject.Forward, -1200 * 0x100);
                    eye.Position = VectorMath.Add(viewObject.Position, vector);
                    eye.Up = viewObject.Up;
                    eye.Forward = vector;
                    eye.FixIjk();
                    eye.Velocity = FixedVector.Zero;
                }
                break;
            case 5:
            {
                Events.InitializeCockpitView(4);
                if (!ViewObjectValid)
                    break;
                ref readonly var viewObject = ref Objects[ViewObject];
                vector = viewObject.Velocity;
                if (!VectorMath.Normalize(ref vector))
                    vector = viewObject.Forward;
                PlaceStrafeView(viewObject, vector);
                eye.Velocity = FixedVector.Zero;
                break;
            }
            case 6:
            {
                Events.InitializeCockpitView(4);
                ref readonly var missile = ref Objects[ExternalViewShip];
                vector = VectorMath.Scale(missile.Right, 0x25800);
                eye.Position = VectorMath.Add(missile.Position, vector);
                vector = VectorMath.Delta(eye.Position, missile.Position);
                eye.Up = Objects[ObjectSlots.Player].Up;
                eye.Forward = vector;
                eye.FixIjk();
                eye.Velocity = FixedVector.Zero;
                ExternalViewDistance = 0x20;
                ExternalViewAngle = 0;
                break;
            }
            case 7:
                Events.InitializeCockpitView(4);
                CopyFrame(0, ObjectSlots.Eye);
                eye.Velocity = FixedVector.Zero;
                break;
            case 8:
            {
                Events.InitializeCockpitView(4);
                if (!ViewObjectValid)
                    break;
                ref readonly var viewObject = ref Objects[ViewObject];
                eye.Right = viewObject.Right;
                eye.Up = VectorMath.Negate(viewObject.Forward);
                eye.Forward = viewObject.Up;
                eye.FixIjk();
                eye.Velocity = FixedVector.Zero;
                break;
            }
            case 9:
                Events.InitializeCockpitView(6);
                break;
            case 10:
                Events.InitializeCockpitView(7);
                break;
            case 11:
            case 15:
                Events.InitializeCockpitView(4);
                EyePitchGoal = 0;
                EyeYawGoal = 0;
                EyeRollGoal = 0;
                EyePitchRate = 1;
                EyeYawRate = 1;
                EyeRollRate = 1;
                break;
            case 12:
                Events.InitializeCockpitView(4);
                CopyFrame(0, ObjectSlots.Eye);
                eye.Position = PositionRelativeIjk(0, 500, 0, 2000);
                eye.Velocity = FixedVector.Zero;
                LookAt(0);
                break;
            case 13:
            {
                Events.InitializeCockpitView(4);
                Objects[ObjectSlots.Player].Velocity = FixedVector.Zero;
                if (YourWingman != -1)
                    Objects[YourWingman].Velocity = FixedVector.Zero;
                if (ViewObjectValid)
                    CopyFrame(ViewObject, ObjectSlots.Eye);
                short carrier = FindShipIndex(CarrierMissionShipIndex);
                if (carrier != -1)
                    LookAt(carrier);
                eye.Position = PositionRelativeIjk(ObjectSlots.Eye, 0, -10, -400);
                eye.Velocity = VectorMath.Scale(eye.Forward, -0x2300);
                break;
            }
            case 14:
                SetFleetOverviewView(true);
                break;
        }

        if (ScriptedView)
        {
            ScriptedView = false;
            SetEyeDirectionAndPosition();
            ScriptedView = true;
        }
        else
        {
            SetEyeDirectionAndPosition();
        }
        GenerateStars();
    }

    /// <summary>Starts view script <paramref name="script"/> at <paramref name="start"/>: the eye
    /// (radius 100) is reset and the first block of commands is parsed.</summary>
    /// <remarks>C: initialize_scripted_view (0x42D230, music.c). The Game passes the compiled-in
    /// scripts (ejection, intro camera, victory, Tiger's Claw escape, carrier launch).</remarks>
    public void InitializeScriptedView(short[] script, int start = 0)
    {
        ArgumentNullException.ThrowIfNull(script);
        ScriptedView = true;
        ref var eye = ref Objects[ObjectSlots.Eye];
        eye.Velocity = FixedVector.Zero;
        eye.InitIjk();
        _viewScript = script;
        _viewScriptPosition = start;
        ParseViewScript();
        eye.CollisionRadius = 100;
    }

    /// <summary>The script word at <paramref name="position"/>; -1 (end of script) outside it.</summary>
    private short ViewScriptAt(int position) =>
        (uint)position < (uint)_viewScript.Length ? _viewScript[position] : (short)-1;

    private short NextViewScriptWord() => ViewScriptAt(_viewScriptPosition++);

    /// <summary>
    /// Executes view-script commands up to the next wait (13: until the eye goals are met, 14: for
    /// the following number of frames) or the end (-1): 0 place, 1 turn, 2 speed, 3 camera view,
    /// 4..8 eye goals and rates, 9 add a turned velocity, 10/11/12 copy velocity/frame/position of
    /// the script object, 15 look at it, 16 select it by mission record.
    /// </summary>
    /// <remarks>C: parse_view_script (0x42CDB0, music.c). References to a script object outside the
    /// table are skipped (the original read outside the object arrays).</remarks>
    public void ParseViewScript()
    {
        ref var eye = ref Objects[ObjectSlots.Eye];
        if (ViewScriptAt(_viewScriptPosition) == 13)
            return;
        while (ViewScriptAt(_viewScriptPosition) != 14)
        {
            short command = NextViewScriptWord();
            if (command == -1)
            {
                ScriptedView = false;
                ScriptedViewObject = -1;
                return;
            }
            bool objectValid = (uint)ScriptedViewObject < ObjectSlots.Count;
            switch (command)
            {
                case 0:
                    eye.Position.X = NextViewScriptWord() * 0x100;
                    eye.Position.Y = NextViewScriptWord() * 0x100;
                    eye.Position.Z = NextViewScriptWord() * 0x100;
                    break;
                case 1:
                    eye.AlterYaw(NextViewScriptWord());
                    eye.AlterPitch(NextViewScriptWord());
                    eye.AlterRoll(NextViewScriptWord());
                    break;
                case 2:
                    eye.Velocity = VectorMath.Scale(eye.Forward, NextViewScriptWord() * 0x100);
                    break;
                case 3:
                    ForceView(NextViewScriptWord(), ScriptedViewObject);
                    break;
                case 4:
                    EyePitchGoal = unchecked((short)-NextViewScriptWord());
                    EyePitchRate = NextViewScriptWord();
                    break;
                case 5:
                    EyePitchGoal = NextViewScriptWord();
                    EyePitchRate = NextViewScriptWord();
                    break;
                case 6:
                    EyeYawGoal = NextViewScriptWord();
                    EyeYawRate = NextViewScriptWord();
                    break;
                case 7:
                    EyeYawGoal = unchecked((short)-NextViewScriptWord());
                    EyeYawRate = NextViewScriptWord();
                    break;
                case 8:
                    EyeRollGoal = NextViewScriptWord();
                    EyeRollRate = NextViewScriptWord();
                    break;
                case 9:
                {
                    CopyFrame(ObjectSlots.Eye, ObjectSlots.Scratch);
                    ref var scratch = ref Objects[ObjectSlots.Scratch];
                    scratch.AlterYaw(NextViewScriptWord());
                    scratch.AlterPitch(NextViewScriptWord());
                    scratch.AlterRoll(NextViewScriptWord());
                    var vector = VectorMath.Scale(scratch.Forward, NextViewScriptWord() * 0x100);
                    eye.Velocity = VectorMath.Add(eye.Velocity, vector);
                    break;
                }
                case 10:
                    if (objectValid)
                        eye.Velocity = Objects[ScriptedViewObject].Velocity;
                    break;
                case 11:
                    if (objectValid)
                        CopyFrame(ScriptedViewObject, ObjectSlots.Eye);
                    break;
                case 12:
                    if (objectValid)
                        eye.Position = Objects[ScriptedViewObject].Position;
                    break;
                case 15:
                    if (objectValid)
                    {
                        eye.Forward = VectorMath.Delta(eye.Position, Objects[ScriptedViewObject].Position);
                        eye.FixIjk();
                    }
                    break;
                case 16:
                {
                    short obj = 0;
                    short record = ViewScriptAt(_viewScriptPosition);
                    while (obj < ObjectSlots.ShipSlotCount)
                    {
                        if (Ships[obj].MissionIndex == record)
                            break;
                        obj++;
                    }
                    if (obj < ObjectSlots.ShipSlotCount)
                        ScriptedViewObject = obj;
                    _viewScriptPosition++;
                    break;
                }
            }
            if (ViewScriptAt(_viewScriptPosition) == 13)
                return;
        }
        eye.Counter = ViewScriptAt(_viewScriptPosition + 1);
    }

    /// <summary>Advances a waiting view script: 13 waits until the eye goals compare as in the
    /// original (<c>(yawGoal == pitchGoal) != rollGoal</c>), 14 counts the eye's counter down.</summary>
    /// <remarks>C: update_scripted_view (0x42D1C0, music.c).</remarks>
    public void UpdateScriptedView()
    {
        switch (ViewScriptAt(_viewScriptPosition))
        {
            case 13:
                if ((EyeYawGoal == EyePitchGoal ? 1 : 0) != EyeRollGoal)
                {
                    _viewScriptPosition++;
                    ParseViewScript();
                }
                break;
            case 14:
            {
                ref var eye = ref Objects[ObjectSlots.Eye];
                short counter = eye.Counter;
                eye.Counter = unchecked((short)(eye.Counter - 1));
                if (counter < 1)
                {
                    _viewScriptPosition += 2;
                    ParseViewScript();
                }
                break;
            }
        }
    }

    /// <summary>Places dust speck <paramref name="obj"/> <paramref name="forwardDistance"/> units ahead
    /// of the eye, offset sideways and up, drifting slowly, a streak in one case of four.</summary>
    /// <remarks>C: start_dust (0x411EC0, spc.c).</remarks>
    public void StartDust(short obj, FixedVector origin, short forwardDistance, int rightOffset, int upOffset)
    {
        SetObjectsData(obj, ObjectType.SpaceDust, -1);
        ref readonly var eye = ref Objects[ObjectSlots.Eye];
        origin = VectorMath.Add(origin, VectorMath.Scale(eye.Forward, forwardDistance << 8));
        origin = VectorMath.Add(origin, VectorMath.Scale(eye.Right, rightOffset));
        origin = VectorMath.Add(origin, VectorMath.Scale(eye.Up, upOffset));
        ref var dust = ref Objects[obj];
        dust.Position = VectorMath.Add(origin, eye.Position);
        dust.Velocity = RandomVectors.FillFixedVectorWithRandomComponents(Random, 2);
        short streak = ScalarMath.MaxShort(unchecked((short)(1 - Random.InRange(0, 3))), 0);
        dust.ScreenAngle = unchecked((short)(streak * 0x10 + Random.InRange(0, 3)));
    }

    /// <summary>Scatters the eight dust specks (34..41) up to 1400 units ahead of the eye and places
    /// the seven stars (42..48) 15000 units out within ±45° of the view axis.</summary>
    /// <remarks>C: generate_stars (0x411FE0, spc.c).</remarks>
    public void GenerateStars()
    {
        var origin = FixedVector.Zero;
        for (short obj = ObjectSlots.FirstDust; obj <= ObjectSlots.LastStar; obj++)
        {
            if (obj < ObjectSlots.DustEnd)
            {
                short distance = Random.InRange(0, 1400);
                // MSVC evaluates call arguments right to left: the original draws the up offset of
                // start_dust(obj, origin, distance, signed_random(d) << 8, signed_random(d) << 8) first.
                int upOffset = Random.Signed(distance) << 8;
                int rightOffset = Random.Signed(distance) << 8;
                StartDust(obj, origin, distance, rightOffset, upOffset);
            }
            else
            {
                Objects[obj].Class = ObjectClass.Star;
                MarkSpawned(obj); // port addition
                StarFieldIRotation = Random.Signed(45);
                StarFieldJRotation = Random.Signed(45);
                CopyFrame(ObjectSlots.Eye, ObjectSlots.Scratch);
                ref var scratch = ref Objects[ObjectSlots.Scratch];
                VectorMath.RotateAboutJ(StarFieldJRotation, ref scratch.Right, ref scratch.Forward);
                VectorMath.RotateAboutI(StarFieldIRotation, ref scratch.Up, ref scratch.Forward);
                Objects[obj].Position = VectorMath.Scale(scratch.Forward, 15000 << 8);
                Objects[obj].ViewFrame = (short)(Random.InRange(0, 5) + 32);
            }
        }
    }

    /// <summary>
    /// Once per prepared view frame: the first off-screen star or dust speck of 34..48 that wins a
    /// 1/8 roll (dust: 1/4) is placed again ahead of the moving or turning eye; with an active hazard
    /// field dust slots are freed for hazards and the hazards are managed.
    /// </summary>
    /// <remarks>C: update_star_field (0x412100, spc.c). Its <c>class == 0x21</c> test compares the class
    /// with OBJECT_TYPE_SPACE_MINE and is never true.</remarks>
    public void UpdateStarField()
    {
        bool hazardActive = ActiveHazardField != -1;
        ref readonly var eye = ref Objects[ObjectSlots.Eye];
        PreviousStarFieldMotion = StarFieldMotion;
        var cameraMotion = VectorMath.Scale(eye.Forward, 200 << 8);
        StarFieldMotion = VectorMath.Scale(eye.Velocity, 20 << 8);
        StarFieldMotion = VectorMath.Add(cameraMotion, StarFieldMotion);
        var origin = VectorMath.Delta(PreviousStarFieldMotion, StarFieldMotion);

        ref readonly var player = ref Objects[ObjectSlots.Player];
        for (short obj = ObjectSlots.FirstDust; obj <= ObjectSlots.LastStar; obj++)
        {
            ref var o = ref Objects[obj];
            if (o.ScreenX != ObjectSlots.NotVisible)
                continue;
            short randomChoice = Random.InRange(0, 7);
            if (!hazardActive)
            {
                if (o.Class == ObjectClass.Asteroid || (int)o.Class == 0x21 || o.Class == ObjectClass.Null)
                {
                    SetObjectsData(obj, ObjectType.SpaceDust, -1);
                    randomChoice = 0;
                }
            }
            else if (obj < ObjectSlots.DustEnd)
            {
                ExtraHazard(obj);
            }

            if (o.Class == ObjectClass.Star && randomChoice == 0 && (player.YawRotation | player.PitchRotation) != 0)
            {
                CopyFrame(ObjectSlots.Eye, ObjectSlots.Scratch);
                if (player.PitchRotation != 0)
                {
                    StarFieldIRotation = player.PitchRotation < 0 ? (short)-45 : (short)45;
                    StarFieldJRotation = Random.Signed(45);
                }
                if (player.YawRotation != 0 && (player.PitchRotation == 0 || Random.InRange(0, 1) != 0))
                {
                    StarFieldJRotation = player.YawRotation < 0 ? (short)-45 : (short)45;
                    StarFieldIRotation = Random.Signed(45);
                }
                ref var scratch = ref Objects[ObjectSlots.Scratch];
                VectorMath.RotateAboutI(StarFieldIRotation, ref scratch.Up, ref scratch.Forward);
                VectorMath.RotateAboutJ(StarFieldJRotation, ref scratch.Right, ref scratch.Forward);
                o.Position = VectorMath.Scale(scratch.Forward, 15000 << 8);
                o.ViewFrame = (short)(Random.InRange(0, 5) + 32);
                MarkSpawned(obj); // port addition: a new star
                break;
            }

            if (o.Class == ObjectClass.Dust && randomChoice < 2)
            {
                var viewMotion = eye.TransformToObjectsFrame(eye.Velocity);
                viewMotion = VectorMath.Scale(viewMotion, 10 << 8);
                int distance;
                if (viewMotion.Z >= 0)
                {
                    distance = (ushort)Random.InRange(0, (short)(viewMotion.Z >> 8)) + eye.CollisionRadius;
                    distance = distance * 2 + (ushort)Random.InRange(0, 350);
                }
                else
                {
                    distance = (ushort)Random.InRange(0, 40) + eye.CollisionRadius;
                }
                short rightRandom = Random.Signed((short)(distance >> 1));
                short upRandom = Random.Signed((short)(distance >> 1));
                int shift = viewMotion.Z <= 0 ? 9 : 8;
                int rightOffset = viewMotion.X + rightRandom * (1 << shift);
                int upOffset = viewMotion.Y + upRandom * (1 << shift);
                StartDust(obj, origin, unchecked((short)distance), rightOffset, upOffset);
                break;
            }
        }
        if (ActiveHazardField != -1)
            UpdateHazards();
    }
}
