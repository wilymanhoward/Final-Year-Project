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

        /// <summary>
        /// Plausible head height above the floor of a standing experimenter. Outside this range the headset's floor
        /// estimate is probably wrong (headset test, session 6: head 2.00 m above the scene = floor ~0.3 m too low),
        /// so a placement would put the scene under (or above) the real floor.
        /// </summary>
        public const float MinStandingHeadHeight = 1.20f;
        public const float MaxStandingHeadHeight = 1.95f;

        public static bool IsHeadHeightPlausible(float headAboveFloor) =>
            headAboveFloor >= MinStandingHeadHeight && headAboveFloor <= MaxStandingHeadHeight;

        /// <summary>Label text when the floor estimate looks wrong at placement.</summary>
        public static string FloorLooksWrongMessage(float headAboveFloor)
        {
            string h = headAboveFloor.ToString("0.00", CultureInfo.InvariantCulture);
            string range = MinStandingHeadHeight.ToString("0.00", CultureInfo.InvariantCulture) + "-" +
                           MaxStandingHeadHeight.ToString("0.00", CultureInfo.InvariantCulture);
            return "Floor looks wrong: your head is " + h + " m above it (standing should be " + range + " m). " +
                   "Stand up straight, re-set the floor in Quest settings, or press A again within 5 s to place anyway.";
        }

        /// <summary>Label text when a loaded placement sits at an implausible height.</summary>
        public static string LoadedFloorLooksWrongMessage(float headAboveScene)
        {
            return "Loaded, but the scene floor looks wrong: your head is " +
                   headAboveScene.ToString("0.00", CultureInfo.InvariantCulture) +
                   " m above it - re-set the Quest floor and re-place with A";
        }

        /// <summary>Label text, e.g. "Placement height wrong (+0.62 m) - re-place".</summary>
        public static string WrongMessage(float offsetMeters)
        {
            return "Placement height wrong (" + offsetMeters.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " m) - re-place";
        }
    }
}
