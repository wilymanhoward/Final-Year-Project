using System.Text;
using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class DataTransferTests
    {
        [Test]
        public void Beacon_ParsesPort_RejectsOthers()
        {
            Assert.IsTrue(DataTransfer.TryParseBeacon("FYPDATA1|8765", out int port));
            Assert.AreEqual(8765, port);
            Assert.IsFalse(DataTransfer.TryParseBeacon("FYPDATA2|8765", out _));
            Assert.IsFalse(DataTransfer.TryParseBeacon("FYPDATA1|80", out _));       // below 1024
            Assert.IsFalse(DataTransfer.TryParseBeacon("FYPDATA1|-5", out _));
            Assert.IsFalse(DataTransfer.TryParseBeacon(null, out _));
        }

        [Test]
        public void StudyFiles_OnlyLogsSummariesAndMeta()
        {
            Assert.IsTrue(DataTransfer.IsStudyFile("G01_P01_A_X_20261017_101500.csv"));
            Assert.IsTrue(DataTransfer.IsStudyFile("G01_P01_A_X_20261017_101500_summary.csv"));
            Assert.IsTrue(DataTransfer.IsStudyFile("G01_P01_A_X_20261017_101500_meta.json"));
            Assert.IsFalse(DataTransfer.IsStudyFile("experiment_config.json"));
            Assert.IsFalse(DataTransfer.IsStudyFile(DataUploader.LedgerFileName));
            Assert.IsFalse(DataTransfer.IsStudyFile("../evil.csv"));
            Assert.IsFalse(DataTransfer.IsStudyFile(".hidden.csv"));
            Assert.IsFalse(DataTransfer.IsStudyFile("a b.csv"));
            Assert.IsFalse(DataTransfer.IsStudyFile(new string('a', 130) + ".csv"));
        }

        [Test]
        public void DeviceTag_IsShortAndSafe()
        {
            Assert.AreEqual("quest-ab12cd34", DataTransfer.DeviceTag("AB12-CD34-EF56"));
            Assert.AreEqual("quest-unknown", DataTransfer.DeviceTag(null));
            Assert.AreEqual("quest-unknown", DataTransfer.DeviceTag("---"));
            Assert.IsTrue(DataTransfer.IsSafeName(DataTransfer.DeviceTag("x/y\\z")));
        }

        [Test]
        public void UploadUrl_And_Confirmation()
        {
            Assert.AreEqual("http://192.168.1.20:8765/upload/quest-ab12cd34/run.csv",
                DataTransfer.UploadUrl("192.168.1.20", 8765, "quest-ab12cd34", "run.csv"));
            string sha = DataTransfer.Sha256Hex(Encoding.ASCII.GetBytes("abc"));
            Assert.AreEqual("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", sha);   // SHA-256("abc")
            Assert.IsTrue(DataTransfer.IsConfirmed(200, "OK " + sha + "\n", sha));
            Assert.IsFalse(DataTransfer.IsConfirmed(200, "OK " + new string('0', 64), sha));    // other content arrived
            Assert.IsFalse(DataTransfer.IsConfirmed(400, "OK " + sha, sha));
            Assert.IsFalse(DataTransfer.IsConfirmed(200, null, sha));
        }
    }

    public class UploadLedgerTests
    {
        [Test]
        public void SentOnlyWhileTheFileIsUnchanged()
        {
            var ledger = new UploadLedger();
            Assert.IsFalse(ledger.IsSent("run.csv", 100, 5));
            ledger.Record("run.csv", 100, 5, "abc");
            Assert.IsTrue(ledger.IsSent("run.csv", 100, 5));
            Assert.IsFalse(ledger.IsSent("run.csv", 120, 5));   // grew
            Assert.IsFalse(ledger.IsSent("run.csv", 100, 6));   // rewritten
        }

        [Test]
        public void FormatParseRoundTrip_SkipsBrokenLines()
        {
            var ledger = new UploadLedger();
            ledger.Record("b_summary.csv", 10, 20, "s2");
            ledger.Record("a.csv", 1, 2, "s1");
            string text = ledger.Format();
            Assert.AreEqual("a.csv|1|2|s1\nb_summary.csv|10|20|s2\n", text);
            var back = UploadLedger.Parse(text + "garbage\n../x.csv|1|2|s\nc.csv|x|2|s\n");
            Assert.AreEqual(2, back.Count);
            Assert.IsTrue(back.IsSent("a.csv", 1, 2));
            Assert.IsTrue(back.IsSent("b_summary.csv", 10, 20));
            Assert.AreEqual(0, UploadLedger.Parse(null).Count);
        }
    }
}
