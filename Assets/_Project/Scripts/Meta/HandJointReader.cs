using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Hand-joint source for the hand log (<see cref="HandJointCsv"/>): world poses of every joint of the hand visual this
    /// headset is showing, via <c>IHand.GetJointPose(HandJointId, out Pose)</c> (Runtime/Scripts/Input/Hands/IHand.cs). The
    /// installed ISDK always defines ISDK_OPENXR_HAND (Runtime/Oculus.Interaction.asmdef), so HandJointId is the OpenXR
    /// set (Runtime/OpenXR/OpenXRHandPrimitives.cs: palm 0, wrist 1 .. pinky tip 25), which matches JointNames + 1.
    /// Installs itself after the scene loads (no scene setup) and registers in <see cref="HandJointSources"/>.
    /// </summary>
    public class HandJointReader : MonoBehaviour, IHandJointSource
    {
        private HandVisual[] _visuals = new HandVisual[0];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<HandJointReader>() != null || FindFirstObjectByType<SessionManager>() == null) return;
            new GameObject("HandJointReader").AddComponent<HandJointReader>();
        }

        private void Awake()
        {
            if ((int)HandJointId.HandPinkyTip != HandJointCsv.JointCount)
            {
                Debug.LogError("[HandJointReader] Unexpected HandJointId layout - hand joints are not logged.");
                enabled = false;
                return;
            }
            _visuals = FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private void OnEnable() => HandJointSources.Current = this;

        private void OnDisable()
        {
            if (ReferenceEquals(HandJointSources.Current, this)) HandJointSources.Current = null;
        }

        public bool TryGetJoints(BodyPart hand, Vector3[] worldPositions)
        {
            HandVisual v = PartnerHands.Visible(_visuals, hand == BodyPart.LeftHand ? Handedness.Left : Handedness.Right);
            if (v == null || !v.Hand.IsTrackedDataValid) return false;
            for (int i = 0; i < HandJointCsv.JointCount; i++)
            {
                if (!v.Hand.GetJointPose((HandJointId)(i + 1), out Pose pose)) return false;
                worldPositions[i] = pose.position;
            }
            return true;
        }
    }
}
