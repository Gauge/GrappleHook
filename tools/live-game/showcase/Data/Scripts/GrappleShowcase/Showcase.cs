using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace GrappleShowcase
{
    /// <summary>
    /// Test-world only (tools/live-game, LT_SHOWCASE=1): frames each grapple block on the
    /// GrappleRig grids for Workshop screenshots, one view every few seconds, and logs
    /// "[GrappleShowcase] READY name" once a view has settled, for lt shot to capture.
    /// Nothing fires: every hook rests in its barrel.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Showcase : MySessionComponentBase
    {
        class View
        {
            public string Name;
            public string Block;        // subtype, or null for the group shot
            public double Distance;
            public Vector3D Direction;  // from the target to the eye, in the block's frame (x right, y up, z back)
            public Vector3D Aim;        // target offset from the block centre, in its frame
        }

        const int Start = 1800;          // 30 s: time for lt to hide the HUD first
        const int Hold = 300;           // ticks per view
        const int Settle = 180;         // ticks before a view is ready to capture

        static readonly View[] Views =
        {
            new View { Name = "turret-large", Block = "GrappleHookTurretLarge", Distance = 9.5, Direction = new Vector3D(-0.85, 0.45, -1.0), Aim = new Vector3D(0, 0.6, -0.8) },
            new View { Name = "turret-large-side", Block = "GrappleHookTurretLarge", Distance = 9.5, Direction = new Vector3D(1.0, 0.3, -0.25), Aim = new Vector3D(0, 0.6, -0.8) },
            new View { Name = "turret-small", Block = "GrappleHookTurretSmall", Distance = 5.0, Direction = new Vector3D(-0.85, 0.45, -1.0), Aim = new Vector3D(0, 0.3, -0.8) },
            new View { Name = "turret-small-side", Block = "GrappleHookTurretSmall", Distance = 5.0, Direction = new Vector3D(1.0, 0.3, -0.3), Aim = new Vector3D(0, 0.3, -0.8) },
            new View { Name = "launcher-large", Block = "GrappleHookFixedLarge", Distance = 8.0, Direction = new Vector3D(-0.9, 0.5, -1.0), Aim = new Vector3D(0, 0, -0.5) },
            new View { Name = "launcher-small", Block = "GrappleHookFixedSmall", Distance = 3.6, Direction = new Vector3D(-0.9, 0.5, -1.0), Aim = new Vector3D(0, 0, -0.3) },
            new View { Name = "all", Block = null, Distance = 17.0, Direction = new Vector3D(-0.6, 0.55, -1.0), Aim = Vector3D.Zero },
        };

        int tick;
        readonly Dictionary<string, IMyTerminalBlock> blocks = new Dictionary<string, IMyTerminalBlock>();

        static void Log(string s) { MyLog.Default.Info("[GrappleShowcase] " + s); }

        public override void UpdateAfterSimulation()
        {
            if (!MyAPIGateway.Session.IsServer) return;
            tick++;
            if (tick == Start - 60) Find();
            if (tick < Start || blocks.Count == 0) return;

            int index = (tick - Start) / Hold;
            int phase = (tick - Start) % Hold;
            if (index >= Views.Length)
            {
                if (phase == 0 && index == Views.Length) Log("DONE");
                return;
            }

            View v = Views[index];
            if (phase == 0) Place(v);
            if (phase == Settle) Log("READY " + v.Name);
        }

        void Find()
        {
            HashSet<IMyEntity> grids = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(grids, e => e is IMyCubeGrid && (((IMyCubeGrid)e).DisplayName == "GrappleRig" || ((IMyCubeGrid)e).DisplayName == "GrappleRigSmall"));
            foreach (IMyEntity e in grids)
            {
                List<IMySlimBlock> found = new List<IMySlimBlock>();
                ((IMyCubeGrid)e).GetBlocks(found, b => b.FatBlock is IMyTerminalBlock && b.BlockDefinition.Id.SubtypeName.StartsWith("GrappleHook"));
                foreach (IMySlimBlock b in found) blocks[b.BlockDefinition.Id.SubtypeName] = (IMyTerminalBlock)b.FatBlock;
            }
            Log("found " + blocks.Count + " grapple blocks");
        }

        void Place(View v)
        {
            Vector3D target;
            MatrixD frame;
            if (v.Block == null)
            {
                // the group: the middle of all four, framed in the large turret's orientation
                target = Vector3D.Zero;
                foreach (IMyTerminalBlock b in blocks.Values) target += b.WorldMatrix.Translation;
                target /= blocks.Count;
                frame = blocks.ContainsKey("GrappleHookTurretLarge") ? blocks["GrappleHookTurretLarge"].WorldMatrix : MatrixD.Identity;
            }
            else
            {
                IMyTerminalBlock b;
                if (!blocks.TryGetValue(v.Block, out b)) { Log("ERR no " + v.Block); return; }
                frame = b.WorldMatrix;
                target = Vector3D.Transform(v.Aim, frame);
            }

            Vector3D direction = Vector3D.Normalize(Vector3D.TransformNormal(v.Direction, frame));
            Vector3D eye = target + direction * v.Distance;
            MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Spectator, null, eye);
            MySpectator.Static.Position = eye;
            MySpectator.Static.SetTarget(target, frame.Up);
            Log("VIEW " + v.Name);
        }
    }
}
