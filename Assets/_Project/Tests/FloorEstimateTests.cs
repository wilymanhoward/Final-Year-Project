using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class FloorEstimateTests
    {
        [Test]
        public void Thresholds()
        {
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
