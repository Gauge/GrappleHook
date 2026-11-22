using System;
using VRageMath;

namespace GrappleHook.Core
{
    /// <summary>
    /// The rope as the player sees it: a chain of points pinned at both ends, moved by
    /// gravity and their own momentum and held to the rope's length (Verlet integration with
    /// length constraints). In gravity it settles into the catenary; in space a slack rope
    /// floats and keeps whatever swing it had. Only for drawing and riding - the forces on the
    /// grids come from the rope's length, not from these points.
    /// </summary>
    public class RopeChain
    {
        /// <summary>An end moving further than this in one tick has jumped, not moved.</summary>
        public const double TeleportDistance = 50d;

        public Vector3D[] Points = new Vector3D[0];
        private Vector3D[] previous = new Vector3D[0];

        public int Count;
        public bool Active;

        private Vector3D lastStart;
        private Vector3D lastEnd;

        /// <summary>Not stepped since its reset: it takes on its ends' motion on the first step.</summary>
        private bool fresh;

        /// <summary>Starts the chain from a shape, at rest.</summary>
        public void Reset(Vector3D[] shape, int offset, int count)
        {
            if (Points.Length < count)
            {
                Points = new Vector3D[count];
                previous = new Vector3D[count];
            }

            Array.Copy(shape, offset, Points, 0, count);
            Array.Copy(shape, offset, previous, 0, count);
            Count = count;
            lastStart = Points[0];
            lastEnd = Points[count - 1];
            Active = true;
            fresh = true;
        }

        /// <summary>
        /// One tick. <paramref name="gravityStep"/> is gravity times the tick squared.
        /// <paramref name="keep"/> is how much of its swing, relative to the ends, the rope keeps
        /// each tick; <paramref name="airDrag"/> is how much of its speed through the air it
        /// loses. Returns false if an end jumped, and the chain needs a <see cref="Reset"/>.
        /// </summary>
        public bool Step(Vector3D start, Vector3D end, double restLength, Vector3D gravityStep, double keep, double airDrag, int iterations)
        {
            if (!Active || Count < 2) return false;

            Vector3D startMoved = start - lastStart;
            Vector3D endMoved = end - lastEnd;
            if (startMoved.LengthSquared() > TeleportDistance * TeleportDistance || endMoved.LengthSquared() > TeleportDistance * TeleportDistance)
            {
                Active = false;
                return false;
            }

            lastStart = start;
            lastEnd = end;

            // the ends' own motion: damping works against swinging relative to them, so a slack
            // rope on a fast ship in space travels with it instead of trailing behind
            Vector3D frame = (startMoved + endMoved) * 0.5d;
            int last = Count - 1;

            // a chain started on a moving ship starts moving with it, rather than being yanked
            if (fresh)
            {
                fresh = false;
                for (int i = 1; i < last; i++) previous[i] = Points[i] - frame;
            }

            if (restLength <= Vector3D.Distance(start, end))
            {
                for (int i = 0; i <= last; i++)
                {
                    Points[i] = Vector3D.Lerp(start, end, (double)i / last);
                    previous[i] = Points[i] - frame;
                }
                return true;
            }

            Points[0] = start;
            Points[last] = end;

            for (int i = 1; i < last; i++)
            {
                Vector3D velocity = Points[i] - previous[i];
                velocity = frame + (velocity - frame) * keep;
                velocity *= 1d - airDrag;

                previous[i] = Points[i];
                Points[i] += velocity + gravityStep;
            }

            double segment = restLength / last;
            double segmentSquared = segment * segment;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                // alternate the sweep so neither end drags the error to the other
                bool forward = (iteration & 1) == 0;
                for (int k = 0; k < last; k++)
                {
                    int i = forward ? k : last - 1 - k;
                    Vector3D d = Points[i + 1] - Points[i];
                    double lengthSquared = d.LengthSquared();
                    if (lengthSquared <= segmentSquared) continue;

                    double length = Math.Sqrt(lengthSquared);
                    Vector3D correction = d * ((length - segment) / length);

                    bool pinnedA = i == 0;
                    bool pinnedB = i + 1 == last;
                    if (pinnedA && pinnedB) continue;

                    if (pinnedA)
                    {
                        Points[i + 1] -= correction;
                    }
                    else if (pinnedB)
                    {
                        Points[i] += correction;
                    }
                    else
                    {
                        correction *= 0.5d;
                        Points[i] += correction;
                        Points[i + 1] -= correction;
                    }
                }
            }

            return true;
        }
    }
}
