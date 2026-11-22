using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace GrappleProbe
{
    /// <summary>
    /// Test-world only (tools/live-game). Drives every grapple block on the GrappleRig grids -
    /// both turrets and both fixed launchers - through the toolbar path and the manual-trigger path,
    /// and logs, once a second, what can be seen from outside the mod: each hook entity's
    /// position, the guns' shooting and ammo state. Parks the spectator camera on the small
    /// turret so screenshots show it.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Probe : MySessionComponentBase
    {
        class Gun
        {
            public string Name;
            public IMyUserControllableGun Block;
            public IMyEntity Hook;
            /// <summary>What the air rig's launchers fire at: a free grid the probe spawns.</summary>
            public IMyCubeGrid Payload;
            /// <summary>The rotor on AIRLARGE's payload, whose head the hook catches.</summary>
            public IMyMotorStator Stator;
            public double JointRest = -1;
        }

        int tick;
        readonly List<Gun> guns = new List<Gun>();
        IMyUserControllableGun vanilla;

        static void Log(string s) { MyLog.Default.Info("[GrappleProbe] " + s); }

        public override void UpdateAfterSimulation()
        {
            if (!MyAPIGateway.Session.IsServer) return;
            tick++;
            if (tick == 100) Setup();
            if (guns.Count == 0) return;
            if (tick == 110) View();
            if (tick == 200) Seat();
            if (tick == 600) ViewSide();
            // take the large turret over, as Control in its terminal does, while its hook is out:
            // lt holds W and S then, and the samples show the winch speed they set
            if (tick == 700) TakeControl("LARGE");
            // paint the small turret while its hook is still out, and look at it close up
            if (tick == 1500) { Paint(); View(); }
            // reload ends ~20 s after the Detach: then look at each fixed launcher with its hook home
            if (tick == 3120) ViewGun("FIXEDLARGE", 9.0);
            if (tick == 3600) ViewGun("FIXEDSMALL", 3.2);
            // the camera back in the seat, so mouse input reaches the ship (lt drives the weapon group test)
            if (tick == 3900) ViewSeat();
            // A vanilla turret must still obey the Shoot actions the mod wraps (they used to all run
            // whichever delegate was captured last). Shoot reads the toggle state each time.
            if (vanilla != null && tick >= 120 && tick <= 220 && tick % 20 == 0)
            {
                string id = tick == 120 ? "Shoot_On" : tick == 140 ? "Shoot_Off" : tick == 160 ? "Shoot" : tick == 180 ? "Shoot" : null;
                bool before = vanilla.Shoot;
                if (id != null) ApplyTo(vanilla, id);
                if (id != null) Log("VANILLA " + id + ": Shoot " + before + " -> " + vanilla.Shoot);
            }
            if (tick == 250) { AssignHooks(); foreach (Gun g in guns) RestPose(g); }
            // the rope physics, on the air rig: payloads appear just before everything fires,
            // hang and swing on their ropes, are winched up at 15 s for 7 s, and let go at 30 s
            if (tick == 290) SpawnPayloads();
            if (tick == 292) BuildRotorHead();
            if (tick >= 298 && tick <= 2700 && tick % 6 == 0) foreach (Gun g in guns) Physics(g);
            if (tick == 900 || tick == 1320)
            {
                float speed = tick == 900 ? 3f : 0f;
                foreach (Gun g in guns) if (g.Payload != null) ((IMyTerminalBlock)g.Block).SetValueFloat("Winch", speed);
                Log("ACT air winch " + speed);
            }
            if (tick == 300) { Log("ACT ShootOnce (toolbar path)"); foreach (Gun g in guns) ApplyTo(g.Block, "ShootOnce"); }
            if (tick == 1800) { Log("ACT Detach"); foreach (Gun g in guns) ApplyTo(g.Block, "Detach"); }
            // attached from ~6 s to the Detach at 30 s, long enough to catch the rope in a screenshot.
            // The weapon's ReloadTime is 20 s; the mod goes idle once it has counted that down
            if (tick == 6600) { Log("ACT gun.Shoot = true for 2 s (manual-trigger path)"); foreach (Gun g in guns) g.Block.Shoot = true; }
            if (tick >= 6600 && tick < 6720) foreach (Gun g in guns) if (g.Block.IsShooting && tick % 30 == 0) Log(g.Name + " IsShooting=True at tick " + tick);
            if (tick == 6720) { foreach (Gun g in guns) g.Block.Shoot = false; Log("ACT gun.Shoot = false"); }
            if (tick % 60 == 0 && tick >= 240 && tick <= 7800) foreach (Gun g in guns) Sample(g);
            if (tick == 7860) Log("DONE");
        }

        void ApplyTo(IMyUserControllableGun target, string id)
        {
            ITerminalAction a = ((IMyTerminalBlock)target).GetActionWithName(id);
            if (a == null) { Log("ERR no action " + id + " on " + target.BlockDefinition); return; }
            a.Apply((IMyTerminalBlock)target);
        }

        void Setup()
        {
            HashSet<IMyEntity> grids = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(grids, e => e is IMyCubeGrid && ((IMyCubeGrid)e).DisplayName.StartsWith("GrappleRig"));
            foreach (IMyEntity e in grids)
            {
                IMyCubeGrid grid = (IMyCubeGrid)e;
                List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks, b => b.FatBlock is IMyUserControllableGun);
                foreach (IMySlimBlock b in blocks)
                {
                    Log("weapon block " + b.BlockDefinition.Id + " on " + grid.DisplayName + " (" + grid.GridSizeEnum + ") functional=" + b.FatBlock.IsFunctional + " working=" + b.FatBlock.IsWorking);
                    string sub = b.BlockDefinition.Id.SubtypeName;
                    string name = sub.Replace("GrappleHookTurret", "").Replace("GrappleHook", "").ToUpperInvariant();
                    // the air rig: AIRLARGE and AIRSMALL hang payloads, AIRMISS (on the platform's
                    // front edge) fires at the open sky
                    if (grid.DisplayName.StartsWith("GrappleRigAir"))
                        name = b.Min.Z == -3 ? "AIRMISS" : "AIR" + name.Replace("FIXED", "");
                    if (sub.StartsWith("GrappleHook"))
                        guns.Add(new Gun { Name = name, Block = (IMyUserControllableGun)b.FatBlock });
                    if (b.BlockDefinition.Id.TypeId.ToString().EndsWith("LargeGatlingTurret")) vanilla = (IMyUserControllableGun)b.FatBlock;
                }
            }
            if (guns.Count == 0) { Log("ERR no grapple block on the GrappleRig grids"); return; }

            foreach (Gun g in guns)
            {
                MyObjectBuilder_AmmoMagazine mag = MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_AmmoMagazine>("GrappleHookMagazine");
                IMyInventory inv = g.Block.GetInventory(0);
                // AddItems adds all or nothing: load what fits, up to 3 (a magazine is 0.1 m3)
                int n = System.Math.Min(3, (int)((double)inv.MaxVolume / 0.1 + 1e-6));
                inv.AddItems(n, mag);
                Log(g.Name + " inventory after adding " + n + " magazines: " + inv.CurrentVolume + " m3 of " + inv.MaxVolume + ", items=" + inv.ItemCount);

                List<ITerminalAction> actions = new List<ITerminalAction>();
                ((IMyTerminalBlock)g.Block).GetActions(actions);
                StringBuilder sb = new StringBuilder();
                foreach (ITerminalAction a in actions) sb.Append(a.Id).Append(a.IsEnabled((IMyTerminalBlock)g.Block) ? "" : "(disabled)").Append(' ');
                Log(g.Name + " actions " + actions.Count + ": " + sb);

                // the turret's moving parts, as the game loaded them from the model's dummies
                MyEntity block = (MyEntity)g.Block;
                foreach (var part in block.Subparts)
                {
                    Log(g.Name + " subpart " + part.Key + " model=" + ((IMyEntity)part.Value).Model.AssetName);
                    foreach (var inner in part.Value.Subparts)
                        Log(g.Name + "   subpart " + inner.Key + " model=" + ((IMyEntity)inner.Value).Model.AssetName);
                }
            }
        }

        /// <summary>Where the idle hook sits against the barrel subpart: the pivot it is placed from.</summary>
        void RestPose(Gun g)
        {
            // a turret rests its hook from the barrel; a fixed gun from its block
            IMyEntity frame = (IMyEntity)Barrel(g) ?? g.Block;
            if (g.Hook == null) { Log(g.Name + " REST no hook"); return; }
            MatrixD inv = MatrixD.Invert(frame.WorldMatrix);
            Vector3D local = Vector3D.Transform(g.Hook.WorldMatrix.Translation, inv);
            Log(g.Name + " REST hook model=" + g.Hook.Model.AssetName + " centre in barrel frame=" + Fmt(local)
                + " in " + (frame == g.Block ? "block" : "barrel") + " frame, forward.dot(hook forward)=" + Vector3D.Dot(frame.WorldMatrix.Forward, g.Hook.WorldMatrix.Forward).ToString("0.000"));
        }

        static MyEntity Barrel(Gun g)
        {
            foreach (var part in ((MyEntity)g.Block).Subparts)
                foreach (var inner in part.Value.Subparts)
                    return inner.Value;
            return null;
        }

        void SpawnPayloads()
        {
            Vector3D gravity = MyAPIGateway.Physics.CalculateNaturalGravityAt(guns.Find(x => x.Name == "AIRMISS")?.Block.WorldMatrix.Translation ?? Vector3D.Zero, out _unused);
            Log("air rig gravity " + gravity.Length().ToString("0.00") + " m/s2");
            // a heavy hull with a rotor on the face towards the launcher: the hook catches the rotor
            // head, so the rope pulls the hull through the joint, as hooking a ship's turret or
            // rotor part does
            Payload("AIRLARGE", MyCubeSize.Large, "LargeHeavyBlockArmorBlock", 5, 20.0, true);
            Payload("AIRSMALL", MyCubeSize.Small, "SmallBlockArmorBlock", 1, 6.0);
        }
        float _unused;

        /// <summary>A free grid, n x n x n blocks, centred dist metres in front of a launcher.</summary>
        void Payload(string name, MyCubeSize size, string block, int n, double dist, bool rotor = false)
        {
            Gun g = guns.Find(x => x.Name == name);
            if (g == null) return;
            MatrixD m = g.Block.WorldMatrix;
            double cell = size == MyCubeSize.Large ? 2.5 : 0.5;
            double half = (n - 1) * cell / 2;
            Vector3D centre = m.Translation + m.Forward * dist;
            // grid +X right, +Y up, +Z back; block (0,0,0) sits at the grid's origin
            Vector3D origin = centre - m.Right * half - m.Up * half - m.Backward * half;

            MyObjectBuilder_CubeGrid ob = new MyObjectBuilder_CubeGrid
            {
                GridSizeEnum = size,
                IsStatic = false,
                PersistentFlags = MyPersistentEntityFlags2.InScene,
                PositionAndOrientation = new MyPositionAndOrientation(origin, m.Forward, m.Up),
                DisplayName = "Payload" + name,
            };
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    for (int z = 0; z < n; z++)
                        ob.CubeBlocks.Add(new MyObjectBuilder_CubeBlock { SubtypeName = block, Min = new SerializableVector3I(x, y, z) });
            // grid +Z faces the launcher: the stator sits on that face, its top pointing at it
            if (rotor)
                ob.CubeBlocks.Add(new MyObjectBuilder_MotorStator
                {
                    SubtypeName = "LargeStator",
                    Min = new SerializableVector3I(n / 2, n / 2, n),
                    BlockOrientation = new SerializableBlockOrientation(Base6Directions.Direction.Up, Base6Directions.Direction.Backward),
                });

            MyAPIGateway.Entities.RemapObjectBuilder(ob);
            g.Payload = (IMyCubeGrid)MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(ob);
            if (rotor)
            {
                List<IMySlimBlock> stators = new List<IMySlimBlock>();
                g.Payload.GetBlocks(stators, b => b.FatBlock is IMyMotorStator);
                if (stators.Count > 0) g.Stator = (IMyMotorStator)stators[0].FatBlock;
            }
            Log("PAYLOAD " + name + " " + n + "x" + n + "x" + n + " " + block + " mass=" + (g.Payload.Physics == null ? "?" : g.Payload.Physics.Mass.ToString("0")) + " kg, " + dist + " m out");
        }

        void BuildRotorHead()
        {
            foreach (Gun g in guns)
            {
                if (g.Stator == null) continue;
                // the terminal's Add Top Part: the mod API has no other way to build a head
                List<ITerminalAction> actions = new List<ITerminalAction>();
                ((IMyTerminalBlock)g.Stator).GetActions(actions);
                ITerminalAction add = actions.Find(x => (x.Id.Contains("Top") || x.Id.Contains("Head")) && !x.Id.Contains("Small"));
                if (add == null)
                {
                    StringBuilder ids = new StringBuilder();
                    foreach (ITerminalAction x in actions) ids.Append(x.Id).Append(' ');
                    Log("ERR no add-head action on the stator: " + ids);
                    continue;
                }
                Log("ROTOR using action " + add.Id);
                add.Apply((IMyTerminalBlock)g.Stator);
                Log("ROTOR head requested on " + g.Name + "'s payload");
            }
        }

        /// <summary>
        /// Ten times a second: how the rope holds each payload (distance from the muzzle, how far
        /// below it, speed, where its hook is), and where the AIRMISS hook is in flight or coming back.
        /// </summary>
        void Physics(Gun g)
        {
            if (!g.Name.StartsWith("AIR") || g.Block.MarkedForClose) return;
            FindHook(g);
            IMyGunObject<MyGunBase> gb = g.Block as IMyGunObject<MyGunBase>;
            MatrixD muzzle = gb.GunBase.GetMuzzleWorldMatrix();
            string hook = g.Hook == null || g.Hook.WorldMatrix.Translation.X > 1e30 ? "hidden"
                : "ahead " + Vector3D.Dot(g.Hook.WorldMatrix.Translation - muzzle.Translation, muzzle.Forward).ToString("0.00")
                + " below " + Vector3D.Dot(muzzle.Translation - g.Hook.WorldMatrix.Translation, muzzle.Up).ToString("0.00");
            string line = "PHYS " + g.Name + " t=" + (tick / 60.0).ToString("0.0") + " hook " + hook;
            if (g.Payload != null && g.Payload.Physics != null)
            {
                Vector3D p = g.Payload.Physics.CenterOfMassWorld;
                line += " | payload dist " + Vector3D.Distance(p, muzzle.Translation).ToString("0.000")
                    + " ahead " + Vector3D.Dot(p - muzzle.Translation, muzzle.Forward).ToString("0.00")
                    + " below " + Vector3D.Dot(muzzle.Translation - p, muzzle.Up).ToString("0.00")
                    + " speed " + g.Payload.Physics.LinearVelocity.Length().ToString("0.000")
                    + (g.Hook == null ? "" : " hook-to-payload " + Vector3D.Distance(g.Hook.WorldMatrix.Translation, p).ToString("0.00"))
                    + " spin " + g.Payload.Physics.AngularVelocity.Length().ToString("0.00");
                // how far the rotor head has been pulled from where it sat on its stator
                if (g.Stator != null && g.Stator.Top == null && g.JointRest >= 0)
                {
                    Log("ROTOR head gone from " + g.Name + "'s payload at t=" + (tick / 60.0).ToString("0.0") + "; stator integrity " + g.Stator.SlimBlock.Integrity.ToString("0") + " of " + g.Stator.SlimBlock.MaxIntegrity.ToString("0"));
                    g.JointRest = -2;
                }
                if (g.Stator != null && g.Stator.Top != null)
                {
                    double joint = Vector3D.Distance(g.Stator.Top.WorldMatrix.Translation, g.Stator.WorldMatrix.Translation);
                    if (g.JointRest < 0) g.JointRest = joint;
                    IMySlimBlock head = g.Stator.Top.SlimBlock;
                    line += " head integrity " + (head.Integrity / head.MaxIntegrity).ToString("0.00");
                    line += " joint " + (joint - g.JointRest).ToString("0.000") + " m head " + (g.Stator.Top.CubeGrid.Physics == null ? "?" : g.Stator.Top.CubeGrid.Physics.Mass.ToString("0")) + " kg";
                }
            }
            Log(line);
        }

        static string Fmt(Vector3D v) { return "(" + v.X.ToString("0.00") + ", " + v.Y.ToString("0.00") + ", " + v.Z.ToString("0.00") + ")"; }

        /// <summary>
        /// Sits the player in the rig's cockpit seat, whose toolbar world.py fills with the
        /// grapple actions, so the HUD shows their icons.
        /// </summary>
        void Seat()
        {
            HashSet<IMyEntity> grids = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(grids, e => e is IMyCubeGrid && ((IMyCubeGrid)e).DisplayName == "GrappleRig");
            IMyCharacter me = MyAPIGateway.Session.Player?.Character;
            foreach (IMyEntity e in grids)
            {
                List<IMySlimBlock> seats = new List<IMySlimBlock>();
                ((IMyCubeGrid)e).GetBlocks(seats, b => b.FatBlock is IMyCockpit);
                if (seats.Count == 0 || me == null) continue;
                ((IMyCockpit)seats[0].FatBlock).AttachPilot(me);
                Log("SEATED in " + seats[0].BlockDefinition.Id.SubtypeName);
                return;
            }
            Log("ERR no cockpit seat on GrappleRig or no character");
        }

        void TakeControl(string name)
        {
            Gun g = guns.Find(x => x.Name == name);
            if (g == null) return;
            // Control is a terminal button on turrets, not a toolbar action: press the button
            List<IMyTerminalControl> controls;
            MyAPIGateway.TerminalControls.GetControls<IMyLargeTurretBase>(out controls);
            IMyTerminalControlButton button = controls.Find(c => c.Id == "Control") as IMyTerminalControlButton;
            if (button == null) { Log("ERR no Control button on " + name); return; }
            button.Action((IMyTerminalBlock)g.Block);
            Log("CONTROL " + name + " turret requested");
        }

        void ViewSeat()
        {
            IMyEntity seat = MyAPIGateway.Session.ControlledObject?.Entity;
            if (seat == null) return;
            MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Entity, seat);
            Log("VIEW seat");
        }

        /// <summary>The camera's subject: the small turret, or the large one where there is none.</summary>
        Gun Subject()
        {
            return guns.Find(g => g.Name == "SMALL") ?? guns.Find(g => g.Name == "LARGE");
        }

        /// <summary>Paints the small turret red, as a player's paint tool would, to show which parts take colour.</summary>
        void Paint()
        {
            Gun small = guns.Find(g => g.Name == "SMALL");
            if (small == null) return;
            IMySlimBlock slim = small.Block.SlimBlock;
            Vector3 before = slim.ColorMaskHSV;
            small.Block.CubeGrid.ColorBlocks(slim.Min, slim.Max, new Vector3(0f, 0.8f, 0.05f));
            Log("PAINT small turret ColorMaskHSV " + before + " -> " + slim.ColorMaskHSV);
        }

        /// <summary>Front-left three-quarter view of one grapple block, dist metres out.</summary>
        void ViewGun(string name, double dist)
        {
            Gun g = guns.Find(x => x.Name == name);
            if (g == null) return;
            MatrixD m = g.Block.WorldMatrix;
            Vector3D target = m.Translation + m.Forward * (dist * 0.12);
            Vector3D eye = target + Vector3D.Normalize(m.Forward * 1.0 + m.Left * 0.8 + m.Up * 0.5) * dist;
            MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Spectator, null, eye);
            MySpectator.Static.Position = eye;
            MySpectator.Static.SetTarget(target, m.Up);
            Log("VIEW " + name);
        }

        void View()
        {
            Gun small = Subject();
            if (small == null) return;
            MatrixD m = small.Block.WorldMatrix;
            Vector3D target = m.Translation + m.Up * -0.2;
            Vector3D eye = target + Vector3D.Normalize(m.Forward * 1.0 + m.Left * 0.75 + m.Up * 0.55) * 5.0;
            MyAPIGateway.Session.SetCameraController(MyCameraControllerEnum.Spectator, null, eye);
            MySpectator.Static.Position = eye;
            MySpectator.Static.SetTarget(target, m.Up);
            Log("VIEW spectator on the " + small.Name + " turret");
        }

        /// <summary>Side-on and back, once both hooks are attached: the turrets, the ropes and the wall.</summary>
        void ViewSide()
        {
            Gun small = Subject();
            if (small == null) return;
            MatrixD m = small.Block.WorldMatrix;
            Vector3D target = m.Translation + m.Forward * 3.0 + m.Up * 0.5;
            Vector3D eye = target + Vector3D.Normalize(m.Left * 1.0 + m.Backward * 0.35 + m.Up * 0.45) * 13.0;
            MySpectator.Static.Position = eye;
            MySpectator.Static.SetTarget(target, m.Up);
            Log("VIEW side on, ropes attached");
        }

        /// <summary>
        /// One hook entity per grapple block, all idle at their muzzles: pair them closest first,
        /// one to one, since the small grid's hooks rest close together.
        /// </summary>
        void AssignHooks()
        {
            HashSet<IMyEntity> found = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(found, e => e.Model != null && e.Model.AssetName != null && e.Model.AssetName.Contains("GrappleHookAmmo"));
            List<IMyEntity> hooks = new List<IMyEntity>(found);
            List<Gun> left = new List<Gun>(guns);
            while (left.Count > 0 && hooks.Count > 0)
            {
                double best = double.MaxValue; Gun bg = null; IMyEntity bh = null;
                foreach (Gun g in left)
                    foreach (IMyEntity h in hooks)
                    {
                        double d = Vector3D.DistanceSquared(h.WorldMatrix.Translation, g.Block.WorldMatrix.Translation);
                        if (d < best) { best = d; bg = g; bh = h; }
                    }
                bg.Hook = bh; left.Remove(bg); hooks.Remove(bh);
            }
            Log("hooks found " + found.Count + " for " + guns.Count + " grapple blocks");
        }

        void FindHook(Gun g)
        {
            if (g.Hook == null || g.Hook.MarkedForClose) AssignHooks();
        }

        void Sample(Gun g)
        {
            if (g.Block.MarkedForClose || g.Block.Closed) return;
            FindHook(g);
            IMyGunObject<MyGunBase> gb = g.Block as IMyGunObject<MyGunBase>;
            string hookAt = g.Hook == null ? "none" : (g.Hook.WorldMatrix.Translation.X > 1e30 ? "hidden" : Vector3D.Distance(g.Hook.WorldMatrix.Translation, g.Block.WorldMatrix.Translation).ToString("0.0") + "m from gun");
            Log(g.Name + " t=" + (tick / 60) + "s hook=" + hookAt
                + " IsShooting=" + g.Block.IsShooting + " working=" + g.Block.IsWorking
                + " ammo=" + (gb == null ? "?" : gb.GunBase.HasEnoughAmmunition() + "/" + gb.GunBase.CurrentAmmo + " reload=" + gb.GunBase.ReloadTime)
                + " inv=" + g.Block.GetInventory(0).ItemCount
                + " winch=" + ((IMyTerminalBlock)g.Block).GetValueFloat("Winch").ToString("0.0")
                + (MyAPIGateway.Session.ControlledObject?.Entity == g.Block ? " CONTROLLED" : ""));
        }
    }
}
