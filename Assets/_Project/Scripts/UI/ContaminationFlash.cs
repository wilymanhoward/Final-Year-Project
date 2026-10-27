using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Red edge flash in the player's view when their own body enters an exclusion zone (a counted contamination).
    /// Creates itself at startup (no scene setup); partner contamination does not flash. Same in both conditions.
    /// </summary>
    public class ContaminationFlash : MonoBehaviour
    {
        private const float PeakIntensity = 0.75f;
        private const float FadeSeconds = 0.9f;

        private SessionManager _session;
        private Material _material;
        private MeshRenderer _renderer;
        private float _level;   // 1 right after an entry, fades to 0

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<ContaminationFlash>() != null || FindFirstObjectByType<SessionManager>() == null) return;
            new GameObject("ContaminationFlash").AddComponent<ContaminationFlash>();
        }

        private void Awake()
        {
            _session = FindFirstObjectByType<SessionManager>();
            var rig = FindFirstObjectByType<PlayerRig>();
            Shader shader = Resources.Load<Shader>("FYP_ScreenFlash");
            if (shader == null) { enabled = false; return; }

            _material = new Material(shader);
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1f, 1f, 0f) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);   // vertex shader ignores the transform; never cull it
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
            if (rig != null && rig.Head != null) transform.SetParent(rig.Head, false);
        }

        private void OnEnable()
        {
            if (_session != null) _session.State.ContaminationRecorded += HandleContamination;
        }

        private void OnDisable()
        {
            if (_session != null) _session.State.ContaminationRecorded -= HandleContamination;
        }

        private void HandleContamination(ContaminationInfo _) => _level = 1f;

        private void Update()
        {
            if (_level <= 0f) return;
            _level = Mathf.Max(0f, _level - Time.unscaledDeltaTime / FadeSeconds);
            _renderer.enabled = _level > 0f;
            _material.SetFloat("_Intensity", _level * PeakIntensity);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
