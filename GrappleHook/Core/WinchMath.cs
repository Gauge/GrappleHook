using System;

namespace GrappleHook.Core
{
    /// <summary>
    /// Winch and rope tension math. Pure, so it can be tested outside the game.
    /// </summary>
    public static class WinchMath
    {
        public const double TickSeconds = 1d / 60d;

        /// <summary>
        /// Applies one tick of winch travel to the rope length. A positive winch speed
        /// reels the rope in, a negative one pays it out. Reeling in stops at the minimum
        /// length and paying out at the maximum; a rope already outside them is never moved
        /// the wrong way (a hook that caught 5 m away is not paid out to the minimum by
        /// tightening).
        /// </summary>
        public static double StepLength(double length, float winchSpeed, float minLength, float maxLength)
        {
            if (winchSpeed > 0)
            {
                return Math.Max(length - winchSpeed * TickSeconds, Math.Min(length, minLength));
            }

            if (winchSpeed < 0)
            {
                return Math.Min(length - winchSpeed * TickSeconds, Math.Max(length, maxLength));
            }

            return length;
        }

        /// <summary>
        /// One press of the tighten/loosen terminal actions.
        /// </summary>
        public static float StepWinch(float winchSpeed, float delta, float loosenSpeed, float tightenSpeed)
        {
            return Math.Max(Math.Min(winchSpeed + delta, tightenSpeed), -loosenSpeed);
        }

        /// <summary>
        /// A winch reeling in stops when the rope already pulls as hard as it can; its brake
        /// still holds. Paying out never stalls.
        /// </summary>
        public static bool WinchStalled(float winchSpeed, double tension, double winchForce)
        {
            return winchSpeed > 0 && tension >= winchForce;
        }

        /// <summary>
        /// 1 / (1/mA + 1/mB) from the two inverse masses: what the rope feels pulling the two
        /// ends together. An anchored end has an inverse mass of 0. Zero when both are anchored.
        /// </summary>
        public static double EffectiveMass(double inverseMassA, double inverseMassB)
        {
            double sum = inverseMassA + inverseMassB;
            return sum > 0 ? 1d / sum : 0d;
        }

        /// <summary>
        /// Tension in the rope, in newtons. The rope is a stiff, damped one-sided constraint:
        /// it pulls only when stretched past the length paid out, and its stiffness and damping
        /// are scaled to the mass on its ends, so it springs back at the same
        /// <paramref name="frequency"/> (Hz) and with the same <paramref name="dampingRatio"/>
        /// (1 is critically damped) on a drone as on a capital ship.
        /// <paramref name="stretchRate"/> is how fast the rope is lengthening, in m/s.
        /// A rope cannot push, so the result is never negative.
        /// </summary>
        public static double Tension(double stretch, double stretchRate, double effectiveMass, double frequency, double dampingRatio)
        {
            if (stretch <= 0 || effectiveMass <= 0)
            {
                return 0d;
            }

            double omega = 2d * Math.PI * frequency;
            double tension = effectiveMass * (omega * omega * stretch + 2d * dampingRatio * omega * stretchRate);
            return tension > 0 ? tension : 0d;
        }
    }
}
