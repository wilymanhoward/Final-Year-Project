using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Release velocity of a held clue for the capped toss (user decision, session 6): positions sampled while held
    /// (ring buffer, time passed in), velocity = displacement over the last <see cref="Window"/> seconds, capped at
    /// <see cref="MaxSpeed"/> so a clue can be tossed or passed a metre or two but not flung across the room.
    /// A release slower than <see cref="TossThreshold"/> counts as a drop, not a toss. Pure, unit tested.
    /// </summary>
    public sealed class TossEstimator
    {
        public const float DefaultMaxSpeed = 2f;
        public const float DefaultTossThreshold = 0.6f;
        public const float DefaultWindow = 0.1f;

        public float MaxSpeed { get; set; } = DefaultMaxSpeed;
        public float TossThreshold { get; set; } = DefaultTossThreshold;
        public float Window { get; set; } = DefaultWindow;

        private const int Capacity = 16;
        private readonly double[] _times = new double[Capacity];
        private readonly Vector3[] _positions = new Vector3[Capacity];
        private int _count;
        private int _next;

        public void Clear()
        {
            _count = 0;
            _next = 0;
        }

        public void AddSample(double time, Vector3 position)
        {
            _times[_next] = time;
            _positions[_next] = position;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>
        /// Measured release velocity (uncapped): from the oldest sample within <see cref="Window"/> of the newest one
        /// to the newest. Zero with fewer than two samples or no time between them.
        /// </summary>
        public Vector3 RawVelocity()
        {
            if (_count < 2) return Vector3.zero;
            int newest = (_next - 1 + Capacity) % Capacity;
            int oldest = newest;
            for (int k = 1; k < _count; k++)
            {
                int i = (newest - k + Capacity) % Capacity;
                if (_times[newest] - _times[i] > Window) break;
                oldest = i;
            }
            double dt = _times[newest] - _times[oldest];
            if (dt <= 1e-4) return Vector3.zero;
            return (_positions[newest] - _positions[oldest]) / (float)dt;
        }

        /// <summary>Velocity limited to <paramref name="maxSpeed"/> (direction kept).</summary>
        public static Vector3 Cap(Vector3 velocity, float maxSpeed) =>
            velocity.sqrMagnitude > maxSpeed * maxSpeed ? velocity.normalized * maxSpeed : velocity;

        /// <summary>The velocity to give the clue on release, its measured speed, and whether it counts as a toss.</summary>
        public Vector3 Release(out float rawSpeed, out bool isToss, out bool capped)
        {
            Vector3 raw = RawVelocity();
            rawSpeed = raw.magnitude;
            isToss = rawSpeed >= TossThreshold;
            capped = rawSpeed > MaxSpeed;
            return Cap(raw, MaxSpeed);
        }
    }
}
