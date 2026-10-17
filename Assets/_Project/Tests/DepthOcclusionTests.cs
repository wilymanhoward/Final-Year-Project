using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class DepthOcclusionTests
    {
        [Test]
        public void StatusChanges_RaisedOnce_StartsOff()
        {
            var go = new GameObject("session");
            try
            {
                var session = go.AddComponent<SessionManager>();
                var seen = new System.Collections.Generic.List<DepthOcclusionStatus>();
                session.DepthOcclusionChanged += seen.Add;
                Assert.AreEqual(DepthOcclusionStatus.Off, session.DepthOcclusion);
                session.ReportDepthOcclusion(DepthOcclusionStatus.Off);          // no change
                session.ReportDepthOcclusion(DepthOcclusionStatus.Waiting);
                session.ReportDepthOcclusion(DepthOcclusionStatus.Active);
                session.ReportDepthOcclusion(DepthOcclusionStatus.Active);       // repeated poll
                CollectionAssert.AreEqual(new[] { DepthOcclusionStatus.Waiting, DepthOcclusionStatus.Active }, seen);
                Assert.AreEqual(DepthOcclusionStatus.Active, session.DepthOcclusion);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LogCodes()
        {
            Assert.AreEqual("active", SessionManager.DepthOcclusionCode(DepthOcclusionStatus.Active));
            Assert.AreEqual("waiting", SessionManager.DepthOcclusionCode(DepthOcclusionStatus.Waiting));
            Assert.AreEqual("no_permission", SessionManager.DepthOcclusionCode(DepthOcclusionStatus.NoPermission));
            Assert.AreEqual("unsupported", SessionManager.DepthOcclusionCode(DepthOcclusionStatus.Unsupported));
            Assert.AreEqual("off", SessionManager.DepthOcclusionCode(DepthOcclusionStatus.Off));
        }
    }
}
