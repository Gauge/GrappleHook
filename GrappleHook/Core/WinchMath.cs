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
        /// reels the rope in, a negative one pays it out. The result is clamped to the
        /// configured rope limits.
        /// </summary>
        public static double StepLength(double length, float winchSpeed, float minLength, float maxLength)
        {
            if (winchSpeed == 0)
            {
                return length;
            }

            return Math.Max(Math.Min(length - winchSpeed * TickSeconds, maxLength), minLength);
        }

        /// <summary>
        /// One press of the tighten/loosen terminal actions.
        /// </summary>
        public static float StepWinch(float winchSpeed, float delta, float loosenSpeed, float tightenSpeed)
        {
            return Math.Max(Math.Min(winchSpeed + delta, tightenSpeed), -loosenSpeed);
        }

        /// <summary>
        /// Tension in the rope: a spring pulling the two ends towards each other once the
        /// span exceeds the length of rope paid out, damped by how fast the two ends are
        /// moving. Zero or negative means the rope is slack and pulls on nothing.
        /// </summary>
        public static double TensionForce(double spanLength, double ropeLength, double ropeForce, double damping, double relativeSpeed)
        {
            return ropeForce * (spanLength - ropeLength) - damping * relativeSpeed;
        }
    }
}
