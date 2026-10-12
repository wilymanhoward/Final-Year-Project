using System;
using System.Globalization;

namespace FYP.Detective
{
    /// <summary>
    /// Pure rule for the placement height check: CaseRoot must sit on the tracking floor (within
    /// <see cref="DefaultTolerance"/>). A loaded anchor can come back too high or too low when the headset's
    /// tracking space (floor + yaw) changed between saving and loading, e.g. after a recenter burst at launch.
    /// </summary>
    public static class PlacementHeight
    {
        public const float DefaultTolerance = 0.05f;

        public static bool IsOk(float offsetMeters, float tolerance = DefaultTolerance)
        {
            return Math.Abs(offsetMeters) <= tolerance;
        }

        /// <summary>Label text, e.g. "Placement height wrong (+0.62 m) - re-place".</summary>
        public static string WrongMessage(float offsetMeters)
        {
            return "Placement height wrong (" + offsetMeters.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " m) - re-place";
        }
    }
}
