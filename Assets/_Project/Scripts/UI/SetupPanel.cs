using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// Experimenter setup panel in the headset (Phase C), built by FYP > Set Up Headset UI. Toggled with the left
    /// controller's menu button (<see cref="Toggle"/>); appears in front of the head and stays put for poking.
    /// Shows codes/case/condition, run state, placement and network status; its buttons are wired by the menu tool
    /// to ExperimenterPanel (codes, case), ScenePlacement (place/load/manual), ColocationCoordinator (host/join/stop)
    /// and to <see cref="Arm"/> / <see cref="Disarm"/> / <see cref="StartNow"/> / <see cref="Abort"/> here.
    /// Hidden automatically while a run is in progress so the participant never sees it.
    /// </summary>
    public class SetupPanel : MonoBehaviour
    {
        [SerializeField] private SessionManager session;
        [SerializeField] private ExperimenterPanel panel;
        [SerializeField] private Transform head;
        [SerializeField] private GameObject content;
        [SerializeField] private Text statusText;
        [Tooltip("Optional: shows whether the run files reached the laptop (found automatically if empty).")]
        [SerializeField] private DataUploader uploader;

        [Header("Sectioned layout (session 6 redesign; empty = the single status text above)")]
        [SerializeField] private Text headerText;
        [SerializeField] private Text chipText;
        [SerializeField] private Image chipImage;
        [SerializeField] private Text participantText;
        [SerializeField] private Text placementText;
        [SerializeField] private Text networkText;
        [SerializeField] private Text runText;
        [SerializeField] private Image placementLight;
        [SerializeField] private Image networkLight;
        [SerializeField] private Image dataLight;
        [SerializeField] private Image runLight;
        [Tooltip("Label of the Occlusion on/off button (text and colour follow the setting).")]
        [SerializeField] private Text occlusionLabel;

        [Header("Guided layout (session 6 redesign: detective desk, NEXT banner, per-value labels)")]
        [Tooltip("The NEXT banner: what the experimenter should do now (SetupStatusModel.NextStep).")]
        [SerializeField] private Text nextStepText;
        [Tooltip("Frame behind each card (0 Participant, 1 Placement, 2 Network, 3 Run); the next step's card is highlighted.")]
        [SerializeField] private Image[] cardFrames = new Image[0];
        [SerializeField] private Text pairValue;
        [SerializeField] private Text participantValue;
        [SerializeField] private Text caseValue;
        [SerializeField] private Text conditionValue;
        [Tooltip("Label of the Abort button: asks for a second tap.")]
        [SerializeField] private Text abortLabel;
        [SerializeField] private Color frameNormal = new Color(0.30f, 0.23f, 0.18f, 1f);
        [SerializeField] private Color frameHighlight = new Color(0.96f, 0.76f, 0.26f, 1f);

        private float _abortConfirmUntil = -1f;
        private const float AbortConfirmSeconds = 3f;
        private const string AbortText = "Abort run";
        private const string AbortConfirmText = "Tap again to abort";
        [Tooltip("Shows a one-time hint at launch that the panel exists (it is hidden until the menu button is pressed).")]
        [SerializeField] private StatusLabel hintLabel;
        [SerializeField, Min(0.2f)] private float distance = 0.5f;
        [SerializeField] private float heightOffset = -0.1f;

        private string _placementStatus = "-";
        private string _networkStatus = "off";
        private float _nextRefresh;
        private readonly StringBuilder _sb = new StringBuilder(256);

        public bool IsOpen => content != null && content.activeSelf;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (panel == null) panel = FindFirstObjectByType<ExperimenterPanel>();
            if (uploader == null) uploader = FindFirstObjectByType<DataUploader>();
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (content != null) content.SetActive(false);
        }

        private void Start()
        {
            if (hintLabel != null) hintLabel.Show("Experimenter: press the MENU button on the LEFT controller for the setup panel", 10f);
        }

        private void OnEnable()
        {
            if (session != null) session.SessionStarted += Close;
        }

        private void OnDisable()
        {
            if (session != null) session.SessionStarted -= Close;
        }

        /// <summary>Left controller menu button: open in front of the head / close.</summary>
        public void Toggle()
        {
            if (content == null) return;
            if (IsOpen) { Close(); return; }
            if (session != null && session.IsRunning) return;   // never during a run
            PlaceInFront();
            content.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (content != null) content.SetActive(false);
        }

        // ---- Status feeds (wired to ScenePlacement / launcher / coordinator status events) ----

        public void SetPlacementStatus(string text) { _placementStatus = text; Refresh(); }
        public void SetNetworkStatus(string text) { _networkStatus = text; Refresh(); }

        // ---- Buttons ----

        /// <summary>Arm the run: the participant's Start card appears. Closes the panel when arming worked.</summary>
        public void Arm()
        {
            if (session == null) return;
            session.RequestArm(true);
            Refresh();
            if (session.IsArmed || session.IsTeamMode) Close();   // team: armed when the host's message arrives
        }

        public void Disarm()
        {
            if (session != null) session.RequestArm(false);
            Refresh();
        }

        /// <summary>Fallback: start at once without the participant's Start.</summary>
        public void StartNow()
        {
            if (session != null) session.RequestStart();
            Refresh();
        }

        /// <summary>Abort needs two taps within 3 s (first tap: "Tap again to abort").</summary>
        public void Abort()
        {
            if (Time.unscaledTime > _abortConfirmUntil)
            {
                _abortConfirmUntil = Time.unscaledTime + AbortConfirmSeconds;
                Refresh();
                return;
            }
            _abortConfirmUntil = -1f;
            if (session != null) session.RequestAbort();
            Refresh();
        }

        private void Update()
        {
            if (!IsOpen || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        private void Refresh()
        {
            if (!IsOpen) return;
            if (headerText != null) { RefreshSections(); return; }
            if (statusText == null) return;
            _sb.Clear();
            _sb.Append(panel != null ? panel.BuildStatus() : "No ExperimenterPanel").Append('\n');
            if (session != null)
            {
                string mismatch = SessionManager.ConditionMismatch(session.Config.condition, session.IsTeamMode);
                _sb.Append(session.IsArmed ? "ARMED - waiting for the participant's Start" : "Not armed")
                   .Append(session.IsTeamMode ? "  |  partner connected" : "  |  no partner")
                   .Append(mismatch != null ? "  |  CAN'T ARM: " + mismatch : "")
                   .Append('\n');
            }
            _sb.Append("Placement: ").Append(_placementStatus).Append('\n');
            _sb.Append("Network: ").Append(_networkStatus);
            if (uploader != null) _sb.Append('\n').Append(uploader.StatusText);
            statusText.text = _sb.ToString();
        }

        // Redesigned panel: header with codes + run-state chip, four sections with traffic lights.
        private void RefreshSections()
        {
            if (session == null) return;
            ExperimentConfig c = session.Config;
            CaseState s = session.State;
            bool running = session.IsRunning, armed = session.IsArmed, ended = s.Phase == SessionPhase.Ended;
            string mismatch = SessionManager.ConditionMismatch(c.condition, session.IsTeamMode);

            headerText.text = c.pairCode + "  ·  " + c.participantId + "  ·  Case " + c.CaseCode + "  ·  Cond " + c.ConditionCode;

            // Guided layout: what to do next, and that step's card highlighted.
            bool placementOk = session.Placement.Method != PlacementMethod.None && session.IsPlacementHeightOk;
            string next = SetupStatusModel.NextStep(running, ended, armed, c.condition, session.IsTeamMode, placementOk, out int nextCard);
            if (nextStepText != null) nextStepText.text = next;
            for (int i = 0; i < cardFrames.Length; i++)
                if (cardFrames[i] != null) cardFrames[i].color = i == nextCard ? frameHighlight : frameNormal;
            var caseDef = session.ActiveCase;
            if (pairValue != null) pairValue.text = c.pairCode;
            if (participantValue != null) participantValue.text = c.participantId;
            if (caseValue != null) caseValue.text = c.CaseCode + (caseDef != null && !string.IsNullOrEmpty(caseDef.Title) ? "  ·  " + caseDef.Title : "");
            if (conditionValue != null) conditionValue.text = c.ConditionCode + (c.condition == StudyCondition.A ? "  ·  Solo" : "  ·  Team of two");
            if (abortLabel != null) abortLabel.text = Time.unscaledTime <= _abortConfirmUntil ? AbortConfirmText : AbortText;
            StatusLight chip = SetupStatusModel.ChipLight(running, armed, ended);
            if (chipText != null) chipText.text = SetupStatusModel.Chip(running, armed, ended, session.ElapsedSeconds);
            if (chipImage != null) chipImage.color = SetupStatusModel.ColorOf(chip);

            if (participantText != null)
            {
                var def = session.ActiveCase;
                participantText.text = "Pair " + c.pairCode + "    Participant " + c.participantId + "\n" +
                    "Case " + c.CaseCode + (def != null && !string.IsNullOrEmpty(def.Title) ? " - " + def.Title : "") + "\n" +
                    "Condition " + c.ConditionCode + (c.condition == StudyCondition.A ? " (solo)" : " (team of two)");
            }

            if (placementText != null)
                placementText.text = _placementStatus == "-" ? "Not placed yet.\nStand on the tape mark facing the front wall, then Place here." : _placementStatus;
            SetLight(placementLight, SetupStatusModel.Placement(session.Placement.Method, session.IsPlacementHeightOk));
            if (occlusionLabel != null)
            {
                occlusionLabel.text = c.occlusionEnabled
                    ? "Occlusion: ON  (" + SessionManager.DepthOcclusionCode(session.DepthOcclusion) + ")"
                    : "Occlusion: OFF";
                var image = occlusionLabel.transform.parent != null ? occlusionLabel.transform.parent.GetComponent<Image>() : null;
                if (image != null) image.color = c.occlusionEnabled ? new Color(0.16f, 0.45f, 0.27f, 1f) : new Color(0.50f, 0.39f, 0.24f, 1f);   // desk green / brass
            }

            if (networkText != null)
            {
                _sb.Clear();
                _sb.Append(session.IsTeamMode ? "Partner connected" : "No partner").Append(" - ").Append(_networkStatus);
                if (uploader != null) _sb.Append('\n').Append(uploader.StatusText);
                networkText.text = _sb.ToString();
            }
            SetLight(networkLight, SetupStatusModel.Network(c.condition, session.IsTeamMode));
            SetLight(dataLight, uploader != null ? SetupStatusModel.Data(uploader.WaitingCount) : StatusLight.Off);

            if (runText != null)
            {
                _sb.Clear();
                _sb.Append(running ? "Running" : ended ? "Ended (" + s.EndReason + ")" : armed ? "Armed - waiting for the participant's Start" : "Not armed")
                   .Append("   ").Append(Clock(session.ElapsedSeconds)).Append(" / ").Append(Clock(c.TimeCapSeconds)).Append('\n')
                   .Append("Clues ").Append(s.FoundClues.Count).Append(" found, ").Append(s.TaggedClues.Count).Append(" tagged   ")
                   .Append("Errors: contam. ").Append(s.ContaminationErrors).Append(", reasoning ").Append(s.ReasoningErrors);
                if (mismatch != null) _sb.Append("\nCAN'T ARM: ").Append(mismatch);
                else if (!running && !string.IsNullOrEmpty(session.LastStartError)) _sb.Append("\nNot started: ").Append(session.LastStartError);
                runText.text = _sb.ToString();
            }
            SetLight(runLight, SetupStatusModel.Run(running, armed, mismatch == null));
        }

        private static void SetLight(Image light, StatusLight state)
        {
            if (light != null) light.color = SetupStatusModel.ColorOf(state);
        }

        private static string Clock(double seconds)
        {
            int t = Mathf.Max(0, (int)seconds);
            return (t / 60).ToString("00") + ":" + (t % 60).ToString("00");
        }

        private void PlaceInFront()
        {
            if (head == null) return;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            transform.SetPositionAndRotation(head.position + forward * distance + Vector3.up * heightOffset,
                Quaternion.LookRotation(forward, Vector3.up));
        }
    }
}
