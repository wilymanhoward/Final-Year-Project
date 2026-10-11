using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Pure rule for when a loose clue must be put back: it fell below the floor or ended up too far from
    /// CaseRoot. Positions are CaseRoot-local (floor = y 0).
    /// </summary>
    public static class ClueBounds
    {
        public const float DefaultMaxDistance = 3f;
        public const float DefaultBelowFloorMargin = 0.05f;

        public static bool IsOutOfBounds(Vector3 caseRootLocalPosition,
            float maxDistance = DefaultMaxDistance, float belowFloorMargin = DefaultBelowFloorMargin)
        {
            return caseRootLocalPosition.y < -belowFloorMargin
                || caseRootLocalPosition.sqrMagnitude > maxDistance * maxDistance;
        }
    }
}
