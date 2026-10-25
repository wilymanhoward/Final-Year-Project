using System;
using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class CaseSpecTests
    {
        [Test]
        public void BothCasesPassTheValidator()
        {
            var errors = CaseSpecValidator.ValidatePair(CaseSpecs.X, CaseSpecs.Y);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void ContentMatchesTheDocs()
        {
            Assert.AreEqual("Grace Wong", CaseSpecs.X.Culprit.Name);
            Assert.AreEqual("Farid Rahman", CaseSpecs.Y.Culprit.Name);
            Assert.AreEqual("The Missing Design Award", CaseSpecs.X.Title);
            Assert.AreEqual("Death in the Melaka Room", CaseSpecs.Y.Title);
            StringAssert.Contains("21:42", CaseSpecs.X.Clue(ClueSlot.K1).InspectText);
            StringAssert.Contains("20:15", CaseSpecs.Y.Clue(ClueSlot.K1).InspectText);
            Assert.AreEqual("X_kumar", CaseSpecs.X.Clue(ClueSlot.R1).PointsAtSuspectId);
            Assert.AreEqual("Y_sofia", CaseSpecs.Y.Clue(ClueSlot.R3).PointsAtSuspectId);
        }

        [Test]
        public void MeanDistancesAreEqual()
        {
            Assert.AreEqual(CaseSpecValidator.MeanDistance(CaseSpecs.X), CaseSpecValidator.MeanDistance(CaseSpecs.Y), 0.001f);
            Assert.AreEqual(2.10f, CaseSpecValidator.MeanDistance(CaseSpecs.X), 0.01f);
        }

        [Test]
        public void GradingFollowsTheDocs()
        {
            var x = CaseSpecs.X;
            string[] keys = { "X_K1", "X_K2", "X_K3", "X_K4" };
            var perfect = AnswerChecker.Check(x.Culprit.Id, keys, "X_grace", keys);
            Assert.IsTrue(perfect.IsCorrect);
            Assert.IsTrue(perfect.IsPerfect);

            var culpritWithHerring = AnswerChecker.Check(x.Culprit.Id, keys, "X_grace", new[] { "X_K1", "X_K2", "X_K3", "X_K4", "X_R3" });
            Assert.IsTrue(culpritWithHerring.IsCorrect, "answer_correct = culprit only");
            Assert.IsFalse(culpritWithHerring.IsPerfect);
            Assert.AreEqual(1, culpritWithHerring.ReasoningErrorCount);
            Assert.AreEqual(4, culpritWithHerring.CorrectEvidenceIds.Count);
            Assert.AreEqual(4, perfect.CorrectEvidenceIds.Count);

            var withHerrings = AnswerChecker.Check(x.Culprit.Id, keys, "X_lena", new[] { "X_K1", "X_K2", "X_R1", "X_R2" });
            Assert.IsFalse(withHerrings.IsCorrect);
            Assert.IsFalse(withHerrings.IsPerfect);
            Assert.AreEqual(3, withHerrings.ReasoningErrorCount);   // wrong suspect + 2 red herrings
            Assert.AreEqual(2, withHerrings.CorrectEvidenceIds.Count);   // evidence score 2 of 4
        }

        [Test]
        public void ValidatorCatchesBrokenSpecs()
        {
            var bad = CaseSpecs.X;
            var clue = bad.Clue(ClueSlot.R2);
            float oldX = clue.X, oldZ = clue.Z;
            var oldBand = bad.Clue(ClueSlot.K1).Height;
            try
            {
                clue.X = bad.Zones[1].X;   // move R2 into the spilled coffee
                clue.Z = bad.Zones[1].Z;
                bad.Clue(ClueSlot.K1).Height = HeightBand.Floor;
                var errors = CaseSpecValidator.ValidatePair(bad, CaseSpecs.Y);
                string all = string.Join("\n", errors);
                StringAssert.Contains("R2 is inside", all);
                StringAssert.Contains("K1 is at Floor", all);
                StringAssert.Contains("R2: Y position isn't X mirrored", all);
            }
            finally
            {
                clue.X = oldX;
                clue.Z = oldZ;
                bad.Clue(ClueSlot.K1).Height = oldBand;
            }
        }

        [Test]
        public void ReachRuleCatchesAClueBehindAZone()
        {
            var x = CaseSpecs.X;
            var glass = x.Zones[0];
            float oldX = glass.X, oldW = glass.Width;
            try
            {
                glass.X = x.Prop("display").X;   // the first headset test: glass across the whole display front
                glass.Width = 0.9f;
                var errors = string.Join("; ", CaseSpecValidator.Validate(x));
                StringAssert.Contains("K3 can't be reached", errors);   // K1 is now read from the case's side
            }
            finally
            {
                glass.X = oldX;
                glass.Width = oldW;
            }
            // A zone on K1's reading side (the keypad on the display case's side glass) blocks it.
            var k1 = x.Clue(ClueSlot.K1);
            float gz = glass.Z, gd = glass.Depth;
            try
            {
                glass.X = k1.X + 0.30f; glass.Z = k1.Z; glass.Width = 0.3f; glass.Depth = 0.6f;
                StringAssert.Contains("K1 can't be reached", string.Join("; ", CaseSpecValidator.Validate(x)));
            }
            finally
            {
                glass.X = oldX; glass.Z = gz; glass.Width = oldW; glass.Depth = gd;
            }
            foreach (var clue in x.Clues) Assert.IsTrue(CaseSpecValidator.IsReachable(x, clue), clue.Slot.ToString());

            // Headset test 2: coffee directly in front of the desk blocked the natural reach to the sign-in sheet (K2).
            var coffee = x.Zones[1];
            float cx = coffee.X, cz = coffee.Z, cw = coffee.Width, cd = coffee.Depth;
            try
            {
                var desk = x.Prop("desk");   // the second headset test: coffee right in front of the desk
                Vector3 front = Quaternion.Euler(0f, desk.Yaw, 0f) * Vector3.forward;
                coffee.X = desk.X + front.x * 0.45f; coffee.Z = desk.Z + front.z * 0.45f; coffee.Width = 0.4f; coffee.Depth = 0.5f;
                StringAssert.Contains("K2 can't be reached", string.Join("; ", CaseSpecValidator.Validate(x)));
            }
            finally
            {
                coffee.X = cx; coffee.Z = cz; coffee.Width = cw; coffee.Depth = cd;
            }
        }

        [Test]
        public void MayaVarnishDecalUsesTheZonePose()
        {
            // tools/maya/build_props.py cuts the Case Y varnish decal from the floorboards at this pose; if the zone
            // moves in CaseSpecs, update Y_Z3_POSE there and rerun mayapy, or the decal won't line up with the boards.
            string path = System.IO.Path.Combine(Application.dataPath, "..", "tools", "maya", "build_props.py");
            var m = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(path),
                @"Y_Z3_POSE = \(([-\d.]+), ([-\d.]+), ([-\d.]+)\)");
            Assert.IsTrue(m.Success, "Y_Z3_POSE not found in build_props.py");
            var zone = CaseSpecs.Y.Zones[2];
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Assert.AreEqual(zone.X, float.Parse(m.Groups[1].Value, inv), 1e-3f);
            Assert.AreEqual(zone.Z, float.Parse(m.Groups[2].Value, inv), 1e-3f);
            Assert.AreEqual(zone.Yaw, float.Parse(m.Groups[3].Value, inv), 1e-3f);
        }

        [Test]
        public void ZoneTestUsesYaw()
        {
            var strip = new ZoneSpec { X = 0f, Z = 0f, Width = 0.2f, Depth = 2f, Yaw = 90f };   // turned to lie along x
            Assert.IsTrue(CaseSpecValidator.InZone(0.9f, 0f, strip));
            Assert.IsFalse(CaseSpecValidator.InZone(0f, 0.9f, strip));
        }

        [Test]
        public void MirrorFlipsTheLayout()
        {
            foreach (ClueSlot slot in Enum.GetValues(typeof(ClueSlot)))
            {
                Assert.AreEqual(-CaseSpecs.X.Clue(slot).X, CaseSpecs.Y.Clue(slot).X, 1e-4f, slot.ToString());
                Assert.AreEqual(CaseSpecs.X.Clue(slot).Z, CaseSpecs.Y.Clue(slot).Z, 1e-4f, slot.ToString());
            }
            Assert.Less(CaseSpecs.X.Prop("door").X * CaseSpecs.Y.Prop("door").X, 0f, "door switches sides");
        }
    }
}
