using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class FootEstimateTests
    {
        private static readonly Vector3 Head = new Vector3(1f, 1.6f, 2f);

        [Test]
        public void LookingLevel_FeetAreBaseDistanceBehindTheHead()
        {
            Vector3 feet = FootEstimate.FromHead(Head, Quaternion.identity);   // facing +z
            Assert.AreEqual(Head.x, feet.x, 1e-4f);
            Assert.AreEqual(Head.z - FootEstimate.BaseBack, feet.z, 1e-4f);
        }

        [Test]
        public void LookingDown_FeetMoveFurtherBack_CappedAndAgainstTheHeading()
        {
            // Facing +x (yaw 90), looking 45 degrees down.
            Vector3 feet = FootEstimate.FromHead(Head, Quaternion.Euler(45f, 90f, 0f));
            float back = Head.x - feet.x;
            Assert.Greater(back, FootEstimate.BaseBack);
            Assert.LessOrEqual(back, FootEstimate.MaxBack + 1e-4f);
            Assert.AreEqual(Head.z, feet.z, 1e-4f);

            // Straight down: heading still defined (from the head's up vector), full cap.
            Vector3 down = FootEstimate.FromHead(Head, Quaternion.Euler(90f, 90f, 0f));
            Assert.AreEqual(FootEstimate.MaxBack, Head.x - down.x, 1e-3f);
        }
    }
}
