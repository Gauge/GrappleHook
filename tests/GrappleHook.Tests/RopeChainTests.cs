using System;
using GrappleHook.Core;
using VRageMath;

namespace GrappleHook.Tests
{
    public class RopeChainTests
    {
        const double Dt = 1d / 60d;
        static readonly Vector3D GravityStep = new Vector3D(0, -9.81, 0) * Dt * Dt;

        static RopeChain Start(Vector3D a, Vector3D b, double length, int points = 21, Vector3D? down = null)
        {
            RopePath shape = new RopePath();
            RopeMath.ComputeCurvePoints(shape, a, b, down ?? Vector3D.Down, length, points);
            RopeChain chain = new RopeChain();
            chain.Reset(shape.Points, 0, shape.Count);
            return chain;
        }

        static RopePath AsPath(RopeChain chain)
        {
            RopePath path = new RopePath();
            for (int i = 0; i < chain.Count; i++) path.Add(chain.Points[i]);
            path.UpdateDistances();
            return path;
        }

        [Fact]
        public void SettlesNearTheCatenary()
        {
            Vector3D a = Vector3D.Zero, b = new Vector3D(20, 0, 0);
            RopeChain chain = Start(a, b, 20d, 21, Vector3D.Zero);   // starts straight

            for (int i = 0; i < 600; i++) chain.Step(a, b, 26d, GravityStep, 0.985d, 0d, 12);

            RopePath exact = new RopePath();
            RopeMath.ComputeCurvePoints(exact, a, b, Vector3D.Down, 26d, 21);

            // hanging, at about the catenary's depth (a little lower: the chain gives a little)
            Assert.InRange(chain.Points[10].Y, exact.Points[10].Y - 1d, exact.Points[10].Y + 0.2d);
            Assert.InRange(AsPath(chain).Length, 25.9d, 27d);
        }

        [Fact]
        public void EndsArePinned()
        {
            Vector3D a = Vector3D.Zero, b = new Vector3D(20, 0, 0);
            RopeChain chain = Start(a, b, 26d);

            Vector3D a2 = new Vector3D(0, 1, 0), b2 = new Vector3D(19, 2, 0);
            chain.Step(a2, b2, 26d, GravityStep, 0.985d, 0d, 12);

            Assert.Equal(a2, chain.Points[0]);
            Assert.Equal(b2, chain.Points[chain.Count - 1]);
        }

        [Fact]
        public void InSpaceASlackRopeStaysWhereItIs()
        {
            Vector3D a = Vector3D.Zero, b = new Vector3D(20, 0, 0);
            RopeChain chain = Start(a, b, 26d);
            Vector3D middle = chain.Points[10];

            for (int i = 0; i < 300; i++) chain.Step(a, b, 26d, Vector3D.Zero, 0.985d, 0d, 12);

            Assert.Equal(middle.X, chain.Points[10].X, 6);
            Assert.Equal(middle.Y, chain.Points[10].Y, 6);
        }

        [Fact]
        public void InSpaceASlackRopeTravelsWithItsEnds()
        {
            Vector3D a = Vector3D.Zero, b = new Vector3D(20, 0, 0);
            RopeChain chain = Start(a, b, 26d);
            double sag = chain.Points[10].Y;
            Vector3D step = new Vector3D(0, 0, 100d * Dt);   // 100 m/s sideways

            for (int i = 1; i <= 300; i++) chain.Step(a + step * i, b + step * i, 26d, Vector3D.Zero, 0.985d, 0d, 12);

            // still bowed the same way, keeping up rather than trailing behind
            Assert.Equal(sag, chain.Points[10].Y, 3);
            Assert.Equal((a + step * 300).Z, chain.Points[10].Z, 3);
        }

        [Fact]
        public void ATautRopeIsStraight()
        {
            Vector3D a = Vector3D.Zero, b = new Vector3D(20, 0, 0);
            RopeChain chain = Start(a, b, 26d);

            chain.Step(a, b, 19d, GravityStep, 0.985d, 0d, 12);

            for (int i = 0; i < chain.Count; i++) Assert.Equal(0d, chain.Points[i].Y, 9);
        }

        [Fact]
        public void AJumpingEndAsksForAReset()
        {
            Vector3D a = Vector3D.Zero, b = new Vector3D(20, 0, 0);
            RopeChain chain = Start(a, b, 26d);

            Assert.False(chain.Step(a, b + new Vector3D(500, 0, 0), 26d, GravityStep, 0.985d, 0d, 12));
            Assert.False(chain.Active);
        }

        [Fact]
        public void AirDragSettlesASwingFaster()
        {
            Vector3D a = Vector3D.Zero, b = new Vector3D(20, 0, 0);
            RopeChain vacuum = Start(a, b, 26d, 21, Vector3D.Left);   // starts bowed sideways
            RopeChain air = Start(a, b, 26d, 21, Vector3D.Left);

            for (int i = 0; i < 120; i++)
            {
                vacuum.Step(a, b, 26d, GravityStep, 1d, 0d, 12);
                air.Step(a, b, 26d, GravityStep, 1d, 0.02d, 12);
            }

            double vacuumSpeed = (vacuum.Points[10] - Previous(vacuum, a, b)).Length();
            double airSpeed = (air.Points[10] - Previous(air, a, b)).Length();
            Assert.True(airSpeed < vacuumSpeed);
        }

        static Vector3D Previous(RopeChain chain, Vector3D a, Vector3D b)
        {
            Vector3D before = chain.Points[10];
            chain.Step(a, b, 26d, GravityStep, 1d, 0d, 12);
            Vector3D after = chain.Points[10];
            return before - (after - before);
        }
    }
}
