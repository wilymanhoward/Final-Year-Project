using System;

namespace FYP.Detective
{
    /// <summary>
    /// Per-session meta data, written as JSON next to the CSV log
    /// ({pair}_{participant}_{cond}_{case}_{yyyyMMdd_HHmmss}_meta.json). Written at session start, refreshed
    /// regularly and on every app pause (phase "Running", endReason "Interrupted"), and overwritten at session
    /// end with the final totals. A file still saying "Interrupted" means the app was killed or crashed.
    /// Public fields so <c>JsonUtility</c> can serialise it. No names, only codes.
    /// </summary>
    [Serializable]
    public class SessionMeta
    {
        /// <summary>endReason written while the session has not ended (yet).</summary>
        public const string InterruptedReason = "Interrupted";

        public int schemaVersion = 2;

        // Run identity
        public ExperimentConfig config;
        public string conditionCode;
        public string caseCode;
        public string caseTitle;
        public string logFile;

        // Build and device
        public string buildGuid;
        public string appVersion;
        public string unityVersion;
        public string deviceModel;
        public string operatingSystem;

        // Placement ("anchor", "manual" or "none")
        public string placementMethod;
        public string placementEvent;
        public string placementUtc;
        public string anchorUuid;
        /// <summary>Frame of the logged positions: "CaseRoot" or "world".</summary>
        public string positionFrame;

        // Timing
        public string phase;
        public string startUtc;
        public string endUtc;
        public string endReason;
        /// <summary>Total time from start to end (includes pauses). Capped at the time limit.</summary>
        public double elapsedSeconds;
        /// <summary>elapsedSeconds minus pausedSeconds. Use this for analysis.</summary>
        public double activeSeconds;
        public double pausedSeconds;
        public int pauseCount;
        public bool completed;

        // Final CaseState summary
        public int cluesFound;
        public int cluesTagged;
        public string[] foundClueIds;
        public string[] taggedClueIds;
        public int contaminationErrors;
        public int contaminationHead;
        public int contaminationLeftHand;
        public int contaminationRightHand;
        public int reasoningErrors;
        public bool submitted;
        public string submittedSuspectId;
        public string[] submittedEvidenceIds;
        public bool suspectCorrect;
        public bool answerCorrect;
        /// <summary>Key evidence items submitted (0..evidenceScoreMax).</summary>
        public int evidenceScore;
        /// <summary>Number of key evidence items in the case.</summary>
        public int evidenceScoreMax;
        public int wrongEvidenceCount;
    }

    /// <summary>Build and device facts for <see cref="SessionMeta"/>, gathered by the caller from Unity.</summary>
    public struct BuildInfo
    {
        public string BuildGuid;
        public string AppVersion;
        public string UnityVersion;
        public string DeviceModel;
        public string OperatingSystem;
    }
}
