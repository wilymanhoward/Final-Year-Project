using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Shows where the team partner's head is (user, session 6: hard to see them without depth occlusion): the neutral
    /// low-poly head from Maya (Resources/PartnerHead.fbx, origin between the eyes, front +Z) in translucent cyan, drawn
    /// over the virtual props (shader FYP/PartnerMarker). Fed with the partner's CaseRoot-local head pose
    /// ~15 times a second by the team sync; smoothed between updates, hidden after <see cref="StaleSeconds"/> without
    /// one. Their hands are drawn by the Meta-side PartnerHands. Creates itself at startup (no scene setup). Not logged.
    /// </summary>
    public class PartnerMarker : MonoBehaviour
    {
        public const float StaleSeconds = 1f;
        private const float Smoothing = 15f;   // 1/s; follows a 15 Hz stream without visible steps

        private CaseRoot _caseRoot;
        private Transform _head;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private float _lastUpdate = -100f;
        private bool _snap = true;

        /// <summary>Material of the partner markers (also used by the partner's hands).</summary>
        public Material Material { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<PartnerMarker>() != null || FindFirstObjectByType<CaseRoot>() == null) return;
            new GameObject("PartnerMarker").AddComponent<PartnerMarker>();
        }

        private void Awake()
        {
            _caseRoot = FindFirstObjectByType<CaseRoot>();
            Shader shader = Resources.Load<Shader>("FYP_PartnerMarker");
            if (shader == null || _caseRoot == null) { enabled = false; return; }
            Material = new Material(shader);

            var head = new GameObject("PartnerHead");
            head.transform.SetParent(transform, false);
            Mesh mesh = Resources.Load<Mesh>("PartnerHead");
            if (mesh == null)   // FBX missing: fall back to a head-sized ellipsoid
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                mesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                Destroy(sphere);
                head.transform.localScale = new Vector3(0.18f, 0.24f, 0.21f);
            }
            head.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = head.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _head = head.transform;
            head.SetActive(false);
        }

        /// <summary>The partner's latest head pose, CaseRoot-local.</summary>
        public void Show(Vector3 headPosition, Quaternion headRotation)
        {
            if (!enabled) return;
            if (Time.unscaledTime - _lastUpdate > StaleSeconds) _snap = true;   // reappearing: jump, don't glide in
            _targetPosition = headPosition;
            _targetRotation = headRotation;
            _lastUpdate = Time.unscaledTime;
        }

        private void Update()
        {
            bool fresh = Time.unscaledTime - _lastUpdate <= StaleSeconds;
            if (_head.gameObject.activeSelf != fresh) _head.gameObject.SetActive(fresh);
            if (!fresh) return;
            Transform root = _caseRoot.transform;
            float k = _snap ? 1f : 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime);
            _snap = false;
            _head.SetPositionAndRotation(Vector3.Lerp(_head.position, root.TransformPoint(_targetPosition), k),
                Quaternion.Slerp(_head.rotation, root.rotation * _targetRotation, k));
        }
    }
}
