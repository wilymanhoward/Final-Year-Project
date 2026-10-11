using System;

namespace FYP.Detective
{
    /// <summary>What can pause a run. Several can be active at once.</summary>
    public enum PauseSource
    {
        /// <summary>The app is paused / in the background (Unity stops updating).</summary>
        App = 0,
        /// <summary>The headset is off the user's head (proximity sensor); the app may keep running.</summary>
        HeadsetOff = 1,
    }

    /// <summary>
    /// Counts pauses during a run and their total length, so analysis can use active time = elapsed - paused.
    /// A pause lasts while ANY source is active, so overlapping sources (headset off, then the app also
    /// pauses) are counted once: one pause, time = the union of the intervals. Pure logic; the caller passes
    /// the clock in. Setting a source to the state it already has is ignored.
    /// </summary>
    public sealed class PauseTracker
    {
        private readonly bool[] _active = new bool[2];   // indexed by PauseSource
        private double _pausedSince = -1;
        private double _completedPausedSeconds;

        public int PauseCount { get; private set; }
        public bool IsPaused => _pausedSince >= 0;

        /// <summary>Length of the last finished pause (0 if none).</summary>
        public double LastPauseSeconds { get; private set; }

        public bool IsActive(PauseSource source) => _active[(int)source];

        public void Reset()
        {
            Array.Clear(_active, 0, _active.Length);
            _pausedSince = -1;
            _completedPausedSeconds = 0;
            LastPauseSeconds = 0;
            PauseCount = 0;
        }

        /// <summary>
        /// Turns one source on/off. Returns false if it already had that state. <paramref name="pauseStarted"/> /
        /// <paramref name="pauseEnded"/> say whether this call began or finished a (combined) pause.
        /// </summary>
        public bool SetActive(PauseSource source, bool active, double now, out bool pauseStarted, out bool pauseEnded)
        {
            pauseStarted = pauseEnded = false;
            int i = (int)source;
            if (_active[i] == active) return false;
            _active[i] = active;

            bool anyActive = false;
            foreach (bool a in _active) anyActive |= a;

            if (anyActive && !IsPaused)
            {
                _pausedSince = now;
                PauseCount++;
                pauseStarted = true;
            }
            else if (!anyActive && IsPaused)
            {
                LastPauseSeconds = Math.Max(0, now - _pausedSince);
                _completedPausedSeconds += LastPauseSeconds;
                _pausedSince = -1;
                pauseEnded = true;
            }
            return true;
        }

        /// <summary>App pause shortcut. Returns false if the app source was already paused.</summary>
        public bool Pause(double now) => SetActive(PauseSource.App, true, now, out _, out _);

        /// <summary>App resume shortcut. Returns false if the app source was not paused.</summary>
        public bool Resume(double now) => SetActive(PauseSource.App, false, now, out _, out _);

        /// <summary>Ends every source (e.g. at session end), closing a pause still in progress.</summary>
        public void EndAll(double now)
        {
            for (int i = 0; i < _active.Length; i++) SetActive((PauseSource)i, false, now, out _, out _);
        }

        /// <summary>Total paused time, including a pause still in progress at <paramref name="now"/>.</summary>
        public double PausedSeconds(double now)
        {
            return _completedPausedSeconds + (IsPaused ? Math.Max(0, now - _pausedSince) : 0);
        }
    }
}
