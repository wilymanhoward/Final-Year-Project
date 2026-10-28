using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Team partner's hands (user, session 6: "the same hand model the player has, accurate"). Sending side: reads this
    /// headset's visible Interaction SDK hand visuals (<see cref="HandVisual.Root"/> = wrist, <see cref="HandVisual.Joints"/>
    /// = finger joints; Runtime/Scripts/Interaction/Visuals/HandVisual.cs) into a <see cref="BodyPoseFrame"/>, CaseRoot-local.
    /// Receiving side: a copy of the same hand model per hand (the local hand visual cloned with all scripts removed),
    /// posed from the partner's frames with smoothing, in the partner-marker material, hidden when stale, untracked, or
    /// frozen (the same exact pose for <see cref="FrozenSeconds"/>: tracking lost but a last pose still sent).
    /// Both headsets run the same build, so the joint order matches.
    /// </summary>
    public class PartnerHands : MonoBehaviour
    {
        private const float Smoothing = 15f;
        // Real tracked hands always move a little; a pose that stays exactly the same this long is a frozen last pose.
        private const float FrozenSeconds = 0.4f;

        private CaseRoot _caseRoot;
        private HandVisual[] _visuals = new HandVisual[0];
        private readonly Copy _left = new Copy(Handedness.Left), _right = new Copy(Handedness.Right);
        private float _lastUpdate = -100f;

        private sealed class Copy
        {
            public readonly Handedness Side;
            public GameObject Holder;
            public Transform Root;
            public readonly List<Transform> Joints = new List<Transform>();
            public readonly HandFrame Target = new HandFrame();
            public bool Built, Snap = true;
            public Vector3 LastWrist;
            public Quaternion LastRotation;
            public float LastChange;
            public Copy(Handedness side) { Side = side; }
        }

        private void Awake()
        {
            _caseRoot = FindFirstObjectByType<CaseRoot>();
            _visuals = FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        // ---- Sending ----

        /// <summary>
        /// Fills the frame's hands from this headset's visible hand visuals. A hand counts as tracked only while the
        /// headset really tracks it (rig flag from HandTrackingMonitor, ISDK data valid and high confidence), so a lost
        /// hand is not sent as a frozen last pose.
        /// </summary>
        public void Capture(BodyPoseFrame frame, PlayerRig rig)
        {
            CaptureHand(Visible(Handedness.Left), frame.Left, rig == null || rig.IsTracked(BodyPart.LeftHand));
            CaptureHand(Visible(Handedness.Right), frame.Right, rig == null || rig.IsTracked(BodyPart.RightHand));
        }

        private void CaptureHand(HandVisual v, HandFrame h, bool rigTracked)
        {
            h.Tracked = rigTracked && v != null && _caseRoot != null && v.Root != null
                && v.Hand.IsTrackedDataValid && v.Hand.IsHighConfidence;
            if (!h.Tracked) return;
            Transform root = _caseRoot.transform;
            h.WristPosition = root.InverseTransformPoint(v.Root.position);
            h.WristRotation = Quaternion.Inverse(root.rotation) * v.Root.rotation;
            h.Scale = v.Root.localScale.x;
            h.Joints.Clear();
            foreach (Transform j in v.Joints) h.Joints.Add(j != null ? j.localRotation : Quaternion.identity);
        }

        // The hand visual this headset is showing now (hands; the controller-driven visuals are hidden then).
        private HandVisual Visible(Handedness side)
        {
            foreach (HandVisual v in _visuals)
                if (v != null && v.isActiveAndEnabled && v.IsVisible && v.Hand != null && v.Hand.Handedness == side) return v;
            return null;
        }

        // ---- Receiving ----

        /// <summary>The partner's latest frame (CaseRoot-local).</summary>
        public void Show(BodyPoseFrame frame)
        {
            if (Time.unscaledTime - _lastUpdate > PartnerMarker.StaleSeconds) _left.Snap = _right.Snap = true;
            CopyTarget(frame.Left, _left);
            CopyTarget(frame.Right, _right);
            _lastUpdate = Time.unscaledTime;
        }

        private static void CopyTarget(HandFrame from, Copy to)
        {
            if (from.Tracked && !to.Target.Tracked) to.Snap = true;   // reappearing hand jumps, doesn't fly in
            if (from.Tracked && (from.WristPosition != to.LastWrist || from.WristRotation != to.LastRotation))
            {
                to.LastWrist = from.WristPosition;
                to.LastRotation = from.WristRotation;
                to.LastChange = Time.unscaledTime;
            }
            to.Target.Tracked = from.Tracked;
            to.Target.WristPosition = from.WristPosition;
            to.Target.WristRotation = from.WristRotation;
            to.Target.Scale = from.Scale;
            to.Target.Joints.Clear();
            to.Target.Joints.AddRange(from.Joints);
        }

        private void Update()
        {
            bool fresh = Time.unscaledTime - _lastUpdate <= PartnerMarker.StaleSeconds;
            Drive(_left, fresh);
            Drive(_right, fresh);
        }

        private void Drive(Copy c, bool fresh)
        {
            bool frozen = Time.unscaledTime - c.LastChange > FrozenSeconds;
            bool show = fresh && c.Target.Tracked && !frozen && _caseRoot != null;
            if (show && !c.Built) Build(c);
            if (c.Holder == null) return;
            if (show && !c.Holder.activeSelf) c.Snap = true;   // back after being hidden (also frozen): jump, never streak in
            if (c.Holder.activeSelf != show) c.Holder.SetActive(show);
            if (!show || c.Root == null) return;
            float k = c.Snap ? 1f : 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime);
            c.Snap = false;
            Transform root = _caseRoot.transform;
            c.Root.SetPositionAndRotation(Vector3.Lerp(c.Root.position, root.TransformPoint(c.Target.WristPosition), k),
                Quaternion.Slerp(c.Root.rotation, root.rotation * c.Target.WristRotation, k));
            c.Root.localScale = Vector3.one * c.Target.Scale;
            int n = Mathf.Min(c.Joints.Count, c.Target.Joints.Count);
            for (int i = 0; i < n; i++)
                if (c.Joints[i] != null) c.Joints[i].localRotation = Quaternion.Slerp(c.Joints[i].localRotation, c.Target.Joints[i], k);
        }

        // Copies this headset's hand model for that side (inactive first, so none of its scripts ever run), keeps only
        // the mesh and bones, and maps the joints by their path so they line up with the sender's joint order.
        private void Build(Copy c)
        {
            c.Built = true;
            HandVisual template = null;
            foreach (HandVisual v in _visuals)
                if (v != null && v.Hand != null && v.Hand.Handedness == c.Side && v.Root != null && v.Joints.Count > 0) { template = v; break; }
            var marker = FindFirstObjectByType<PartnerMarker>();
            if (template == null || marker == null || marker.Material == null) return;

            var holder = new GameObject("PartnerHand" + c.Side);
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            GameObject clone = Instantiate(template.gameObject, holder.transform);
            foreach (MonoBehaviour b in clone.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(b);
            // Anything another component still required survives DestroyImmediate: switch it off, so it never runs.
            foreach (MonoBehaviour b in clone.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            foreach (SkinnedMeshRenderer smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = new Material[smr.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = marker.Material;
                smr.sharedMaterials = mats;
                smr.enabled = true;
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // Active states are copied as they are (the hand visual keeps only one skeleton variant active).
            c.Root = Find(template.transform, clone.transform, template.Root);
            foreach (Transform j in template.Joints) c.Joints.Add(j != null ? Find(template.transform, clone.transform, j) : null);
            if (c.Root == null) { Destroy(holder); return; }
            c.Holder = holder;   // shown by Drive; the root is posed directly in world space
        }

        private static Transform Find(Transform templateRoot, Transform cloneRoot, Transform target)
        {
            var path = new List<int>();
            for (Transform t = target; t != null && t != templateRoot; t = t.parent) path.Add(t.GetSiblingIndex());
            Transform result = cloneRoot;
            for (int i = path.Count - 1; i >= 0 && result != null; i--)
                result = path[i] < result.childCount ? result.GetChild(path[i]) : null;
            return result;
        }
    }
}
