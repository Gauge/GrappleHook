using System;
using VRageMath;

namespace GrappleHook.Core
{
    /// <summary>
    /// Where a rider hangs from the rope and how they slide along it. Pure math, no game
    /// state, so it can be tested outside the game.
    /// </summary>
    public static class ZiplineMath
    {
        /// <summary>
        /// Sine of the angle between two vectors, via the cross product. Returns 0 if
        /// either vector has no length.
        /// </summary>
        public static double GetSinAngle(Vector3D a, Vector3D b)
        {
            double magnitudeA = a.Length();
            double magnitudeB = b.Length();

            if (magnitudeA == 0 || magnitudeB == 0)
            {
                return 0;
            }

            return Vector3D.Cross(a, b).Length() / (magnitudeA * magnitudeB);
        }

        /// <summary>
        /// Index of the rope point nearest the rider. Scanned from the far end when the
        /// rider faces back down the rope, so the tie break favours the point they are
        /// travelling towards.
        /// </summary>
        public static int NearestPointIndex(RopePath path, Vector3D position, bool direction)
        {
            int nearIndex = 0;
            double nearValue = double.MaxValue;

            for (int i = 0; i < path.Count; i++)
            {
                int index = (direction) ? i : path.Count - 1 - i;

                double distance = (position - path.Points[index]).LengthSquared();
                if (distance < nearValue)
                {
                    nearIndex = index;
                    nearValue = distance;
                }
            }

            return nearIndex;
        }

        /// <summary>
        /// Drops a pulley onto the rope beneath the rider: the point on the nearest rope
        /// segment closest to them, which is what the tether then pulls towards.
        /// </summary>
        public static Vector3D ComputePulley(RopePath path, Vector3D characterPosition, bool direction)
        {
            if (path.Count == 0)
            {
                return characterPosition;
            }

            int nearIndex = NearestPointIndex(path, characterPosition, direction);
            Vector3D centerSegment = path.Points[nearIndex];

            Vector3D nextSegment = (nearIndex + 1 < path.Count) ? path.Points[nearIndex + 1] : centerSegment;
            Vector3D previousSegment = (nearIndex - 1 >= 0) ? path.Points[nearIndex - 1] : centerSegment;

            if (!direction)
            {
                Vector3D temp = nextSegment;
                nextSegment = previousSegment;
                previousSegment = temp;
            }

            Vector3D centerToNext = nextSegment - centerSegment;
            Vector3D playerToCenter = centerSegment - characterPosition;
            double playerToCenterMag = playerToCenter.Length();

            if (playerToCenterMag == 0)
            {
                return centerSegment;
            }

            Vector3D anchor;
            Vector3D anchorNorm;
            double sinTheta;

            // in front of the centre point: lean on the segment ahead of the rider
            if (Vector3D.Dot(playerToCenter, centerToNext) < 0)
            {
                anchor = centerToNext;
                sinTheta = GetSinAngle(anchor, -playerToCenter);

                if (anchor.LengthSquared() == 0)
                {
                    return centerSegment;
                }

                anchorNorm = Vector3D.Normalize(anchor);
            }
            else
            {
                anchor = centerSegment - previousSegment;
                sinTheta = GetSinAngle(anchor, playerToCenter);

                if (anchor.LengthSquared() == 0)
                {
                    return centerSegment;
                }

                anchorNorm = -Vector3D.Normalize(anchor);
            }

            double playerPulleyMag = sinTheta * playerToCenterMag;
            double pulleyToAnchorSquared = playerToCenterMag * playerToCenterMag - playerPulleyMag * playerPulleyMag;

            if (pulleyToAnchorSquared <= 0)
            {
                return centerSegment;
            }

            return centerSegment + anchorNorm * Math.Sqrt(pulleyToAnchorSquared);
        }

        /// <summary>
        /// Speed of the pulley for this tick. The rider accelerates under gravity by how
        /// steeply the rope runs ahead of them, and never drops below the configured
        /// minimum so they always reach the far end.
        /// </summary>
        public static double StepPulleySpeed(double lastSpeed, double acceleration, double travelled, double minSpeed)
        {
            double velocitySquared = Math.Max(lastSpeed * lastSpeed + 2d * acceleration * travelled * travelled, minSpeed * minSpeed);
            return Math.Sqrt(velocitySquared);
        }

        /// <summary>
        /// Component of gravity along the direction the pulley last moved. Positive when
        /// the rope runs downhill ahead of the rider.
        /// </summary>
        public static double PulleyAcceleration(Vector3D tetherDirectionNorm, Vector3D travelVector, double gravity)
        {
            if (travelVector == Vector3D.Zero)
            {
                return 0;
            }

            return Vector3D.Dot(-tetherDirectionNorm, Vector3D.Normalize(travelVector)) * gravity;
        }

        /// <summary>
        /// Slides the pulley <paramref name="distance"/> metres along the rope and returns
        /// whatever distance was left over, which happens only when the pulley runs off the
        /// end of the rope.
        /// </summary>
        public static double AdvancePulley(RopePath path, ref Vector3D pulley, bool direction, double distance)
        {
            for (int j = 0; j < path.SegmentCount; j++)
            {
                if (distance == 0) break;

                int index = j;
                int index2 = j + 1;
                if (!direction)
                {
                    index = path.Count - 1 - j;
                    index2 = index - 1;
                }

                Vector3D start = path.Points[index];
                Vector3D end = path.Points[index2];

                Vector3D segmentDirection = end - start;
                if (segmentDirection == Vector3D.Zero) continue;

                Vector3D segmentNorm = Vector3D.Normalize(segmentDirection);

                Vector3D startDirection = start - pulley;
                Vector3D startNorm = Vector3D.Zero;
                if (startDirection != Vector3D.Zero)
                {
                    startNorm = Vector3D.Normalize(startDirection);
                }

                Vector3D endDirection = end - pulley;
                if (endDirection == Vector3D.Zero) continue;

                Vector3D endNorm = Vector3D.Normalize(endDirection);

                // the pulley is on this segment when the start is behind it and the end ahead
                bool startBehind = Vector3D.Dot(startNorm, segmentNorm) <= 0 || Math.Abs((pulley - startDirection).Length()) < 0.01f;
                if (!startBehind || Vector3D.Dot(endNorm, segmentNorm) < 0) continue;

                double length = endDirection.Length();
                if (distance < length)
                {
                    pulley += endNorm * distance;
                    distance = 0;
                }
                else
                {
                    distance -= length;
                    pulley += endNorm * length;
                }
            }

            return distance;
        }
    }
}
