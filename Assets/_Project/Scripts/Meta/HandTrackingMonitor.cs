using UnityEngine;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Pushes per-hand tracking state into <see cref="PlayerRig"/>. A hand side counts as tracked when its
    /// OVRHand reports <c>IsTracked</c> (Library/PackageCache/com.meta.xr.sdk.core/Scripts/Util/OVRHand.cs) or its
    /// Touch controller position is tracked (<c>OVRInput.GetControllerPositionTracked</c>, Scripts/OVRInput.cs),
    /// because Left/RightHandAnchor follow whichever is active. Untracked hands are then left out of the log
    /// (empty cells) and ignored by exclusion zones; changes are logged as hand_tracking_lost / _regained.
    /// Runs before zones and the logger.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class HandTrackingMonitor : MonoBehaviour
    {
        [SerializeField] private PlayerRig rig;
        [Tooltip("OVRHand under LeftHandAnchor. Empty = found under the rig's left hand anchor.")]
        [SerializeField] private OVRHand leftHand;
        [Tooltip("OVRHand under RightHandAnchor. Empty = found under the rig's right hand anchor.")]
        [SerializeField] private OVRHand rightHand;

        private void Awake()
        {
            if (rig == null) rig = FindFirstObjectByType<PlayerRig>();
            if (rig == null) return;
            if (leftHand == null && rig.LeftHand != null) leftHand = rig.LeftHand.GetComponentInChildren<OVRHand>(true);
            if (rightHand == null && rig.RightHand != null) rightHand = rig.RightHand.GetComponentInChildren<OVRHand>(true);
        }

        private void Update()
        {
            if (rig == null) return;
            rig.SetTracked(BodyPart.LeftHand, IsTracked(leftHand, OVRInput.Controller.LTouch));
            rig.SetTracked(BodyPart.RightHand, IsTracked(rightHand, OVRInput.Controller.RTouch));
        }

        private static bool IsTracked(OVRHand hand, OVRInput.Controller controller)
        {
            return (hand != null && hand.IsTracked) || OVRInput.GetControllerPositionTracked(controller);
        }
    }
}
