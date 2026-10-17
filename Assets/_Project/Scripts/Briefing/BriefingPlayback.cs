namespace FYP.Detective
{
    /// <summary>Where the detective's briefing is: not begun yet, talking, or said everything.</summary>
    public enum BriefingStage
    {
        NotStarted = 0,
        Playing = 1,
        Finished = 2,
        /// <summary>Team runs: this player tapped Begin; waiting for the partner to tap it too.</summary>
        WaitingForPartner = 3,
    }

    /// <summary>
    /// Timing of the detective's briefing (session 6): lines play one after another with a short pause between them;
    /// <see cref="Tick"/> says which line is current and when everything has been said. Time is passed in (pure,
    /// unit tested); the durations are the audio clip lengths (or an estimate for lines without audio).
    /// </summary>
    public sealed class BriefingPlayback
    {
        public const float DefaultGapSeconds = 0.45f;

        private readonly float[] _durations;
        private readonly float _gap;
        private double _startedAt = -1.0;

        public BriefingPlayback(float[] durations, float gapSeconds = DefaultGapSeconds)
        {
            _durations = durations ?? new float[0];
            _gap = gapSeconds;
        }

        public int LineCount => _durations.Length;
        public bool IsPlaying => _startedAt >= 0.0 && !IsFinished;

        /// <summary>True once every line has been said (stays true until <see cref="Begin"/> is called again).</summary>
        public bool IsFinished { get; private set; }

        /// <summary>Index of the line being said, or -1 (not started / in a pause / finished).</summary>
        public int CurrentLine { get; private set; } = -1;

        /// <summary>Starts (or replays) from the first line.</summary>
        public void Begin(double now)
        {
            _startedAt = now;
            IsFinished = _durations.Length == 0;
            CurrentLine = -1;
        }

        public void Stop()
        {
            _startedAt = -1.0;
            CurrentLine = -1;
        }

        /// <summary>Updates <see cref="CurrentLine"/>; returns true when the line changed (start the next clip then).</summary>
        public bool Tick(double now)
        {
            if (_startedAt < 0.0 || IsFinished) return false;
            double t = now - _startedAt;
            int line = -1;
            for (int i = 0; i < _durations.Length; i++)
            {
                if (t < _durations[i]) { line = i; break; }
                t -= _durations[i];
                if (t < _gap) { line = -1; t = -1; break; }   // in the pause after line i
                t -= _gap;
            }
            if (t >= 0 && line == -1) IsFinished = true;   // past the last line (and its pause)
            bool changed = line != CurrentLine && line >= 0;
            CurrentLine = line;
            return changed;
        }
    }
}
