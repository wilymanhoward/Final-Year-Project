using System;

namespace FYP.Detective
{
    /// <summary>
    /// Counts app pauses during a run (headset taken off, app sent to the background) and their total length,
    /// so analysis can use active time = elapsed - paused. Pure logic; the caller passes the clock in.
    /// Repeated Pause/Resume calls are ignored, so it can be fed from both pause and focus callbacks.
    /// </summary>
    public sealed class PauseTracker
    {
        private double _pausedSince = -1;
        private double _completedPausedSeconds;

        public int PauseCount { get; private set; }
        public bool IsPaused => _pausedSince >= 0;

        public void Reset()
        {
            _pausedSince = -1;
            _completedPausedSeconds = 0;
            PauseCount = 0;
        }

        /// <summary>Starts a pause. Returns false if already paused.</summary>
        public bool Pause(double now)
        {
            if (IsPaused) return false;
            _pausedSince = now;
            PauseCount++;
            return true;
        }

        /// <summary>Ends the current pause. Returns false if not paused.</summary>
        public bool Resume(double now)
        {
            if (!IsPaused) return false;
            _completedPausedSeconds += Math.Max(0, now - _pausedSince);
            _pausedSince = -1;
            return true;
        }

        /// <summary>Total paused time, including a pause still in progress at <paramref name="now"/>.</summary>
        public double PausedSeconds(double now)
        {
            return _completedPausedSeconds + (IsPaused ? Math.Max(0, now - _pausedSince) : 0);
        }
    }
}
