using WingCommander.Core.Resources;
using WingCommander.Game.Flow;
using WingCommander.Graphics.Palettes;
using WingCommander.Graphics.Shapes;
using WingCommander.Simulation;
using WingCommander.Simulation.Data;
using WingCommander.Simulation.Geometry;
using WingCommander.Simulation.Objects;

namespace WingCommander.Game.Flight;

// The 3D halves of the campaign endings (screen.c ShowCampaignVictorySequence and
// ShowTigerClawEscapeScene); the captions, music, presents and Esc handling are the Game's
// SceneDirector.Endings.
internal sealed partial class FlightSession
{
    /// <remarks>C: asCampaignVictoryViewScript[24] (0x0046C160).</remarks>
    private static readonly short[] CampaignVictoryViewScript =
        [16, 38, 0, 1200, 0, 1600, 1, 180, 0, 0, 15, 3, 4, 14, 100, 2, 15, 3, 15, 14, 120, -1, 0, 0];

    /// <remarks>C: asTigerClawEscapeViewScript[12] (0x0046C238).</remarks>
    private static readonly short[] TigerClawEscapeViewScript = [0, 0, 0, 0, 2, 15, 3, 15, 14, 400, -1, 0];

    /// <summary>Projectile origins {x, y, flip} on the planet (8.8 of the planet scale).</summary>
    /// <remarks>C: aCampaignVictoryProjectileOrigins[4] (0x0046ADB0).</remarks>
    private static readonly (short X, short Y, short Flip)[] CampaignVictoryProjectileOrigins =
        [(-55, 42, 0), (-68, 46, 0), (60, 42, 16), (73, 46, 16)];

    /// <summary>Starts the 3D part of a campaign ending; null for other action spheres.</summary>
    public ICannedSpaceScene? BeginCannedScene(int actionSphere) => actionSphere switch
    {
        CannedScene.CampaignVictory => new CampaignVictoryScene(this),
        CannedScene.TigerClawEscape => new TigerClawEscapeScene(this),
        _ => null,
    };

    /// <summary>
    /// The renderer half of <c>init_3Space_objects</c> (the simulation keeps the rest): a fresh 3D
    /// space forces the next <c>initialize_cockpit</c> to redraw and ends a view script.
    /// </summary>
    /// <remarks>C: init_3Space_objects (0x424A80, logic.c): cScreenViewportMode = -1, bScriptedView = 0.</remarks>
    private void PrepareFresh3Space()
    {
        if (Sim.Space3DObjectsActive)
            return;
        ScreenViewportMode = -1;
        Sim.ScriptedView = false;
    }

    /// <remarks>C: init_3Space_objects (0x424A80, logic.c).</remarks>
    public void Init3SpaceObjects(short scene)
    {
        PrepareFresh3Space();
        Sim.Init3SpaceObjects(scene);
    }

    /// <summary>init_mission with the renderer half of its init_3Space_objects.</summary>
    /// <remarks>C: init_mission (0x40B730, brains.c).</remarks>
    public bool InitMission(short series, short mission)
    {
        PrepareFresh3Space();
        return Sim.InitMission(series, mission);
    }

    /// <summary>The common start of both endings: 3D space, canned AI, no letterbox presents, the action sphere.</summary>
    private void BeginEndingScene(short scene, short actionSphere)
    {
        var sim = Sim;
        PrepareCampaignData(trainingSimulator: false);
        Init3SpaceObjects(scene);
        sim.CannedSceneMode = 2;
        IntroSceneResourcesActive = false;
        sim.SetUpActionSphere(actionSphere);
    }

    /// <summary>The common end of both endings.</summary>
    private void EndEndingScene()
    {
        CancelSpaceSpriteFrame();
        var sim = Sim;
        sim.FreeAllSlots();
        sim.Free3Space();
        sim.ScriptedView = false;
        sim.CannedSceneMode = 0;
        IntroSceneResourcesActive = true;
    }

    /// <summary>
    /// The Tiger's Claw's final attack: the scripted camera over action sphere 0x12, a planet
    /// object, then the attacked planet (TITLE.VGA 3) approaching with projectile pairs (TITLE.VGA 2)
    /// fired at it.
    /// </summary>
    /// <remarks>C: the 3D part of ShowCampaignVictorySequence (0x42FC00, screen.c). The original
    /// read two uninitialised locals at the first projectile spawn (the spawn countdown and the
    /// planet scale); the port starts the countdown at 8, as after a spawn, so the first pair uses
    /// the computed planet scale.</remarks>
    private sealed class CampaignVictoryScene : ICannedSpaceScene
    {
        private sealed class Projectile
        {
            public int X;
            public int Y;
            public int Depth;
            public short ScreenX;
            public short ScreenY;
            public short Flip;
            public int Scale = -1;
        }

        private readonly FlightSession _session;
        private readonly ShapeTable? _planetShape;
        private readonly ShapeTable? _projectileShape;
        private readonly short _planetObject;
        private readonly Projectile[] _projectiles = new Projectile[16];
        private readonly short[] _vacant = new short[2];
        private short _frame;
        private short _spawnCountdown = 8;
        private int _planetScale;
        private int _verticalOffset = -70000;
        private int _planetDepth = -1500;
        private bool _disposed;

        public CampaignVictoryScene(FlightSession session)
        {
            _session = session;
            var sim = session.Sim;
            session.BeginEndingScene(0, 0x12);
            _planetShape = session.Shapes.Get(LogicalFile.TitleVga, 3);
            _projectileShape = session.Shapes.Get(LogicalFile.TitleVga, 2);
            _planetObject = sim.CreateCannedSceneObject(-4, 30000, sim.FetchShape(LogicalFile.TitleVga, 3), 0, 0, 0x50);
            sim.ScriptedViewObject = 1;
            sim.InitializeScriptedView(CampaignVictoryViewScript);
            for (int i = 0; i < _projectiles.Length; i++)
                _projectiles[i] = new Projectile();
            sim.FrameSkipCounter = 1;
        }

        public bool Step()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var session = _session;
            var sim = session.Sim;
            sim.Update3Space();
            bool drawn = session.Draw3SpaceFrame();
            if (drawn)
            {
                if (_frame > 90 && _planetObject >= 0)
                    sim.Objects[_planetObject].ScreenScale++;
                if (sim.Objects[ObjectSlots.Eye].CollisionRadius < _planetDepth)
                {
                    DrawProjectiles(session, sim);
                    if (_frame < 170 && --_spawnCountdown < 1)
                        SpawnProjectiles(sim);
                    _planetScale = 0x40000 / _planetDepth;
                    session.DrawCannedSprite(sim.ViewCenterX, (short)(sim.ViewCenterY + _verticalOffset / _planetDepth),
                        _planetShape, 0, (short)_planetScale, 0);
                    _verticalOffset += 200;
                }
                session.DumpBufferToScreen();
                session.ClearViewBuffer();
            }
            _planetDepth += 15;
            _frame++;
            return drawn;
        }

        private void DrawProjectiles(FlightSession session, SpaceSimulation sim)
        {
            short eyeRadius = sim.Objects[ObjectSlots.Eye].CollisionRadius;
            foreach (var projectile in _projectiles)
            {
                if (projectile.Scale == -1 || eyeRadius >= projectile.Depth)
                    continue;
                projectile.ScreenX = (short)(projectile.X / projectile.Depth);
                projectile.ScreenY = (short)(projectile.Y / projectile.Depth);
                projectile.Scale = 0x10000 / projectile.Depth;
                if (projectile.Scale < 16)
                {
                    projectile.Scale = -1;
                    continue;
                }
                session.DrawCannedSprite((short)(projectile.ScreenX + sim.ViewCenterX),
                    (short)(projectile.ScreenY + sim.ViewCenterY), _projectileShape, 1, (short)projectile.Scale,
                    projectile.Flip);
                projectile.Depth += 100;
                projectile.Y += 4000;
            }
        }

        private void SpawnProjectiles(SpaceSimulation sim)
        {
            int vacantCount = 0;
            for (short slot = 0; slot < _projectiles.Length; slot++)
            {
                if (_projectiles[slot].Scale != -1)
                    continue;
                _vacant[vacantCount++] = slot;
                if (vacantCount == 2)
                    break;
            }
            if (vacantCount > 1)
            {
                var origin = CampaignVictoryProjectileOrigins[sim.Random.BelowOrEqual(3)];
                var first = _projectiles[_vacant[0]];
                first.Depth = _planetDepth;
                first.X = ((origin.X * _planetScale) >> 8) * _planetDepth;
                first.Y = ((origin.Y * _planetScale) >> 8) * first.Depth + _verticalOffset;
                first.Scale = 0x100;
                first.Flip = origin.Flip;
                first.Depth += 40;

                var second = _projectiles[_vacant[1]];
                second.Depth = _planetDepth;
                second.X = (((origin.X - 4) * _planetScale) >> 8) * _planetDepth;
                second.Y = ((origin.Y * _planetScale) >> 8) * second.Depth + _verticalOffset;
                second.Scale = 0x100;
                second.Flip = origin.Flip;
                second.Depth += 40;
            }
            _spawnCountdown = 8;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _session.EndEndingScene();
        }
    }

    /// <summary>
    /// The Tiger's Claw's escape: the scripted camera over action sphere 0x13 with the fleeing
    /// carrier (TITLE.VGA 2) receding, the hyperspace flash at frame 190 and the white frame at 198.
    /// </summary>
    /// <remarks>C: the 3D part of ShowTigerClawEscapeScene (0x430150, screen.c).</remarks>
    private sealed class TigerClawEscapeScene : ICannedSpaceScene
    {
        private readonly FlightSession _session;
        private readonly ShapeTable? _escapeShape;
        private short _frame;
        private short _approachStep = 15;
        private int _depth = -1000;
        private int _verticalOffset = -70000;
        private bool _disposed;

        public TigerClawEscapeScene(FlightSession session)
        {
            _session = session;
            var sim = session.Sim;
            session.BeginEndingScene(session.Game.Session.State.CurrentSeries, 0x13);
            _escapeShape = session.Shapes.Get(LogicalFile.TitleVga, 2);
            ref var flash = ref sim.TypeResources[(int)ObjectType.HyperspaceJumpFlash];
            if (flash.ShapeSet.IsNone)
                flash.ShapeSet = sim.FetchShape(LogicalFile.ObjectsVga, 14);
            sim.ScriptedViewObject = 1;
            sim.InitializeScriptedView(TigerClawEscapeViewScript);
            sim.FrameSkipCounter = 1;
        }

        public bool Step()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var session = _session;
            var sim = session.Sim;
            sim.Update3Space();
            bool drawn = session.Draw3SpaceFrame();
            short eyeRadius = sim.Objects[ObjectSlots.Eye].CollisionRadius;
            if (drawn)
            {
                if (eyeRadius < _depth && _frame < 198)
                {
                    session.DrawCannedSprite(sim.ViewCenterX, (short)(sim.ViewCenterY + _verticalOffset / _depth),
                        _escapeShape, 0, (short)(0x40000 / _depth), 0);
                }
                session.DumpBufferToScreen();
                session.ClearViewBuffer();
            }
            if (eyeRadius < _depth)
                _verticalOffset += 400;
            _depth += _approachStep;
            if (_frame > 170)
                _approachStep += 10;

            switch (_frame)
            {
                case 190:
                    short effect = sim.FindVacant3dObject();
                    if (effect != -1)
                    {
                        sim.SetObjectsData(effect, ObjectType.HyperspaceJumpFlash, -1);
                        ref var eye = ref sim.Objects[ObjectSlots.Eye];
                        var jumpOffset = VectorMath.Scale(eye.Forward, 0x271000);
                        ref var o = ref sim.Objects[effect];
                        o.Scale = (short)(o.Scale << 2);
                        o.Velocity = default;
                        o.Position = VectorMath.Add(eye.Position, jumpOffset);
                    }
                    break;
                case 198:
                    session.Gfx.ClearViewport(session.SpaceBuffer, PaletteColours.ViewportClear);
                    break;
            }
            _frame++;
            return drawn;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            var sim = _session.Sim;
            sim.TypeResources[(int)ObjectType.HyperspaceJumpFlash].ShapeSet = ShapeRef.None;
            _session.EndEndingScene();
        }
    }

    /// <summary>A 2D sprite of a canned scene drawn into the space buffer (recorded for R2 when active).</summary>
    private void DrawCannedSprite(short x, short y, ShapeTable? shape, int frame, short scale, short flip)
    {
        if (!TryRecordSpaceSprite(shape, frame, x, y, 0, scale, flip, -1))
            Gfx.DrawSpriteScaled(SpaceBuffer, x, y, shape, frame, 0, scale, flip);
    }
}
