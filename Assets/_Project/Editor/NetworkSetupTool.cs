using FYP.Detective.Meta;
using FYP.Detective.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Week 3 Phase 1: adds a "Network" object (NetworkManager + UnityTransport + NetworkSessionLauncher) to the open
    /// scene, wires the controller shortcuts and the status label, and forces the Android INTERNET permission.
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

            // 3. Controller shortcuts -> launcher.
            AddVoidListenerOnce(shortcuts.onHostRequested, launcher, nameof(NetworkSessionLauncher.StartHost), launcher.StartHost);
            AddVoidListenerOnce(shortcuts.onJoinRequested, launcher, nameof(NetworkSessionLauncher.StartClient), launcher.StartClient);
            AddVoidListenerOnce(shortcuts.onNetworkStopRequested, launcher, nameof(NetworkSessionLauncher.StopNetwork), launcher.StopNetwork);
            EditorUtility.SetDirty(shortcuts);
            EditorUtility.SetDirty(launcher);

            // 4. Sockets need INTERNET; Unity doesn't detect UnityTransport's native sockets on its own.
            bool permissionChanged = !PlayerSettings.Android.forceInternetPermission;
            PlayerSettings.Android.forceInternetPermission = true;

            var scene = go.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Networking",
                (created ? "Created 'Network' (NetworkManager + UnityTransport).\n" : "NetworkManager already present.\n") +
                "NetworkSessionLauncher wired: right stick up = host, right stick down = join, left stick down = stop.\n" +
                (permissionChanged ? "Android INTERNET permission forced on.\n" : "") +
                "Scene saved. Rebuild to apply.", "OK");
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
