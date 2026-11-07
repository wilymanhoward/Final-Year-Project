using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// The crime replay after the verdict (supervisor + user, 2026-11-05/06): two see-through ghost characters (culprit
    /// red, victim blue, in FYP/Ghost) re-enact the crime at the real spots while Inspector Hart narrates. The characters
    /// are Quaternius low-poly people animated with Mixamo motions (<see cref="ReplayCast"/>, Resources/ReplayCast), one
    /// motion per <see cref="GhostPose"/>, cross-faded through a small PlayableGraph per character (no Animator
    /// Controller); one-shot motions (sit to stand, falls, kneel) keep their last frame. Each beat of <see cref="RevealScript"/> starts when the detective starts its line
    /// (<see cref="DetectiveBriefing.LineStarted"/>) and lasts as long as the line, so voice and action stay together; the
    /// clue made at that moment glows (its hover shell) and the time ("21:42") floats above the culprit. Poses are
    /// eased between motions. Hidden again at reveal_outro's end, on ARM and on Start. Creates
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
        private ReplayCast _cast;
        private Shader _shader;
        private CaseId _castCase = (CaseId)(-1);
        private List<RevealBeat> _beats = new List<RevealBeat>();
        private RevealBeat _beat;
        private float _beatStart, _beatSeconds = 1f, _hideAt = -1f;
        private int _nextGlow;
        private readonly Dictionary<string, ClueHighlight> _highlights = new Dictionary<string, ClueHighlight>();
        private readonly Dictionary<string, Transform> _clues = new Dictionary<string, Transform>();
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
            _shader = Resources.Load<Shader>("FYP_Ghost");
            _cast = Resources.Load<ReplayCast>("ReplayCast");
            if (_briefing == null || _shader == null || _cast == null || _floor == null)
            {
                if (_cast == null) Debug.LogWarning("[CrimeReplay] No Resources/ReplayCast - run FYP > Set Up Crime Replay Cast");
                enabled = false;
                return;
            }
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
                CaseId id = _session != null ? _session.Config.caseId : CaseId.X;
                _beats = RevealScript.For(id);
                MakeCast(id);
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
                Reach(_beat, t);
                while (_nextGlow < _beat.Glows.Count && _beat.Glows[_nextGlow].T <= t) Glow(_beat.Glows[_nextGlow++].ClueId, now);
            }
            if (_culprit != null)
            {
                _culprit.Animate(Time.unscaledDeltaTime);
                _victim.Animate(Time.unscaledDeltaTime);
            }
            for (int i = _glowing.Count - 1; i >= 0; i--)
            {
                if (now < _glowing[i].until) continue;
                if (_glowing[i].h != null) _glowing[i].h.Unhover();
                _glowing.RemoveAt(i);
            }
            if (_label != null)
            {
                bool show = now < _labelUntil && _culprit != null && _culprit.Visible;
                if (_label.transform.parent.gameObject.activeSelf != show) _label.transform.parent.gameObject.SetActive(show);
                if (show) PlaceLabel();
            }
            if (_hideAt > 0f && now >= _hideAt) HideAll();
        }

        // The case's two characters (built once per case, kept until the case changes).
        private void MakeCast(CaseId id)
        {
            if (_castCase == id && _culprit != null) return;
            _culprit?.Destroy();
            _victim?.Destroy();
            _culprit = new Ghost("Replay - culprit", _cast.Culprit(id), _cast, id == CaseId.X, _shader, CulpritColour, transform);
            _victim = new Ghost("Replay - victim", _cast.Victim(id), _cast, false, _shader, VictimColour, transform);
            _castCase = id;
        }

        private void OnDestroy()
        {
            _culprit?.Destroy();
            _victim?.Destroy();
        }

        private void Pose(RevealBeat beat, float t, bool snap)
        {
            if (_culprit == null) return;
            Apply(_culprit, beat.Culprit, t, snap);
            Apply(_victim, beat.Victim, t, snap);
        }

        private void Apply(Ghost ghost, List<GhostKey> keys, float t, bool snap)
        {
            if (!RevealScript.Evaluate(keys, t, out float x, out float z, out float yaw, out GhostPose pose)) return;
            ghost.SetTarget(_floor.TransformPoint(new Vector3(x, 0f, z)), _floor.rotation * Quaternion.Euler(0f, yaw, 0f), pose, snap);
        }

        // Right hand onto the beat's reach clue: eases in over the first and out over the last 15 % of the window.
        private void Reach(RevealBeat beat, float t)
        {
            float w = 0f;
            Vector3 target = Vector3.zero;
            if (!string.IsNullOrEmpty(beat.ReachClueId) && _clues.TryGetValue(beat.ReachClueId, out Transform clue) && clue != null
                && t > beat.ReachFrom && t < beat.ReachTo)
            {
                float edge = (beat.ReachTo - beat.ReachFrom) * 0.15f;
                w = Mathf.Clamp01(Mathf.Min(t - beat.ReachFrom, beat.ReachTo - t) / Mathf.Max(edge, 1e-3f));
                target = clue.position;
            }
            _culprit.SetHandTarget(target, w);
        }

        private void MapHighlights()
        {
            _highlights.Clear();
            _clues.Clear();
            foreach (var clue in FindObjectsByType<Clue>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var h = clue.GetComponentInChildren<ClueHighlight>(true);
                if (h != null) _highlights[clue.ClueId] = h;
                _clues[clue.ClueId] = clue.transform;
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

        // ---- one replay character ----

        private sealed class Ghost
        {
            private const float FadeSeconds = 0.25f, Height = 1.72f, WomanHeight = 1.64f;
            // Mixamo's sitting motions sit on a ~0.33 m seat; the office chair's seat is ~0.48 m: lift him onto it.
            private const float SeatLift = 0.15f;
            private Transform _body;
            private float _lift;
            // Locomotion follows the real speed (user, 2026-11-07: walking on the spot, sliding feet).
            private GhostPose _intent = GhostPose.Hidden;   // the pose the timeline asks for
            private Vector3 _lastPos;
            private float _speed;                            // smoothed ground speed, m/s
            private Vector3 _carry;                          // where root motion left him vs. the timeline, fades out

            public readonly Transform Root;
            private readonly ReplayCast _cast;
            private readonly bool _woman;
            private readonly PlayableGraph _graph;
            private readonly AnimationMixerPlayable _mixer;
            private AnimationClipPlayable _from, _to;
            private AnimationClip _clip;
            private float _fade = 1f;
            private AnimationScriptPlayable _ik;
            private HandReachJob _reach;
            private Vector3 _targetPos;
            private Quaternion _targetRot = Quaternion.identity;
            private GhostPose _pose = GhostPose.Hidden;

            public bool Visible => Root != null && Root.gameObject.activeSelf;

            public Ghost(string name, GameObject model, ReplayCast cast, bool woman, Shader shader, Color colour, Transform parent)
            {
                _cast = cast;
                _woman = woman;
                Root = new GameObject(name).transform;
                Root.SetParent(parent, false);
                if (model == null) { Root.gameObject.SetActive(false); return; }
                GameObject body = Object.Instantiate(model, Root, false);
                _body = body.transform;
                var material = new Material(shader);
                material.SetColor("_Color", colour);
                float top = float.MinValue, bottom = float.MaxValue;
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = material;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (r is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;   // bounds follow the falls
                    top = Mathf.Max(top, r.bounds.max.y);
                    bottom = Mathf.Min(bottom, r.bounds.min.y);
                }
                // Whatever the file's units: scale to a person's height.
                if (top > bottom) body.transform.localScale *= (woman ? WomanHeight : Height) / (top - bottom);

                var animator = body.GetComponent<Animator>();
                if (animator == null) animator = body.AddComponent<Animator>();
                animator.applyRootMotion = true;   // only SitToStand has root motion (he steps forward as he rises)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                _graph = PlayableGraph.Create(name);
                _graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
                _mixer = AnimationMixerPlayable.Create(_graph, 2);
                _ik = AnimationScriptPlayable.Create(_graph, _reach, 1);
                _graph.Connect(_mixer, 0, _ik, 0);
                _ik.SetInputWeight(0, 1f);
                AnimationPlayableOutput.Create(_graph, "Animation", animator).SetSourcePlayable(_ik);
                _graph.Play();
                Root.gameObject.SetActive(false);
            }

            public void SetTarget(Vector3 position, Quaternion rotation, GhostPose pose, bool snap)
            {
                if (pose == GhostPose.Hidden) { Hide(); return; }
                _targetPos = position;
                _targetRot = rotation;
                if (!Root.gameObject.activeSelf || snap)
                {
                    Root.gameObject.SetActive(true);
                    Root.SetPositionAndRotation(position, rotation);
                    if (_body != null) _body.localPosition = Vector3.zero;   // nothing left over from a previous replay
                    _lastPos = position;
                    _speed = 0f;
                    _carry = Vector3.zero;
                }
                _intent = pose;
                if (pose == _pose) return;
                // Keep where a root-motion clip left the body: move the root there (the body doesn't move on screen).
                if (_pose == GhostPose.SitToStand && _body != null && _body.localPosition != Vector3.zero)
                {
                    Vector3 p = Root.position;
                    Root.position = new Vector3(_body.position.x, p.y, _body.position.z);
                    _body.localPosition = Vector3.zero;
                    // The timeline still has him at the chair: start the next move from where he stood up.
                    _carry = new Vector3(Root.position.x - _targetPos.x, 0f, Root.position.z - _targetPos.z);
                    _lastPos = Root.position;
                }
                if (IsLocomotion(pose)) return;   // Animate picks walk or stand from the real speed
                _pose = pose;
                if (pose == GhostPose.Lying && _clip != null) return;   // keep the last frame of the fall
                AnimationClip clip = pose == GhostPose.Walk && _woman && _cast.walkFemale != null ? _cast.walkFemale
                    : pose == GhostPose.Lying ? _cast.Clip(GhostPose.Collapse) : _cast.Clip(pose);
                Play(clip, pose == GhostPose.Lying || snap);
            }

            // Cross-fade to a motion; atEnd = jump to its last frame (a fall that already happened).
            private void Play(AnimationClip clip, bool atEnd)
            {
                if (clip == null || !_graph.IsValid() || clip == _clip) return;
                _graph.Disconnect(_mixer, 0);
                _graph.Disconnect(_mixer, 1);
                if (_from.IsValid()) _from.Destroy();
                _from = _to;
                _to = AnimationClipPlayable.Create(_graph, clip);
                if (atEnd && !clip.isLooping) _to.SetTime(clip.length);
                if (_from.IsValid()) _graph.Connect(_from, 0, _mixer, 0);
                _graph.Connect(_to, 0, _mixer, 1);
                _clip = clip;
                _fade = _from.IsValid() && !atEnd ? 0f : 1f;
            }

            // One step cycle per clip: 2 x foot travel / length, at 1.72 m (WalkCareful 0.44, WalkFemale 1.28 on the
            // woman, WalkHolding 1.28, Run 2.9; measured 2026-11-07).
            private const float WalkSpeed = 0.44f, WomanWalkSpeed = 1.28f, CarrySpeed = 1.28f, RunSpeed = 2.9f;
            private const float MoveThreshold = 0.15f;   // m/s: below this he stands instead of walking on the spot

            private static bool IsLocomotion(GhostPose p) => p == GhostPose.Walk || p == GhostPose.WalkCarry || p == GhostPose.Run;

            private AnimationClip ClipFor(GhostPose pose) =>
                pose == GhostPose.Walk && _woman && _cast.walkFemale != null ? _cast.walkFemale : _cast.Clip(pose);

            // Ground speed (m/s) each in-place motion was captured at, measured from its foot travel (2026-11-07).
            private float NaturalSpeed(GhostPose pose) =>
                pose == GhostPose.Run ? RunSpeed : pose == GhostPose.WalkCarry ? CarrySpeed : _woman ? WomanWalkSpeed : WalkSpeed;

            /// <summary>Guides the right hand to a world position with this weight (0 = the motion alone).</summary>
            public void SetHandTarget(Vector3 target, float weight)
            {
                if (!_ik.IsValid() || (weight <= 0f && _reach.Weight <= 0f)) return;
                _reach.Target = target;
                _reach.Weight = weight;
                _ik.SetJobData(_reach);
            }

            public void Hide()
            {
                if (Root != null) Root.gameObject.SetActive(false);
                _pose = GhostPose.Hidden;
            }

            public void Destroy()
            {
                if (_graph.IsValid()) _graph.Destroy();
                if (Root != null) Object.Destroy(Root.gameObject);
            }

            public void Animate(float dt)
            {
                if (!Visible || !_graph.IsValid()) return;
                float k = 1f - Mathf.Exp(-8f * dt);
                _carry = Vector3.MoveTowards(_carry, Vector3.zero, dt * 0.6f);
                Root.position = Vector3.Lerp(Root.position, _targetPos + _carry, k);
                // Seat lift: full while sitting, back to the floor while he stands up.
                float lift = _pose == GhostPose.Sit || _pose == GhostPose.SitTyping || _pose == GhostPose.Drink ? SeatLift
                    : _pose == GhostPose.SitToStand && _clip != null ? SeatLift * (1f - Mathf.Clamp01((float)_to.GetTime() / _clip.length))
                    : 0f;
                _lift = Mathf.MoveTowards(_lift, lift, dt * 0.6f);
                Root.position = new Vector3(Root.position.x, _targetPos.y + _lift, Root.position.z);
                Root.rotation = Quaternion.Slerp(Root.rotation, _targetRot, k);
                // Speed of the root over the ground (not the timeline's plan).
                Vector3 step = Root.position - _lastPos;
                step.y = 0f;
                _lastPos = Root.position;
                _speed = Mathf.Lerp(_speed, dt > 0f ? step.magnitude / dt : 0f, 1f - Mathf.Exp(-10f * dt));
                if (IsLocomotion(_intent) || IsLocomotion(_pose))
                {
                    GhostPose want = _speed > MoveThreshold ? (IsLocomotion(_intent) ? _intent : GhostPose.Walk)
                        : _intent == GhostPose.WalkCarry ? GhostPose.Carry : IsLocomotion(_intent) ? GhostPose.Stand : _intent;
                    if (want != _pose) { _pose = want; Play(ClipFor(want), false); }
                    // Steps match the speed: the motion's natural pace at its natural speed.
                    if (IsLocomotion(_pose) && _to.IsValid())
                        _to.SetSpeed(Mathf.Clamp(_speed / NaturalSpeed(_pose), 0.4f, 1.8f));
                }
                else if (_to.IsValid() && _to.GetSpeed() != 1.0) _to.SetSpeed(1.0);
                _fade = Mathf.Min(1f, _fade + dt / FadeSeconds);
                _mixer.SetInputWeight(0, _from.IsValid() ? 1f - _fade : 0f);
                _mixer.SetInputWeight(1, _fade);
                // One-shot motions stop on their last frame.
                if (_clip != null && !_clip.isLooping && _to.GetTime() >= _clip.length) _to.SetTime(_clip.length);
            }
        }

        // Humanoid hand IK inside the PlayableGraph (Animation C# job): moves the right hand to Target, the arm follows.
        private struct HandReachJob : IAnimationJob
        {
            public Vector3 Target;   // world space
            public float Weight;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                if (Weight <= 0f || !stream.isHumanStream) return;
                AnimationHumanStream human = stream.AsHuman();
                human.SetGoalPosition(AvatarIKGoal.RightHand, Target);
                human.SetGoalWeightPosition(AvatarIKGoal.RightHand, Weight);
                human.SolveIK();
            }
        }
    }
}
