using System;
using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>A contamination entry: which zone, which body part, when.</summary>
    public readonly struct ContaminationInfo
    {
        public readonly string ZoneId;
        public readonly BodyPart BodyPart;
        public readonly double SessionTime;

        public ContaminationInfo(string zoneId, BodyPart bodyPart, double sessionTime)
        {
            ZoneId = zoneId;
            BodyPart = bodyPart;
            SessionTime = sessionTime;
        }
    }

    /// <summary>
    /// Single source of truth for one run of a case. Plain C# (no Unity types) so it
    /// can be unit tested now and mirrored into Netcode NetworkVariables later.
    /// All changes go through methods; nothing else should keep its own copy of this data.
    /// Mutating methods do nothing unless <see cref="Phase"/> is Running.
    /// </summary>
    public sealed class CaseState
    {
        private readonly HashSet<string> _found = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _tagged = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _contaminationByZone = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> _submittedEvidence = new List<string>();

        public SessionPhase Phase { get; private set; } = SessionPhase.NotStarted;
        public double ElapsedSeconds { get; private set; }
        public SessionEndReason? EndReason { get; private set; }

        /// <summary>True only if the session ended because an answer was submitted before the cap.</summary>
        public bool Completed => EndReason == SessionEndReason.Submitted;

        public IReadOnlyCollection<string> FoundClues => _found;
        public IReadOnlyCollection<string> TaggedClues => _tagged;
        public IReadOnlyDictionary<string, int> ContaminationByZone => _contaminationByZone;
        public int ContaminationErrors { get; private set; }
        public int ReasoningErrors { get; private set; }

        public bool HasSubmitted { get; private set; }
        public string SubmittedSuspectId { get; private set; }
        public IReadOnlyList<string> SubmittedEvidenceIds => _submittedEvidence;
        public AnswerResult LastResult { get; private set; }

        public bool IsRunning => Phase == SessionPhase.Running;

        public bool IsClueFound(string clueId) => clueId != null && _found.Contains(clueId);
        public bool IsClueTagged(string clueId) => clueId != null && _tagged.Contains(clueId);

        // Events for logging/UI. Raised after the state has changed.
        public event Action<string> ClueFound;
        public event Action<string> ClueTagged;
        public event Action<string> ClueUntagged;
        public event Action<ContaminationInfo> ContaminationRecorded;
        public event Action<AnswerResult> AnswerSubmitted;
        public event Action Changed;

        /// <summary>Clears everything back to NotStarted.</summary>
        public void Reset()
        {
            _found.Clear();
            _tagged.Clear();
            _contaminationByZone.Clear();
            _submittedEvidence.Clear();
            Phase = SessionPhase.NotStarted;
            ElapsedSeconds = 0;
            EndReason = null;
            ContaminationErrors = 0;
            ReasoningErrors = 0;
            HasSubmitted = false;
            SubmittedSuspectId = null;
            LastResult = null;
            Changed?.Invoke();
        }

        /// <summary>Resets and enters Running.</summary>
        public void Begin()
        {
            Reset();
            Phase = SessionPhase.Running;
            Changed?.Invoke();
        }

        /// <summary>Stops the run. Returns false if it was not running.</summary>
        public bool End(SessionEndReason reason, double finalElapsedSeconds)
        {
            if (!IsRunning) return false;
            ElapsedSeconds = finalElapsedSeconds;
            EndReason = reason;
            Phase = SessionPhase.Ended;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Called every frame by the session timer. Does not raise Changed (too frequent).</summary>
        public void SetElapsed(double seconds)
        {
            if (IsRunning) ElapsedSeconds = seconds;
        }

        /// <summary>Returns true only the first time a clue is found in this run.</summary>
        public bool MarkClueFound(string clueId)
        {
            if (!IsRunning || string.IsNullOrEmpty(clueId) || !_found.Add(clueId)) return false;
            ClueFound?.Invoke(clueId);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Tags a clue as relevant. Tagging also counts as finding it.</summary>
        public bool MarkClueTagged(string clueId)
        {
            if (!IsRunning || string.IsNullOrEmpty(clueId)) return false;
            MarkClueFound(clueId);
            if (!_tagged.Add(clueId)) return false;
            ClueTagged?.Invoke(clueId);
            Changed?.Invoke();
            return true;
        }

        public bool UntagClue(string clueId)
        {
            if (!IsRunning || string.IsNullOrEmpty(clueId) || !_tagged.Remove(clueId)) return false;
            ClueUntagged?.Invoke(clueId);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Records one contamination entry (already debounced by the caller's cooldown).</summary>
        public bool RecordContamination(string zoneId, BodyPart bodyPart)
        {
            if (!IsRunning) return false;
            zoneId = zoneId ?? string.Empty;
            _contaminationByZone.TryGetValue(zoneId, out int count);
            _contaminationByZone[zoneId] = count + 1;
            ContaminationErrors++;
            ContaminationRecorded?.Invoke(new ContaminationInfo(zoneId, bodyPart, ElapsedSeconds));
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Stores the submitted answer and adds its reasoning errors.
        /// Does not end the session - SessionManager does that right after.
        /// </summary>
        public bool RecordSubmission(string suspectId, IEnumerable<string> evidenceIds, AnswerResult result)
        {
            if (!IsRunning || result == null) return false;
            HasSubmitted = true;
            SubmittedSuspectId = suspectId;
            _submittedEvidence.Clear();
            if (evidenceIds != null) _submittedEvidence.AddRange(evidenceIds);
            LastResult = result;
            ReasoningErrors += result.ReasoningErrorCount;
            AnswerSubmitted?.Invoke(result);
            Changed?.Invoke();
            return true;
        }
    }
}
