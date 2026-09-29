using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Holds the tracked transforms of the local player so other scripts don't each
    /// need their own references. With OVRCameraRig / the Camera Rig building block,
    /// assign TrackingSpace/CenterEyeAnchor, LeftHandAnchor and RightHandAnchor.
    /// Later, a networked version will exist per player.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;

        public Transform Head => head;
        public Transform LeftHand => leftHand;
        public Transform RightHand => rightHand;

        /// <summary>Current pose of head and hands. Missing/inactive transforms are flagged, not zeroed.</summary>
        public PoseSample Sample()
        {
            var s = new PoseSample();
            if (IsUsable(head))
            {
                s.HasHead = true;
                head.GetPositionAndRotation(out s.HeadPosition, out s.HeadRotation);
            }
            if (IsUsable(leftHand))
            {
                s.HasLeftHand = true;
                s.LeftHandPosition = leftHand.position;
            }
            if (IsUsable(rightHand))
            {
                s.HasRightHand = true;
                s.RightHandPosition = rightHand.position;
            }
            return s;
        }

        private static bool IsUsable(Transform t) => t != null && t.gameObject.activeInHierarchy;

        private void Reset()
        {
            // Convenience when the component is added to the rig root: find anchors by their OVRCameraRig names.
            head = FindChildRecursive(transform, "CenterEyeAnchor");
            leftHand = FindChildRecursive(transform, "LeftHandAnchor");
            rightHand = FindChildRecursive(transform, "RightHandAnchor");
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
