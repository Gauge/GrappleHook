using GrappleHook.Core;
using Sandbox.ModAPI;
using SENetworkAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Input;
using VRageMath;

namespace GrappleHook
{
    /// <summary>
    /// Riding the rope: F while looking at it clips the player on, F again lets go. A rider's
    /// place is a distance along the rope, so they travel with the rope when it moves; a
    /// tether pulls them up to that point, and gravity along the rope speeds them up downhill.
    /// </summary>
    public abstract partial class WeaponControlLayer
    {
        /// <summary>How close the player has to be looking at the rope to grab it.</summary>
        private const double InteractRange = 3.5d;

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
                Vector3D ziplineDirection = attachPoint - MuzzlePosition();

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

            // a dedicated server builds the rope only while someone rides it
            if (rope.Count < 2)
            {
                BuildAnchors();
                UpdateRopePath();
            }

            IMyCharacter character = ziplineData.player.Character;
            Vector3D characterPosition = character.WorldMatrix.Translation + character.WorldMatrix.Up;

            ziplineData.distance = ZiplineMath.ClosestDistance(rope, characterPosition);
            ziplineData.speed = 0;
            ziplineData.hasLastPulley = false;

            Tools.Debug($"Rider clipped on {ziplineData.distance:n1} m along the rope");
        }

        private void UpdateZiplineForces()
        {
            List<ZiplineEntity> riders = ZiplinePlayers.Value;
            if (riders.Count == 0 || rope.Count < 2) return;

            double dt = WinchMath.TickSeconds;

            // iterated backwards: riders drop off the rope from inside the loop
            for (int i = riders.Count - 1; i >= 0; i--)
            {
                ZiplineEntity zipEntity = riders[i];
                ZiplineEntity.Populate(ref zipEntity);

                if (zipEntity.player == null || !(zipEntity.player.Controller.ControlledEntity is IMyCharacter)) continue;

                IMyCharacter character = zipEntity.player.Character;

                if (character == null || character.Physics == null) continue;

                if (character.IsDead)
                {
                    riders.RemoveAt(i);
                    ZiplinePlayers.Push();
                    continue;
                }

                Vector3D pulley;
                Vector3D tangent;
                ZiplineMath.PointAt(rope, zipEntity.distance, out pulley, out tangent);

                Vector3D pulleyVelocity = zipEntity.hasLastPulley ? (pulley - zipEntity.lastPulley) / dt : Vector3D.Zero;
                zipEntity.lastPulley = pulley;
                zipEntity.pulley = pulley;
                zipEntity.hasLastPulley = true;

                // the tether: a damped spring up to the pulley, damped against the pulley's own
                // motion so a rider on a moving rope is carried along, not held back
                Vector3D characterPosition = character.WorldMatrix.Translation + character.WorldMatrix.Up * 1.7f;
                Vector3D tetherVector = pulley - characterPosition;
                double tetherLength = tetherVector.Length();

                if (tetherLength > 1e-6)
                {
                    double force = Config.Zipline.TetherForce * (tetherLength - Config.Zipline.TetherLength);
                    if (force > 0)
                    {
                        Vector3D forceVector = tetherVector / tetherLength * force - Config.Zipline.TetherDamping * ((Vector3D)character.Physics.LinearVelocity - pulleyVelocity);
                        character.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, forceVector, characterPosition, null, null, true);
                    }
                }

                Vector3D travel = zipEntity.direction ? tangent : -tangent;
                double acceleration = ZiplineMath.PulleyAcceleration(travel, WorldPlanets.GetGravity(pulley));
                zipEntity.speed = ZiplineMath.StepPulleySpeed(zipEntity.speed, acceleration, Config.Zipline.MinSpeed);

                double over = ZiplineMath.AdvancePulley(rope.Length, ref zipEntity.distance, zipEntity.direction, zipEntity.speed * dt);

                // ran off the end of the rope
                if (over > 0)
                {
                    riders.RemoveAt(i);
                    ZiplinePlayers.Push(SyncType.Broadcast);
                }
            }
        }

        private bool ZiplineContainsPlayer(long playerId)
        {
            return GetZiplineEntity(playerId) != null;
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
            if (State.Value != States.attached) return;

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
    }
}
