using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FYP.Detective
{
    /// <summary>
    /// Which study files the laptop has confirmed (name, size, last-write time, SHA-256), saved as text lines
    /// "name|size|ticks|sha" next to the data (upload_ledger.txt). A file counts as sent only while its size and
    /// write time are unchanged, so a file that changes is sent again. Pure: the caller does the file IO.
    /// </summary>
    public sealed class UploadLedger
    {
        private struct Entry
        {
            public long Size;
            public long Ticks;
            public string Sha;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public int Count => _entries.Count;

        public static UploadLedger Parse(string text)
        {
            var ledger = new UploadLedger();
            if (string.IsNullOrEmpty(text)) return ledger;
            foreach (string raw in text.Split('\n'))
            {
                string[] parts = raw.Trim().Split('|');
                if (parts.Length != 4 || !DataTransfer.IsSafeName(parts[0])) continue;
                if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long size)) continue;
                if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)) continue;
                ledger._entries[parts[0]] = new Entry { Size = size, Ticks = ticks, Sha = parts[3] };
            }
            return ledger;
        }

        /// <summary>True if this exact version of the file (same size and write time) was confirmed by the laptop.</summary>
        public bool IsSent(string name, long size, long ticks) =>
            _entries.TryGetValue(name, out Entry e) && e.Size == size && e.Ticks == ticks;

        public void Record(string name, long size, long ticks, string sha256) =>
            _entries[name] = new Entry { Size = size, Ticks = ticks, Sha = sha256 };

        public string Format()
        {
            var sb = new StringBuilder();
            var names = new List<string>(_entries.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string n in names)
            {
                Entry e = _entries[n];
                sb.Append(n).Append('|').Append(e.Size.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.Ticks.ToString(CultureInfo.InvariantCulture)).Append('|').Append(e.Sha).Append('\n');
            }
            return sb.ToString();
        }
    }
}
