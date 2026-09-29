using System;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// A rectangle on the floor (centre, yaw, width x depth). Points are projected onto
    /// the floor, i.e. height (y) is ignored. Pure maths, unit tested.
    /// </summary>
    public static class FloorFootprint
    {
        /// <param name="point">World point to test (y ignored).</param>
        /// <param name="center">Rectangle centre (y ignored).</param>
        /// <param name="yawDegrees">Rotation of the rectangle around world up.</param>
        /// <param name="size">x = width along the rectangle's local X, y = depth along its local Z.</param>
        public static bool Contains(Vector3 point, Vector3 center, float yawDegrees, Vector2 size)
        {
            double dx = point.x - center.x;
            double dz = point.z - center.z;

            // Rotate the offset by -yaw into the rectangle's local frame.
            // Unity yaw is clockwise seen from above: local X = (cos, -sin), local Z = (sin, cos) in (x, z).
            double rad = yawDegrees * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double localX = dx * cos - dz * sin;
            double localZ = dx * sin + dz * cos;

            return Math.Abs(localX) <= size.x * 0.5 && Math.Abs(localZ) <= size.y * 0.5;
        }
    }
}
