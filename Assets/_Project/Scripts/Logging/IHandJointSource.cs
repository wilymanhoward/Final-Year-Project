using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Gives the world positions of a tracked hand's joints in <see cref="HandJointCsv.JointNames"/> order. Implemented in
    /// the Meta assembly (HandJointReader, Interaction SDK) so the core assembly stays free of Meta types.
    /// </summary>
    public interface IHandJointSource
    {
        /// <summary>False when the hand isn't tracked now; <paramref name="worldPositions"/> is then unchanged.</summary>
        bool TryGetJoints(BodyPart hand, Vector3[] worldPositions);
    }

    /// <summary>The active joint source (null in the Editor without hands or when none was installed).</summary>
    public static class HandJointSources
    {
        public static IHandJointSource Current { get; set; }
    }
}
