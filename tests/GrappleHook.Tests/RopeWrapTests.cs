using System.Collections.Generic;
using GrappleHook.Core;
using VRageMath;

namespace GrappleHook.Tests
{
    public class RopeWrapTests
    {
        // a rope from the left over a corner at the origin and down to the right
        static readonly Vector3D Launcher = new Vector3D(-10, 0, 0);
        static readonly Vector3D Corner = Vector3D.Zero;
        static readonly Vector3D Hook = new Vector3D(5, -5, 0);

        [Fact]
        public void StaysWrappedWhileBent()
        {
            Vector3D bend = RopeWrap.BendNormal(Launcher, Corner, Hook);

            Assert.False(RopeWrap.ShouldUnwrap(Launcher, Corner, Hook, bend));
            // bent further round
            Assert.False(RopeWrap.ShouldUnwrap(Launcher, Corner, new Vector3D(1, -8, 0), bend));
        }

        [Fact]
        public void UnwrapsOnceSwungBackPastStraight()
        {
            Vector3D bend = RopeWrap.BendNormal(Launcher, Corner, Hook);

            Assert.True(RopeWrap.ShouldUnwrap(Launcher, Corner, new Vector3D(5, 1, 0), bend));
        }

        [Fact]
        public void PressDirection_PushesIntoTheCorner()
        {
            Vector3D press = RopeWrap.PressDirection(Launcher, Corner, Hook);

            // towards the launcher and the hook: left and down, into the edge
            Assert.True(press.X < 0);
            Assert.True(press.Y < 0);
            Assert.Equal(0d, RopeWrap.PressDirection(Launcher, Corner, new Vector3D(10, 0, 0)).Length(), 9);
        }

        [Fact]
        public void Length_IsTheSumOfTheSpans()
        {
            List<Vector3D> points = new List<Vector3D> { Launcher, Corner, new Vector3D(0, -5, 0) };

            Assert.Equal(15d, RopeWrap.Length(points), 9);
        }

        [Fact]
        public void Direction_OfCoincidentPointsIsZero()
        {
            Assert.Equal(Vector3D.Zero, RopeWrap.Direction(Corner, Corner));
        }
    }
}
