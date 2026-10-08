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

    /// <summary>Which tracked point entered an exclusion zone.</summary>
    public enum BodyPart
    {
        Head = 0,
        LeftHand = 1,
        RightHand = 2,
    }
}
