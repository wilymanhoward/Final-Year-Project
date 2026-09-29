using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Per-run settings chosen by the experimenter (codes, condition, case, time cap).
    /// Plain serializable class: shown in the Inspector, saved as JSON, and unit tested.
    /// Only anonymous codes are allowed here - never participant names.
    /// </summary>
    [Serializable]
    public class ExperimentConfig
    {
        public const float DefaultTimeCapMinutes = 15f;
        public const string FileTimestampFormat = "yyyyMMdd_HHmmss";

        [Tooltip("Anonymous pair/group code, e.g. G01. Letters, digits and '-' only.")]
        public string pairCode = "G01";

        [Tooltip("Anonymous participant code, e.g. P01. Letters, digits and '-' only.")]
        public string participantId = "P01";

        [Tooltip("A = solo, B = team of two.")]
        public StudyCondition condition = StudyCondition.A;

        [Tooltip("Which case is played in this run.")]
        public CaseId caseId = CaseId.X;

        [Tooltip("Session ends automatically (not completed) after this many minutes.")]
        [Min(0.1f)]
        public float timeCapMinutes = DefaultTimeCapMinutes;

        public double TimeCapSeconds => timeCapMinutes * 60.0;

        public string ConditionCode => condition.ToString();
        public string CaseCode => caseId.ToString();

        public ExperimentConfig Clone()
        {
            return (ExperimentConfig)MemberwiseClone();
        }

        /// <summary>Returns false (with a reason) if the config must not be used to start a run.</summary>
        public bool Validate(out string error)
        {
            if (!IsValidCode(pairCode)) { error = "Pair code must be 1-16 letters/digits/'-'."; return false; }
            if (!IsValidCode(participantId)) { error = "Participant id must be 1-16 letters/digits/'-'."; return false; }
            if (timeCapMinutes <= 0f) { error = "Time cap must be positive."; return false; }
            error = null;
            return true;
        }

        /// <summary>
        /// {pairCode}_{participantId}_{condition}_{case}_{yyyyMMdd_HHmmss}.csv
        /// </summary>
        public string BuildFileName(DateTime timestamp, string suffix = null)
        {
            var sb = new StringBuilder(64);
            sb.Append(SanitizeCode(pairCode)).Append('_')
              .Append(SanitizeCode(participantId)).Append('_')
              .Append(ConditionCode).Append('_')
              .Append(CaseCode).Append('_')
              .Append(timestamp.ToString(FileTimestampFormat, CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(suffix)) sb.Append('_').Append(suffix);
            sb.Append(".csv");
            return sb.ToString();
        }

        public static bool IsValidCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 16) return false;
            foreach (char c in code)
            {
                if (!IsAllowedCodeChar(c)) return false;
            }
            return true;
        }

        /// <summary>Strips anything that is not safe in a filename. Empty input becomes "NA".</summary>
        public static string SanitizeCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return "NA";
            var sb = new StringBuilder(code.Length);
            foreach (char c in code.Trim())
            {
                if (IsAllowedCodeChar(c)) sb.Append(c);
            }
            return sb.Length == 0 ? "NA" : sb.ToString();
        }

        /// <summary>
        /// Adds <paramref name="delta"/> to the trailing number of a code, keeping its
        /// prefix and zero-padding: "P01" + 1 = "P02", "G09" + 1 = "G10". Never below 0.
        /// A code with no trailing digits gets "01" appended.
        /// </summary>
        public static string StepCode(string code, int delta)
        {
            code = SanitizeCode(code);
            int digitStart = code.Length;
            while (digitStart > 0 && char.IsDigit(code[digitStart - 1])) digitStart--;

            string prefix = code.Substring(0, digitStart);
            string digits = code.Substring(digitStart);
            if (digits.Length == 0) return prefix + "01";

            int value = int.Parse(digits, CultureInfo.InvariantCulture) + delta;
            if (value < 0) value = 0;
            return prefix + value.ToString(CultureInfo.InvariantCulture).PadLeft(digits.Length, '0');
        }

        private static bool IsAllowedCodeChar(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
        }
    }
}
