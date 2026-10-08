using System;
using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class SessionMetaTests
    {
        private static readonly DateTime Start = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);

        private static ExperimentConfig Config() => new ExperimentConfig
        {
            pairCode = "G07", participantId = "P13", condition = StudyCondition.B, caseId = CaseId.Y, timeCapMinutes = 15f,
        };

        private static BuildInfo Build() => new BuildInfo
        {
            BuildGuid = "abc123", AppVersion = "0.1", UnityVersion = "6000.3.25f1", DeviceModel = "Oculus Quest", OperatingSystem = "Android",
        };

        private static PlacementRecord Anchor() =>
            new PlacementRecord(PlacementMethod.Anchor, true, "uuid-1", new Vector3(1f, 0f, 2f), 45f, Start.AddMinutes(-5));

        [Test]
        public void WhileRunning_HasNoEndAndKeepsPlacement()
        {
            var s = new CaseState();
            s.Begin();
            var meta = SessionMetaBuilder.Build(s, Config(), "Case Y", 4, Anchor(), Build(), true, Start, null, "log.csv");

            Assert.AreEqual("Running", meta.phase);
            Assert.AreEqual("", meta.endUtc);
            // A snapshot of a running session is marked Interrupted until the session really ends.
            Assert.AreEqual(SessionMeta.InterruptedReason, meta.endReason);
            Assert.IsFalse(meta.completed);
            Assert.AreEqual("anchor", meta.placementMethod);
            Assert.AreEqual(LogEvents.PlacementLoaded, meta.placementEvent);
            Assert.AreEqual("uuid-1", meta.anchorUuid);
            Assert.AreEqual("CaseRoot", meta.positionFrame);
            Assert.AreEqual("2026-10-08T10:00:00.000Z", meta.startUtc);
            Assert.AreEqual(4, meta.evidenceScoreMax);
            Assert.AreEqual("B", meta.conditionCode);
            Assert.AreEqual("Y", meta.caseCode);
            Assert.AreEqual("G07", meta.config.pairCode);
        }

        [Test]
        public void AfterSubmit_HasFinalSummary()
        {
            var s = new CaseState();
            s.Begin();
            s.MarkClueFound("c1");
            s.MarkClueTagged("c2");
            s.MarkClueTagged("c3");
            s.RecordContamination("z1", BodyPart.Head);
            s.RecordContamination("z1", BodyPart.LeftHand);
            s.RecordContamination("z2", BodyPart.LeftHand);
            var evidence = new[] { "c2", "c3" };
            var result = AnswerChecker.Check("s1", new[] { "c2", "c4" }, "s1", evidence);
            s.RecordSubmission("s1", evidence, result);
            s.SetPauseStats(21.5, 1);
            s.End(SessionEndReason.Submitted, 321.5);

            var meta = SessionMetaBuilder.Build(s, Config(), "Case Y", 2, PlacementRecord.None, Build(), false,
                Start, Start.AddSeconds(321.5), "log.csv");

            Assert.AreEqual("Ended", meta.phase);
            Assert.AreEqual("Submitted", meta.endReason);
            Assert.IsTrue(meta.completed);
            Assert.AreEqual(321.5, meta.elapsedSeconds, 1e-6);
            Assert.AreEqual(300.0, meta.activeSeconds, 1e-6);
            Assert.AreEqual(21.5, meta.pausedSeconds, 1e-6);
            Assert.AreEqual(1, meta.pauseCount);
            Assert.AreEqual(3, meta.cluesFound);
            Assert.AreEqual(2, meta.cluesTagged);
            CollectionAssert.AreEqual(new[] { "c1", "c2", "c3" }, meta.foundClueIds);
            Assert.AreEqual(3, meta.contaminationErrors);
            Assert.AreEqual(1, meta.contaminationHead);
            Assert.AreEqual(2, meta.contaminationLeftHand);
            Assert.AreEqual(0, meta.contaminationRightHand);
            Assert.AreEqual(1, meta.reasoningErrors);
            Assert.IsTrue(meta.submitted);
            Assert.IsTrue(meta.suspectCorrect);
            Assert.IsFalse(meta.answerCorrect);
            Assert.AreEqual(1, meta.evidenceScore);
            Assert.AreEqual(2, meta.evidenceScoreMax);
            Assert.AreEqual(1, meta.wrongEvidenceCount);
            Assert.AreEqual("none", meta.placementMethod);
            Assert.AreEqual("", meta.placementEvent);
            Assert.AreEqual("world", meta.positionFrame);
        }

        [Test]
        public void SerialisesToJson()
        {
            var s = new CaseState();
            s.Begin();
            var meta = SessionMetaBuilder.Build(s, Config(), "Case Y", 4, Anchor(), Build(), true, Start, null, "log.csv");
            string json = JsonUtility.ToJson(meta, true);

            StringAssert.Contains("\"pairCode\": \"G07\"", json);
            StringAssert.Contains("\"buildGuid\": \"abc123\"", json);
            StringAssert.Contains("\"placementMethod\": \"anchor\"", json);
            StringAssert.Contains("\"evidenceScoreMax\": 4", json);
            var back = JsonUtility.FromJson<SessionMeta>(json);
            Assert.AreEqual("P13", back.config.participantId);
            Assert.AreEqual("uuid-1", back.anchorUuid);
        }
    }
}
