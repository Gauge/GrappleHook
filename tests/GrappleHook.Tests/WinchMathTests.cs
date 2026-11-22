using System;
using GrappleHook.Core;

namespace GrappleHook.Tests
{
    public class WinchMathTests
    {
        const float Min = 10f;
        const float Max = 1000f;

        [Fact]
        public void StepLength_TighteningShortensTheRope()
        {
            double length = WinchMath.StepLength(100d, 3f, Min, Max);

            Assert.Equal(100d - 3d / 60d, length, 9);
        }

        [Fact]
        public void StepLength_LooseningLengthensTheRope()
        {
            double length = WinchMath.StepLength(100d, -10f, Min, Max);

            Assert.Equal(100d + 10d / 60d, length, 9);
        }

        [Fact]
        public void StepLength_IdleWinchLeavesTheRopeAlone()
        {
            Assert.Equal(100d, WinchMath.StepLength(100d, 0f, Min, Max), 12);
        }

        [Fact]
        public void StepLength_ClampsToTheMinimum()
        {
            Assert.Equal(Min, WinchMath.StepLength(Min, 3f, Min, Max), 9);
            Assert.Equal(Min, WinchMath.StepLength(Min + 0.001d, 3f, Min, Max), 9);
        }

        [Fact]
        public void StepLength_ClampsToTheMaximum()
        {
            Assert.Equal(Max, WinchMath.StepLength(Max, -10f, Min, Max), 9);
        }

        [Fact]
        public void StepLength_NeverMovesARopeOutsideTheLimitsTheWrongWay()
        {
            // caught 5 m away, under the 10 m minimum: tightening holds, it does not pay out
            Assert.Equal(5d, WinchMath.StepLength(5d, 3f, Min, Max), 9);
            // loosening it pays out as usual
            Assert.Equal(5d + 10d / 60d, WinchMath.StepLength(5d, -10f, Min, Max), 9);
            // past the maximum: loosening holds, tightening reels in
            Assert.Equal(1200d, WinchMath.StepLength(1200d, -10f, Min, Max), 9);
            Assert.Equal(1200d - 3d / 60d, WinchMath.StepLength(1200d, 3f, Min, Max), 9);
        }

        [Fact]
        public void StepLength_AnHourOfTighteningNeverGoesBelowTheMinimum()
        {
            double length = 500d;
            for (int i = 0; i < 60 * 60 * 60; i++)
            {
                length = WinchMath.StepLength(length, 3f, Min, Max);
            }

            Assert.Equal(Min, length, 9);
        }

        [Fact]
        public void StepWinch_StepsWithinTheConfiguredSpeeds()
        {
            Assert.Equal(1f, WinchMath.StepWinch(0f, 1f, 10f, 3f));
            Assert.Equal(-1f, WinchMath.StepWinch(0f, -1f, 10f, 3f));
        }

        [Fact]
        public void StepWinch_ClampsToTheTightenAndLoosenSpeeds()
        {
            Assert.Equal(3f, WinchMath.StepWinch(3f, 1f, 10f, 3f));
            Assert.Equal(-10f, WinchMath.StepWinch(-10f, -1f, 10f, 3f));
        }

        [Fact]
        public void WinchStalled_OnlyWhileReelingInAgainstTooMuchTension()
        {
            Assert.True(WinchMath.WinchStalled(3f, 2e6, 1e6));
            Assert.True(WinchMath.WinchStalled(3f, 1e6, 1e6));
            Assert.False(WinchMath.WinchStalled(3f, 0.5e6, 1e6));
            // paying out and holding never stall
            Assert.False(WinchMath.WinchStalled(-10f, 2e6, 1e6));
            Assert.False(WinchMath.WinchStalled(0f, 2e6, 1e6));
        }

        [Fact]
        public void EffectiveMass_IsTheReducedMass()
        {
            Assert.Equal(500d, WinchMath.EffectiveMass(1d / 1000d, 1d / 1000d), 9);
            // anchored on one end: all of the other
            Assert.Equal(1000d, WinchMath.EffectiveMass(1d / 1000d, 0d), 9);
            // anchored on both: nothing to pull
            Assert.Equal(0d, WinchMath.EffectiveMass(0d, 0d));
        }

        [Fact]
        public void Tension_IsZeroWhenSlack()
        {
            Assert.Equal(0d, WinchMath.Tension(-1d, 0d, 1000d, 1.5d, 0.7d));
            Assert.Equal(0d, WinchMath.Tension(0d, 5d, 1000d, 1.5d, 0.7d));
        }

        [Fact]
        public void Tension_NeverPushes()
        {
            // stretched, but the ends rushing together faster than the rope can pull
            Assert.Equal(0d, WinchMath.Tension(0.01d, -50d, 1000d, 1.5d, 0.7d));
        }

        [Fact]
        public void Tension_ScalesWithMassAndStretch()
        {
            double light = WinchMath.Tension(0.1d, 0d, 100d, 1.5d, 0.7d);
            double heavy = WinchMath.Tension(0.1d, 0d, 100000d, 1.5d, 0.7d);
            double more = WinchMath.Tension(0.2d, 0d, 100d, 1.5d, 0.7d);

            Assert.Equal(1000d * light, heavy, 3);
            Assert.Equal(2d * light, more, 6);
        }

        [Fact]
        public void Tension_HoldsAWeightWithASmallStretch()
        {
            // 1000 kg hanging at 1 g settles where the rope pulls 9810 N: a tenth of a metre at 1.5 Hz
            double omega = 2d * Math.PI * 1.5d;
            double stretch = 9.81d / (omega * omega);

            Assert.Equal(9810d, WinchMath.Tension(stretch, 0d, 1000d, 1.5d, 0.7d), 3);
            Assert.InRange(stretch, 0.1d, 0.12d);
        }

        [Fact]
        public void Tension_IsDampedByHowFastTheRopeStretches()
        {
            double still = WinchMath.Tension(0.1d, 0d, 1000d, 1.5d, 0.7d);
            double opening = WinchMath.Tension(0.1d, 1d, 1000d, 1.5d, 0.7d);

            Assert.Equal(still + 1000d * 2d * 0.7d * 2d * Math.PI * 1.5d, opening, 6);
        }

        /// <summary>
        /// A body on the rope, stepped the way the game does: force applied for a tick, then the
        /// velocity, then the position. The old fixed spring blew up on light grids.
        /// </summary>
        [Theory]
        [InlineData(20d)]
        [InlineData(1000d)]
        [InlineData(5000000d)]
        public void Tension_CatchesAFallingBodyOfAnyMassWithoutBlowingUp(double mass)
        {
            double ropeLength = 10d;
            double y = -10d;           // hanging from 0, rope just taut
            double v = -20d;           // falling fast when it goes taut
            double dt = WinchMath.TickSeconds;
            double lowest = y;

            for (int i = 0; i < 60 * 10; i++)
            {
                double stretch = -y - ropeLength;
                double tension = WinchMath.Tension(stretch, -v, mass, 1.5d, 0.7d);
                v += (tension / mass - 9.81d) * dt;
                y += v * dt;
                lowest = Math.Min(lowest, y);
            }

            // stopped, and hanging still a little below the rope's length
            Assert.InRange(v, -0.05d, 0.05d);
            Assert.InRange(-y - ropeLength, 0d, 0.2d);
            Assert.True(lowest > -14d);
        }
    }
}
