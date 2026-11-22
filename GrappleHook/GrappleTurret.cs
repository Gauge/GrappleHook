using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;

namespace GrappleHook
{
    /// <summary>
    /// The turret variant: aims on its own two axes and fires wherever it is pointed.
    /// </summary>
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), true, "GrappleHookTurretLarge", "GrappleHookTurretSmall")]
    public class GrappleTurret : WeaponControlLayer
    {
        protected override void InitWeapon()
        {
            // the turret must not acquire targets of its own - the grapple is aimed by hand
            IMyLargeTurretBase turret = Weapon as IMyLargeTurretBase;
            if (turret != null)
            {
                turret.Range = 0;
            }
        }

        /// <summary>
        /// Registered against IMyLargeTurretBase, not IMyLargeMissileTurret: the terminal factory
        /// does not recognise SpaceEngineers.Game.ModAPI.IMyLargeMissileTurret at all (no
        /// controls, no actions, CreateControl returns null), which crashed the game on any world
        /// with this block. Checked in game on 1.211; IMyLargeTurretBase is accepted.
        /// </summary>
        protected override void CreateTerminalControls()
        {
            TerminalControls.Create<IMyLargeTurretBase>(Config, ModContext.ModPath);
        }
    }
}
