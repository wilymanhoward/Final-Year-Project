using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective
{
    /// <summary>
    /// The board where players pick a suspect and evidence, then submit.
    /// UI-agnostic: world-space buttons (uGUI Button.onClick, or Interaction SDK
    /// poke buttons via their UnityEvent wrapper) call SelectSuspect("id"),
    /// ToggleEvidence("id") and Submit(). The answer is checked against the active
    /// CaseDefinition; wrong suspect / wrong evidence become reasoning errors, and
    /// submitting ends the session (timer stops).
    /// </summary>
    public class EvidenceBoard : MonoBehaviour
    {
        [SerializeField] private SessionManager session;

        [Tooltip("Minimum evidence items required before Submit is accepted.")]
        [SerializeField, Min(0)] private int minEvidence = 1;

        [Header("Feedback (optional)")]
        [Tooltip("Human-readable selection summary, e.g. wire to a TMP_Text.text.")]
        public UnityEvent<string> onSelectionChanged = new UnityEvent<string>();
        [Tooltip("Invoked with true/false = whether Submit is currently possible, e.g. wire to Button.interactable.")]
        public UnityEvent<bool> onCanSubmitChanged = new UnityEvent<bool>();
        public UnityEvent onSubmitted = new UnityEvent();

        private readonly EvidenceSelection _selection = new EvidenceSelection();

        public EvidenceSelection Selection => _selection;
        public bool CanSubmit => session != null && session.IsRunning && _selection.CanSubmit(minEvidence);

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void OnEnable()
        {
            _selection.Changed += NotifyChanged;
            if (session != null)
            {
                session.SessionStarted += _selection.Clear;
                session.SessionReset += _selection.Clear;
                session.SessionEnded += HandleSessionEnded;
            }
            NotifyChanged();
        }

        private void OnDisable()
        {
            _selection.Changed -= NotifyChanged;
            if (session != null)
            {
                session.SessionStarted -= _selection.Clear;
                session.SessionReset -= _selection.Clear;
                session.SessionEnded -= HandleSessionEnded;
            }
        }

        public void SelectSuspect(string suspectId)
        {
            if (session != null && session.IsRunning) _selection.SelectSuspect(suspectId);
        }

        public void ToggleEvidence(string evidenceId)
        {
            if (session != null && session.IsRunning) _selection.ToggleEvidence(evidenceId);
        }

        public void ClearSelection() => _selection.Clear();

        /// <summary>Checks the answer, records it in CaseState and ends the session.</summary>
        public void Submit()
        {
            if (!CanSubmit) return;
            var caseDef = session.ActiveCase;
            if (caseDef == null)
            {
                Debug.LogError("[EvidenceBoard] No active CaseDefinition; cannot check answer.");
                return;
            }

            // Copy: the selection is cleared on the next session start, the log must keep this list.
            var evidence = new System.Collections.Generic.List<string>(_selection.EvidenceIds);
            AnswerResult result = caseDef.Check(_selection.SuspectId, evidence);
            if (session.SubmitAnswer(_selection.SuspectId, evidence, result))
            {
                onSubmitted.Invoke();
                NotifyChanged();
            }
        }

        private void HandleSessionEnded(SessionEndReason reason) => NotifyChanged();

        private void NotifyChanged()
        {
            var sb = new StringBuilder();
            sb.Append("Suspect: ").Append(_selection.SuspectId ?? "-").Append('\n');
            sb.Append("Evidence: ");
            sb.Append(_selection.EvidenceIds.Count == 0 ? "-" : string.Join(", ", _selection.EvidenceIds));
            onSelectionChanged.Invoke(sb.ToString());
            onCanSubmitChanged.Invoke(CanSubmit);
        }
    }
}
