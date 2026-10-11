using System;
using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Holds the tracked transforms of the local player so other scripts don't each
    /// need their own references. With OVRCameraRig / the Camera Rig building block,
    /// assign TrackingSpace/CenterEyeAnchor, LeftHandAnchor and RightHandAnchor.
    /// Later, a networked version will exist per player.
    /// Tracking state per point is pushed in by an adapter (e.g. HandTrackingMonitor reading OVRHand /
    /// OVRInput); untracked points are left out of samples and ignored by exclusion zones. Default = tracked.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;

        public Transform Head => head;
        public Transform LeftHand => leftHand;
        public Transform RightHand => rightHand;

        private readonly bool[] _tracked = { true, true, true };   // indexed by BodyPart

        /// <summary>Raised when a point loses or regains tracking (part, isTracked now).</summary>
        public event Action<BodyPart, bool> TrackingChanged;

        public bool IsTracked(BodyPart part)
        {
            int i = (int)part;
            return i >= 0 && i < _tracked.Length && _tracked[i];
        }

        public void SetTracked(BodyPart part, bool tracked)
        {
            int i = (int)part;
            if (i < 0 || i >= _tracked.Length || _tracked[i] == tracked) return;
            _tracked[i] = tracked;
            TrackingChanged?.Invoke(part, tracked);
        }

        /// <summary>Transform of a point, or null.</summary>
        public Transform Get(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Head: return head;
                case BodyPart.LeftHand: return leftHand;
                case BodyPart.RightHand: return rightHand;
                default: return null;
            }
        }

        /// <summary>True if the point exists, is active and is currently tracked.</summary>
        public bool IsAvailable(BodyPart part) => IsUsable(Get(part)) && IsTracked(part);

        /// <summary>Current pose of head and hands. Missing/inactive/untracked points are flagged, not zeroed.</summary>
        public PoseSample Sample()
        {
            var s = new PoseSample();
            if (IsAvailable(BodyPart.Head))
            {
                s.HasHead = true;
                head.GetPositionAndRotation(out s.HeadPosition, out s.HeadRotation);
            }
            if (IsAvailable(BodyPart.LeftHand))
            {
                s.HasLeftHand = true;
                s.LeftHandPosition = leftHand.position;
            }
            if (IsAvailable(BodyPart.RightHand))
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
