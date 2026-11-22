using GrappleHook.Core;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using SENetworkAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace GrappleHook
{

    /// <summary>
    /// Everything a grapple block does, independent of what kind of weapon block it is
    /// bolted onto. GrappleTurret and GrappleFixedGun supply the block specific parts.
    /// This file: the block's life cycle, its states and the hook in flight. The rope is in
    /// WeaponControlLayer.Rope.cs, riding it in WeaponControlLayer.Zipline.cs.
    ///
    ///   idle        hook in the barrel, ready
    ///   projectile  hook in flight, paying out rope
    ///   attached    hook caught; the rope pulls, the winch works, players can ride it
    ///   retracting  hook let go (detach, a miss, its target gone) and reeled back in
    ///   reloading   counting down the weapon's reload time
    /// </summary>
    public abstract partial class WeaponControlLayer : MyGameLogicComponent
    {
        /// <summary>How far the muzzle has to move before the hook entity is moved with it.</summary>
        private const double MuzzleEpsilon = 0.001d;

        /// <summary>The idle hook's centre rests this far ahead of the muzzle.</summary>
        private const double HookRestOffset = 1.1d;

        /// <summary>Rope paid out beyond the distance to the target when the hook catches.</summary>
        private const double AttachSlack = 1.25d;

        private bool waitframe = true;

        protected IMyUserControllableGun Weapon;
        private IMyGunObject<MyGunBase> gun;

        private MyEntity Projectile;

        // retracting is last: the state is synced by its number
        public enum States { idle, reloading, projectile, attached, retracting }

        private NetSync<States> State;
        private NetSync<long> ConnectedEntityId;
        private IMyEntity ConnectedEntity = null;
        private NetSync<Vector3> LocalGrapplePosition;
        private NetSync<Vector3I> LocalGrapplePositionI;

        private NetSync<double> GrappleLength;

        private NetSync<ShootData> Shooting;
        private NetSync<bool> ResetIndicator;
        private NetSync<Settings> settings;
        internal NetSync<float> Winch;

        private NetSync<float> ReloadTime;

        private NetSync<ZiplineEntity> RequestZiplineActivation;
        private NetSync<ZiplineEntity> RequestZiplineDisconnect;
        private NetSync<List<ZiplineEntity>> ZiplinePlayers;

        /// <summary>The points the rope wraps around, worked out by the server.</summary>
        private NetSync<List<WrapPoint>> Wraps;

        /// <summary>The hook: where it is, which way it points.</summary>
        private MatrixD GrappleMatrix = MatrixD.Zero;

        /// <summary>The hook's velocity in flight, m/s, and the launcher's when it fired.</summary>
        private Vector3D hookVelocity;
        private Vector3D launchVelocity;

        /// <summary>Where the muzzle was last tick, so a retracting hook keeps up with a moving ship.</summary>
        private Vector3D lastMuzzle;
        private bool retractStarted;

        /// <summary>The caught hook's orientation in its target's frame, so it turns with it.</summary>
        private MatrixD hookLocal;
        private bool hookLocalSet;

        internal bool terminalShootOn = false;

        /// <summary>Where the hook is anchored, in world space. Recomputed once a frame.</summary>
        private Vector3D attachPoint = Vector3D.Zero;

        private MatrixD lastProjectileMatrix = MatrixD.Zero;

        /// <summary>A server with nothing to draw skips everything that only feeds the view.</summary>
        private bool isDedicated;
        private bool isServer;

        /// <summary>Ticks since the block loaded.</summary>
        private int frame;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            isServer = MyAPIGateway.Session.IsServer;

            if (isServer)
            {
                Settings.Init();

                settings = new NetSync<Settings>(this, TransferType.ServerToClient, Settings.Instance, true, false);
            }
            else
            {
                settings = new NetSync<Settings>(this, TransferType.ServerToClient, Settings.GetDefaults(), true, false);
            }

            State = new NetSync<States>(this, TransferType.ServerToClient, States.idle);

            ConnectedEntityId = new NetSync<long>(this, TransferType.ServerToClient, 0);
            ConnectedEntityId.ValueChangedByNetwork += AttemptConnect;

            LocalGrapplePosition = new NetSync<Vector3>(this, TransferType.ServerToClient, Vector3.Zero);
            LocalGrapplePositionI = new NetSync<Vector3I>(this, TransferType.ServerToClient, Vector3I.Zero);

            Shooting = new NetSync<ShootData>(this, TransferType.Both, new ShootData());
            Shooting.ValueChanged += Shoot;

            ResetIndicator = new NetSync<bool>(this, TransferType.ClientToServer, false);
            ResetIndicator.ValueChanged += Detach;

            GrappleLength = new NetSync<double>(this, TransferType.ServerToClient, 0);

            Winch = new NetSync<float>(this, TransferType.Both, 0);

            ReloadTime = new NetSync<float>(this, TransferType.ServerToClient, 0);

            ZiplinePlayers = new NetSync<List<ZiplineEntity>>(this, TransferType.ServerToClient, new List<ZiplineEntity>());

            RequestZiplineActivation = new NetSync<ZiplineEntity>(this, TransferType.ClientToServer, new ZiplineEntity(), false);
            RequestZiplineActivation.ValueChanged += ZiplineActivation;

            RequestZiplineDisconnect = new NetSync<ZiplineEntity>(this, TransferType.ClientToServer, new ZiplineEntity(), false);
            RequestZiplineDisconnect.ValueChanged += ZiplineDisconnect;

            Wraps = new NetSync<List<WrapPoint>>(this, TransferType.ServerToClient, new List<WrapPoint>());

            isDedicated = MyAPIGateway.Utilities.IsDedicated;

            gun = Entity as IMyGunObject<MyGunBase>;
            Weapon = Entity as IMyUserControllableGun;

            InitWeapon();

            // the hook model is decoration, so a dedicated server does not need one at all
            if (!isDedicated)
            {
                Projectile = new MyEntity();
                Projectile.Init(null, ModContext.ModPath + "\\Models\\Ammo\\GrappleHookAmmo.mwm", null, null);

                MyEntities.Add(Projectile, true);
            }

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        /// <summary>Block specific setup, run once the weapon interfaces are resolved.</summary>
        protected virtual void InitWeapon() { }

        /// <summary>
        /// Every frame, in every state: lets a block react to being fired as part of a cockpit's
        /// selected weapon group. Only the fixed launchers can be selected that way.
        /// </summary>
        protected virtual void UpdateWeaponGroup() { }

        /// <summary>The game's gun object behind the block (shooting state, ammo, muzzle).</summary>
        protected IMyGunObject<MyGunBase> Gun { get { return gun; } }

        protected bool IsAttached { get { return State.Value == States.attached; } }

        /// <summary>A server with no player: nothing there reads input or draws.</summary>
        protected bool IsDedicated { get { return isDedicated; } }

        private bool IsLargeGrid { get { return Weapon.CubeGrid.GridSizeEnum == MyCubeSize.Large; } }

        /// <summary>
        /// Builds this block class's terminal. Each block class needs its own registration -
        /// see TerminalControls.
        /// </summary>
        protected abstract void CreateTerminalControls();

        private void Detach(bool o, bool n)
        {
            Reset(true);
        }

        /// <summary>
        /// Lets go of whatever the hook holds. With <paramref name="retract"/> a hook that is out
        /// is reeled back in first; without it (the rope snapped) the hook is lost.
        /// </summary>
        private void Reset(bool retract)
        {
            if (retract && State.Value == States.retracting) return;

            bool hookOut = State.Value == States.projectile || State.Value == States.attached;

            if (State.Value == States.attached && ConnectedEntity != null)
            {
                UpdateAttachPoint();
                GrappleMatrix.Translation = attachPoint;
            }

            if (ConnectedEntity != null)
                ConnectedEntity.OnMarkForClose -= attachedEntityClosed;
            if (ConnectedEntity is IMyCubeGrid)
                (ConnectedEntity as IMyCubeGrid).OnBlockRemoved -= Grid_OnBlockRemoved;

            ConnectedEntity = null;
            ConnectedEntityId.Value = 0;
            LocalGrapplePosition.Value = Vector3.Zero;
            LocalGrapplePositionI.Value = Vector3I.Zero;
            tension = 0;

            if (Wraps.Value.Count > 0)
            {
                Wraps.Value.Clear();
                if (isServer) Wraps.Push();
            }

            // nobody rides a rope that has let go
            if (ZiplinePlayers.Value.Count > 0)
            {
                ZiplinePlayers.Value.Clear();
                if (isServer) ZiplinePlayers.Push();
            }

            if (retract && hookOut)
            {
                State.Value = States.retracting;
            }
            else
            {
                HideProjectile();
                HideRope();
                State.Value = States.reloading;
            }

            Tools.Debug($"Reset - State Change: {State}");
        }

        /// <summary>
        /// A client following the server's target: a new id replaces whatever the client
        /// caught itself, and 0 (the server let go) drops it.
        /// </summary>
        private void AttemptConnect(long o, long n, ulong steamId)
        {
            if (ConnectedEntity != null && ConnectedEntity.EntityId != n)
            {
                ConnectedEntity.OnMarkForClose -= attachedEntityClosed;
                if (ConnectedEntity is IMyCubeGrid)
                    (ConnectedEntity as IMyCubeGrid).OnBlockRemoved -= Grid_OnBlockRemoved;
                ConnectedEntity = null;
            }

            ValidateConnectedEntity();
        }

        private void ValidateConnectedEntity()
        {
            if (ConnectedEntityId.Value != 0 && ConnectedEntity == null)
            {
                ConnectedEntity = MyAPIGateway.Entities.GetEntityById(ConnectedEntityId.Value);
                if (ConnectedEntity != null)
                {
                    ConnectedEntity.OnMarkForClose += attachedEntityClosed;
                    massRefreshFrame = 0;
                }
            }
        }

        public override void MarkForClose()
        {
            if (Projectile != null)
            {
                Projectile.Close();
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            // one frame of grace so the block class has finished creating its own vanilla
            // controls before this mod goes looking for them
            if (waitframe)
            {
                waitframe = false;
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                return;
            }

            CreateTerminalControls();
        }

        public override void UpdateBeforeSimulation()
        {
            frame++;

            UpdateWeaponGroup();
            UpdateDirectControlInput();

            if (State.Value != States.retracting) retractStarted = false;

            switch (State.Value)
            {
                case States.idle:
                    HideRope();
                    MoveProjectileToMuzzle();
                    VerifyAndRequestShoot();
                    break;
                case States.reloading:
                    HideRope();
                    Reload();
                    break;
                case States.projectile:
                    UpdateProjectile();
                    break;
                case States.attached:
                    ValidateConnectedEntity();
                    UpdateAttachPoint();
                    BuildAnchors();
                    UpdateWrapping();
                    UpdateMasses();
                    UpdateGrappleLength();
                    ApplyForce();

                    // the rope snapped
                    if (State.Value != States.attached) break;

                    // a dedicated server only needs the rope's shape for riders
                    if (isDedicated && ZiplinePlayers.Value.Count > 0) UpdateRopePath();
                    UpdateZiplineInteract();
                    UpdateZiplineForces();
                    break;
                case States.retracting:
                    CountDownReload();
                    UpdateRetract();
                    break;
            }
        }

        /// <summary>
        /// After the physics step, so the rope is drawn where its ends are now rather than a
        /// frame behind them.
        /// </summary>
        public override void UpdateAfterSimulation()
        {
            if (isDedicated) return;

            States state = State.Value;
            if (state == States.attached && ConnectedEntity != null)
            {
                UpdateAttachPoint();
                FollowHook();
            }

            // a client that has not found the hook's target yet has no rope to show
            bool ropeOut = state == States.projectile || state == States.retracting || (state == States.attached && ConnectedEntity != null);
            if (!ropeOut)
            {
                HideRope();
            }
            else
            {
                BuildAnchors();

                if (ZiplinePlayers.Value.Count > 0 || RopeInView())
                {
                    UpdateRopePath();
                }
                else
                {
                    HideRope();
                }
            }

            Draw();
        }

        /// <summary>The settings this block is running with. Server values, synced to clients.</summary>
        internal Settings Config
        {
            get { return settings.Value; }
        }

        /// <summary>Lets go and reels the hook in. Routed through the server by the reset indicator.</summary>
        internal void RequestDetach()
        {
            ResetIndicator.Value = !ResetIndicator.Value;
        }

        /// <summary>
        /// Input while the local player controls this block directly - a turret taken over
        /// with Control - read through the game's controls, so it follows the player's key
        /// bindings, and ignored while a menu or the chat has the cursor. With the rope attached:
        ///   right click (secondary action)   detach. Vanilla turrets give it no use; zoom is
        ///                                    the scroll wheel.
        ///   W held (forward)                 pay rope out at the full loosen speed
        ///   S held (backward)                reel rope in at the full tighten speed
        /// Releasing W or S, or leaving the turret while holding one, stops the winch; a speed
        /// set on the terminal slider is left alone until W or S is pressed. Taken over from a
        /// cockpit, the game also passes W and S on to that ship's thrusters.
        /// </summary>
        private void UpdateDirectControlInput()
        {
            if (isDedicated) return;

            VRage.Game.ModAPI.Interfaces.IMyControllableEntity controlled = MyAPIGateway.Session.ControlledObject;
            bool inControl = controlled != null && controlled.Entity == Entity
                && !MyAPIGateway.Gui.IsCursorVisible && !MyAPIGateway.Gui.ChatEntryVisible
                && State.Value == States.attached;

            float winch = 0;
            if (inControl)
            {
                if (MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SECONDARY_TOOL_ACTION))
                {
                    Tools.Debug("Detach requested by the secondary action");
                    RequestDetach();
                }

                bool loosen = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.FORWARD);
                bool tighten = MyAPIGateway.Input.IsGameControlPressed(MyControlsSpace.BACKWARD);
                if (loosen != tighten)
                {
                    winch = loosen ? -Config.Winch.LoosenSpeed : Config.Winch.TightenSpeed;
                }
            }

            if (winch != 0)
            {
                winchFromKeys = true;
                if (Winch.Value != winch) Winch.Value = winch;
            }
            else if (winchFromKeys)
            {
                winchFromKeys = false;
                Winch.Value = 0;
            }
        }

        /// <summary>The winch speed was last set by W or S, so letting go of them stops it.</summary>
        private bool winchFromKeys;

        internal void VerifyAndRequestShoot(bool terminalShoot = false)
        {
            if (State.Value == States.idle && (Weapon.IsShooting || terminalShoot || terminalShootOn) && ReloadTime.Value <= 0)
            {
                Tools.Debug("Requesting Shoot");
                MatrixD muzzleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
                Vector3 direction = muzzleMatrix.Forward;
                Vector3D origin = Weapon.PositionComp.WorldMatrixRef.Translation;

                ShootData shoot = new ShootData();
                shoot.direction = direction;
                shoot.position = origin;

                Shooting.Value = shoot;
            }
        }

        private void Shoot(ShootData o, ShootData n)
        {
            if (State.Value != States.attached && State.Value != States.retracting && n.direction != Vector3.Zero)
            {
                GrappleMatrix = gun.GunBase.GetMuzzleWorldMatrix();

                // the hook leaves with the launcher's own velocity, as any projectile does
                MyPhysicsComponentBase physics = Weapon.CubeGrid.Physics;
                launchVelocity = physics != null ? (Vector3D)physics.GetVelocityAtPoint(GrappleMatrix.Translation) : Vector3D.Zero;
                hookVelocity = GrappleMatrix.Forward * Config.Hook.Speed + launchVelocity;

                hookLocalSet = false;
                HideRope();
                State.Value = States.projectile;
                ReloadTime.Value = (float)gun.GunBase.ReloadTime;
                Shooting.SetValue(new ShootData());
                Tools.Debug($"Shooting - State Change: {State.Value}");
            }
        }

        private Vector3D MuzzlePosition()
        {
            return gun.GunBase.GetMuzzleWorldMatrix().Translation;
        }

        private void MoveProjectileToMuzzle()
        {
            // the muzzle matrix is only wanted here to park the hook on it; the shot itself
            // reads it fresh
            if (isDedicated) return;

            GrappleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
            GrappleMatrix.Translation += GrappleMatrix.Forward * HookRestOffset;
            MoveProjectile();
        }

        private void HideProjectile()
        {
            GrappleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
            GrappleMatrix.Translation = Vector3D.MaxValue;
            MoveProjectile();
        }

        /// <summary>
        /// The hook entity is decoration - it has no physics and nothing reads its position -
        /// so it is only worth moving when it has visibly moved, and not at all on a server
        /// with nothing to draw it.
        /// </summary>
        private void MoveProjectile()
        {
            if (Projectile == null) return;

            if (!MatrixTools.HasMoved(ref GrappleMatrix, ref lastProjectileMatrix, MuzzleEpsilon)) return;

            lastProjectileMatrix = GrappleMatrix;
            Projectile.WorldMatrix = GrappleMatrix;
        }

        private void CountDownReload()
        {
            if (ReloadTime.Value > 0)
                ReloadTime.SetValue(ReloadTime.Value - 16.666667f);
        }

        private void Reload()
        {
            if (ReloadTime.Value > 0)
                CountDownReload();
            else
                State.Value = States.idle;
        }

        /// <summary>
        /// The hook in flight: launch speed plus the launcher's velocity, and with
        /// Hook.Gravity it falls. It catches on the first thing its path crosses, and when the
        /// rope runs out it gives up and is reeled back in.
        /// </summary>
        private void UpdateProjectile()
        {
            double dt = WinchMath.TickSeconds;

            if (Config.Hook.Gravity)
            {
                hookVelocity += WorldPlanets.GetGravity(GrappleMatrix.Translation) * dt;
            }

            Vector3D delta = hookVelocity * dt;

            IHitInfo hit = null;
            MyAPIGateway.Physics.CastRay(GrappleMatrix.Translation, GrappleMatrix.Translation + delta, out hit);
            if (hit != null && hit.HitEntity != null && !hit.HitEntity.MarkedForClose)
            {
                Attach(hit, delta);
            }
            else
            {
                GrappleMatrix.Translation += delta;

                // falling, it points the way it flies relative to the launcher
                if (Config.Hook.Gravity)
                {
                    Vector3D relative = hookVelocity - launchVelocity;
                    if (relative.LengthSquared() > 1e-6)
                    {
                        GrappleMatrix = Orient(GrappleMatrix.Translation, Vector3D.Normalize(relative), GrappleMatrix.Up);
                    }
                }

                if (isServer && RopeMath.ExceededRange(MuzzlePosition(), GrappleMatrix.Translation, Config.Hook.Range))
                {
                    Reset(true);
                }
            }

            MoveProjectile();
        }

        private void Attach(IHitInfo hit, Vector3D delta)
        {
            Vector3D forward = Vector3D.Normalize(delta);

            ConnectedEntityId.Value = hit.HitEntity.EntityId;
            ConnectedEntity = hit.HitEntity;
            ConnectedEntity.OnMarkForClose += attachedEntityClosed;

            // a little way into the surface, so the hook sits in it rather than on it
            Vector3D point = hit.Position + forward * 0.1f;

            if (ConnectedEntity is IMyCubeGrid)
            {
                MyCubeGrid grid = (ConnectedEntity as MyCubeGrid);
                grid.OnBlockRemoved += Grid_OnBlockRemoved;

                Vector3I pos = grid.WorldToGridInteger(point);
                IMySlimBlock block = grid.GetCubeBlock(pos);
                if (block != null)
                {
                    LocalGrapplePositionI.Value = block.Position;
                }
            }

            GrappleMatrix.Translation = point;
            attachPoint = point;
            LocalGrapplePosition.Value = Vector3D.Transform(point, MatrixD.Invert(ConnectedEntity.WorldMatrix));
            GrappleLength.Value = Vector3D.Distance(point, MuzzlePosition()) + AttachSlack;
            hookLocalSet = false;
            massRefreshFrame = 0;
            State.Value = States.attached;
            Tools.Debug($"Attached {hit.HitEntity.DisplayName} - State Change: {State.Value}");
        }

        /// <summary>
        /// The hook reeled back to the muzzle, tail first, keeping up with the launcher if it
        /// moves. It goes straight through anything in the way.
        /// </summary>
        private void UpdateRetract()
        {
            Vector3D muzzle = MuzzlePosition();
            if (!retractStarted)
            {
                // a client told by the server starts here too
                lastMuzzle = muzzle;
                retractStarted = true;
            }

            Vector3D position = GrappleMatrix.Translation + (muzzle - lastMuzzle);
            lastMuzzle = muzzle;

            Vector3D back = muzzle - position;
            double distance = back.Length();
            double step = Config.Hook.RetractSpeed * WinchMath.TickSeconds;

            if (distance <= step + HookRestOffset)
            {
                // home: the server says when, the clients park it and wait
                HideRope();
                MoveProjectileToMuzzle();
                if (isServer) State.Value = States.reloading;
                return;
            }

            Vector3D direction = back / distance;
            GrappleMatrix = Orient(position + direction * step, -direction, GrappleMatrix.Up);
            MoveProjectile();
        }

        /// <summary>
        /// While caught, the hook moves and turns with what it is caught in. Only the view needs
        /// it; the rope reads the attach point itself.
        /// </summary>
        private void FollowHook()
        {
            if (Projectile == null || ConnectedEntity == null) return;

            MatrixD entityMatrix = ConnectedEntity.WorldMatrix;

            if (!hookLocalSet)
            {
                // a client that never saw the flight has no orientation for it: point it along the rope
                if (GrappleMatrix.Forward.LengthSquared() < 0.5d)
                {
                    GrappleMatrix = Orient(attachPoint, RopeWrap.Direction(MuzzlePosition(), attachPoint), Vector3D.Up);
                }

                hookLocal = GrappleMatrix * MatrixD.Invert(entityMatrix);
                hookLocal.Translation = Vector3D.Zero;
                hookLocalSet = true;
            }

            MatrixD world = hookLocal * entityMatrix;
            world.Translation = attachPoint;
            GrappleMatrix = world;
            MoveProjectile();
        }

        /// <summary>A world matrix at position facing forward, rolled as close to up as it allows.</summary>
        private static MatrixD Orient(Vector3D position, Vector3D forward, Vector3D up)
        {
            Vector3D right = Vector3D.Cross(forward, up);
            if (right.LengthSquared() < 1e-9)
            {
                right = Vector3D.Cross(forward, RopeMath.Perpendicular(forward));
            }
            right = Vector3D.Normalize(right);

            return MatrixD.CreateWorld(position, forward, Vector3D.Cross(right, forward));
        }

        /// <summary>
        /// Where the hook is anchored this frame. Transforming the local attach position by
        /// the connected entity's world matrix is done once here rather than in each of the
        /// force, the rope and the zipline direction check.
        /// </summary>
        private void UpdateAttachPoint()
        {
            if (ConnectedEntity != null)
            {
                attachPoint = Vector3D.Transform(LocalGrapplePosition.Value, ConnectedEntity.WorldMatrix);
            }
        }

        private void Grid_OnBlockRemoved(IMySlimBlock block)
        {
            if (block.Position == LocalGrapplePositionI.Value)
            {
                Tools.Debug("The hooked block was removed; letting go");
                Reset(true);
            }
        }

        private void attachedEntityClosed(IMyEntity entity)
        {
            Tools.Debug($"The hooked entity {entity.DisplayName} closed; letting go");
            Reset(true);
        }
    }
}
