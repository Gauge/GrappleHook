using System;
using GrappleHook.Core;
using VRageMath;

namespace GrappleHook.Tests
{
    public class RopeMathTests
    {
        [Fact]
        public void SagProfile_IsZeroAtBothEndsAndOneInTheMiddle()
        {
            Assert.Equal(0d, RopeMath.SagProfile(0d), 12);
            Assert.Equal(1d, RopeMath.SagProfile(0.5d), 12);
            Assert.Equal(0d, RopeMath.SagProfile(1d), 12);
        }

        [Fact]
        public void SagProfile_IsSymmetric()
        {
            for (double u = 0; u <= 0.5; u += 0.05)
            {
                Assert.Equal(RopeMath.SagProfile(u), RopeMath.SagProfile(1d - u), 12);
            }
        }

        [Fact]
        public void SagAmount_IsHalfTheSlack()
        {
            Assert.Equal(5d, RopeMath.SagAmount(10d, 20d), 12);
        }

        [Fact]
        public void SagAmount_IsZeroWhenTheRopeIsTaut()
        {
            Assert.Equal(0d, RopeMath.SagAmount(20d, 20d), 12);
            Assert.Equal(0d, RopeMath.SagAmount(30d, 20d), 12);
        }

        [Fact]
        public void ComputeCurvePoints_TautRopeIsAStraightLine()
        {
            RopePath path = new RopePath();
            Vector3D start = new Vector3D(0, 0, 0);
            Vector3D end = new Vector3D(10, 0, 0);

            RopeMath.ComputeCurvePoints(path, start, end, Vector3D.Down, 10d, 5);

            Assert.Equal(5, path.Count);
            Assert.Equal(4, path.SegmentCount);
            for (int i = 0; i < path.Count; i++)
            {
                Assert.Equal(i * 2.5d, path.Points[i].X, 9);
                Assert.Equal(0d, path.Points[i].Y, 9);
            }
        }

        [Fact]
        public void ComputeCurvePoints_EndsAreAlwaysExact()
        {
            RopePath path = new RopePath();
            Vector3D start = new Vector3D(1, 2, 3);
            Vector3D end = new Vector3D(-4, 5, 6);

            RopeMath.ComputeCurvePoints(path, start, end, Vector3D.Down, 500d, 15);

            Assert.Equal(start, path.Points[0]);
            Assert.Equal(end, path.Points[path.Count - 1]);
        }

        [Fact]
        public void ComputeCurvePoints_SlackRopeHangsAlongTheSagDirection()
        {
            RopePath path = new RopePath();

            // 10m apart with 20m of rope: 5m of sag at the middle
            RopeMath.ComputeCurvePoints(path, new Vector3D(0, 0, 0), new Vector3D(10, 0, 0), Vector3D.Down, 20d, 3);

            Assert.Equal(-5d, path.Points[1].Y, 9);
            Assert.Equal(5d, path.Points[1].X, 9);
        }

        [Fact]
        public void ComputeCurvePoints_ClampsToTwoPoints()
        {
            RopePath path = new RopePath();

            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(1, 0, 0), Vector3D.Down, 1d, 0);

            Assert.Equal(2, path.Count);
            Assert.Equal(1, path.SegmentCount);
        }

        [Fact]
        public void ComputeCurvePoints_ReusesTheBufferAndNeverShrinksIt()
        {
            RopePath path = new RopePath();

            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(10, 0, 0), Vector3D.Down, 10d, 15);
            Vector3D[] buffer = path.Points;

            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(10, 0, 0), Vector3D.Down, 10d, 15);
            Assert.Same(buffer, path.Points);

            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(10, 0, 0), Vector3D.Down, 10d, 4);
            Assert.Same(buffer, path.Points);
            Assert.Equal(4, path.Count);
        }

        [Fact]
        public void Clear_LeavesNoSegments()
        {
            RopePath path = new RopePath();
            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(10, 0, 0), Vector3D.Down, 10d, 15);

            path.Clear();

            Assert.Equal(0, path.Count);
            Assert.Equal(0, path.SegmentCount);
        }

        [Fact]
        public void RayHitsPath_HitsARopeItPointsAt()
        {
            RopePath path = new RopePath();
            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(10, 0, 0), Vector3D.Down, 10d, 5);

            RayD ray = new RayD(new Vector3D(5, 5, 0), Vector3D.Down);

            Assert.True(RopeMath.RayHitsPath(path, ref ray, 10d));
        }

        [Fact]
        public void RayHitsPath_MissesARopeOutOfRange()
        {
            RopePath path = new RopePath();
            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(10, 0, 0), Vector3D.Down, 10d, 5);

            RayD ray = new RayD(new Vector3D(5, 50, 0), Vector3D.Down);

            Assert.False(RopeMath.RayHitsPath(path, ref ray, 3.5d));
        }

        [Fact]
        public void RayHitsPath_MissesARopePointedAway()
        {
            RopePath path = new RopePath();
            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(10, 0, 0), Vector3D.Down, 10d, 5);

            RayD ray = new RayD(new Vector3D(5, 5, 0), Vector3D.Up);

            Assert.False(RopeMath.RayHitsPath(path, ref ray, 10d));
        }

        [Fact]
        public void RayHitsPath_EmptyPathIsNeverHit()
        {
            RopePath path = new RopePath();
            RayD ray = new RayD(Vector3D.Zero, Vector3D.Forward);

            Assert.False(RopeMath.RayHitsPath(path, ref ray, 1000d));
        }

        [Fact]
        public void ExceededRange_TripsOnlyBeyondTheLimit()
        {
            Vector3D origin = new Vector3D(100, 100, 100);

            Assert.False(RopeMath.ExceededRange(origin, origin + new Vector3D(0, 0, 299), 300d));
            Assert.False(RopeMath.ExceededRange(origin, origin + new Vector3D(0, 0, 300), 300d));
            Assert.True(RopeMath.ExceededRange(origin, origin + new Vector3D(0, 0, 301), 300d));
        }
    }
}
