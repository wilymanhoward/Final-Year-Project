using System.Collections.Generic;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Decides when a clue's pose is worth a log row (for the replay viewer): the first time an id is seen, then only
    /// when it has moved more than <see cref="PositionStep"/> or turned more than <see cref="AngleStep"/> since its last
    /// logged pose, so still clues cost nothing.
    /// </summary>
    public class ObjectPoseFilter
    {
        public const float PositionStep = 0.005f;   // m
        public const float AngleStep = 2f;          // degrees

        private readonly Dictionary<string, (Vector3 pos, Quaternion rot)> _last = new Dictionary<string, (Vector3, Quaternion)>();

        /// <summary>True (and remembers the pose) if this pose should be logged.</summary>
        public bool ShouldLog(string id, Vector3 position, Quaternion rotation)
        {
            if (_last.TryGetValue(id, out var last)
                && (position - last.pos).sqrMagnitude <= PositionStep * PositionStep
                && Quaternion.Angle(rotation, last.rot) <= AngleStep)
                return false;
            _last[id] = (position, rotation);
            return true;
        }

        public void Reset() => _last.Clear();
    }
}
