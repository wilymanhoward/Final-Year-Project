using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// Small "Tag as Evidence" button that appears above a clue once it has been found (only while a session
    /// runs). It floats above the clue's bounds and turns to face the player (yaw only). The button itself is
    /// a world-space uGUI Button made pokeable by the Interaction SDK; its onClick calls Clue.ToggleTagged
    /// (wired by the FYP clue menu tools). Tag/untag events are logged through CaseState.
    /// </summary>
    public class ClueTagButton : MonoBehaviour
    {
        [SerializeField] private Clue clue;
        [Tooltip("Child holding the canvas + poke interactable. Shown/hidden as a whole, so it can't be poked while hidden.")]
        [SerializeField] private GameObject content;
        [SerializeField] private Text label;
        [Tooltip("Optional: shows the clue's name and inspect text above the button once found.")]
        [SerializeField] private Text inspectLabel;
        [Tooltip("Usually CenterEyeAnchor. Empty = Camera.main.")]
        [SerializeField] private Transform head;
        [SerializeField, Min(0f)] private float heightAboveClue = 0.08f;
        [SerializeField] private string tagText = "Tag as Evidence";
        [SerializeField] private string taggedText = "Tagged - tap to untag";

        private Renderer[] _clueRenderers;
        private bool _lastTagged;

        public Clue Clue => clue;

        private void Awake()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (clue != null) _clueRenderers = clue.GetComponentsInChildren<Renderer>();
            if (content != null) content.SetActive(false);
            SetLabel(false);
        }

        private void LateUpdate()
        {
            bool show = clue != null && clue.IsSessionRunning && clue.IsFound;
            if (content != null && content.activeSelf != show) content.SetActive(show);
            if (!show) return;

            if (inspectLabel != null && inspectLabel.text.Length == 0)
                inspectLabel.text = clue.DisplayName + "\n" + clue.InspectText;
            bool tagged = clue.IsTagged;
            if (tagged != _lastTagged) SetLabel(tagged);
            FollowClue();
        }

        private void SetLabel(bool tagged)
        {
            _lastTagged = tagged;
            if (label != null) label.text = tagged ? taggedText : tagText;
        }

        private void FollowClue()
        {
            Vector3 top = clue.transform.position;
            if (_clueRenderers != null && _clueRenderers.Length > 0)
            {
                Bounds b = _clueRenderers[0].bounds;
                for (int i = 1; i < _clueRenderers.Length; i++) b.Encapsulate(_clueRenderers[i].bounds);
                top = new Vector3(b.center.x, b.max.y, b.center.z);
            }
            Vector3 position = top + Vector3.up * heightAboveClue;

            Quaternion rotation = transform.rotation;
            if (head != null)
            {
                Vector3 away = position - head.position;   // canvas faces the viewer when its forward points away
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f) rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
