using System;
using System.IO;
using System.Text;

namespace FYP.Detective
{
    /// <summary>
    /// Appends lines to a new CSV file. Buffered in memory; call <see cref="Flush"/>
    /// regularly (DataLogger does it every second and on pause/quit) so a crash
    /// loses at most the last interval. Never overwrites an existing file.
    /// </summary>
    public sealed class CsvLogWriter : IDisposable
    {
        private FileStream _stream;
        private StreamWriter _writer;

        public string FilePath { get; }
        public bool IsOpen => _writer != null;

        public CsvLogWriter(string filePath)
        {
            FilePath = filePath;
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // CreateNew throws if the file exists, so data can never be overwritten.
            _stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(_stream, new UTF8Encoding(false), 16 * 1024) { NewLine = "\n" };
        }

        public void WriteLine(string line)
        {
            if (_writer == null) return;
            _writer.WriteLine(line);
        }

        /// <summary>Pushes buffered text to the OS and asks it to write to storage.</summary>
        public void Flush()
        {
            if (_writer == null) return;
            _writer.Flush();
            _stream.Flush(true);
        }

        public void Dispose()
        {
            if (_writer == null) return;
            try { Flush(); }
            finally
            {
                _writer.Dispose();
                _writer = null;
                _stream = null;
            }
        }

        /// <summary>
        /// Returns directory/fileName, or directory/name_2.csv, name_3.csv ... if it already exists.
        /// </summary>
        public static string GetUniquePath(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path)) return path;

            string stem = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            for (int i = 2; i < 1000; i++)
            {
                path = Path.Combine(directory, stem + "_" + i + ext);
                if (!File.Exists(path)) return path;
            }
            throw new IOException("Could not find a free file name for " + fileName);
        }
    }
}
