using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class PauseTrackerTests
    {
        [Test]
        public void CountsPausesAndTotalPausedTime()
        {
            var p = new PauseTracker();
            Assert.IsTrue(p.Pause(10));
            Assert.IsTrue(p.Resume(15));
            Assert.IsTrue(p.Pause(20));
            Assert.IsTrue(p.Resume(21.5));
            Assert.AreEqual(2, p.PauseCount);
            Assert.AreEqual(6.5, p.PausedSeconds(100), 1e-9);
        }

        [Test]
        public void RepeatedCallsAreIgnored()
        {
            var p = new PauseTracker();
            Assert.IsFalse(p.Resume(5));        // not paused
            Assert.IsTrue(p.Pause(10));
            Assert.IsFalse(p.Pause(11));        // pause + focus loss both report
            Assert.IsTrue(p.Resume(14));
            Assert.IsFalse(p.Resume(15));
            Assert.AreEqual(1, p.PauseCount);
            Assert.AreEqual(4, p.PausedSeconds(20), 1e-9);
        }

        [Test]
        public void OpenPause_CountsUpToNow()
        {
            var p = new PauseTracker();
            p.Pause(10);
            Assert.IsTrue(p.IsPaused);
            Assert.AreEqual(5, p.PausedSeconds(15), 1e-9);
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var p = new PauseTracker();
            p.Pause(1);
            p.Reset();
            Assert.IsFalse(p.IsPaused);
            Assert.AreEqual(0, p.PauseCount);
            Assert.AreEqual(0, p.PausedSeconds(100), 1e-9);
        }
    }

    public class PauseTrackerOverlapTests
    {
        private static void Set(PauseTracker p, PauseSource s, bool on, double t) => p.SetActive(s, on, t, out _, out _);

        [Test]
        public void HeadsetOff_IsAPause()
        {
            var p = new PauseTracker();
            Assert.IsTrue(p.SetActive(PauseSource.HeadsetOff, true, 10, out bool started, out _));
            Assert.IsTrue(started);
            Assert.IsTrue(p.SetActive(PauseSource.HeadsetOff, false, 25, out _, out bool ended));
            Assert.IsTrue(ended);
            Assert.AreEqual(1, p.PauseCount);
            Assert.AreEqual(15, p.PausedSeconds(100), 1e-9);
            Assert.AreEqual(15, p.LastPauseSeconds, 1e-9);
        }

        [Test]
        public void AppPauseInsideHeadsetOff_IsCountedOnce()
        {
            // headset off 10..40, app paused 15..30 (nested)
            var p = new PauseTracker();
            Set(p, PauseSource.HeadsetOff, true, 10);
            Assert.IsTrue(p.SetActive(PauseSource.App, true, 15, out bool started, out _));
            Assert.IsFalse(started);                    // already paused: not a new pause
            Assert.IsTrue(p.SetActive(PauseSource.App, false, 30, out _, out bool ended));
            Assert.IsFalse(ended);                      // headset still off
            Set(p, PauseSource.HeadsetOff, false, 40);
            Assert.AreEqual(1, p.PauseCount);
            Assert.AreEqual(30, p.PausedSeconds(100), 1e-9);
            Assert.AreEqual(30, p.LastPauseSeconds, 1e-9);
        }

        [Test]
        public void PartlyOverlapping_UsesTheUnion()
        {
            // headset off 10..20, app paused 18..26 → one pause 10..26 = 16 s (not 10 + 8 = 18)
            var p = new PauseTracker();
            Set(p, PauseSource.HeadsetOff, true, 10);
            Set(p, PauseSource.App, true, 18);
            Set(p, PauseSource.HeadsetOff, false, 20);
            Assert.IsTrue(p.IsPaused);
            Set(p, PauseSource.App, false, 26);
            Assert.AreEqual(1, p.PauseCount);
            Assert.AreEqual(16, p.PausedSeconds(100), 1e-9);
        }

        [Test]
        public void SeparatePauses_FromDifferentSources_AreCountedSeparately()
        {
            var p = new PauseTracker();
            Set(p, PauseSource.App, true, 10);
            Set(p, PauseSource.App, false, 12);
            Set(p, PauseSource.HeadsetOff, true, 50);
            Set(p, PauseSource.HeadsetOff, false, 55);
            Assert.AreEqual(2, p.PauseCount);
            Assert.AreEqual(7, p.PausedSeconds(100), 1e-9);
        }

        [Test]
        public void RepeatedSourceState_IsIgnored()
        {
            var p = new PauseTracker();
            Set(p, PauseSource.HeadsetOff, true, 10);
            Assert.IsFalse(p.SetActive(PauseSource.HeadsetOff, true, 12, out _, out _));
            Assert.IsFalse(p.SetActive(PauseSource.App, false, 13, out _, out _));
            Assert.AreEqual(1, p.PauseCount);
        }

        [Test]
        public void EndAll_ClosesAnOpenPause()
        {
            var p = new PauseTracker();
            Set(p, PauseSource.HeadsetOff, true, 10);
            Set(p, PauseSource.App, true, 12);
            p.EndAll(20);
            Assert.IsFalse(p.IsPaused);
            Assert.IsFalse(p.IsActive(PauseSource.App));
            Assert.IsFalse(p.IsActive(PauseSource.HeadsetOff));
            Assert.AreEqual(10, p.PausedSeconds(100), 1e-9);
            Assert.AreEqual(1, p.PauseCount);
        }

        [Test]
        public void Reset_ClearsSources()
        {
            var p = new PauseTracker();
            Set(p, PauseSource.HeadsetOff, true, 10);
            p.Reset();
            Assert.IsFalse(p.IsActive(PauseSource.HeadsetOff));
            Assert.IsTrue(p.SetActive(PauseSource.HeadsetOff, true, 20, out bool started, out _));
            Assert.IsTrue(started);
        }
    }

    public class CaseStatePauseTests
    {
        [Test]
        public void ActiveTime_IsElapsedMinusPaused_AndResetOnBegin()
        {
            var s = new CaseState();
            s.SetPauseStats(5, 1);                 // ignored: not running
            Assert.AreEqual(0, s.PausedSeconds);
            s.Begin();
            s.SetElapsed(100);
            s.SetPauseStats(30, 2);
            Assert.AreEqual(70, s.ActiveSeconds, 1e-9);
            Assert.AreEqual(2, s.PauseCount);
            s.End(SessionEndReason.Aborted, 100);
            s.Begin();
            Assert.AreEqual(0, s.PausedSeconds);
            Assert.AreEqual(0, s.PauseCount);
        }

        [Test]
        public void ActiveTime_NeverNegative()
        {
            var s = new CaseState();
            s.Begin();
            s.SetElapsed(10);
            s.SetPauseStats(20, 1);
            Assert.AreEqual(0, s.ActiveSeconds);
        }
    }
}
