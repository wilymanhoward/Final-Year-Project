using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FYP.Detective
{
    /// <summary>
    /// Experimenter controls: set codes/condition/case, start, abort, reset - no rebuild.
    /// Three ways to use it:
    ///  1. On-screen IMGUI window (Editor, Quest Link, or the desktop mirror). Not
    ///     visible inside a standalone headset build.
    ///  2. Keyboard shortcuts (Editor/Link): F5 start, F6 abort, F7 reset, F8 show/hide window.
    ///  3. World-space buttons in the headset: wire Button.onClick to the public methods
    ///     (ToggleCondition, ToggleCase, NextParticipant, StartSession, ...) and
    ///     <see cref="onStatusChanged"/> to a TMP_Text.text. Hide that panel from
    ///     participants once the run starts.
    /// Codes can also be preset by pushing experiment_config.json (see ExperimentConfigStore).
    /// </summary>
    public class ExperimenterPanel : MonoBehaviour
    {
        [SerializeField] private SessionManager session;
        [SerializeField] private bool showOnScreenWindow = true;
        [SerializeField] private bool enableKeyboardShortcuts = true;

        [Tooltip("Status text for a world-space label, refreshed twice a second.")]
        public UnityEvent<string> onStatusChanged = new UnityEvent<string>();

        private const float StatusInterval = 0.5f;
        private float _nextStatusTime;
        private Rect _windowRect = new Rect(10, 10, 330, 330);
        private string _timeCapText;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void Update()
        {
            HandleKeyboard();
            if (Time.unscaledTime >= _nextStatusTime)
            {
                _nextStatusTime = Time.unscaledTime + StatusInterval;
                onStatusChanged.Invoke(BuildStatus());
            }
        }

        // ---- Public actions (for world-space buttons) ----

        public void StartSession()
        {
            if (session != null) session.StartSession();
        }

        public void AbortSession()
        {
            if (session != null) session.EndSession(SessionEndReason.Aborted);
        }

        public void ResetSession()
        {
            if (session != null) session.ResetSession();
        }

        public void ToggleCondition() => EditConfig(c => c.condition = c.condition == StudyCondition.A ? StudyCondition.B : StudyCondition.A);
        public void ToggleCase() => EditConfig(c => c.caseId = c.caseId == CaseId.X ? CaseId.Y : CaseId.X);
        public void NextParticipant() => EditConfig(c => c.participantId = ExperimentConfig.StepCode(c.participantId, 1));
        public void PreviousParticipant() => EditConfig(c => c.participantId = ExperimentConfig.StepCode(c.participantId, -1));
        public void NextPair() => EditConfig(c => c.pairCode = ExperimentConfig.StepCode(c.pairCode, 1));
        public void PreviousPair() => EditConfig(c => c.pairCode = ExperimentConfig.StepCode(c.pairCode, -1));

        public void SetWindowVisible(bool visible) => showOnScreenWindow = visible;

        /// <summary>Config changes are only allowed between runs.</summary>
        private void EditConfig(System.Action<ExperimentConfig> edit)
        {
            if (session == null || session.IsRunning) return;
            var copy = session.Config.Clone();
            edit(copy);
            session.ApplyConfig(copy);
            onStatusChanged.Invoke(BuildStatus());
        }

        public string BuildStatus()
        {
            if (session == null) return "No SessionManager";
            var c = session.Config;
            var s = session.State;
            string phase = s.Phase == SessionPhase.Ended ? "Ended (" + s.EndReason + ")" : s.Phase.ToString();
            if (!s.IsRunning && !string.IsNullOrEmpty(session.LastStartError))
                phase += " - not started: " + session.LastStartError;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} | {1} | Cond {2} | Case {3}\n{4}  {5} / {6}\nClues {7} found, {8} tagged | Contam {9} | Reason {10}",
                c.pairCode, c.participantId, c.ConditionCode, c.CaseCode,
                phase, FormatTime(session.ElapsedSeconds), FormatTime(c.TimeCapSeconds),
                s.FoundClues.Count, s.TaggedClues.Count, s.ContaminationErrors, s.ReasoningErrors);
        }

        private static string FormatTime(double seconds)
        {
            int total = (int)seconds;
            return (total / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        private void HandleKeyboard()
        {
#if ENABLE_INPUT_SYSTEM
            if (!enableKeyboardShortcuts) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f5Key.wasPressedThisFrame) StartSession();
            if (kb.f6Key.wasPressedThisFrame) AbortSession();
            if (kb.f7Key.wasPressedThisFrame) ResetSession();
            if (kb.f8Key.wasPressedThisFrame) showOnScreenWindow = !showOnScreenWindow;
#endif
        }

        // ---- IMGUI window (debug / desktop only) ----

        private void OnGUI()
        {
            if (!showOnScreenWindow || session == null) return;
            _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, "Experimenter");
        }

        private void DrawWindow(int id)
        {
            bool running = session.IsRunning;
            var c = session.Config.Clone();

            GUI.enabled = !running;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pair", GUILayout.Width(80));
            c.pairCode = GUILayout.TextField(c.pairCode ?? "", 16);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Participant", GUILayout.Width(80));
            c.participantId = GUILayout.TextField(c.participantId ?? "", 16);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Condition", GUILayout.Width(80));
            c.condition = (StudyCondition)GUILayout.Toolbar((int)c.condition, new[] { "A solo", "B team" });
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Case", GUILayout.Width(80));
            c.caseId = (CaseId)GUILayout.Toolbar((int)c.caseId, new[] { "X", "Y" });
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Cap (min)", GUILayout.Width(80));
            if (_timeCapText == null) _timeCapText = c.timeCapMinutes.ToString(CultureInfo.InvariantCulture);
            _timeCapText = GUILayout.TextField(_timeCapText, 6);
            if (float.TryParse(_timeCapText, NumberStyles.Float, CultureInfo.InvariantCulture, out float cap) && cap > 0f)
                c.timeCapMinutes = cap;
            GUILayout.EndHorizontal();

            if (!running && GUI.changed && HasChanged(c, session.Config)) session.ApplyConfig(c);
            GUI.enabled = true;

            GUILayout.Space(6);
            GUILayout.Label(BuildStatus());
            if (!c.Validate(out string error)) GUILayout.Label("<color=red>" + error + "</color>");

            GUILayout.BeginHorizontal();
            GUI.enabled = !running;
            if (GUILayout.Button("Start (F5)")) StartSession();
            GUI.enabled = running;
            if (GUILayout.Button("Abort (F6)")) AbortSession();
            GUI.enabled = true;
            if (GUILayout.Button("Reset (F7)")) ResetSession();
            GUILayout.EndHorizontal();

            GUILayout.Label("Data: " + Application.persistentDataPath);
            GUI.DragWindow();
        }

        private static bool HasChanged(ExperimentConfig a, ExperimentConfig b)
        {
            return a.pairCode != b.pairCode || a.participantId != b.participantId || a.condition != b.condition
                || a.caseId != b.caseId || !Mathf.Approximately(a.timeCapMinutes, b.timeCapMinutes);
        }
    }
}
