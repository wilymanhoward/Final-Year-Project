using System;
using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>Outcome of checking one evidence-board submission.</summary>
    public sealed class AnswerResult
    {
        public bool SuspectCorrect { get; }

        /// <summary>Submitted evidence ids that are not key evidence.</summary>
        public IReadOnlyList<string> WrongEvidenceIds { get; }

        /// <summary>Key evidence ids that were not submitted.</summary>
        public IReadOnlyList<string> MissingEvidenceIds { get; }

        /// <summary>Submitted evidence ids that are key evidence.</summary>
        public IReadOnlyList<string> CorrectEvidenceIds { get; }

        /// <summary>Number of key evidence items in the case (correct + missing).</summary>
        public int KeyEvidenceTotal => CorrectEvidenceIds.Count + MissingEvidenceIds.Count;

        /// <summary>Right suspect, all key evidence, no wrong evidence.</summary>
        public bool IsCorrect => SuspectCorrect && WrongEvidenceIds.Count == 0 && MissingEvidenceIds.Count == 0;

        /// <summary>
        /// Reasoning errors = 1 for a wrong suspect + 1 per wrong evidence item submitted.
        /// Missing key evidence is reported separately and not counted here.
        /// </summary>
        public int ReasoningErrorCount => (SuspectCorrect ? 0 : 1) + WrongEvidenceIds.Count;

        public AnswerResult(bool suspectCorrect, IReadOnlyList<string> wrongEvidenceIds, IReadOnlyList<string> missingEvidenceIds,
            IReadOnlyList<string> correctEvidenceIds = null)
        {
            SuspectCorrect = suspectCorrect;
            WrongEvidenceIds = wrongEvidenceIds ?? Array.Empty<string>();
            MissingEvidenceIds = missingEvidenceIds ?? Array.Empty<string>();
            CorrectEvidenceIds = correctEvidenceIds ?? Array.Empty<string>();
        }
    }

    /// <summary>Pure answer checking. Ids are compared exactly (ordinal) after trimming.</summary>
    public static class AnswerChecker
    {
        public static AnswerResult Check(
            string correctSuspectId,
            IEnumerable<string> keyEvidenceIds,
            string submittedSuspectId,
            IEnumerable<string> submittedEvidenceIds)
        {
            bool suspectCorrect = !string.IsNullOrWhiteSpace(submittedSuspectId)
                && string.Equals(Normalize(correctSuspectId), Normalize(submittedSuspectId), StringComparison.Ordinal);

            var key = ToSet(keyEvidenceIds);
            var submitted = ToSet(submittedEvidenceIds);

            var wrong = new List<string>();
            var correct = new List<string>();
            foreach (var id in submitted)
            {
                if (key.Contains(id)) correct.Add(id);
                else wrong.Add(id);
            }

            var missing = new List<string>();
            foreach (var id in key)
            {
                if (!submitted.Contains(id)) missing.Add(id);
            }

            // Sorted so logs are deterministic regardless of selection order.
            wrong.Sort(StringComparer.Ordinal);
            missing.Sort(StringComparer.Ordinal);
            correct.Sort(StringComparer.Ordinal);
            return new AnswerResult(suspectCorrect, wrong, missing, correct);
        }

        private static string Normalize(string id) => id?.Trim() ?? string.Empty;

        private static HashSet<string> ToSet(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (ids == null) return set;
            foreach (var id in ids)
            {
                var n = Normalize(id);
                if (n.Length > 0) set.Add(n);
            }
            return set;
        }
    }
}
