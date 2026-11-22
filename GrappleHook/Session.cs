using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SENetworkAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace GrappleHook
{
	[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
	public class Session : MySessionComponentBase
	{
		public const ushort ModId = 32144;
		public const string ModName = "GrappleHook";


        /// <summary>
        /// The server reads its settings once, before any grapple block loads (the blocks used
        /// to race each other to read and write the file).
        /// </summary>
        public override void LoadData()
        {
            if (MyAPIGateway.Session.IsServer)
            {
                Settings.Init();
            }
        }

        protected override void UnloadData()
        {
            Settings.Instance = null;
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
            Tools.DebugMode = false;
            NetworkAPI.LogNetworkTraffic = false;

			if (!NetworkAPI.IsInitialized)
			{
				NetworkAPI.Init(ModId, ModName);
			}
		}
    }
}

