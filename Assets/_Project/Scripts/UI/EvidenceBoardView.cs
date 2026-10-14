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
        [SerializeField] private Color normalColor = new Color(0.16f, 0.20f, 0.27f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.85f, 0.60f, 0.10f, 1f);
        [SerializeField] private Color submitColor = new Color(0.10f, 0.45f, 0.25f, 1f);
        [SerializeField] private Color disabledColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        private readonly Dictionary<string, string> _clueNames = new Dictionary<string, string>();
        private EvidenceBoardLayout _layout;
        private SessionManager _session;
        private bool _dirty = true;
        private bool _confirming;

        private void Awake()
        {
            if (board == null) board = GetComponent<EvidenceBoard>();
            foreach (var clue in FindObjectsByType<Clue>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                _clueNames[clue.ClueId] = clue.DisplayName;
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
            _layout = EvidenceBoardModel.Build(
                def != null ? def.Suspects : null,
                s.TaggedClues,
                id => _clueNames.TryGetValue(id, out string name) ? name : id,
                s.BoardSuspectId,
                s.IsBoardEvidenceSelected,
                suspectButtons.Length,
                evidenceButtons.Length,
                board.MinEvidence);

            Fill(suspectButtons, _layout.Suspects);
            Fill(evidenceButtons, _layout.Evidence);
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
                if (image != null) image.color = slot.Selected ? selectedColor : normalColor;
            }
        }
    }
}
