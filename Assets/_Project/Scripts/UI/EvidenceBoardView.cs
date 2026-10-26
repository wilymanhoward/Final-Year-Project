using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// The fixed, pokeable evidence board (Phase C2), built by FYP > Set Up Evidence Board under CaseRoot so it
    /// lines up for both partners. Prebuilt slot buttons (no UI is created at runtime) are filled from
    /// <see cref="EvidenceBoardModel"/>: suspects of the active case, tagged clues, the shared selection.
    /// Submit asks "Submit? Yes / No" first. Shown only while a run is in progress.
    /// Look (session 6, user: "like an actual evidence board"): cards keep their own paper colour and turn
    /// <see cref="selectedColor"/> when chosen, with their "Stamp" child shown; red strings (prebuilt, one per evidence
    /// slot) run from the chosen suspect's pin to every chosen evidence card's pin.
    /// </summary>
    [RequireComponent(typeof(EvidenceBoard))]
    public class EvidenceBoardView : MonoBehaviour
    {
        [SerializeField] private EvidenceBoard board;
        [SerializeField] private GameObject content;
        [SerializeField] private Text summary;
        [SerializeField] private GameObject[] suspectButtons = new GameObject[0];
        [SerializeField] private GameObject[] evidenceButtons = new GameObject[0];
        [SerializeField] private GameObject submitButton;
        [SerializeField] private GameObject confirmGroup;
        [Tooltip("Pin of each suspect / evidence card (same order as the buttons); the red strings run between them.")]
        [SerializeField] private RectTransform[] suspectPins = new RectTransform[0];
        [SerializeField] private RectTransform[] evidencePins = new RectTransform[0];
        [Tooltip("One red string per evidence slot (Image, pivot at its left end, child of the content canvas).")]
        [SerializeField] private RectTransform[] strings = new RectTransform[0];
        [SerializeField, Min(0.5f)] private float stringThickness = 3f;
        [SerializeField] private Color normalColor = new Color(0.16f, 0.20f, 0.27f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.85f, 0.60f, 0.10f, 1f);
        [SerializeField] private Color submitColor = new Color(0.10f, 0.45f, 0.25f, 1f);
        [SerializeField] private Color disabledColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        private readonly Dictionary<string, string> _clueNames = new Dictionary<string, string>();
        private readonly Dictionary<GameObject, Color> _baseColors = new Dictionary<GameObject, Color>();
        private EvidenceBoardLayout _layout;
        private SessionManager _session;
        private bool _dirty = true;
        private bool _confirming;

        private void Awake()
        {
            if (board == null) board = GetComponent<EvidenceBoard>();
            foreach (var clue in FindObjectsByType<Clue>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                _clueNames[clue.ClueId] = clue.DisplayName;
            RememberColors(suspectButtons);
            RememberColors(evidenceButtons);
        }

        // Each card's own paper colour (polaroid white, index-card cream) is its unselected colour.
        private void RememberColors(GameObject[] buttons)
        {
            foreach (GameObject b in buttons)
            {
                var image = b != null ? b.GetComponent<Image>() : null;
                if (image != null) _baseColors[b] = image.color;
            }
        }

        private void OnEnable()
        {
            _session = board != null ? board.Session : null;
            if (_session == null) _session = FindFirstObjectByType<SessionManager>();
            if (_session == null) return;
            _session.State.Changed += MarkDirty;
            _session.ConfigChanged += MarkDirty;
            _dirty = true;
        }

        private void OnDisable()
        {
            if (_session == null) return;
            _session.State.Changed -= MarkDirty;
            _session.ConfigChanged -= MarkDirty;
        }

        private void MarkDirty() => _dirty = true;

        // ---- Buttons (wired with the slot index by the menu tool) ----

        public void PressSuspect(int slot)
        {
            if (_layout != null && slot >= 0 && slot < _layout.Suspects.Length && _layout.Suspects[slot].Visible)
                board.SelectSuspect(_layout.Suspects[slot].Id);
        }

        public void PressEvidence(int slot)
        {
            if (_layout != null && slot >= 0 && slot < _layout.Evidence.Length && _layout.Evidence[slot].Visible)
                board.ToggleEvidence(_layout.Evidence[slot].Id);
        }

        public void PressSubmit()
        {
            if (!board.CanSubmit) return;
            _confirming = true;
            _dirty = true;
        }

        public void ConfirmYes()
        {
            _confirming = false;
            _dirty = true;
            board.Submit();
        }

        public void ConfirmNo()
        {
            _confirming = false;
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (_session == null) return;
            bool show = _session.IsRunning;
            if (content != null && content.activeSelf != show) content.SetActive(show);
            if (!show) { _confirming = false; return; }
            if (!_dirty) return;
            _dirty = false;
            Refresh();
        }

        private void Refresh()
        {
            CaseState s = _session.State;
            CaseDefinition def = _session.ActiveCase;
            ExperimentConfig c = _session.Config;
            _layout = EvidenceBoardModel.Build(
                def != null ? SuspectOrder.Shuffled(def.Suspects, c.pairCode, c.CaseCode, c.ConditionCode) : null,
                s.TaggedClues,
                id => _clueNames.TryGetValue(id, out string name) ? name : id,
                s.BoardSuspectId,
                s.IsBoardEvidenceSelected,
                suspectButtons.Length,
                evidenceButtons.Length,
                board.MinEvidence);

            Fill(suspectButtons, _layout.Suspects);
            Fill(evidenceButtons, _layout.Evidence);
            UpdateStrings();
            if (summary != null) summary.text = _layout.Summary;

            bool canSubmit = board.CanSubmit;
            if (!canSubmit) _confirming = false;
            if (submitButton != null)
            {
                submitButton.SetActive(!_confirming);
                var image = submitButton.GetComponent<Image>();
                if (image != null) image.color = canSubmit ? submitColor : disabledColor;
            }
            if (confirmGroup != null) confirmGroup.SetActive(_confirming);
        }

        private void Fill(GameObject[] buttons, BoardSlot[] slots)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                GameObject b = buttons[i];
                if (b == null) continue;
                BoardSlot slot = i < slots.Length ? slots[i] : BoardSlot.Empty;
                if (b.activeSelf != slot.Visible) b.SetActive(slot.Visible);
                if (!slot.Visible) continue;
                var label = b.GetComponentInChildren<Text>(true);
                if (label != null) label.text = slot.Label;
                var image = b.GetComponent<Image>();
                if (image != null)
                    image.color = slot.Selected ? selectedColor : _baseColors.TryGetValue(b, out Color c) ? c : normalColor;
                var stamp = b.transform.Find("Stamp");
                if (stamp != null && stamp.gameObject.activeSelf != slot.Selected) stamp.gameObject.SetActive(slot.Selected);
            }
        }

        // Red string from the chosen suspect's pin to each chosen evidence card's pin (canvas units = mm).
        private void UpdateStrings()
        {
            int suspect = -1;
            for (int i = 0; i < _layout.Suspects.Length; i++)
                if (_layout.Suspects[i].Visible && _layout.Suspects[i].Selected) suspect = i;
            for (int i = 0; i < strings.Length; i++)
            {
                RectTransform line = strings[i];
                if (line == null) continue;
                bool show = suspect >= 0 && suspect < suspectPins.Length && suspectPins[suspect] != null
                    && i < _layout.Evidence.Length && _layout.Evidence[i].Visible && _layout.Evidence[i].Selected
                    && i < evidencePins.Length && evidencePins[i] != null;
                if (line.gameObject.activeSelf != show) line.gameObject.SetActive(show);
                if (!show) continue;
                Vector2 a = PinPoint(line, suspectPins[suspect]);
                Vector2 b = PinPoint(line, evidencePins[i]);
                Vector2 d = b - a;
                line.anchoredPosition = a;
                line.sizeDelta = new Vector2(d.magnitude, stringThickness);
                line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
        }

        // Centre of a pin in the coordinates of the string's parent (strings are anchored at the parent's centre).
        private static Vector2 PinPoint(RectTransform line, RectTransform pin)
        {
            Vector3 world = pin.TransformPoint(pin.rect.center);
            return line.parent.InverseTransformPoint(world);
        }
    }
}
