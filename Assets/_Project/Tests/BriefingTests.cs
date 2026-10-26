using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class BriefingScriptTests
    {
        private static string RealCsv() =>
            File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Audio", "Briefing", "briefing_lines.csv"));

        [Test]
        public void ParsesQuotedFieldsWithCommasAndQuotes()
        {
            var lines = BriefingScript.Parse("order,file,case,condition,text\n1,a_01,*,*,\"Hello, \"\"detective\"\".\"\r\n2,b_01,X,A,Plain\n");
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("Hello, \"detective\".", lines[0].Text);
            Assert.AreEqual("b_01", lines[1].File);
            Assert.AreEqual("X", lines[1].Case);
        }

        [Test]
        public void RealScript_SameStructureForBothCases_RightWorkLine()
        {
            var all = BriefingScript.Parse(RealCsv());
            var xa = BriefingScript.For(all, "X", "A");
            var yb = BriefingScript.For(all, "Y", "B");
            Assert.AreEqual(xa.Count, yb.Count, "both cases/conditions have the same number of lines");
            CollectionAssert.AreEqual(xa.Select(l => l.Order).ToList(), yb.Select(l => l.Order).ToList());
            Assert.IsTrue(xa.Any(l => l.File == "work_solo") && xa.All(l => l.File != "work_team"));
            Assert.IsTrue(yb.Any(l => l.File == "work_team") && yb.All(l => l.File != "work_solo"));
            Assert.IsTrue(xa.All(l => !l.File.StartsWith("Y_")) && yb.All(l => !l.File.StartsWith("X_")));
            StringAssert.Contains("award", string.Join(" ", xa.Select(l => l.Text)));
            StringAssert.Contains("Datuk", string.Join(" ", yb.Select(l => l.Text)));
            Assert.AreEqual("outro_01", xa.Last().File);
            foreach (var l in all) Assert.IsTrue(DataTransfer.IsSafeName(l.File), l.File);
        }

        [Test]
        public void CueColumn_MarksTheDisplayLinesOfCaseX()
        {
            var all = BriefingScript.Parse(RealCsv());
            var x = BriefingScript.For(all, "X", "A");
            var y = BriefingScript.For(all, "Y", "A");
            CollectionAssert.AreEqual(new[] { "X_story_01", "X_story_02" }, x.Where(l => l.Cue == "display").Select(l => l.File).ToArray());
            Assert.IsTrue(y.All(l => l.Cue == ""));
            var parsed = BriefingScript.Parse("order,file,case,condition,text,cue\n1,a,*,*,\"Hi\",display\n2,b,*,*,\"Yo\"\n");
            Assert.AreEqual("display", parsed[0].Cue);
            Assert.AreEqual("", parsed[1].Cue);
        }

        [Test]
        public void EstimateForLinesWithoutAudio()
        {
            Assert.AreEqual(1.5f, BriefingScript.EstimateSeconds(""), 1e-4f);
            Assert.AreEqual(10f / 2.6f, BriefingScript.EstimateSeconds("one two three four five six seven eight nine ten"), 1e-3f);
        }
    }

    public class BriefingPlaybackTests
    {
        [Test]
        public void LinesFollowEachOtherWithPauses_ThenFinished()
        {
            var p = new BriefingPlayback(new[] { 2f, 1f }, 0.5f);
            p.Begin(10.0);
            Assert.IsTrue(p.Tick(10.1));              // line 0 starts
            Assert.AreEqual(0, p.CurrentLine);
            Assert.IsFalse(p.Tick(11.9));
            Assert.IsFalse(p.Tick(12.2));             // pause
            Assert.AreEqual(-1, p.CurrentLine);
            Assert.IsTrue(p.Tick(12.6));              // line 1
            Assert.AreEqual(1, p.CurrentLine);
            Assert.IsFalse(p.IsFinished);
            p.Tick(13.7);                             // pause after the last line
            Assert.IsFalse(p.IsFinished);
            p.Tick(14.1);
            Assert.IsTrue(p.IsFinished);
        }

        [Test]
        public void ReplayStartsOver()
        {
            var p = new BriefingPlayback(new[] { 1f }, 0.2f);
            p.Begin(0.0);
            p.Tick(5.0);
            Assert.IsTrue(p.IsFinished);
            p.Begin(5.0);
            Assert.IsFalse(p.IsFinished);
            Assert.IsTrue(p.Tick(5.1));
            Assert.AreEqual(0, p.CurrentLine);
        }
    }

    public class BriefingCardAndMessageTests
    {
        [Test]
        public void CardFollowsTheBriefing()
        {
            var begin = ParticipantCardModel.Compute(true, SessionPhase.NotStarted, null, false, "T", "story", BriefingStage.NotStarted, false);
            Assert.IsTrue(begin.Visible && begin.ShowBegin && !begin.ShowStart && !begin.ShowReplay);

            Assert.IsFalse(ParticipantCardModel.Compute(true, SessionPhase.NotStarted, null, false, "T", "story", BriefingStage.Playing, false).Visible,
                "card out of the way while the detective talks");

            var waiting = ParticipantCardModel.Compute(true, SessionPhase.NotStarted, null, true, "T", "story", BriefingStage.Finished, false);
            Assert.IsTrue(waiting.Visible && !waiting.ShowStart && waiting.ShowReplay);
            StringAssert.Contains("partner", waiting.Body);

            var ready = ParticipantCardModel.Compute(true, SessionPhase.NotStarted, null, false, "T", "story", BriefingStage.Finished, true);
            Assert.IsTrue(ready.ShowStart && ready.ShowReplay && !ready.ShowBegin);
            StringAssert.DoesNotContain("story", ready.Body);   // the detective told the story
        }

        [Test]
        public void BriefingDoneMessageRoundTrips()
        {
            Assert.IsTrue(TeamMessage.TryParse(TeamMessage.BriefingDone("P02"), out var m));
            Assert.AreEqual(TeamMessageKind.BriefingDone, m.Kind);
            Assert.AreEqual("P02", m.By);
            Assert.IsFalse(TeamMessage.TryParse("BriefingDone", out _));
        }
    }
}
