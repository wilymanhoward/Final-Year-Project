using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class LanAddressTests
    {
        [TestCase("192.168.1.20", "192.168.1.20")]
        [TestCase(" 10.0.0.5 ", "10.0.0.5")]
        [TestCase("192.168.001.020", "192.168.1.20")]
        [TestCase("0.0.0.0", "0.0.0.0")]
        public void ParsesValidIpv4(string text, string expected)
        {
            Assert.IsTrue(LanAddress.TryParseIpv4(text, out string n));
            Assert.AreEqual(expected, n);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("192.168.1")]
        [TestCase("192.168.1.256")]
        [TestCase("192.168.1.x")]
        [TestCase("192.168..1")]
        [TestCase("-1.2.3.4")]
        [TestCase("host.local")]
        [TestCase("1234.1.1.1")]
        public void RejectsInvalidIpv4(string text)
        {
            Assert.IsFalse(LanAddress.TryParseIpv4(text, out _));
        }

        [TestCase("10.1.2.3", true)]
        [TestCase("172.16.0.1", true)]
        [TestCase("172.31.255.1", true)]
        [TestCase("172.32.0.1", false)]
        [TestCase("192.168.43.1", true)]
        [TestCase("8.8.8.8", false)]
        public void DetectsPrivateRanges(string ip, bool expected)
        {
            Assert.AreEqual(expected, LanAddress.IsPrivate(ip));
        }

        [Test]
        public void PrefersPrivateWifiAddress()
        {
            var picked = LanAddress.Pick(new[]
            {
                ("lo", "127.0.0.1"),
                ("rmnet0", "10.20.30.40"),
                ("wlan0", "192.168.1.20"),
            });
            Assert.AreEqual("192.168.1.20", picked);
        }

        [Test]
        public void FallsBackToAnyPrivateThenPublic()
        {
            Assert.AreEqual("10.0.0.7", LanAddress.Pick(new[] { ("eth0", "10.0.0.7"), ("x", "8.8.4.4") }));
            Assert.AreEqual("8.8.4.4", LanAddress.Pick(new[] { ("x", "8.8.4.4") }));
        }

        [Test]
        public void IgnoresLoopbackLinkLocalAndGarbage()
        {
            Assert.IsNull(LanAddress.Pick(new[] { ("lo", "127.0.0.1"), ("wlan0", "169.254.3.4"), ("w", "nope"), ("z", "0.0.0.0") }));
        }

        [Test]
        public void ConfigDefaultsToStandardPortAndNoHost()
        {
            var c = new ExperimentConfig();
            Assert.AreEqual(7777, c.netPort);
            Assert.AreEqual("", c.hostAddress);
        }
    }
}
