namespace FYP.Detective
{
    /// <summary>
    /// Pure stopwatch with a time cap. The caller passes in the current time
    /// (seconds), which makes it deterministic in tests.
    /// </summary>
    public sealed class SessionTimer
    {
        private double _startTime;
        private double _stoppedElapsed;

        public double CapSeconds { get; set; }
        public bool IsRunning { get; private set; }

        public SessionTimer(double capSeconds)
        {
            CapSeconds = capSeconds;
        }

        public void Start(double now)
        {
            _startTime = now;
            _stoppedElapsed = 0;
            IsRunning = true;
        }

        /// <summary>Freezes the elapsed time and returns it. Stopping twice keeps the first value.</summary>
        public double Stop(double now)
        {
            if (IsRunning)
            {
                _stoppedElapsed = Clamp(now - _startTime);
                IsRunning = false;
            }
            return _stoppedElapsed;
        }

        public void Reset()
        {
            IsRunning = false;
            _startTime = 0;
            _stoppedElapsed = 0;
        }

        /// <summary>Elapsed seconds, never more than the cap (a capped session lasted exactly the cap).</summary>
        public double Elapsed(double now)
        {
            return IsRunning ? Clamp(now - _startTime) : _stoppedElapsed;
        }

        public bool IsCapReached(double now)
        {
            return IsRunning && CapSeconds > 0 && now - _startTime >= CapSeconds;
        }

        private double Clamp(double elapsed)
        {
            if (elapsed < 0) return 0;
            return CapSeconds > 0 && elapsed > CapSeconds ? CapSeconds : elapsed;
        }
    }
}
