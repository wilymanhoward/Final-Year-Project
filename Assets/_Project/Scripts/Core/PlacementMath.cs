using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Pure maths for placing the case from the experimenter's head pose: position = head (x, z) on the
    /// floor, rotation = head heading (yaw) only, so the case is always level whatever the head tilt.
    /// </summary>
    public static class PlacementMath
    {
        // Below this the forward vector is nearly vertical (looking straight down/up) and has no usable heading.
        private const float MinHorizontalLength = 0.2f;

        /// <summary>Floor pose under the head, facing the way the head faces.</summary>
        public static Pose FloorPoseFromHead(Vector3 headPosition, Quaternion headRotation, float floorY = 0f)
        {
            float yaw = HeadingDegrees(headRotation);
            return new Pose(new Vector3(headPosition.x, floorY, headPosition.z), Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>
        /// Heading of the face around world up, in degrees (0 = +Z, 90 = +X, range -180..180).
        /// Ignores pitch and roll.
        /// </summary>
        /// <summary>
        /// Team placement (session 6 fix): the host's shared anchor sits at the HOST headset's floor estimate, which
        /// differs from the client's by a few cm. Keep the anchor's floor position (x, z) and heading, but put the scene
        /// on THIS headset's floor (<paramref name="floorY"/>) and level it, so it stands on the real floor in both views.
        /// </summary>
        public static Pose FloorAlignedPose(Vector3 anchorPosition, Quaternion anchorRotation, float floorY)
        {
            return new Pose(new Vector3(anchorPosition.x, floorY, anchorPosition.z),
                Quaternion.Euler(0f, HeadingDegrees(anchorRotation), 0f));
        }

        public static float HeadingDegrees(Quaternion headRotation)
        {
            Vector3 forward = headRotation * Vector3.forward;
            var horizontal = new Vector2(forward.x, forward.z);
            if (horizontal.magnitude < MinHorizontalLength)
            {
                // Looking straight down, the head's up vector points the way the face is turned
                // (looking straight up, the down vector does).
                Vector3 up = headRotation * Vector3.up;
                horizontal = forward.y < 0f ? new Vector2(up.x, up.z) : new Vector2(-up.x, -up.z);
            }
            if (horizontal.sqrMagnitude < 1e-8f) return 0f;
            return Mathf.Atan2(horizontal.x, horizontal.y) * Mathf.Rad2Deg;
        }
    }
}
