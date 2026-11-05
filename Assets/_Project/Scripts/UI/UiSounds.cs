using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// Interaction sounds (user, 2026-11-04): every button clicks; the game actions get the sound of the real object,
    /// played in 3D where it happens, so a partner hears it from the right place too (sounds come from
    /// <see cref="CaseState"/> events, which fire on both headsets of a team):
    /// tag = rubber stamp at the clue, untag = tag peeled off, board suspect/evidence chosen = pushpin into cork at the
    /// board, removed = pin pulled out, answer submitted = heavy stamp + case file closing. Those buttons don't also click.
    /// Clips come from <see cref="SoundSynth"/> (no assets). Creates itself at startup (no scene setup); not logged.
    /// Same in both conditions.
    /// </summary>
    public class UiSounds : MonoBehaviour
    {
        private const int Voices = 6;
        private const float Volume = 0.7f;

        // Buttons whose action has its own sound from a CaseState event (method names of their persistent listeners).
        private static readonly HashSet<string> OwnSound = new HashSet<string> { "PressSuspect", "PressEvidence", "ToggleTagged", "ConfirmYes" };

        private readonly Dictionary<UiSound, AudioClip> _clips = new Dictionary<UiSound, AudioClip>();
        private readonly Dictionary<string, Transform> _clues = new Dictionary<string, Transform>();
        private readonly AudioSource[] _voices = new AudioSource[Voices];
        private int _next;
        private SessionManager _session;
        private Transform _board;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<UiSounds>() != null || FindFirstObjectByType<SessionManager>() == null) return;
            new GameObject("UiSounds").AddComponent<UiSounds>();
        }

        private void Awake()
        {
            _session = FindFirstObjectByType<SessionManager>();
            foreach (UiSound s in System.Enum.GetValues(typeof(UiSound)))
            {
                float[] data = SoundSynth.Make(s);
                var clip = AudioClip.Create("Ui" + s, data.Length, 1, SoundSynth.SampleRate, false);
                clip.SetData(data, 0);
                _clips[s] = clip;
            }
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false;
                a.spatialBlend = 1f;            // 3D: from the button / clue / board
                a.rolloffMode = AudioRolloffMode.Linear;
                a.minDistance = 1f;
                a.maxDistance = 12f;            // still heard across the 5 x 5 m room
                a.dopplerLevel = 0f;
                a.volume = Volume;
                _voices[i] = a;
            }

            foreach (var clue in FindObjectsByType<Clue>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                _clues[clue.ClueId] = clue.transform;
            var board = FindFirstObjectByType<EvidenceBoardView>(FindObjectsInactive.Include);
            if (board != null) _board = board.transform;

            foreach (var button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (HasOwnSound(button)) continue;
                Transform at = button.transform;
                button.onClick.AddListener(() => Play(UiSound.Click, at.position));
            }
        }

        private static bool HasOwnSound(Button button)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (OwnSound.Contains(button.onClick.GetPersistentMethodName(i))) return true;
            return false;
        }

        private void OnEnable()
        {
            if (_session == null) return;
            CaseState s = _session.State;
            s.ClueTagged += HandleTagged;
            s.ClueUntagged += HandleUntagged;
            s.BoardSuspectSelected += HandleSuspect;
            s.BoardEvidenceChanged += HandleEvidence;
            s.AnswerSubmitted += HandleSubmitted;
        }

        private void OnDisable()
        {
            if (_session == null) return;
            CaseState s = _session.State;
            s.ClueTagged -= HandleTagged;
            s.ClueUntagged -= HandleUntagged;
            s.BoardSuspectSelected -= HandleSuspect;
            s.BoardEvidenceChanged -= HandleEvidence;
            s.AnswerSubmitted -= HandleSubmitted;
        }

        private void HandleTagged(string clueId) => PlayAtClue(UiSound.Stamp, clueId);
        private void HandleUntagged(string clueId) => PlayAtClue(UiSound.Peel, clueId);
        private void HandleSuspect(string suspectId)
        {
            if (_session.IsRunning) PlayAtBoard(suspectId != null ? UiSound.PinIn : UiSound.PinOut);
        }

        private void HandleEvidence(string clueId, bool added)
        {
            if (_session.IsRunning) PlayAtBoard(added ? UiSound.PinIn : UiSound.PinOut);
        }

        private void HandleSubmitted(AnswerResult result) => PlayAtBoard(UiSound.CaseClosed);   // the run may already be ending

        private void PlayAtClue(UiSound sound, string clueId)
        {
            if (_session.IsRunning) Play(sound, _clues.TryGetValue(clueId, out Transform t) && t != null ? t.position : Listener());
        }

        private void PlayAtBoard(UiSound sound) => Play(sound, _board != null ? _board.position : Listener());

        private Vector3 Listener()
        {
            Camera cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        /// <summary>Plays <paramref name="sound"/> at a world position on the next free voice.</summary>
        public void Play(UiSound sound, Vector3 position)
        {
            AudioSource voice = _voices[_next];
            _next = (_next + 1) % Voices;
            voice.transform.position = position;
            voice.clip = _clips[sound];
            voice.Play();
        }
    }
}
