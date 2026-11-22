using VRageMath;

namespace GrappleHook.Core
{
    /// <summary>
    /// A reusable buffer of world space points describing the rope.
    /// The rope is recomputed several times a frame (draw, zipline interaction, zipline
    /// forces) so the points live in a buffer owned by the block instead of a fresh array
    /// per call.
    /// </summary>
    public class RopePath
    {
        public Vector3D[] Points = new Vector3D[0];

        /// <summary>
        /// Number of valid entries in <see cref="Points"/>. The array itself may be longer.
        /// </summary>
        public int Count;

        public int SegmentCount
        {
            get { return (Count < 2) ? 0 : Count - 1; }
        }

        public void EnsureCapacity(int count)
        {
            if (Points.Length < count)
            {
                Points = new Vector3D[count];
            }
        }

        public void Clear()
        {
            Count = 0;
        }
    }
}
