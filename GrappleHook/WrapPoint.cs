using ProtoBuf;
using System.Xml.Serialization;
using VRage.ModAPI;
using VRageMath;

namespace GrappleHook
{
    /// <summary>
    /// A point the rope bends around, held in the frame of the grid or voxel it caught on so it
    /// moves with it. Worked out by the server and synced to clients.
    /// </summary>
    [ProtoContract]
    public class WrapPoint
    {
        [ProtoMember(1)]
        public long EntityId;

        /// <summary>Where the rope touches, in the entity's frame.</summary>
        [ProtoMember(2)]
        public Vector3D Local;

        /// <summary>Which way the rope bent when it caught (RopeWrap.BendNormal), in the entity's frame.</summary>
        [ProtoMember(3)]
        public Vector3D LocalBend;

        [XmlIgnore]
        public IMyEntity Entity;

        public WrapPoint() { }

        public WrapPoint(IMyEntity entity, Vector3D world, Vector3D bend)
        {
            Entity = entity;
            EntityId = entity.EntityId;
            MatrixD inverse = MatrixD.Invert(entity.WorldMatrix);
            Local = Vector3D.Transform(world, inverse);
            LocalBend = Vector3D.TransformNormal(bend, inverse);
        }

        /// <summary>The entity, looked up by id the first time (a client receives only the id).</summary>
        public bool Resolve()
        {
            if (Entity == null || Entity.MarkedForClose)
            {
                Entity = EntityId != 0 ? Sandbox.ModAPI.MyAPIGateway.Entities.GetEntityById(EntityId) : null;
            }
            return Entity != null && !Entity.MarkedForClose;
        }

        public Vector3D World
        {
            get { return Vector3D.Transform(Local, Entity.WorldMatrix); }
        }

        public Vector3D WorldBend
        {
            get { return Vector3D.TransformNormal(LocalBend, Entity.WorldMatrix); }
        }
    }
}
