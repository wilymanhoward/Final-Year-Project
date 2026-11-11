using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class ObjectPoseFilterTests
    {
        [Test]
        public void LogsFirstPoseThenOnlyRealMoves()
        {
            var f = new ObjectPoseFilter();
            Quaternion q = Quaternion.identity;
            Assert.IsTrue(f.ShouldLog("X_K1", Vector3.zero, q), "first sight");
            Assert.IsFalse(f.ShouldLog("X_K1", new Vector3(0.004f, 0f, 0f), q), "jitter below 5 mm");
            Assert.IsTrue(f.ShouldLog("X_K1", new Vector3(0.006f, 0f, 0f), q), "moved 6 mm");
            Assert.IsFalse(f.ShouldLog("X_K1", new Vector3(0.006f, 0f, 0f), Quaternion.Euler(0f, 1.5f, 0f)), "turned 1.5 deg");
            Assert.IsTrue(f.ShouldLog("X_K1", new Vector3(0.006f, 0f, 0f), Quaternion.Euler(0f, 3f, 0f)), "turned 3 deg");
            Assert.IsTrue(f.ShouldLog("X_K2", Vector3.zero, q), "ids are independent");
        }

        [Test]
        public void SmallStepsAddUpFromTheLastLoggedPose()
        {
            var f = new ObjectPoseFilter();
            f.ShouldLog("X_K1", Vector3.zero, Quaternion.identity);
            Assert.IsFalse(f.ShouldLog("X_K1", new Vector3(0.003f, 0f, 0f), Quaternion.identity));
            Assert.IsTrue(f.ShouldLog("X_K1", new Vector3(0.0055f, 0f, 0f), Quaternion.identity), "slow drift is not lost");
        }

        [Test]
        public void ResetLogsEverythingAgain()
        {
            var f = new ObjectPoseFilter();
            f.ShouldLog("X_K1", Vector3.zero, Quaternion.identity);
            f.Reset();
            Assert.IsTrue(f.ShouldLog("X_K1", Vector3.zero, Quaternion.identity));
        }
    }
}
