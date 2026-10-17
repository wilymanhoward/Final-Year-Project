using FYP.Detective.Meta;
using FYP.Detective.Net;
using Unity.Netcode;
using Unity.Netcode.Components;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Week 3: adds a "Network" object (NetworkManager + UnityTransport + NetworkSessionLauncher +
    /// ColocationCoordinator) to the open scene, wires the controller shortcuts and the status label, and forces the Android INTERNET permission.
    /// Also adds the <see cref="DataUploader"/> (run files to the laptop) and allows plain HTTP for it.
    /// Idempotent: running it again only fills in what is missing.
    /// </summary>
    /// <remarks>
    /// Verified in com.unity.netcode.gameobjects 2.13.3: <c>NetworkManager.NetworkConfig</c> (null until the editor's
    /// reset hook runs, Editor/NetworkManagerHelper.cs), <c>NetworkConfig.NetworkTransport</c> and
    /// <c>Prefabs.NetworkPrefabsLists</c> (Runtime/Configuration; a manager added from script may also get the
    /// default list via the reset hook, which this tool removes), <c>UnityTransport.ConnectionData.Port</c>.
    /// </remarks>
    public static class NetworkSetupTool
    {
        private const string ObjectName = "Network";
        private const string DefaultPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

        [MenuItem("FYP/Set Up Networking")]
        public static void SetUp()
        {
            var session = Object.FindFirstObjectByType<SessionManager>();
            var shortcuts = Object.FindFirstObjectByType<ExperimenterControllerShortcuts>();
            var label = Object.FindFirstObjectByType<StatusLabel>(FindObjectsInactive.Include);
            var logger = Object.FindFirstObjectByType<DataLogger>();
            var placement = Object.FindFirstObjectByType<ScenePlacement>();
            if (session == null || shortcuts == null || label == null)
            {
                EditorUtility.DisplayDialog("Networking",
                    "Open Main.unity and run FYP > Set Up Case Root and Placement first " +
                    "(needs SessionManager, ExperimenterControllerShortcuts and the status label).", "OK");
                return;
            }

            // 1. NetworkManager + transport on one object.
            var manager = Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
            bool created = manager == null;
            GameObject go = created ? new GameObject(ObjectName) : manager.gameObject;
            if (created) manager = go.AddComponent<NetworkManager>();
            var transport = GetOrAdd<UnityTransport>(go);

            Undo.RecordObject(manager, "Set up networking");
            if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.NetworkTransport = transport;
            // No network prefabs yet. NGO's generated DefaultNetworkPrefabs list holds Meta's Building Block
            // prefabs (avatars, name tags) we don't use, so it is deliberately not attached.
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.RemoveAll(l => l != null && AssetDatabase.GetAssetPath(l) == DefaultPrefabsPath);
            EditorUtility.SetDirty(manager);

            Undo.RecordObject(transport, "Set up networking");
            var data = transport.ConnectionData;
            data.Port = (ushort)ExperimentConfig.DefaultNetPort;
            transport.ConnectionData = data;
            EditorUtility.SetDirty(transport);

            // 2. Launcher with its references.
            var launcher = GetOrAdd<NetworkSessionLauncher>(go);
            MainSceneBuilder.SetObject(launcher, "networkManager", manager);
            MainSceneBuilder.SetObject(launcher, "transport", transport);
            MainSceneBuilder.SetObject(launcher, "session", session);
            if (logger != null) MainSceneBuilder.SetObject(launcher, "logger", logger);
            AddListenerOnce(launcher.onStatusChanged, label);

            // 3. Colocation coordinator (Phase 2): discovery + shared anchor on top of the launcher.
            var coordinator = GetOrAdd<ColocationCoordinator>(go);
            MainSceneBuilder.SetObject(coordinator, "launcher", launcher);
            MainSceneBuilder.SetObject(coordinator, "session", session);
            if (placement != null) MainSceneBuilder.SetObject(coordinator, "placement", placement);
            AddListenerOnce(coordinator.onStatusChanged, label);

            // 4. Controller shortcuts -> coordinator (Phase 1 wired them straight to the launcher; remove those).
            RemoveListeners(shortcuts.onHostRequested, launcher);
            RemoveListeners(shortcuts.onJoinRequested, launcher);
            RemoveListeners(shortcuts.onNetworkStopRequested, launcher);
            AddVoidListenerOnce(shortcuts.onHostRequested, coordinator, nameof(ColocationCoordinator.Host), coordinator.Host);
            AddVoidListenerOnce(shortcuts.onJoinRequested, coordinator, nameof(ColocationCoordinator.Join), coordinator.Join);
            AddVoidListenerOnce(shortcuts.onNetworkStopRequested, coordinator, nameof(ColocationCoordinator.Stop), coordinator.Stop);
            EditorUtility.SetDirty(shortcuts);
            EditorUtility.SetDirty(launcher);
            EditorUtility.SetDirty(coordinator);

            // 5. Phase 3: team state sync + networked grabbable clues.
            var team = GetOrAdd<TeamSessionSync>(go);
            MainSceneBuilder.SetObject(team, "launcher", launcher);
            MainSceneBuilder.SetObject(team, "session", session);
            AddListenerOnce(team.onStatusChanged, label);
            EditorUtility.SetDirty(team);
            int networkedClues = 0;
            foreach (var clue in Object.FindObjectsByType<Clue>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (MakeNetworked(clue)) networkedClues++;
            }

            // 6. Sockets need INTERNET; Unity doesn't detect UnityTransport's native sockets on its own.
            bool permissionChanged = !PlayerSettings.Android.forceInternetPermission;
            PlayerSettings.Android.forceInternetPermission = true;

            // 7. Data upload to the laptop (session 6): DataUploader next to the DataLogger, plain HTTP allowed
            //    (local network only, user decision; CleartextHttpBuildStep also marks the Gradle manifests).
            bool uploaderAdded = false;
            if (logger != null)
            {
                var uploader = logger.GetComponent<DataUploader>();
                uploaderAdded = uploader == null;
                if (uploaderAdded) uploader = logger.gameObject.AddComponent<DataUploader>();
                MainSceneBuilder.SetObject(uploader, "session", session);
                var setupPanel = Object.FindFirstObjectByType<SetupPanel>(FindObjectsInactive.Include);
                if (setupPanel != null) MainSceneBuilder.SetObject(setupPanel, "uploader", uploader);
            }
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            var scene = go.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Networking",
                (created ? "Created 'Network' (NetworkManager + UnityTransport).\n" : "NetworkManager already present.\n") +
                "Launcher + ColocationCoordinator wired: right stick up = host (advertise + share anchor), " +
                "right stick down = join (discover + load anchor), left stick down = stop.\n" +
                $"TeamSessionSync added; {networkedClues} grabbable clue(s) networked.\n" +
                (placement == null ? "WARNING: no ScenePlacement found - run Set Up Case Root and Placement.\n" : "") +
                (permissionChanged ? "Android INTERNET permission forced on.\n" : "") +
                (logger == null ? "WARNING: no DataLogger - data upload not set up.\n"
                    : (uploaderAdded ? "DataUploader added" : "DataUploader present") + " (sends run files to tools/receive_data.py; HTTP allowed).\n") +
                "Scene saved. Rebuild to apply.", "OK");
        }

        /// <summary>
        /// Grabbable clue → NetworkObject (no parent sync: CaseRoot isn't a NetworkObject) + NetworkTransform (owner
        /// authority, local space = relative to CaseRoot) + NetworkClue, and fills Clue.grabInteractables.
        /// Static (poke-only) clues are skipped: they never move. Returns true if the clue is grabbable.
        /// </summary>
        private static bool MakeNetworked(Clue clue)
        {
            var grabs = new System.Collections.Generic.List<Behaviour>();
            grabs.AddRange(clue.GetComponentsInChildren<HandGrabInteractable>(true));
            grabs.AddRange(clue.GetComponentsInChildren<GrabInteractable>(true));
            if (grabs.Count == 0) return false;

            GameObject go = clue.gameObject;
            var netObj = GetOrAdd<NetworkObject>(go);
            netObj.AutoObjectParentSync = false;
            var netTransform = GetOrAdd<NetworkTransform>(go);
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            netTransform.InLocalSpace = true;
            GetOrAdd<NetworkClue>(go);

            var so = new SerializedObject(clue);
            var arr = so.FindProperty("grabInteractables");
            arr.arraySize = grabs.Count;
            for (int i = 0; i < grabs.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = grabs[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(netObj);
            EditorUtility.SetDirty(netTransform);
            EditorUtility.SetDirty(clue);
            return true;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void AddListenerOnce(UnityEvent<string> evt, StatusLabel label)
        {
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            {
                if (evt.GetPersistentTarget(i) == label && evt.GetPersistentMethodName(i) == nameof(StatusLabel.Show)) return;
            }
            UnityEventTools.AddPersistentListener(evt, label.Show);
        }

        private static void RemoveListeners(UnityEvent evt, Object target)
        {
            for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                if (evt.GetPersistentTarget(i) == target) UnityEventTools.RemovePersistentListener(evt, i);
            }
        }

        private static void AddVoidListenerOnce(UnityEvent evt, Object target, string method, UnityAction call)
        {
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            {
                if (evt.GetPersistentTarget(i) == target && evt.GetPersistentMethodName(i) == method) return;
            }
            UnityEventTools.AddPersistentListener(evt, call);
        }
    }
}
