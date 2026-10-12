using System;

namespace FYP.Detective
{
    /// <summary>
    /// Says whether the headset's tracking space has been stable for <see cref="SettleSeconds"/> since the last
    /// change (recenter). Placement waits for this so an anchor isn't loaded or created in the middle of a
    /// recenter burst. The start time counts as a change, so the first seconds after launch also wait.
    /// Pure logic; the caller passes the clock in.
    /// </summary>
    public sealed class TrackingSettleGate
    {
        public const double DefaultSettleSeconds = 3.0;

        public double SettleSeconds { get; set; }
        public double LastChange { get; private set; }

        public TrackingSettleGate(double settleSeconds = DefaultSettleSeconds, double startTime = 0)
        {
            SettleSeconds = settleSeconds;
            LastChange = startTime;
        }

        public void NoteChange(double now) => LastChange = Math.Max(LastChange, now);

        public bool IsSettled(double now) => now - LastChange >= SettleSeconds;

        public double SecondsUntilSettled(double now) => Math.Max(0, SettleSeconds - (now - LastChange));
    }
}
