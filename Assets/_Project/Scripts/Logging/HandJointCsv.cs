using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Format of the hand-joint log <c>{run}_hands.csv</c> (replay viewer: moving fingers). One row per tick: session
    /// time, UTC time, then x/y/z of every joint of the left hand, then of the right hand, CaseRoot-local, in metres with
    /// 3 decimals (1 mm). An untracked hand = empty cells. Joint order = OpenXR hand joints without the palm
    /// (wrist .. pinky tip, Oculus.Interaction.Input.HandJointId 1..25); names = the WebXR joint names, '-' -> '_'.
    /// </summary>
    public static class HandJointCsv
    {
        public const float RateHz = 20f;
        public const string FileSuffix = "_hands";

        public static readonly string[] JointNames =
        {
            "wrist",
            "thumb-metacarpal", "thumb-phalanx-proximal", "thumb-phalanx-distal", "thumb-tip",
            "index-finger-metacarpal", "index-finger-phalanx-proximal", "index-finger-phalanx-intermediate", "index-finger-phalanx-distal", "index-finger-tip",
            "middle-finger-metacarpal", "middle-finger-phalanx-proximal", "middle-finger-phalanx-intermediate", "middle-finger-phalanx-distal", "middle-finger-tip",
            "ring-finger-metacarpal", "ring-finger-phalanx-proximal", "ring-finger-phalanx-intermediate", "ring-finger-phalanx-distal", "ring-finger-tip",
            "pinky-finger-metacarpal", "pinky-finger-phalanx-proximal", "pinky-finger-phalanx-intermediate", "pinky-finger-phalanx-distal", "pinky-finger-tip",
        };

        public static int JointCount => JointNames.Length;

        public static readonly string Header = BuildHeader();

        private static string BuildHeader()
        {
            var sb = new StringBuilder("session_time_s,utc_time");
            foreach (string side in new[] { "l", "r" })
                foreach (string j in JointNames)
                {
                    string n = side + "_" + j.Replace('-', '_');
                    sb.Append(',').Append(n).Append("_x,").Append(n).Append("_y,").Append(n).Append("_z");
                }
            return sb.ToString();
        }

        /// <summary>One row; <paramref name="left"/>/<paramref name="right"/> hold <see cref="JointCount"/> positions.</summary>
        public static string FormatRow(double sessionTime, DateTime utc, Vector3[] left, bool leftTracked,
            Vector3[] right, bool rightTracked, StringBuilder sb)
        {
            sb.Clear();
            sb.Append(sessionTime.ToString("F3", CultureInfo.InvariantCulture)).Append(',')   // 2 small strings per row are fine
              .Append(utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
            AppendHand(sb, left, leftTracked);
            AppendHand(sb, right, rightTracked);
            return sb.ToString();
        }

        private static void AppendHand(StringBuilder sb, Vector3[] joints, bool tracked)
        {
            for (int i = 0; i < JointCount; i++)
            {
                if (!tracked) { sb.Append(",,,"); continue; }
                Vector3 p = joints[i];
                AppendMetres(sb.Append(','), p.x);
                AppendMetres(sb.Append(','), p.y);
                AppendMetres(sb.Append(','), p.z);
            }
        }

        /// <summary>
        /// Metres with 3 decimals ("-0.042") without allocating: 150 values x 20 Hz would otherwise make thousands of
        /// strings a second on the Quest (Mono's Append(long) allocates too, so digits are written one by one).
        /// Same text as ToString("F3", InvariantCulture), except that a value rounding to zero is always "0.000".
        /// </summary>
        public static void AppendMetres(StringBuilder sb, float v)
        {
            long mm = (long)Math.Round((double)v * 1000.0, MidpointRounding.AwayFromZero);
            if (mm < 0) { sb.Append('-'); mm = -mm; }
            AppendDigits(sb, mm / 1000);
            sb.Append('.');
            long frac = mm % 1000;
            sb.Append((char)('0' + frac / 100)).Append((char)('0' + frac / 10 % 10)).Append((char)('0' + frac % 10));
        }

        private static void AppendDigits(StringBuilder sb, long n)
        {
            long div = 1;
            while (n / div >= 10) div *= 10;
            for (; div > 0; div /= 10) sb.Append((char)('0' + n / div % 10));
        }
    }
}
