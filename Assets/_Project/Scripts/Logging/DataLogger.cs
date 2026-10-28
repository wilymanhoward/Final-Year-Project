using System;
using System.Collections.Generic;
using System.Globalization;
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
        private double _pendingPlacementCheckAt = -1;   // re-check the height shortly after a recenter
        private readonly TrackingEventFilter _leftTrackingFilter = new TrackingEventFilter();
        private readonly TrackingEventFilter _rightTrackingFilter = new TrackingEventFilter();
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
            session.HeadsetRemoved += HandleHeadsetRemoved;
            session.HeadsetPutOn += HandleHeadsetPutOn;
            session.TrackingRecentered += HandleTrackingRecentered;
            session.DepthOcclusionChanged += HandleDepthOcclusionChanged;
            var s = session.State;
            s.ClueFound += HandleClueFound;
            s.ClueTagged += HandleClueTagged;
            s.ClueUntagged += HandleClueUntagged;
            s.ContaminationRecorded += HandleContamination;
            s.AnswerSubmitted += HandleAnswerSubmitted;
            s.ClueReset += HandleClueReset;
            s.ClueThrown += HandleClueThrown;
            s.PartnerContaminationRecorded += HandlePartnerContamination;
            s.ClueGrabTaken += HandleClueGrabTaken;
            s.BoardSuspectSelected += HandleBoardSuspect;
            s.BoardEvidenceChanged += HandleBoardEvidence;
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.SessionStarted -= HandleSessionStarted;
                session.SessionEnded -= HandleSessionEnded;
                session.AppPaused -= HandleAppPaused;
                session.AppResumed -= HandleAppResumed;
                session.HeadsetRemoved -= HandleHeadsetRemoved;
                session.HeadsetPutOn -= HandleHeadsetPutOn;
                session.TrackingRecentered -= HandleTrackingRecentered;
                session.DepthOcclusionChanged -= HandleDepthOcclusionChanged;
                var s = session.State;
                s.ClueFound -= HandleClueFound;
                s.ClueTagged -= HandleClueTagged;
                s.ClueUntagged -= HandleClueUntagged;
                s.ContaminationRecorded -= HandleContamination;
                s.AnswerSubmitted -= HandleAnswerSubmitted;
                s.ClueReset -= HandleClueReset;
                s.ClueThrown -= HandleClueThrown;
                s.PartnerContaminationRecorded -= HandlePartnerContamination;
                s.ClueGrabTaken -= HandleClueGrabTaken;
                s.BoardSuspectSelected -= HandleBoardSuspect;
                s.BoardEvidenceChanged -= HandleBoardEvidence;
            }
            CloseFile();
        }

        private void Update()
        {
            if (_writer == null) return;
            double now = SessionManager.Now;
            double interval = 1.0 / sampleRateHz;

            UpdateTrackingEvents();
            if (_pendingPlacementCheckAt > 0 && now >= _pendingPlacementCheckAt)
            {
                _pendingPlacementCheckAt = -1;
                LogPlacementCheck("after_recenter");
            }

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
            _leftTrackingFilter.Reset();
            _rightTrackingFilter.Reset();
            var caseDef = session.ActiveCase;
            var placement = session.Placement;
            LogEvent(LogEvents.SessionStart, FormattableString.Invariant(
                $"time_cap_s={_config.TimeCapSeconds:F0};case_title={(caseDef != null ? caseDef.Title : "")};frame={PositionFrame};placement={placement.MethodCode}")
                + ";start_source=" + SessionManager.StartSourceCode(session.LastStartSource)
                + ";zone_point=" + FootEstimate.RuleCode
                + (session.State.IsTeamRun ? ";team=1;self=" + session.State.SelfId : ""));
            // Arming happens before the file is open (like placement), so it is recorded here with its own time.
            if (session.RunArmedUtc != DateTime.MinValue)
                LogEvent(LogEvents.RunArmed, "at_utc=" + PlacementRecord.FormatUtc(session.RunArmedUtc));
            // Placement happens before the run (no file is open then), so it is recorded here;
            // at_utc in the detail is when it actually happened.
            if (placement.Method != PlacementMethod.None) LogEvent(placement.EventName, placement.FormatDetail());
            _pendingPlacementCheckAt = -1;
            LogPlacementCheck("session_start");
            LogDepthOcclusion("session_start");
            LogBriefing();
            LogBoardOrder();
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

        // Headset off: the app keeps running, so 10 Hz samples continue; only the event rows mark the gap.
        private void HandleHeadsetRemoved()
        {
            var s = session.State;
            LogEvent(LogEvents.HmdUnmounted, FormattableString.Invariant($"pause_count={s.PauseCount};paused_s={s.PausedSeconds:F3}"));
            WriteSnapshot();
        }

        private void HandleHeadsetPutOn()
        {
            var s = session.State;
            LogEvent(LogEvents.HmdMounted, FormattableString.Invariant($"pause_count={s.PauseCount};paused_s={s.PausedSeconds:F3}"));
            WriteSnapshot();
        }

        // Left-to-right order of the suspect cards on the evidence board (shuffled per pair/case/condition, SuspectOrder), so
        // card position can be analysed against the culprit and the chosen suspect.
        private void LogBoardOrder()
        {
            var def = session.ActiveCase;
            if (_writer == null || def == null) return;
            var order = SuspectOrder.Shuffled(def.Suspects, _config.pairCode, _config.CaseCode, _config.ConditionCode);
            var ids = new List<string>();
            foreach (SuspectInfo s in order) if (s != null) ids.Add(s.id);
            LogEvent(LogEvents.BoardOrder, "left_to_right=" + string.Join("|", ids));
        }

        // The detective's briefing happens before the run (no file open), so it is summarised at session start:
        // when it began and finished (UTC), replays, whether this headset heard it to the end (and the partner, team).
        private void LogBriefing()
        {
            if (_writer == null || !session.BriefingRequired) return;
            string Utc(DateTime t) => t == DateTime.MinValue ? "" : PlacementRecord.FormatUtc(t);
            LogEvent(LogEvents.Briefing, "started_utc=" + Utc(session.BriefingStartedUtc) + ";finished_utc=" + Utc(session.BriefingFinishedUtc)
                + ";replays=" + session.BriefingReplays.ToString(CultureInfo.InvariantCulture)
                + ";heard=" + (session.LocalBriefingDone ? "1" : "0")
                + (session.State.IsTeamRun ? ";partner_heard=" + (session.PartnerBriefingDone ? "1" : "0") : ""));
        }

        // Depth occlusion (Meta Depth API) status at session start and on every change during the run.
        private void HandleDepthOcclusionChanged(DepthOcclusionStatus status) => LogDepthOcclusion("change");

        private void LogDepthOcclusion(string when)
        {
            if (_writer == null) return;
            LogEvent(LogEvents.DepthOcclusion, "when=" + when + ";status=" + SessionManager.DepthOcclusionCode(session.DepthOcclusion));
        }

        // A recenter changes the tracking space; the anchor pose settles over the next frames, so the height is
        // logged again 1 s later.
        private void HandleTrackingRecentered()
        {
            if (_writer == null) return;
            LogEvent(LogEvents.TrackingRecentered, "count=" + session.RecenterCount.ToString(CultureInfo.InvariantCulture));
            _pendingPlacementCheckAt = SessionManager.Now + 1.0;
        }

        private void LogPlacementCheck(string when)
        {
            // head_m = head height above the scene floor (CaseRoot); ~1.5-1.8 m when standing, else the floor was off.
            string head = rig != null && rig.Head != null && caseRoot != null
                ? (rig.Head.position.y - caseRoot.transform.position.y).ToString("F3", CultureInfo.InvariantCulture) : "";
            LogEvent(LogEvents.PlacementCheck, FormattableString.Invariant(
                $"when={when};height_m={session.PlacementHeightOffset:F3};ok={(session.IsPlacementHeightOk ? 1 : 0)};recenters={session.RecenterCount};head_m={head}"));
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
                meta.startSource = SessionManager.StartSourceCode(session.LastStartSource);
                meta.armedUtc = PlacementRecord.FormatUtc(session.RunArmedUtc);
                meta.zonePoint = FootEstimate.RuleCode;
                CsvLogWriter.WriteAllTextSafely(_metaPath, JsonUtility.ToJson(meta, true));
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
                CsvLogWriter.WriteAllTextSafely(summaryPath,
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

        // Team runs append ";by=<participantId>" (who caused it); solo rows are unchanged.
        private void HandleClueFound(string clueId) => LogEvent(LogEvents.ClueFound, clueId + ByField());
        private void HandleClueTagged(string clueId) => LogEvent(LogEvents.ClueTagged, clueId + ByField());
        private void HandleClueUntagged(string clueId) => LogEvent(LogEvents.ClueUntagged, clueId + ByField());
        private void HandleClueReset(string clueId) => LogEvent(LogEvents.ClueReset, clueId + ResetReasonField() + ByField());
        private void HandleClueThrown(string clueId) => LogEvent(LogEvents.ClueThrown, clueId + FormattableString.Invariant(
            $";speed={session.State.LastThrowSpeed:F2};capped={(session.State.LastThrowCapped ? 1 : 0)}") + ByField());
        private void HandleClueGrabTaken(string clueId) => LogEvent(LogEvents.ClueGrabTaken, clueId + ByField());

        private void HandleBoardSuspect(string suspectId) =>
            LogEvent(LogEvents.BoardSuspect, "suspect=" + (suspectId ?? "") + ByField());

        private void HandleBoardEvidence(string clueId, bool selected) =>
            LogEvent(LogEvents.BoardEvidence, "clue=" + clueId + ";selected=" + (selected ? "1" : "0") + ByField());

        private string ResetReasonField()
        {
            string reason = session.State.LastResetReason;
            return reason == null ? string.Empty : ";reason=" + reason;
        }

        private string ByField()
        {
            string by = session.State.LastActorId;
            return by == null ? string.Empty : ";by=" + by;
        }

        // Hand-tracking events are debounced (losses shorter than 0.2 s aren't logged); samples and zones use the raw state.
        private void UpdateTrackingEvents()
        {
            if (rig == null) return;
            double t = session.ElapsedSeconds;
            LogTrackingEvent(BodyPart.LeftHand, _leftTrackingFilter.Update(rig.IsTracked(BodyPart.LeftHand), t), _leftTrackingFilter);
            LogTrackingEvent(BodyPart.RightHand, _rightTrackingFilter.Update(rig.IsTracked(BodyPart.RightHand), t), _rightTrackingFilter);
        }

        private void LogTrackingEvent(BodyPart part, TrackingEvent e, TrackingEventFilter filter)
        {
            if (e == TrackingEvent.Lost)
                LogEvent(LogEvents.HandTrackingLost, FormattableString.Invariant($"part={part};lost_since_s={filter.LostSince:F3}"));
            else if (e == TrackingEvent.Regained)
                LogEvent(LogEvents.HandTrackingRegained, "part=" + part);
        }

        private void HandleContamination(ContaminationInfo info)
        {
            LogEvent(LogEvents.ErrorContamination, $"zone={info.ZoneId};part={info.BodyPart}" + ByField());
        }

        private void HandlePartnerContamination(ContaminationInfo info)
        {
            LogEvent(LogEvents.PartnerContamination, $"zone={info.ZoneId};part={info.BodyPart}" + ByField());
        }

        private void HandleAnswerSubmitted(AnswerResult result)
        {
            var s = session.State;
            LogEvent(LogEvents.AnswerSubmitted,
                $"suspect={s.SubmittedSuspectId};evidence={CsvFormatter.JoinIds(s.SubmittedEvidenceIds)};correct={(result.IsCorrect ? 1 : 0)};perfect={(result.IsPerfect ? 1 : 0)}" + ByField());

            if (!result.SuspectCorrect)
                LogEvent(LogEvents.ErrorReasoning, "type=wrong_suspect;suspect=" + s.SubmittedSuspectId);
            foreach (var id in result.WrongEvidenceIds)
                LogEvent(LogEvents.ErrorReasoning, "type=wrong_evidence;evidence=" + id);
        }
    }
}
