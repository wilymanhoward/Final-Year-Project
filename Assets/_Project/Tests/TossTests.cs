using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class TossEstimatorTests
    {
        private static TossEstimator Moving(Vector3 velocity, int frames = 10, double dt = 1.0 / 72.0)
        {
            var t = new TossEstimator();
            for (int i = 0; i < frames; i++) t.AddSample(i * dt, velocity * (float)(i * dt));
            return t;
        }

        [Test]
        public void GentleToss_KeepsItsVelocity()
        {
            Vector3 v = Moving(new Vector3(1.2f, 0.5f, 0f)).Release(out float speed, out bool isToss, out bool capped);
            Assert.AreEqual(1.2f, v.x, 0.01f);
            Assert.AreEqual(0.5f, v.y, 0.01f);
            Assert.AreEqual(1.3f, speed, 0.01f);
            Assert.IsTrue(isToss);
            Assert.IsFalse(capped);
        }

        [Test]
        public void HardThrow_IsCappedAt2_DirectionKept()
        {
            Vector3 v = Moving(new Vector3(0f, 0f, 6f)).Release(out float speed, out bool isToss, out bool capped);
            Assert.AreEqual(2f, v.magnitude, 1e-4f);
            Assert.AreEqual(1f, v.normalized.z, 1e-4f);
            Assert.AreEqual(6f, speed, 0.01f);
            Assert.IsTrue(isToss && capped);
        }

        [Test]
        public void SlowRelease_IsADropNotAToss()
        {
            Moving(new Vector3(0.3f, 0f, 0f)).Release(out float speed, out bool isToss, out _);
            Assert.AreEqual(0.3f, speed, 0.01f);
            Assert.IsFalse(isToss);
        }

        [Test]
        public void OnlyTheLastWindowCounts()
        {
            var t = new TossEstimator();
            t.AddSample(0.0, Vector3.zero);
            t.AddSample(0.5, new Vector3(5f, 0f, 0f));     // fast swing long ago
            t.AddSample(0.55, new Vector3(5.05f, 0f, 0f)); // then slow at release: 1 m/s
            Assert.AreEqual(1f, t.RawVelocity().x, 0.01f);
        }

        [Test]
        public void NoSamples_NoVelocity()
        {
            var t = new TossEstimator();
            Assert.AreEqual(Vector3.zero, t.RawVelocity());
            t.AddSample(1.0, Vector3.one);
            Assert.AreEqual(Vector3.zero, t.RawVelocity());
        }

        [Test]
        public void ThrownRows_OnlyWhileRunning()
        {
            var s = new CaseState();
            int raised = 0;
            s.ClueThrown += _ => raised++;
            Assert.IsFalse(s.RecordClueThrown("X_K2", "P01", 1.5f, false));
            s.Begin();
            Assert.IsTrue(s.RecordClueThrown("X_K2", "P01", 3.4f, true));
            Assert.AreEqual(1, raised);
            Assert.AreEqual(3.4f, s.LastThrowSpeed, 1e-4f);
            Assert.IsTrue(s.LastThrowCapped);
            Assert.AreEqual("P01", s.LastActorId);
        }
    }
}
