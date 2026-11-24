using System;
using VRageMath;

namespace GrappleHook.Baseline
{
    /// <summary>
    /// Where a rider hangs on the rope and how they slide along it. A rider's place is a
    /// distance along the rope from its start, so they move with the rope when it moves.
    /// Pure math, no game state, so it can be tested outside the game.
    /// </summary>
    public static class ZiplineMath
    {
        /// <summary>
        /// Distance along the rope to the point on it closest to <paramref name="position"/>.
        /// The path's distances must be up to date.
        /// </summary>
        public static double ClosestDistance(RopePath path, Vector3D position)
        {
            double best = double.MaxValue;
            double bestDistance = 0;

            for (int i = 0; i < path.SegmentCount; i++)
            {
                Vector3D a = path.Points[i];
                Vector3D ab = path.Points[i + 1] - a;
                double lengthSquared = ab.LengthSquared();
                double t = lengthSquared > 0 ? Math.Max(0d, Math.Min(1d, Vector3D.Dot(position - a, ab) / lengthSquared)) : 0d;

                double d = (a + ab * t - position).LengthSquared();
                if (d < best)
                {
                    best = d;
                    bestDistance = path.Distances[i] + (path.Distances[i + 1] - path.Distances[i]) * t;
                }
            }

            return bestDistance;
        }

        /// <summary>
        /// The point <paramref name="distance"/> along the rope and the rope's direction there
        /// (towards its end). The path's distances must be up to date.
        /// </summary>
        public static void PointAt(RopePath path, double distance, out Vector3D point, out Vector3D tangent)
        {
            tangent = Vector3D.Zero;

            if (path.Count == 0)
            {
                point = Vector3D.Zero;
                return;
            }

            point = path.Points[0];

            for (int i = 0; i < path.SegmentCount; i++)
            {
                double from = path.Distances[i];
                double to = path.Distances[i + 1];
                Vector3D segment = path.Points[i + 1] - path.Points[i];

                if (to > from)
                {
                    tangent = segment / (to - from);
                }

                if (distance <= to || i == path.SegmentCount - 1)
                {
                    double t = to > from ? Math.Max(0d, Math.Min(1d, (distance - from) / (to - from))) : 0d;
                    point = path.Points[i] + segment * t;
                    return;
                }
            }
        }

        /// <summary>
        /// The pull of gravity along the direction of travel, m/s². Positive when the rope runs
        /// downhill ahead of the rider.
        /// </summary>
        public static double PulleyAcceleration(Vector3D travelDirection, Vector3D gravity)
        {
            return Vector3D.Dot(gravity, travelDirection);
        }

        /// <summary>
        /// Speed of the pulley, m/s, after one tick of <paramref name="acceleration"/>. The
        /// pulley never drops below <paramref name="minSpeed"/>, so riders always reach the end.
        /// </summary>
        public static double StepPulleySpeed(double speed, double acceleration, double minSpeed)
        {
            return Math.Max(speed + acceleration * WinchMath.TickSeconds, minSpeed);
        }

        /// <summary>
        /// Moves a rider <paramref name="step"/> metres along a rope <paramref name="ropeLength"/>
        /// long, towards its end when <paramref name="direction"/> is true. Returns how far past
        /// the end they would have gone: more than zero means they ran off the rope.
        /// </summary>
        public static double AdvancePulley(double ropeLength, ref double distance, bool direction, double step)
        {
            distance += direction ? step : -step;

            if (distance > ropeLength)
            {
                double over = distance - ropeLength;
                distance = ropeLength;
                return over;
            }

            if (distance < 0)
            {
                double over = -distance;
                distance = 0;
                return over;
            }

            return 0;
        }
    }
}
