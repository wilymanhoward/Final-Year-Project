using System;
using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class ExperimentConfigTests
    {
        [Test]
        public void FileName_FollowsSpec()
        {
            var c = new ExperimentConfig { pairCode = "G07", participantId = "P13", condition = StudyCondition.B, caseId = CaseId.X };
            Assert.AreEqual("G07_P13_B_X_20261001_090503.csv", c.BuildFileName(new DateTime(2026, 10, 1, 9, 5, 3)));
        }

        [Test]
        public void FileName_SanitizesUnsafeCharacters()
        {
            var c = new ExperimentConfig { pairCode = "G/0 7", participantId = "", condition = StudyCondition.A, caseId = CaseId.Y };
            Assert.AreEqual("G07_NA_A_Y_20261001_090503_summary.csv", c.BuildFileName(new DateTime(2026, 10, 1, 9, 5, 3), "summary"));
        }

        [Test]
        public void Validate_RejectsBadCodes()
        {
            Assert.IsTrue(new ExperimentConfig().Validate(out _));
            Assert.IsFalse(new ExperimentConfig { participantId = "Jane Doe" }.Validate(out _));
            Assert.IsFalse(new ExperimentConfig { pairCode = "" }.Validate(out _));
            Assert.IsFalse(new ExperimentConfig { timeCapMinutes = 0f }.Validate(out _));
        }

        [TestCase("P01", 1, "P02")]
        [TestCase("G09", 1, "G10")]
        [TestCase("P01", -1, "P00")]
        [TestCase("P00", -1, "P00")]
        [TestCase("P", 1, "P01")]
        [TestCase("P99", 1, "P100")]
        public void StepCode(string code, int delta, string expected)
        {
            Assert.AreEqual(expected, ExperimentConfig.StepCode(code, delta));
        }

        [Test]
        public void TimeCapSeconds()
        {
            Assert.AreEqual(900.0, new ExperimentConfig().TimeCapSeconds, 1e-9);
        }
    }

    public class SessionTimerTests
    {
        [Test]
        public void MeasuresElapsed_AndFreezesOnStop()
        {
            var t = new SessionTimer(900);
            t.Start(100);
            Assert.AreEqual(30, t.Elapsed(130), 1e-9);
            Assert.AreEqual(45, t.Stop(145), 1e-9);
            Assert.AreEqual(45, t.Elapsed(500), 1e-9);
            Assert.AreEqual(45, t.Stop(600), 1e-9);
        }

        [Test]
        public void CapReached_AndElapsedClampedToCap()
        {
            var t = new SessionTimer(900);
            t.Start(0);
            Assert.IsFalse(t.IsCapReached(899.9));
            Assert.IsTrue(t.IsCapReached(900));
            Assert.AreEqual(900, t.Stop(903), 1e-9);
        }

        [Test]
        public void NotRunning_NeverReachesCap()
        {
            var t = new SessionTimer(1);
            Assert.IsFalse(t.IsCapReached(100));
            Assert.AreEqual(0, t.Elapsed(100));
        }
    }
}
