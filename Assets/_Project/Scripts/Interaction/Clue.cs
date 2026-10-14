using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective
{
    /// <summary>
    /// A piece of evidence in the scene. Holds its data and requests "found"/"tagged" through
    /// <see cref="SessionManager"/> (applied locally in solo runs, via the host in team runs). It does NOT implement
    /// grabbing - the Meta Interaction SDK does. Wired by the FYP clue menu tools (or by hand in the Inspector):
    ///   Oculus.Interaction.InteractableUnityEventWrapper.WhenSelect   -> Clue.MarkFound(), Clue.BeginHold()
    ///   Oculus.Interaction.InteractableUnityEventWrapper.WhenUnselect -> Clue.EndHold()   (grabbable clues)
    /// (one wrapper per interactable, e.g. HandGrabInteractable and GrabInteractable, or the PokeInteractable
    /// of a static clue; MarkFound is idempotent so double wiring is harmless). Only counts while a session runs.
    /// Team runs: <see cref="HasLocalAuthority"/> says whether this headset drives the clue's position (set by the
    /// networking layer), and <see cref="ForceRelease"/> drops it when the partner grabs it.
    /// Keeping Meta types out of this script means the core assembly compiles and unit tests run without the SDK.
    /// </summary>
    public class Clue : MonoBehaviour
    {
        [Tooltip("Stable unique id, e.g. X_knife. Must match CaseDefinition key evidence ids.")]
        [SerializeField] private string clueId = "clue";
        [SerializeField] private string displayName = "Clue";
        [Tooltip("Informational. The answer check uses CaseDefinition.keyEvidenceIds.")]
        [SerializeField] private bool isKeyEvidence;
        [Tooltip("What the participant reads once the clue is found (shown above its tag button).")]
        [TextArea(1, 4)]
        [SerializeField] private string inspectText = "";

        [SerializeField] private SessionManager session;

        [Tooltip("Grab interactables (HandGrabInteractable, GrabInteractable). Briefly disabled by ForceRelease, which " +
                 "makes the Interaction SDK drop the clue. Filled in by FYP > Set Up Networking.")]
        [SerializeField] private Behaviour[] grabInteractables = Array.Empty<Behaviour>();

        [Header("Local feedback (optional)")]
        public UnityEvent onFound = new UnityEvent();
        public UnityEvent onTagged = new UnityEvent();
        public UnityEvent onUntagged = new UnityEvent();

        /// <summary>Raised when a hand on this headset starts holding the clue.</summary>
        public event Action<Clue> HoldBegan;

        public string ClueId => clueId;
        public string DisplayName => displayName;
        public bool IsKeyEvidence => isKeyEvidence;
        public string InspectText => inspectText;

        public bool IsFound => session != null && session.State.IsClueFound(clueId);
        public bool IsTagged => session != null && session.State.IsClueTagged(clueId);

        /// <summary>True while a session is running (clues only respond then).</summary>
        public bool IsSessionRunning => session != null && session.IsRunning;

        /// <summary>
        /// True if this headset controls the clue's position (always in solo runs; in team runs the owner of its
        /// network object). Only the controlling headset puts a stray clue back home.
        /// </summary>
        public bool HasLocalAuthority { get; set; } = true;

        private int _holdCount;

        /// <summary>True while at least one interactor holds this clue (it is never reset while held).</summary>
        public bool IsHeld => _holdCount > 0;

        /// <summary>Hook to WhenSelect of a grab interactable.</summary>
        public void BeginHold()
        {
            _holdCount++;
            if (_holdCount == 1) HoldBegan?.Invoke(this);
        }

        /// <summary>Hook to WhenUnselect of a grab interactable.</summary>
        public void EndHold()
        {
            if (_holdCount > 0) _holdCount--;
        }

        /// <summary>True for a grabbable clue whose grab interactables are known (networked in team runs).</summary>
        public bool IsNetworkedGrabbable => grabInteractables.Length > 0;

        /// <summary>Enables/disables grabbing (used to hide the inactive case's clues).</summary>
        public void SetInteractable(bool interactable)
        {
            foreach (Behaviour b in grabInteractables) if (b != null) b.enabled = interactable;
        }

        /// <summary>Makes the Interaction SDK let go of the clue (disables its grab interactables for one frame).</summary>
        public void ForceRelease()
        {
            if (!IsHeld || grabInteractables.Length == 0 || !isActiveAndEnabled) return;
            StartCoroutine(ReleaseRoutine());
        }

        private IEnumerator ReleaseRoutine()
        {
            foreach (Behaviour b in grabInteractables) if (b != null) b.enabled = false;
            yield return null;
            foreach (Behaviour b in grabInteractables) if (b != null) b.enabled = true;
            _holdCount = 0;
        }

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void OnEnable()
        {
            if (session == null) return;
            session.State.ClueFound += HandleFound;
            session.State.ClueTagged += HandleTagged;
            session.State.ClueUntagged += HandleUntagged;
        }

        private void OnDisable()
        {
            if (session == null) return;
            session.State.ClueFound -= HandleFound;
            session.State.ClueTagged -= HandleTagged;
            session.State.ClueUntagged -= HandleUntagged;
        }

        /// <summary>Hook to the Interaction SDK's select/grab event. Only the first find per run counts.</summary>
        public void MarkFound()
        {
            if (session != null) session.RequestClueFound(clueId);
        }

        /// <summary>Marks this clue as relevant (e.g. from a "tag" button or board slot).</summary>
        public void MarkTagged()
        {
            if (session != null) session.RequestClueTagged(clueId, true);
        }

        public void Untag()
        {
            if (session != null) session.RequestClueTagged(clueId, false);
        }

        public void ToggleTagged()
        {
            if (IsTagged) Untag(); else MarkTagged();
        }

        // Feedback follows the state, so it also fires when the partner found/tagged this clue.
        private void HandleFound(string id) { if (id == clueId) onFound.Invoke(); }
        private void HandleTagged(string id) { if (id == clueId) onTagged.Invoke(); }
        private void HandleUntagged(string id) { if (id == clueId) onUntagged.Invoke(); }
    }
}
