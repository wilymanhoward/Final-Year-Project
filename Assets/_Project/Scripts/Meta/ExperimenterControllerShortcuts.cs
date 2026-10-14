using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Experimenter-only Touch controller shortcuts (participants use hand tracking, so these never fire
    /// for them). Until the in-headset setup panel exists, this is how placement and sessions are driven
    /// in the headset:
    ///   A                     Place Scene Here (anchor)
    ///   B                     Load Saved Placement
    ///   X                     Manual Placement
    ///   hold Y                Clear Placement
    ///   hold right stick      Start session
    ///   hold left stick       Abort session
    ///   hold right stick up   Network: host          (UnityEvents, wired to NetworkSessionLauncher by
    ///   hold right stick down Network: join host      FYP > Set Up Networking, so this assembly needs no Netcode)
    ///   hold left stick down  Network: stop
    ///   left menu button      Open/close the setup panel (UnityEvent onMenuPressed)
    /// Uses physical buttons (OVRInput.RawButton A/B/X/Y/LThumbstick/RThumbstick with OVRInput.Get/GetDown(RawButton),
    /// Library/PackageCache/com.meta.xr.sdk.core/Scripts/OVRInput.cs) so left/right can't be confused by the
    /// controller-relative virtual mapping. Every shortcut that fires is logged as "[Shortcuts] ...".
    /// </summary>
    public class ExperimenterControllerShortcuts : MonoBehaviour
    {
        [SerializeField] private ScenePlacement placement;
        [SerializeField] private ExperimenterPanel panel;
        [Tooltip("Seconds to hold Y (clear) or a stick (start / abort).")]
        [SerializeField, Min(0.2f)] private float holdSeconds = 1.5f;

        [Tooltip("Session status after a start/abort shortcut, e.g. wire to StatusLabel.Show.")]
        public UnityEvent<string> onFeedback = new UnityEvent<string>();

        [Tooltip("Hold right stick up: start this headset as network host.")]
        public UnityEvent onHostRequested = new UnityEvent();
        [Tooltip("Hold right stick down: join the host in experiment_config.json.")]
        public UnityEvent onJoinRequested = new UnityEvent();
        [Tooltip("Hold left stick down: stop hosting / leave.")]
        public UnityEvent onNetworkStopRequested = new UnityEvent();
        [Tooltip("Left controller menu button: open/close the setup panel.")]
        public UnityEvent onMenuPressed = new UnityEvent();

        // Controllers only: with Controller.Active, hand-tracking pinches can also map to buttons.
        private const OVRInput.Controller Touch = OVRInput.Controller.Touch;

        private float _clearHeld;
        private float _startHeld;
        private float _abortHeld;
        private float _hostHeld;
        private float _joinHeld;
        private float _netStopHeld;

        private void Awake()
        {
            if (placement == null) placement = FindFirstObjectByType<ScenePlacement>();
            if (panel == null) panel = FindFirstObjectByType<ExperimenterPanel>();
        }

        private void Update()
        {
            if (placement != null)
            {
                if (OVRInput.GetDown(OVRInput.RawButton.A, Touch)) Fire("A", "Place Scene Here", placement.PlaceSceneHere);
                if (OVRInput.GetDown(OVRInput.RawButton.B, Touch)) Fire("B", "Load Saved Placement", placement.LoadSavedPlacement);
                if (OVRInput.GetDown(OVRInput.RawButton.X, Touch)) Fire("X", "Manual Placement", placement.ManualPlacement);
                if (Held(OVRInput.RawButton.Y, ref _clearHeld)) Fire("hold Y", "Clear Placement", placement.ClearPlacement);
            }
            if (panel != null)
            {
                if (Held(OVRInput.RawButton.RThumbstick, ref _startHeld))
                {
                    Fire("hold right stick", "Start session", panel.StartSession);
                    onFeedback.Invoke(panel.BuildStatus());
                }
                if (Held(OVRInput.RawButton.LThumbstick, ref _abortHeld))
                {
                    Fire("hold left stick", "Abort session", panel.AbortSession);
                    onFeedback.Invoke(panel.BuildStatus());
                }
            }
            if (OVRInput.GetDown(OVRInput.RawButton.Start, Touch)) Fire("menu", "Setup panel", onMenuPressed.Invoke);

            // Stick directions only count while the stick is not clicked, so start/abort can't also fire these.
            bool rClicked = OVRInput.Get(OVRInput.RawButton.RThumbstick, Touch);
            bool lClicked = OVRInput.Get(OVRInput.RawButton.LThumbstick, Touch);
            if (HeldUnless(rClicked, OVRInput.RawButton.RThumbstickUp, ref _hostHeld)) Fire("hold right stick up", "Network host", onHostRequested.Invoke);
            if (HeldUnless(rClicked, OVRInput.RawButton.RThumbstickDown, ref _joinHeld)) Fire("hold right stick down", "Network join", onJoinRequested.Invoke);
            if (HeldUnless(lClicked, OVRInput.RawButton.LThumbstickDown, ref _netStopHeld)) Fire("hold left stick down", "Network stop", onNetworkStopRequested.Invoke);
        }

        private static void Fire(string input, string action, System.Action call)
        {
            Debug.Log($"[Shortcuts] {input} -> {action}");
            call();
        }

        private bool HeldUnless(bool blocked, OVRInput.RawButton button, ref float heldFor)
        {
            if (blocked) { heldFor = -1f; return false; }   // re-arms only after the direction is released
            return Held(button, ref heldFor);
        }

        /// <summary>True once when the button has been held for holdSeconds; re-arms on release.</summary>
        private bool Held(OVRInput.RawButton button, ref float heldFor)
        {
            if (!OVRInput.Get(button, Touch))
            {
                heldFor = 0f;
                return false;
            }
            if (heldFor < 0f) return false;   // already fired during this press
            heldFor += Time.unscaledDeltaTime;
            if (heldFor < holdSeconds) return false;
            heldFor = -1f;
            return true;
        }
    }
}
