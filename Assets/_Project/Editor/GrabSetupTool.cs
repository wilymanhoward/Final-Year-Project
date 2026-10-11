using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Turns the selected objects into clues using Meta's own Quick Actions API (the same code as right-click >
    /// Interaction SDK > Add Grab / Poke Interaction). No custom grab code. Each run is idempotent: it only adds
    /// what is missing (interaction, Clue, InteractableUnityEventWrapper wiring, resetter, tag button).
    /// </summary>
    public static class GrabSetupTool
    {
        private const string TestCubeName = "TestCube (delete me)";

        /// <summary>Grabbable clues: grab interaction + Clue + WhenSelect → MarkFound + reset + tag button.</summary>
        [MenuItem("FYP/Make Selected Clues Grabbable")]
        public static void MakeSelectedGrabbable()
        {
            if (!TryGetContext(out var ctx)) return;
            var targets = PickTargets(fallbackToTestCube: true);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Clues", "Select the clue objects in the Hierarchy first.", "OK");
                return;
            }

            bool addedRig = EnsureInteractionRig();
            int wrapped = 0;
            foreach (var target in targets)
            {
                if (target.GetComponentInChildren<Grabbable>(true) == null) QuickActionsAPI.AddGrabInteraction(target);
                var clue = ClueWiring.EnsureClue(target, ctx.Session);
                wrapped += ClueWiring.WireGrabInteractables(clue);
                ClueWiring.EnsureResetter(clue, ctx.CaseRoot, ctx.Session);
                ClueWiring.EnsureTagButton(clue, ctx.Rig.Head);
            }
            Finish(ctx, addedRig,
                $"{targets.Count} grabbable clue(s) set up ({wrapped} interactable(s) wired: WhenSelect → MarkFound).");
        }

        /// <summary>Static clues (e.g. writing on a wall, a flat object facing its local -Z): found by poking.</summary>
        [MenuItem("FYP/Make Selected Static Clues Pokeable")]
        public static void MakeSelectedPokeable()
        {
            if (!TryGetContext(out var ctx)) return;
            var targets = PickTargets(fallbackToTestCube: false);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Clues", "Select the static clue objects (flat, readable side facing local -Z) first.", "OK");
                return;
            }

            bool addedRig = EnsureInteractionRig();
            var failed = new List<string>();
            foreach (var target in targets)
            {
                var clue = ClueWiring.EnsureClue(target, ctx.Session);
                if (!ClueWiring.EnsurePokeSurface(clue)) failed.Add(target.name);
                ClueWiring.EnsureTagButton(clue, ctx.Rig.Head);
            }
            Finish(ctx, addedRig, $"{targets.Count - failed.Count} static clue(s) set up (poke → MarkFound)." +
                (failed.Count > 0 ? "\nNo MeshFilter, skipped: " + string.Join(", ", failed) : ""));
        }

        private struct Context
        {
            public SessionManager Session;
            public PlayerRig Rig;
            public CaseRoot CaseRoot;
        }

        private static bool TryGetContext(out Context ctx)
        {
            ctx = new Context
            {
                Session = Object.FindFirstObjectByType<SessionManager>(),
                Rig = Object.FindFirstObjectByType<PlayerRig>(),
                CaseRoot = Object.FindFirstObjectByType<CaseRoot>(FindObjectsInactive.Include),
            };
            if (ctx.Session != null && ctx.Rig != null && ctx.CaseRoot != null) return true;
            EditorUtility.DisplayDialog("Clues",
                "Open Main.unity and run FYP > Set Up Case Root and Placement first (needs SessionManager, PlayerRig, CaseRoot).", "OK");
            return false;
        }

        private static List<GameObject> PickTargets(bool fallbackToTestCube)
        {
            var list = new List<GameObject>();
            foreach (var go in Selection.gameObjects)
            {
                if (go.scene.IsValid() && go.GetComponentInChildren<Renderer>() != null
                    && go.GetComponentInParent<OVRCameraRig>() == null)
                {
                    list.Add(go);
                }
            }
            if (list.Count == 0 && fallbackToTestCube)
            {
                var cube = GameObject.Find(TestCubeName);
                if (cube != null) list.Add(cube);
            }
            return list;
        }

        private static bool EnsureInteractionRig()
        {
            if (Object.FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0) return false;
            // Adds Meta's interaction rig under OVRCameraRig and disables the OVRHand meshes (their visuals
            // are replaced by the Interaction SDK hands, which wrap around grabbed objects).
            OVRQuickActionsAPI.AddOVRInteractionRig();
            return true;
        }

        private static void Finish(Context ctx, bool addedRig, string message)
        {
            ClueWiring.EnsureHandTrackingMonitor(ctx.Session, ctx.Rig);
            ClueWiring.EnsureHeadsetPresenceMonitor(ctx.Session);
            int ghosted = ApplyGhostToInteractionHands();
            var scene = ctx.Session.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog("Clues", message +
                (addedRig ? "\nAdded the Interaction SDK rig." : "") +
                $"\nGhost material on {ghosted} hand mesh(es). HandTrackingMonitor + HeadsetPresenceMonitor present. Scene saved." +
                "\nCheck each Clue's id matches the CaseDefinition key evidence ids.", "OK");
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
