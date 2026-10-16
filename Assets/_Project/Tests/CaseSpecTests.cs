using System;
using NUnit.Framework;

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
            Assert.AreEqual("The Missing Keris", CaseSpecs.Y.Title);
            StringAssert.Contains("21:42", CaseSpecs.X.Clue(ClueSlot.K1).InspectText);
            StringAssert.Contains("20:17", CaseSpecs.Y.Clue(ClueSlot.K1).InspectText);
            Assert.AreEqual("X_kumar", CaseSpecs.X.Clue(ClueSlot.R1).PointsAtSuspectId);
            Assert.AreEqual("Y_sofia", CaseSpecs.Y.Clue(ClueSlot.R3).PointsAtSuspectId);
        }

        [Test]
        public void MeanDistancesAreEqual()
        {
            Assert.AreEqual(CaseSpecValidator.MeanDistance(CaseSpecs.X), CaseSpecValidator.MeanDistance(CaseSpecs.Y), 0.001f);
            Assert.AreEqual(1.22f, CaseSpecValidator.MeanDistance(CaseSpecs.X), 0.01f);
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
                glass.X = -0.95f;   // the first headset test: glass across the whole display front
                glass.Width = 0.9f;
                var errors = string.Join("; ", CaseSpecValidator.Validate(x));
                StringAssert.Contains("K1 can't be reached", errors);
            }
            finally
            {
                glass.X = oldX;
                glass.Width = oldW;
            }
            foreach (var clue in x.Clues) Assert.IsTrue(CaseSpecValidator.IsReachable(x, clue), clue.Slot.ToString());

            // Headset test 2: coffee directly in front of the desk blocked the natural reach to the sign-in sheet (K2).
            var coffee = x.Zones[1];
            float cx = coffee.X, cz = coffee.Z, cw = coffee.Width, cd = coffee.Depth;
            try
            {
                coffee.X = 1.00f; coffee.Z = 0.05f; coffee.Width = 0.5f; coffee.Depth = 0.4f;
                StringAssert.Contains("K2 can't be reached", string.Join("; ", CaseSpecValidator.Validate(x)));
            }
            finally
            {
                coffee.X = cx; coffee.Z = cz; coffee.Width = cw; coffee.Depth = cd;
            }
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
