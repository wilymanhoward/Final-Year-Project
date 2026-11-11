using System;
using System.Globalization;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class HandJointCsvTests
    {
        [Test]
        public void HeaderHasTimeAndThreeColumnsPerJointPerHand()
        {
            string[] cols = HandJointCsv.Header.Split(',');
            Assert.AreEqual(2 + 2 * 3 * 25, cols.Length);
            Assert.AreEqual(25, HandJointCsv.JointCount);
            Assert.AreEqual("l_wrist_x", cols[2]);
            Assert.AreEqual("l_index_finger_tip_z", cols[2 + 9 * 3 + 2]);
            Assert.AreEqual("r_wrist_x", cols[2 + 75]);
            Assert.AreEqual("r_pinky_finger_tip_z", cols[cols.Length - 1]);
        }

        [Test]
        public void UntrackedHandIsEmptyCellsAndRowMatchesHeader()
        {
            var left = new Vector3[25];
            var right = new Vector3[25];
            for (int i = 0; i < 25; i++) right[i] = new Vector3(0.1f * i, 1.25f, -0.0425f);
            string row = HandJointCsv.FormatRow(1.5, new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc),
                left, false, right, true, new StringBuilder());
            string[] cells = row.Split(',');
            Assert.AreEqual(HandJointCsv.Header.Split(',').Length, cells.Length);
            Assert.AreEqual("1.500", cells[0]);
            Assert.AreEqual("2026-11-11T12:00:00.000Z", cells[1]);
            for (int i = 2; i < 77; i++) Assert.AreEqual("", cells[i], "left hand untracked");
            Assert.AreEqual("0.000", cells[77]);
            Assert.AreEqual("1.250", cells[78]);
            Assert.AreEqual("-0.043", cells[79], "rounded half away from zero");
            Assert.AreEqual("2.400", cells[77 + 24 * 3]);
        }

        [Test]
        public void MetresMatchInvariantFormattingInAnyCulture()
        {
            CultureInfo old = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");   // decimal comma
            try
            {
                var sb = new StringBuilder();
                foreach (float v in new[] { 0f, 0.001f, -0.001f, 0.0994f, 1.2345f, -1.2346f, 12.5f, -0.5f, 123.456f })
                {
                    sb.Clear();
                    HandJointCsv.AppendMetres(sb, v);
                    double expected = Math.Round((double)v * 1000.0, MidpointRounding.AwayFromZero) / 1000.0;
                    Assert.AreEqual(expected.ToString("F3", CultureInfo.InvariantCulture), sb.ToString(), "value " + v);
                }
                sb.Clear();
                HandJointCsv.AppendMetres(sb, -0.0004f);
                Assert.AreEqual("0.000", sb.ToString(), "no negative zero");
            }
            finally { Thread.CurrentThread.CurrentCulture = old; }
        }
    }
}
