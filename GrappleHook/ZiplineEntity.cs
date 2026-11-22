using ProtoBuf;
using Sandbox.ModAPI;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage.Game.ModAPI;
using VRageMath;

namespace GrappleHook
{
    /// <summary>
    /// A player riding the rope. Their place is a distance along the rope from the launcher,
    /// so they stay on it when the rope moves or swings.
    /// </summary>
    [ProtoContract]
    public class ZiplineEntity
    {
        [ProtoMember(1)]
        public long playerId;

        /// <summary>True when riding towards the hook, false towards the launcher.</summary>
        [ProtoMember(2)]
        public bool direction;

        /// <summary>Metres along the rope from the launcher.</summary>
        [ProtoMember(7)]
        public double distance;

        /// <summary>m/s along the rope.</summary>
        [ProtoMember(8)]
        public double speed;

        /// <summary>The pulley's world position this frame. Worked out locally, not synced.</summary>
        [XmlIgnore]
        public Vector3D pulley;

        [XmlIgnore]
        public Vector3D lastPulley;

        [XmlIgnore]
        public bool hasLastPulley;

        [XmlIgnore]
        public IMyPlayer player;

        public ZiplineEntity() { }

        public ZiplineEntity(long pid, bool d)
        {
            playerId = pid;
            direction = d;
        }

        // reused: this runs per rider per frame until the player resolves, and on a client
        // that has not seen the rider yet it never does
        private static readonly List<IMyPlayer> PlayerBuffer = new List<IMyPlayer>();

        public static void Populate(ref ZiplineEntity ziplineData)
        {
            if (ziplineData.player != null) return;

            PlayerBuffer.Clear();
            MyAPIGateway.Players.GetPlayers(PlayerBuffer);

            for (int i = 0; i < PlayerBuffer.Count; i++)
            {
                IMyPlayer p = PlayerBuffer[i];
                if (ziplineData.playerId == p.IdentityId)
                {
                    ziplineData.player = p;
                    break;
                }
            }

            PlayerBuffer.Clear();
        }
    }
}
