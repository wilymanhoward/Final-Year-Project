namespace FYP.Detective
{
    /// <summary>What the participant card shows: hidden, the briefing with a Start button, or a neutral end message.</summary>
    public readonly struct ParticipantCardView
    {
        public readonly bool Visible;
        public readonly string Title;
        public readonly string Body;
        public readonly bool ShowStart;
        /// <summary>Briefing detective: "Begin briefing" (before it played) and "Replay" (after).</summary>
        public readonly bool ShowBegin;
        public readonly bool ShowReplay;

        public ParticipantCardView(bool visible, string title, string body, bool showStart, bool showBegin = false, bool showReplay = false)
        {
            Visible = visible;
            Title = title;
            Body = body;
            ShowStart = showStart;
            ShowBegin = showBegin;
            ShowReplay = showReplay;
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
        public const string BeginLabel = "Begin briefing";
        public const string ReplayLabel = "Replay";

        /// <param name="briefing">Stage of the detective's briefing, or null when there is no detective (card shows the story).</param>
        /// <param name="canStart">With a detective: Start is allowed (briefing done, and the partner's in team runs).</param>
        public static ParticipantCardView Compute(bool armed, SessionPhase phase, SessionEndReason? endReason, bool teamRun,
            string caseTitle = null, string caseBriefing = null, BriefingStage? briefing = null, bool canStart = true)
        {
            if (phase == SessionPhase.Running) return ParticipantCardView.Hidden;

            if (armed && briefing.HasValue)
            {
                string title = string.IsNullOrEmpty(caseTitle) ? BriefingTitle : caseTitle;
                switch (briefing.Value)
                {
                    case BriefingStage.NotStarted:
                        return new ParticipantCardView(true, title,
                            "Inspector Hart will explain your case.\n\nTap Begin briefing when you are ready to listen.", false, true, false);
                    case BriefingStage.Playing:
                        return ParticipantCardView.Hidden;   // out of the way while the detective talks
                    case BriefingStage.WaitingForPartner:
                        return new ParticipantCardView(true, title,
                            "Waiting for your partner to tap Begin briefing...", false, false, false);
                    default:
                        if (!canStart)
                            return new ParticipantCardView(true, title,
                                "Waiting for your partner to finish the briefing...", false, false, true);
                        return new ParticipantCardView(true, title, Instructions(teamRun), true, false, true);
                }
            }

            if (armed)
            {
                string story = string.IsNullOrEmpty(caseBriefing) ? "" : caseBriefing + "\n\n";
                string body = story + (teamRun
                    ? "Work together with your partner. Tag the evidence, choose the suspect and the evidence on the " +
                      "evidence board, and submit. Don't step into the marked areas.\n\nTap Start when you are both ready."
                    : "Tag the evidence, choose the suspect and the evidence on the evidence board, and submit. " +
                      "Don't step into the marked areas.\n\nTap Start when you are ready.");
                string title = string.IsNullOrEmpty(caseTitle) ? BriefingTitle : caseTitle;
                return new ParticipantCardView(true, title, body, true);
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

        private static string Instructions(bool teamRun) => teamRun
            ? "Work together with your partner. Tag the evidence, choose the suspect and the evidence on the evidence " +
              "board, and submit. Don't step into the marked areas.\n\nTap Start when you are both ready."
            : "Tag the evidence, choose the suspect and the evidence on the evidence board, and submit. " +
              "Don't step into the marked areas.\n\nTap Start when you are ready.";
    }
}
