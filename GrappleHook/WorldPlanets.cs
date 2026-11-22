using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRageMath;

namespace GrappleHook
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class WorldPlanets : MySessionComponentBase
    {
        private class Planet
        {
            public Vector3D Position;
            public MyGravityProviderComponent Gravity;
            public MyPlanet GamePlanet;
        }

        private static List<Planet> Planets = new List<Planet>();

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            MyAPIGateway.Entities.OnEntityAdd += AddPlanet;
            MyAPIGateway.Entities.OnEntityRemove += RemovePlanet;
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Entities.OnEntityAdd -= AddPlanet;
            MyAPIGateway.Entities.OnEntityRemove -= RemovePlanet;
            Planets.Clear();
        }

        private void AddPlanet(IMyEntity ent)
        {
            if (ent is MyPlanet)
            {
                MyPlanet gamePlanet = ent as MyPlanet;

                Planet p = new Planet()
                {
                    Position = gamePlanet.PositionComp.GetPosition(),
                    Gravity = gamePlanet.Components.Get<MyGravityProviderComponent>(),
                    GamePlanet = gamePlanet
                };

                Planets.Add(p);
            }
        }

        private void RemovePlanet(IMyEntity ent)
        {
            for (int i = 0; i < Planets.Count; i++)
            {
                if (Planets[i].GamePlanet.EntityId == ent.EntityId)
                {
                    Planets.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// The gravity vector at a location. Called every frame by every attached grapple to
        /// work out which way the rope sags, so it allocates nothing.
        /// </summary>
        public static Vector3D GetGravity(Vector3D worldPosition)
        {
            Vector3D gravity = Vector3D.Zero;

            for (int i = 0; i < Planets.Count; i++)
            {
                gravity += Planets[i].Gravity.GetWorldGravity(worldPosition);
            }

            return gravity;
        }

        /// <summary>
        /// Air density at a location, summed over every planet. Not used by the rope; kept
        /// because it is the other half of what the planet list is for.
        /// </summary>
        public static float GetAirDensity(Vector3D worldPosition)
        {
            float pressure = 0;

            for (int i = 0; i < Planets.Count; i++)
            {
                if (Planets[i].GamePlanet.HasAtmosphere)
                {
                    pressure += Planets[i].GamePlanet.GetAirDensity(worldPosition);
                }
            }

            return pressure;
        }
    }
}
