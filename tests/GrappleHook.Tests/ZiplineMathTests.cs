using System;
using GrappleHook.Core;
using VRageMath;

namespace GrappleHook.Tests
{
    public class ZiplineMathTests
    {
        static RopePath StraightRope(double length = 10d, int points = 11)
        {
            RopePath path = new RopePath();
            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(length, 0, 0), Vector3D.Down, length, points);
            return path;
        }

        [Fact]
        public void GetSinAngle_IsZeroForParallelVectors()
        {
            Assert.Equal(0d, ZiplineMath.GetSinAngle(new Vector3D(1, 0, 0), new Vector3D(5, 0, 0)), 12);
        }

        [Fact]
        public void GetSinAngle_IsOneForPerpendicularVectors()
        {
            Assert.Equal(1d, ZiplineMath.GetSinAngle(new Vector3D(1, 0, 0), new Vector3D(0, 3, 0)), 12);
        }

        [Fact]
        public void GetSinAngle_IsHalfAtThirtyDegrees()
        {
            Vector3D a = new Vector3D(1, 0, 0);
            Vector3D b = new Vector3D(Math.Cos(Math.PI / 6d), Math.Sin(Math.PI / 6d), 0);

            Assert.Equal(0.5d, ZiplineMath.GetSinAngle(a, b), 9);
        }

        [Fact]
        public void GetSinAngle_ZeroLengthVectorsDoNotProduceNaN()
        {
            Assert.Equal(0d, ZiplineMath.GetSinAngle(Vector3D.Zero, new Vector3D(1, 0, 0)), 12);
            Assert.Equal(0d, ZiplineMath.GetSinAngle(new Vector3D(1, 0, 0), Vector3D.Zero), 12);
        }

        [Fact]
        public void NearestPointIndex_FindsThePointUnderTheRider()
        {
            RopePath path = StraightRope();

            Assert.Equal(3, ZiplineMath.NearestPointIndex(path, new Vector3D(3, -2, 0), true));
            Assert.Equal(7, ZiplineMath.NearestPointIndex(path, new Vector3D(7.1, -2, 0), true));
        }

        [Fact]
        public void NearestPointIndex_TieBreaksTowardsTheDirectionOfTravel()
        {
            RopePath path = StraightRope();
            Vector3D midway = new Vector3D(3.5, -2, 0);

            Assert.Equal(3, ZiplineMath.NearestPointIndex(path, midway, true));
            Assert.Equal(4, ZiplineMath.NearestPointIndex(path, midway, false));
        }

        [Fact]
        public void ComputePulley_LandsOnTheRopeDirectlyAboveTheRider()
        {
            RopePath path = StraightRope();

            Vector3D pulley = ZiplineMath.ComputePulley(path, new Vector3D(4.4, -2, 0), true);

            Assert.Equal(4.4d, pulley.X, 6);
            Assert.Equal(0d, pulley.Y, 6);
            Assert.Equal(0d, pulley.Z, 6);
        }

        [Fact]
        public void ComputePulley_LandsOnTheRopeWhenRidingTheOtherWay()
        {
            RopePath path = StraightRope();

            Vector3D pulley = ZiplineMath.ComputePulley(path, new Vector3D(4.4, -2, 0), false);

            Assert.Equal(4.4d, pulley.X, 6);
            Assert.Equal(0d, pulley.Y, 6);
        }

        [Fact]
        public void ComputePulley_StaysOnTheRopeForARiderPastTheEnd()
        {
            RopePath path = StraightRope();

            Vector3D pulley = ZiplineMath.ComputePulley(path, new Vector3D(12, -2, 0), true);

            Assert.True(pulley.X <= 10.001d, $"pulley ran off the end of the rope at {pulley.X}");
        }

        [Fact]
        public void ComputePulley_EmptyRopeLeavesTheRiderWhereTheyAre()
        {
            Vector3D position = new Vector3D(1, 2, 3);

            Assert.Equal(position, ZiplineMath.ComputePulley(new RopePath(), position, true));
        }

        [Fact]
        public void ComputePulley_RiderOnTheRopeDoesNotProduceNaN()
        {
            RopePath path = StraightRope();

            Vector3D pulley = ZiplineMath.ComputePulley(path, path.Points[4], true);

            Assert.False(double.IsNaN(pulley.X) || double.IsNaN(pulley.Y) || double.IsNaN(pulley.Z));
            Assert.Equal(path.Points[4], pulley);
        }

        [Fact]
        public void ComputePulley_SaggingRopeStillLandsOnTheRope()
        {
            RopePath path = new RopePath();
            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(20, 0, 0), Vector3D.Down, 30d, 15);

            Vector3D pulley = ZiplineMath.ComputePulley(path, new Vector3D(10, -10, 0), true);

            // On a bent rope the pulley is placed along the segment next to the nearest
            // point rather than at the exact foot of the perpendicular, so it can sit up
            // to a segment away from straight above the rider. It must still be on the rope.
            Assert.True(DistanceToPath(path, pulley) < 1e-6d, $"pulley is off the rope at {pulley}");
            Assert.InRange(pulley.X, 8.5d, 11.5d);
            // the middle of a 20m span with 30m of rope hangs 5m down
            Assert.InRange(pulley.Y, -5.05d, -4.9d);
        }

        [Fact]
        public void AdvancePulley_SlidesAlongTheRope()
        {
            RopePath path = StraightRope();
            Vector3D pulley = new Vector3D(2, 0, 0);

            double remaining = ZiplineMath.AdvancePulley(path, ref pulley, true, 3d);

            Assert.Equal(0d, remaining, 9);
            Assert.Equal(5d, pulley.X, 6);
        }

        [Fact]
        public void AdvancePulley_SlidesBackwardsWhenRidingTheOtherWay()
        {
            RopePath path = StraightRope();
            Vector3D pulley = new Vector3D(8, 0, 0);

            double remaining = ZiplineMath.AdvancePulley(path, ref pulley, false, 3d);

            Assert.Equal(0d, remaining, 9);
            Assert.Equal(5d, pulley.X, 6);
        }

        [Fact]
        public void AdvancePulley_ReportsWhatIsLeftWhenItRunsOffTheEnd()
        {
            RopePath path = StraightRope();
            Vector3D pulley = new Vector3D(9, 0, 0);

            double remaining = ZiplineMath.AdvancePulley(path, ref pulley, true, 4d);

            Assert.True(remaining > 0, "running off the end must leave distance over, which is what dismounts the rider");
            Assert.Equal(10d, pulley.X, 6);
        }

        [Fact]
        public void AdvancePulley_CrossesSegmentsOfASaggingRope()
        {
            RopePath path = new RopePath();
            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(20, 0, 0), Vector3D.Down, 30d, 15);
            Vector3D pulley = path.Points[0];

            double lastX = pulley.X;
            for (int i = 0; i < 10; i++)
            {
                Vector3D before = pulley;
                double remaining = ZiplineMath.AdvancePulley(path, ref pulley, true, 1d);

                Assert.Equal(0d, remaining, 9);
                // a metre of rope crossing a bend covers slightly less ground
                Assert.InRange((pulley - before).Length(), 0.99d, 1.000001d);
                Assert.True(pulley.X > lastX, "the pulley must keep moving down the rope");
                Assert.True(DistanceToPath(path, pulley) < 1e-6d, "the pulley left the rope");

                lastX = pulley.X;
            }
        }

        static double DistanceToPath(RopePath path, Vector3D point)
        {
            double nearest = double.MaxValue;
            for (int i = 0; i < path.SegmentCount; i++)
            {
                Vector3D a = path.Points[i];
                Vector3D b = path.Points[i + 1];
                Vector3D ab = b - a;

                double t = Math.Min(Math.Max(Vector3D.Dot(point - a, ab) / ab.LengthSquared(), 0d), 1d);
                double distance = (point - (a + ab * t)).Length();

                if (distance < nearest) nearest = distance;
            }

            return nearest;
        }

        [Fact]
        public void AdvancePulley_ZeroDistanceIsANoOp()
        {
            RopePath path = StraightRope();
            Vector3D pulley = new Vector3D(5, 0, 0);

            ZiplineMath.AdvancePulley(path, ref pulley, true, 0d);

            Assert.Equal(5d, pulley.X, 9);
        }

        [Fact]
        public void AdvancePulley_DegenerateRopeDoesNotProduceNaN()
        {
            RopePath path = new RopePath();
            path.EnsureCapacity(3);
            path.Count = 3;
            path.Points[0] = Vector3D.Zero;
            path.Points[1] = Vector3D.Zero;
            path.Points[2] = Vector3D.Zero;

            Vector3D pulley = Vector3D.Zero;
            double remaining = ZiplineMath.AdvancePulley(path, ref pulley, true, 5d);

            Assert.Equal(5d, remaining, 9);
            Assert.False(double.IsNaN(pulley.X));
        }

        [Fact]
        public void StepPulleySpeed_NeverFallsBelowTheMinimum()
        {
            Assert.Equal(5d, ZiplineMath.StepPulleySpeed(0d, 0d, 0d, 5d), 9);
            Assert.Equal(5d, ZiplineMath.StepPulleySpeed(1d, -100d, 1d, 5d), 9);
        }

        [Fact]
        public void StepPulleySpeed_AcceleratesDownhill()
        {
            double flat = ZiplineMath.StepPulleySpeed(5d, 0d, 1d, 1d);
            double downhill = ZiplineMath.StepPulleySpeed(5d, 9.8d, 1d, 1d);

            Assert.True(downhill > flat);
        }

        [Fact]
        public void PulleyAcceleration_IsGravityWhenTheRopeRunsStraightDown()
        {
            // tether points from the rider up to the pulley, travel runs down the rope
            Vector3D tetherNorm = Vector3D.Up;
            Vector3D travel = new Vector3D(0, -1, 0);

            Assert.Equal(9.8d, ZiplineMath.PulleyAcceleration(tetherNorm, travel, 9.8d), 9);
        }

        [Fact]
        public void PulleyAcceleration_IsZeroOnALevelRope()
        {
            Assert.Equal(0d, ZiplineMath.PulleyAcceleration(Vector3D.Up, new Vector3D(1, 0, 0), 9.8d), 9);
        }

        [Fact]
        public void PulleyAcceleration_IsNegativeUphill()
        {
            Assert.True(ZiplineMath.PulleyAcceleration(Vector3D.Up, new Vector3D(0, 1, 0), 9.8d) < 0);
        }

        [Fact]
        public void PulleyAcceleration_StationaryPulleyDoesNotProduceNaN()
        {
            Assert.Equal(0d, ZiplineMath.PulleyAcceleration(Vector3D.Up, Vector3D.Zero, 9.8d), 12);
        }
    }
}
