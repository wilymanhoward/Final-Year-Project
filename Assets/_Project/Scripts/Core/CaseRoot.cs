using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Parent of all case content (furniture, clues, exclusion zones, evidence board). Placement moves
    /// this transform onto the experimenter's floor mark, and the 10 Hz log records positions relative
    /// to it. Keep exactly one in the scene, unscaled.
    /// </summary>
    [DisallowMultipleComponent]
    public class CaseRoot : MonoBehaviour
    {
        /// <summary>Detaches from any anchor and moves to a world pose.</summary>
        public void PlaceAt(Pose worldPose)
        {
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
        }

        /// <summary>Follows an anchor from now on: becomes its child with zero local offset.</summary>
        public void AttachTo(Transform anchor)
        {
            transform.SetParent(anchor, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>Unparents while keeping the current world pose (call before an anchor object is destroyed).</summary>
        public void Detach()
        {
            transform.SetParent(null, true);
        }

        /// <summary>Converts a world-space pose sample into this root's local frame.</summary>
        public PoseSample ToLocal(in PoseSample worldSample)
        {
            return worldSample.ToLocal(transform.position, transform.rotation);
        }
    }
}
