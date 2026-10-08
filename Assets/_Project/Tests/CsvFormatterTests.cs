using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class CsvFormatterTests
    {
        private static ExperimentConfig Config() => new ExperimentConfig
        {
            pairCode = "G03", participantId = "P05", condition = StudyCondition.B, caseId = CaseId.Y, timeCapMinutes = 15f,
        };

        private static LogRow FullRow() => new LogRow
        {
            SessionTime = 12.3456,
            UtcTime = new DateTime(2026, 10, 1, 14, 5, 9, 123, DateTimeKind.Utc),
            RowType = LogRow.TypeSample,
            FrameDeltaMs = 13.889f,
            Pose = new PoseSample
            {
                HasHead = true, HeadPosition = new Vector3(1.5f, 1.7f, -0.25f), HeadRotation = new Quaternion(0f, 0f, 0f, 1f),
                HasLeftHand = true, LeftHandPosition = new Vector3(1f, 1f, 1f),
                HasRightHand = true, RightHandPosition = new Vector3(-1f, 0.5f, 2f),
            },
        };

        private static string[] Split(string line) => line.Split(',');

        [Test]
        public void Header_MatchesColumnCount()
        {
            Assert.AreEqual(CsvFormatter.Columns.Length, Split(CsvFormatter.Header).Length);
            StringAssert.StartsWith("session_time_s,utc_time,row_type,event,detail,", CsvFormatter.Header);
        }

        [Test]
        public void SampleRow_HasSameColumnCountAsHeader()
        {
            string line = CsvFormatter.FormatRow(FullRow(), Config());
            Assert.AreEqual(CsvFormatter.Columns.Length, Split(line).Length, line);
        }

        [Test]
        public void SampleRow_ExactFormat()
        {
            string line = CsvFormatter.FormatRow(FullRow(), Config());
            Assert.AreEqual(
                "12.346,2026-10-01T14:05:09.123Z,sample,,,G03,P05,B,Y," +
                "1.5000,1.7000,-0.2500," +
                "0.00000,0.00000,0.00000,1.00000," +
                "0.00,0.00," +
                "1.0000,1.0000,1.0000," +
                "-1.0000,0.5000,2.0000," +
                "13.89",
                line);
        }

        [Test]
        public void MissingHandsAndHead_AreEmptyCells_NotZeros()
        {
            var row = FullRow();
            row.Pose = new PoseSample();
            string line = CsvFormatter.FormatRow(row, Config());
            var cells = Split(line);
            Assert.AreEqual(CsvFormatter.Columns.Length, cells.Length);
            int headX = Array.IndexOf(CsvFormatter.Columns, "head_x");
            int rhandZ = Array.IndexOf(CsvFormatter.Columns, "rhand_z");
            for (int i = headX; i <= rhandZ; i++) Assert.AreEqual("", cells[i], CsvFormatter.Columns[i]);
        }

        [Test]
        public void EventRow_WritesNameAndDetail()
        {
            var row = FullRow();
            row.RowType = LogRow.TypeEvent;
            row.EventName = LogEvents.ClueFound;
            row.EventDetail = "X_knife";
            var cells = Split(CsvFormatter.FormatRow(row, Config()));
            Assert.AreEqual("event", cells[2]);
            Assert.AreEqual("clue_found", cells[3]);
            Assert.AreEqual("X_knife", cells[4]);
        }

        [Test]
        public void UsesInvariantCulture_EvenOnCommaDecimalLocale()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string line = CsvFormatter.FormatRow(FullRow(), Config());
                Assert.AreEqual(CsvFormatter.Columns.Length, Split(line).Length);
                StringAssert.StartsWith("12.346,", line);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = old;
            }
        }

        [TestCase("plain", "plain")]
        [TestCase("a,b", "\"a,b\"")]
        [TestCase("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [TestCase("line1\nline2", "\"line1\nline2\"")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void Escape_FollowsRfc4180(string input, string expected)
        {
            Assert.AreEqual(expected, CsvFormatter.Escape(input));
        }

        [Test]
        public void DetailWithComma_IsQuoted()
        {
            var row = FullRow();
            row.RowType = LogRow.TypeEvent;
            row.EventDetail = "a,b";
            StringAssert.Contains(",\"a,b\",", CsvFormatter.FormatRow(row, Config()));
        }

        [Test]
        public void ReusedStringBuilder_GivesSameResult()
        {
            var sb = new System.Text.StringBuilder();
            string a = CsvFormatter.FormatRow(FullRow(), Config(), sb);
            string b = CsvFormatter.FormatRow(FullRow(), Config(), sb);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void HeadAngles_Identity_LooksAlongPlusZ()
        {
            CsvFormatter.HeadAngles(Quaternion.identity, out float yaw, out float pitch);
            Assert.AreEqual(0f, yaw, 1e-4f);
            Assert.AreEqual(0f, pitch, 1e-4f);
        }

        [Test]
        public void HeadAngles_Yaw90_LooksAlongPlusX()
        {
            // Rotation of +90 deg around Y: (0, sin45, 0, cos45)
            float s = Mathf.Sqrt(0.5f);
            CsvFormatter.HeadAngles(new Quaternion(0f, s, 0f, s), out float yaw, out float pitch);
            Assert.AreEqual(90f, yaw, 1e-3f);
            Assert.AreEqual(0f, pitch, 1e-3f);
        }

        [Test]
        public void HeadAngles_PitchDown_IsNegative()
        {
            // +30 deg around X tilts forward down in Unity (left-handed).
            float half = 15f * Mathf.Deg2Rad;
            CsvFormatter.HeadAngles(new Quaternion(Mathf.Sin(half), 0f, 0f, Mathf.Cos(half)), out _, out float pitch);
            Assert.AreEqual(-30f, pitch, 1e-3f);
        }

        [Test]
        public void Summary_MatchesHeaderAndValues()
        {
            var state = new CaseState();
            state.Begin();
            state.MarkClueFound("c1");
            state.MarkClueTagged("c2");
            state.RecordContamination("z1", BodyPart.Head);
            state.RecordContamination("z1", BodyPart.LeftHand);
            var evidence = new[] { "c2", "c1" };
            var result = AnswerChecker.Check("s1", new[] { "c2" }, "s1", evidence);
            state.RecordSubmission("s1", evidence, result);
            state.End(SessionEndReason.Submitted, 100.5);

            string line = CsvFormatter.FormatSummary(state, Config(), "Case Y", new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc));
            var cells = Split(line);
            Assert.AreEqual(CsvFormatter.SummaryColumns.Length, cells.Length, line);
            Assert.AreEqual(
                "G03,P05,B,Y,Case Y,2026-10-01T14:00:00.000Z,Submitted,1,0,100.500,900,2,1,2,1,s1,1,c1;c2,c1,,z1=2,100.500,0.000,0",
                line);
        }

        [Test]
        public void Summary_Snapshot_UsesOverrideAndPauseColumns()
        {
            var state = new CaseState();
            state.Begin();
            state.SetElapsed(60);
            state.SetPauseStats(12.25, 2);

            string line = CsvFormatter.FormatSummary(state, Config(), "Case Y",
                new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc), SessionMeta.InterruptedReason);
            var cells = Split(line);
            Assert.AreEqual(CsvFormatter.SummaryColumns.Length, cells.Length, line);
            Assert.AreEqual("Interrupted", cells[Array.IndexOf(CsvFormatter.SummaryColumns, "end_reason")]);
            Assert.AreEqual("0", cells[Array.IndexOf(CsvFormatter.SummaryColumns, "completed")]);
            Assert.AreEqual("60.000", cells[Array.IndexOf(CsvFormatter.SummaryColumns, "elapsed_s")]);
            Assert.AreEqual("47.750", cells[Array.IndexOf(CsvFormatter.SummaryColumns, "active_s")]);
            Assert.AreEqual("12.250", cells[Array.IndexOf(CsvFormatter.SummaryColumns, "paused_s")]);
            Assert.AreEqual("2", cells[Array.IndexOf(CsvFormatter.SummaryColumns, "pause_count")]);
        }
    }
}
