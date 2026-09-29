using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective
{
    /// <summary>
    /// A piece of evidence in the scene. Holds its data and forwards "found"/"tagged"
    /// to <see cref="CaseState"/>. It does NOT implement grabbing - the Meta
    /// Interaction SDK does. Wire it in the Inspector:
    ///   Oculus.Interaction.InteractableUnityEventWrapper.WhenSelect -> Clue.MarkFound()
    /// (one wrapper per interactable, e.g. HandGrabInteractable and GrabInteractable;
    /// MarkFound is idempotent so double wiring is harmless).
    /// Keeping Meta types out of this script means the core assembly compiles and
    /// unit tests run without the SDK.
    /// </summary>
    public class Clue : MonoBehaviour
    {
        [Tooltip("Stable unique id, e.g. X_knife. Must match CaseDefinition key evidence ids.")]
        [SerializeField] private string clueId = "clue";
        [SerializeField] private string displayName = "Clue";
        [Tooltip("Informational. The answer check uses CaseDefinition.keyEvidenceIds.")]
        [SerializeField] private bool isKeyEvidence;

        [SerializeField] private SessionManager session;

        [Header("Local feedback (optional)")]
        public UnityEvent onFound = new UnityEvent();
        public UnityEvent onTagged = new UnityEvent();
        public UnityEvent onUntagged = new UnityEvent();

        public string ClueId => clueId;
        public string DisplayName => displayName;
        public bool IsKeyEvidence => isKeyEvidence;

        public bool IsFound => session != null && session.State.IsClueFound(clueId);
        public bool IsTagged => session != null && session.State.IsClueTagged(clueId);

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        /// <summary>Hook to the Interaction SDK's select/grab event. Only the first call per run counts.</summary>
        public void MarkFound()
        {
            if (session != null && session.State.MarkClueFound(clueId)) onFound.Invoke();
        }

        /// <summary>Marks this clue as relevant (e.g. from a "tag" button or board slot).</summary>
        public void MarkTagged()
        {
            if (session != null && session.State.MarkClueTagged(clueId)) onTagged.Invoke();
        }

        public void Untag()
        {
            if (session != null && session.State.UntagClue(clueId)) onUntagged.Invoke();
        }

        public void ToggleTagged()
        {
            if (IsTagged) Untag(); else MarkTagged();
        }
    }
}
