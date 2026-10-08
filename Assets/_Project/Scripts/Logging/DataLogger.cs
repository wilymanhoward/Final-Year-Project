using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Writes the continuous 10 Hz log plus discrete events to
    /// persistentDataPath/{pair}_{participant}_{cond}_{case}_{yyyyMMdd_HHmmss}.csv,
    /// one summary row to the same name with "_summary" appended, and a "_meta.json"
    /// (<see cref="SessionMeta"/>) written at session start and rewritten at session end.
    /// Positions and head rotation are relative to <see cref="CaseRoot"/> when one is assigned.
    /// A file is opened on session start and closed on session end.
    /// Flushes to disk every <see cref="flushIntervalSeconds"/>, on every session
    /// event, and when the app is paused (headset taken off) or quits.
    /// </summary>
    public class DataLogger : MonoBehaviour
    {
        [SerializeField] private SessionManager session;
        [SerializeField] private PlayerRig rig;

        [Tooltip("Positions are logged relative to this. Empty = world space.")]
        [SerializeField] private CaseRoot caseRoot;

        [Tooltip("Continuous samples per second.")]
        [SerializeField, Min(1f)] private float sampleRateHz = 10f;

        [Tooltip("Seconds between disk flushes. Lower = less data lost on crash.")]
        [SerializeField, Min(0.1f)] private float flushIntervalSeconds = 1f;

        [Tooltip("Seconds between refreshes of the summary/meta snapshot (end reason \"Interrupted\") while running.")]
        [SerializeField, Min(1f)] private float snapshotIntervalSeconds = 5f;

        private CsvLogWriter _writer;
        private readonly StringBuilder _rowBuilder = new StringBuilder(256);
        private double _nextSampleTime;
        private double _nextFlushTime;
        private double _nextSnapshotTime;
        private ExperimentConfig _config;   // snapshot taken at session start
        private string _metaPath;

        public string CurrentFilePath => _writer?.FilePath;
        public static string OutputDirectory => Application.persistentDataPath;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (rig == null) rig = FindFirstObjectByType<PlayerRig>();
            if (caseRoot == null) caseRoot = FindFirstObjectByType<CaseRoot>();
        }

        private void OnEnable()
        {
            if (session == null) return;
            session.SessionStarted += HandleSessionStarted;
            session.SessionEnded += HandleSessionEnded;
            session.AppPaused += HandleAppPaused;
            session.AppResumed += HandleAppResumed;
            var s = session.State;
            s.ClueFound += HandleClueFound;
            s.ClueTagged += HandleClueTagged;
            s.ClueUntagged += HandleClueUntagged;
            s.ContaminationRecorded += HandleContamination;
            s.AnswerSubmitted += HandleAnswerSubmitted;
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.SessionStarted -= HandleSessionStarted;
                session.SessionEnded -= HandleSessionEnded;
                session.AppPaused -= HandleAppPaused;
                session.AppResumed -= HandleAppResumed;
                var s = session.State;
                s.ClueFound -= HandleClueFound;
                s.ClueTagged -= HandleClueTagged;
                s.ClueUntagged -= HandleClueUntagged;
                s.ContaminationRecorded -= HandleContamination;
                s.AnswerSubmitted -= HandleAnswerSubmitted;
            }
            CloseFile();
        }

        private void Update()
        {
            if (_writer == null) return;
            double now = SessionManager.Now;
            double interval = 1.0 / sampleRateHz;

            if (now >= _nextSampleTime)
            {
                WriteRow(LogRow.TypeSample, null, null);
                _nextSampleTime += interval;
                // After a hitch, don't burst-write missed samples; resume the grid from now.
                if (_nextSampleTime < now) _nextSampleTime = now + interval;
            }

            if (now >= _nextFlushTime)
            {
                _writer.Flush();
                _nextFlushTime = now + flushIntervalSeconds;
            }

            if (now >= _nextSnapshotTime)
            {
                WriteSnapshot();
                _nextSnapshotTime = now + snapshotIntervalSeconds;
            }
        }

        /// <summary>Writes a discrete event row (with the current pose) and flushes.</summary>
        public void LogEvent(string eventName, string detail = null)
        {
            if (_writer == null) return;
            WriteRow(LogRow.TypeEvent, eventName, detail);
            _writer.Flush();
        }

        private void WriteRow(string rowType, string eventName, string detail)
        {
            PoseSample pose = rig != null ? rig.Sample() : default;
            if (caseRoot != null) pose = caseRoot.ToLocal(pose);
            var row = new LogRow
            {
                SessionTime = session.ElapsedSeconds,
                UtcTime = DateTime.UtcNow,
                RowType = rowType,
                EventName = eventName,
                EventDetail = detail,
                FrameDeltaMs = Time.unscaledDeltaTime * 1000f,
                Pose = pose,
            };
            _writer.WriteLine(CsvFormatter.FormatRow(row, _config, _rowBuilder));
        }

        // ---- Session lifecycle ----

        private void HandleSessionStarted()
        {
            CloseFile();
            _config = session.Config.Clone();
            string fileName = _config.BuildFileName(DateTime.Now);
            try
            {
                _writer = new CsvLogWriter(CsvLogWriter.GetUniquePath(OutputDirectory, fileName));
            }
            catch (Exception e)
            {
                Debug.LogError("[DataLogger] Could not create log file: " + e.Message);
                _writer = null;
                return;
            }
            _writer.WriteLine(CsvFormatter.Header);
            string path = _writer.FilePath;
            _metaPath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_meta.json");
            double now = SessionManager.Now;
            _nextSampleTime = now;
            _nextFlushTime = now + flushIntervalSeconds;
            var caseDef = session.ActiveCase;
            var placement = session.Placement;
            LogEvent(LogEvents.SessionStart, FormattableString.Invariant(
                $"time_cap_s={_config.TimeCapSeconds:F0};case_title={(caseDef != null ? caseDef.Title : "")};frame={PositionFrame};placement={placement.MethodCode}"));
            // Placement happens before the run (no file is open then), so it is recorded here;
            // at_utc in the detail is when it actually happened.
            if (placement.Method != PlacementMethod.None) LogEvent(placement.EventName, placement.FormatDetail());
            WriteSnapshot();
            _nextSnapshotTime = now + snapshotIntervalSeconds;
            Debug.Log("[DataLogger] Logging to " + path);
        }

        /// <summary>
        /// Summary + meta with the latest state and end reason "Interrupted", so a run that is killed or crashes
        /// still has both files. Overwritten with the real end reason when the session ends.
        /// </summary>
        private void WriteSnapshot()
        {
            if (_writer == null || !session.State.IsRunning) return;
            WriteSummary(SessionMeta.InterruptedReason);
            WriteMeta(endUtc: null);
        }

        private void HandleAppPaused()
        {
            var s = session.State;
            LogEvent(LogEvents.AppPaused, FormattableString.Invariant($"pause_count={s.PauseCount};paused_s={s.PausedSeconds:F3}"));
            WriteSnapshot();
        }

        private void HandleAppResumed()
        {
            var s = session.State;
            LogEvent(LogEvents.AppResumed, FormattableString.Invariant($"pause_count={s.PauseCount};paused_s={s.PausedSeconds:F3}"));
            WriteSnapshot();
            // Restart the sample grid instead of writing the samples missed during the pause.
            _nextSampleTime = SessionManager.Now;
        }

        private string PositionFrame => caseRoot != null ? "CaseRoot" : "world";

        /// <summary>Writes the meta JSON. endUtc null = snapshot of a running session.</summary>
        private void WriteMeta(DateTime? endUtc)
        {
            if (string.IsNullOrEmpty(_metaPath)) return;
            try
            {
                var caseDef = session.ActiveCase;
                var build = new BuildInfo
                {
                    BuildGuid = Application.buildGUID,
                    AppVersion = Application.version,
                    UnityVersion = Application.unityVersion,
                    DeviceModel = SystemInfo.deviceModel,
                    OperatingSystem = SystemInfo.operatingSystem,
                };
                var meta = SessionMetaBuilder.Build(session.State, _config,
                    caseDef != null ? caseDef.Title : "", caseDef != null ? caseDef.KeyEvidenceIds.Count : 0,
                    session.Placement, build, caseRoot != null, session.StartUtc, endUtc,
                    _writer != null ? Path.GetFileName(_writer.FilePath) : "");
                File.WriteAllText(_metaPath, JsonUtility.ToJson(meta, true));
            }
            catch (Exception e)
            {
                Debug.LogError("[DataLogger] Could not write meta file: " + e.Message);
            }
        }

        private void HandleSessionEnded(SessionEndReason reason)
        {
            if (_writer == null) return;
            var s = session.State;
            LogEvent(LogEvents.SessionEnd, FormattableString.Invariant(
                $"reason={reason};completed={(s.Completed ? 1 : 0)};elapsed_s={s.ElapsedSeconds:F3};active_s={s.ActiveSeconds:F3};paused_s={s.PausedSeconds:F3};pauses={s.PauseCount};contamination={s.ContaminationErrors};reasoning={s.ReasoningErrors}"));
            WriteSummary(null);
            WriteMeta(session.EndUtc);
            CloseFile();
            _metaPath = null;
        }

        private void WriteSummary(string endReasonOverride)
        {
            string path = _writer.FilePath;
            string summaryPath = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + "_summary.csv");
            try
            {
                var caseDef = session.ActiveCase;
                File.WriteAllText(summaryPath,
                    CsvFormatter.SummaryHeader + "\n" +
                    CsvFormatter.FormatSummary(session.State, _config, caseDef != null ? caseDef.Title : "", session.StartUtc,
                        endReasonOverride) + "\n");
            }
            catch (Exception e)
            {
                Debug.LogError("[DataLogger] Could not write summary: " + e.Message);
            }
        }

        private void CloseFile()
        {
            if (_writer == null) return;
            try { _writer.Dispose(); }
            catch (Exception e) { Debug.LogError("[DataLogger] Error closing log: " + e.Message); }
            _writer = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _writer != null) _writer.Flush();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && _writer != null) _writer.Flush();
        }

        // ---- CaseState events ----

        private void HandleClueFound(string clueId) => LogEvent(LogEvents.ClueFound, clueId);
        private void HandleClueTagged(string clueId) => LogEvent(LogEvents.ClueTagged, clueId);
        private void HandleClueUntagged(string clueId) => LogEvent(LogEvents.ClueUntagged, clueId);

        private void HandleContamination(ContaminationInfo info)
        {
            LogEvent(LogEvents.ErrorContamination, $"zone={info.ZoneId};part={info.BodyPart}");
        }

        private void HandleAnswerSubmitted(AnswerResult result)
        {
            var s = session.State;
            LogEvent(LogEvents.AnswerSubmitted,
                $"suspect={s.SubmittedSuspectId};evidence={CsvFormatter.JoinIds(s.SubmittedEvidenceIds)};correct={(result.IsCorrect ? 1 : 0)}");

            if (!result.SuspectCorrect)
                LogEvent(LogEvents.ErrorReasoning, "type=wrong_suspect;suspect=" + s.SubmittedSuspectId);
            foreach (var id in result.WrongEvidenceIds)
                LogEvent(LogEvents.ErrorReasoning, "type=wrong_evidence;evidence=" + id);
        }
    }
}
