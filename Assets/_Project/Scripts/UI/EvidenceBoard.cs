using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective
{
    /// <summary>
    /// The board where players pick a suspect and evidence, then submit. The selection is game state in
    /// <see cref="CaseState"/> (shared by the team, through the host); this component only requests changes.
    /// UI-agnostic: <see cref="EvidenceBoardView"/> (or any buttons) call SelectSuspect("id"), ToggleEvidence("id")
    /// and Submit(). Only tagged clues can be chosen as evidence. The answer is checked against the active
    /// CaseDefinition; wrong suspect / wrong evidence become reasoning errors, and submitting ends the session.
    /// </summary>
    public class EvidenceBoard : MonoBehaviour
    {
        [SerializeField] private SessionManager session;

        [Tooltip("Minimum evidence items required before Submit is accepted.")]
        [SerializeField, Min(0)] private int minEvidence = 1;

        [Header("Feedback (optional)")]
        [Tooltip("Human-readable selection summary, e.g. wire to a Text.text.")]
        public UnityEvent<string> onSelectionChanged = new UnityEvent<string>();
        [Tooltip("Invoked with true/false = whether Submit is currently possible.")]
        public UnityEvent<bool> onCanSubmitChanged = new UnityEvent<bool>();
        public UnityEvent onSubmitted = new UnityEvent();

        public int MinEvidence => minEvidence;
        public SessionManager Session => session;

        public bool CanSubmit => session != null && session.IsRunning && !session.State.HasSubmitted
            && session.State.BoardSuspectId != null && session.State.BoardEvidenceIds.Count >= minEvidence;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void OnEnable()
        {
            if (session == null) return;
            session.State.BoardChanged += NotifyChanged;
            session.SessionEnded += HandleSessionEnded;
            NotifyChanged();
        }

        private void OnDisable()
        {
            if (session == null) return;
            session.State.BoardChanged -= NotifyChanged;
            session.SessionEnded -= HandleSessionEnded;
        }

        public void SelectSuspect(string suspectId)
        {
            if (session != null) session.RequestBoardSuspect(suspectId);
        }

        /// <summary>Adds a tagged clue to the chosen evidence, or removes it if already chosen.</summary>
        public void ToggleEvidence(string evidenceId)
        {
            if (session == null) return;
            session.RequestBoardEvidence(evidenceId, !session.State.IsBoardEvidenceSelected(evidenceId));
        }

        /// <summary>
        /// Sends the answer. SessionManager checks it, records it and ends the run (in a team run the first
        /// submission of either partner ends both). onSubmitted fires when the run has ended as Submitted.
        /// </summary>
        public void Submit()
        {
            if (!CanSubmit) return;
            // Copy: the selection is cleared on the next session start, the log must keep this list.
            var evidence = new List<string>(session.State.BoardEvidenceIds);
            session.RequestSubmit(session.State.BoardSuspectId, evidence);
        }

        private void HandleSessionEnded(SessionEndReason reason)
        {
            if (reason == SessionEndReason.Submitted) onSubmitted.Invoke();
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            var s = session.State;
            var sb = new StringBuilder();
            sb.Append("Suspect: ").Append(s.BoardSuspectId ?? "-").Append('\n');
            sb.Append("Evidence: ");
            sb.Append(s.BoardEvidenceIds.Count == 0 ? "-" : string.Join(", ", s.BoardEvidenceIds));
            onSelectionChanged.Invoke(sb.ToString());
            onCanSubmitChanged.Invoke(CanSubmit);
        }
    }
}
