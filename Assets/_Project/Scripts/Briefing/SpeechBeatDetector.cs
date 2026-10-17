namespace FYP.Detective
{
    /// <summary>
    /// Finds "beats" in speech (session 6): moments where the voice gets clearly louder than a moment ago, roughly the
    /// stressed syllables, which people mark with small head nods and hand beats. Feed it the voice loudness (0..1) every
    /// frame; <see cref="Update"/> returns the beat strength (0 = no beat). At most one beat per
    /// <see cref="MinInterval"/> s. Time passed in, pure, unit tested.
    /// </summary>
    public sealed class SpeechBeatDetector
    {
        public float Threshold { get; set; } = 0.12f;
        public float MinRise { get; set; } = 0.08f;
        public float MinInterval { get; set; } = 0.22f;

        private float _slow;
        private double _lastBeat = double.NegativeInfinity;

        public void Reset()
        {
            _slow = 0f;
            _lastBeat = double.NegativeInfinity;
        }

        public float Update(float level, double now, float dt)
        {
            // Slow follower of the loudness: a beat is the level jumping above it.
            float k = dt <= 0f ? 0f : 1f - (float)System.Math.Exp(-dt * 6.0);
            float rise = level - _slow;
            _slow += (level - _slow) * k;
            if (level < Threshold || rise < MinRise || now - _lastBeat < MinInterval) return 0f;
            _lastBeat = now;
            return System.Math.Min(1f, rise / 0.4f);
        }
    }
}
