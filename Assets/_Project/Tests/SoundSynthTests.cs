using System;
using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class SoundSynthTests
    {
        [Test]
        public void EverySound_IsShort_Finite_AndNeverClips()
        {
            foreach (UiSound s in Enum.GetValues(typeof(UiSound)))
            {
                float[] d = SoundSynth.Make(s);
                float seconds = d.Length / (float)SoundSynth.SampleRate;
                Assert.That(seconds, Is.InRange(0.05f, 1f), s.ToString());
                float peak = 0f;
                foreach (float v in d)
                {
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), s.ToString());
                    peak = Math.Max(peak, Math.Abs(v));
                }
                Assert.That(peak, Is.InRange(0.3f, 0.81f), s + " peak");
                Assert.Less(Math.Abs(d[d.Length - 1]), 1e-3f, s + " ends silent (no click)");
            }
        }

        [Test]
        public void Fanfare_IsAboutASecond_AndClean()
        {
            float[] d = SoundSynth.Fanfare();
            Assert.That(d.Length / (float)SoundSynth.SampleRate, Is.InRange(1f, 1.5f));
            float peak = 0f;
            foreach (float v in d) { Assert.IsFalse(float.IsNaN(v)); peak = Math.Max(peak, Math.Abs(v)); }
            Assert.That(peak, Is.InRange(0.5f, 0.61f));
            Assert.Less(Math.Abs(d[d.Length - 1]), 1e-3f);
        }

        [Test]
        public void Sounds_AreTheSameEveryTime_AndDifferent()
        {
            CollectionAssert.AreEqual(SoundSynth.Make(UiSound.Peel), SoundSynth.Make(UiSound.Peel));
            CollectionAssert.AreNotEqual(SoundSynth.Make(UiSound.PinIn), SoundSynth.Make(UiSound.PinOut));
            Assert.Greater(SoundSynth.Make(UiSound.CaseClosed).Length, SoundSynth.Make(UiSound.Stamp).Length);
        }
    }
}
