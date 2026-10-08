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
