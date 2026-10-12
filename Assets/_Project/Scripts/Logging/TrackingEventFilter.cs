namespace FYP.Detective
{
    /// <summary>What a <see cref="TrackingEventFilter"/> wants logged this frame.</summary>
    public enum TrackingEvent
    {
        None = 0,
        Lost = 1,
        Regained = 2,
    }

    /// <summary>
    /// Debounces hand-tracking changes for the LOG only: a loss is reported once tracking has been missing for
    /// <see cref="MinLostSeconds"/> (dropouts shorter than that produce no events), and "regained" only follows a
    /// reported loss. Exclusion zones and samples still use the raw state, so safety stays strict.
    /// Pure logic; call <see cref="Update"/> every frame with the raw state and the clock.
    /// </summary>
    public sealed class TrackingEventFilter
    {
        public const double DefaultMinLostSeconds = 0.2;

        private double _untrackedSince = -1;
        private bool _reportedLost;

        public double MinLostSeconds { get; set; }

        /// <summary>Session time when the reported loss actually began (valid after a Lost event).</summary>
        public double LostSince { get; private set; }

        public TrackingEventFilter(double minLostSeconds = DefaultMinLostSeconds)
        {
            MinLostSeconds = minLostSeconds;
        }

        public TrackingEvent Update(bool tracked, double now)
        {
            if (tracked)
            {
                _untrackedSince = -1;
                if (!_reportedLost) return TrackingEvent.None;
                _reportedLost = false;
                return TrackingEvent.Regained;
            }

            if (_untrackedSince < 0) _untrackedSince = now;
            if (_reportedLost || now - _untrackedSince < MinLostSeconds) return TrackingEvent.None;
            _reportedLost = true;
            LostSince = _untrackedSince;
            return TrackingEvent.Lost;
        }

        public void Reset()
        {
            _untrackedSince = -1;
            _reportedLost = false;
            LostSince = 0;
        }
    }
}
