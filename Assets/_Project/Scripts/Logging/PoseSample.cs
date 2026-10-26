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
        public const string HmdUnmounted = "hmd_unmounted";
        public const string HmdMounted = "hmd_mounted";
        public const string TrackingRecentered = "tracking_recentered";
        public const string PlacementCheck = "placement_check";
        public const string HandTrackingLost = "hand_tracking_lost";
        public const string HandTrackingRegained = "hand_tracking_regained";
        public const string ClueReset = "clue_reset";
        public const string ClueThrown = "clue_thrown";
        public const string Briefing = "briefing";
        public const string BoardOrder = "board_order";
        public const string DepthOcclusion = "depth_occlusion";
        // Networking (Week 3). Written only while a run is open, like every event row.
        public const string NetPeerConnected = "net_peer_connected";
        public const string NetPeerDisconnected = "net_peer_disconnected";
        public const string NetStopped = "net_stopped";
        // Team runs (Week 3 Phase 3).
        public const string PartnerContamination = "partner_contamination";
        public const string ClueGrabTaken = "clue_grab_taken";
        // Evidence board (Phase C2).
        public const string BoardSuspect = "board_suspect";
        public const string BoardEvidence = "board_evidence";
        /// <summary>The experimenter armed this run (before the file was open; at_utc = when).</summary>
        public const string RunArmed = "run_armed";
        /// <summary>Something went wrong during a run, e.g. type=partner_disconnected (the run keeps its condition).</summary>
        public const string Incident = "incident";
    }
}
