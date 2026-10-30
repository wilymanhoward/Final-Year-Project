using UnityEngine;
using UnityEngine.UI;

namespace FYP.Detective
{
    /// <summary>
    /// Evidence tag that appears above a clue once it has been found (only while a session runs): the clue's name and
    /// description on a slip, and a "TAG AS EVIDENCE" stamp button (built by ClueWiring.EnsureTagButton). It floats just
    /// above the clue's bounds and turns to face the player (yaw only). While tagged the button turns ink blue and a
    /// TAGGED stamp shows on the slip. The button is a world-space uGUI Button made pokeable by the Interaction SDK; its
    /// onClick calls Clue.ToggleTagged. Tag/untag events are logged through CaseState.
    /// </summary>
    public class ClueTagButton : MonoBehaviour
    {
        [SerializeField] private Clue clue;
        [Tooltip("Child holding the canvas + poke interactable. Shown/hidden as a whole, so it can't be poked while hidden.")]
        [SerializeField] private GameObject content;
        [SerializeField] private Text label;
        [Tooltip("Optional: the clue's inspect text (with its name in front when there is no name label).")]
        [SerializeField] private Text inspectLabel;
        [Tooltip("Optional: the clue's name.")]
        [SerializeField] private Text nameLabel;
        [Tooltip("Optional: the button background, coloured by tag state.")]
        [SerializeField] private Image buttonImage;
        [Tooltip("Optional: the TAGGED stamp, shown while tagged.")]
        [SerializeField] private GameObject taggedStamp;
        [Tooltip("Usually CenterEyeAnchor. Empty = Camera.main.")]
        [SerializeField] private Transform head;
        [Tooltip("Gap in metres between the top of the clue and the bottom of the tag.")]
        [SerializeField, Min(0f)] private float heightAboveClue = 0.015f;
        [SerializeField] private string tagText = "TAG AS EVIDENCE";
        [SerializeField] private string taggedText = "EVIDENCE TAGGED\n<size=11>tap to remove</size>";
        [SerializeField] private Color tagColor = new Color(0.76f, 0.10f, 0.08f, 1f);      // stamp red
        [SerializeField] private Color taggedColor = new Color(0.14f, 0.24f, 0.46f, 1f);   // ink blue

        private Renderer[] _clueRenderers;
        private bool _lastTagged;
        private float _halfHeight;   // half the tag's height in metres, so its bottom sits just above the clue

        public Clue Clue => clue;

        private void Awake()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (clue != null) _clueRenderers = clue.GetComponentsInChildren<Renderer>();
            if (content != null)
            {
                var rect = content.transform as RectTransform;
                if (rect != null) _halfHeight = rect.rect.height * rect.localScale.y * 0.5f;
                content.SetActive(false);
            }
            SetLabel(false);
        }

        private void LateUpdate()
        {
            bool show = clue != null && clue.IsSessionRunning && clue.IsFound;
            if (content != null && content.activeSelf != show) content.SetActive(show);
            if (!show) return;

            if (inspectLabel != null && inspectLabel.text.Length == 0)
                inspectLabel.text = nameLabel != null ? clue.InspectText : clue.DisplayName + "\n" + clue.InspectText;
            if (nameLabel != null && nameLabel.text.Length == 0) nameLabel.text = clue.DisplayName;
            bool tagged = clue.IsTagged;
            if (tagged != _lastTagged) SetLabel(tagged);
            FollowClue();
        }

        private void SetLabel(bool tagged)
        {
            _lastTagged = tagged;
            if (label != null) label.text = tagged ? taggedText : tagText;
            if (buttonImage != null) buttonImage.color = tagged ? taggedColor : tagColor;
            if (taggedStamp != null && taggedStamp.activeSelf != tagged) taggedStamp.SetActive(tagged);
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
            Vector3 position = top + Vector3.up * (heightAboveClue + _halfHeight);

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
