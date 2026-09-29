using System;
using System.Collections.Generic;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Owns the <see cref="CaseState"/> and the session timer. Start/stop/reset,
    /// time cap, answer submission. Other components only read state and call
    /// the public methods here or on <see cref="State"/>.
    /// Put exactly one in the scene (e.g. on a "SessionSystems" GameObject).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SessionManager : MonoBehaviour
    {
        [SerializeField] private ExperimentConfig config = new ExperimentConfig();
        [SerializeField] private CaseDefinition caseX;
        [SerializeField] private CaseDefinition caseY;

        [Tooltip("Load the last used config from persistentDataPath on start (overrides the Inspector values).")]
        [SerializeField] private bool loadSavedConfig = true;

        private SessionTimer _timer;

        public CaseState State { get; } = new CaseState();
        public ExperimentConfig Config => config;
        public CaseDefinition ActiveCase => config.caseId == CaseId.X ? caseX : caseY;
        public bool IsRunning => State.IsRunning;
        public DateTime StartUtc { get; private set; }

        /// <summary>Seconds since session start, live while running and frozen after it ends.</summary>
        public double ElapsedSeconds => _timer != null ? _timer.Elapsed(Now) : 0;

        /// <summary>Clock used for all session timing (unaffected by Time.timeScale).</summary>
        public static double Now => Time.realtimeSinceStartupAsDouble;

        public event Action SessionStarted;
        public event Action<SessionEndReason> SessionEnded;
        public event Action SessionReset;
        public event Action ConfigChanged;

        private void Awake()
        {
            if (loadSavedConfig) ExperimentConfigStore.TryLoadInto(config);
            _timer = new SessionTimer(config.TimeCapSeconds);
        }

        private void Update()
        {
            if (!State.IsRunning) return;
            double now = Now;
            State.SetElapsed(_timer.Elapsed(now));
            if (_timer.IsCapReached(now)) EndSession(SessionEndReason.TimeCap);
        }

        /// <summary>Replaces the config (only while no session is running) and saves it.</summary>
        public bool ApplyConfig(ExperimentConfig newConfig)
        {
            if (State.IsRunning || newConfig == null) return false;
            config = newConfig.Clone();
            ExperimentConfigStore.Save(config);
            ConfigChanged?.Invoke();
            return true;
        }

        /// <summary>Experimenter trigger. Returns false (and logs why) if it cannot start.</summary>
        public bool StartSession()
        {
            if (State.IsRunning)
            {
                Debug.LogWarning("[SessionManager] Session already running.");
                return false;
            }
            if (!config.Validate(out string error))
            {
                Debug.LogError("[SessionManager] Invalid config: " + error);
                return false;
            }
            if (ActiveCase == null)
            {
                Debug.LogError($"[SessionManager] No CaseDefinition assigned for case {config.CaseCode}.");
                return false;
            }

            ExperimentConfigStore.Save(config);
            _timer.CapSeconds = config.TimeCapSeconds;
            _timer.Start(Now);
            StartUtc = DateTime.UtcNow;
            State.Begin();
            Debug.Log($"[SessionManager] Session started: {config.pairCode} {config.participantId} {config.ConditionCode} {config.CaseCode}");
            SessionStarted?.Invoke();
            return true;
        }

        /// <summary>Stops the timer and ends the run. Safe to call when not running.</summary>
        public void EndSession(SessionEndReason reason)
        {
            if (!State.IsRunning) return;
            double elapsed = _timer.Stop(Now);
            State.End(reason, elapsed);
            Debug.Log($"[SessionManager] Session ended: {reason}, {elapsed:F1}s");
            SessionEnded?.Invoke(reason);
        }

        /// <summary>Records a checked answer and ends the session (timer stops on submit).</summary>
        public bool SubmitAnswer(string suspectId, IReadOnlyList<string> evidenceIds, AnswerResult result)
        {
            if (!State.RecordSubmission(suspectId, evidenceIds, result)) return false;
            EndSession(SessionEndReason.Submitted);
            return true;
        }

        /// <summary>Aborts a running session (logged as Aborted) and clears state for the next run.</summary>
        public void ResetSession()
        {
            EndSession(SessionEndReason.Aborted);
            _timer.Reset();
            State.Reset();
            SessionReset?.Invoke();
        }

        private void OnApplicationQuit()
        {
            EndSession(SessionEndReason.Aborted);
        }
    }
}
