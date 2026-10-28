using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class BodyPoseFrameTests
    {
        [Test]
        public void RoundTrip_KeepsHeadWristAndJoints_UntrackedHandStaysOff()
        {
            var a = new BodyPoseFrame { HasHead = true, HeadPosition = new Vector3(0.4f, 1.62f, -1.2f), HeadRotation = Quaternion.Euler(25f, 95f, 0f) };
            a.Left.Tracked = true;
            a.Left.WristPosition = new Vector3(0.3f, 1.1f, -1f);
            a.Left.WristRotation = Quaternion.Euler(10f, 40f, -70f);
            a.Left.Scale = 1.05f;
            for (int i = 0; i < 24; i++) a.Left.Joints.Add(Quaternion.Euler(i * 3f, 0f, i));

            byte[] bytes = a.Write(out int length);
            Assert.Less(length, 400);
            var b = new BodyPoseFrame();
            Assert.IsTrue(b.TryRead(bytes, length));
            Assert.IsTrue(b.HasHead);
            Assert.AreEqual(1.62f, b.HeadPosition.y, 1e-5f);
            Assert.Less(Quaternion.Angle(a.HeadRotation, b.HeadRotation), 0.1f);
            Assert.IsTrue(b.Left.Tracked);
            Assert.IsFalse(b.Right.Tracked);
            Assert.AreEqual(24, b.Left.Joints.Count);
            Assert.Less(Quaternion.Angle(a.Left.WristRotation, b.Left.WristRotation), 0.1f);
            Assert.Less(Quaternion.Angle(a.Left.Joints[23], b.Left.Joints[23]), 0.1f);
            Assert.AreEqual(1.05f, b.Left.Scale, 1e-5f);
        }

        [Test]
        public void TurnAround_PutsTheCopyOppositeThePivot_FacingBack()
        {
            var f = new BodyPoseFrame { HasHead = true, HeadPosition = new Vector3(0f, 1.6f, 0f), HeadRotation = Quaternion.identity };
            f.Right.Tracked = true;
            f.Right.WristPosition = new Vector3(0.25f, 1.1f, 0.3f);
            f.TurnAround(new Vector3(0f, 0f, 0.75f));
            Assert.AreEqual(1.5f, f.HeadPosition.z, 1e-5f);   // 1.5 m ahead of where the head was
            Assert.AreEqual(1.6f, f.HeadPosition.y, 1e-5f);
            Assert.AreEqual(-1f, (f.HeadRotation * Vector3.forward).z, 1e-4f);   // faces back towards you
            Assert.AreEqual(-0.25f, f.Right.WristPosition.x, 1e-5f);              // their right hand is on your left
            Assert.AreEqual(1.2f, f.Right.WristPosition.z, 1e-5f);
        }

        [Test]
        public void TryRead_RejectsTruncatedOrForeignBytes()
        {
            var a = new BodyPoseFrame { HasHead = true, HeadPosition = Vector3.one };
            a.Right.Tracked = true;
            a.Right.Joints.Add(Quaternion.identity);
            byte[] bytes = a.Write(out int length);
            var b = new BodyPoseFrame();
            Assert.IsFalse(b.TryRead(bytes, length - 3));
            Assert.IsFalse(b.TryRead(new byte[] { 9, 1, 0, 0 }, 4));   // unknown version
            Assert.IsFalse(b.TryRead(null, 0));
        }
    }
}
