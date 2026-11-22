using System;
using VRageMath;

namespace GrappleHook.Core
{
    public static class MatrixTools
    {
        /// <summary>
        /// True when two matrices differ by more than <paramref name="epsilon"/> in position
        /// or in facing. Used to skip moving the hook entity on frames where the muzzle has
        /// not actually moved: writing WorldMatrix propagates through the position component
        /// and dirties the render proxy, so it is worth not doing 60 times a second for a
        /// block that is sitting still.
        /// </summary>
        public static bool HasMoved(ref MatrixD a, ref MatrixD b, double epsilon)
        {
            if ((a.Translation - b.Translation).LengthSquared() > epsilon * epsilon)
            {
                return true;
            }

            return (a.Forward - b.Forward).LengthSquared() > epsilon * epsilon
                || (a.Up - b.Up).LengthSquared() > epsilon * epsilon;
        }
    }
}
