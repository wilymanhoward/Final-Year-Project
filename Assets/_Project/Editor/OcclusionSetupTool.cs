using FYP.Detective.Meta;
using Meta.XR.EnvironmentDepth;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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

            var mso = new SerializedObject(manager);
            mso.FindProperty("requestScenePermissionOnStartup").boolValue = true;
            mso.ApplyModifiedPropertiesWithoutUndo();
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config.sceneSupport == OVRProjectConfig.FeatureSupport.None)
            {
                config.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
                OVRProjectConfig.CommitProjectConfig(config);
            }

            AssetDatabase.SaveAssets();
            var scene = session.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog("Occlusion",
                "Depth API occlusion set up:\n" +
                "- EnvironmentDepthManager: soft occlusion, hands removed from depth\n" +
                "- Prop materials on " + PropAssets.PropShaderName + ", world-space UI on " + UiShaderName + " (applied at start)\n" +
                "- Scene permission requested at startup; scene support = " + config.sceneSupport + "\n" +
                "- Runs log depth_occlusion (status=active / no_permission / unsupported ...)\n\n" +
                "On each headset allow \"spatial data\" when asked (once). Over Link: Meta Quest Link app > Settings > " +
                "Developer > Spatial Data over Meta Quest Link. Scene saved; rebuild to apply.", "OK");
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
