using System.Collections.Generic;
using VRageMath;

namespace GrappleHook.Baseline
{
    /// <summary>
    /// The geometry of a rope bent around obstacles: a polyline from the launcher through
    /// each point it wraps around to the hook. Pure math, so it can be tested outside the game.
    /// </summary>
    public static class RopeWrap
    {
        /// <summary>
        /// Which way the rope bends at a wrap point. Its sign is kept from when the rope
        /// caught on the edge, to tell when it swings back off.
        /// </summary>
        public static Vector3D BendNormal(Vector3D previous, Vector3D wrap, Vector3D next)
        {
            return Vector3D.Cross(previous - wrap, next - wrap);
        }

        /// <summary>
        /// True once the rope has swung back past straight at a wrap point, so it no longer
        /// bends around the edge it caught on.
        /// </summary>
        public static bool ShouldUnwrap(Vector3D previous, Vector3D wrap, Vector3D next, Vector3D bendNormal)
        {
            return Vector3D.Dot(BendNormal(previous, wrap, next), bendNormal) <= 0;
        }

        /// <summary>
        /// Direction of the force a taut rope presses into the edge it bends around: the sum of
        /// the two directions it leaves in. Its length grows from 0 (straight) to 2 (doubled back).
        /// </summary>
        public static Vector3D PressDirection(Vector3D previous, Vector3D wrap, Vector3D next)
        {
            return Direction(wrap, previous) + Direction(wrap, next);
        }

        /// <summary>Unit vector from a to b, or zero if they coincide.</summary>
        public static Vector3D Direction(Vector3D a, Vector3D b)
        {
            Vector3D d = b - a;
            double length = d.Length();
            return length > 1e-9 ? d / length : Vector3D.Zero;
        }

        public static double Length(List<Vector3D> points)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++)
            {
                length += Vector3D.Distance(points[i - 1], points[i]);
            }
            return length;
        }
    }
}
