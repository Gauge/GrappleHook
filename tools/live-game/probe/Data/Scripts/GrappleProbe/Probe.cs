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
                    if (sub.StartsWith("GrappleHook"))
                        guns.Add(new Gun { Name = sub.Replace("GrappleHookTurret", "").Replace("GrappleHook", "").ToUpperInvariant(), Block = (IMyUserControllableGun)b.FatBlock });
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
