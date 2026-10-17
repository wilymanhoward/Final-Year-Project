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
        private readonly int[] _contaminationByPart = new int[3];   // indexed by BodyPart
        // Team runs: who found / currently has tagged each clue (participant id; null = this headset in a solo run).
        private readonly Dictionary<string, string> _foundBy = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _taggedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        // Evidence board selection (Phase C2). Game state: shared by the team, cleared every run.
        private readonly EvidenceSelection _board = new EvidenceSelection();

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

        // ---- Team condition (Week 3 Phase 3) ----

        /// <summary>True if this run is shared with a partner headset (set by <see cref="Begin"/>).</summary>
        public bool IsTeamRun { get; private set; }

        /// <summary>Participant id of this headset in the current run (for the *_self counts).</summary>
        public string SelfId { get; private set; }

        /// <summary>Participant who caused the event currently being raised (null in solo runs). Read it in event handlers.</summary>
        public string LastActorId { get; private set; }

        /// <summary>Why the last reported clue reset happened ("out_of_bounds" / "in_zone"; null if not given).</summary>
        public string LastResetReason { get; private set; }

        /// <summary>Contamination entries of the partner's body (each headset detects its own body).</summary>
        public int PartnerContaminationErrors { get; private set; }

        /// <summary>This headset's entries plus the partner's.</summary>
        public int TeamContaminationErrors => ContaminationErrors + PartnerContaminationErrors;

        /// <summary>Participant who submitted the answer (null in solo runs).</summary>
        public string SubmittedBy { get; private set; }

        /// <summary>Clues found by this participant (all found clues in a solo run).</summary>
        public int CluesFoundBySelf => CountBySelf(_foundBy, _found);

        /// <summary>Currently tagged clues that this participant tagged (all tagged clues in a solo run).</summary>
        public int CluesTaggedBySelf => CountBySelf(_taggedBy, _tagged);

        public string FoundBy(string clueId) => clueId != null && _foundBy.TryGetValue(clueId, out string by) ? by : null;

        // ---- Evidence board (Phase C2) ----

        /// <summary>Suspect currently chosen on the evidence board (null = none).</summary>
        public string BoardSuspectId => _board.SuspectId;

        /// <summary>Evidence currently chosen on the board (always a subset of the tagged clues).</summary>
        public IReadOnlyList<string> BoardEvidenceIds => _board.EvidenceIds;

        public bool IsBoardEvidenceSelected(string clueId) => _board.IsEvidenceSelected(clueId);

        /// <summary>Raised after the board selection changed (suspect or evidence).</summary>
        public event Action BoardChanged;
        /// <summary>For logging: suspect chosen (null = cleared).</summary>
        public event Action<string> BoardSuspectSelected;
        /// <summary>For logging: evidence item chosen (true) or removed (false).</summary>
        public event Action<string, bool> BoardEvidenceChanged;

        /// <summary>Chooses the suspect on the board. Only while running; false if nothing changed.</summary>
        public bool SetBoardSuspect(string suspectId, string by = null)
        {
            if (!IsRunning || HasSubmitted) return false;
            string before = _board.SuspectId;
            _board.SelectSuspect(suspectId);
            if (_board.SuspectId == before) return false;
            LastActorId = by;
            BoardSuspectSelected?.Invoke(_board.SuspectId);
            BoardChanged?.Invoke();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Adds/removes a board evidence item. Only tagged clues can be added. False if nothing changed.</summary>
        public bool SetBoardEvidence(string clueId, bool selected, string by = null)
        {
            if (!IsRunning || HasSubmitted || string.IsNullOrEmpty(clueId)) return false;
            if (_board.IsEvidenceSelected(clueId) == selected) return false;
            if (selected && !_tagged.Contains(clueId)) return false;
            _board.ToggleEvidence(clueId);
            LastActorId = by;
            BoardEvidenceChanged?.Invoke(clueId, selected);
            BoardChanged?.Invoke();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Time the app was paused during the run (headset off, app in background).</summary>
        public double PausedSeconds { get; private set; }
        public int PauseCount { get; private set; }

        /// <summary>Elapsed time minus paused time. Use this for analysis.</summary>
        public double ActiveSeconds => Math.Max(0, ElapsedSeconds - PausedSeconds);

        /// <summary>
        /// Updated by the session manager on pause/resume, every frame while a pause is open, and at the end.
        /// Ignored when not running.
        /// </summary>
        public void SetPauseStats(double pausedSeconds, int pauseCount)
        {
            if (!IsRunning) return;
            PausedSeconds = Math.Max(0, pausedSeconds);
            PauseCount = Math.Max(0, pauseCount);
        }

        /// <summary>Contamination entries triggered by one body part (head, left hand, right hand).</summary>
        public int ContaminationCount(BodyPart part)
        {
            int i = (int)part;
            return i >= 0 && i < _contaminationByPart.Length ? _contaminationByPart[i] : 0;
        }

        public bool IsClueFound(string clueId) => clueId != null && _found.Contains(clueId);
        public bool IsClueTagged(string clueId) => clueId != null && _tagged.Contains(clueId);

        // Events for logging/UI. Raised after the state has changed.
        public event Action<string> ClueFound;
        public event Action<string> ClueTagged;
        public event Action<string> ClueUntagged;
        public event Action<ContaminationInfo> ContaminationRecorded;
        public event Action<AnswerResult> AnswerSubmitted;
        public event Action<string> ClueReset;
        /// <summary>A clue was tossed (logging only; see <see cref="RecordClueThrown"/>).</summary>
        public event Action<string> ClueThrown;

        /// <summary>Release speed (m/s, before the cap) of the last reported toss.</summary>
        public float LastThrowSpeed { get; private set; }

        /// <summary>True if the last reported toss was faster than the cap and slowed down to it.</summary>
        public bool LastThrowCapped { get; private set; }
        /// <summary>Team runs: the partner's body entered a zone (already counted on their headset).</summary>
        public event Action<ContaminationInfo> PartnerContaminationRecorded;
        /// <summary>Team runs: the partner grabbed a clue this headset was holding (last grabber wins).</summary>
        public event Action<string> ClueGrabTaken;
        public event Action Changed;

        /// <summary>Clears everything back to NotStarted.</summary>
        public void Reset()
        {
            _found.Clear();
            _tagged.Clear();
            _contaminationByZone.Clear();
            _submittedEvidence.Clear();
            _foundBy.Clear();
            _taggedBy.Clear();
            bool hadBoard = _board.SuspectId != null || _board.EvidenceIds.Count > 0;
            _board.Clear();
            Array.Clear(_contaminationByPart, 0, _contaminationByPart.Length);
            PartnerContaminationErrors = 0;
            SubmittedBy = null;
            LastActorId = null;
            IsTeamRun = false;
            SelfId = null;
            if (hadBoard) BoardChanged?.Invoke();
            Phase = SessionPhase.NotStarted;
            ElapsedSeconds = 0;
            PausedSeconds = 0;
            PauseCount = 0;
            EndReason = null;
            ContaminationErrors = 0;
            ReasoningErrors = 0;
            HasSubmitted = false;
            SubmittedSuspectId = null;
            LastResult = null;
            Changed?.Invoke();
        }

        /// <summary>Resets and enters Running. <paramref name="teamRun"/>: shared with a partner (actors are logged).</summary>
        public void Begin(bool teamRun = false, string selfId = null)
        {
            Reset();
            IsTeamRun = teamRun;
            SelfId = selfId;
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

        /// <summary>Returns true only the first time a clue is found in this run (by anyone in a team run).</summary>
        public bool MarkClueFound(string clueId, string by = null)
        {
            if (!IsRunning || string.IsNullOrEmpty(clueId) || !_found.Add(clueId)) return false;
            _foundBy[clueId] = by;
            LastActorId = by;
            ClueFound?.Invoke(clueId);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Tags a clue as relevant. Tagging also counts as finding it.</summary>
        public bool MarkClueTagged(string clueId, string by = null)
        {
            if (!IsRunning || string.IsNullOrEmpty(clueId)) return false;
            MarkClueFound(clueId, by);
            if (!_tagged.Add(clueId)) return false;
            _taggedBy[clueId] = by;
            LastActorId = by;
            ClueTagged?.Invoke(clueId);
            Changed?.Invoke();
            return true;
        }

        public bool UntagClue(string clueId, string by = null)
        {
            if (!IsRunning || string.IsNullOrEmpty(clueId) || !_tagged.Remove(clueId)) return false;
            _taggedBy.Remove(clueId);
            LastActorId = by;
            ClueUntagged?.Invoke(clueId);
            // Board shows tagged clues only: an untagged clue leaves the selection too (logged as board_evidence).
            if (_board.IsEvidenceSelected(clueId))
            {
                _board.ToggleEvidence(clueId);
                BoardEvidenceChanged?.Invoke(clueId, false);
                BoardChanged?.Invoke();
            }
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
            int part = (int)bodyPart;
            if (part >= 0 && part < _contaminationByPart.Length) _contaminationByPart[part]++;
            ContaminationErrors++;
            LastActorId = IsTeamRun ? SelfId : null;   // own body
            ContaminationRecorded?.Invoke(new ContaminationInfo(zoneId, bodyPart, ElapsedSeconds));
            Changed?.Invoke();
            return true;
        }

        /// <summary>Team runs: one contamination entry of the partner (counted separately from this headset's own).</summary>
        public bool RecordPartnerContamination(string zoneId, BodyPart bodyPart, string by)
        {
            if (!IsRunning) return false;
            PartnerContaminationErrors++;
            LastActorId = by;
            PartnerContaminationRecorded?.Invoke(new ContaminationInfo(zoneId ?? string.Empty, bodyPart, ElapsedSeconds));
            Changed?.Invoke();
            return true;
        }

        /// <summary>Team runs: the partner took a clue out of this headset's hand. For logging only.</summary>
        public bool RecordClueGrabTaken(string clueId, string by)
        {
            if (!IsRunning || string.IsNullOrEmpty(clueId)) return false;
            LastActorId = by;
            ClueGrabTaken?.Invoke(clueId);
            return true;
        }

        /// <summary>
        /// Reports that a loose clue was put back at its start position (fell through the floor / too far away /
        /// came to rest in an exclusion zone; <paramref name="reason"/>, see <see cref="LastResetReason"/>).
        /// Not game state; raised for logging. Only while running.
        /// </summary>
        /// <summary>
        /// Reports that a clue was tossed (released faster than the toss threshold, user decision session 6: capped toss
        /// with logging). <paramref name="speed"/> is the measured release speed, <paramref name="capped"/> whether it
        /// was limited to the cap. Not game state; raised for logging. Only while running.
        /// </summary>
        public bool RecordClueThrown(string clueId, string by, float speed, bool capped)
        {
            if (!IsRunning || string.IsNullOrEmpty(clueId)) return false;
            LastActorId = by;
            LastThrowSpeed = speed;
            LastThrowCapped = capped;
            ClueThrown?.Invoke(clueId);
            return true;
        }

        public bool RecordClueReset(string clueId, string by = null, string reason = null)
        {
            if (!IsRunning || string.IsNullOrEmpty(clueId)) return false;
            LastActorId = by;
            LastResetReason = reason;
            ClueReset?.Invoke(clueId);
            return true;
        }

        /// <summary>
        /// Stores the submitted answer and adds its reasoning errors.
        /// Does not end the session - SessionManager does that right after.
        /// </summary>
        public bool RecordSubmission(string suspectId, IEnumerable<string> evidenceIds, AnswerResult result, string by = null)
        {
            if (!IsRunning || result == null || HasSubmitted) return false;
            HasSubmitted = true;
            SubmittedBy = by;
            LastActorId = by;
            SubmittedSuspectId = suspectId;
            _submittedEvidence.Clear();
            if (evidenceIds != null) _submittedEvidence.AddRange(evidenceIds);
            LastResult = result;
            ReasoningErrors += result.ReasoningErrorCount;
            AnswerSubmitted?.Invoke(result);
            Changed?.Invoke();
            return true;
        }

        private int CountBySelf(Dictionary<string, string> byClue, HashSet<string> clues)
        {
            int n = 0;
            foreach (string clue in clues)
            {
                byClue.TryGetValue(clue, out string by);
                if (by == null || by == SelfId) n++;
            }
            return n;
        }
    }
}
