using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class ZoneEntryTrackerTests
    {
        [Test]
        public void StayingInside_CountsOneEntry_NotFrames()
        {
            var t = new ZoneEntryTracker(1.0);
            Assert.IsTrue(t.Update(true, 0.0));
            for (int i = 1; i <= 100; i++) Assert.IsFalse(t.Update(true, i * 0.016));
            Assert.AreEqual(1, t.EntryCount);
        }

        [Test]
        public void StartingOutside_CountsNothing()
        {
            var t = new ZoneEntryTracker(1.0);
            Assert.IsFalse(t.Update(false, 0.0));
            Assert.IsFalse(t.Update(false, 5.0));
            Assert.AreEqual(0, t.EntryCount);
        }

        [Test]
        public void ReEntryWithinCooldown_IsIgnored()
        {
            var t = new ZoneEntryTracker(1.0);
            t.Update(true, 0.0);
            t.Update(false, 0.2);
            Assert.IsFalse(t.Update(true, 0.5));   // 0.5 s after the counted entry
            t.Update(false, 0.7);
            Assert.IsFalse(t.Update(true, 0.99));
            Assert.AreEqual(1, t.EntryCount);
        }

        [Test]
        public void ReEntryAfterCooldown_IsCounted()
        {
            var t = new ZoneEntryTracker(1.0);
            t.Update(true, 0.0);
            t.Update(false, 0.3);
            Assert.IsTrue(t.Update(true, 1.0));    // exactly at cooldown boundary counts
            t.Update(false, 1.5);
            Assert.IsTrue(t.Update(true, 2.5));
            Assert.AreEqual(3, t.EntryCount);
        }

        [Test]
        public void EdgeJitter_CountsOncePerCooldownWindow()
        {
            var t = new ZoneEntryTracker(1.0);
            // Toggle in/out every frame for 2.5 s at 72 fps.
            double dt = 1.0 / 72.0;
            for (int i = 0; i < 180; i++) t.Update(i % 2 == 0, i * dt);
            Assert.AreEqual(3, t.EntryCount); // entries at ~0 s, ~1 s, ~2 s
        }

        [Test]
        public void CooldownIsMeasuredFromLastCountedEntry_NotFromIgnoredOne()
        {
            var t = new ZoneEntryTracker(1.0);
            t.Update(true, 0.0);   // counted
            t.Update(false, 0.1);
            t.Update(true, 0.9);   // ignored
            t.Update(false, 0.95);
            Assert.IsTrue(t.Update(true, 1.05)); // 1.05 s after the counted one
        }

        [Test]
        public void ZeroCooldown_CountsEveryEntry()
        {
            var t = new ZoneEntryTracker(0.0);
            t.Update(true, 0.0); t.Update(false, 0.01); t.Update(true, 0.02);
            Assert.AreEqual(2, t.EntryCount);
        }

        [Test]
        public void Reset_ClearsCountAndCooldown()
        {
            var t = new ZoneEntryTracker(1.0);
            t.Update(true, 0.0);
            t.Reset();
            Assert.AreEqual(0, t.EntryCount);
            Assert.IsFalse(t.IsInside);
            Assert.IsTrue(t.Update(true, 0.1)); // cooldown not carried over
        }

        [Test]
        public void TwoTrackers_AreIndependent()
        {
            var a = new ZoneEntryTracker(1.0);
            var b = new ZoneEntryTracker(1.0);
            Assert.IsTrue(a.Update(true, 0.0));
            Assert.IsTrue(b.Update(true, 0.1));
            Assert.AreEqual(1, a.EntryCount);
            Assert.AreEqual(1, b.EntryCount);
        }
    }

    public class FloorFootprintTests
    {
        private static readonly Vector2 Size = new Vector2(2f, 1f); // 2 m wide (X), 1 m deep (Z)

        [Test]
        public void IgnoresHeight()
        {
            Assert.IsTrue(FloorFootprint.Contains(new Vector3(0f, 1.7f, 0f), Vector3.zero, 0f, Size));
            Assert.IsTrue(FloorFootprint.Contains(new Vector3(0.5f, -3f, 0.2f), Vector3.zero, 0f, Size));
        }

        [Test]
        public void Unrotated_UsesWidthOnXAndDepthOnZ()
        {
            Assert.IsTrue(FloorFootprint.Contains(new Vector3(0.9f, 0f, 0f), Vector3.zero, 0f, Size));
            Assert.IsFalse(FloorFootprint.Contains(new Vector3(0f, 0f, 0.9f), Vector3.zero, 0f, Size));
            Assert.IsFalse(FloorFootprint.Contains(new Vector3(1.1f, 0f, 0f), Vector3.zero, 0f, Size));
        }

        [Test]
        public void Rotated90_SwapsAxes()
        {
            Assert.IsFalse(FloorFootprint.Contains(new Vector3(0.9f, 0f, 0f), Vector3.zero, 90f, Size));
            Assert.IsTrue(FloorFootprint.Contains(new Vector3(0f, 0f, 0.9f), Vector3.zero, 90f, Size));
        }

        [Test]
        public void OffsetCentre()
        {
            var c = new Vector3(3f, 0f, -2f);
            Assert.IsTrue(FloorFootprint.Contains(new Vector3(3.5f, 1.6f, -2.2f), c, 0f, Size));
            Assert.IsFalse(FloorFootprint.Contains(Vector3.zero, c, 0f, Size));
        }
    }
}
