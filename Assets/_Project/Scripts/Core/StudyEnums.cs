namespace FYP.Detective
{
    /// <summary>Study condition. Written as "A"/"B" in filenames and CSV columns.</summary>
    public enum StudyCondition
    {
        /// <summary>Condition A: solo.</summary>
        A = 0,
        /// <summary>Condition B: team of two.</summary>
        B = 1,
    }

    /// <summary>Which of the two equally difficult cases is played. Written as "X"/"Y".</summary>
    public enum CaseId
    {
        X = 0,
        Y = 1,
    }

    /// <summary>Lifecycle of one experimental run.</summary>
    public enum SessionPhase
    {
        NotStarted = 0,
        Running = 1,
        Ended = 2,
    }

    /// <summary>Why a session ended. Only <see cref="Submitted"/> counts as "completed".</summary>
    public enum SessionEndReason
    {
        Submitted = 0,
        TimeCap = 1,
        /// <summary>Ended on purpose by the experimenter.</summary>
        Aborted = 2,
        /// <summary>
        /// The app was closed mid-session. Also written to the summary/meta files while a session is still
        /// running, so a run that is killed or crashes is left marked "Interrupted".
        /// </summary>
        Interrupted = 3,
    }

    /// <summary>How a run was started (logged as start_source).</summary>
    public enum StartSource
    {
        /// <summary>Experimenter started it directly (setup panel "Start now", right stick, desktop panel).</summary>
        StartNow = 0,
        /// <summary>The participant poked Start on the briefing card after the experimenter armed the run.</summary>
        ParticipantStart = 1,
    }

    /// <summary>Which tracked point entered an exclusion zone.</summary>
    public enum BodyPart
    {
        Head = 0,
        LeftHand = 1,
        RightHand = 2,
    }

    /// <summary>
    /// Whether real people/objects hide virtual ones (Meta Depth API environment occlusion), logged per run as
    /// depth_occlusion status=... so runs without working occlusion can be found in the data.
    /// </summary>
    public enum DepthOcclusionStatus
    {
        /// <summary>No occlusion component in the scene (or it is switched off).</summary>
        Off = 0,
        /// <summary>The device / runtime has no environment depth (e.g. the Editor without Link).</summary>
        Unsupported = 1,
        /// <summary>Spatial data permission (com.oculus.permission.USE_SCENE) not granted.</summary>
        NoPermission = 2,
        /// <summary>Supported and permitted, but no depth frame yet.</summary>
        Waiting = 3,
        /// <summary>Depth frames arrive; virtual objects behind real ones are hidden.</summary>
        Active = 4,
    }
}
