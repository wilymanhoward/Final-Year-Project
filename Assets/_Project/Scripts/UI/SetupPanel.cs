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

        public void Abort()
        {
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
            if (statusText == null || !IsOpen) return;
            _sb.Clear();
            _sb.Append(panel != null ? panel.BuildStatus() : "No ExperimenterPanel").Append('\n');
            if (session != null)
            {
                _sb.Append(session.IsArmed ? "ARMED - waiting for the participant's Start" : "Not armed")
                   .Append(session.IsTeamMode ? "  |  partner connected (team, condition B)" : "  |  solo (condition A)")
                   .Append('\n');
            }
            _sb.Append("Placement: ").Append(_placementStatus).Append('\n');
            _sb.Append("Network: ").Append(_networkStatus);
            statusText.text = _sb.ToString();
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
