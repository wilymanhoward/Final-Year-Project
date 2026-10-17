using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class SharedPlacementTests
    {
        [Test]
        public void SharedAnchor_KeepsPositionAndHeading_StandsOnThisHeadsetsFloor()
        {
            // Host's anchor 6 cm above this headset's floor, slightly tilted (anchors can carry a little tilt).
            var anchorPos = new Vector3(1.2f, 0.06f, -0.4f);
            var anchorRot = Quaternion.Euler(2f, 35f, -1.5f);
            Pose p = PlacementMath.FloorAlignedPose(anchorPos, anchorRot, 0f);
            Assert.AreEqual(1.2f, p.position.x, 1e-5f);
            Assert.AreEqual(-0.4f, p.position.z, 1e-5f);
            Assert.AreEqual(0f, p.position.y, 1e-5f, "on this headset's floor");
            Assert.AreEqual(35f, p.rotation.eulerAngles.y, 0.2f, "same heading as the host");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, p.rotation.eulerAngles.x), 1e-3f, "level");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, p.rotation.eulerAngles.z), 1e-3f, "level");
            Assert.IsTrue(PlacementHeight.IsOk(p.position.y - 0f), "so the height check passes");
        }
    }
}
