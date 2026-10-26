using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// The briefing detective (user decision, session 6): after the experimenter arms a run, the participant taps
    /// "Begin briefing" on their card; a low-poly detective (Inspector Hart, meshes from tools/maya/build_props.py)
    /// appears <see cref="standDistance"/> in front of them on the floor, explains the case line by line with audio and
    /// a speech bubble (<see cref="BriefingScript"/>, <see cref="BriefingPlayback"/>), and then the card offers Start
    /// (and Replay). Start waits for the briefing - in team runs for both partners' (SessionManager.CanParticipantStart).
    /// The timer starts only on Start, so listening never counts as completion time. Animation is procedural (no rig,
    /// user decisions session 6): the mouth opens with the loudness of the voice, the eyes blink, and every joint moves
    /// through damped springs driven by noise, speech beats and one gesture per line (see "Natural motion" below).
    /// Built by FYP > Set Up Briefing Detective. Lines without an audio clip are shown for an estimated time.
    /// </summary>
    public class DetectiveBriefing : MonoBehaviour
    {
        [SerializeField] private SessionManager session;
        [Tooltip("Usually the CenterEyeAnchor. Empty = Camera.main.")]
        [SerializeField] private Transform head;
        [Tooltip("CaseRoot: the floor height and the centre of the play area.")]
        [SerializeField] private Transform floorReference;
        [SerializeField] private GameObject character;
        [SerializeField] private Transform headPart;
        [SerializeField] private Transform armPart;
        [SerializeField] private Transform moustache;
        [Tooltip("Pivot at the feet holding all body parts: breathing and weight shift (bubble and voice stay still).")]
        [SerializeField] private Transform rig;
        [SerializeField] private Transform leftArm;
        [Tooltip("Forearm pivots at the elbows (session 6): gestures bend the elbows.")]
        [SerializeField] private Transform rightForearm;
        [SerializeField] private Transform leftForearm;
        [Tooltip("Mesh of the right forearm, and the variant with the index finger out used while he points (Case X briefing).")]
        [SerializeField] private MeshFilter rightForearmFilter;
        [SerializeField] private Mesh pointForearmMesh;
        [SerializeField] private Transform eyes;
        [SerializeField] private Transform mouth;
        [SerializeField] private AudioSource voice;
        [SerializeField] private Text bubbleText;
        [Tooltip("Game-style bubble (session 6): the part that pops in on each line, and the 'more' marker shown when a line is fully typed.")]
        [SerializeField] private RectTransform bubbleBody;
        [SerializeField] private RectTransform moreIndicator;
        [SerializeField] private TextAsset script;
        [Tooltip("Voice clips; matched to the script's 'file' column by name.")]
        [SerializeField] private AudioClip[] clips = new AudioClip[0];
        [SerializeField, Min(0.8f)] private float standDistance = 1.6f;
        [Tooltip("The detective stays this far inside the play area (CaseRoot-local).")]
        [SerializeField, Min(0.5f)] private float playAreaHalfSize = 2.2f;

        private List<BriefingLine> _all = new List<BriefingLine>();
        private List<BriefingLine> _lines = new List<BriefingLine>();
        private readonly Dictionary<string, AudioClip> _clipByName = new Dictionary<string, AudioClip>();
        private BriefingPlayback _playback = new BriefingPlayback(new float[0]);
        private readonly float[] _samples = new float[256];
        private float _talk;
        private Quaternion _headRest = Quaternion.identity, _armRest = Quaternion.identity;
        private Vector3 _moustacheRest;
        private Quaternion _leftArmRest = Quaternion.identity;
        private Vector3 _mouthScale = Vector3.one, _eyesScale = Vector3.one;
        private float _nextBlink;
        private readonly System.Random _blinkRandom = new System.Random(7);

        // Typewriter text (letters appear with the voice) and pop-in of the bubble on every line.
        private string _fullText = "";
        private int _shownChars = -1;
        private float _sayTime = -10f;
        private float _typeSeconds = 1f;
        private Vector2 _moreRest;
        private const string HiddenOpen = "<color=#00000000>";
        private const string HiddenClose = "</color>";

        private const float BlinkSeconds = 0.14f;

        // Team briefing (session 6): both players tap Begin, then both headsets start at the same moment with the
        // detective at one shared spot (CaseRoot-local, computed by the host); he faces the pair and looks at player A
        // on even lines and player B on odd lines.
        private bool _team;
        private BriefingSpot _spot;
        private int _gazeLine;

        // Stage cue "display" (user, session 6, Case X): while such a line is spoken one hand points at the display case
        // (he keeps facing and looking at the player) and the award's silhouette blinks in the empty case (TrophyGhost,
        // built under the display prop). He stands at one fixed spot, DisplaySideOffset to the right of the display case
        // (CaseRoot-local, so the same for everyone), facing the player, and points at it with his right hand (the one with the
        // magnifying glass; from there the display is on his right); the arm is aimed in PointRotation.
        private bool _pointAtDisplay;
        private const float DisplaySideOffset = 2.0f;   // user: 1 m was too close
        // Elbow while pointing (degrees from the modelled rest, where the forearm is bent 90 degrees forward holding the
        // magnifier up): 90 = fully straight, but the lens faces along the arm and shows edge-on (looked like a stick, user:
        // "weird"); 55 = nearly straight with the forearm tipped 35 degrees towards the player, so the lens shows.
        private const float PointElbowDegrees = 55f;   // tools/maya/build_props.py POINT_FINGER_DOWN = 90 - this
        private Mesh _normalForearmMesh;
        private Spring _pointBlend = new Spring(1.8f, 0.8f);
        private TrophyGhost _ghost;

        /// <summary>Where the briefing is (the participant card follows it).</summary>
        public BriefingStage Stage { get; private set; } = BriefingStage.NotStarted;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (floorReference == null)
            {
                var root = FindFirstObjectByType<CaseRoot>();
                if (root != null) floorReference = root.transform;
            }
            _all = BriefingScript.Parse(script != null ? script.text : null);
            foreach (AudioClip c in clips) if (c != null) _clipByName[c.name] = c;
            if (headPart != null) _headRest = headPart.localRotation;
            if (armPart != null) _armRest = armPart.localRotation;
            if (moustache != null) _moustacheRest = moustache.localPosition;
            if (leftArm != null) _leftArmRest = leftArm.localRotation;
            if (rightForearm != null) _rForeRest = rightForearm.localRotation;
            if (rightForearmFilter != null) _normalForearmMesh = rightForearmFilter.sharedMesh;
            if (leftForearm != null) _lForeRest = leftForearm.localRotation;
            if (mouth != null) _mouthScale = mouth.localScale;
            if (eyes != null) _eyesScale = eyes.localScale;
            _nextBlink = 2f;
            if (moreIndicator != null) _moreRest = moreIndicator.anchoredPosition;
            Hide();
        }

        private void OnEnable()
        {
            if (session == null) return;
            session.SetBriefingRequired(true);
            session.ArmedChanged += HandleArmed;
            session.SessionStarted += Hide;
            session.BriefingGo += HandleBriefingGo;
        }

        private void OnDisable()
        {
            if (session == null) return;
            session.SetBriefingRequired(false);
            session.ArmedChanged -= HandleArmed;
            session.SessionStarted -= Hide;
            session.BriefingGo -= HandleBriefingGo;
        }

        private void HandleArmed(bool armed)
        {
            Stage = BriefingStage.NotStarted;
            _team = false;
            _turnInit = false;
            Hide();
        }

        /// <summary>Participant card "Begin briefing": the detective appears in front of the participant and starts.</summary>
        public void BeginBriefing()
        {
            if (session == null || !session.IsArmed || session.IsRunning) return;
            if (session.IsTeamMode)
            {
                // Team: wait for the partner; the host starts both headsets together (HandleBriefingGo).
                if (Stage == BriefingStage.NotStarted)
                {
                    Stage = BriefingStage.WaitingForPartner;
                    session.RequestBriefingReady(OwnPose());
                }
                return;
            }
            _team = false;
            BuildLines();
            PlaceInFront();
            if (character != null) character.SetActive(true);
            _playback.Begin(Time.unscaledTimeAsDouble);
            Stage = BriefingStage.Playing;
            session.ReportBriefingStarted();
        }

        // Both players tapped Begin: same spot on both headsets, start now.
        private void HandleBriefingGo(BriefingSpot spot)
        {
            if (session == null || !session.IsArmed || session.IsRunning || floorReference == null) return;
            _team = true;
            BuildLines();
            PropSpec display = DisplayProp();
            if (display != null && HasDisplayCue())
            {
                float sx = Mathf.Clamp(display.X + DisplaySideOffset, -playAreaHalfSize, playAreaHalfSize), sz = display.Z;
                float mx = (spot.AX + spot.BX) * 0.5f, mz = (spot.AZ + spot.BZ) * 0.5f;
                spot = spot.Moved(sx, sz, Mathf.Atan2(mx - sx, mz - sz) * Mathf.Rad2Deg);   // fixed place, facing the pair
            }
            _spot = spot;
            character.transform.SetPositionAndRotation(floorReference.TransformPoint(new Vector3(spot.X, 0f, spot.Z)),
                floorReference.rotation * Quaternion.Euler(0f, spot.Yaw, 0f));
            character.SetActive(true);
            _turnInit = false;
            _playback.Begin(Time.unscaledTimeAsDouble);
            Stage = BriefingStage.Playing;
            session.ReportBriefingStarted();
        }

        // This player's floor position and facing in CaseRoot space (sent to the host for the shared spot).
        private PlayerPose2D OwnPose()
        {
            if (head == null || floorReference == null) return new PlayerPose2D(0f, 0f, 0f);
            Vector3 p = floorReference.InverseTransformPoint(head.position);
            Vector3 f = floorReference.InverseTransformDirection(head.forward);
            return new PlayerPose2D(p.x, p.z, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
        }

        // Who he looks at: solo = the player; team = player A on even lines, B on odd lines (same order on both
        // headsets). "Self" is the live head; the partner is where they stood when they tapped Begin, at head height.
        // The display case in the world (the point he looks and points at), from the case spec; CaseRoot-local, so the same on
        // both headsets.
        private PropSpec DisplayProp() => session != null ? CaseSpecs.For(session.Config.caseId).Prop("display") : null;

        private bool HasDisplayCue() => _lines.Exists(l => l.Cue == "display");

        private Vector3 DisplayPoint()
        {
            PropSpec display = DisplayProp();
            if (floorReference == null || display == null) return character.transform.position + character.transform.forward * 2f;
            return floorReference.TransformPoint(new Vector3(display.X, 1.2f, display.Z));
        }

        // Arm rotation (relative to its rest) that makes a hanging arm point from the shoulder at the display: first swing
        // forward / back about x (asin of the forward part), then raise sideways about z, so it can reach a point behind him.
        private Quaternion PointRotation(Transform shoulder)
        {
            Vector3 d = character.transform.InverseTransformDirection(DisplayPoint() - shoulder.position);
            if (d.sqrMagnitude < 1e-4f) return Quaternion.identity;
            d.Normalize();
            float swing = -Mathf.Asin(Mathf.Clamp(d.z, -1f, 1f)) * Mathf.Rad2Deg;   // - forward, + back
            float raise = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;                    // - left, + right
            return Quaternion.Euler(0f, 0f, raise) * Quaternion.Euler(swing, 0f, 0f);
        }

        private Vector3 GazeTarget()
        {
            if (head == null) return character.transform.position + character.transform.forward + Vector3.up * 1.6f;
            if (!_team || floorReference == null) return head.position;
            Vector3 self = floorReference.InverseTransformPoint(head.position);
            bool lookAtA = _gazeLine % 2 == 0;
            float tx = lookAtA ? _spot.AX : _spot.BX, tz = lookAtA ? _spot.AZ : _spot.BZ;
            float selfToA = (self.x - _spot.AX) * (self.x - _spot.AX) + (self.z - _spot.AZ) * (self.z - _spot.AZ);
            float selfToB = (self.x - _spot.BX) * (self.x - _spot.BX) + (self.z - _spot.BZ) * (self.z - _spot.BZ);
            bool selfIsA = selfToA <= selfToB;
            if (lookAtA == selfIsA) return head.position;
            return floorReference.TransformPoint(new Vector3(tx, self.y, tz));
        }

        /// <summary>Participant card "Replay": the whole briefing again (logged as a replay).</summary>
        public void Replay()
        {
            if (session == null || !session.IsArmed || session.IsRunning) return;
            if (Stage == BriefingStage.NotStarted || Stage == BriefingStage.WaitingForPartner) { BeginBriefing(); return; }
            session.ReportBriefingReplayed();
            if (voice != null) voice.Stop();
            if (character != null && !character.activeSelf) { PlaceInFront(); character.SetActive(true); }
            _playback.Begin(Time.unscaledTimeAsDouble);
            Stage = BriefingStage.Playing;
        }

        private void BuildLines()
        {
            ExperimentConfig c = session.Config;
            _lines = BriefingScript.For(_all, c.CaseCode, c.ConditionCode);
            var durations = new float[_lines.Count];
            for (int i = 0; i < _lines.Count; i++)
                durations[i] = _clipByName.TryGetValue(_lines[i].File, out AudioClip clip) && clip != null
                    ? clip.length
                    : BriefingScript.EstimateSeconds(_lines[i].Text);
            _playback = new BriefingPlayback(durations);
            _ghost = FindFirstObjectByType<TrophyGhost>();   // only exists in Case X, only the active case is shown
        }

        private void Update()
        {
            if (Stage != BriefingStage.Playing) return;
            if (_playback.Tick(Time.unscaledTimeAsDouble)) Say(_playback.CurrentLine);
            if (!_playback.IsFinished) return;
            Stage = BriefingStage.Finished;
            _pointAtDisplay = false;
            if (_ghost != null) _ghost.SetBlinking(false);
            session.ReportBriefingFinished();
        }

        private void Say(int index)
        {
            if (index < 0 || index >= _lines.Count) return;
            BriefingLine line = _lines[index];
            _gazeLine = index;
            _pointAtDisplay = line.Cue == "display";
            if (_ghost != null) _ghost.SetBlinking(_pointAtDisplay);
            _fullText = line.Text ?? "";
            _shownChars = -1;
            _sayTime = Time.unscaledTime;
            float spoken = _clipByName.TryGetValue(line.File, out AudioClip lineClip) && lineClip != null
                ? lineClip.length : BriefingScript.EstimateSeconds(line.Text);
            _typeSeconds = Mathf.Max(0.5f, spoken * 0.8f);   // finish typing a little before the voice ends
            PickGesture(index, spoken);
            if (_pointAtDisplay)
            {
                _gesture = Gesture.PointAtDisplay;
                _gestureUntil = Time.unscaledTime + spoken + 0.5f;
            }
            UpdateBubbleText();
            if (voice == null) return;
            voice.Stop();
            if (_clipByName.TryGetValue(line.File, out AudioClip clip) && clip != null)
            {
                voice.clip = clip;
                voice.Play();
            }
        }

        private void Hide()
        {
            if (voice != null) voice.Stop();
            if (character != null) character.SetActive(false);
            _fullText = "";
            _shownChars = -1;
            _pointAtDisplay = false;
            if (_ghost != null) _ghost.SetBlinking(false);
            if (bubbleText != null) bubbleText.text = "";
        }

        // Typewriter: the not-yet-typed letters are already laid out but transparent, so the text never re-wraps.
        private void UpdateBubbleText()
        {
            if (bubbleText == null) return;
            int n = Mathf.Clamp(Mathf.CeilToInt(_fullText.Length * Mathf.Clamp01((Time.unscaledTime - _sayTime) / _typeSeconds)),
                0, _fullText.Length);
            if (n == _shownChars) return;
            _shownChars = n;
            bubbleText.text = n >= _fullText.Length
                ? _fullText
                : _fullText.Substring(0, n) + HiddenOpen + _fullText.Substring(n) + HiddenClose;
        }

        private void AnimateBubble(float t)
        {
            UpdateBubbleText();
            if (bubbleBody != null)
            {
                float k = Mathf.Clamp01((t - _sayTime) / 0.2f);
                float c = 1.70158f;   // ease-out-back: a small overshoot, like a game dialogue box
                float e = 1f + (c + 1f) * Mathf.Pow(k - 1f, 3f) + c * Mathf.Pow(k - 1f, 2f);
                bubbleBody.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, e);
            }
            if (moreIndicator != null)
            {
                bool done = _fullText.Length > 0 && _shownChars >= _fullText.Length;
                if (moreIndicator.gameObject.activeSelf != done) moreIndicator.gameObject.SetActive(done);
                if (done) moreIndicator.anchoredPosition = _moreRest + new Vector2(0f, Mathf.Sin(t * 6f) * 5f);
            }
        }

        // On the floor, standDistance in front of the participant, inside the play area, facing them.
        private void PlaceInFront()
        {
            if (head == null || character == null) return;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 position = head.position + forward * standDistance;
            if (floorReference != null)
            {
                Vector3 local = floorReference.InverseTransformPoint(position);
                local.x = Mathf.Clamp(local.x, -playAreaHalfSize, playAreaHalfSize);
                local.z = Mathf.Clamp(local.z, -playAreaHalfSize, playAreaHalfSize);
                local.y = 0f;
                position = floorReference.TransformPoint(local);
            }
            Quaternion rotation = Quaternion.LookRotation(-forward, Vector3.up);
            PropSpec display = DisplayProp();
            if (floorReference != null && display != null && HasDisplayCue())
            {
                // One fixed spot next to the display, facing the player.
                position = floorReference.TransformPoint(new Vector3(
                    Mathf.Clamp(display.X + DisplaySideOffset, -playAreaHalfSize, playAreaHalfSize), 0f, display.Z));
                Vector3 toPlayer = head.position - position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 1e-4f) rotation = Quaternion.LookRotation(toPlayer, Vector3.up);
            }
            character.transform.SetPositionAndRotation(position, rotation);
            _turnInit = false;
        }

        // Procedural animation while visible: loudness drives the moustache, head nods, arm gestures.
        // ---- Natural motion (session 6, user: "too robotic") ----
        // Every joint chases a target through a damped Spring (acceleration, slight overshoot, settling). Targets come
        // from: layered Perlin noise (nothing repeats), speech beats (SpeechBeatDetector: stressed syllables kick a head
        // nod and a hand beat), one gesture per line held for part of it, gaze towards the participant's head (the head
        // leads, the body turns after it only when needed), breathing and weight shift on the Rig at the feet.

        private enum Gesture { Relaxed, OpenPalm, Explain, Magnifier, Point, PointAtDisplay }

        private struct ArmPose
        {
            public float Pitch, Roll, Elbow;
            public ArmPose(float pitch, float roll, float elbow) { Pitch = pitch; Roll = roll; Elbow = elbow; }
        }

        private Spring _headPitch = new Spring(2.2f, 0.65f), _headYaw = new Spring(1.8f, 0.7f), _headRoll = new Spring(1.6f, 0.7f);
        private Spring _nod = new Spring(3.2f, 0.45f), _beat = new Spring(3.6f, 0.4f);
        private Spring _lean = new Spring(1.0f, 0.85f), _turn = new Spring(0.55f, 0.9f);
        private Spring _rPitch = new Spring(1.5f, 0.72f), _rRoll = new Spring(1.5f, 0.72f), _rElbow = new Spring(1.7f, 0.7f);
        private Spring _lPitch = new Spring(1.4f, 0.72f), _lRoll = new Spring(1.4f, 0.72f), _lElbow = new Spring(1.6f, 0.7f);
        private readonly SpeechBeatDetector _beats = new SpeechBeatDetector();
        private System.Random _gestureRandom = new System.Random(11);
        private Gesture _gesture = Gesture.Relaxed;
        private float _gestureUntil = -1f;
        private float _lineTilt, _lineYaw;
        private float _glanceYaw, _nextGlance = 3f;
        private float _turnTarget;
        private bool _turnInit;
        private Quaternion _rForeRest = Quaternion.identity, _lForeRest = Quaternion.identity;

        private static float Noise(float x, float seed) => (Mathf.PerlinNoise(x, seed) - 0.5f) * 2f;

        // A gesture per line: picked from a small repertoire (seeded, so a replay looks the same), held for 55-85 %.
        private void PickGesture(int index, float lineSeconds)
        {
            if (index == 0) _gestureRandom = new System.Random(11);
            double r = _gestureRandom.NextDouble();
            _gesture = index == 0 ? Gesture.OpenPalm
                : r < 0.22 ? Gesture.Relaxed : r < 0.47 ? Gesture.Explain : r < 0.67 ? Gesture.OpenPalm : r < 0.84 ? Gesture.Magnifier : Gesture.Point;
            _gestureUntil = Time.unscaledTime + lineSeconds * (0.55f + 0.3f * (float)_gestureRandom.NextDouble());
            _lineTilt = ((float)_gestureRandom.NextDouble() - 0.5f) * 9f;
            _lineYaw = ((float)_gestureRandom.NextDouble() - 0.5f) * 10f;
        }

        // Shoulder pitch (- = forward/up), roll (outward: - left / + right), elbow (- = bend up). Degrees from rest.
        private static void Poses(Gesture g, out ArmPose right, out ArmPose left)
        {
            right = new ArmPose(0f, 0f, 0f);   // rest = magnifier held in front of the chest
            left = new ArmPose(-4f, -3f, -8f); // rest = hanging, slightly bent
            switch (g)
            {
                case Gesture.OpenPalm: left = new ArmPose(-42f, -14f, -48f); right = new ArmPose(-4f, 2f, -6f); break;
                case Gesture.Explain: left = new ArmPose(-30f, -8f, -72f); right = new ArmPose(-10f, 4f, -16f); break;
                case Gesture.Magnifier: right = new ArmPose(-22f, 6f, -38f); break;
                case Gesture.Point: left = new ArmPose(-62f, -4f, -22f); break;
                case Gesture.PointAtDisplay: break;   // both arms at rest; the pointing arm is aimed in LateUpdate
            }
        }

        private void LateUpdate()
        {
            if (character == null || !character.activeSelf) return;
            float t = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            AnimateBubble(t);

            // Voice loudness -> mouth, speech beats.
            float level = 0f;
            if (voice != null && voice.isPlaying)
            {
                voice.GetOutputData(_samples, 0);
                float sum = 0f;
                for (int i = 0; i < _samples.Length; i++) sum += _samples[i] * _samples[i];
                level = Mathf.Clamp01(Mathf.Sqrt(sum / _samples.Length) * 9f);
            }
            _talk = Mathf.Lerp(_talk, level, 1f - Mathf.Exp(-dt * 18f));
            bool speaking = Stage == BriefingStage.Playing;
            float beat = speaking ? _beats.Update(level, t, dt) : 0f;
            if (beat > 0f)
            {
                _nod.Kick(-55f * beat);    // head dips on the stress
                _beat.Kick(70f * beat);    // the gesturing hand marks it
            }

            if (mouth != null)
                mouth.localScale = new Vector3(_mouthScale.x * (1f + 0.15f * _talk), _mouthScale.y * (0.25f + 1.55f * _talk), _mouthScale.z);
            if (moustache != null)
                moustache.localPosition = _moustacheRest + new Vector3(0f, 0.004f * _talk, 0.001f * _talk);

            // Blink every 2.5-5 s, and when the gaze jumps.
            if (eyes != null)
            {
                if (t >= _nextBlink + BlinkSeconds) _nextBlink = t + 2.5f + (float)_blinkRandom.NextDouble() * 2.5f;
                float k = (t - _nextBlink) / BlinkSeconds;
                float closed = k >= 0f && k < 1f ? 1f - Mathf.Abs(k * 2f - 1f) : 0f;   // close then open
                eyes.localScale = new Vector3(_eyesScale.x, _eyesScale.y * Mathf.Lerp(1f, 0.1f, closed), _eyesScale.z);
            }

            // Body: breathing with a drifting rate, weight shift, leaning in a little while talking.
            if (rig != null)
            {
                float breath = Mathf.Sin(t * (1.25f + 0.15f * Noise(t * 0.05f, 1f)));
                float shift = Noise(t * 0.11f, 2f);
                float lean = _lean.Step(speaking ? 1.6f + 1.2f * _talk : 0f, dt);
                rig.localScale = new Vector3(1f + 0.004f * breath, 1f + 0.003f * breath, 1f + 0.006f * breath);
                rig.localRotation = Quaternion.Euler(lean + 0.5f * breath + 0.6f * Noise(t * 0.2f, 3f), 0f, 1.4f * shift);
                rig.localPosition = new Vector3(0.012f * shift, 0f, 0f);
            }

            // Gaze: look at the participant's head (limited), plus per-line tilt, idle glances and drift.
            float gazeYaw = 0f, gazePitch = 0f;
            if (head != null && headPart != null)
            {
                Vector3 d = character.transform.InverseTransformDirection(GazeTarget() - headPart.position);
                gazeYaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -35f, 35f);
                gazePitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -18f, 18f);
            }
            if (!speaking && t >= _nextGlance)
            {
                _glanceYaw = _glanceYaw == 0f ? ((float)_blinkRandom.NextDouble() - 0.5f) * 30f : 0f;
                _nextGlance = t + (_glanceYaw == 0f ? 2.5f + (float)_blinkRandom.NextDouble() * 3f : 0.8f);
                if (_glanceYaw != 0f) _nextBlink = t;   // people blink when they look away
            }
            if (speaking) _glanceYaw = 0f;
            float yawT = gazeYaw + (speaking ? _lineYaw * 0.5f : _glanceYaw) + 4f * Noise(t * 0.17f, 4f);
            float pitchT = gazePitch + 2.5f * Noise(t * 0.23f, 5f) + (speaking ? 2f * _talk : 0f);
            float rollT = (speaking ? _lineTilt : 0f) + 2f * Noise(t * 0.13f, 6f);
            float nod = _nod.Step(0f, dt);
            if (headPart != null)
                headPart.localRotation = _headRest * Quaternion.Euler(_headPitch.Step(pitchT, dt) - nod, _headYaw.Step(yawT, dt),
                    _headRoll.Step(rollT, dt));

            // Arms: the line's gesture (held, then back to rest), hand beats on the gesturing arm, slight idle drift.
            Poses(speaking && t < _gestureUntil ? _gesture : Gesture.Relaxed, out ArmPose right, out ArmPose left);
            float handBeat = _beat.Step(0f, dt);
            bool leftLeads = _gesture == Gesture.OpenPalm || _gesture == Gesture.Explain || _gesture == Gesture.Point;
            float drift = Noise(t * 0.3f, 7f);
            bool pointing = _pointAtDisplay && speaking && t < _gestureUntil;
            // Only point when the display really is on his right; otherwise the arm would swing across his body.
            if (pointing && armPart != null)
                pointing = character.transform.InverseTransformDirection(DisplayPoint() - armPart.position).x > 0.15f;
            float pointK = _pointBlend.Step(pointing ? 1f : 0f, dt);
            float rElbowT = right.Elbow - (leftLeads ? 0f : 0.25f * handBeat);
            float lElbowT = left.Elbow - (leftLeads ? 0.35f * handBeat : 0f);
            if (pointing) rElbowT = PointElbowDegrees;   // positive = straighter (negative bends the forearm up)
            Quaternion rArm = Quaternion.Euler(_rPitch.Step(right.Pitch + 2f * drift, dt), 0f, _rRoll.Step(right.Roll, dt));
            Quaternion lArm = Quaternion.Euler(_lPitch.Step(left.Pitch - 2f * drift, dt), 0f, _lRoll.Step(left.Roll, dt));
            if (rightForearmFilter != null && pointForearmMesh != null)
            {
                Mesh want = pointK > 0.5f ? pointForearmMesh : _normalForearmMesh;   // index finger out only while pointing
                if (rightForearmFilter.sharedMesh != want) rightForearmFilter.sharedMesh = want;
            }
            if (pointK > 0.001f && armPart != null) rArm = Quaternion.Slerp(rArm, PointRotation(armPart), pointK);
            if (armPart != null) armPart.localRotation = _armRest * rArm;
            if (rightForearm != null) rightForearm.localRotation = _rForeRest * Quaternion.Euler(_rElbow.Step(rElbowT, dt), 0f, 0f);
            if (leftArm != null) leftArm.localRotation = _leftArmRest * lArm;
            if (leftForearm != null) leftForearm.localRotation = _lForeRest * Quaternion.Euler(_lElbow.Step(lElbowT, dt), 0f, 0f);

            // Solo: turn towards the participant only when they are clearly off to the side; the head covers the rest.
            // Team: he keeps facing the pair (the spot's yaw), so both players see the same detective.
            if (head != null && !_team)
            {
                Vector3 toHead = head.position - character.transform.position;
                toHead.y = 0f;
                if (toHead.sqrMagnitude > 0.01f)
                {
                    float desired = Mathf.Atan2(toHead.x, toHead.z) * Mathf.Rad2Deg;
                    float current = character.transform.eulerAngles.y;
                    if (!_turnInit) { _turn.Value = current; _turnTarget = current; _turnInit = true; }
                    if (Mathf.Abs(Mathf.DeltaAngle(_turnTarget, desired)) > 22f) _turnTarget = _turn.Value + Mathf.DeltaAngle(_turn.Value, desired);
                    character.transform.rotation = Quaternion.Euler(0f, _turn.Step(_turnTarget, dt), 0f);
                }
            }
        }
    }
}
