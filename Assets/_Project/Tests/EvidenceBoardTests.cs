using System.Collections.Generic;
using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class EvidenceBoardTests
    {
        private static readonly List<SuspectInfo> Suspects = new List<SuspectInfo>
        {
            new SuspectInfo { id = "s1", displayName = "Butler" },
            new SuspectInfo { id = "s2", displayName = "" },
            null,
        };

        private static EvidenceBoardLayout Build(IEnumerable<string> tagged, string suspect, ISet<string> chosen,
            int suspectSlots = 6, int evidenceSlots = 8, int minEvidence = 1)
        {
            var names = new Dictionary<string, string> { { "knife", "Knife" }, { "cup", "a cup" } };
            return EvidenceBoardModel.Build(Suspects, tagged, id => names.TryGetValue(id, out var n) ? n : null,
                suspect, id => chosen.Contains(id), suspectSlots, evidenceSlots, minEvidence);
        }

        [Test]
        public void SuspectsFromCaseWithFallbackNames()
        {
            var l = Build(new string[0], "s2", new HashSet<string>());
            Assert.AreEqual("Butler", l.Suspects[0].Label);
            Assert.AreEqual("s2", l.Suspects[1].Label);
            Assert.IsTrue(l.Suspects[1].Selected);
            Assert.IsFalse(l.Suspects[2].Visible);
            StringAssert.Contains("Tag clues", l.Summary);
            Assert.IsFalse(l.CanSubmit);
        }

        [Test]
        public void SuspectCardsShowRoleAndAlibi()
        {
            Assert.AreEqual("Grace Wong - Studio manager\nalibi", EvidenceBoardModel.SuspectCard("Grace Wong", "Studio manager", "alibi"));
            Assert.AreEqual("Grace Wong", EvidenceBoardModel.SuspectCard("Grace Wong", "", null));
        }

        [Test]
        public void EvidenceIsTaggedCluesSortedByName()
        {
            var l = Build(new[] { "knife", "cup", "zz" }, "s1", new HashSet<string> { "knife" });
            Assert.AreEqual("a cup", l.Evidence[0].Label);
            Assert.AreEqual("Knife", l.Evidence[1].Label);
            Assert.AreEqual("zz", l.Evidence[2].Label);   // no display name -> id
            Assert.IsTrue(l.Evidence[1].Selected);
            Assert.IsFalse(l.Evidence[3].Visible);
            Assert.IsTrue(l.CanSubmit);
            StringAssert.Contains("Butler", l.Summary);
        }

        [Test]
        public void ReportsOverflowAndMinimum()
        {
            var l = Build(new[] { "a", "b", "c" }, "s1", new HashSet<string> { "a" }, evidenceSlots: 2, minEvidence: 2);
            Assert.AreEqual(1, l.HiddenEvidence);
            Assert.IsFalse(l.CanSubmit);
            StringAssert.Contains("don't fit", l.Summary);
            StringAssert.Contains("at least 2", l.Summary);
        }

        // ---- CaseState board selection ----

        [Test]
        public void BoardEvidenceMustBeTagged()
        {
            var s = new CaseState();
            s.Begin();
            Assert.IsFalse(s.SetBoardEvidence("knife", true));
            s.MarkClueTagged("knife");
            Assert.IsTrue(s.SetBoardEvidence("knife", true));
            Assert.IsFalse(s.SetBoardEvidence("knife", true), "no change");
            CollectionAssert.AreEqual(new[] { "knife" }, s.BoardEvidenceIds);
        }

        [Test]
        public void UntaggingRemovesFromBoard()
        {
            var s = new CaseState();
            s.Begin();
            s.MarkClueTagged("knife");
            s.SetBoardEvidence("knife", true);
            var removed = new List<string>();
            s.BoardEvidenceChanged += (id, selected) => { if (!selected) removed.Add(id); };
            s.UntagClue("knife");
            Assert.AreEqual(0, s.BoardEvidenceIds.Count);
            CollectionAssert.AreEqual(new[] { "knife" }, removed);
        }

        [Test]
        public void BoardLocksAfterSubmissionAndClearsOnNewRun()
        {
            var s = new CaseState();
            s.Begin(true, "P01");
            string actor = null;
            s.BoardSuspectSelected += _ => actor = s.LastActorId;
            Assert.IsTrue(s.SetBoardSuspect("s1", "P02"));
            Assert.AreEqual("P02", actor);
            s.RecordSubmission("s1", new string[0], AnswerChecker.Check("s1", new string[0], "s1", new string[0]), "P02");
            Assert.IsFalse(s.SetBoardSuspect("s2", "P01"));
            s.Begin();
            Assert.IsNull(s.BoardSuspectId);
        }

        [Test]
        public void BoardMessagesRoundTrip()
        {
            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.BoardSuspect("s1", "P02"), out var m));
            Assert.AreEqual(TeamMessageKind.BoardSuspect, m.Kind);
            Assert.AreEqual("s1", m.SuspectId);
            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.ReqBoardSuspect(null, "P01"), out m));
            Assert.IsNull(m.SuspectId);
            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.ReqBoardEvidence("knife", true, "P01"), out m));
            Assert.AreEqual("knife", m.ClueId);
            Assert.IsTrue(m.Tagged);
            Assert.IsFalse(TeamMessage.TryParse("BoardEvidence|knife|x|P01", out _));
        }
    }
}
