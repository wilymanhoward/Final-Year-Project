using UnityEngine;

namespace FYP.Detective
{
    /// <summary>One snapshot of head + hands. Has* flags mark missing/untracked data.</summary>
    public struct PoseSample
    {
        public bool HasHead;
        public Vector3 HeadPosition;
        public Quaternion HeadRotation;

        public bool HasLeftHand;
        public Vector3 LeftHandPosition;

        public bool HasRightHand;
        public Vector3 RightHandPosition;

        /// <summary>
        /// Same sample expressed in the frame of <paramref name="origin"/> / <paramref name="rotation"/>
        /// (e.g. CaseRoot). Missing parts stay missing.
        /// </summary>
        public PoseSample ToLocal(Vector3 origin, Quaternion rotation)
        {
            var inverse = Quaternion.Inverse(rotation);
            var local = this;
            if (HasHead)
            {
                local.HeadPosition = inverse * (HeadPosition - origin);
                local.HeadRotation = inverse * HeadRotation;
            }
            if (HasLeftHand) local.LeftHandPosition = inverse * (LeftHandPosition - origin);
            if (HasRightHand) local.RightHandPosition = inverse * (RightHandPosition - origin);
            return local;
        }
    }

    /// <summary>One CSV row: either a 10 Hz "sample" or a discrete "event".</summary>
    public struct LogRow
    {
        public const string TypeSample = "sample";
        public const string TypeEvent = "event";

        public double SessionTime;
        public System.DateTime UtcTime;
        public string RowType;
        public string EventName;
        public string EventDetail;
        public float FrameDeltaMs;
        public PoseSample Pose;
    }

    /// <summary>Event names written to the log. Keep in sync with CLAUDE.md.</summary>
    public static class LogEvents
    {
        public const string SessionStart = "session_start";
        public const string SessionEnd = "session_end";
        public const string ClueFound = "clue_found";
        public const string ClueTagged = "clue_tagged";
        public const string ClueUntagged = "clue_untagged";
        public const string ErrorContamination = "error_contamination";
        public const string ErrorReasoning = "error_reasoning";
        public const string AnswerSubmitted = "answer_submitted";
        public const string PlacementSet = "placement_set";
        public const string PlacementLoaded = "placement_loaded";
        public const string AppPaused = "app_paused";
        public const string AppResumed = "app_resumed";
    }
}
