using GrappleHook.Core;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace GrappleHook
{

    /// <summary>
    /// Everything a grapple block does, independent of what kind of weapon block it is
    /// bolted onto. GrappleTurret and GrappleFixedGun supply the block specific parts.
    /// </summary>
    public abstract class WeaponControlLayer : MyGameLogicComponent
    {
        /// <summary>How close the player has to be looking at the rope to grab it.</summary>
        private const double InteractRange = 3.5d;

        /// <summary>How far the muzzle has to move before the hook entity is moved with it.</summary>
        private const double MuzzleEpsilon = 0.001d;

        private static readonly MyStringId RopeTexture = MyStringId.GetOrCompute("cable");
        private static readonly MyStringId RopeTextureInteractable = MyStringId.GetOrCompute("cableRed");
        private static readonly Vector4 RopeColor = Color.DarkGray;
        private static readonly Vector4 RopeColorInteractable = Color.Red;

        private bool waitframe = true;

        protected IMyUserControllableGun Weapon;
        private IMyGunObject<MyGunBase> gun;

        private MyEntity Projectile;

        public enum States { idle, reloading, projectile, attached }

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

        //private Vector3D GrapplePosition = Vector3D.Zero;
        //private Vector3 GrappleDirection = Vector3.Zero;

        private MatrixD GrappleMatrix = MatrixD.Zero;

        /// <summary>
        /// The rope, recomputed once a frame and shared by the draw, the zipline
        /// interaction check and the zipline forces.
        /// </summary>
        private readonly RopePath rope = new RopePath();

        internal bool terminalShootOn = false;
        private bool interactable = false;

        /// <summary>Where the hook is anchored, in world space. Recomputed once a frame.</summary>
        private Vector3D attachPoint = Vector3D.Zero;

        private MatrixD lastProjectileMatrix = MatrixD.Zero;

        /// <summary>A server with nothing to draw skips everything that only feeds the view.</summary>
        private bool isDedicated;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (MyAPIGateway.Session.IsServer)
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
            ResetIndicator.ValueChanged += Reset;

            GrappleLength = new NetSync<double>(this, TransferType.ServerToClient, 0);

            Winch = new NetSync<float>(this, TransferType.Both, 0);

            ReloadTime = new NetSync<float>(this, TransferType.ServerToClient, 0);


            ZiplinePlayers = new NetSync<List<ZiplineEntity>>(this, TransferType.ServerToClient, new List<ZiplineEntity>());

            RequestZiplineActivation = new NetSync<ZiplineEntity>(this, TransferType.ClientToServer, new ZiplineEntity(), false);
            RequestZiplineActivation.ValueChanged += ZiplineActivation;

            RequestZiplineDisconnect = new NetSync<ZiplineEntity>(this, TransferType.ClientToServer, new ZiplineEntity(), false);
            RequestZiplineDisconnect.ValueChanged += ZiplineDisconnect;


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

        /// <summary>
        /// Builds this block class's terminal. Each block class needs its own registration -
        /// see TerminalControls.
        /// </summary>
        protected abstract void CreateTerminalControls();

        private void Reset(bool o, bool n)
        {
            Reset();
        }
        private void Reset()
        {
            HideProjectile();

            if (ConnectedEntity != null)
                ConnectedEntity.OnMarkForClose -= attachedEntityClosed;
            if (ConnectedEntity is IMyCubeGrid)
                (ConnectedEntity as IMyCubeGrid).OnBlockRemoved -= Grid_OnBlockRemoved;

            ConnectedEntity = null;
            ConnectedEntityId.Value = 0;
            LocalGrapplePosition.Value = Vector3.Zero;
            LocalGrapplePositionI.Value = Vector3I.Zero;
            State.Value = States.reloading;

            Tools.Debug($"Reset - State Change: {State}");
        }

        private void AttemptConnect(long o, long n, ulong steamId)
        {
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
            UpdateWeaponGroup();
            UpdateDirectControlInput();

            switch (State.Value)
            {
                case States.idle:

                    rope.Clear();
                    MoveProjectileToMuzzle();
                    VerifyAndRequestShoot();
                    break;
                case States.reloading:
                    rope.Clear();
                    Reload();
                    break;
                case States.projectile:
                    UpdateProjectile();
                    UpdateRope();
                    break;
                case States.attached:
                    ValidateConnectedEntity();
                    UpdateAttachPoint();
                    UpdateGrappleLength();
                    ApplyForce();
                    UpdateRope();
                    UpdateZiplineInteract();
                    UpdateZiplineForces();
                    break;
            }


        }

        public override void UpdateAfterSimulation()
        {
            Draw();
        }

        /// <summary>The settings this block is running with. Server values, synced to clients.</summary>
        internal Settings Config
        {
            get { return settings.Value; }
        }

        /// <summary>Breaks the connection. Routed through the server by the reset indicator.</summary>
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
                    winch = loosen ? -Config.LoosenSpeed : Config.TightenSpeed;
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
            if (State.Value != States.attached && n.direction != Vector3.Zero)
            {
                GrappleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
                State.Value = States.projectile;
                ReloadTime.Value = (float)gun.GunBase.ReloadTime;
                Shooting.SetValue(new ShootData());
                Tools.Debug($"Shooting - State Change: {State.Value}");
            }
        }

        private void MoveProjectileToMuzzle()
        {
            // the muzzle matrix is only wanted here to park the hook on it; the shot itself
            // reads it fresh
            if (isDedicated) return;

            GrappleMatrix = gun.GunBase.GetMuzzleWorldMatrix();
            GrappleMatrix.Translation += GrappleMatrix.Forward * 1.1f;
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
        private void Reload()
        {
            if (ReloadTime.Value > 0)
                ReloadTime.SetValue(ReloadTime.Value - 16.666667f);
            else
                State.Value = States.idle;
        }

        private void UpdateProjectile()
        {
            //Tools.Debug($"Projectile In Flight {(Weapon.PositionComp.WorldMatrixRef.Translation - GrapplePosition).Length()}");
            Vector3 delta = GrappleMatrix.Forward * settings.Value.GrappleProjectileSpeed;

            IHitInfo hit = null;
            MyAPIGateway.Physics.CastRay(GrappleMatrix.Translation, GrappleMatrix.Translation + delta, out hit);
            if (hit != null && !hit.HitEntity.MarkedForClose)
            {
                ConnectedEntityId.Value = hit.HitEntity.EntityId;
                ConnectedEntity = hit.HitEntity;
                ConnectedEntity.OnMarkForClose += attachedEntityClosed;

                if (ConnectedEntity is IMyCubeGrid)
                {
                    MyCubeGrid grid = (ConnectedEntity as MyCubeGrid);
                    grid.OnBlockRemoved += Grid_OnBlockRemoved;

                    Vector3I pos = grid.WorldToGridInteger(hit.Position + GrappleMatrix.Forward * 0.1f);
                    IMySlimBlock block = grid.GetCubeBlock(pos);
                    if (block != null)
                    {
                        LocalGrapplePositionI.Value = block.Position;
                    }
                }

                GrappleMatrix.Translation = hit.Position + GrappleMatrix.Forward * 0.1f;
                LocalGrapplePosition.Value = Vector3D.Transform(hit.Position + GrappleMatrix.Forward * 0.1f, MatrixD.Invert(ConnectedEntity.WorldMatrix));
                GrappleLength.Value = (hit.Position - Weapon.PositionComp.WorldMatrixRef.Translation).Length() + 1.25f;
                State.Value = States.attached;
                Tools.Debug($"Attached {hit.HitEntity.DisplayName} - State Change: {State.Value}");
            }
            else
            {
                GrappleMatrix.Translation += delta;

                // if grapple length goes beyond max length
                if (RopeMath.ExceededRange(Weapon.PositionComp.WorldMatrixRef.Translation, GrappleMatrix.Translation, settings.Value.ShootRopeLength))
                {
                    ResetIndicator.Value = !ResetIndicator.Value;
                }
            }

            MoveProjectile();
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

        private void UpdateGrappleLength()
        {
            double length = WinchMath.StepLength(GrappleLength.Value, Winch.Value, settings.Value.MinRopeLength, settings.Value.MaxRopeLength);

            if (length != GrappleLength.Value)
            {
                GrappleLength.SetValue(length);
            }
        }

        public void ApplyForce()
        {
            if (Weapon.CubeGrid.Physics == null || ConnectedEntity == null || ConnectedEntity.Physics == null || Entity.MarkedForClose || ConnectedEntity.MarkedForClose)
            {
                return;
            }

            Vector3D turretPostion = Weapon.PositionComp.WorldMatrixRef.Translation;
            Vector3D entityPostion = attachPoint;
            Vector3D direction = turretPostion - entityPostion;
            double currentLength = direction.Length();
            direction = direction / currentLength;

            double relativeSpeed = (Weapon.CubeGrid.Physics.LinearVelocity + ConnectedEntity.Physics.LinearVelocity).Length();
            double force = WinchMath.TensionForce(currentLength, GrappleLength.Value, settings.Value.RopeForce, settings.Value.RopeDamping, relativeSpeed);
            Vector3D forceVector = direction * force;

            if (force > 0)
            {
                Weapon.CubeGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, -1 * forceVector, turretPostion, null, null, true);
                ConnectedEntity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, forceVector, entityPostion, null, null, true);
            }
        }

        private void Grid_OnBlockRemoved(IMySlimBlock block)
        {
            if (block.Position == LocalGrapplePositionI.Value)
            {
                Reset();
            }
        }

        private void attachedEntityClosed(IMyEntity entity)
        {
            Reset();
        }

        private void UpdateZiplineInteract()
        {
            interactable = false;

            if (isDedicated) return;

            bool interactKeyPressed = MyAPIGateway.Input.IsNewKeyPressed(MyKeys.F);

            if (MyAPIGateway.Session.Player == null || !(MyAPIGateway.Session.Player.Controller.ControlledEntity is IMyCharacter)) return;

            ZiplineEntity e;
            if ((e = GetZiplineEntity(MyAPIGateway.Session.Player.IdentityId)) != null)
            {
                if (interactKeyPressed)
                {
                    RequestZiplineDisconnect.Value = e;
                }
                return;
            }

            IMyCamera cam = MyAPIGateway.Session.Camera;
            if (cam == null) return;

            RayD camRay = new RayD(cam.WorldMatrix.Translation, cam.WorldMatrix.Forward);

            interactable = RopeMath.RayHitsPath(rope, ref camRay, InteractRange);

            if (interactKeyPressed && interactable)
            {
                Vector3D ziplineDirection = attachPoint - Weapon.WorldMatrix.Translation;

                bool direction = Vector3D.Dot(ziplineDirection, cam.WorldMatrix.Forward) >= 0;

                RequestZiplineActivation.Value = new ZiplineEntity(MyAPIGateway.Session.Player.IdentityId, direction);
            }
        }

        private void AttachToZipline(ZiplineEntity ziplineData)
        {
            Tools.Debug($"Attaching to zipline");
            ZiplineEntity.Populate(ref ziplineData);

            if (ziplineData.player == null || !(ziplineData.player.Controller.ControlledEntity is IMyCharacter))
            {
                Tools.Info($"Warning could not find the player in the list of players. This should never be happening!");
                return;
            }

            IMyCharacter character = ziplineData.player.Character;
            UpdateRope();

            Vector3D characterPosition = character.WorldMatrix.Translation + character.WorldMatrix.Up;
            Vector3D pulley = ZiplineMath.ComputePulley(rope, characterPosition, ziplineData.direction);

            Tools.Debug($"Got pulley location {pulley}");

            ziplineData.pulley = pulley;
            ziplineData.lastPulley = pulley;
        }

        private void UpdateZiplineForces()
        {
            // iterated backwards: riders drop off the rope from inside the loop
            for (int i = ZiplinePlayers.Value.Count - 1; i >= 0; i--)
            {
                ZiplineEntity zipEntity = ZiplinePlayers.Value[i];
                ZiplineEntity.Populate(ref zipEntity);

                if (zipEntity.player == null || !(zipEntity.player.Controller.ControlledEntity is IMyCharacter)) continue;

                IMyCharacter character = zipEntity.player.Character;

                if (character == null) continue;

                if (character.IsDead)
                {
                    ZiplinePlayers.Value.RemoveAt(i);
                    ZiplinePlayers.Push();
                    continue;
                }

                Vector3D characterPosition = character.WorldMatrix.Translation + character.WorldMatrix.Up * 1.7f;
                Vector3D tetherVector = zipEntity.pulley - characterPosition;
                double tetherLength = tetherVector.Length();
                Vector3D tetherDirectionNorm = tetherVector / tetherLength;

                double force = settings.Value.ZiplineTetherForce * (tetherLength - settings.Value.ZiplineTetherLength);
                Vector3D forceVector = tetherDirectionNorm * force - settings.Value.ZiplineDamping * character.Physics.LinearVelocity;
                if (force > 0)
                {
                    character.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, forceVector, characterPosition, null, null, true);
                }

                Vector3D travelled = zipEntity.pulley - zipEntity.lastPulley;
                double acceleration = ZiplineMath.PulleyAcceleration(tetherDirectionNorm, travelled, settings.Value.ZiplineGraveForce);
                double minSpeed = settings.Value.ZiplinePulleyMinSpeed * WinchMath.TickSeconds;

                double velocityToApply = ZiplineMath.StepPulleySpeed(zipEntity.lastPulleyVelocity, acceleration, travelled.Length(), minSpeed);

                zipEntity.lastPulley = zipEntity.pulley;
                zipEntity.lastPulleyVelocity = zipEntity.pulleyVelocity;
                zipEntity.pulleyVelocity = velocityToApply;

                double distanceRemaining = ZiplineMath.AdvancePulley(rope, ref zipEntity.pulley, zipEntity.direction, velocityToApply);

                // ran off the end of the rope
                if (distanceRemaining > 0)
                {
                    ZiplinePlayers.Value.RemoveAt(i);
                    ZiplinePlayers.Push(SyncType.Broadcast);
                }
            }
        }

        private bool ZiplineContainsPlayer(IMyPlayer player)
        {
            return ZiplineContainsPlayer(player.IdentityId);
        }

        private bool ZiplineContainsPlayer(long playerId)
        {
            for (int i = 0; i < ZiplinePlayers.Value.Count; i++)
            {
                ZiplineEntity ent = ZiplinePlayers.Value[i];

                if (ent.playerId == playerId) return true;
            }
            return false;
        }

        private ZiplineEntity GetZiplineEntity(long playerId)
        {
            for (int i = 0; i < ZiplinePlayers.Value.Count; i++)
            {
                ZiplineEntity ent = ZiplinePlayers.Value[i];

                if (ent.playerId == playerId) return ent;
            }
            return null;
        }

        private void ZiplineActivation(ZiplineEntity o, ZiplineEntity n)
        {
            if (!ZiplineContainsPlayer(n.playerId))
            {
                AttachToZipline(n);
                ZiplinePlayers.Value.Add(n);
                ZiplinePlayers.Push(SyncType.Broadcast);
                Tools.Debug($"Player {n.playerId} activated the zipline. Active zipliners {ZiplinePlayers.Value.Count}");
            }
            else
            {
                Tools.Debug($"Player {n.playerId} is already active!");
            }
        }

        private void ZiplineDisconnect(ZiplineEntity o, ZiplineEntity n)
        {
            ZiplineEntity e = GetZiplineEntity(n.playerId);
            if (e != null)
            {
                ZiplinePlayers.Value.Remove(e);
                ZiplinePlayers.Push();
                Tools.Debug($"Player {n.playerId} was removed from the zipline. Active zipliners {ZiplinePlayers.Value.Count}");
            }
            else
            {
                Tools.Debug($"Player {n.playerId} does not exist in the list!");
            }
        }

        private void Draw()
        {
            try
            {
                if (isDedicated) return;

                List<ZiplineEntity> riders = ZiplinePlayers.Value;
                if (rope.SegmentCount == 0 && riders.Count == 0) return;

                Vector4 color = interactable ? RopeColorInteractable : RopeColor;
                MyStringId texture = interactable ? RopeTextureInteractable : RopeTexture;

                for (int i = 0; i < rope.SegmentCount; i++)
                {
                    MySimpleObjectDraw.DrawLine(rope.Points[i], rope.Points[i + 1], texture, ref color, 0.15f, BlendTypeEnum.Standard);
                }

                for (int i = 0; i < riders.Count; i++)
                {
                    ZiplineEntity zipEntity = riders[i];
                    ZiplineEntity.Populate(ref zipEntity);

                    if (zipEntity.player == null || !(zipEntity.player.Controller.ControlledEntity is IMyCharacter)) continue;

                    IMyCharacter character = zipEntity.player.Character;
                    if (character == null) continue;

                    MySimpleObjectDraw.DrawLine(character.WorldMatrix.Translation + character.WorldMatrix.Up * 1.85f, zipEntity.pulley, texture, ref color, 0.05f, BlendTypeEnum.Standard);
                }

            }
            catch (Exception e)
            {
                Tools.Debug(e.ToString());
            }
        }

        private Vector3D GetSegmentDirection()
        {
            Vector3D sagDirection = WorldPlanets.GetGravity(Weapon.PositionComp.WorldMatrixRef.Translation);

            if (sagDirection == Vector3D.Zero)
            {
                sagDirection = Weapon.WorldMatrix.Down;
            }

            return sagDirection;
        }

        /// <summary>
        /// Rebuilds the rope for this frame. Everything that needs the rope - the draw, the
        /// zipline interaction check, the zipline forces - reads the one buffer.
        /// </summary>
        private void UpdateRope()
        {
            Vector3D gunPosition = Weapon.PositionComp.WorldMatrixRef.Translation;
            Vector3D end;
            double referenceLength;

            if (State.Value == States.projectile)
            {
                end = GrappleMatrix.Translation;
                // barely any slack: the rope is being dragged out behind the projectile
                referenceLength = Vector3D.Distance(gunPosition, end) * 1.005d;
            }
            else if (State.Value == States.attached && ConnectedEntity != null)
            {
                end = attachPoint;
                referenceLength = GrappleLength.Value;
            }
            else
            {
                rope.Clear();
                return;
            }

            RopeMath.ComputeCurvePoints(rope, gunPosition, end, GetSegmentDirection(), referenceLength, settings.Value.RopeSegments);
        }
    }
}
