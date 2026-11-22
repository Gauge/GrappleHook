using GrappleHook.Core;
using VRageMath;

namespace GrappleHook.Tests
{
    public class MatrixToolsTests
    {
        const double Epsilon = 0.001d;

        [Fact]
        public void AMatrixHasNotMovedFromItself()
        {
            MatrixD a = MatrixD.CreateWorld(new Vector3D(10, 20, 30), Vector3D.Forward, Vector3D.Up);
            MatrixD b = a;

            Assert.False(MatrixTools.HasMoved(ref a, ref b, Epsilon));
        }

        [Fact]
        public void TranslationBeyondTheEpsilonCounts()
        {
            MatrixD a = MatrixD.CreateWorld(new Vector3D(10, 20, 30), Vector3D.Forward, Vector3D.Up);
            MatrixD b = MatrixD.CreateWorld(new Vector3D(10, 20, 30.01), Vector3D.Forward, Vector3D.Up);

            Assert.True(MatrixTools.HasMoved(ref a, ref b, Epsilon));
        }

        [Fact]
        public void TranslationBelowTheEpsilonDoesNot()
        {
            MatrixD a = MatrixD.CreateWorld(new Vector3D(10, 20, 30), Vector3D.Forward, Vector3D.Up);
            MatrixD b = MatrixD.CreateWorld(new Vector3D(10, 20, 30.0001), Vector3D.Forward, Vector3D.Up);

            Assert.False(MatrixTools.HasMoved(ref a, ref b, Epsilon));
        }

        [Fact]
        public void RotationInPlaceCounts()
        {
            // a turret traversing without moving must still drag the hook around with it
            Vector3D position = new Vector3D(10, 20, 30);
            MatrixD a = MatrixD.CreateWorld(position, Vector3D.Forward, Vector3D.Up);
            MatrixD b = MatrixD.CreateWorld(position, Vector3D.Right, Vector3D.Up);

            Assert.True(MatrixTools.HasMoved(ref a, ref b, Epsilon));
        }

        [Fact]
        public void RollAroundTheFiringAxisCounts()
        {
            Vector3D position = new Vector3D(10, 20, 30);
            MatrixD a = MatrixD.CreateWorld(position, Vector3D.Forward, Vector3D.Up);
            MatrixD b = MatrixD.CreateWorld(position, Vector3D.Forward, Vector3D.Right);

            Assert.True(MatrixTools.HasMoved(ref a, ref b, Epsilon));
        }

        [Fact]
        public void AnUninitialisedLastMatrixAlwaysCountsAsMoved()
        {
            MatrixD a = MatrixD.CreateWorld(new Vector3D(10, 20, 30), Vector3D.Forward, Vector3D.Up);
            MatrixD zero = MatrixD.Zero;

            Assert.True(MatrixTools.HasMoved(ref a, ref zero, Epsilon));
        }
    }
}
