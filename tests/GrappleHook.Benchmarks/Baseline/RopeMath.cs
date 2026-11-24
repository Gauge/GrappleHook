using System;
using VRageMath;

namespace GrappleHook.Baseline
{
    /// <summary>
    /// Shape of the rope. Pure math, no game state, so it can be tested outside the game.
    /// </summary>
    public static class RopeMath
    {
        public const int MinSegmentPoints = 2;

        /// <summary>Slack below this fraction of the span is drawn as a straight, taut rope.</summary>
        public const double TautRatio = 1e-6;

        public static double Asinh(double x)
        {
            return x < 0 ? -Asinh(-x) : Math.Log(x + Math.Sqrt(x * x + 1d));
        }

        public static double Atanh(double x)
        {
            return 0.5d * Math.Log((1d + x) / (1d - x));
        }

        /// <summary>
        /// Solves sinh(xi) / xi = r for xi &gt; 0, the shape parameter of a hanging rope: r is
        /// how much longer the rope is than the chord it spans (corrected for the height
        /// difference), and xi is half the span measured in catenary parameters. Returns 0 for
        /// a rope with no slack.
        /// </summary>
        public static double SolveCatenaryShape(double r)
        {
            if (r <= 1d) return 0d;

            // Both guesses are right of the root: sinh(x)/x >= 1 + x^2/6 bounds the first, and
            // sinh(2 ln 2r) ~ 2r^2 >= 2r ln 2r the second. The function is convex there, so
            // Newton walks down onto the root without overshooting. Typically 3 to 6 steps.
            double xi = Math.Sqrt(6d * (r - 1d));
            if (r > 3d) xi = Math.Min(xi, 2d * Math.Log(2d * r));

            for (int i = 0; i < 60; i++)
            {
                double f = Math.Sinh(xi) - r * xi;
                double df = Math.Cosh(xi) - r;
                if (df <= 0) break;

                double step = f / df;
                xi -= step;
                if (Math.Abs(step) <= 1e-12 * xi) break;
            }

            return xi;
        }

        /// <summary>
        /// Fills <paramref name="path"/> with <paramref name="count"/> points from start to end
        /// along a rope <paramref name="ropeLength"/> long hanging under gravity along
        /// <paramref name="down"/>: a catenary, with its low point shifted towards the lower end.
        /// The points are evenly spaced along the rope. A rope with no slack, or no gravity to
        /// hang it, is a straight line.
        /// </summary>
        public static void ComputeCurvePoints(RopePath path, Vector3D start, Vector3D end, Vector3D down, double ropeLength, int count)
        {
            path.Clear();
            AppendCurvePoints(path, start, end, down, ropeLength, count, false);
        }

        /// <summary>
        /// As <see cref="ComputeCurvePoints"/>, appended to what is already in the path.
        /// <paramref name="skipStart"/> leaves out the first point, for a span that continues
        /// from the last point already there.
        /// </summary>
        public static void AppendCurvePoints(RopePath path, Vector3D start, Vector3D end, Vector3D down, double ropeLength, int count, bool skipStart)
        {
            if (count < MinSegmentPoints)
            {
                count = MinSegmentPoints;
            }

            path.EnsureCapacity(path.Count + count);

            Vector3D span = end - start;
            double spanLength = span.Length();
            double downLength = down.Length();
            double last = count - 1;
            int first = skipStart ? 1 : 0;

            if (downLength < 1e-9 || ropeLength <= spanLength * (1d + TautRatio) || ropeLength <= 1e-9)
            {
                for (int i = first; i < count; i++)
                {
                    path.Points[path.Count++] = Vector3D.Lerp(start, end, i / last);
                }
                return;
            }

            Vector3D dn = down / downLength;
            double rise = -Vector3D.Dot(span, dn);
            Vector3D horizontal = span + dn * rise;
            double h = horizontal.Length();
            Vector3D across;

            if (h < 1e-6)
            {
                // hanging straight down: any horizontal will do, the rope folds in a narrow loop
                across = Perpendicular(dn);
                h = 1e-6;
            }
            else
            {
                across = horizontal / h;
            }

            double chord = Math.Sqrt(ropeLength * ropeLength - rise * rise);
            double xi = SolveCatenaryShape(chord / h);
            if (xi <= 0)
            {
                for (int i = first; i < count; i++)
                {
                    path.Points[path.Count++] = Vector3D.Lerp(start, end, i / last);
                }
                return;
            }

            // y = a cosh(u) with u = (x - x0) / a, the vertex at x0. The start sits at u0.
            double a = h / (2d * xi);
            double x0 = h * 0.5d - a * Atanh(rise / ropeLength);
            double u0 = -x0 / a;
            double sinhU0 = Math.Sinh(u0);

            for (int i = first; i < count; i++)
            {
                if (i == count - 1)
                {
                    path.Points[path.Count++] = end;
                    continue;
                }

                if (i == 0)
                {
                    path.Points[path.Count++] = start;
                    continue;
                }

                double s = ropeLength * i / last;
                double u = Asinh(s / a + sinhU0);
                double x = a * (u - u0);
                // cosh(u) - cosh(u0), written so it keeps its precision when the rope is nearly taut
                double y = 2d * a * Math.Sinh((u + u0) * 0.5d) * Math.Sinh((u - u0) * 0.5d);

                path.Points[path.Count++] = start + across * x - dn * y;
            }
        }

        /// <summary>Any unit vector at right angles to <paramref name="v"/>, a unit vector.</summary>
        public static Vector3D Perpendicular(Vector3D v)
        {
            Vector3D axis = Math.Abs(v.X) < 0.9d ? Vector3D.UnitX : Vector3D.UnitY;
            return Vector3D.Normalize(Vector3D.Cross(v, axis));
        }

        /// <summary>
        /// Half the width of the region a rope of <paramref name="ropeLength"/> between two
        /// points can reach: every point of it is within that of the straight line between them.
        /// </summary>
        public static double ReachAcross(double spanLength, double ropeLength)
        {
            return ropeLength > spanLength ? Math.Sqrt(ropeLength * ropeLength - spanLength * spanLength) * 0.5d : 0d;
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
