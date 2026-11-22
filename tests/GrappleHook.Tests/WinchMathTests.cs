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
        public void TensionForce_IsPositiveOnlyWhenTheRopeIsStretched()
        {
            // 110m of span on 100m of rope
            Assert.True(WinchMath.TensionForce(110d, 100d, 8000000d, 0.5d, 0d) > 0);

            // slack rope pulls on nothing
            Assert.True(WinchMath.TensionForce(90d, 100d, 8000000d, 0.5d, 0d) < 0);
            Assert.Equal(0d, WinchMath.TensionForce(100d, 100d, 8000000d, 0.5d, 0d), 9);
        }

        [Fact]
        public void TensionForce_IsDampedByRelativeSpeed()
        {
            double undamped = WinchMath.TensionForce(110d, 100d, 8000000d, 0.5d, 0d);
            double damped = WinchMath.TensionForce(110d, 100d, 8000000d, 0.5d, 100d);

            Assert.Equal(undamped - 50d, damped, 6);
        }

        [Fact]
        public void TensionForce_ScalesWithStretch()
        {
            double small = WinchMath.TensionForce(101d, 100d, 8000000d, 0d, 0d);
            double large = WinchMath.TensionForce(102d, 100d, 8000000d, 0d, 0d);

            Assert.Equal(2d * small, large, 3);
        }
    }
}
