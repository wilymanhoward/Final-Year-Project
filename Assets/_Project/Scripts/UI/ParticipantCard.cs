using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// World-space card for the participant, built by FYP > Set Up Headset UI: the briefing with a pokeable Start
    /// button once the experimenter has armed the run, and a neutral end message afterwards
    /// (<see cref="ParticipantCardModel"/>). Shown in front of the head when it appears and stays put so it can be poked;
    /// re-placed if it has been out of view or reach for 1 s (headset handed over). The Start button calls <see cref="PressStart"/>.
    /// </summary>
    public class ParticipantCard : MonoBehaviour
    {
        [SerializeField] private SessionManager session;
        [SerializeField] private Transform head;
        [SerializeField] private GameObject content;
        [SerializeField] private Text title;
        [SerializeField] private Text body;
        [SerializeField] private GameObject startButton;
        [Tooltip("Distance in front of the head when the card appears (within arm's reach for poking).")]
        [SerializeField, Min(0.2f)] private float distance = 0.45f;
        [SerializeField] private float heightOffset = -0.15f;

        private bool _visible;
        private string _shownTitle;
        private float _outOfViewSince = -1f;

        // Re-place the card when it has been out of view or reach this long (e.g. after the headset is handed over).
        private const float RecenterAfterSeconds = 1f;
        private const float MaxViewAngle = 50f;
        private const float MaxReach = 1.2f;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (content != null) content.SetActive(false);
        }

        /// <summary>Hook for the Start button.</summary>
        public void PressStart()
        {
            if (session != null) session.RequestParticipantStart();
        }

        private void LateUpdate()
        {
            if (session == null) return;
            var s = session.State;
            CaseDefinition def = session.ActiveCase;
            ParticipantCardView view = ParticipantCardModel.Compute(
                session.IsArmed, s.Phase, s.EndReason, session.IsTeamMode || s.IsTeamRun,
                def != null ? def.Title : null, def != null ? def.Briefing : null);

            if (view.Visible != _visible || (view.Visible && view.Title != _shownTitle))
            {
                if (view.Visible) PlaceInFront();   // re-place when it appears or changes, then keep it still
                _visible = view.Visible;
                _shownTitle = view.Title;
                if (content != null) content.SetActive(view.Visible);
            }
            if (!view.Visible) return;
            KeepReachable();
            if (title != null) title.text = view.Title;
            if (body != null) body.text = view.Body;
            if (startButton != null && startButton.activeSelf != view.ShowStart) startButton.SetActive(view.ShowStart);
        }

        private void KeepReachable()
        {
            if (head == null) return;
            Vector3 toCard = transform.position - head.position;
            bool reachable = toCard.magnitude <= MaxReach && Vector3.Angle(head.forward, toCard) <= MaxViewAngle;
            if (reachable) { _outOfViewSince = -1f; return; }
            if (_outOfViewSince < 0f) { _outOfViewSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _outOfViewSince < RecenterAfterSeconds) return;
            PlaceInFront();
            _outOfViewSince = -1f;
        }

        private void PlaceInFront()
        {
            if (head == null) return;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 position = head.position + forward * distance + Vector3.up * heightOffset;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }
    }
}
