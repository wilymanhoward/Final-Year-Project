using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// One-click builder for the MR study scene (menu: FYP > Create Main Scene).
    /// Does what the Meta "Camera Rig" and "Passthrough" building blocks do, using the same SDK prefabs
    /// (OVRCameraRig.prefab, PassthroughUnderlay.prefab), then adds the study systems:
    /// SessionManager, DataLogger, ExperimenterPanel, PlayerRig and placeholder Case X / Case Y assets.
    /// Editor-only. Safe to run again; it asks before overwriting Main.unity.
    /// </summary>
    public static class MainSceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string RigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
        private const string PassthroughPrefabPath =
            "Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/Passthrough/Prefabs/PassthroughUnderlay.prefab";
        private const string CaseXPath = "Assets/_Project/Cases/CaseX/CaseX_Definition.asset";
        private const string CaseYPath = "Assets/_Project/Cases/CaseY/CaseY_Definition.asset";

        [MenuItem("FYP/Create Main Scene")]
        public static void CreateMainScene()
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            var passthroughPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PassthroughPrefabPath);
            if (rigPrefab == null || passthroughPrefab == null)
            {
                EditorUtility.DisplayDialog("Main scene",
                    "Could not find the Meta SDK prefabs:\n" +
                    (rigPrefab == null ? RigPrefabPath + "\n" : "") +
                    (passthroughPrefab == null ? PassthroughPrefabPath + "\n" : "") +
                    "\nMake sure the Meta XR SDK finished importing, then try again.", "OK");
                return;
            }

            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Main scene",
                    ScenePath + " already exists. Replace it with a fresh scene?", "Replace", "Cancel"))
            {
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Camera rig (head + hand/controller tracking), same prefab as the Camera Rig building block.
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "OVRCameraRig";
            var manager = rig.GetComponent<OVRManager>();
            if (manager != null)
            {
                manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
                manager.isInsightPassthroughEnabled = true;
            }
            else
            {
                Debug.LogWarning("[MainSceneBuilder] OVRManager not found on the camera rig prefab.");
            }

            // 2. Passthrough needs a transparent camera background.
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

            // 3. Passthrough layer (same prefab as the Passthrough building block).
            var passthrough = (GameObject)PrefabUtility.InstantiatePrefab(passthroughPrefab);
            passthrough.name = "PassthroughUnderlay";

            // 4. Manifest features: passthrough + hands and controllers.
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config != null)
            {
                config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
                config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
                OVRProjectConfig.CommitProjectConfig(config);
            }
            else
            {
                Debug.LogWarning("[MainSceneBuilder] OVRProjectConfig not ready; set Passthrough Support = Required " +
                                 "and Hand Tracking = Controllers And Hands in Project Settings > Meta XR.");
            }

            // 5. Study systems.
            var playerRig = rig.AddComponent<PlayerRig>();
            SetObject(playerRig, "head", FindDeep(rig.transform, "CenterEyeAnchor"));
            SetObject(playerRig, "leftHand", FindDeep(rig.transform, "LeftHandAnchor"));
            SetObject(playerRig, "rightHand", FindDeep(rig.transform, "RightHandAnchor"));

            var caseX = GetOrCreateCase(CaseXPath, CaseId.X);
            var caseY = GetOrCreateCase(CaseYPath, CaseId.Y);

            var systems = new GameObject("SessionSystems");
            var session = systems.AddComponent<SessionManager>();
            SetObject(session, "caseX", caseX);
            SetObject(session, "caseY", caseY);
            var logger = systems.AddComponent<DataLogger>();
            SetObject(logger, "session", session);
            SetObject(logger, "rig", playerRig);
            var panel = systems.AddComponent<ExperimenterPanel>();
            SetObject(panel, "session", session);

            // 6. A visible test object 1 m in front of the origin, 1 m up (delete later).
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "TestCube (delete me)";
            cube.transform.position = new Vector3(0f, 1f, 1f);
            cube.transform.localScale = Vector3.one * 0.2f;
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultMaterial != null)
            {
                cube.GetComponent<MeshRenderer>().sharedMaterial = pipeline.defaultMaterial;
            }

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 7. Save and make it the only scene in the build.
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Main scene",
                "Created " + ScenePath + " and set it as the only scene in Build Profiles.\n\n" +
                "It contains: OVRCameraRig (head + hand tracking), PassthroughUnderlay, SessionSystems, " +
                "a small test cube and placeholder Case X / Case Y assets.\n\n" +
                "Next: Meta > Tools > Project Setup Tool > Fix All, then build to the Quest.", "OK");
        }

        private static CaseDefinition GetOrCreateCase(string path, CaseId id)
        {
            var existing = AssetDatabase.LoadAssetAtPath<CaseDefinition>(path);
            if (existing != null) return existing;

            string p = id.ToString();
            var asset = ScriptableObject.CreateInstance<CaseDefinition>();
            var so = new SerializedObject(asset);
            so.FindProperty("caseId").intValue = (int)id;
            so.FindProperty("title").stringValue = "Case " + p + " (placeholder)";
            so.FindProperty("correctSuspectId").stringValue = p + "_butler";

            string[] suspects = { "butler", "gardener", "cook" };
            var suspectList = so.FindProperty("suspects");
            suspectList.arraySize = suspects.Length;
            for (int i = 0; i < suspects.Length; i++)
            {
                var element = suspectList.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").stringValue = p + "_" + suspects[i];
                element.FindPropertyRelative("displayName").stringValue = suspects[i];
            }

            string[] evidence = { "knife", "letter" };
            var evidenceList = so.FindProperty("keyEvidenceIds");
            evidenceList.arraySize = evidence.Length;
            for (int i = 0; i < evidence.Length; i++)
            {
                evidenceList.GetArrayElementAtIndex(i).stringValue = p + "_" + evidence[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Sets a private [SerializeField] object reference by field name.</summary>
        private static void SetObject(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[MainSceneBuilder] {target.GetType().Name} has no serialized field '{fieldName}'.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
