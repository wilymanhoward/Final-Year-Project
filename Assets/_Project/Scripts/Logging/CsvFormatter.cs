using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Pure CSV formatting (no file IO). Always uses invariant culture so decimals are
    /// written with '.' regardless of the headset's locale. Missing values are empty cells.
    /// </summary>
    public static class CsvFormatter
    {
        public const string Separator = ",";

        /// <summary>Column order of the continuous log. Changing it breaks analysis scripts.</summary>
        public static readonly string[] Columns =
        {
            "session_time_s", "utc_time", "row_type", "event", "detail",
            "pair_code", "participant_id", "condition", "case",
            "head_x", "head_y", "head_z",
            "head_qx", "head_qy", "head_qz", "head_qw",
            "head_yaw_deg", "head_pitch_deg",
            "lhand_x", "lhand_y", "lhand_z",
            "rhand_x", "rhand_y", "rhand_z",
            "frame_dt_ms",
        };

        public static string Header => string.Join(Separator, Columns);

        private const string PosFormat = "F4";   // 0.1 mm
        private const string RotFormat = "F5";
        private const string AngleFormat = "F2";
        private const string TimeFormat = "F3";   // 1 ms
        private const string UtcFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

        /// <summary>Formats one row. Pass a reusable StringBuilder to avoid allocations in the 10 Hz loop.</summary>
        public static string FormatRow(in LogRow row, ExperimentConfig config, StringBuilder sb = null)
        {
            sb = sb ?? new StringBuilder(256);
            sb.Clear();
            var inv = CultureInfo.InvariantCulture;
            var p = row.Pose;

            sb.Append(row.SessionTime.ToString(TimeFormat, inv)).Append(Separator);
            sb.Append(row.UtcTime.ToUniversalTime().ToString(UtcFormat, inv)).Append(Separator);
            sb.Append(Escape(row.RowType)).Append(Separator);
            sb.Append(Escape(row.EventName)).Append(Separator);
            sb.Append(Escape(row.EventDetail)).Append(Separator);

            sb.Append(Escape(config?.pairCode)).Append(Separator);
            sb.Append(Escape(config?.participantId)).Append(Separator);
            sb.Append(config != null ? config.ConditionCode : "").Append(Separator);
            sb.Append(config != null ? config.CaseCode : "").Append(Separator);

            AppendVector(sb, p.HasHead, p.HeadPosition);
            if (p.HasHead)
            {
                var q = p.HeadRotation;
                sb.Append(q.x.ToString(RotFormat, inv)).Append(Separator)
                  .Append(q.y.ToString(RotFormat, inv)).Append(Separator)
                  .Append(q.z.ToString(RotFormat, inv)).Append(Separator)
                  .Append(q.w.ToString(RotFormat, inv)).Append(Separator);
                HeadAngles(q, out float yaw, out float pitch);
                sb.Append(yaw.ToString(AngleFormat, inv)).Append(Separator)
                  .Append(pitch.ToString(AngleFormat, inv)).Append(Separator);
            }
            else
            {
                AppendEmpty(sb, 6);
            }
            AppendVector(sb, p.HasLeftHand, p.LeftHandPosition);
            AppendVector(sb, p.HasRightHand, p.RightHandPosition);

            if (row.FrameDeltaMs > 0f) sb.Append(row.FrameDeltaMs.ToString("F2", inv));
            return sb.ToString();
        }

        /// <summary>RFC 4180 escaping: quote if the value contains a comma, quote or newline.</summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            bool needsQuotes = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0
                || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            if (!needsQuotes) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Yaw (heading around world up, 0 = +Z, positive = towards +X) and pitch
        /// (positive = looking up) of the forward vector of <paramref name="q"/>, in degrees.
        /// Computed by hand so it is independent of Unity's Euler conventions.
        /// </summary>
        public static void HeadAngles(Quaternion q, out float yawDeg, out float pitchDeg)
        {
            // forward = q * (0,0,1)
            double fx = 2.0 * (q.x * q.z + q.w * q.y);
            double fy = 2.0 * (q.y * q.z - q.w * q.x);
            double fz = 1.0 - 2.0 * (q.x * q.x + q.y * q.y);
            yawDeg = (float)(Math.Atan2(fx, fz) * 180.0 / Math.PI);
            double horizontal = Math.Sqrt(fx * fx + fz * fz);
            pitchDeg = (float)(Math.Atan2(fy, horizontal) * 180.0 / Math.PI);
        }

        // ---- Session summary (one row per run, written next to the continuous log) ----

        public static readonly string[] SummaryColumns =
        {
            "pair_code", "participant_id", "condition", "case", "case_title",
            "start_utc", "end_reason", "completed", "answer_correct",
            "elapsed_s", "time_cap_s",
            "clues_found", "clues_tagged",
            "contamination_errors", "reasoning_errors",
            "submitted_suspect", "suspect_correct",
            "submitted_evidence", "wrong_evidence", "missing_evidence",
            "contamination_by_zone",
            "active_s", "paused_s", "pause_count",
        };

        public static string SummaryHeader => string.Join(Separator, SummaryColumns);

        /// <summary>
        /// One summary row. <paramref name="endReasonOverride"/> replaces end_reason, e.g. "Interrupted" for the
        /// snapshot written while a session is still running.
        /// </summary>
        public static string FormatSummary(CaseState state, ExperimentConfig config, string caseTitle, DateTime startUtc,
            string endReasonOverride = null)
        {
            var inv = CultureInfo.InvariantCulture;
            var r = state.LastResult;
            var cells = new[]
            {
                config?.pairCode, config?.participantId, config?.ConditionCode, config?.CaseCode, caseTitle,
                startUtc.ToUniversalTime().ToString(UtcFormat, inv),
                endReasonOverride ?? (state.EndReason.HasValue ? state.EndReason.Value.ToString() : ""),
                Bool(state.Completed),
                Bool(r != null && r.IsCorrect),
                state.ElapsedSeconds.ToString(TimeFormat, inv),
                config != null ? config.TimeCapSeconds.ToString("F0", inv) : "",
                state.FoundClues.Count.ToString(inv),
                state.TaggedClues.Count.ToString(inv),
                state.ContaminationErrors.ToString(inv),
                state.ReasoningErrors.ToString(inv),
                state.SubmittedSuspectId,
                r != null ? Bool(r.SuspectCorrect) : "",
                JoinIds(state.SubmittedEvidenceIds),
                r != null ? JoinIds(r.WrongEvidenceIds) : "",
                r != null ? JoinIds(r.MissingEvidenceIds) : "",
                JoinZoneCounts(state.ContaminationByZone),
                state.ActiveSeconds.ToString(TimeFormat, inv),
                state.PausedSeconds.ToString(TimeFormat, inv),
                state.PauseCount.ToString(inv),
            };
            var sb = new StringBuilder(256);
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(Separator);
                sb.Append(Escape(cells[i]));
            }
            return sb.ToString();
        }

        /// <summary>Joins ids with ';' (sorted) so a list fits in one CSV cell.</summary>
        public static string JoinIds(IEnumerable<string> ids)
        {
            if (ids == null) return string.Empty;
            var list = new List<string>(ids);
            list.Sort(StringComparer.Ordinal);
            return string.Join(";", list);
        }

        private static string JoinZoneCounts(IReadOnlyDictionary<string, int> counts)
        {
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add(kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture));
            parts.Sort(StringComparer.Ordinal);
            return string.Join(";", parts);
        }

        private static string Bool(bool b) => b ? "1" : "0";

        private static void AppendVector(StringBuilder sb, bool has, Vector3 v)
        {
            if (!has) { AppendEmpty(sb, 3); return; }
            var inv = CultureInfo.InvariantCulture;
            sb.Append(v.x.ToString(PosFormat, inv)).Append(Separator)
              .Append(v.y.ToString(PosFormat, inv)).Append(Separator)
              .Append(v.z.ToString(PosFormat, inv)).Append(Separator);
        }

        private static void AppendEmpty(StringBuilder sb, int count)
        {
            for (int i = 0; i < count; i++) sb.Append(Separator);
        }
    }
}
