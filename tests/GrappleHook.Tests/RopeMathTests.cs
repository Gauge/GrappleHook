using System;
using GrappleHook.Core;
using VRageMath;

namespace GrappleHook.Tests
{
    public class RopeMathTests
    {
        static RopePath Curve(Vector3D start, Vector3D end, double length, int points = 41, Vector3D? down = null)
        {
            RopePath path = new RopePath();
            RopeMath.ComputeCurvePoints(path, start, end, down ?? Vector3D.Down, length, points);
            path.UpdateDistances();
            return path;
        }

        static double LowestY(RopePath path)
        {
            double y = double.MaxValue;
            for (int i = 0; i < path.Count; i++) y = Math.Min(y, path.Points[i].Y);
            return y;
        }

        [Theory]
        [InlineData(1.000001)]
        [InlineData(1.01)]
        [InlineData(1.5)]
        [InlineData(3)]
        [InlineData(10)]
        [InlineData(1000)]
        [InlineData(1e7)]
        public void SolveCatenaryShape_Solves(double r)
        {
            double xi = RopeMath.SolveCatenaryShape(r);

            Assert.True(xi > 0);
            Assert.Equal(r, Math.Sinh(xi) / xi, 6);
        }

        [Fact]
        public void SolveCatenaryShape_NoSlackIsZero()
        {
            Assert.Equal(0d, RopeMath.SolveCatenaryShape(1d));
            Assert.Equal(0d, RopeMath.SolveCatenaryShape(0.5d));
        }

        [Fact]
        public void TautRopeIsAStraightLine()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 0, 0), 10d, 5);

            Assert.Equal(5, path.Count);
            Assert.Equal(4, path.SegmentCount);
            for (int i = 0; i < path.Count; i++)
            {
                Assert.Equal(i * 2.5d, path.Points[i].X, 9);
                Assert.Equal(0d, path.Points[i].Y, 9);
            }
        }

        [Fact]
        public void NoGravityIsAStraightLine()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 0, 0), 20d, 5, Vector3D.Zero);

            for (int i = 0; i < path.Count; i++) Assert.Equal(0d, path.Points[i].Y, 9);
        }

        [Theory]
        [InlineData(0, 0, 10.5)]
        [InlineData(0, 0, 30)]
        [InlineData(0, 6, 12)]
        [InlineData(0, -6, 12)]
        [InlineData(3, 4, 12)]
        public void TheRopeIsAsLongAsThePaidOutLength(double z, double y, double length)
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(8, y, z), length, 400);

            Assert.Equal(length, path.Length, 2);
        }

        [Fact]
        public void PointsAreEvenlySpacedAlongTheRope()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 3, 0), 25d, 21);

            // chords of an evenly divided curve: all about the same, all a little under 25/20
            for (int i = 0; i < path.SegmentCount; i++)
            {
                double chord = Vector3D.Distance(path.Points[i], path.Points[i + 1]);
                Assert.InRange(chord, 1.1d, 1.25d);
            }
        }

        [Fact]
        public void EndsAreAlwaysExact()
        {
            Vector3D start = new Vector3D(1, 2, 3);
            Vector3D end = new Vector3D(-4, 5, 6);

            RopePath path = Curve(start, end, 500d, 15);

            Assert.Equal(start, path.Points[0]);
            Assert.Equal(end, path.Points[path.Count - 1]);
        }

        [Fact]
        public void ALittleSlackSagsALot()
        {
            // a parabola's arc length: 100 m spanned by 101 m of rope sags about sqrt(3*100*1/8) = 6.1 m
            RopePath path = Curve(Vector3D.Zero, new Vector3D(100, 0, 0), 101d, 101);

            Assert.Equal(-6.1d, LowestY(path), 1);
        }

        [Fact]
        public void ALevelRopeSagsSymmetrically()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 0, 0), 20d, 21);

            for (int i = 0; i < path.Count; i++)
            {
                Assert.Equal(path.Points[i].Y, path.Points[path.Count - 1 - i].Y, 6);
                Assert.Equal(10d - path.Points[i].X, path.Points[path.Count - 1 - i].X, 6);
            }
            Assert.Equal(5d, path.Points[10].X, 6);
        }

        [Fact]
        public void TheLowPointMovesTowardsTheLowerEnd()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 4, 0), 16d, 81);

            int lowest = 0;
            for (int i = 1; i < path.Count; i++) if (path.Points[i].Y < path.Points[lowest].Y) lowest = i;

            Assert.True(path.Points[lowest].X < 5d);
            Assert.True(path.Points[lowest].Y < 0d);
        }

        [Fact]
        public void ARopeHangingStraightDownFoldsBelowTheLowerEnd()
        {
            // 10 m apart vertically with 16 m of rope: 3 m of slack loops below the lower end
            RopePath path = Curve(Vector3D.Zero, new Vector3D(0, -10, 0), 16d, 161);

            Assert.Equal(-13d, LowestY(path), 1);
            Assert.Equal(16d, path.Length, 1);
        }

        [Fact]
        public void ANearlyTautRopeKeepsItsPrecision()
        {
            RopePath path = Curve(new Vector3D(1000, 1000, 1000), new Vector3D(1100, 1000, 1000), 100.0001d, 21);

            for (int i = 0; i < path.Count; i++)
            {
                Assert.False(double.IsNaN(path.Points[i].Y));
                Assert.InRange(path.Points[i].Y, 999.5d, 1000d);
            }
        }

        [Fact]
        public void ClampsToTwoPoints()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(1, 0, 0), 1d, 0);

            Assert.Equal(2, path.Count);
            Assert.Equal(1, path.SegmentCount);
        }

        [Fact]
        public void AppendContinuesFromTheLastPoint()
        {
            RopePath path = new RopePath();
            RopeMath.AppendCurvePoints(path, Vector3D.Zero, new Vector3D(5, 0, 0), Vector3D.Down, 6d, 6, false);
            RopeMath.AppendCurvePoints(path, new Vector3D(5, 0, 0), new Vector3D(5, 0, 5), Vector3D.Down, 6d, 6, true);

            Assert.Equal(11, path.Count);
            Assert.Equal(new Vector3D(5, 0, 0), path.Points[5]);
            Assert.Equal(new Vector3D(5, 0, 5), path.Points[10]);
        }

        [Fact]
        public void ReusesTheBufferAndNeverShrinksIt()
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
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 0, 0), 10d, 15);

            path.Clear();

            Assert.Equal(0, path.Count);
            Assert.Equal(0, path.SegmentCount);
            Assert.Equal(0d, path.Length);
        }

        [Fact]
        public void ReachAcross_BoundsTheRope()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 0, 0), 20d, 41);
            double reach = RopeMath.ReachAcross(10d, 20d);

            Assert.True(-LowestY(path) <= reach);
            Assert.Equal(0d, RopeMath.ReachAcross(10d, 10d));
        }

        [Fact]
        public void RayHitsPath_HitsARopeItPointsAt()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 0, 0), 10d, 5);
            RayD ray = new RayD(new Vector3D(5, 5, 0), Vector3D.Down);

            Assert.True(RopeMath.RayHitsPath(path, ref ray, 10d));
        }

        [Fact]
        public void RayHitsPath_MissesARopeOutOfRange()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 0, 0), 10d, 5);
            RayD ray = new RayD(new Vector3D(5, 50, 0), Vector3D.Down);

            Assert.False(RopeMath.RayHitsPath(path, ref ray, 3.5d));
        }

        [Fact]
        public void RayHitsPath_MissesARopePointedAway()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 0, 0), 10d, 5);
            RayD ray = new RayD(new Vector3D(5, 5, 0), Vector3D.Up);

            Assert.False(RopeMath.RayHitsPath(path, ref ray, 10d));
        }

        [Fact]
        public void RayHitsPath_HitsJustInsideRangeAndMissesJustOutside()
        {
            RopePath path = Curve(Vector3D.Zero, new Vector3D(10, 0, 0), 10d, 5);
            RayD near = new RayD(new Vector3D(5, 3.49d, 0), Vector3D.Down);
            RayD far = new RayD(new Vector3D(5, 3.51d, 0), Vector3D.Down);

            Assert.True(RopeMath.RayHitsPath(path, ref near, 3.5d));
            Assert.False(RopeMath.RayHitsPath(path, ref far, 3.5d));
        }

        [Fact]
        public void RayHitsPath_HitsFromInsideASegmentsBounds()
        {
            RopePath path = Curve(new Vector3D(0, 0, 0), new Vector3D(10, 4, 4), 10.8d, 5);
            RayD ray = new RayD(new Vector3D(5, 2, 2), Vector3D.Left);

            Assert.True(RopeMath.RayHitsPath(path, ref ray, 0.1d));
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
