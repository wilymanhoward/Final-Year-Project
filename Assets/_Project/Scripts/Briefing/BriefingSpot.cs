using System;

namespace FYP.Detective
{
    /// <summary>A player's floor position and facing in CaseRoot space (x right, z to the front wall; yaw in degrees).</summary>
    public readonly struct PlayerPose2D
    {
        public readonly float X, Z, Yaw;
        public PlayerPose2D(float x, float z, float yaw) { X = x; Z = z; Yaw = yaw; }
    }

    /// <summary>
    /// Team briefing (user decision, session 6): where the detective stands so that BOTH players see him in the same
    /// physical place - in front of the pair (along their average facing), at least <c>minDistance</c> from each, inside
    /// the play area, facing the point between them. Also carries the two players' positions (A = host, B = client) so
    /// each headset can make him look at either player. CaseRoot-local, pure, unit tested.
    /// </summary>
    public readonly struct BriefingSpot
    {
        public readonly float X, Z, Yaw;
        public readonly float AX, AZ, BX, BZ;

        public BriefingSpot(float x, float z, float yaw, float ax, float az, float bx, float bz)
        {
            X = x; Z = z; Yaw = yaw; AX = ax; AZ = az; BX = bx; BZ = bz;
        }

        public static BriefingSpot Compute(PlayerPose2D a, PlayerPose2D b, float distance = 1.6f, float halfSize = 2.2f,
            float minDistance = 1.1f)
        {
            float mx = (a.X + b.X) * 0.5f, mz = (a.Z + b.Z) * 0.5f;
            double ra = a.Yaw * Math.PI / 180.0, rb = b.Yaw * Math.PI / 180.0;
            float fx = (float)(Math.Sin(ra) + Math.Sin(rb)), fz = (float)(Math.Cos(ra) + Math.Cos(rb));
            float len = (float)Math.Sqrt(fx * fx + fz * fz);
            if (len < 0.2f)
            {
                // Facing opposite ways: stand to the side of the pair, towards the room centre.
                fx = -(b.Z - a.Z); fz = b.X - a.X;
                if (fx * -mx + fz * -mz < 0f) { fx = -fx; fz = -fz; }
                len = (float)Math.Sqrt(fx * fx + fz * fz);
                if (len < 1e-4f) { fx = 0f; fz = 1f; len = 1f; }
            }
            fx /= len; fz /= len;

            float x = mx + fx * distance, z = mz + fz * distance;
            // Not too close to either player (e.g. when they stand far apart).
            for (int i = 0; i < 8; i++)
            {
                if (Dist(x, z, a.X, a.Z) >= minDistance && Dist(x, z, b.X, b.Z) >= minDistance) break;
                x += fx * 0.15f; z += fz * 0.15f;
            }
            x = Clamp(x, -halfSize, halfSize);
            z = Clamp(z, -halfSize, halfSize);
            float yaw = (float)(Math.Atan2(mx - x, mz - z) * 180.0 / Math.PI);   // face the point between the players
            return new BriefingSpot(x, z, yaw, a.X, a.Z, b.X, b.Z);
        }

        private static float Dist(float x0, float z0, float x1, float z1) =>
            (float)Math.Sqrt((x0 - x1) * (x0 - x1) + (z0 - z1) * (z0 - z1));

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
