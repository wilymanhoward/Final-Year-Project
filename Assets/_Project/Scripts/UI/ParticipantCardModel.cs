namespace FYP.Detective
{
    /// <summary>What the participant card shows: hidden, the briefing with a Start button, or a neutral end message.</summary>
    public readonly struct ParticipantCardView
    {
        public readonly bool Visible;
        public readonly string Title;
        public readonly string Body;
        public readonly bool ShowStart;

        public ParticipantCardView(bool visible, string title, string body, bool showStart)
        {
            Visible = visible;
            Title = title;
            Body = body;
            ShowStart = showStart;
        }

        public static readonly ParticipantCardView Hidden = new ParticipantCardView(false, "", "", false);
    }

    /// <summary>
    /// Pure logic for the participant card (Phase C, user decisions session 6): armed → briefing + Start; running →
    /// hidden; ended → a neutral message that never reveals whether the answer was right (within-subjects design).
    /// </summary>
    public static class ParticipantCardModel
    {
        public const string BriefingTitle = "Your case";
        public const string StartLabel = "Start";

        public static ParticipantCardView Compute(bool armed, SessionPhase phase, SessionEndReason? endReason, bool teamRun)
        {
            if (phase == SessionPhase.Running) return ParticipantCardView.Hidden;

            if (armed)
            {
                string body = teamRun
                    ? "Work together with your partner. Find the clues, tag the important ones and submit your answer " +
                      "on the evidence board. Don't step into the marked areas around the evidence.\n\nTap Start when you are both ready."
                    : "Find the clues, tag the important ones and submit your answer on the evidence board. " +
                      "Don't step into the marked areas around the evidence.\n\nTap Start when you are ready.";
                return new ParticipantCardView(true, BriefingTitle, body, true);
            }

            if (phase == SessionPhase.Ended && endReason.HasValue)
            {
                switch (endReason.Value)
                {
                    case SessionEndReason.Submitted:
                        return new ParticipantCardView(true, "Case closed", "Thank you. Please take off the headset.", false);
                    case SessionEndReason.TimeCap:
                        return new ParticipantCardView(true, "Time is up", "Thank you. Please take off the headset.", false);
                    default:
                        return new ParticipantCardView(true, "Session stopped", "Please wait for the experimenter.", false);
                }
            }
            return ParticipantCardView.Hidden;
        }
    }
}
