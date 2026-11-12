using System;

namespace FYP.Detective
{
    /// <summary>
    /// Backup floor check when the depth sensor can't help: someone holds one hand flat on the floor. A hand counts as
    /// "on the floor" when all its joints are within <see cref="MaxFlatSpan"/> in height (a flat hand; a hanging hand
    /// spans ~18 cm), it is at least <see cref="MinBelowHead"/> below the head, and it stays within
    /// <see cref="StillRange"/> for <see cref="HoldSeconds"/>. Feed one hand per call; time passed in.
    /// </summary>
    public sealed class TouchFloorDetector
    {
        public const float MaxFlatSpan = 0.04f;
        public const float MinBelowHead = 0.4f;
        public const float StillRange = 0.015f;
        public const double HoldSeconds = 1.0;
        /// <summary>Height of the lowest joint centre above the surface for a flat hand (finger thickness).</summary>
        public const float JointAboveSurface = 0.008f;

        private double _since = -1;
        private float _min, _max;

        /// <summary>Forget the current hold (hand lost or lifted).</summary>
        public void Reset() => _since = -1;

        /// <summary>
        /// One frame of one hand: lowest and highest joint height. True (with the floor height) once the hand has been
        /// flat, low and still long enough.
        /// </summary>
        public bool Update(double now, float headY, float lowestJointY, float highestJointY, out float floorY)
        {
            floorY = 0f;
            if (highestJointY - lowestJointY > MaxFlatSpan || headY - lowestJointY < MinBelowHead)
            {
                Reset();
                return false;
            }
            if (_since < 0 || Math.Max(_max, lowestJointY) - Math.Min(_min, lowestJointY) > StillRange)
            {
                _since = now;
                _min = _max = lowestJointY;
                return false;
            }
            _min = Math.Min(_min, lowestJointY);
            _max = Math.Max(_max, lowestJointY);
            if (now - _since < HoldSeconds) return false;
            floorY = (_min + _max) * 0.5f - JointAboveSurface;
            return true;
        }
    }
}
