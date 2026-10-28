using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Where the player's feet probably are, from the head pose (Quest 3 tracks no feet). The eyes sit in front of the
    /// body's axis, and looking down usually means leaning over, which moves the head further forward while the feet
    /// stay put. So the feet are estimated behind the head's floor position: <see cref="BaseBack"/> when looking level,
    /// plus <see cref="LeanBack"/> x sin(look-down angle), at most <see cref="MaxBack"/>. Used for the head's
    /// contamination check (user, session 6: "only when my feet step in"). Pure maths, unit tested.
    /// </summary>
    /// <remarks>shortcut: a heuristic, not tracking - someone leaning far with a level head still counts; upgrade to
    /// Meta body tracking (hips) if pilots show false entries.</remarks>
    public static class FootEstimate
    {
        public const float BaseBack = 0.08f;
        public const float LeanBack = 0.25f;
        public const float MaxBack = 0.30f;

        /// <summary>Estimated foot point on the floor plane of the head (y = head y; zones ignore height).</summary>
        public static Vector3 FromHead(Vector3 headPosition, Quaternion headRotation)
        {
            Vector3 forward = headRotation * Vector3.forward;
            // forward + up stays horizontal-meaningful even when looking straight down (then up points ahead).
            Vector3 heading = forward + headRotation * Vector3.up;
            heading.y = 0f;
            if (heading.sqrMagnitude < 1e-6f) return headPosition;
            heading.Normalize();
            float lookDown = Mathf.Clamp01(-forward.y);   // sin of the look-down angle
            float back = Mathf.Min(MaxBack, BaseBack + LeanBack * lookDown);
            return headPosition - heading * back;
        }
    }
}
