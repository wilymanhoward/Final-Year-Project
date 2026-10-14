using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Lets the passthrough app suppress the Quest boundary (Guardian), so walking outside a small stationary
    /// boundary no longer hides the scene. Idempotent: sets the two required settings and saves.
    /// </summary>
    /// <remarks>
    /// Verified in com.meta.xr.sdk.core 207: <c>OVRProjectConfig.boundaryVisibilitySupport</c>
    /// (Editor/OVRProjectConfig.cs:171; adds the BOUNDARY_VISIBILITY permission in
    /// Editor/OVRManifestPreprocessor.cs:1029) and <c>OVRManager.shouldBoundaryVisibilityBeSuppressed</c>
    /// (Scripts/OVRManager.cs:1229; only honoured while passthrough is enabled, UpdateBoundary at :3779).
    /// The system may still refuse; OVRManager then logs "Cannot suppress boundary visibility".
    /// </remarks>
    public static class BoundarySetupTool
    {
        [MenuItem("FYP/Suppress Boundary (Passthrough)")]
        public static void SetUp()
        {
            var manager = Object.FindFirstObjectByType<OVRManager>();
            if (manager == null)
            {
                EditorUtility.DisplayDialog("Boundary", "Open Main.unity first (it needs an OVRManager).", "OK");
                return;
            }

            var config = OVRProjectConfig.CachedProjectConfig;
            if (config.boundaryVisibilitySupport == OVRProjectConfig.FeatureSupport.None)
            {
                config.boundaryVisibilitySupport = OVRProjectConfig.FeatureSupport.Supported;
                OVRProjectConfig.CommitProjectConfig(config);
            }

            Undo.RecordObject(manager, "Suppress boundary");
            manager.shouldBoundaryVisibilityBeSuppressed = true;
            EditorUtility.SetDirty(manager);
            var scene = manager.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorUtility.DisplayDialog("Boundary",
                "Boundary Visibility support = Supported (project config).\n" +
                "OVRManager: Should Boundary Visibility Be Suppressed = on" +
                (manager.isInsightPassthroughEnabled ? "." : " (WARNING: passthrough is off, so it has no effect).") +
                "\nScene saved. Rebuild to apply.", "OK");
        }
    }
}
