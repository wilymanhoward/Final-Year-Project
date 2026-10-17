using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class TrackedPointGateTests
    {
        [Test]
        public void TrackedPoint_PassesInsideThrough()
        {
            var g = new TrackedPointGate();
            Assert.IsFalse(g.Update(true, false));
            Assert.IsTrue(g.Update(true, true));
            Assert.IsFalse(g.Update(true, false));
        }

        [Test]
        public void UntrackedPoint_IsNeverInside()
        {
            var g = new TrackedPointGate();
            g.Update(true, false);
            Assert.IsFalse(g.Update(false, true));
            Assert.IsFalse(g.Update(false, true));
        }

        [Test]
        public void RegainingTrackingInside_DoesNotCountUntilSeenOutside()
        {
            var g = new TrackedPointGate();
            Assert.IsFalse(g.Update(true, false));   // tracked, outside
            Assert.IsFalse(g.Update(false, false));  // lost
            Assert.IsFalse(g.Update(true, true));    // back, already inside: suppressed
            Assert.IsFalse(g.Update(true, true));    // still inside: still suppressed
            Assert.IsFalse(g.Update(true, false));   // leaves: suppression cleared
            Assert.IsTrue(g.Update(true, true));     // real entry
        }

        [Test]
        public void LostWhileInside_ThenBackInside_IsNotANewEntry()
        {
            var g = new TrackedPointGate();
            var zone = new ZoneEntryTracker(1.0);
            Assert.IsFalse(zone.Update(g.Update(true, false), 0));   // tracked, outside
            Assert.IsTrue(zone.Update(g.Update(true, true), 1));     // enters: 1
            Assert.IsFalse(zone.Update(g.Update(false, true), 2));   // tracking lost inside: ignored (zone empties)
            Assert.IsFalse(zone.Update(g.Update(true, true), 5));    // back inside after cooldown: NOT an entry
            Assert.AreEqual(1, zone.EntryCount);
        }

        [Test]
        public void FirstObservationInside_IsNotAnEntry()
        {
            // e.g. a hand already inside a zone when the session starts
            var g = new TrackedPointGate();
            Assert.IsFalse(g.Update(true, true));
        }
    }

    public class ClueBoundsTests
    {
        [Test]
        public void InsideRoom_IsInBounds()
        {
            Assert.IsFalse(ClueBounds.IsOutOfBounds(new Vector3(1f, 0.8f, -1.2f)));
            Assert.IsFalse(ClueBounds.IsOutOfBounds(new Vector3(0f, -0.03f, 0f)));   // within floor margin
        }

        [Test]
        public void BelowFloor_IsOut()
        {
            Assert.IsTrue(ClueBounds.IsOutOfBounds(new Vector3(0f, -0.2f, 0f)));
        }

        [Test]
        public void TooFarFromRoot_IsOut()
        {
            Assert.IsTrue(ClueBounds.IsOutOfBounds(new Vector3(3f, 1f, 2f)));      // 3.74 m (limit 3.6 m)
            Assert.IsFalse(ClueBounds.IsOutOfBounds(new Vector3(2.5f, 1f, 2f)));   // 3.35 m: a 5 x 5 m corner is still inside
            Assert.IsFalse(ClueBounds.IsOutOfBounds(new Vector3(2.5f, 1f, 1f)));   // 2.87 m
        }
    }

    public class PlayerRigTrackingTests
    {
        [Test]
        public void SetTracked_RaisesOnlyOnChange_AndDefaultsToTracked()
        {
            var go = new GameObject("rig");
            try
            {
                var rig = go.AddComponent<PlayerRig>();
                int raised = 0;
                rig.TrackingChanged += (part, tracked) => raised++;
                Assert.IsTrue(rig.IsTracked(BodyPart.LeftHand));
                rig.SetTracked(BodyPart.LeftHand, true);
                Assert.AreEqual(0, raised);
                rig.SetTracked(BodyPart.LeftHand, false);
                rig.SetTracked(BodyPart.LeftHand, false);
                Assert.AreEqual(1, raised);
                Assert.IsFalse(rig.IsTracked(BodyPart.LeftHand));
                Assert.IsTrue(rig.IsTracked(BodyPart.RightHand));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    public class ClueResetEventTests
    {
        [Test]
        public void ClueReset_OnlyReportedWhileRunning()
        {
            var s = new CaseState();
            int raised = 0;
            s.ClueReset += _ => raised++;
            Assert.IsFalse(s.RecordClueReset("c1"));
            s.Begin();
            Assert.IsTrue(s.RecordClueReset("c1"));
            Assert.AreEqual(1, raised);
        }
    }
}

namespace FYP.Detective.Tests
{
    public class TrackingEventFilterTests
    {
        [Test]
        public void ShortDropout_IsNotLogged()
        {
            var f = new TrackingEventFilter(0.2);
            Assert.AreEqual(TrackingEvent.None, f.Update(true, 0));
            Assert.AreEqual(TrackingEvent.None, f.Update(false, 1.0));
            Assert.AreEqual(TrackingEvent.None, f.Update(false, 1.15));
            Assert.AreEqual(TrackingEvent.None, f.Update(true, 1.19));   // back within 0.2 s: nothing
        }

        [Test]
        public void LongLoss_IsLoggedOnce_ThenRegained()
        {
            var f = new TrackingEventFilter(0.2);
            f.Update(true, 0);
            Assert.AreEqual(TrackingEvent.None, f.Update(false, 1.0));
            Assert.AreEqual(TrackingEvent.Lost, f.Update(false, 1.25));   // past 0.2 s (1.2 - 1.0 is 0.1999... in floating point)
            Assert.AreEqual(1.0, f.LostSince, 1e-9);                      // when it really started
            Assert.AreEqual(TrackingEvent.None, f.Update(false, 3.0));    // not repeated
            Assert.AreEqual(TrackingEvent.Regained, f.Update(true, 3.1));
            Assert.AreEqual(TrackingEvent.None, f.Update(true, 3.2));
        }

        [Test]
        public void Flicker_ProducesNoEvents()
        {
            // the 6 toggles in 0.4 s seen on the headset
            var f = new TrackingEventFilter(0.2);
            double[] t = { 11.498, 11.597, 11.638, 11.665, 11.720, 11.901 };
            for (int i = 0; i < t.Length; i++)
                Assert.AreEqual(TrackingEvent.None, f.Update(i % 2 == 1, t[i]));
        }

        [Test]
        public void Reset_ForgetsAReportedLoss()
        {
            var f = new TrackingEventFilter(0.2);
            f.Update(false, 0);
            Assert.AreEqual(TrackingEvent.Lost, f.Update(false, 0.3));
            f.Reset();
            Assert.AreEqual(TrackingEvent.None, f.Update(true, 1));       // no Regained after reset
        }
    }
}
