using System;
using System.Collections.Generic;
using VRageMath;

namespace GrappleHook.Baseline
{
    /// <summary>
    /// Builds the rope's shape each frame from its corners (muzzle, wrap points, hook): on a
    /// client a swinging chain per span (RopeChain), on a dedicated server the exact catenary
    /// (RopeMath). Pure math, so it can be tested and measured outside the game.
    /// </summary>
    public class RopeShape
    {
        private readonly List<RopeChain> chains = new List<RopeChain>();
        private readonly RopePath shapeBuffer = new RopePath();

        /// <summary>
        /// One tick of the drawn rope. <paramref name="ropeLength"/> is the rope out, shared
        /// among the spans by their length. A chain that is new, or whose ends jumped, starts
        /// from the shape it would hang in; with no gravity it bows along
        /// <paramref name="fallbackDown"/>. Fills <paramref name="path"/> and its distances.
        /// </summary>
        public void StepChains(RopePath path, List<Vector3D> anchors, double ropeLength, Vector3D gravity, double airDrag, Vector3D fallbackDown, int drawPoints, double keep, int iterations)
        {
            path.Clear();
            if (anchors.Count < 2) return;

            double total = RopeWrap.Length(anchors);
            double scale = total > 1e-9 ? ropeLength / total : 1d;
            int spans = anchors.Count - 1;

            if (chains.Count != spans)
            {
                while (chains.Count < spans) chains.Add(new RopeChain());
                while (chains.Count > spans) chains.RemoveAt(chains.Count - 1);
                for (int s = 0; s < spans; s++) chains[s].Active = false;
            }

            Vector3D gravityStep = gravity * (WinchMath.TickSeconds * WinchMath.TickSeconds);
            Vector3D down = gravity.LengthSquared() > 1e-6 ? gravity : fallbackDown;

            for (int s = 0; s < spans; s++)
            {
                RopeChain chain = chains[s];
                double span = Vector3D.Distance(anchors[s], anchors[s + 1]);
                double restLength = span * scale;

                if (!chain.Step(anchors[s], anchors[s + 1], restLength, gravityStep, keep, airDrag, iterations))
                {
                    RopeMath.ComputeCurvePoints(shapeBuffer, anchors[s], anchors[s + 1], down, restLength, PointsFor(drawPoints, anchors.Count, span, total));
                    chain.Reset(shapeBuffer.Points, 0, shapeBuffer.Count);
                }

                for (int i = s > 0 ? 1 : 0; i < chain.Count; i++)
                {
                    path.Add(chain.Points[i]);
                }
            }

            path.UpdateDistances();
        }

        /// <summary>
        /// The exact hanging shape, for a dedicated server that draws nothing but carries
        /// riders. Fills <paramref name="path"/> and its distances.
        /// </summary>
        public static void BuildCatenary(RopePath path, List<Vector3D> anchors, double ropeLength, Vector3D gravity, int drawPoints)
        {
            path.Clear();
            if (anchors.Count < 2) return;

            double total = RopeWrap.Length(anchors);
            double scale = total > 1e-9 ? ropeLength / total : 1d;
            int spans = anchors.Count - 1;

            for (int s = 0; s < spans; s++)
            {
                double span = Vector3D.Distance(anchors[s], anchors[s + 1]);
                RopeMath.AppendCurvePoints(path, anchors[s], anchors[s + 1], gravity, span * scale, PointsFor(drawPoints, anchors.Count, span, total), s > 0);
            }

            path.UpdateDistances();
        }

        /// <summary>Stops every chain, so each starts again from its hanging shape.</summary>
        public void Deactivate()
        {
            for (int i = 0; i < chains.Count; i++) chains[i].Active = false;
        }

        /// <summary>The rope's points shared among its spans by length.</summary>
        public static int PointsFor(int drawPoints, int anchorCount, double span, double total)
        {
            if (anchorCount <= 2 || total <= 0) return drawPoints;
            return Math.Max(RopeMath.MinSegmentPoints, (int)Math.Round(drawPoints * span / total) + 1);
        }
    }
}
