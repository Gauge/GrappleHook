using System;
using VRageMath;

namespace GrappleHook.Core
{
    /// <summary>
    /// Shape of the rope. Pure math, no game state, so it can be tested outside the game.
    /// </summary>
    public static class RopeMath
    {
        public const int MinSegmentPoints = 2;

        /// <summary>
        /// Normalised sag profile over a rope span. Zero at both ends, 1 in the middle.
        /// </summary>
        public static double SagProfile(double u)
        {
            return -4d * u * u + 4d * u;
        }

        /// <summary>
        /// How far the middle of the rope hangs below the straight line between its ends.
        /// Slack is the difference between the length of rope paid out and the distance
        /// actually spanned.
        /// </summary>
        public static double SagAmount(double spanLength, double referenceLength)
        {
            return Math.Max(0d, referenceLength - spanLength) * 0.5d;
        }

        /// <summary>
        /// Fills <paramref name="path"/> with <paramref name="count"/> points from start to
        /// end, bowed along <paramref name="sagDirection"/> by however much rope is slack.
        /// </summary>
        public static void ComputeCurvePoints(RopePath path, Vector3D start, Vector3D end, Vector3D sagDirection, double referenceLength, int count)
        {
            if (count < MinSegmentPoints)
            {
                count = MinSegmentPoints;
            }

            path.EnsureCapacity(count);
            path.Count = count;

            double sagAmount = SagAmount(Vector3D.Distance(start, end), referenceLength);
            double last = count - 1;

            for (int i = 0; i < count; i++)
            {
                double u = i / last;
                double v = 1d - u;

                Vector3D point = start * v + end * u;
                if (sagAmount > 0)
                {
                    point += sagDirection * SagProfile(u) * sagAmount;
                }

                path.Points[i] = point;
            }
        }

        /// <summary>
        /// True when the ray passes within <paramref name="range"/> of the rope. Used to
        /// decide whether the player is looking at the rope closely enough to grab it.
        /// </summary>
        public static bool RayHitsPath(RopePath path, ref RayD ray, double range)
        {
            for (int i = 0; i < path.SegmentCount; i++)
            {
                Vector3D a = path.Points[i];
                Vector3D b = path.Points[i + 1];

                BoundingBoxD bounds = new BoundingBoxD(Vector3D.Min(a, b), Vector3D.Max(a, b));

                double? result;
                bounds.Intersects(ref ray, out result);
                if (result.HasValue && result.Value < range)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True once the projectile in flight has travelled further than the rope is long.
        /// </summary>
        public static bool ExceededRange(Vector3D origin, Vector3D projectile, double maxLength)
        {
            return (origin - projectile).LengthSquared() > maxLength * maxLength;
        }
    }
}
