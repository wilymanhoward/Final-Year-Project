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
            Assert.AreEqual("Last Coffee at Kite", CaseSpecs.X.Title);
            StringAssert.Contains("poisoned", CaseSpecs.X.Briefing);
            StringAssert.Contains("BEST MANAGER", CaseSpecs.X.Clue(ClueSlot.K3).InspectText);
            Assert.AreEqual("Death in the Melaka Room", CaseSpecs.Y.Title);
            StringAssert.Contains("21:42", CaseSpecs.X.Clue(ClueSlot.K1).InspectText);
            StringAssert.Contains("8:17", CaseSpecs.Y.Clue(ClueSlot.K1).InspectText);
            StringAssert.Contains("F.R.", CaseSpecs.Y.Clue(ClueSlot.K2).InspectText);
            StringAssert.Contains("scarf", CaseSpecs.Y.Clue(ClueSlot.K3).InspectText);
            Assert.AreEqual("X_kumar", CaseSpecs.X.Clue(ClueSlot.R1).PointsAtSuspectId);
            Assert.AreEqual("Y_sofia", CaseSpecs.Y.Clue(ClueSlot.R3).PointsAtSuspectId);
        }

        [Test]
        public void MeanDistancesAreEqual()
        {
            Assert.AreEqual(CaseSpecValidator.MeanDistance(CaseSpecs.X), CaseSpecValidator.MeanDistance(CaseSpecs.Y),
                CaseSpecValidator.MaxMeanDistanceDifference);
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
        public void ZoneTestUsesYaw()
        {
            var strip = new ZoneSpec { X = 0f, Z = 0f, Width = 0.2f, Depth = 2f, Yaw = 90f };   // turned to lie along x
            Assert.IsTrue(CaseSpecValidator.InZone(0.9f, 0f, strip));
            Assert.IsFalse(CaseSpecValidator.InZone(0f, 0.9f, strip));
        }

        [Test]
        public void CaseYMurderSceneMakesSense()
        {
            var y = CaseSpecs.Y;
            float headX = CaseSpecs.YBodyX + CaseSpecs.YBodyHeadOffsetX;
            Assert.IsTrue(CaseSpecValidator.InZone(headX, CaseSpecs.YBodyZ, y.Zones[0]), "the blood pool is under the head");
            var scrap = y.Clue(ClueSlot.K3);
            Assert.Less(Mathf.Abs(scrap.X - CaseSpecs.YBodyX), 0.8f, "the torn scarf scrap lies beside the body");
            Assert.Less(Mathf.Abs(scrap.Z - CaseSpecs.YBodyZ), 0.6f);
            StringAssert.DoesNotContain("keris", scrap.ObjectName.ToLowerInvariant(), "the killer took the weapon away");
            var trail = y.Zones[1];
            var door = y.Prop("door");
            // The trail runs from the pool towards the entrance: its far end is nearer the door than its near end.
            float a = Mathf.Deg2Rad * trail.Yaw;
            var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            var far = new Vector2(trail.X, trail.Z) - dir * (trail.Depth / 2f);
            var near = new Vector2(trail.X, trail.Z) + dir * (trail.Depth / 2f);
            var d = new Vector2(door.X, door.Z);
            Assert.Less((far - d).magnitude, (near - d).magnitude);
            Assert.Less((near - new Vector2(CaseSpecs.YBodyX + CaseSpecs.YBodyHeadOffsetX, CaseSpecs.YBodyZ)).magnitude, 0.8f, "the trail starts at the pool");
        }

        [Test]
        public void CaseXBodyAndChair_ClearOfTheDetectiveCluesAndTheSignInSheet()
        {
            var x = CaseSpecs.X;
            Assert.AreEqual(180f, CaseSpecs.XBodyYaw, "extents below assume the outline is turned 180 (head at -x)");
            // Outline extents from tools/maya/build_props.py body_tape_side (head at +x), turned 180 degrees.
            float minX = CaseSpecs.XBodyX - 0.80f, maxX = CaseSpecs.XBodyX + 0.78f;
            float minZ = CaseSpecs.XBodyZ - 0.42f, maxZ = CaseSpecs.XBodyZ + 0.20f;
            float r = CaseSpecs.XChairRadius;
            var display = x.Prop("display");
            var k2 = x.Clue(ClueSlot.K2);
            var k2Spot = new Vector2(k2.X - 0.45f, k2.Z);   // the sign-in sheet is read from the room side (-x)
            var detective = new Vector2(display.X + 2f, display.Z);   // DetectiveBriefing.DisplaySideOffset
            var chair = new Vector2(CaseSpecs.XChairX, CaseSpecs.XChairZ);

            Assert.Greater(detective.y - maxZ, 0.3f, "the detective doesn't stand on the body");
            Assert.Greater((detective - chair).magnitude - r, 0.3f, "nor in the chair");
            Assert.Greater(minZ - k2Spot.y, 0.3f, "room to stand at the sign-in sheet");
            Assert.Greater((k2Spot - chair).magnitude - r, 0.3f);
            foreach (var c in x.Clues)
            {
                Assert.IsFalse(c.X > minX && c.X < maxX && c.Z > minZ && c.Z < maxZ, c.Id + " under the body");
                Assert.Greater((new Vector2(c.X, c.Z) - chair).magnitude, r, c.Id + " under the chair");
            }
            var coffee = x.Zones[1];
            Assert.Greater(coffee.X - coffee.Width / 2f, chair.x + r - 0.01f, "the chair stands beside the spilled coffee, not in it");
            Assert.Less(chair.x - r - maxX, 0.1f, "his legs lie by the chair he slid off");
        }

        [Test]
        public void VerdictSpotIsClearInBothCases()
        {
            var spot = new Vector2(CaseSpecs.VerdictX, CaseSpecs.VerdictZ);
            foreach (var spec in new[] { CaseSpecs.X, CaseSpecs.Y })
            {
                foreach (var z in spec.Zones)   // 0.4 m clear of every zone
                    Assert.IsFalse(CaseSpecValidator.InZone(spot.x, spot.y, z, 0.4f), spec.Case + " " + z.Id);
                foreach (var p in spec.Props)
                {
                    float reach = Mathf.Sqrt(p.Width * p.Width + p.Depth * p.Depth) / 2f + 0.3f;
                    Assert.Greater((spot - new Vector2(p.X, p.Z)).magnitude, reach, spec.Case + " " + p.Role);
                }
                foreach (var c in spec.Clues) Assert.Greater((spot - new Vector2(c.X, c.Z)).magnitude, 0.5f, c.Id);
                Assert.Greater((spot - new Vector2(spec.BoardX, spec.BoardZ)).magnitude, 0.9f, "beside the board, not in front of it");
            }
        }

        [Test]
        public void CaseYDetectiveStandsClearOfEveryZone()
        {
            foreach (var zone in CaseSpecs.Y.Zones)
            {
                // Margin of 0.5 m beyond the zone's half-diagonal (ignores its yaw, so it is conservative).
                float reach = Mathf.Sqrt(zone.Width * zone.Width + zone.Depth * zone.Depth) / 2f + 0.5f;
                Assert.Greater(new Vector2(CaseSpecs.YBriefingX - zone.X, CaseSpecs.YBriefingZ - zone.Z).magnitude, reach, zone.Id);
            }
        }

        [Test]
        public void KeyCluesNeverNameTheCulprit()
        {
            // User, 2026-11-04: "harder and less obvious" - initials, roles and times only; the answer needs several clues.
            foreach (var spec in new[] { CaseSpecs.X, CaseSpecs.Y })
                foreach (var clue in spec.Clues)
                    foreach (string part in spec.Culprit.Name.Split(' '))
                        StringAssert.DoesNotContain(part.ToLowerInvariant(), (clue.ObjectName + " " + clue.InspectText).ToLowerInvariant(), clue.Id);
        }

        [Test]
        public void SuspectCardsDontGiveAwayTheCulprit()
        {
            // Supervisor: "unconfirmed" on the board made the answer obvious. Every card is a plain quoted claim.
            foreach (var spec in new[] { CaseSpecs.X, CaseSpecs.Y })
                foreach (var s in spec.Suspects)
                {
                    StringAssert.StartsWith("\"", s.Alibi, s.Id);
                    StringAssert.DoesNotContain("confirm", s.Alibi.ToLowerInvariant(), s.Id);
                    StringAssert.DoesNotContain("no one", s.Alibi.ToLowerInvariant(), s.Id);
                    StringAssert.DoesNotContain("alone", s.Alibi.ToLowerInvariant(), s.Id);
                }
        }
    }
}
