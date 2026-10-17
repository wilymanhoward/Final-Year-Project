namespace FYP.Detective
{
    /// <summary>
    /// Pure rule: a dropped clue has come to rest once its speed stays below <see cref="SpeedThreshold"/> for
    /// <see cref="RestSeconds"/>. <see cref="Update"/> returns true once per rest (time passed in, so it is testable).
    /// Used by <see cref="ClueResetter"/> to check where a dropped clue ended up.
    /// </summary>
    public sealed class ClueRestDetector
    {
        public const float DefaultSpeedThreshold = 0.05f;
        public const float DefaultRestSeconds = 0.5f;

        public float SpeedThreshold { get; set; }
        public float RestSeconds { get; set; }

        private double _slowSince = -1.0;
        private bool _reported;

        public ClueRestDetector(float speedThreshold = DefaultSpeedThreshold, float restSeconds = DefaultRestSeconds)
        {
            SpeedThreshold = speedThreshold;
            RestSeconds = restSeconds;
        }

        /// <summary>Feeds the current speed (m/s). True on the first update at which the clue counts as at rest.</summary>
        public bool Update(float speed, double now)
        {
            if (speed > SpeedThreshold)
            {
                _slowSince = -1.0;
                _reported = false;
                return false;
            }
            if (_slowSince < 0.0) _slowSince = now;
            if (_reported || now - _slowSince < RestSeconds) return false;
            _reported = true;
            return true;
        }

        /// <summary>Forget the current state (e.g. when the clue is picked up or put back home).</summary>
        public void Reset()
        {
            _slowSince = -1.0;
            _reported = false;
        }
    }
}
