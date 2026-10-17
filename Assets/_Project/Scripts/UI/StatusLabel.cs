using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// Small world-space message for the experimenter (e.g. placement status). <see cref="Show(string)"/>
    /// places it in front of the head, level and facing the user, and hides it again after
    /// <see cref="visibleSeconds"/>. Put it on the root of a world-space Canvas.
    /// With <see cref="showDebugMessages"/> on, it also shows "Resumed - paused X.X s" when a pause during a
    /// session ends (app paused and/or headset off, overlap counted once). Debug only: turn it OFF for the real study so participants never see it.
    /// Styled like the setup panel by the FYP menu tools (rounded card, "EXPERIMENTER" header, coloured accent bar).
    /// </summary>
    public class StatusLabel : MonoBehaviour
    {
        [SerializeField] private Text text;
        [SerializeField] private Canvas canvas;
        [Tooltip("Optional accent bar coloured by the message (red = problem, amber = in progress, green = ok).")]
        [SerializeField] private Image accent;
        [Tooltip("Usually OVRCameraRig/TrackingSpace/CenterEyeAnchor. Empty = Camera.main.")]
        [SerializeField] private Transform head;
        [Tooltip("Seconds the message stays visible. 0 = until the next message.")]
        [SerializeField, Min(0f)] private float visibleSeconds = 6f;
        [SerializeField, Min(0.3f)] private float distance = 0.8f;
        [SerializeField] private float heightOffset = -0.15f;

        [Header("Debug (turn OFF for the study)")]
        [Tooltip("Show debug messages such as \"Resumed - paused X.X s\". ON for testing, OFF for the real study.")]
        [SerializeField] private bool showDebugMessages = true;
        [SerializeField, Min(0.5f)] private float debugMessageSeconds = 3f;
        [Tooltip("Empty = found in the scene.")]
        [SerializeField] private SessionManager session;

        private float _hideAt = -1f;

        public bool ShowDebugMessages
        {
            get => showDebugMessages;
            set => showDebugMessages = value;
        }

        public string CurrentText => text != null ? text.text : "";

        private void Awake()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (canvas != null) canvas.enabled = false;
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void OnEnable()
        {
            if (session == null) return;
            session.PauseEnded += HandlePauseEnded;
        }

        private void OnDisable()
        {
            if (session == null) return;
            session.PauseEnded -= HandlePauseEnded;
        }

        private void HandlePauseEnded(double pauseSeconds)
        {
            if (!showDebugMessages) return;
            Show("Resumed - paused " + pauseSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s", debugMessageSeconds);
        }

        /// <summary>Shows a message in front of the user (wire to a UnityEvent&lt;string&gt;).</summary>
        public void Show(string message) => Show(message, visibleSeconds);

        /// <summary>Shows a message for <paramref name="seconds"/> (0 = until the next message).</summary>
        public void Show(string message, float seconds)
        {
            if (text != null) text.text = message;
            if (accent != null) accent.color = SetupStatusModel.ColorOf(SetupStatusModel.MessageLight(message));
            Reposition();
            if (canvas != null) canvas.enabled = true;
            _hideAt = seconds > 0f ? Time.unscaledTime + seconds : -1f;
        }

        private void Update()
        {
            if (_hideAt > 0f && Time.unscaledTime >= _hideAt)
            {
                _hideAt = -1f;
                if (canvas != null) canvas.enabled = false;
            }
        }

        private void Reposition()
        {
            if (head == null) return;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            transform.SetPositionAndRotation(
                head.position + forward * distance + Vector3.up * heightOffset,
                Quaternion.LookRotation(forward, Vector3.up));
        }
    }
}
