using System;

namespace FYP.Detective
{
    /// <summary>
    /// Limits of the floor check (user, 2026-11-12: after the headset is taken off and put on again the Quest's floor, and
    /// the anchor with it, can come back ~40 cm off). The floor is set by touch (<see cref="TouchFloorDetector"/>); the
    /// depth-sensor estimate was removed on 2026-11-19 (sometimes the wrong height).
    /// </summary>
    public static class FloorEstimate
    {
        /// <summary>
        /// Smallest difference that moves the scene: <see cref="ManualCorrectionThreshold"/> for a touch (someone asked for
        /// it), <see cref="AutoCorrectionThreshold"/> is the default of <see cref="SessionManager.CorrectFloor"/>.
        /// </summary>
        public const float AutoCorrectionThreshold = 0.015f;
        public const float ManualCorrectionThreshold = 0.005f;
        /// <summary>A touched height further than this from the Quest's floor is refused (a table or a knee).</summary>
        public const float MaxCorrection = 0.8f;

        /// <summary>True when the scene floor is further than <paramref name="threshold"/> from the measured floor.</summary>
        public static bool NeedsCorrection(float sceneFloorY, float measuredFloorY, float threshold = AutoCorrectionThreshold) =>
            Math.Abs(measuredFloorY - sceneFloorY) > threshold;
    }
}
