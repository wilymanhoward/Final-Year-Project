using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Hover highlight of a clue: shows an outline shell (the clue mesh, slightly enlarged, drawn with front faces culled)
    /// while a hand or controller hovers it. Identical for every clue in both cases, so it can't make one case's clues
    /// easier to spot. Wired by the case-scene generator to the Interaction SDK's InteractableUnityEventWrapper
    /// WhenHover / WhenUnhover. The shell is shown by activating its GameObject (not by enabling the renderer), so
    /// <see cref="CaseContent"/> hiding the inactive case can't switch it on.
    /// </summary>
    public class ClueHighlight : MonoBehaviour
    {
        [SerializeField] private GameObject shell;

        private int _hovers;

        private void Awake()
        {
            if (shell != null) shell.SetActive(false);
        }

        private void OnDisable()
        {
            _hovers = 0;
            if (shell != null) shell.SetActive(false);
        }

        /// <summary>Hook to WhenHover (one call per hovering interactor).</summary>
        public void Hover()
        {
            _hovers++;
            if (shell != null) shell.SetActive(true);
        }

        /// <summary>Hook to WhenUnhover.</summary>
        public void Unhover()
        {
            if (_hovers > 0) _hovers--;
            if (_hovers == 0 && shell != null) shell.SetActive(false);
        }
    }
}
