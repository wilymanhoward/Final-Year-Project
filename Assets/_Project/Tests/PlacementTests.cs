using System;
using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class PlacementMathTests
    {
        private const float Tol = 1e-3f;

        private static void AssertYaw(float expected, float actual)
        {
            Assert.AreEqual(0f, Mathf.DeltaAngle(expected, actual), 0.01f, $"expected yaw {expected}, got {actual}");
        }

        [Test]
        public void Position_IsHeadXZ_OnTheFloor()
        {
            var pose = PlacementMath.FloorPoseFromHead(new Vector3(1.2f, 1.65f, -0.4f), Quaternion.identity, 0f);
            Assert.AreEqual(1.2f, pose.position.x, Tol);
            Assert.AreEqual(0f, pose.position.y, Tol);
            Assert.AreEqual(-0.4f, pose.position.z, Tol);
        }

        [Test]
        public void FloorHeight_IsUsed()
        {
            var pose = PlacementMath.FloorPoseFromHead(new Vector3(0f, 1.7f, 0f), Quaternion.identity, 0.25f);
            Assert.AreEqual(0.25f, pose.position.y, Tol);
        }

        [Test]
        public void Rotation_IsYawOnly_IgnoringPitchAndRoll()
        {
            var pose = PlacementMath.FloorPoseFromHead(Vector3.zero, Quaternion.Euler(25f, 90f, 15f));
            AssertYaw(90f, pose.rotation.eulerAngles.y);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, pose.rotation.eulerAngles.x), 0.01f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, pose.rotation.eulerAngles.z), 0.01f);
        }

        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(-120f)]
        [TestCase(180f)]
        public void Heading_MatchesYaw(float yaw)
        {
            AssertYaw(yaw, PlacementMath.HeadingDegrees(Quaternion.Euler(-10f, yaw, 0f)));
        }

        [Test]
        public void LookingStraightDown_StillUsesFacingDirection()
        {
            AssertYaw(45f, PlacementMath.HeadingDegrees(Quaternion.Euler(90f, 45f, 0f)));
            AssertYaw(-60f, PlacementMath.HeadingDegrees(Quaternion.Euler(-90f, -60f, 0f)));
        }
    }

    public class PoseSampleTests
    {
        private const float Tol = 1e-4f;

        [Test]
        public void ToLocal_UndoesFrameTranslationAndRotation()
        {
            var frameRotation = Quaternion.Euler(0f, 90f, 0f);
            var frameOrigin = new Vector3(2f, 0f, 3f);
            var world = new PoseSample
            {
                HasHead = true,
                HeadPosition = frameOrigin + frameRotation * new Vector3(0.5f, 1.6f, 1f),
                HeadRotation = frameRotation * Quaternion.Euler(10f, 20f, 0f),
                HasLeftHand = true,
                LeftHandPosition = frameOrigin + frameRotation * new Vector3(-0.3f, 1f, 0.2f),
            };

            var local = world.ToLocal(frameOrigin, frameRotation);

            Assert.AreEqual(0.5f, local.HeadPosition.x, Tol);
            Assert.AreEqual(1.6f, local.HeadPosition.y, Tol);
            Assert.AreEqual(1f, local.HeadPosition.z, Tol);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(10f, 20f, 0f), local.HeadRotation), 0.01f);
            Assert.AreEqual(-0.3f, local.LeftHandPosition.x, Tol);
            Assert.AreEqual(0.2f, local.LeftHandPosition.z, Tol);
        }

        [Test]
        public void ToLocal_KeepsMissingPartsMissing()
        {
            var world = new PoseSample { HasHead = true, HeadPosition = Vector3.one, HeadRotation = Quaternion.identity };
            var local = world.ToLocal(Vector3.one, Quaternion.identity);
            Assert.IsTrue(local.HasHead);
            Assert.IsFalse(local.HasLeftHand);
            Assert.IsFalse(local.HasRightHand);
            Assert.AreEqual(Vector3.zero, local.HeadPosition);
        }
    }

    public class PlacementRecordTests
    {
        [Test]
        public void EventName_DependsOnLoaded()
        {
            var set = new PlacementRecord(PlacementMethod.Anchor, false, "u", Vector3.zero, 0f, DateTime.UtcNow);
            var loaded = new PlacementRecord(PlacementMethod.Anchor, true, "u", Vector3.zero, 0f, DateTime.UtcNow);
            Assert.AreEqual(LogEvents.PlacementSet, set.EventName);
            Assert.AreEqual(LogEvents.PlacementLoaded, loaded.EventName);
        }

        [Test]
        public void Detail_IsInvariant_AndHasNoCommas()
        {
            var r = new PlacementRecord(PlacementMethod.Manual, false, null, new Vector3(1.5f, 0f, -2.25f), 90f,
                new DateTime(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc));
            Assert.AreEqual(
                "method=manual;uuid=;x=1.5000;y=0.0000;z=-2.2500;yaw_deg=90.00;at_utc=2026-10-08T09:30:00.000Z",
                r.FormatDetail());
        }
    }
}
