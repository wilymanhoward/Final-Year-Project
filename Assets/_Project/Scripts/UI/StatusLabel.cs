using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// Small world-space message for the experimenter (e.g. placement status). <see cref="Show"/>
    /// places it in front of the head, level and facing the user, and hides it again after
    /// <see cref="visibleSeconds"/>. Put it on the root of a world-space Canvas.
    /// </summary>
    public class StatusLabel : MonoBehaviour
    {
        [SerializeField] private Text text;
        [SerializeField] private Canvas canvas;
        [Tooltip("Usually OVRCameraRig/TrackingSpace/CenterEyeAnchor. Empty = Camera.main.")]
        [SerializeField] private Transform head;
        [Tooltip("Seconds the message stays visible. 0 = until the next message.")]
        [SerializeField, Min(0f)] private float visibleSeconds = 6f;
        [SerializeField, Min(0.3f)] private float distance = 0.8f;
        [SerializeField] private float heightOffset = -0.15f;

        private float _hideAt = -1f;

        public string CurrentText => text != null ? text.text : "";

        private void Awake()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (canvas != null) canvas.enabled = false;
        }

        /// <summary>Shows a message in front of the user (wire to a UnityEvent&lt;string&gt;).</summary>
        public void Show(string message)
        {
            if (text != null) text.text = message;
            Reposition();
            if (canvas != null) canvas.enabled = true;
            _hideAt = visibleSeconds > 0f ? Time.unscaledTime + visibleSeconds : -1f;
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
