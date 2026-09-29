using System.IO;
using NUnit.Framework;

namespace FYP.Detective.Tests
{
    public class CsvLogWriterTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "fyp_csv_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void FlushedLines_AreReadableWhileFileIsOpen()
        {
            string path = Path.Combine(_dir, "a.csv");
            using (var w = new CsvLogWriter(path))
            {
                w.WriteLine("h1,h2");
                w.WriteLine("1,2");
                w.Flush();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var r = new StreamReader(fs))
                {
                    Assert.AreEqual("h1,h2\n1,2\n", r.ReadToEnd());
                }
            }
        }

        [Test]
        public void NeverOverwrites_UniquePathAddsSuffix()
        {
            string first = CsvLogWriter.GetUniquePath(_dir, "x.csv");
            using (new CsvLogWriter(first)) { }
            string second = CsvLogWriter.GetUniquePath(_dir, "x.csv");
            Assert.AreEqual(Path.Combine(_dir, "x_2.csv"), second);
            Assert.Throws<IOException>(() => new CsvLogWriter(first).Dispose());
        }
    }
}
