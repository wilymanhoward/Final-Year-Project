using System.Collections.Generic;
using Oculus.Interaction.Locomotion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Switches off the Interaction SDK locomotion that came with the comprehensive rig. In passthrough MR the
    /// rig must never move: locomotion shifts TrackingSpace, so the scene no longer matches the real floor.
    /// Idempotent: deactivates the objects (nothing is deleted) and saves the scene.
    /// </summary>
    /// <remarks>
    /// Verified in com.meta.xr.sdk.interaction 207: <c>FirstPersonLocomotor</c>
    /// (Runtime/Scripts/Interaction/Models/Locomotion/FirstPersonLocomotor.cs) moves the player origin, incl.
    /// <c>_crouchHeightOffset = -0.5</c>; Prefabs/Locomotion/ControllerLocomotionSlideActions.prefab toggles crouch
    /// on <c>ControllerButtonUsage.Primary2DAxisClick</c> (thumbstick click), which our start/abort shortcuts use.
    /// The interactor groups come from Runtime/Prefabs/BaseInteractors.prefab (teleport/turn by hand and controller).
    /// </remarks>
    public static class LocomotionDisableTool
    {
        private static readonly string[] GroupNames = { "LocomotionControllerInteractorGroup", "LocomotionHandInteractorGroup" };

        [MenuItem("FYP/Disable Locomotion")]
        public static void Disable()
        {
            var targets = new List<GameObject>();
            foreach (var locomotor in Object.FindObjectsByType<FirstPersonLocomotor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                targets.Add(locomotor.gameObject);
            }
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (System.Array.IndexOf(GroupNames, t.name) >= 0) targets.Add(t.gameObject);
            }

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Locomotion", "No locomotion objects found in the open scene.", "OK");
                return;
            }

            var report = new System.Text.StringBuilder();
            foreach (var go in targets)
            {
                report.Append(go.activeSelf ? "Deactivated: " : "Already off: ").Append(go.name).Append('\n');
                if (!go.activeSelf) continue;
                Undo.RecordObject(go, "Disable locomotion");
                go.SetActive(false);
                EditorUtility.SetDirty(go);
            }

            var scene = targets[0].scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog("Locomotion", report + "Scene saved. Rebuild to apply.", "OK");
        }
    }
}
