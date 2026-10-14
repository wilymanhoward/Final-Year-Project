using System;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FYP.Detective.Tests
{
    public class ColocationProtocolTests
    {
        [Test]
        public void AdvertRoundTrips()
        {
            byte[] data = ColocationProtocol.EncodeAdvert("10.72.55.149", 7777);
            Assert.AreEqual("FYPDET1|10.72.55.149|7777", Encoding.UTF8.GetString(data));
            Assert.IsTrue(ColocationProtocol.TryDecodeAdvert(data, out string ip, out int port));
            Assert.AreEqual("10.72.55.149", ip);
            Assert.AreEqual(7777, port);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("OTHERAPP|10.0.0.1|7777")]
        [TestCase("FYPDET1|10.0.0.1")]
        [TestCase("FYPDET1|10.0.0.999|7777")]
        [TestCase("FYPDET1|10.0.0.1|0")]
        [TestCase("FYPDET1|10.0.0.1|70000")]
        [TestCase("FYPDET1|10.0.0.1|-5")]
        [TestCase("FYPDET1|10.0.0.1|77x")]
        public void RejectsForeignOrBrokenAdverts(string text)
        {
            byte[] data = text == null ? null : Encoding.UTF8.GetBytes(text);
            Assert.IsFalse(ColocationProtocol.TryDecodeAdvert(data, out string ip, out int port));
            Assert.IsNull(ip);
            Assert.AreEqual(0, port);
        }

        [Test]
        public void EncodeRejectsBadInput()
        {
            Assert.Throws<ArgumentException>(() => ColocationProtocol.EncodeAdvert("not-an-ip", 7777));
            Assert.Throws<ArgumentOutOfRangeException>(() => ColocationProtocol.EncodeAdvert("10.0.0.1", 0));
        }

        [Test]
        public void AnchorMessageRoundTrips()
        {
            var group = Guid.NewGuid();
            var anchor = Guid.NewGuid();
            string text = ColocationProtocol.FormatAnchorMessage(group, anchor);
            Assert.IsTrue(ColocationProtocol.TryParseAnchorMessage(text, out Guid g, out Guid a));
            Assert.AreEqual(group, g);
            Assert.AreEqual(anchor, a);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc|def")]
        [TestCase("00000000-0000-0000-0000-000000000000|00000000-0000-0000-0000-000000000000")]
        [TestCase("6f9619ff-8b86-d011-b42d-00c04fc964ff")]
        public void RejectsBrokenAnchorMessages(string text)
        {
            Assert.IsFalse(ColocationProtocol.TryParseAnchorMessage(text, out _, out _));
        }

        [TestCase("FailureNetworkTimeout", "internet")]
        [TestCase("Failure_SpaceNetworkRequestFailed", "internet")]
        [TestCase("FailureCloudStorageDisabled", "Share Point Cloud Data")]
        [TestCase("Failure_SpaceLocalizationFailed", "Manual")]
        [TestCase("Failure_GroupNotFound", "re-place")]
        [TestCase("SomethingNew", "SomethingNew")]
        public void ExplainsFailures(string status, string expectedFragment)
        {
            StringAssert.Contains(expectedFragment, ColocationProtocol.ExplainAnchorFailure(status));
        }

        [Test]
        public void SharedAnchorMethodCode()
        {
            var r = new PlacementRecord(PlacementMethod.SharedAnchor, true, "u", Vector3.zero, 0f, DateTime.UtcNow);
            Assert.AreEqual("shared_anchor", r.MethodCode);
            StringAssert.StartsWith("method=shared_anchor;uuid=u;", r.FormatDetail());
        }
    }
}
