using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Pure rules for when a loose clue must be put back: it fell below the floor or ended up too far from
    /// CaseRoot (positions CaseRoot-local, floor = y 0), or it came to rest inside an exclusion zone.
    /// </summary>
    public static class ClueBounds
    {
        /// <summary>Just past the corners of the 5 x 5 m play area (2.5 * sqrt 2 = 3.54 m), so only a clue carried out of the room resets.</summary>
        public const float DefaultMaxDistance = 3.6f;
        public const float DefaultBelowFloorMargin = 0.05f;

        /// <summary>A dropped clue counts as in a zone if its centre is within this margin of the zone rectangle.</summary>
        public const float DefaultZoneMargin = 0.05f;

        /// <summary>
        /// True if a resting clue lies in (or within <paramref name="margin"/> of) an exclusion zone's floor rectangle,
        /// at any height: picking it up would mean reaching into the zone, which the layout never asks for.
        /// </summary>
        public static bool IsInZone(Vector3 cluePosition, Vector3 zoneCentre, float zoneYawDegrees, Vector2 zoneSize,
            float margin = DefaultZoneMargin)
        {
            return FloorFootprint.Contains(cluePosition, zoneCentre, zoneYawDegrees, zoneSize + new Vector2(2f * margin, 2f * margin));
        }

        /// <summary>Log text for a <see cref="ClueResetReason"/> (the <c>reason=</c> field of <c>clue_reset</c>).</summary>
        public static string ReasonText(ClueResetReason reason) => reason == ClueResetReason.InZone ? "in_zone" : "out_of_bounds";

        public static bool IsOutOfBounds(Vector3 caseRootLocalPosition,
            float maxDistance = DefaultMaxDistance, float belowFloorMargin = DefaultBelowFloorMargin)
        {
            return caseRootLocalPosition.y < -belowFloorMargin
                || caseRootLocalPosition.sqrMagnitude > maxDistance * maxDistance;
        }
    }

    /// <summary>Why a clue went back to its start position (the <c>reason=</c> field of <c>clue_reset</c>).</summary>
    public enum ClueResetReason
    {
        OutOfBounds = 0,
        InZone = 1,
    }
}
