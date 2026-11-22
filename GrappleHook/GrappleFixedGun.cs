using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;

namespace GrappleHook
{
    /// <summary>
    /// The fixed variant: no traverse, it fires along the block's forward axis and is aimed
    /// by turning the ship. Everything downstream of the shot - the rope, the winch, the
    /// zipline - is identical to the turret.
    ///
    /// MyGunBase takes the muzzle from the model's muzzle_projectile dummy (the grapple ammo is
    /// a projectile type), which each launcher model puts at the breech of its tube, so the
    /// hook rests in the tube and the shot starts there, along the block's forward axis.
    ///
    /// Like the vanilla fixed guns it can be selected from a cockpit's toolbar as a weapon
    /// group (Data/BlockCategories.sbc lists it under the ship weapons). Then left click hooks
    /// and right click lets go, for every launcher of the type on the cockpit's grid.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallMissileLauncher), true, "GrappleHookFixedLarge", "GrappleHookFixedSmall")]
    public class GrappleFixedGun : WeaponControlLayer
    {
        /// <summary>The gun object's shooting state last frame, to catch it starting.</summary>
        private bool gunWasShooting;

        protected override void CreateTerminalControls()
        {
            TerminalControls.Create<IMySmallMissileLauncher>(Config, ModContext.ModPath);
        }

        protected override void UpdateWeaponGroup()
        {
            // Left click on the selected group fires the vanilla gun: it spends a magazine and
            // its gun object reports shooting from then until it may fire again (its whole
            // reload). The moment it starts is the shot, so that edge launches the hook, once.
            // The server does it; the hook's state reaches clients from there.
            bool shooting = Gun.IsShooting;
            if (shooting && !gunWasShooting && MyAPIGateway.Session.IsServer)
            {
                Tools.Debug("Fired as a cockpit weapon group");
                VerifyAndRequestShoot(true);
            }
            gunWasShooting = shooting;

            if (!IsDedicated && IsAttached)
            {
                UpdateReleaseInput();
            }
        }

        /// <summary>
        /// Right click lets go, while the local player flies the ship from a seat whose selected
        /// weapon group is this launcher's type. The game itself never passes the secondary
        /// action to a missile launcher (its CanShoot refuses everything but primary), so the
        /// mod reads the input, as the turret does for its own right click.
        /// </summary>
        private void UpdateReleaseInput()
        {
            if (!MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.SECONDARY_TOOL_ACTION)) return;
            if (MyAPIGateway.Gui.IsCursorVisible || MyAPIGateway.Gui.ChatEntryVisible) return;

            IMyShipController seat = MyAPIGateway.Session.ControlledObject as IMyShipController;
            // a weapon group is the guns of one type on the seat's own grid
            if (seat == null || seat.CubeGrid != Weapon.CubeGrid) return;

            MyObjectBuilder_ShipController state = seat.GetObjectBuilderCubeBlock(false) as MyObjectBuilder_ShipController;
            if (state == null || !state.SelectedGunId.HasValue || (MyDefinitionId)state.SelectedGunId.Value != (MyDefinitionId)Weapon.BlockDefinition) return;

            Tools.Debug("Detach requested by the weapon group's secondary action");
            RequestDetach();
        }
    }
}
