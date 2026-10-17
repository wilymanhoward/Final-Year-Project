using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.Android;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Reports whether Meta's environment depth occlusion actually works to <see cref="SessionManager.ReportDepthOcclusion"/>
    /// (logged as depth_occlusion at session start and on every change), checked twice a second. Uses (installed core
    /// SDK 207, Scripts/EnvironmentDepth/EnvironmentDepthManager.cs): <c>EnvironmentDepthManager.IsSupported</c> (static),
    /// <c>IsDepthAvailable</c>; the manager waits for <c>OVRPermissionsRequester.ScenePermission</c>
    /// ("com.oculus.permission.USE_SCENE", Scripts/OVRPermissionsRequester.cs:96), checked here the same way.
    /// Added next to the manager by FYP > Set Up Occlusion.
    /// </summary>
    public class DepthOcclusionMonitor : MonoBehaviour
    {
        [SerializeField] private EnvironmentDepthManager depth;
        [SerializeField] private SessionManager session;
        [SerializeField, Min(0.1f)] private float checkIntervalSeconds = 0.5f;

        private float _nextCheck;

        private void Awake()
        {
            if (depth == null) depth = FindFirstObjectByType<EnvironmentDepthManager>();
            if (session == null) session = FindFirstObjectByType<SessionManager>();
        }

        private void Update()
        {
            if (session == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + checkIntervalSeconds;
            session.ReportDepthOcclusion(Current());
        }

        private DepthOcclusionStatus Current()
        {
            if (!EnvironmentDepthManager.IsSupported) return DepthOcclusionStatus.Unsupported;
            if (depth == null || !depth.isActiveAndEnabled) return DepthOcclusionStatus.Off;
            if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission)) return DepthOcclusionStatus.NoPermission;
            return depth.IsDepthAvailable ? DepthOcclusionStatus.Active : DepthOcclusionStatus.Waiting;
        }
    }
}
