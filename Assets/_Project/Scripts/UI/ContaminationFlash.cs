using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Red flash in the player's view plus a short alarm buzz when their own body enters an exclusion zone (a counted
    /// contamination). The buzz is generated in code (no audio asset). Creates itself at startup (no scene setup);
    /// partner contamination does not flash or buzz. Same in both conditions.
    /// </summary>
    public class ContaminationFlash : MonoBehaviour
    {
        private const float PeakIntensity = 0.95f;   // user: 0.75 was too transparent
        private const float FadeSeconds = 1.0f;
        private const float BuzzVolume = 0.8f;

        private AudioSource _audio;
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

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = MakeBuzz();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;   // 2D: always heard the same, wherever the head turns
            _audio.volume = BuzzVolume;
        }

        // Two short descending buzzes (880 Hz, then 660 Hz), square-ish so they cut through the room, with soft edges.
        private static AudioClip MakeBuzz()
        {
            const int rate = 44100;
            const float beep = 0.14f, gap = 0.05f;
            int n = Mathf.CeilToInt(rate * (2f * beep + gap));
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                bool second = t >= beep + gap;
                float local = second ? t - beep - gap : t;
                if (!second && t >= beep) continue;   // the gap
                float freq = second ? 660f : 880f;
                float wave = Mathf.Sin(2f * Mathf.PI * freq * t);
                wave = Mathf.Clamp(wave * 3f, -1f, 1f) * 0.6f;   // soft-clipped sine = buzzy, not harsh
                float fade = Mathf.Clamp01(local / 0.01f) * Mathf.Clamp01((beep - local) / 0.02f);
                data[i] = wave * fade;
            }
            var clip = AudioClip.Create("ContaminationBuzz", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void OnEnable()
        {
            if (_session != null) _session.State.ContaminationRecorded += HandleContamination;
        }

        private void OnDisable()
        {
            if (_session != null) _session.State.ContaminationRecorded -= HandleContamination;
        }

        private void HandleContamination(ContaminationInfo _)
        {
            _level = 1f;
            if (_audio != null) _audio.Play();   // restarts if a second entry follows quickly
        }

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
