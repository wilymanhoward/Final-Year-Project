using System;
using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>Pure builder for <see cref="SessionMeta"/> (no Unity calls), so it can be unit tested.</summary>
    public static class SessionMetaBuilder
    {
        /// <summary>
        /// Snapshot of one run. <paramref name="endUtc"/> is null while the session is still running.
        /// <paramref name="keyEvidenceCount"/> (from the CaseDefinition) is used for evidenceScoreMax
        /// when no answer was submitted.
        /// </summary>
        public static SessionMeta Build(
            CaseState state,
            ExperimentConfig config,
            string caseTitle,
            int keyEvidenceCount,
            PlacementRecord placement,
            BuildInfo build,
            bool positionsInCaseRoot,
            DateTime startUtc,
            DateTime? endUtc,
            string logFile)
        {
            placement = placement ?? PlacementRecord.None;
            var result = state.LastResult;

            return new SessionMeta
            {
                config = config?.Clone(),
                conditionCode = config?.ConditionCode,
                caseCode = config?.CaseCode,
                caseTitle = caseTitle ?? "",
                logFile = logFile ?? "",

                buildGuid = build.BuildGuid ?? "",
                appVersion = build.AppVersion ?? "",
                unityVersion = build.UnityVersion ?? "",
                deviceModel = build.DeviceModel ?? "",
                operatingSystem = build.OperatingSystem ?? "",

                placementMethod = placement.MethodCode,
                placementEvent = placement.Method == PlacementMethod.None ? "" : placement.EventName,
                placementUtc = PlacementRecord.FormatUtc(placement.Utc),
                anchorUuid = placement.AnchorUuid ?? "",
                positionFrame = positionsInCaseRoot ? "CaseRoot" : "world",

                phase = state.Phase.ToString(),
                startUtc = PlacementRecord.FormatUtc(startUtc),
                endUtc = endUtc.HasValue ? PlacementRecord.FormatUtc(endUtc.Value) : "",
                endReason = state.EndReason.HasValue ? state.EndReason.Value.ToString()
                    : state.IsRunning ? SessionMeta.InterruptedReason : "",
                elapsedSeconds = Math.Round(state.ElapsedSeconds, 3),
                activeSeconds = Math.Round(state.ActiveSeconds, 3),
                pausedSeconds = Math.Round(state.PausedSeconds, 3),
                pauseCount = state.PauseCount,
                completed = state.Completed,

                cluesFound = state.FoundClues.Count,
                cluesTagged = state.TaggedClues.Count,
                foundClueIds = Sorted(state.FoundClues),
                taggedClueIds = Sorted(state.TaggedClues),
                contaminationErrors = state.ContaminationErrors,
                contaminationHead = state.ContaminationCount(BodyPart.Head),
                contaminationLeftHand = state.ContaminationCount(BodyPart.LeftHand),
                contaminationRightHand = state.ContaminationCount(BodyPart.RightHand),
                reasoningErrors = state.ReasoningErrors,
                submitted = state.HasSubmitted,
                submittedSuspectId = state.SubmittedSuspectId ?? "",
                submittedEvidenceIds = Sorted(state.SubmittedEvidenceIds),
                suspectCorrect = result != null && result.SuspectCorrect,
                answerCorrect = result != null && result.IsCorrect,
                evidenceScore = result != null ? result.CorrectEvidenceIds.Count : 0,
                evidenceScoreMax = result != null ? result.KeyEvidenceTotal : keyEvidenceCount,
                wrongEvidenceCount = result != null ? result.WrongEvidenceIds.Count : 0,
            };
        }

        private static string[] Sorted(IEnumerable<string> ids)
        {
            var list = new List<string>(ids ?? Array.Empty<string>());
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }
    }
}
