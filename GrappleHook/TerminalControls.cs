using GrappleHook.Core;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Utils;

namespace GrappleHook
{
    /// <summary>
    /// The terminal surface of the grapple blocks: it hides the vanilla weapon controls that
    /// mean nothing for a grapple, repoints the shoot controls at the grapple, and adds the
    /// detach and winch controls.
    ///
    /// Registration happens against the concrete block interface (IMyLargeMissileTurret,
    /// IMySmallMissileLauncher), never IMyTerminalBlock. The game keeps one control list per
    /// concrete block class and fills it with a *copy* of every base class list the first
    /// time that class is asked for its controls (MyTerminalControlFactory.InitializeControls).
    /// Anything a mod adds to the shared MyTerminalBlock list after that snapshot is taken
    /// never reaches the block - and whether the snapshot is taken before or after the mod
    /// runs depends on whether anything happened to open a terminal or enumerate actions for
    /// that block class first. Adding to the concrete class instead appends to the live list
    /// and always shows up.
    ///
    /// This must still run from a block's game logic rather than from session load: the
    /// vanilla controls of a block class are created by the first instance of it
    /// (MyTerminalBlock.BeforeGameLogicInit), so registering before any grapple block exists
    /// would snapshot base lists that are still empty.
    /// </summary>
    public static class TerminalControls
    {
        private static readonly HashSet<string> Registered = new HashSet<string>();

        /// <summary>
        /// Builds the terminal for one block class. Safe to call from every block; only the
        /// first call for a given class does anything. modPath is the mod's folder, where the
        /// action icons are (Textures\GUI\Icons\Actions, made by tools/model/action_icons.py).
        /// </summary>
        public static void Create<TBlock>(Settings settings, string modPath)
        {
            if (!Registered.Add(typeof(TBlock).Name)) return;

            Tools.Debug($"Building terminal controls for {typeof(TBlock).Name}");

            OverrideDefaultControls<TBlock>();
            CreateControls<TBlock>(settings, modPath + "\\Textures\\GUI\\Icons\\Actions\\");
        }

        private static bool IsGrapple(IMyTerminalBlock block)
        {
            return block.GameLogic.GetAs<WeaponControlLayer>() != null;
        }

        private static void DisableAction(IMyTerminalAction a)
        {
            Func<IMyTerminalBlock, bool> oldEnabled = a.Enabled;
            a.Enabled = (block) =>
            {
                if (IsGrapple(block))
                {
                    return false;
                }

                return oldEnabled.Invoke(block);
            };
        }

        private static void HideControl(IMyTerminalControl a)
        {
            Func<IMyTerminalBlock, bool> oldVisiable = a.Visible;
            a.Visible = (block) =>
            {
                if (IsGrapple(block))
                {
                    return false;
                }

                return oldVisiable.Invoke(block);
            };
        }

        private static void OverrideDefaultControls<T>()
        {
            // Every "old" delegate below is declared inside its loop on purpose: each wrapper
            // captures its own copy. They were once declared here, once for the method, so every
            // wrapper on a vanilla block called whichever delegate was assigned last - a vanilla
            // turret's Shoot On, Shoot Off and Shoot toggle all fired once instead.

            List<IMyTerminalAction> actions = new List<IMyTerminalAction>();
            MyAPIGateway.TerminalControls.GetActions<T>(out actions);
            foreach (IMyTerminalAction a in actions)
            {
                Action<IMyTerminalBlock> oldAction = a.Action;
                Action<IMyTerminalBlock, StringBuilder> oldWriter = a.Writer;

                if (WeaponControlIds.IsBannedAction(a.Id))
                {
                    DisableAction(a);
                }
                else if (a.Id == WeaponControlIds.Shoot)
                {
                    a.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.terminalShootOn = !layer.terminalShootOn;

                            if (layer.terminalShootOn)
                            {
                                layer.VerifyAndRequestShoot(true);
                            }
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };

                    a.Writer = (block, text) =>
                    {
                        WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (logic != null)
                        {
                            text.Append(logic.terminalShootOn ? "On" : "Off");
                        }
                        else
                        {
                            oldWriter?.Invoke(block, text);
                        }
                    };
                }
                else if (a.Id == WeaponControlIds.ShootOn)
                {
                    a.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.terminalShootOn = true;
                            layer.VerifyAndRequestShoot(true);
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };

                    a.Writer = (block, text) =>
                    {
                        WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (logic != null)
                        {
                            text.Append(logic.terminalShootOn ? "On" : "Off");
                        }
                        else
                        {
                            oldWriter?.Invoke(block, text);
                        }
                    };
                }
                else if (a.Id == WeaponControlIds.ShootOff)
                {
                    a.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.terminalShootOn = false;
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };

                    a.Writer = (block, text) =>
                    {
                        WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (logic != null)
                        {
                            text.Append(logic.terminalShootOn ? "On" : "Off");
                        }
                        else
                        {
                            oldWriter?.Invoke(block, text);
                        }
                    };
                }
                else if (a.Id == WeaponControlIds.ShootOnce)
                {
                    a.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.VerifyAndRequestShoot(true);
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };
                }
            }

            List<IMyTerminalControl> controls = new List<IMyTerminalControl>();
            MyAPIGateway.TerminalControls.GetControls<T>(out controls);
            foreach (IMyTerminalControl c in controls)
            {
                if (WeaponControlIds.IsBannedControl(c.Id))
                {
                    HideControl(c);
                }
                else if (c.Id == WeaponControlIds.Shoot)
                {
                    IMyTerminalControlOnOffSwitch onoff = c as IMyTerminalControlOnOffSwitch;
                    if (onoff == null) continue;

                    Func<IMyTerminalBlock, bool> oldGetter = onoff.Getter;
                    Action<IMyTerminalBlock, bool> oldSetter = onoff.Setter;

                    onoff.Setter = (block, value) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.terminalShootOn = !layer.terminalShootOn;

                            if (layer.terminalShootOn)
                            {
                                layer.VerifyAndRequestShoot(true);
                            }
                        }
                        else
                        {
                            oldSetter?.Invoke(block, value);
                        }
                    };

                    onoff.Getter = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            return layer.terminalShootOn;
                        }

                        return oldGetter.Invoke(block);
                    };
                }
                else if (c.Id == WeaponControlIds.ShootOnce)
                {
                    IMyTerminalControlButton button = c as IMyTerminalControlButton;
                    if (button == null) continue;

                    Action<IMyTerminalBlock> oldAction = button.Action;
                    button.Action = (block) =>
                    {
                        WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                        if (layer != null)
                        {
                            layer.VerifyAndRequestShoot(true);
                        }
                        else
                        {
                            oldAction?.Invoke(block);
                        }
                    };
                }
            }
        }

        private static void CreateControls<TBlock>(Settings settings, string icons)
        {
            Func<IMyTerminalBlock, bool> isThisMod = IsGrapple;

            IMyTerminalControlSlider sliderWinch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, TBlock>("Winch");
            if (sliderWinch == null)
            {
                // the factory returns null for a block interface it does not recognise; a null
                // here must not reach the game loop, where it crashes the game
                Tools.Error($"The terminal factory does not accept {typeof(TBlock).FullName}; grapple controls not created");
                return;
            }

            sliderWinch.Title = MyStringId.GetOrCompute("Tighten Winch");
            sliderWinch.Enabled = isThisMod;
            sliderWinch.Visible = isThisMod;
            sliderWinch.SetLimits(-settings.LoosenSpeed, settings.TightenSpeed);
            sliderWinch.Getter = (block) =>
            {
                WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                if (layer != null)
                {
                    return layer.Winch.Value;
                }

                return 0;
            };
            sliderWinch.Setter = (block, value) =>
            {
                WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                if (layer != null)
                {
                    layer.Winch.Value = value;
                }
            };
            sliderWinch.Writer = (block, builder) =>
            {
                WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                if (layer != null)
                {
                    builder.Append($"{layer.Winch.Value.ToString("n2")}m/s");
                }
            };

            IMyTerminalAction detach = MyAPIGateway.TerminalControls.CreateAction<TBlock>("Detach");
            detach.Name = new StringBuilder("Detach");
            detach.Icon = icons + "GrappleDetach.dds";
            detach.Enabled = isThisMod;
            detach.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.RequestDetach();
                }
            };
            detach.Writer = (block, text) => { text.Append("detach"); };

            IMyTerminalAction resetAction = MyAPIGateway.TerminalControls.CreateAction<TBlock>("ResetWinch");
            resetAction.Name = new StringBuilder("Reset");
            resetAction.Icon = icons + "GrappleWinchReset.dds";
            resetAction.Enabled = isThisMod;
            resetAction.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.Winch.Value = 0;
                    sliderWinch.UpdateVisual();
                }
            };
            resetAction.Writer = (block, text) => { text.Append("Reset"); };

            IMyTerminalAction tighten = MyAPIGateway.TerminalControls.CreateAction<TBlock>("TightenWinch");
            tighten.Name = new StringBuilder("Tighten");
            tighten.Icon = icons + "GrappleWinchTighten.dds";
            tighten.Enabled = isThisMod;
            tighten.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.Winch.Value = WinchMath.StepWinch(logic.Winch.Value, 1f, logic.Config.LoosenSpeed, logic.Config.TightenSpeed);
                    sliderWinch.UpdateVisual();
                }
            };
            tighten.Writer = (block, text) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    text.Append($"{logic.Winch.Value.ToString("n0")}");
                }
            };

            IMyTerminalAction loosen = MyAPIGateway.TerminalControls.CreateAction<TBlock>("LoosenWinch");
            loosen.Name = new StringBuilder("Loosen");
            loosen.Icon = icons + "GrappleWinchLoosen.dds";
            loosen.Enabled = isThisMod;
            loosen.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.Winch.Value = WinchMath.StepWinch(logic.Winch.Value, -1f, logic.Config.LoosenSpeed, logic.Config.TightenSpeed);
                    sliderWinch.UpdateVisual();
                }
            };
            loosen.Writer = (block, text) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    text.Append($"{logic.Winch.Value.ToString("n0")}");
                }
            };

            IMyTerminalControlButton detachControl = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, TBlock>("Detach");
            detachControl.Title = MyStringId.GetOrCompute("Detach");
            detachControl.Tooltip = MyStringId.GetOrCompute("Breaks active connection");
            detachControl.Visible = isThisMod;
            detachControl.Enabled = isThisMod;
            detachControl.Action = (block) =>
            {
                WeaponControlLayer logic = block.GameLogic.GetAs<WeaponControlLayer>();
                if (logic != null)
                {
                    logic.RequestDetach();
                }
            };

            IMyTerminalControlButton resetWinch = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, TBlock>("ResetWinch");
            resetWinch.Title = MyStringId.GetOrCompute("Reset Winch");
            resetWinch.Enabled = isThisMod;
            resetWinch.Visible = isThisMod;
            resetWinch.Action = (block) =>
            {
                WeaponControlLayer layer = block.GameLogic.GetAs<WeaponControlLayer>();
                if (layer != null)
                {
                    layer.Winch.Value = 0;
                    sliderWinch.UpdateVisual();
                }
            };

            MyAPIGateway.TerminalControls.AddControl<TBlock>(detachControl);
            MyAPIGateway.TerminalControls.AddControl<TBlock>(resetWinch);
            MyAPIGateway.TerminalControls.AddControl<TBlock>(sliderWinch);

            MyAPIGateway.TerminalControls.AddAction<TBlock>(detach);
            MyAPIGateway.TerminalControls.AddAction<TBlock>(resetAction);
            MyAPIGateway.TerminalControls.AddAction<TBlock>(tighten);
            MyAPIGateway.TerminalControls.AddAction<TBlock>(loosen);
        }
    }
}
