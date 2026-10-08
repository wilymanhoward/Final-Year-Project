using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Events;

namespace FYP.Detective.Meta
{
    /// <summary>
    /// Places <see cref="CaseRoot"/> in the room from the experimenter's head pose: they stand on the taped
    /// floor mark facing the front wall and press "Place Scene Here". Position = head (x, z) on the floor,
    /// rotation = head yaw only (<see cref="PlacementMath"/>).
    ///
    /// Anchor flow (Meta XR SDK 207, all in Library/PackageCache/com.meta.xr.sdk.core/Scripts/OVRSpatialAnchor.cs):
    ///   place: AddComponent&lt;OVRSpatialAnchor&gt; → WhenLocalizedAsync() → SaveAnchorAsync() → UUID in PlayerPrefs
    ///   load:  LoadUnboundAnchorsAsync(uuids, list) → UnboundAnchor.LocalizeAsync() → TryGetPose → BindTo(new component)
    ///   clear: EraseAnchorsAsync(anchors, uuids)
    /// CaseRoot becomes a child of the anchor object so it follows the anchor's pose.
    ///
    /// Saving/loading anchors needs the runtime "Spatial data" permission (com.oculus.permission.USE_SCENE,
    /// OVRPermissionsRequester.ScenePermission in Scripts/OVRPermissionsRequester.cs); without it SaveAnchorAsync
    /// returns FailurePermissionInsufficient. Place/Load check it first and request it with Unity's Android
    /// Permission API (as recommended in OVRPermissionsRequester's remarks). Manual placement doesn't need it.
    ///
    /// Public methods are meant for buttons / controller shortcuts. Placement is refused while a session runs.
    /// </summary>
    public class ScenePlacement : MonoBehaviour
    {
        public const string SavedAnchorKey = "FYP.Detective.CaseAnchorUuid";
        public const string StatusPlacedSaved = "Placement set (anchor saved)";
        public const string StatusLoaded = "Placement loaded";
        public const string StatusAnchorFailed = "Anchor failed - use Manual";
        public const string StatusManual = "Manual placement set (no anchor)";
        public const string StatusCleared = "Placement cleared";
        public const string StatusNoSaved = "No saved placement - use Place Scene Here";
        public const string StatusPermissionNeeded = "Spatial data permission needed - accept the dialog";
        public const string StatusPermissionGranted = "Spatial data permission granted - press again";
        public const string StatusPermissionDenied =
            "Spatial data permission denied - enable Spatial data in Settings > Apps > Permissions, or use Manual";

        // Permission callback results, applied on the main thread in Update (callbacks may come from the Java thread).
        private const int PermissionPending = 0, PermissionGrantedResult = 1, PermissionDeniedResult = 2;
        private volatile int _permissionResult = PermissionPending;
        private volatile bool _permissionResultReady;

        private const string AnchorObjectName = "CaseAnchor";

        [SerializeField] private SessionManager session;
        [SerializeField] private PlayerRig rig;
        [SerializeField] private CaseRoot caseRoot;
        [Tooltip("OVRCameraRig/TrackingSpace. With tracking origin = Floor Level its height is the floor. Empty = y 0.")]
        [SerializeField] private Transform trackingSpace;
        [Tooltip("Give up on creating/loading an anchor after this many seconds.")]
        [SerializeField, Min(1f)] private float anchorTimeoutSeconds = 15f;

        [Tooltip("Status text for the experimenter, e.g. wire to StatusLabel.Show or a UI Text.")]
        public UnityEvent<string> onStatusChanged = new UnityEvent<string>();

        private OVRSpatialAnchor _anchor;            // anchor CaseRoot currently follows (null = none)
        private GameObject _pendingAnchorObject;     // anchor being created/bound, destroyed on timeout
        private int _operation;                      // bumped per request; stale async results are ignored
        private float _deadline = -1f;
        private readonly List<OVRSpatialAnchor.UnboundAnchor> _unbound = new List<OVRSpatialAnchor.UnboundAnchor>();

        public string Status { get; private set; } = "Not placed";
        public bool IsBusy => _deadline > 0f;
        public bool HasSavedPlacement => PlayerPrefs.HasKey(SavedAnchorKey);

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<SessionManager>();
            if (rig == null) rig = FindFirstObjectByType<PlayerRig>();
            if (caseRoot == null) caseRoot = FindFirstObjectByType<CaseRoot>();
        }

        private void Update()
        {
            if (_permissionResultReady)
            {
                _permissionResultReady = false;
                SetStatus(_permissionResult == PermissionGrantedResult ? StatusPermissionGranted : StatusPermissionDenied);
            }
            if (_deadline > 0f && Time.unscaledTime > _deadline)
            {
                _operation++;
                _deadline = -1f;
                DestroyPending();
                Debug.LogWarning("[ScenePlacement] Anchor operation timed out.");
                SetStatus(StatusAnchorFailed);
            }
        }

        private void OnDestroy() => _operation++;

        // ---- Public actions ----

        /// <summary>Creates a spatial anchor at the head's floor pose, moves CaseRoot onto it and saves it.</summary>
        public async void PlaceSceneHere()
        {
            if (!CanChange() || !EnsureSpatialDataPermission()) return;
            int op = BeginOperation("Creating anchor...");
            Pose pose = HeadFloorPose();
            var go = new GameObject(AnchorObjectName);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _pendingAnchorObject = go;
            var anchor = go.AddComponent<OVRSpatialAnchor>();

            try
            {
                bool localized = await anchor.WhenLocalizedAsync();
                if (!IsCurrent(op)) { DestroyIfAlive(go); return; }
                if (!localized || anchor == null) { DestroyIfAlive(go); Fail("anchor could not be created/localized"); return; }

                _pendingAnchorObject = null;
                UseAnchor(anchor);
                Commit(PlacementMethod.Anchor, false, anchor.Uuid.ToString("D"));

                var save = await anchor.SaveAnchorAsync();
                if (anchor == null || _anchor != anchor) return;   // replaced or cleared meanwhile
                if (save.Success)
                {
                    PlayerPrefs.SetString(SavedAnchorKey, anchor.Uuid.ToString("D"));
                    PlayerPrefs.Save();
                    SetStatus(StatusPlacedSaved);
                }
                else
                {
                    Debug.LogWarning("[ScenePlacement] SaveAnchorAsync failed: " + save.Status);
                    SetStatus(save.Status == OVRAnchor.SaveResult.FailurePermissionInsufficient
                        ? StatusPermissionDenied
                        : "Placement set, but saving failed (" + save.Status + ") - Load won't work");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (IsCurrent(op)) { DestroyIfAlive(go); Fail("exception while creating the anchor"); }
            }
        }

        /// <summary>Loads the anchor saved by <see cref="PlaceSceneHere"/> and moves CaseRoot onto it.</summary>
        public async void LoadSavedPlacement()
        {
            if (!CanChange()) return;
            if (!TryGetSavedUuid(out Guid uuid)) { SetStatus(StatusNoSaved); return; }
            if (!EnsureSpatialDataPermission()) return;
            if (_anchor != null && _anchor.Uuid == uuid)
            {
                SetStatus(StatusLoaded);   // that anchor is already active
                return;
            }

            int op = BeginOperation("Loading saved placement...");
            try
            {
                var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, _unbound);
                if (!IsCurrent(op)) return;
                if (!result.Success || _unbound.Count == 0)
                {
                    Fail("load failed (" + result.Status + ", " + _unbound.Count + " anchors)");
                    return;
                }

                var unbound = _unbound[0];
                if (!unbound.Localized && !await unbound.LocalizeAsync())
                {
                    if (IsCurrent(op)) Fail("saved anchor could not be localized");
                    return;
                }
                if (!IsCurrent(op)) return;

                // Same pattern as Meta's SpatialAnchorCoreBuildingBlock.LoadAnchorsAsync: create the object at the
                // anchor pose, add the component and bind it in the same frame.
                var go = new GameObject(AnchorObjectName);
                if (unbound.TryGetPose(out Pose pose)) go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                var anchor = go.AddComponent<OVRSpatialAnchor>();
                unbound.BindTo(anchor);

                UseAnchor(anchor);
                Commit(PlacementMethod.Anchor, true, uuid.ToString("D"));
                SetStatus(StatusLoaded);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (IsCurrent(op)) Fail("exception while loading the anchor");
            }
        }

        /// <summary>Fallback: same head-based pose, no anchor. Shifts if the headset is recentered.</summary>
        public void ManualPlacement()
        {
            if (!CanChange()) return;
            CancelOperation();
            Pose pose = HeadFloorPose();
            ReleaseAnchor();
            caseRoot.PlaceAt(pose);
            Commit(PlacementMethod.Manual, false, null);
            SetStatus(StatusManual);
        }

        /// <summary>Erases the saved anchor, forgets its UUID and moves CaseRoot back to the origin.</summary>
        public async void ClearPlacement()
        {
            if (!CanChange()) return;
            bool hadSaved = TryGetSavedUuid(out Guid uuid);
            int op = BeginOperation("Clearing placement...");
            ReleaseAnchor();
            caseRoot.PlaceAt(new Pose(Vector3.zero, Quaternion.identity));
            session.SetPlacement(PlacementRecord.None);
            PlayerPrefs.DeleteKey(SavedAnchorKey);
            PlayerPrefs.Save();

            if (!hadSaved)
            {
                EndOperation();
                SetStatus(StatusCleared);
                return;
            }

            try
            {
                var result = await OVRSpatialAnchor.EraseAnchorsAsync(Array.Empty<OVRSpatialAnchor>(), new[] { uuid });
                if (!IsCurrent(op)) return;
                EndOperation();
                if (result.Success) SetStatus(StatusCleared);
                else
                {
                    Debug.LogWarning("[ScenePlacement] EraseAnchorsAsync failed: " + result.Status);
                    SetStatus("Placement cleared (erasing the saved anchor failed: " + result.Status + ")");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (IsCurrent(op)) { EndOperation(); SetStatus("Placement cleared (erasing the saved anchor failed)"); }
            }
        }

        // ---- Helpers ----

        private bool CanChange()
        {
            if (session == null || caseRoot == null || rig == null || rig.Head == null)
            {
                SetStatus("Placement not set up (missing SessionManager, CaseRoot or PlayerRig head)");
                return false;
            }
            if (session.IsRunning)
            {
                SetStatus("Stop the session before changing placement");
                return false;
            }
            return true;
        }

        /// <summary>
        /// True if Spatial data is granted. Otherwise asks for it (Android dialog), shows a status and returns
        /// false; the result is shown when the user answers, and the experimenter presses the button again.
        /// </summary>
        private bool EnsureSpatialDataPermission()
        {
            if (OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene)) return true;

            SetStatus(StatusPermissionNeeded);
#if UNITY_ANDROID && !UNITY_EDITOR
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => ReportPermission(PermissionGrantedResult);
            callbacks.PermissionDenied += _ => ReportPermission(PermissionDeniedResult);
            callbacks.PermissionRequestDismissed += _ => ReportPermission(PermissionDeniedResult);
            Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission, callbacks);
#endif
            return false;
        }

        private void ReportPermission(int result)
        {
            _permissionResult = result;
            _permissionResultReady = true;
        }

        private Pose HeadFloorPose()
        {
            float floorY = trackingSpace != null ? trackingSpace.position.y : 0f;
            return PlacementMath.FloorPoseFromHead(rig.Head.position, rig.Head.rotation, floorY);
        }

        /// <summary>CaseRoot follows <paramref name="anchor"/>; any previous anchor object is destroyed.</summary>
        private void UseAnchor(OVRSpatialAnchor anchor)
        {
            if (_anchor != anchor) ReleaseAnchor();
            _anchor = anchor;
            caseRoot.AttachTo(anchor.transform);
            EndOperation();
        }

        /// <summary>Unparents CaseRoot (keeping its pose) and destroys the current anchor object.</summary>
        private void ReleaseAnchor()
        {
            if (caseRoot != null) caseRoot.Detach();
            if (_anchor != null) Destroy(_anchor.gameObject);
            _anchor = null;
        }

        private void Commit(PlacementMethod method, bool loaded, string uuid)
        {
            Transform t = caseRoot.transform;
            var record = new PlacementRecord(method, loaded, uuid, t.position, t.eulerAngles.y, DateTime.UtcNow);
            if (!session.SetPlacement(record))
                Debug.LogWarning("[ScenePlacement] SessionManager refused the placement (session running?).");
        }

        private int BeginOperation(string status)
        {
            _operation++;
            DestroyPending();
            _deadline = Time.unscaledTime + anchorTimeoutSeconds;
            SetStatus(status);
            return _operation;
        }

        private void CancelOperation()
        {
            _operation++;
            DestroyPending();
            _deadline = -1f;
        }

        private void EndOperation() => _deadline = -1f;

        private bool IsCurrent(int op) => this != null && op == _operation;

        private void Fail(string reason)
        {
            EndOperation();
            Debug.LogWarning("[ScenePlacement] " + reason);
            SetStatus(StatusAnchorFailed);
        }

        private void DestroyPending()
        {
            DestroyIfAlive(_pendingAnchorObject);
            _pendingAnchorObject = null;
        }

        private static void DestroyIfAlive(GameObject go)
        {
            if (go != null) Destroy(go);
        }

        private static bool TryGetSavedUuid(out Guid uuid)
        {
            uuid = Guid.Empty;
            string saved = PlayerPrefs.GetString(SavedAnchorKey, "");
            return saved.Length > 0 && Guid.TryParse(saved, out uuid) && uuid != Guid.Empty;
        }

        private void SetStatus(string status)
        {
            Status = status;
            Debug.Log("[ScenePlacement] " + status);
            onStatusChanged.Invoke(status);
        }
    }
}
