using System;

namespace FYP.Detective
{
    /// <summary>The interaction sounds (user, 2026-11-04: "a sound when I click a button or pick on the board").</summary>
    public enum UiSound
    {
        /// <summary>Any button: a soft switch, press and release.</summary>
        Click,
        /// <summary>Tag as evidence: a rubber stamp hitting paper on a hard surface.</summary>
        Stamp,
        /// <summary>Untag: the evidence tag peeled off (paper rustle).</summary>
        Peel,
        /// <summary>Board: a pushpin pressed into cork (suspect or evidence chosen).</summary>
        PinIn,
        /// <summary>Board: a pushpin pulled out (choice removed).</summary>
        PinOut,
        /// <summary>Answer submitted: a heavy stamp, then the case file closing.</summary>
        CaseClosed,
    }

    /// <summary>
    /// Makes the interaction sounds in code (no audio assets, like the contamination buzz): short physical sounds built
    /// from decaying sines (the body of an object) and filtered noise (paper, contact). Pure and deterministic (fixed noise
    /// seed), so every headset and every run sounds the same; unit tested.
    /// </summary>
    public static class SoundSynth
    {
        public const int SampleRate = 44100;

        public static float[] Make(UiSound sound)
        {
            switch (sound)
            {
                case UiSound.Click: return Click();
                case UiSound.Stamp: return Stamp(1f, 0.32f);
                case UiSound.Peel: return Peel();
                case UiSound.PinIn: return PinIn();
                case UiSound.PinOut: return PinOut();
                case UiSound.CaseClosed: return CaseClosed();
                default: throw new ArgumentOutOfRangeException(nameof(sound));
            }
        }

        // Press: a bright contact tick and the switch body ringing briefly; release 45 ms later, quieter.
        private static float[] Click()
        {
            var d = Buffer(0.08f);
            var rnd = new Noise(1);
            Tick(d, 0f, rnd, 0.0015f, 0.55f, 0.45f);
            Ring(d, 0f, 1900f, 1900f, 0.006f, 0.35f);
            Tick(d, 0.045f, rnd, 0.001f, 0.25f, 0.45f);
            Ring(d, 0.045f, 2300f, 2300f, 0.004f, 0.15f);
            return Normalise(d, 0.5f);
        }

        // Thump of the stamp on the desk (pitch falls as it lands), the slap of paper, then the stamp lifting off.
        private static float[] Stamp(float weight, float length)
        {
            var d = Buffer(length);
            var rnd = new Noise(2);
            Ring(d, 0f, 150f / weight, 55f / weight, 0.025f * weight, 0.9f);
            Burst(d, 0f, rnd, 0.008f * weight, 0.6f, 0.25f);                  // paper slap, muffled
            Burst(d, 0.11f * weight, rnd, 0.025f, 0.12f, 0.6f, attack: 0.01f);   // sticky lift-off
            return Normalise(d, 0.8f);
        }

        // Crinkly paper: a dozen tiny bright grains spread over a quarter second.
        private static float[] Peel()
        {
            var d = Buffer(0.28f);
            var rnd = new Noise(3);
            for (int g = 0; g < 14; g++)
            {
                float at = 0.01f + 0.22f * rnd.Unit();
                Burst(d, at, rnd, 0.004f + 0.008f * rnd.Unit(), 0.2f + 0.3f * rnd.Unit(), 0.7f);
            }
            return Normalise(d, 0.45f);
        }

        // The metal tip clicks on the cork, then the dull thud of the thumb pushing it home.
        private static float[] PinIn()
        {
            var d = Buffer(0.12f);
            var rnd = new Noise(4);
            Tick(d, 0f, rnd, 0.0012f, 0.5f, 0.85f);
            Ring(d, 0f, 3300f, 3300f, 0.003f, 0.25f);
            Ring(d, 0.009f, 240f, 180f, 0.018f, 0.7f);    // cork thud
            Burst(d, 0.012f, rnd, 0.01f, 0.12f, 0.5f);    // the card settles
            return Normalise(d, 0.6f);
        }

        // A short squeak as the pin comes out of the cork, then a light tick.
        private static float[] PinOut()
        {
            var d = Buffer(0.1f);
            var rnd = new Noise(5);
            Ring(d, 0f, 2400f, 3400f, 0.012f, 0.25f, attack: 0.006f);
            Burst(d, 0.004f, rnd, 0.015f, 0.18f, 0.6f, attack: 0.005f);
            Tick(d, 0.03f, rnd, 0.001f, 0.35f, 0.85f);
            return Normalise(d, 0.4f);
        }

        // A heavy stamp, then the case file closing: a soft whoosh of air and the flat clap of the cover.
        private static float[] CaseClosed()
        {
            float[] stamp = Stamp(1.4f, 0.4f);
            var d = Buffer(0.85f);
            Array.Copy(stamp, d, stamp.Length);
            var rnd = new Noise(6);
            Burst(d, 0.42f, rnd, 0.08f, 0.15f, 0.12f, attack: 0.06f);   // air as the cover swings shut
            Ring(d, 0.5f, 120f, 80f, 0.03f, 0.55f);
            Burst(d, 0.5f, rnd, 0.012f, 0.4f, 0.3f);
            return Normalise(d, 0.8f);
        }

        /// <summary>
        /// Short victory fanfare for a solved case (supervisor, 2026-11-05): a brassy rising C-E-G arpeggio, then a held
        /// C major chord with a little vibrato. About 1.3 s.
        /// </summary>
        public static float[] Fanfare()
        {
            var d = Buffer(1.3f);
            Note(d, 0.00f, 0.13f, 523.25f, 0.5f);
            Note(d, 0.13f, 0.13f, 659.25f, 0.5f);
            Note(d, 0.26f, 0.13f, 783.99f, 0.5f);
            foreach (float f in new[] { 523.25f, 659.25f, 783.99f, 1046.5f })
                Note(d, 0.40f, 0.85f, f, 0.32f, vibrato: true);
            return Normalise(d, 0.6f);
        }

        // Brass-like note: a few harmonics, quick attack, gentle release.
        private static void Note(float[] d, float at, float length, float freq, float amp, bool vibrato = false)
        {
            int start = (int)(at * SampleRate), n = (int)(length * SampleRate);
            double phase = 0;
            for (int i = 0; i < n && start + i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Math.Min(1f, t / 0.012f) * Math.Min(1f, (length - t) / 0.08f);
                float f = freq * (vibrato ? 1f + 0.006f * (float)Math.Sin(2 * Math.PI * 5.5 * t) * Math.Min(1f, t / 0.3f) : 1f);
                phase += 2 * Math.PI * f / SampleRate;
                float wave = (float)(Math.Sin(phase) + 0.5 * Math.Sin(2 * phase) + 0.3 * Math.Sin(3 * phase) + 0.15 * Math.Sin(4 * phase));
                d[start + i] += amp * env * wave;
            }
        }

        // ---- building blocks ----

        private static float[] Buffer(float seconds) => new float[(int)Math.Ceiling(seconds * SampleRate)];

        /// <summary>Exponentially decaying sine whose pitch glides from f0 to f1 (a struck object).</summary>
        private static void Ring(float[] d, float at, float f0, float f1, float decay, float amp, float attack = 0.0005f)
        {
            int start = (int)(at * SampleRate);
            double phase = 0;
            for (int i = start; i < d.Length; i++)
            {
                float t = (i - start) / (float)SampleRate;
                float env = amp * (float)Math.Exp(-t / decay) * Math.Min(1f, t / attack);
                if (env < 1e-4f && t > attack) break;
                float f = f1 + (f0 - f1) * (float)Math.Exp(-t / (decay * 0.7f));
                phase += 2 * Math.PI * f / SampleRate;
                d[i] += env * (float)Math.Sin(phase);
            }
        }

        /// <summary>Noise burst; <paramref name="brightness"/> 0..1 = one-pole low-pass coefficient (low = muffled).</summary>
        private static void Burst(float[] d, float at, Noise rnd, float decay, float amp, float brightness, float attack = 0.0003f)
        {
            int start = (int)(at * SampleRate);
            float lp = 0f;
            for (int i = start; i < d.Length; i++)
            {
                float t = (i - start) / (float)SampleRate;
                float env = amp * (float)Math.Exp(-t / decay) * Math.Min(1f, t / attack);
                if (env < 1e-4f && t > attack) break;
                lp += brightness * (rnd.Signed() - lp);
                d[i] += env * lp;
            }
        }

        /// <summary>Very short high-passed noise: a hard contact.</summary>
        private static void Tick(float[] d, float at, Noise rnd, float decay, float amp, float brightness)
        {
            int start = (int)(at * SampleRate);
            float lp = 0f;
            for (int i = start; i < d.Length; i++)
            {
                float t = (i - start) / (float)SampleRate;
                float env = amp * (float)Math.Exp(-t / decay);
                if (env < 1e-4f) break;
                float x = rnd.Signed();
                lp += brightness * (x - lp);
                d[i] += env * (x - lp);   // high-pass = input - low-pass
            }
        }

        /// <summary>Scales to the given peak and fades the last 5 ms so the clip never ends in a click.</summary>
        private static float[] Normalise(float[] d, float peak)
        {
            float max = 1e-6f;
            foreach (float v in d) max = Math.Max(max, Math.Abs(v));
            int fade = Math.Min(d.Length, SampleRate / 200);
            for (int i = 0; i < d.Length; i++)
            {
                float tail = Math.Min(1f, (d.Length - 1 - i) / (float)fade);
                d[i] = d[i] / max * peak * tail;
            }
            return d;
        }

        /// <summary>Xorshift noise with a fixed seed per sound (deterministic, no UnityEngine.Random).</summary>
        private sealed class Noise
        {
            private uint _s;
            public Noise(uint seed) { _s = 2463534242u ^ (seed * 2654435761u); }
            public float Unit() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xFFFFFF) / (float)0x1000000; }
            public float Signed() => Unit() * 2f - 1f;
        }
    }
}
