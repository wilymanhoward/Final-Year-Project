using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class ParticipantCardTests
    {
        [Test]
        public void HiddenBeforeArmingAndWhileRunning()
        {
            Assert.IsFalse(ParticipantCardModel.Compute(false, SessionPhase.NotStarted, null, false).Visible);
            Assert.IsFalse(ParticipantCardModel.Compute(true, SessionPhase.Running, null, false).Visible);
        }

        [Test]
        public void ArmedShowsBriefingWithStart()
        {
            var v = ParticipantCardModel.Compute(true, SessionPhase.NotStarted, null, false);
            Assert.IsTrue(v.Visible);
            Assert.IsTrue(v.ShowStart);
            Assert.AreEqual(ParticipantCardModel.BriefingTitle, v.Title);
            StringAssert.DoesNotContain("partner", v.Body);

            var team = ParticipantCardModel.Compute(true, SessionPhase.Ended, SessionEndReason.Aborted, true);
            Assert.IsTrue(team.ShowStart, "arming again after a run shows the briefing, not the old end message");
            StringAssert.Contains("partner", team.Body);
        }

        [TestCase(SessionEndReason.Submitted, "Case closed")]
        [TestCase(SessionEndReason.TimeCap, "Time is up")]
        [TestCase(SessionEndReason.Aborted, "Session stopped")]
        [TestCase(SessionEndReason.Interrupted, "Session stopped")]
        public void EndMessagesAreNeutral(SessionEndReason reason, string title)
        {
            var v = ParticipantCardModel.Compute(false, SessionPhase.Ended, reason, false);
            Assert.IsTrue(v.Visible);
            Assert.IsFalse(v.ShowStart);
            Assert.AreEqual(title, v.Title);
            StringAssert.DoesNotContain("correct", v.Body.ToLowerInvariant());
            StringAssert.DoesNotContain("wrong", v.Body.ToLowerInvariant());
        }

        [Test]
        public void ArmMessagesRoundTrip()
        {
            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.Arm(true), out var m));
            Assert.AreEqual(TeamMessageKind.Arm, m.Kind);
            Assert.IsTrue(m.Armed);
            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.ReqArm(false), out m));
            Assert.AreEqual(TeamMessageKind.ReqArm, m.Kind);
            Assert.IsFalse(m.Armed);
            Assert.IsFalse(TeamMessage.TryParse("Arm|2", out _));
            Assert.IsFalse(TeamMessage.TryParse("Arm", out _));
        }
    }
}
