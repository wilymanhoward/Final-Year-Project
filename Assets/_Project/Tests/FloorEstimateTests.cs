using System.Collections.Generic;
using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class FloorEstimateTests
    {
        private static List<float> Hits(float y, int n, float spread = 0.01f)
        {
            var list = new List<float>();
            for (int i = 0; i < n; i++) list.Add(y + spread * ((i % 5) - 2) / 2f);
            return list;
        }

        [Test]
        public void FindsTheFloorUnderTablesAndIgnoresStrayLowHits()
        {
            var hits = Hits(-0.40f, 30);          // real floor 40 cm below the scene floor
            hits.AddRange(Hits(0.35f, 40));      // a desk top, more hits than the floor
            hits.Add(-0.9f); hits.Add(-0.7f);    // noise
            Assert.IsTrue(FloorEstimate.TryFindFloor(hits, out float y, out int support));
            Assert.AreEqual(-0.40f, y, 0.011f);
            Assert.GreaterOrEqual(support, 30);
        }

        [Test]
        public void NoisyFloorIsNotPulledTowardsItsLowestHits()
        {
            // 60 floor hits spread evenly +/- 3 cm around -0.40 (depth noise) + a desk: the old "lowest 4 cm band" median
            // came out ~1 cm low (user, 2026-11-13: floor a little below the real one).
            var hits = new List<float>();
            for (int i = 0; i < 60; i++) hits.Add(-0.43f + 0.06f * i / 59f);
            hits.AddRange(Hits(0.35f, 40));
            Assert.IsTrue(FloorEstimate.TryFindFloor(hits, out float y, out int support));
            Assert.AreEqual(-0.40f, y, 0.003f);
            Assert.GreaterOrEqual(support, 50);
        }

        [Test]
        public void TooFewHitsOrAScatteredWallGiveNoFloor()
        {
            Assert.IsFalse(FloorEstimate.TryFindFloor(Hits(0f, 5), out _, out _));
            var wall = new List<float>();
            for (int i = 0; i < 60; i++) wall.Add(i * 0.03f);   // hits spread up a wall
            Assert.IsFalse(FloorEstimate.TryFindFloor(wall, out _, out _));
        }

        [Test]
        public void PlausibilityAndThreshold()
        {
            Assert.IsTrue(FloorEstimate.IsPlausible(0f, -0.4f, 1.2f));
            Assert.IsFalse(FloorEstimate.IsPlausible(0f, -1.0f, 1.2f));   // too big a jump
            Assert.IsFalse(FloorEstimate.IsPlausible(0f, 0.9f, 1.2f));    // head only 0.3 m above it
            Assert.IsFalse(FloorEstimate.NeedsCorrection(0f, 0.01f));                                   // auto: 1.5 cm
            Assert.IsTrue(FloorEstimate.NeedsCorrection(0f, -0.02f));
            Assert.IsTrue(FloorEstimate.NeedsCorrection(0f, -0.01f, FloorEstimate.ManualCorrectionThreshold)); // Fix floor
            Assert.IsFalse(FloorEstimate.NeedsCorrection(0f, -0.004f, FloorEstimate.ManualCorrectionThreshold));
        }

        [Test]
        public void TouchNeedsAFlatLowStillHandForOneSecond()
        {
            var d = new TouchFloorDetector();
            float floor;
            Assert.IsFalse(d.Update(0.0, 1.0f, -0.39f, -0.37f, out _));
            Assert.IsFalse(d.Update(0.5, 1.0f, -0.392f, -0.37f, out _));
            Assert.IsTrue(d.Update(1.05, 1.0f, -0.388f, -0.37f, out floor));
            Assert.AreEqual(-0.39f - TouchFloorDetector.JointAboveSurface, floor, 0.003f);

            d.Reset();
            Assert.IsFalse(d.Update(0.0, 1.6f, 0.80f, 0.98f, out _));   // hanging hand: not flat
            Assert.IsFalse(d.Update(2.0, 1.6f, 0.80f, 0.98f, out _));
            Assert.IsFalse(d.Update(3.0, 1.0f, 0.75f, 0.76f, out _));   // flat but only 0.25 m below the head
            Assert.IsFalse(d.Update(0.0, 1.0f, -0.40f, -0.38f, out _));
            Assert.IsFalse(d.Update(0.6, 1.0f, -0.36f, -0.34f, out _)); // moved 4 cm: the hold restarts
            Assert.IsFalse(d.Update(1.2, 1.0f, -0.36f, -0.34f, out _));
            Assert.IsTrue(d.Update(1.7, 1.0f, -0.36f, -0.34f, out _));
        }
    }
}
