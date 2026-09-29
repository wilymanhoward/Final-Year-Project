using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class AnswerCheckerTests
    {
        private static readonly string[] Key = { "knife", "letter" };

        [Test]
        public void CorrectAnswer()
        {
            var r = AnswerChecker.Check("butler", Key, "butler", new[] { "letter", "knife" });
            Assert.IsTrue(r.IsCorrect);
            Assert.IsTrue(r.SuspectCorrect);
            Assert.AreEqual(0, r.ReasoningErrorCount);
            Assert.IsEmpty(r.WrongEvidenceIds);
            Assert.IsEmpty(r.MissingEvidenceIds);
        }

        [Test]
        public void WrongSuspect_IsOneReasoningError()
        {
            var r = AnswerChecker.Check("butler", Key, "gardener", Key);
            Assert.IsFalse(r.IsCorrect);
            Assert.IsFalse(r.SuspectCorrect);
            Assert.AreEqual(1, r.ReasoningErrorCount);
        }

        [Test]
        public void EachWrongEvidence_IsOneReasoningError()
        {
            var r = AnswerChecker.Check("butler", Key, "butler", new[] { "knife", "letter", "cup", "hat" });
            Assert.IsFalse(r.IsCorrect);
            Assert.AreEqual(2, r.ReasoningErrorCount);
            CollectionAssert.AreEqual(new[] { "cup", "hat" }, r.WrongEvidenceIds);
        }

        [Test]
        public void MissingEvidence_IsIncorrect_ButNotAReasoningError()
        {
            var r = AnswerChecker.Check("butler", Key, "butler", new[] { "knife" });
            Assert.IsFalse(r.IsCorrect);
            Assert.AreEqual(0, r.ReasoningErrorCount);
            CollectionAssert.AreEqual(new[] { "letter" }, r.MissingEvidenceIds);
        }

        [Test]
        public void WrongSuspectAndWrongEvidence_Add()
        {
            var r = AnswerChecker.Check("butler", Key, "cook", new[] { "cup" });
            Assert.AreEqual(2, r.ReasoningErrorCount);
            CollectionAssert.AreEqual(new[] { "knife", "letter" }, r.MissingEvidenceIds);
        }

        [Test]
        public void NoSuspect_IsWrong()
        {
            Assert.IsFalse(AnswerChecker.Check("butler", Key, null, Key).SuspectCorrect);
            Assert.IsFalse(AnswerChecker.Check("butler", Key, "  ", Key).SuspectCorrect);
        }

        [Test]
        public void IdsAreTrimmed_DuplicatesIgnored_CaseSensitive()
        {
            var r = AnswerChecker.Check("butler", Key, " butler ", new[] { "knife", " knife", "letter" });
            Assert.IsTrue(r.IsCorrect);
            Assert.IsFalse(AnswerChecker.Check("butler", Key, "Butler", Key).SuspectCorrect);
        }

        [Test]
        public void NullEvidenceLists_AreEmpty()
        {
            var r = AnswerChecker.Check("s", null, "s", null);
            Assert.IsTrue(r.IsCorrect);
        }
    }

    public class CaseStateTests
    {
        [Test]
        public void IgnoresChangesWhenNotRunning()
        {
            var s = new CaseState();
            Assert.IsFalse(s.MarkClueFound("a"));
            Assert.IsFalse(s.RecordContamination("z", BodyPart.Head));
            Assert.AreEqual(0, s.ContaminationErrors);
        }

        [Test]
        public void ClueFound_OnlyOnce_TaggingImpliesFound()
        {
            var s = new CaseState();
            s.Begin();
            int found = 0;
            s.ClueFound += _ => found++;
            Assert.IsTrue(s.MarkClueFound("a"));
            Assert.IsFalse(s.MarkClueFound("a"));
            Assert.IsTrue(s.MarkClueTagged("b"));
            Assert.AreEqual(2, found);
            Assert.IsTrue(s.IsClueFound("b"));
            Assert.IsTrue(s.UntagClue("b"));
            Assert.IsFalse(s.IsClueTagged("b"));
        }

        [Test]
        public void Submission_AddsReasoningErrors_AndEndMarksCompleted()
        {
            var s = new CaseState();
            s.Begin();
            var result = AnswerChecker.Check("x", new[] { "k" }, "y", new[] { "k", "w" });
            Assert.IsTrue(s.RecordSubmission("y", new[] { "k", "w" }, result));
            Assert.AreEqual(2, s.ReasoningErrors);
            Assert.IsTrue(s.End(SessionEndReason.Submitted, 42));
            Assert.IsTrue(s.Completed);
            Assert.AreEqual(42, s.ElapsedSeconds);
            Assert.IsFalse(s.End(SessionEndReason.TimeCap, 50)); // second end ignored
        }

        [Test]
        public void TimeCap_IsNotCompleted()
        {
            var s = new CaseState();
            s.Begin();
            s.End(SessionEndReason.TimeCap, 900);
            Assert.IsFalse(s.Completed);
            Assert.AreEqual(SessionPhase.Ended, s.Phase);
        }

        [Test]
        public void Begin_ClearsPreviousRun()
        {
            var s = new CaseState();
            s.Begin();
            s.MarkClueFound("a");
            s.RecordContamination("z", BodyPart.Head);
            s.End(SessionEndReason.Aborted, 1);
            s.Begin();
            Assert.AreEqual(0, s.FoundClues.Count);
            Assert.AreEqual(0, s.ContaminationErrors);
            Assert.IsNull(s.EndReason);
        }
    }

    public class EvidenceSelectionTests
    {
        [Test]
        public void ToggleAndSubmitRules()
        {
            var sel = new EvidenceSelection();
            Assert.IsFalse(sel.CanSubmit(1));
            sel.SelectSuspect("s1");
            Assert.IsFalse(sel.CanSubmit(1));
            Assert.IsTrue(sel.ToggleEvidence("e1"));
            Assert.IsTrue(sel.CanSubmit(1));
            Assert.IsFalse(sel.ToggleEvidence("e1"));
            Assert.IsFalse(sel.CanSubmit(1));
            Assert.IsTrue(sel.CanSubmit(0));
            sel.Clear();
            Assert.IsNull(sel.SuspectId);
        }
    }
}
