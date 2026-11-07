using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class RevealScriptTests
    {
        [Test]
        public void CaseX_BeatsMatchTheRecordedLines_AndStayInTheRoom()
        {
            var lines = BriefingScript.Parse(File.ReadAllText(
                Path.Combine(Application.dataPath, "_Project", "Audio", "Briefing", "briefing_lines.csv")));
            var beats = RevealScript.For(CaseId.X);
            Assert.AreEqual(5, beats.Count);
            var clueIds = new HashSet<string>();
            foreach (var c in CaseSpecs.X.Clues) clueIds.Add(c.Id);
            foreach (var beat in beats)
            {
                Assert.IsTrue(lines.Exists(l => l.File == beat.Line && l.Case == "reveal_X"), beat.Line + " in briefing_lines.csv");
                foreach (var track in new[] { beat.Culprit, beat.Victim })
                {
                    float last = -1f;
                    foreach (var k in track)
                    {
                        Assert.That(k.T, Is.InRange(0f, 1f), beat.Line);
                        Assert.GreaterOrEqual(k.T, last, beat.Line + " keys in time order");
                        last = k.T;
                        Assert.LessOrEqual(Mathf.Abs(k.X), PlayArea.HalfSize, beat.Line);
                        Assert.LessOrEqual(Mathf.Abs(k.Z), PlayArea.HalfSize, beat.Line);
                    }
                }
                foreach (var g in beat.Glows) Assert.IsTrue(clueIds.Contains(g.ClueId), g.ClueId);
            }
            Assert.AreEqual("21:35", beats[0].TimeLabel);
            Assert.AreEqual("21:42", beats[3].TimeLabel);
            // The victim ends where the taped outline is (feet by the chair, lying towards -x).
            RevealScript.Evaluate(beats[4].Victim, 1f, out float vx, out float vz, out float yaw, out GhostPose pose);
            Assert.AreEqual(GhostPose.Lying, pose);
            // He stands where the measured fall puts his hips on the outline (head towards -x).
            Assert.AreEqual(1.05f, vx, 0.01f);
            Assert.AreEqual(0.83f, vz, 0.01f);
            Assert.AreEqual(101.0f, yaw, 0.5f);
            // Poisoned: he sways and collapses, no knock-out / fight motion.
            CollectionAssert.Contains(beats[2].Victim.ConvertAll(k => k.Pose), GhostPose.Collapse);
            CollectionAssert.Contains(beats[2].Victim.ConvertAll(k => k.Pose), GhostPose.Dizzy);
        }

        [Test]
        public void CaseY_FiveBeats_DatukFallsWhereTheOutlineIs_FaridRunsToTheDoor()
        {
            var beats = RevealScript.For(CaseId.Y);
            Assert.AreEqual(5, beats.Count);
            CollectionAssert.AreEqual(new[] { "Y_reveal_01", "Y_reveal_02", "Y_reveal_03", "Y_reveal_04", "Y_reveal_05" },
                beats.ConvertAll(b => b.Line));
            RevealScript.Evaluate(beats[3].Victim, 1f, out _, out float vz, out float yaw, out GhostPose pose);
            Assert.AreEqual(GhostPose.FallBack, pose);
            Assert.AreEqual(1.22f, vz, 0.01f);
            Assert.AreEqual(108.1f, yaw, 0.5f, "turned so the measured back fall puts his head towards -x like the outline");
            RevealScript.Evaluate(beats[4].Culprit, 0.6f, out _, out _, out _, out pose);
            Assert.AreEqual(GhostPose.Run, pose);
            RevealScript.Evaluate(beats[0].Victim, 0f, out _, out _, out _, out pose);
            Assert.AreEqual(GhostPose.Hidden, pose, "the Datuk isn't there yet when Farid takes the key");
        }

        [Test]
        public void Evaluate_WalksBetweenSpots_HoldsPosesInPlace()
        {
            var keys = new List<GhostKey>
            {
                new GhostKey(0f, 0f, 0f, 0f, GhostPose.Stand),
                new GhostKey(0.5f, 0f, 2f, 90f, GhostPose.Carry),
                new GhostKey(1f, 0f, 2f, 180f, GhostPose.Carry),
            };
            RevealScript.Evaluate(keys, 0.25f, out float x, out float z, out float yaw, out GhostPose pose);
            Assert.AreEqual(GhostPose.WalkCarry, pose, "moving towards a carry key");
            Assert.AreEqual(1f, z, 1e-4f, "eased half way");
            Assert.AreEqual(0f, yaw, 1e-3f, "faces the way it walks");
            RevealScript.Evaluate(keys, 0.75f, out _, out z, out yaw, out pose);
            Assert.AreEqual(GhostPose.Carry, pose);
            Assert.AreEqual(2f, z, 1e-4f);
            Assert.AreEqual(135f, yaw, 1e-3f, "turns in place");
            RevealScript.Evaluate(keys, 2f, out _, out _, out yaw, out pose);
            Assert.AreEqual(180f, yaw, 1e-3f);
            Assert.IsFalse(RevealScript.Evaluate(new List<GhostKey>(), 0.5f, out _, out _, out _, out _));

        }
    }
}
