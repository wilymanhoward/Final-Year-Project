using System;
using System.Collections.Generic;
using System.Text;

namespace FYP.Detective
{
    /// <summary>One sentence of the detective's briefing: its audio file (base name) and the text in the speech bubble.</summary>
    public readonly struct BriefingLine
    {
        public readonly int Order;
        public readonly string File;
        public readonly string Case;        // "X", "Y" or "*"
        public readonly string Condition;   // "A", "B" or "*"
        public readonly string Text;

        public BriefingLine(int order, string file, string caseCode, string condition, string text)
        {
            Order = order;
            File = file;
            Case = caseCode;
            Condition = condition;
            Text = text;
        }
    }

    /// <summary>
    /// The detective's briefing script (session 6), read from Assets/_Project/Audio/Briefing/briefing_lines.csv
    /// (columns order,file,case,condition,text; text quoted, "" = a quote). The same CSV feeds the placeholder voice
    /// (tools/make_briefing_tts.ps1) and the AI voice files, which keep the same base names. Pure, unit tested.
    /// </summary>
    public static class BriefingScript
    {
        public static List<BriefingLine> Parse(string csv)
        {
            var lines = new List<BriefingLine>();
            if (string.IsNullOrEmpty(csv)) return lines;
            bool header = true;
            foreach (List<string> f in Rows(csv))
            {
                if (header) { header = false; continue; }
                if (f.Count < 5 || !int.TryParse(f[0].Trim(), out int order) || f[1].Trim().Length == 0) continue;
                lines.Add(new BriefingLine(order, f[1].Trim(), f[2].Trim(), f[3].Trim(), f[4].Trim()));
            }
            return lines;
        }

        /// <summary>The lines for one run (case X/Y, condition A/B), in order.</summary>
        public static List<BriefingLine> For(IEnumerable<BriefingLine> all, string caseCode, string condition)
        {
            var result = new List<BriefingLine>();
            foreach (BriefingLine l in all)
                if ((l.Case == "*" || l.Case == caseCode) && (l.Condition == "*" || l.Condition == condition)) result.Add(l);
            result.Sort((a, b) => a.Order.CompareTo(b.Order));
            return result;
        }

        /// <summary>Speaking time when a line has no audio clip yet (about 2.6 words per second, at least 1.5 s).</summary>
        public static float EstimateSeconds(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1.5f;
            int words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Max(1.5f, words / 2.6f);
        }

        // Minimal CSV reader: commas, double-quoted fields with "" escapes, \n or \r\n line ends.
        private static IEnumerable<List<string>> Rows(string csv)
        {
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear();
                    if (row.Count > 1 || row[0].Length > 0) yield return row;
                    row = new List<string>();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                yield return row;
            }
        }
    }
}
