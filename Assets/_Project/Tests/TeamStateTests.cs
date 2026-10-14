using System;
using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class TeamStateTests
    {
        // ---- TeamMessage ----

        [Test]
        public void MessagesRoundTrip()
        {
            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.ReqStart(StartSource.ParticipantStart), out var m));
            Assert.AreEqual(TeamMessageKind.ReqStart, m.Kind);
            Assert.AreEqual(StartSource.ParticipantStart, m.Source);

            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.End(SessionEndReason.Aborted), out m));
            Assert.AreEqual(TeamMessageKind.End, m.Kind);
            Assert.AreEqual(SessionEndReason.Aborted, m.Reason);

            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.Found("knife", "P02"), out m));
            Assert.AreEqual(TeamMessageKind.Found, m.Kind);
            Assert.AreEqual("knife", m.ClueId);
            Assert.AreEqual("P02", m.By);

            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.ReqTag("knife", false, "P01"), out m));
            Assert.AreEqual(TeamMessageKind.ReqTag, m.Kind);
            Assert.IsFalse(m.Tagged);

            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.Submit("P01", "s1", new[] { "a", "b" }), out m));
            Assert.AreEqual("s1", m.SuspectId);
            CollectionAssert.AreEqual(new[] { "a", "b" }, m.EvidenceIds);

            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.ReqSubmit("P01", "s1", Array.Empty<string>()), out m));
            Assert.AreEqual(0, m.EvidenceIds.Count);

            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.Start("Y", "B", StartSource.StartNow), out m));
            Assert.AreEqual(StartSource.StartNow, m.Source);
            Assert.AreEqual("Y", m.CaseCode);
            Assert.AreEqual("B", m.ConditionCode);

            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.Contamination("z1", BodyPart.LeftHand, "P02"), out m));
            Assert.AreEqual(BodyPart.LeftHand, m.Part);
            Assert.AreEqual("z1", m.ZoneId);

            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.ReqOwnership("knife"), out m));
            Assert.AreEqual(TeamMessageKind.ReqOwnership, m.Kind);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Bogus|x")]
        [TestCase("3|x|y")]
        [TestCase("ReqStart")]
        [TestCase("ReqStart|extra")]
        [TestCase("ReqStart|1")]
        [TestCase("Start|Y|B")]
        [TestCase("Found|knife")]
        [TestCase("Found||P01")]
        [TestCase("Tag|knife|2|P01")]
        [TestCase("End|NotAReason")]
        [TestCase("End|1")]
        [TestCase("Contamination|z1|Foot|P01")]
        [TestCase("Submit|P01|s1|a,,b")]
        public void RejectsMalformedMessages(string text)
        {
            Assert.IsFalse(TeamMessage.TryParse(text, out _));
        }

        [Test]
        public void StartSourceCodes()
        {
            Assert.AreEqual("participant_start", SessionManager.StartSourceCode(StartSource.ParticipantStart));
            Assert.AreEqual("start_now", SessionManager.StartSourceCode(StartSource.StartNow));
        }

        [Test]
        public void BuildersRejectUnsendableIds()
        {
            Assert.Throws<ArgumentException>(() => TeamMessage.Found("a|b", "P01"));
            Assert.Throws<ArgumentException>(() => TeamMessage.Submit("P01", "s1", new[] { "a,b" }));
            Assert.IsFalse(TeamMessage.IsSendableId(""));
            Assert.IsTrue(TeamMessage.IsSendableId("TestCube_delete_me"));
        }

        // ---- CaseState team counters ----

        [Test]
        public void TeamRunCountsSelfAndPartner()
        {
            var s = new CaseState();
            s.Begin(true, "P01");
            Assert.IsTrue(s.MarkClueFound("a", "P01"));
            Assert.IsTrue(s.MarkClueFound("b", "P02"));
            Assert.IsFalse(s.MarkClueFound("b", "P01"));   // team-wide: already found by the partner
            Assert.AreEqual("P02", s.FoundBy("b"));
            s.MarkClueTagged("a", "P02");
            s.MarkClueTagged("b", "P01");

            Assert.AreEqual(2, s.FoundClues.Count);
            Assert.AreEqual(1, s.CluesFoundBySelf);
            Assert.AreEqual(1, s.CluesTaggedBySelf);

            s.RecordContamination("z", BodyPart.Head);
            s.RecordPartnerContamination("z", BodyPart.RightHand, "P02");
            s.RecordPartnerContamination("z", BodyPart.RightHand, "P02");
            Assert.AreEqual(1, s.ContaminationErrors);
            Assert.AreEqual(2, s.PartnerContaminationErrors);
            Assert.AreEqual(3, s.TeamContaminationErrors);
        }

        [Test]
        public void LastActorIsSetBeforeEvents()
        {
            var s = new CaseState();
            s.Begin(true, "P01");
            string seen = null;
            s.ClueTagged += _ => seen = s.LastActorId;
            s.MarkClueTagged("a", "P02");
            Assert.AreEqual("P02", seen);

            string own = "unset";
            s.ContaminationRecorded += _ => own = s.LastActorId;
            s.RecordContamination("z", BodyPart.Head);
            Assert.AreEqual("P01", own);
        }

        [Test]
        public void SoloRunHasNoActorsAndSelfEqualsTotal()
        {
            var s = new CaseState();
            s.Begin();
            s.MarkClueFound("a");
            s.MarkClueTagged("b");
            Assert.IsFalse(s.IsTeamRun);
            Assert.IsNull(s.LastActorId);
            Assert.AreEqual(2, s.CluesFoundBySelf);
            Assert.AreEqual(1, s.CluesTaggedBySelf);
        }

        [Test]
        public void OnlyTheFirstSubmissionCounts()
        {
            var s = new CaseState();
            s.Begin(true, "P01");
            var r = AnswerChecker.Check("s1", new[] { "a" }, "s1", new[] { "a" });
            Assert.IsTrue(s.RecordSubmission("s1", new[] { "a" }, r, "P02"));
            Assert.IsFalse(s.RecordSubmission("s2", new[] { "a" }, r, "P01"));
            Assert.AreEqual("P02", s.SubmittedBy);
            Assert.AreEqual("s1", s.SubmittedSuspectId);
        }

        [Test]
        public void ResetClearsTeamFields()
        {
            var s = new CaseState();
            s.Begin(true, "P01");
            s.MarkClueFound("a", "P02");
            s.RecordPartnerContamination("z", BodyPart.Head, "P02");
            s.Reset();
            Assert.IsFalse(s.IsTeamRun);
            Assert.IsNull(s.SelfId);
            Assert.AreEqual(0, s.PartnerContaminationErrors);
            Assert.IsNull(s.FoundBy("a"));
        }
    }
}
