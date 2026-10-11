using UnityEngine;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Forwards headset on/off to <see cref="SessionManager.SetHeadsetWorn"/>, so time with the headset off
    /// counts as paused (logged as hmd_unmounted / hmd_mounted). Uses the static events
    /// <c>OVRManager.HMDUnmounted</c> / <c>OVRManager.HMDMounted</c>, raised from the proximity sensor
    /// (<c>OVRPlugin.userPresent</c>) in OVRManager's update
    /// (Library/PackageCache/com.meta.xr.sdk.core/Scripts/OVRManager.cs:305-310, 2817-2825).
    /// </summary>
    public class HeadsetPresenceMonitor : MonoBehaviour
    {
        [SerializeField] private SessionManager session;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void OnEnable()
        {
            OVRManager.HMDUnmounted += HandleUnmounted;
            OVRManager.HMDMounted += HandleMounted;
        }

        private void OnDisable()
        {
            OVRManager.HMDUnmounted -= HandleUnmounted;
            OVRManager.HMDMounted -= HandleMounted;
        }

        private void HandleUnmounted()
        {
            if (session != null) session.SetHeadsetWorn(false);
        }

        private void HandleMounted()
        {
            if (session != null) session.SetHeadsetWorn(true);
        }
    }
}
