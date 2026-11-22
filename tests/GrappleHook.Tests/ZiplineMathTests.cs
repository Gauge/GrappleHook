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
            path.UpdateDistances();
            return path;
        }

        static RopePath SaggingRope()
        {
            RopePath path = new RopePath();
            RopeMath.ComputeCurvePoints(path, Vector3D.Zero, new Vector3D(20, 0, 0), Vector3D.Down, 26d, 21);
            path.UpdateDistances();
            return path;
        }

        [Fact]
        public void ClosestDistance_IsTheDistanceAlongTheRopeAboveTheRider()
        {
            RopePath rope = StraightRope();

            Assert.Equal(3.5d, ZiplineMath.ClosestDistance(rope, new Vector3D(3.5, -2, 0)), 9);
        }

        [Fact]
        public void ClosestDistance_ClampsToTheEnds()
        {
            RopePath rope = StraightRope();

            Assert.Equal(0d, ZiplineMath.ClosestDistance(rope, new Vector3D(-5, -1, 0)), 9);
            Assert.Equal(10d, ZiplineMath.ClosestDistance(rope, new Vector3D(15, -1, 0)), 9);
        }

        [Fact]
        public void PointAt_IsOnTheRopeAndFacesItsEnd()
        {
            RopePath rope = StraightRope();
            Vector3D point, tangent;

            ZiplineMath.PointAt(rope, 4.25d, out point, out tangent);

            Assert.Equal(4.25d, point.X, 9);
            Assert.Equal(1d, tangent.X, 9);
        }

        [Fact]
        public void PointAt_ClampsToTheEnds()
        {
            RopePath rope = StraightRope();
            Vector3D point, tangent;

            ZiplineMath.PointAt(rope, -3d, out point, out tangent);
            Assert.Equal(0d, point.X, 9);

            ZiplineMath.PointAt(rope, 30d, out point, out tangent);
            Assert.Equal(10d, point.X, 9);
        }

        [Fact]
        public void PointAt_FollowsASaggingRope()
        {
            RopePath rope = SaggingRope();
            Vector3D point, tangent;

            ZiplineMath.PointAt(rope, rope.Length / 2d, out point, out tangent);

            Assert.Equal(10d, point.X, 6);
            Assert.True(point.Y < -4d);
            Assert.InRange(tangent.Y, -0.1d, 0.1d);
        }

        [Fact]
        public void PointAtAndClosestDistance_RoundTrip()
        {
            RopePath rope = SaggingRope();
            Vector3D point, tangent;

            ZiplineMath.PointAt(rope, 7.3d, out point, out tangent);

            Assert.Equal(7.3d, ZiplineMath.ClosestDistance(rope, point), 6);
        }

        [Fact]
        public void PointAt_EmptyRopeDoesNotProduceNaN()
        {
            Vector3D point, tangent;
            ZiplineMath.PointAt(new RopePath(), 3d, out point, out tangent);

            Assert.Equal(Vector3D.Zero, point);
            Assert.Equal(Vector3D.Zero, tangent);
        }

        [Fact]
        public void AdvancePulley_SlidesBothWays()
        {
            double distance = 5d;

            Assert.Equal(0d, ZiplineMath.AdvancePulley(10d, ref distance, true, 2d));
            Assert.Equal(7d, distance, 9);

            Assert.Equal(0d, ZiplineMath.AdvancePulley(10d, ref distance, false, 3d));
            Assert.Equal(4d, distance, 9);
        }

        [Fact]
        public void AdvancePulley_ReportsRunningOffEitherEnd()
        {
            double distance = 9d;
            Assert.Equal(1.5d, ZiplineMath.AdvancePulley(10d, ref distance, true, 2.5d), 9);
            Assert.Equal(10d, distance, 9);

            distance = 1d;
            Assert.Equal(2d, ZiplineMath.AdvancePulley(10d, ref distance, false, 3d), 9);
            Assert.Equal(0d, distance, 9);
        }

        [Fact]
        public void StepPulleySpeed_NeverFallsBelowTheMinimum()
        {
            Assert.Equal(5d, ZiplineMath.StepPulleySpeed(0d, 0d, 5d), 9);
            Assert.Equal(5d, ZiplineMath.StepPulleySpeed(6d, -100d, 5d), 9);
        }

        [Fact]
        public void StepPulleySpeed_GainsGravityTimesTimeDownhill()
        {
            // a second straight down from rest: 9.8 m/s, not the runaway the old step gave
            double speed = 0d;
            for (int i = 0; i < 60; i++) speed = ZiplineMath.StepPulleySpeed(speed, 9.8d, 0d);

            Assert.Equal(9.8d, speed, 6);
        }

        [Fact]
        public void PulleyAcceleration_IsGravityAlongTheRope()
        {
            Vector3D gravity = new Vector3D(0, -9.8, 0);

            Assert.Equal(9.8d, ZiplineMath.PulleyAcceleration(Vector3D.Down, gravity), 9);
            Assert.Equal(0d, ZiplineMath.PulleyAcceleration(Vector3D.Right, gravity), 9);
            Assert.True(ZiplineMath.PulleyAcceleration(Vector3D.Up, gravity) < 0);
            Assert.Equal(0d, ZiplineMath.PulleyAcceleration(Vector3D.Zero, gravity), 9);
        }
    }
}
