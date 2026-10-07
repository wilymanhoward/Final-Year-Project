using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Makes an object grabbable with the Meta Interaction SDK, using Meta's own Quick Actions API
    /// (the same code as right-click > Interaction SDK > Add Grab Interaction). No custom grab code.
    /// </summary>
    public static class GrabSetupTool
    {
        private const string TestCubeName = "TestCube (delete me)";

        /// <summary>
        /// Adds the Interaction SDK rig if missing, then a hand/controller grab interaction to the selected
        /// object (or the test cube when nothing suitable is selected), and saves the scene.
        /// </summary>
        [MenuItem("FYP/Make Selected Object Grabbable")]
        public static void MakeSelectedGrabbable()
        {
            var target = PickTarget();
            if (target == null)
            {
                EditorUtility.DisplayDialog("Grab",
                    "Select the 3D model in the Hierarchy first (or keep the test cube in the scene).", "OK");
                return;
            }
            if (target.GetComponentInChildren<Grabbable>(true) != null)
            {
                EditorUtility.DisplayDialog("Grab", $"'{target.name}' is already grabbable.", "OK");
                return;
            }

            bool addedRig = false;
            if (Object.FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            {
                // Adds Meta's interaction rig under OVRCameraRig and disables the OVRHand meshes (their visuals
                // are replaced by the Interaction SDK hands, which wrap around grabbed objects).
                OVRQuickActionsAPI.AddOVRInteractionRig();
                addedRig = true;
            }

            QuickActionsAPI.AddGrabInteraction(target);
            int ghosted = ApplyGhostToInteractionHands();

            EditorSceneManager.MarkSceneDirty(target.scene);
            EditorSceneManager.SaveScene(target.scene);
            EditorUtility.DisplayDialog("Grab",
                $"'{target.name}' is now grabbable (hand pinch/palm grab or controller grip)." +
                (addedRig ? "\nAdded the Interaction SDK rig." : "") +
                $"\nGhost material applied to {ghosted} hand mesh(es). Scene saved.", "OK");
        }

        private static GameObject PickTarget()
        {
            var selected = Selection.activeGameObject;
            if (selected != null && selected.scene.IsValid() && selected.GetComponentInChildren<Renderer>() != null
                && selected.GetComponentInParent<OVRCameraRig>() == null)
            {
                return selected;
            }
            return GameObject.Find(TestCubeName);
        }

        /// <summary>Puts the ghost material on every Interaction SDK hand mesh (they replace the OVRHand meshes).</summary>
        private static int ApplyGhostToInteractionHands()
        {
            var material = MainSceneBuilder.GetOrCreateGhostHandMaterial();
            if (material == null) return 0;

            int count = 0;
            foreach (var visual in Object.FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (var skinned in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var materials = skinned.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    skinned.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(skinned);
                    count++;
                }
            }
            return count;
        }
    }
}
