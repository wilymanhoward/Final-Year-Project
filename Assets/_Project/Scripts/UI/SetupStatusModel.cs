using UnityEngine;

namespace FYP.Detective
{
    /// <summary>Traffic-light state of one setup panel section.</summary>
    public enum StatusLight
    {
        /// <summary>Not relevant right now (grey).</summary>
        Off = 0,
        Ok = 1,
        /// <summary>Waiting / needs attention, but nothing is wrong (amber).</summary>
        Warn = 2,
        /// <summary>Blocks the run (red).</summary>
        Bad = 3,
    }

    /// <summary>
    /// Pure rules for the redesigned setup panel (session 6): which traffic light each section shows and the run-state
    /// chip, so the experimenter sees at a glance what still blocks a run. Unit tested.
    /// </summary>
    public static class SetupStatusModel
    {
        /// <summary>Placement: red until placed (Start needs a placement) or while the height check fails.</summary>
        public static StatusLight Placement(PlacementMethod method, bool heightOk) =>
            method == PlacementMethod.None || !heightOk ? StatusLight.Bad : StatusLight.Ok;

        /// <summary>Network vs. condition: B needs a partner, A must not have one (same rule as arming).</summary>
        public static StatusLight Network(StudyCondition condition, bool partnerConnected)
        {
            if (condition == StudyCondition.B) return partnerConnected ? StatusLight.Ok : StatusLight.Bad;
            return partnerConnected ? StatusLight.Bad : StatusLight.Off;
        }

        /// <summary>Data upload: green when everything reached the laptop, amber while files wait.</summary>
        public static StatusLight Data(int waitingFiles) => waitingFiles == 0 ? StatusLight.Ok : StatusLight.Warn;

        /// <summary>Run: green while running, amber when armed (waiting for the participant), red if ARM would be refused.</summary>
        public static StatusLight Run(bool running, bool armed, bool canArm)
        {
            if (running) return StatusLight.Ok;
            if (armed) return StatusLight.Warn;
            return canArm ? StatusLight.Off : StatusLight.Bad;
        }

        /// <summary>Header chip text: RUNNING mm:ss / ARMED / ENDED / READY.</summary>
        public static string Chip(bool running, bool armed, bool ended, double elapsedSeconds)
        {
            if (running)
            {
                int s = Mathf.Max(0, (int)elapsedSeconds);
                return "RUNNING " + (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
            }
            if (armed) return "ARMED";
            return ended ? "ENDED" : "READY";
        }

        public static StatusLight ChipLight(bool running, bool armed, bool ended) =>
            running ? StatusLight.Ok : armed ? StatusLight.Warn : ended ? StatusLight.Off : StatusLight.Ok;

        /// <summary>
        /// The experimenter's next step (setup panel "NEXT" banner, session 6 redesign) and which card it belongs to
        /// (0 Participant, 1 Placement, 2 Network, 3 Run) so that card can be highlighted. Order follows the session
        /// checklist: team connection first (the client gets its placement from the host), then placement, then ARM.
        /// </summary>
        public static string NextStep(bool running, bool ended, bool armed, StudyCondition condition, bool partnerConnected,
            bool placementOk, out int card)
        {
            if (running) { card = 3; return "Run in progress - don't intervene. Hold the left stick only to abort."; }
            string prefix = ended && !armed ? "Run finished. Set the next participant's codes, then: " : "";
            if (condition == StudyCondition.B && !partnerConnected)
            {
                card = 2;
                return prefix + "Team run: press Host on this headset, or Join on the partner's.";
            }
            if (condition == StudyCondition.A && partnerConnected)
            {
                card = 2;
                return prefix + "Solo run, but a partner is connected: press Stop network (or switch to Condition B).";
            }
            if (!placementOk)
            {
                card = 1;
                return prefix + "Place the scene: stand on the tape mark facing the front wall, then press Place here.";
            }
            if (!armed)
            {
                card = ended ? 0 : 3;
                return prefix + "Ready - check the codes, then press ARM.";
            }
            card = 3;
            return "Armed - hand over the headset. The participant taps Begin briefing, then Start.";
        }

        private static readonly string[] BadWords =
        {
            "fail", "wrong", "error", "can't", "cannot", "could not", "couldn't", "refused", "not started", "lost",
            "timed out", "timeout", "denied", "disconnected", "unsupported", "not found", "missing",
        };
        private static readonly string[] WaitWords =
        {
            "waiting", "searching", "looking", "connecting", "joining", "loading", "sharing", "placing", "retry", "...",
        };

        /// <summary>
        /// Accent colour of an experimenter status message (StatusLabel): problems red, things in progress amber,
        /// everything else green. Keyword based, so new messages get a sensible colour without code changes.
        /// </summary>
        public static StatusLight MessageLight(string message)
        {
            if (string.IsNullOrEmpty(message)) return StatusLight.Off;
            string m = message.ToLowerInvariant();
            foreach (string w in BadWords) if (m.Contains(w)) return StatusLight.Bad;
            foreach (string w in WaitWords) if (m.Contains(w)) return StatusLight.Warn;
            return StatusLight.Ok;
        }

        public static Color ColorOf(StatusLight light)
        {
            switch (light)
            {
                case StatusLight.Ok: return new Color(0.20f, 0.72f, 0.38f, 1f);
                case StatusLight.Warn: return new Color(0.95f, 0.66f, 0.15f, 1f);
                case StatusLight.Bad: return new Color(0.88f, 0.24f, 0.20f, 1f);
                default: return new Color(0.45f, 0.48f, 0.53f, 1f);
            }
        }
    }
}
