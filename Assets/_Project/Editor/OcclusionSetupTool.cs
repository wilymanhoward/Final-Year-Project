using FYP.Detective.Meta;
using Meta.XR.EnvironmentDepth;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if FYP_OPENXR_META
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;
#endif

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Depth API occlusion (user decision, session 6): real people and objects hide the virtual ones behind them, so the
    /// partner isn't drawn over by a desk. Idempotent: one "Depth Occlusion" object with Meta's EnvironmentDepthManager
    /// (soft occlusion, hands removed from the depth so the ghost hands aren't cut by the real ones),
    /// <see cref="DepthOcclusionMonitor"/> (logs depth_occlusion) and <see cref="UiOcclusion"/> (FYP/OccludedUI material
    /// for the world-space panels); refreshes the prop materials (all on FYP/OccludedProp); makes sure the scene
    /// permission is requested at startup and scene support is on. Saves the scene.
    /// </summary>
    /// <remarks>
    /// Verified in com.meta.xr.sdk.core 207: <c>Meta.XR.EnvironmentDepth.EnvironmentDepthManager</c>
    /// (Scripts/EnvironmentDepth/EnvironmentDepthManager.cs: serialized <c>_occlusionShadersMode</c> (OcclusionShadersMode
    /// None / HardOcclusion / SoftOcclusion, sets the global HARD_OCCLUSION / SOFT_OCCLUSION keyword), <c>_removeHands</c>;
    /// tracking space from <c>OVRCameraRig.GetTrackingSpace()</c> unless CustomTrackingSpace is set; waits for
    /// com.oculus.permission.USE_SCENE), <c>OVRManager.requestScenePermissionOnStartup</c> (Scripts/OVRManager.cs:1265,
    /// internal serialized field), <c>OVRProjectConfig.sceneSupport</c> (Editor/OVRProjectConfig.cs:170).
    /// </remarks>
    public static class OcclusionSetupTool
    {
        public const string ObjectName = "Depth Occlusion";
        public const string UiMaterialPath = "Assets/_Project/Art/Materials/UI_Occluded.mat";
        public const string UiShaderName = "FYP/OccludedUI";

        [MenuItem("FYP/Set Up Occlusion (Depth API)")]
        public static void SetUp()
        {
            var session = Object.FindFirstObjectByType<SessionManager>();
            var manager = Object.FindFirstObjectByType<OVRManager>();
            if (session == null || manager == null)
            {
                EditorUtility.DisplayDialog("Occlusion", "Open Main.unity first (it needs the SessionManager and OVRManager).", "OK");
                return;
            }

            var depth = Object.FindFirstObjectByType<EnvironmentDepthManager>(FindObjectsInactive.Include);
            GameObject go = depth != null ? depth.gameObject : new GameObject(ObjectName);
            if (depth == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Set up occlusion");
                depth = go.AddComponent<EnvironmentDepthManager>();
            }
            go.name = ObjectName;
            go.SetActive(true);
            depth.enabled = true;
            var so = new SerializedObject(depth);
            so.FindProperty("_occlusionShadersMode").enumValueIndex = (int)OcclusionShadersMode.SoftOcclusion;
            so.FindProperty("_removeHands").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            var monitor = go.GetComponent<DepthOcclusionMonitor>();
            if (monitor == null) monitor = go.AddComponent<DepthOcclusionMonitor>();
            MainSceneBuilder.SetObject(monitor, "depth", depth);
            MainSceneBuilder.SetObject(monitor, "session", session);

            var ui = go.GetComponent<UiOcclusion>();
            if (ui == null) ui = go.AddComponent<UiOcclusion>();
            MainSceneBuilder.SetObject(ui, "material", UiMaterial());

            foreach (var m in PropAssets.All) EditorUtility.SetDirty(m);   // (re)creates them on FYP/OccludedProp

            // Default OFF (user decision, session 6): the experimenter switches it on with the setup panel's
            // "Occlusion" button when wanted. SessionManager keeps its config in the scene, so set it there too.
            var sso = new SerializedObject(session);
            sso.FindProperty("config.occlusionEnabled").boolValue = false;
            sso.ApplyModifiedPropertiesWithoutUndo();

            var mso = new SerializedObject(manager);
            mso.FindProperty("requestScenePermissionOnStartup").boolValue = true;
            mso.ApplyModifiedPropertiesWithoutUndo();
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config.sceneSupport == OVRProjectConfig.FeatureSupport.None)
            {
                config.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
                OVRProjectConfig.CommitProjectConfig(config);
            }

            string openXr = EnableOpenXrOcclusion();

            AssetDatabase.SaveAssets();
            var scene = session.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog("Occlusion",
                "Depth API occlusion set up:\n" +
                "- EnvironmentDepthManager: soft occlusion, hands removed from depth\n" +
                "- Prop materials on " + PropAssets.PropShaderName + ", world-space UI on " + UiShaderName + " (applied at start)\n" +
                "- Scene permission requested at startup; scene support = " + config.sceneSupport + "\n" +
                "- Runs log depth_occlusion (status=active / no_permission / unsupported ...)\n" +
                "- OpenXR (Android): " + openXr + "\n\n" +
                "On each headset allow \"spatial data\" when asked (once). Over Link: Meta Quest Link app > Settings > " +
                "Developer > Spatial Data over Meta Quest Link. Scene saved; rebuild to apply.", "OK");
        }

        /// <summary>
        /// The OpenXR backend's depth source (found session 6: without it EnvironmentDepthManager reports unsupported):
        /// package com.unity.xr.meta-openxr (2.5.1, user-approved) and, on Android, its features "Meta Quest: Occlusion"
        /// (AROcclusionFeature, enables XR_META_environment_depth) and "Meta Quest: Session" (ARSessionFeature, its required
        /// dependency) - exactly Meta's own project-setup rule (com.meta.xr.sdk.core Editor/EnvironmentDepth/
        /// ProjectSetupDepthAPI.cs: "DepthAPI requires Occlusions and Session to be enabled in the OpenXR menu").
        /// Render mode must be Single Pass Instanced (Multiview), already the case.
        /// </summary>
        internal static string EnableOpenXrOcclusion()
        {
#if FYP_OPENXR_META
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) return "no OpenXR settings for Android";
            var occlusion = settings.GetFeature<AROcclusionFeature>();
            var sessionFeature = settings.GetFeature<ARSessionFeature>();
            if (occlusion == null || sessionFeature == null) return "Meta Quest features not found (reopen the project)";
            occlusion.enabled = true;
            sessionFeature.enabled = true;
            EditorUtility.SetDirty(occlusion);
            EditorUtility.SetDirty(sessionFeature);
            return "Meta Quest: Occlusion + Session enabled" +
                (settings.renderMode == OpenXRSettings.RenderMode.SinglePassInstanced ? "" : " (WARNING: set render mode to Single Pass Instanced)");
#else
            return "MISSING package com.unity.xr.meta-openxr - occlusion can't work on OpenXR";
#endif
        }

        /// <summary>True when the OpenXR occlusion features are on (for FYP > Prepare Study Build).</summary>
        internal static bool IsOpenXrOcclusionEnabled(out string why)
        {
#if FYP_OPENXR_META
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var occlusion = settings != null ? settings.GetFeature<AROcclusionFeature>() : null;
            var sessionFeature = settings != null ? settings.GetFeature<ARSessionFeature>() : null;
            bool ok = occlusion != null && occlusion.enabled && sessionFeature != null && sessionFeature.enabled
                && settings.renderMode == OpenXRSettings.RenderMode.SinglePassInstanced;
            why = ok ? "" : "OpenXR features 'Meta Quest: Occlusion' + 'Meta Quest: Session' (Android) or Multiview not set";
            return ok;
#else
            why = "package com.unity.xr.meta-openxr is not installed";
            return false;
#endif
        }

        /// <summary>Batch mode: opens Main.unity and runs <see cref="SetUp"/> (dialogs don't block in batch mode).</summary>
        public static void SetUpBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity");
            SetUp();
        }

        /// <summary>The depth-occluded UI material (created / updated on access).</summary>
        public static Material UiMaterial()
        {
            var shader = Shader.Find(UiShaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(UiMaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, UiMaterialPath);
            }
            else if (m.shader != shader) m.shader = shader;
            m.SetFloat("_EnvironmentDepthBias", PropAssets.PropDepthBias);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
