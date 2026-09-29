using System;
using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>
    /// What the player currently has selected on the evidence board (one suspect,
    /// any number of evidence ids). Pure logic so the board's rules are testable.
    /// </summary>
    public sealed class EvidenceSelection
    {
        private readonly List<string> _evidence = new List<string>();

        public string SuspectId { get; private set; }
        public IReadOnlyList<string> EvidenceIds => _evidence;

        public event Action Changed;

        public void SelectSuspect(string suspectId)
        {
            suspectId = string.IsNullOrWhiteSpace(suspectId) ? null : suspectId.Trim();
            if (SuspectId == suspectId) return;
            SuspectId = suspectId;
            Changed?.Invoke();
        }

        /// <summary>Adds the id if absent, removes it if present. Returns true if now selected.</summary>
        public bool ToggleEvidence(string evidenceId)
        {
            if (string.IsNullOrWhiteSpace(evidenceId)) return false;
            evidenceId = evidenceId.Trim();
            bool selected;
            if (_evidence.Remove(evidenceId)) selected = false;
            else { _evidence.Add(evidenceId); selected = true; }
            Changed?.Invoke();
            return selected;
        }

        public bool IsEvidenceSelected(string evidenceId) => evidenceId != null && _evidence.Contains(evidenceId.Trim());

        public void Clear()
        {
            SuspectId = null;
            _evidence.Clear();
            Changed?.Invoke();
        }

        /// <summary>A submission needs a suspect and at least <paramref name="minEvidence"/> evidence items.</summary>
        public bool CanSubmit(int minEvidence) => SuspectId != null && _evidence.Count >= minEvidence;
    }
}
