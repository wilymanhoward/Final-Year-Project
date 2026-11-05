using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Celebration of a solved case (supervisor, 2026-11-05): when the submitted answer names the right culprit, a burst
    /// of confetti (<see cref="ConfettiBurst"/>) goes up in front of the player and a short fanfare
    /// (<see cref="SoundSynth.Fanfare"/>) plays right after the "case closed" stamp. Nothing for a wrong answer (the
    /// detective's verdict says it calmly). Fires on both headsets of a team (CaseState.AnswerSubmitted). Creates itself at
    /// startup (no scene setup), like ContaminationFlash; not logged.
    /// </summary>
    public class VerdictCelebration : MonoBehaviour
    {
        private const int Pieces = 160;
        private const float FanfareDelay = 0.75f;   // after the case-closed stamp (UiSounds)
        private const float PieceLength = 0.03f, PieceWidth = 0.018f;

        private SessionManager _session;
        private CaseRoot _floor;
        private AudioSource _audio;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private ConfettiBurst _burst;
        private Vector3[] _vertices;
        private Color32[] _colours;
        private float _fanfareAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<VerdictCelebration>() != null || FindFirstObjectByType<SessionManager>() == null) return;
            new GameObject("VerdictCelebration").AddComponent<VerdictCelebration>();
        }

        private void Awake()
        {
            _session = FindFirstObjectByType<SessionManager>();
            _floor = FindFirstObjectByType<CaseRoot>();
            _audio = gameObject.AddComponent<AudioSource>();
            float[] data = SoundSynth.Fanfare();
            var clip = AudioClip.Create("Fanfare", data.Length, 1, SoundSynth.SampleRate, false);
            clip.SetData(data, 0);
            _audio.clip = clip;
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.volume = 0.7f;

            Shader shader = Resources.Load<Shader>("FYP_Confetti");
            if (shader == null) return;
            _vertices = new Vector3[Pieces * 4];
            _colours = new Color32[Pieces * 4];
            var triangles = new int[Pieces * 6];
            for (int i = 0; i < Pieces; i++)
            {
                int v = i * 4, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            _mesh = new Mesh { name = "Confetti" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.colors32 = _colours;
            _mesh.triangles = triangles;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);   // world-space vertices; never cull it
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = new Material(shader);
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        private void OnEnable()
        {
            if (_session != null) _session.State.AnswerSubmitted += HandleAnswer;
        }

        private void OnDisable()
        {
            if (_session != null) _session.State.AnswerSubmitted -= HandleAnswer;
        }

        private void HandleAnswer(AnswerResult result)
        {
            if (result == null || !result.IsCorrect) return;
            _fanfareAt = Time.unscaledTime + FanfareDelay;
            Camera cam = Camera.main;
            if (_mesh == null || cam == null) return;
            Transform head = cam.transform;
            Vector3 forward = head.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            float floorY = _floor != null ? _floor.transform.position.y : head.position.y - 1.6f;
            _burst = new ConfettiBurst(Pieces, head.position + forward * 1.1f + Vector3.up * 0.1f, floorY, 2026);
            _renderer.enabled = true;
        }

        private void Update()
        {
            if (_fanfareAt > 0f && Time.unscaledTime >= _fanfareAt)
            {
                _fanfareAt = -1f;
                _audio.Play();
            }
            if (_burst == null) return;
            if (!_burst.Step(Time.unscaledDeltaTime))
            {
                _burst = null;
                _renderer.enabled = false;
                return;
            }
            float s = _burst.Scale;
            Vector3 a = new Vector3(PieceLength, 0f, PieceWidth) * 0.5f * s, b = new Vector3(PieceLength, 0f, -PieceWidth) * 0.5f * s;
            for (int i = 0; i < Pieces; i++)
            {
                Quaternion r = _burst.Rotation[i];
                Vector3 p = _burst.Position[i], ra = r * a, rb = r * b;
                int v = i * 4;
                _vertices[v] = p - ra; _vertices[v + 1] = p - rb; _vertices[v + 2] = p + ra; _vertices[v + 3] = p + rb;
                // Glint: brighter when the piece faces up or towards the light, so the falling paper flickers.
                float glint = 0.55f + 0.45f * Mathf.Abs((r * Vector3.up).y);
                Color32 c = _burst.Colour[i];
                var shaded = new Color32((byte)(c.r * glint), (byte)(c.g * glint), (byte)(c.b * glint), 255);
                _colours[v] = _colours[v + 1] = _colours[v + 2] = _colours[v + 3] = shaded;
            }
            _mesh.vertices = _vertices;
            _mesh.colors32 = _colours;
        }
    }
}
