using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class ClueRestDetectorTests
    {
        [Test]
        public void RestsAfterBeingSlowForLongEnough_ReportedOnce()
        {
            var d = new ClueRestDetector(0.05f, 0.5f);
            Assert.IsFalse(d.Update(2f, 0.0));      // falling
            Assert.IsFalse(d.Update(0.01f, 1.0));   // slow from 1.0
            Assert.IsFalse(d.Update(0.01f, 1.4));
            Assert.IsTrue(d.Update(0.01f, 1.5));
            Assert.IsFalse(d.Update(0f, 2.0));      // already reported
        }

        [Test]
        public void MovingAgain_StartsOver()
        {
            var d = new ClueRestDetector(0.05f, 0.5f);
            d.Update(0f, 0.0);
            Assert.IsTrue(d.Update(0f, 0.6));
            Assert.IsFalse(d.Update(1f, 0.7));      // knocked
            Assert.IsFalse(d.Update(0f, 0.8));
            Assert.IsTrue(d.Update(0f, 1.3));
        }

        [Test]
        public void Reset_ForgetsTheSlowPeriod()
        {
            var d = new ClueRestDetector(0.05f, 0.5f);
            d.Update(0f, 0.0);
            d.Reset();
            Assert.IsFalse(d.Update(0f, 0.6));      // slow only since 0.6
            Assert.IsTrue(d.Update(0f, 1.1));
        }
    }

    public class ClueZoneRuleTests
    {
        private static readonly Vector2 Size = new Vector2(0.4f, 0.5f);

        [Test]
        public void InsideOrWithinMargin_IsInZone_AtAnyHeight()
        {
            Vector3 centre = new Vector3(1f, 0f, 1f);
            Assert.IsTrue(ClueBounds.IsInZone(new Vector3(1f, 0f, 1f), centre, 0f, Size));
            Assert.IsTrue(ClueBounds.IsInZone(new Vector3(1.24f, 0.75f, 1f), centre, 0f, Size));   // 4 cm past the edge, on a table
            Assert.IsFalse(ClueBounds.IsInZone(new Vector3(1.27f, 0f, 1f), centre, 0f, Size));     // 7 cm past the edge
        }

        [Test]
        public void ZoneYawIsApplied()
        {
            Vector3 centre = Vector3.zero;
            Assert.IsFalse(ClueBounds.IsInZone(new Vector3(0.28f, 0f, 0f), centre, 0f, Size));
            Assert.IsTrue(ClueBounds.IsInZone(new Vector3(0.28f, 0f, 0f), centre, 90f, Size));     // depth now lies along x
        }

        [Test]
        public void ResetReasonIsLogged()
        {
            var s = new CaseState();
            s.Begin();
            Assert.IsTrue(s.RecordClueReset("X_K2", "P01", ClueBounds.ReasonText(ClueResetReason.InZone)));
            Assert.AreEqual("in_zone", s.LastResetReason);
            s.RecordClueReset("X_K2", null, ClueBounds.ReasonText(ClueResetReason.OutOfBounds));
            Assert.AreEqual("out_of_bounds", s.LastResetReason);
        }
    }

    public class ClueHoldEventTests
    {
        [Test]
        public void HoldEnded_RaisedWhenTheLastHandLetsGo()
        {
            var go = new GameObject("clue");
            try
            {
                var clue = go.AddComponent<Clue>();
                int ended = 0;
                clue.HoldEnded += _ => ended++;
                clue.BeginHold();
                clue.BeginHold();   // both hands
                clue.EndHold();
                Assert.AreEqual(0, ended);
                Assert.IsTrue(clue.IsHeld);
                clue.EndHold();
                Assert.AreEqual(1, ended);
                clue.EndHold();     // extra unselect: ignored
                Assert.AreEqual(1, ended);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
