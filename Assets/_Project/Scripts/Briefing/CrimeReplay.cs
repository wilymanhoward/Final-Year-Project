using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// The crime replay after the verdict (supervisor + user, 2026-11-05, Phase 2): two see-through ghost figures (culprit
    /// red, victim blue; copies of the detective's body rig in FYP/Ghost) re-enact the crime at the real spots while
    /// Inspector Hart narrates. Each beat of <see cref="RevealScript"/> starts when the detective starts its line
    /// (<see cref="DetectiveBriefing.LineStarted"/>) and lasts as long as the line, so voice and action stay together; the
    /// clue made at that moment glows (its hover shell) and the time ("21:42") floats above the culprit. Poses are
    /// body-part rotations eased towards their targets. Hidden again at reveal_outro's end, on ARM and on Start. Creates
    /// itself at startup next to the detective (no scene setup); not logged yet (Phase 4).
    /// </summary>
    public class CrimeReplay : MonoBehaviour
    {
        private const float GlowSeconds = 3.5f, LabelSeconds = 3.5f;
        private static readonly Color CulpritColour = new Color(1f, 0.32f, 0.28f, 0.33f);
        private static readonly Color VictimColour = new Color(0.35f, 0.7f, 1f, 0.33f);

        private DetectiveBriefing _briefing;
        private SessionManager _session;
        private Transform _floor;
        private Ghost _culprit, _victim;
        private List<RevealBeat> _beats = new List<RevealBeat>();
        private RevealBeat _beat;
        private float _beatStart, _beatSeconds = 1f, _hideAt = -1f;
        private int _nextGlow;
        private readonly Dictionary<string, ClueHighlight> _highlights = new Dictionary<string, ClueHighlight>();
        private readonly List<(ClueHighlight h, float until)> _glowing = new List<(ClueHighlight, float)>();
        private Text _label;
        private float _labelUntil = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<CrimeReplay>() != null || FindFirstObjectByType<DetectiveBriefing>() == null) return;
            new GameObject("CrimeReplay").AddComponent<CrimeReplay>();
        }

        private void Awake()
        {
            _briefing = FindFirstObjectByType<DetectiveBriefing>();
            _session = FindFirstObjectByType<SessionManager>();
            var root = FindFirstObjectByType<CaseRoot>();
            if (root != null) _floor = root.transform;
            Shader shader = Resources.Load<Shader>("FYP_Ghost");
            if (_briefing == null || _briefing.RigTemplate == null || shader == null || _floor == null) { enabled = false; return; }
            _culprit = new Ghost("Ghost - culprit", _briefing, shader, CulpritColour, transform);
            _victim = new Ghost("Ghost - victim", _briefing, shader, VictimColour, transform);
            _label = MakeLabel();
        }

        private void OnEnable()
        {
            if (_briefing != null) _briefing.LineStarted += HandleLine;
            if (_session != null)
            {
                _session.ArmedChanged += HandleArmed;
                _session.SessionStarted += HideAll;
            }
        }

        private void OnDisable()
        {
            if (_briefing != null) _briefing.LineStarted -= HandleLine;
            if (_session != null)
            {
                _session.ArmedChanged -= HandleArmed;
                _session.SessionStarted -= HideAll;
            }
        }

        private void HandleArmed(bool armed) => HideAll();

        private void HandleLine(string file, float seconds)
        {
            if (file == "reveal_intro")
            {
                _beats = RevealScript.For(_session != null ? _session.Config.caseId : CaseId.X);
                MapHighlights();
                if (_beats.Count > 0) Pose(_beats[0], 0f, snap: true);   // the ghosts appear where the story starts
                _beat = null;
                _hideAt = -1f;
                return;
            }
            if (file == "reveal_outro")
            {
                _beat = null;
                _hideAt = Time.unscaledTime + seconds;
                return;
            }
            RevealBeat beat = _beats.Find(b => b.Line == file);
            if (beat == null) return;
            _beat = beat;
            _beatStart = Time.unscaledTime;
            _beatSeconds = Mathf.Max(0.5f, seconds);
            _nextGlow = 0;
            if (!string.IsNullOrEmpty(beat.TimeLabel)) ShowLabel(beat.TimeLabel);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (_beat != null)
            {
                float t = Mathf.Clamp01((now - _beatStart) / _beatSeconds);
                Pose(_beat, t, snap: false);
                while (_nextGlow < _beat.Glows.Count && _beat.Glows[_nextGlow].T <= t) Glow(_beat.Glows[_nextGlow++].ClueId, now);
            }
            if (_culprit.Visible || _victim.Visible)
            {
                _culprit.Animate(now, Time.unscaledDeltaTime);
                _victim.Animate(now, Time.unscaledDeltaTime);
            }
            for (int i = _glowing.Count - 1; i >= 0; i--)
            {
                if (now < _glowing[i].until) continue;
                if (_glowing[i].h != null) _glowing[i].h.Unhover();
                _glowing.RemoveAt(i);
            }
            if (_label != null)
            {
                bool show = now < _labelUntil && _culprit.Visible;
                if (_label.transform.parent.gameObject.activeSelf != show) _label.transform.parent.gameObject.SetActive(show);
                if (show) PlaceLabel();
            }
            if (_hideAt > 0f && now >= _hideAt) HideAll();
        }

        private void Pose(RevealBeat beat, float t, bool snap)
        {
            Apply(_culprit, beat.Culprit, t, snap);
            Apply(_victim, beat.Victim, t, snap);
        }

        private void Apply(Ghost ghost, List<GhostKey> keys, float t, bool snap)
        {
            if (!RevealScript.Evaluate(keys, t, out float x, out float z, out float yaw, out GhostPose pose)) return;
            ghost.SetTarget(_floor.TransformPoint(new Vector3(x, 0f, z)), _floor.rotation * Quaternion.Euler(0f, yaw, 0f), pose, snap);
        }

        private void MapHighlights()
        {
            _highlights.Clear();
            foreach (var clue in FindObjectsByType<Clue>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var h = clue.GetComponentInChildren<ClueHighlight>(true);
                if (h != null) _highlights[clue.ClueId] = h;
            }
        }

        private void Glow(string clueId, float now)
        {
            if (!_highlights.TryGetValue(clueId, out ClueHighlight h) || h == null) return;
            h.Hover();
            _glowing.Add((h, now + GlowSeconds));
        }

        private void HideAll()
        {
            _beat = null;
            _hideAt = -1f;
            _labelUntil = -1f;
            _culprit?.Hide();
            _victim?.Hide();
            foreach (var g in _glowing) if (g.h != null) g.h.Unhover();
            _glowing.Clear();
        }

        // ---- time label ("21:42") floating above the culprit, turned to the player ----

        private Text MakeLabel()
        {
            var canvasGo = new GameObject("Replay time", typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(400f, 120f);
            rect.localScale = Vector3.one * 0.001f;   // 0.4 x 0.12 m
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            var tr = (RectTransform)textGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 96;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.85f, 0.4f, 1f);
            canvasGo.SetActive(false);
            return text;
        }

        private void ShowLabel(string time)
        {
            if (_label == null) return;
            _label.text = time;
            _labelUntil = Time.unscaledTime + LabelSeconds;
        }

        private void PlaceLabel()
        {
            Transform canvas = _label.transform.parent;
            canvas.position = _culprit.Root.position + Vector3.up * 2.05f;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 away = canvas.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) canvas.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        // ---- one ghost figure ----

        private sealed class Ghost
        {
            public readonly Transform Root;
            private readonly Transform _rig, _head, _arm, _forearm, _armL, _forearmL;
            private readonly Quaternion _armRest, _forearmRest, _armLRest, _forearmLRest, _headRest;
            private Vector3 _targetPos;
            private Quaternion _targetRot = Quaternion.identity;
            private GhostPose _pose = GhostPose.Hidden;
            private float _lying;   // 0 standing .. 1 lying on the floor (eased)
            private Quaternion _armNow, _armLNow, _foreNow, _foreLNow;

            public bool Visible => Root.gameObject.activeSelf;

            public Ghost(string name, DetectiveBriefing template, Shader shader, Color colour, Transform parent)
            {
                Root = new GameObject(name).transform;
                Root.SetParent(parent, false);
                _rig = Object.Instantiate(template.RigTemplate, Root, false);
                _rig.name = "Rig";
                _rig.localPosition = Vector3.zero;
                _rig.localRotation = Quaternion.identity;
                var material = new Material(shader);
                material.SetColor("_Color", colour);
                foreach (var r in _rig.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = material;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                foreach (var b in _rig.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
                Transform moustache = Find(_rig, "Moustache");
                if (moustache != null) moustache.gameObject.SetActive(false);   // a ghost, not a second detective
                _head = Find(_rig, "Head");
                _arm = Find(_rig, "Arm");
                _forearm = _arm != null ? Find(_arm, "Forearm") : null;
                _armL = Find(_rig, "ArmL");
                _forearmL = _armL != null ? Find(_armL, "ForearmL") : null;
                if (_forearm != null && template.PointForearmMesh != null)
                {
                    var filter = _forearm.GetComponentInChildren<MeshFilter>(true);
                    if (filter != null) filter.sharedMesh = template.PointForearmMesh;   // a hand, not the magnifier
                }
                _armRest = _arm != null ? _arm.localRotation : Quaternion.identity;
                _forearmRest = _forearm != null ? _forearm.localRotation : Quaternion.identity;
                _armLRest = _armL != null ? _armL.localRotation : Quaternion.identity;
                _forearmLRest = _forearmL != null ? _forearmL.localRotation : Quaternion.identity;
                _headRest = _head != null ? _head.localRotation : Quaternion.identity;
                _armNow = _armRest; _armLNow = _armLRest; _foreNow = _forearmRest; _foreLNow = _forearmLRest;
                Root.gameObject.SetActive(false);
            }

            private static Transform Find(Transform t, string name)
            {
                if (t.name == name) return t;
                foreach (Transform c in t)
                {
                    Transform f = Find(c, name);
                    if (f != null) return f;
                }
                return null;
            }

            public void SetTarget(Vector3 position, Quaternion rotation, GhostPose pose, bool snap)
            {
                _pose = pose;
                if (pose == GhostPose.Hidden) { Hide(); return; }
                _targetPos = position;
                _targetRot = rotation;
                if (!Root.gameObject.activeSelf || snap)
                {
                    Root.gameObject.SetActive(true);
                    Root.SetPositionAndRotation(position, rotation);
                    _lying = pose == GhostPose.Lying ? 1f : 0f;
                }
            }

            public void Hide() => Root.gameObject.SetActive(false);

            public void Animate(float now, float dt)
            {
                if (!Visible) return;
                float k = 1f - Mathf.Exp(-8f * dt);
                Root.position = Vector3.Lerp(Root.position, _targetPos, k);
                Root.rotation = Quaternion.Slerp(Root.rotation, _targetRot, k);

                // Falling over takes about half a second; getting up never happens in a replay.
                _lying = Mathf.MoveTowards(_lying, _pose == GhostPose.Lying ? 1f : 0f, dt * 2.2f);
                float fall = _lying * _lying;
                bool walking = _pose == GhostPose.Walk || _pose == GhostPose.WalkCarry;
                float bob = walking ? Mathf.Abs(Mathf.Sin(now * 7f)) * 0.03f : 0f;
                _rig.localPosition = new Vector3(0f, bob + fall * 0.14f, 0f);
                _rig.localRotation = Quaternion.Euler(fall * 88f, 0f, 0f);   // forward onto the floor, head where he faced

                // Arm targets: Euler about x relative to the rest pose (negative = forward / up in front of the body).
                float r = 0f, l = 0f, fr = 0f, fl = 0f, rr = 0f;
                float swing = walking ? Mathf.Sin(now * 7f) * 25f : 0f;
                switch (_pose)
                {
                    case GhostPose.Walk: r = swing; l = -swing; break;
                    case GhostPose.WalkCarry: r = -35f; l = -35f; break;
                    case GhostPose.Work: r = -50f + Mathf.Sin(now * 6f) * 6f; l = -45f + Mathf.Sin(now * 5f + 1f) * 6f; break;
                    case GhostPose.Drink: r = -65f; fr = -45f; rr = -12f; break;
                    case GhostPose.Reach: r = -80f; fr = 40f; break;
                    case GhostPose.Carry: r = -35f; l = -35f; break;
                    case GhostPose.Smash: r = -150f + 120f * Mathf.Abs(Mathf.Sin(now * 8f)); fr = 30f; break;
                    case GhostPose.Stab: r = -70f + 35f * Mathf.Sin(now * 10f); fr = 45f; break;
                    case GhostPose.Struggle: r = -60f + Mathf.Sin(now * 9f) * 20f; l = -60f + Mathf.Sin(now * 8f + 2f) * 20f; break;
                    case GhostPose.Lying: r = -150f; l = -140f; fr = 60f; fl = 60f; break;
                }
                float a = 1f - Mathf.Exp(-10f * dt);
                _armNow = Quaternion.Slerp(_armNow, _armRest * Quaternion.Euler(r, 0f, rr), a);
                _armLNow = Quaternion.Slerp(_armLNow, _armLRest * Quaternion.Euler(l, 0f, 0f), a);
                _foreNow = Quaternion.Slerp(_foreNow, _forearmRest * Quaternion.Euler(fr, 0f, 0f), a);
                _foreLNow = Quaternion.Slerp(_foreLNow, _forearmLRest * Quaternion.Euler(fl, 0f, 0f), a);
                if (_arm != null) _arm.localRotation = _armNow;
                if (_armL != null) _armL.localRotation = _armLNow;
                if (_forearm != null) _forearm.localRotation = _foreNow;
                if (_forearmL != null) _forearmL.localRotation = _foreLNow;
                if (_head != null)
                    _head.localRotation = _headRest * Quaternion.Euler(_pose == GhostPose.Drink ? -15f : walking ? Mathf.Sin(now * 7f) * 2f : 0f, 0f, 0f);
            }
        }
    }
}
