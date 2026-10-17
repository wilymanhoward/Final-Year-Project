using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class NaturalMotionTests
    {
        [Test]
        public void Spring_ReachesTheTarget_WithASmallOvershoot()
        {
            var s = new Spring(2f, 0.6f);
            float max = 0f;
            for (int i = 0; i < 200; i++) max = System.Math.Max(max, s.Step(10f, 1f / 72f));
            Assert.AreEqual(10f, s.Value, 0.05f, "settles on the target");
            Assert.Greater(max, 10f, "underdamped: overshoots a little");
            Assert.Less(max, 12f, "but only a little");
        }

        [Test]
        public void Spring_Kick_MovesAndReturns()
        {
            var s = new Spring(3f, 0.45f);
            s.Kick(-50f);
            float min = 0f;
            for (int i = 0; i < 300; i++) min = System.Math.Min(min, s.Step(0f, 1f / 72f));
            Assert.Less(min, -1f);
            Assert.AreEqual(0f, s.Value, 0.05f);
        }

        [Test]
        public void Spring_StableWithLongFrames()
        {
            var s = new Spring(3.6f, 0.4f);
            for (int i = 0; i < 50; i++) s.Step(1f, 0.1f);
            Assert.AreEqual(1f, s.Value, 0.05f);
        }

        [Test]
        public void Beats_OnLoudnessJumps_NotOnSteadyOrQuiet()
        {
            var b = new SpeechBeatDetector();
            double t = 0;
            const float dt = 1f / 72f;
            for (int i = 0; i < 30; i++, t += dt) Assert.AreEqual(0f, b.Update(0.02f, t, dt));   // quiet
            Assert.Greater(b.Update(0.6f, t, dt), 0f);                                            // stress
            t += dt;
            Assert.AreEqual(0f, b.Update(0.7f, t, dt), "no second beat within the minimum interval");
            for (int i = 0; i < 60; i++, t += dt) b.Update(0.6f, t, dt);                          // steady loud
            Assert.AreEqual(0f, b.Update(0.6f, t, dt), "steady loudness is not a beat");
        }
    }
}
