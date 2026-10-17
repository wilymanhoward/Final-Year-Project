using UnityEngine;

namespace FYP.Detective
{
    /// <summary>
    /// Root of one case's greybox content under CaseRoot (built by FYP > Build Case Scenes). Only the active case
    /// (<see cref="ExperimentConfig.caseId"/>) is shown; it follows config changes, including the host's case
    /// adopted by a team client. Children without network state (furniture, zones, static clues, tag buttons) are
    /// simply deactivated. Grabbable clues stay active, because Netcode spawns in-scene network objects only when
    /// they start active (and re-enables them during client sync); they are hidden instead: renderers, colliders and
    /// grab interactables off, and their resetter and gravity stopped.
    /// </summary>
    public class CaseContent : MonoBehaviour
    {
        [SerializeField] private CaseId caseId = CaseId.X;
        [SerializeField] private SessionManager session;

        private bool? _shown;

        public CaseId CaseId => caseId;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void OnEnable()
        {
            if (session == null) return;
            session.ConfigChanged += Refresh;
            session.SessionStarted += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (session == null) return;
            session.ConfigChanged -= Refresh;
            session.SessionStarted -= Refresh;
        }

        private void Refresh()
        {
            if (session != null) SetShown(session.Config.caseId == caseId);
        }

        /// <summary>Shows or hides this case's content (see the class summary for how network objects are hidden).</summary>
        public void SetShown(bool shown)
        {
            if (_shown == shown) return;
            _shown = shown;
            foreach (Transform child in transform)
            {
                var clue = child.GetComponent<Clue>();
                if (clue != null && clue.IsNetworkedGrabbable) SoftHide(child.gameObject, shown);
                else child.gameObject.SetActive(shown);
            }
        }

        private static void SoftHide(GameObject go, bool shown)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = shown;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = shown;
            var clue = go.GetComponent<Clue>();
            clue.SetInteractable(shown);
            var resetter = go.GetComponent<ClueResetter>();
            if (resetter != null) resetter.enabled = shown;
            var gravity = go.GetComponent<ClueGravity>();   // colliders are off, so it must not fall
            if (gravity != null) gravity.enabled = shown;
        }
    }
}
