using System;
using VRageMath;

namespace GrappleHook.Baseline
{
    /// <summary>
    /// A reusable buffer of world space points describing the rope, start to end. Several
    /// things read the rope each frame (the draw, the zipline grab check, the zipline riders),
    /// so it lives in a buffer owned by the block instead of a fresh array per call.
    /// </summary>
    public class RopePath
    {
        public Vector3D[] Points = new Vector3D[0];

        /// <summary>Arc length from the start to each point. Valid after <see cref="UpdateDistances"/>.</summary>
        public double[] Distances = new double[0];

        /// <summary>
        /// Number of valid entries in <see cref="Points"/>. The array itself may be longer.
        /// </summary>
        public int Count;

        /// <summary>Length along the points. Valid after <see cref="UpdateDistances"/>.</summary>
        public double Length;

        public int SegmentCount
        {
            get { return (Count < 2) ? 0 : Count - 1; }
        }

        /// <summary>Grows the buffer, keeping what is in it. Never shrinks it.</summary>
        public void EnsureCapacity(int count)
        {
            if (Points.Length < count)
            {
                Array.Resize(ref Points, Math.Max(count, Points.Length * 2));
            }
        }

        public void Add(Vector3D point)
        {
            EnsureCapacity(Count + 1);
            Points[Count++] = point;
        }

        public void Clear()
        {
            Count = 0;
            Length = 0;
        }

        public void UpdateDistances()
        {
            if (Distances.Length < Points.Length)
            {
                Distances = new double[Points.Length];
            }

            Length = 0;
            for (int i = 0; i < Count; i++)
            {
                if (i > 0)
                {
                    Length += Vector3D.Distance(Points[i - 1], Points[i]);
                }
                Distances[i] = Length;
            }
        }
    }
}
