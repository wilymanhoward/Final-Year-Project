using System.Collections.Generic;
using System.Text;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Phase D: builds the scenes of both cases under CaseRoot from <see cref="CaseSpecs"/> (docs/cases.md), after
    /// running <see cref="CaseSpecValidator"/> (refuses to build if it fails). Per case a "Case_X" / "Case_Y" root with
    /// <see cref="CaseContent"/> (only the active case is shown): furniture, 7 clues (grabbable, or flat pokeable panels
    /// for wall/screen clues; K1 is a keypad on the display case's side glass) with id, name and inspect text, tag buttons, resetters, hover highlight,
    /// 3 exclusion zones with floor decals. Visual pass (session 6): every prop, clue and zone has a "Visual" child with
    /// the low-poly mesh from tools/maya/build_props.py (Art/Props/*.fbx, see <see cref="PropAssets"/>); furniture also
    /// gets a blob shadow and a solid collider surface (session 6, ClueGravity), and each case an invisible floor collider. The functional objects (clue ids, colliders, grab/poke components, tag buttons, zone
    /// triggers) are built exactly as before; the grey primitive's renderer is removed and the Visual added. Also updates the two CaseDefinition assets
    /// (title, briefing, suspects with role and alibi, culprit, key evidence), puts the evidence board at the spec's
    /// spot and deactivates the old test cube and Quad. Idempotent: existing Case_X/Case_Y roots are rebuilt.
    /// Grab/poke wiring reuses the clue tools (QuickActionsAPI.AddGrabInteraction, ClueWiring).
    /// </summary>
    public static class CaseSceneTool
    {
        private static readonly string[] TestContent =
            { "TestCube (delete me)", "Quad", "TagButton - TestCube (delete me)", "TagButton - Quad" };

        // Clue collider boxes (x = width, y = height, z = depth) per case and slot; the Maya meshes fill them exactly
        // (keep in sync with tools/maya/build_props.py). Visual pass: per slot the same size range in both cases, so
        // clue visibility doesn't differ between X and Y (e.g. R2 torn sketch 17 x 13 cm vs. business card 13 x 8 cm).
        internal static readonly Dictionary<string, Vector3> ClueSize = new Dictionary<string, Vector3>
        {
            { "X_K1", new Vector3(0.12f, 0.18f, 1f) }, { "Y_K1", new Vector3(0.20f, 0.20f, 1f) },   // X keypad / Y wall clock (quads)
            { "X_R3", new Vector3(0.10f, 0.10f, 1f) }, { "Y_R3", new Vector3(0.12f, 0.12f, 1f) },
            { "X_K2", new Vector3(0.23f, 0.015f, 0.32f) }, { "Y_K2", new Vector3(0.21f, 0.03f, 0.29f) },
            { "X_K3", new Vector3(0.10f, 0.02f, 0.07f) }, { "Y_K3", new Vector3(0.30f, 0.035f, 0.07f) },
            { "X_K4", new Vector3(0.21f, 0.008f, 0.297f) }, { "Y_K4", new Vector3(0.21f, 0.008f, 0.297f) },
            { "X_R1", new Vector3(0.18f, 0.04f, 0.10f) }, { "Y_R1", new Vector3(0.20f, 0.045f, 0.045f) },
            { "X_R2", new Vector3(0.17f, 0.008f, 0.13f) }, { "Y_R2", new Vector3(0.13f, 0.008f, 0.08f) },
        };

        // Mesh per prop role and case (Art/Props/<name>.fbx). Which way it faces comes from PropSpec.Yaw.
        private static readonly Dictionary<string, string> PropMeshX = new Dictionary<string, string>
            { { "display", "X_Display" }, { "desk", "X_FrontDesk" }, { "office", "X_ManagerDesk" }, { "stand", "X_Monitor" }, { "door", "X_Door" } };
        private static readonly Dictionary<string, string> PropMeshY = new Dictionary<string, string>
            { { "display", "Y_Display" }, { "desk", "Y_ReceptionTable" }, { "office", "Y_TourDesk" }, { "stand", "Y_Noticeboard" }, { "door", "Y_Entrance" } };

        /// <summary>Every FBX the case scenes need (props, clues, zone decals).</summary>
        internal static IEnumerable<string> RequiredMeshes()
        {
            foreach (var n in PropMeshX.Values) yield return n;
            foreach (var n in PropMeshY.Values) yield return n;
            yield return "X_Floor";
            yield return "Y_Floor";
            yield return BodyTapeMesh;
            yield return FallenChairMesh;
            yield return TrophyGhostMesh;
            foreach (var spec in new[] { CaseSpecs.X, CaseSpecs.Y })
            {
                foreach (var c in spec.Clues) yield return c.Id;
                foreach (var z in spec.Zones) yield return z.Id;
            }
        }

        /// <summary>Batch mode: opens Main.unity and runs <see cref="Build"/> (dialogs don't block in batch mode).</summary>
        public static void BuildBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity");
            Build();
        }

        [MenuItem("FYP/Build Case Scenes (X + Y)")]
        public static void Build()
        {
            var errors = CaseSpecValidator.ValidatePair(CaseSpecs.X, CaseSpecs.Y);
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Case scenes", "The case specs fail validation - nothing built:\n" + string.Join("\n", errors), "OK");
                return;
            }
            AssetDatabase.Refresh();
            var missing = PropAssets.Missing(RequiredMeshes());
            if (missing.Count > 0)
            {
                EditorUtility.DisplayDialog("Case scenes", "Prop meshes missing in " + PropImportSettings.Folder + " - run\n" +
                    "mayapy tools/maya/build_props.py\nfirst. Missing: " + string.Join(", ", missing), "OK");
                return;
            }
            var session = Object.FindFirstObjectByType<SessionManager>();
            var caseRoot = Object.FindFirstObjectByType<CaseRoot>(FindObjectsInactive.Include);
            var rig = Object.FindFirstObjectByType<PlayerRig>();
            if (session == null || caseRoot == null || rig == null || rig.Head == null)
            {
                EditorUtility.DisplayDialog("Case scenes", "Open Main.unity and run FYP > Set Up Case Root and Placement first.", "OK");
                return;
            }

            var report = new StringBuilder();
            foreach (var spec in new[] { CaseSpecs.X, CaseSpecs.Y })
            {
                Transform built = BuildCase(spec, session, caseRoot, rig.Head);
                report.Append(UpdateDefinition(spec)).Append('\n');
                report.Append($"Case {spec.Case}: {PropAssets.Triangles(built)} triangles; prop heights: {PropHeights(built)}\n");
            }
            int hidden = HideTestContent(caseRoot.transform);
            PlaceBoard();

            var scene = session.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Case scenes",
                "Built Case_X and Case_Y under CaseRoot (validator passed; mean clue distance " +
                CaseSpecValidator.MeanDistance(CaseSpecs.X).ToString("F2") + " m).\n" + report +
                (hidden > 0 ? $"Deactivated {hidden} old test object(s) (TestCube, Quad).\n" : "") +
                "\nNext, in this order:\n1. FYP > Set Up Networking (networks the new grabbable clues)\n" +
                "2. FYP > Set Up Evidence Board (4 suspect cards)\n3. FYP > Set Up Headset UI (bigger briefing card)\n4. Build.",
                "OK");
        }

        /// <summary>Rotation whose readable side (local -Z, like a Quad / a world canvas seen from the front) faces <paramref name="yaw"/>.</summary>
        public static Quaternion FacingRotation(float yaw)
        {
            Vector3 face = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return Quaternion.LookRotation(-face, Vector3.up);
        }

        private static Transform BuildCase(CaseSpec spec, SessionManager session, CaseRoot caseRoot, Transform head)
        {
            string rootName = "Case_" + spec.Case;
            var old = caseRoot.transform.Find(rootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "Build case");
            root.transform.SetParent(caseRoot.transform, false);
            var content = root.AddComponent<CaseContent>();
            var so = new SerializedObject(content);
            so.FindProperty("caseId").enumValueIndex = (int)spec.Case;
            so.FindProperty("session").objectReferenceValue = session;
            so.ApplyModifiedPropertiesWithoutUndo();

            BuildFloor(root.transform, spec);
            foreach (var p in spec.Props) BuildProp(root.transform, spec, p);
            foreach (var z in spec.Zones) BuildZone(root.transform, z, session);
            if (spec.Case == CaseId.Y) BuildMurderScene(root.transform);
            foreach (var c in spec.Clues) BuildClue(root.transform, spec, c, session, caseRoot, head);
            return root.transform;
        }

        /// <summary>Furniture: an empty root on the floor (front turned to its role's facing) with Visual + blob shadow.</summary>
        private static void BuildProp(Transform parent, CaseSpec spec, PropSpec p)
        {
            string mesh = (spec.Case == CaseId.X ? PropMeshX : PropMeshY)[p.Role];
            float yaw = p.Yaw;
            var go = new GameObject(p.Name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(p.X, 0f, p.Z);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var visual = PropAssets.AddVisual(go.transform, mesh, Vector3.zero, Quaternion.identity, Vector3.one);
            Bounds bounds = PropAssets.Mesh(mesh, "_Body").bounds;
            bool hanging = bounds.min.y > HangingAbove;   // wall-hung monitor / noticeboard: no floor shadow
            if (!hanging) PropAssets.AddBlobShadow(go.transform, bounds);
            visual.name = "Visual";
            if (spec.Case == CaseId.X && p.Role == "display") AddTrophyGhost(go.transform);
            if (p.Role != "door") AddSurface(go.transform, p, Mathf.Max(0f, bounds.min.y));   // a dropped clue can walk through the doorway
        }

        /// <summary>A prop whose mesh starts this high above the floor hangs (no legs): no blob shadow, no collider below it.</summary>
        private const float HangingAbove = 0.3f;

        /// <summary>
        /// The room floor (user, session 6): a semi-transparent 5 x 5 m floor (Case X concrete tiles, Case Y floorboards;
        /// the Case Y varnish zone decal is cut from the same boards) just under y = 0, plus the physics floor for dropped
        /// clues (ClueGravity): a box whose top is the CaseRoot floor (y = 0), 0.5 m thick so nothing tunnels through, a
        /// bit larger than the play area.
        /// </summary>
        private static void BuildFloor(Transform parent, CaseSpec spec)
        {
            var go = new GameObject("Floor");
            go.transform.SetParent(parent, false);
            PropAssets.AddVisual(go.transform, spec.Case == CaseId.X ? "X_Floor" : "Y_Floor", Vector3.zero, Quaternion.identity,
                Vector3.one, PropAssets.Floor);
            float size = PlayArea.HalfSize * 2f + 1f;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.25f, 0f);
            box.size = new Vector3(size, 0.5f, size);
        }

        /// <summary>
        /// Solid box over the furniture's footprint up to its spec height (desk tops at 0.75 m), so a dropped clue
        /// lands on top and can't end up underneath; for a hanging prop it starts at the mesh's lowest point. Axis-aligned like the spec footprint (counter-rotated).
        /// </summary>
        private static void AddSurface(Transform prop, PropSpec p, float bottom)
        {
            var go = new GameObject("Surface (clue physics)");
            go.transform.SetParent(prop, false);
            go.transform.localRotation = Quaternion.Inverse(prop.localRotation);
            var box = go.AddComponent<BoxCollider>();
            float top = Mathf.Max(p.Height, bottom + 0.05f);
            box.center = new Vector3(0f, (bottom + top) / 2f, 0f);   // from the mesh's lowest point (hanging props)
            box.size = new Vector3(p.Width, top - bottom, p.Depth);
        }

        private static string PropHeights(Transform caseRoot)
        {
            var parts = new List<string>();
            foreach (Transform t in caseRoot)
            {
                var v = t.Find("Visual");
                if (v == null || t.GetComponent<Clue>() != null || t.GetComponent<ExclusionZone>() != null || t.name == "Floor") continue;
                var f = v.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) parts.Add($"{t.name} {f.sharedMesh.bounds.max.y:F2} m");
            }
            return string.Join(", ", parts);
        }

        // Case Y murder scene (user, session 6): the taped body outline lies in front of the plinth with its head in the blood
        // pool (zone 1), plus a fallen chair from the struggle. Decoration only (no zone, no collider).
        private const string BodyTapeMesh = "Y_BodyTape";
        private const string FallenChairMesh = "Y_FallenChair";
        private const string TrophyGhostMesh = "X_TrophyGhost";

        // Case X: the missing award's silhouette on the empty stand; it only shows (blinking) while the briefing detective
        // points at the display case (TrophyGhost, found by DetectiveBriefing).
        private static void AddTrophyGhost(Transform display)
        {
            var go = new GameObject("Trophy ghost");
            go.transform.SetParent(display, false);
            PropAssets.AddVisual(go.transform, TrophyGhostMesh, Vector3.zero, Quaternion.identity, Vector3.one, PropAssets.TrophyGhost);
            go.AddComponent<TrophyGhost>();
        }

        private static void BuildMurderScene(Transform parent)
        {
            AddDecor(parent, "Body tape outline", BodyTapeMesh, new Vector3(CaseSpecs.YBodyX, 0f, CaseSpecs.YBodyZ), 0f);
            AddDecor(parent, "Fallen chair", FallenChairMesh, new Vector3(-0.35f, 0f, 2.10f), 25f);
        }

        private static void AddDecor(Transform parent, string name, string mesh, Vector3 at, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            PropAssets.AddVisual(go.transform, mesh, Vector3.zero, Quaternion.identity, Vector3.one);
        }

        private static void BuildZone(Transform parent, ZoneSpec z, SessionManager session)
        {
            var go = new GameObject("Zone - " + z.Name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(z.X, 0f, z.Z);
            go.transform.localRotation = Quaternion.Euler(0f, z.Yaw, 0f);
            var zone = go.AddComponent<ExclusionZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("zoneId").stringValue = z.Id;
            so.FindProperty("size").vector2Value = new Vector2(z.Width, z.Depth);
            so.FindProperty("session").objectReferenceValue = session;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Visible floor decal (the zone itself is invisible): glass shards, spill, footprints... with a subtle outline.
            PropAssets.AddVisual(go.transform, z.Id, Vector3.zero, Quaternion.identity, Vector3.one);
        }

        private static void BuildClue(Transform parent, CaseSpec spec, ClueSpec c, SessionManager session, CaseRoot caseRoot, Transform head)
        {
            Vector3 size = ClueSize.TryGetValue(c.Id, out var s) ? s : new Vector3(0.1f, 0.05f, 0.1f);
            GameObject go;
            if (c.Grabbable)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(parent, false);
                go.transform.localScale = size;
                go.transform.localPosition = new Vector3(c.X, PlayArea.Height(c.Height) + size.y / 2f, c.Z);
                go.transform.localRotation = Quaternion.Euler(0f, c.FacingYaw, 0f);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);   // readable side = local -Z
                go.transform.SetParent(parent, false);
                go.transform.localScale = new Vector3(size.x, size.y, 1f);
                go.transform.localPosition = new Vector3(c.X, PlayArea.Height(c.Height), c.Z);
                go.transform.localRotation = FacingRotation(c.FacingYaw);
            }
            go.name = c.Id + " " + c.ObjectName;

            var clue = ClueWiring.EnsureClue(go, session);
            var so = new SerializedObject(clue);
            so.FindProperty("clueId").stringValue = c.Id;
            so.FindProperty("displayName").stringValue = c.ObjectName;
            so.FindProperty("inspectText").stringValue = c.InspectText;
            so.FindProperty("isKeyEvidence").boolValue = PlayArea.IsKey(c.Slot);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (c.Grabbable)
            {
                QuickActionsAPI.AddGrabInteraction(go);
                ClueWiring.WireGrabInteractables(clue);
                ClueWiring.EnsureResetter(clue, caseRoot, session);
                // Grab interactables for CaseContent hiding / ForceRelease (Set Up Networking fills the same list).
                var grabs = new List<Behaviour>();
                grabs.AddRange(go.GetComponentsInChildren<HandGrabInteractable>(true));
                grabs.AddRange(go.GetComponentsInChildren<Oculus.Interaction.GrabInteractable>(true));
                var cso = new SerializedObject(clue);
                var arr = cso.FindProperty("grabInteractables");
                arr.arraySize = grabs.Count;
                for (int i = 0; i < grabs.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = grabs[i];
                cso.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                ClueWiring.EnsurePokeSurface(clue);
            }

            // Visual: the clue mesh replaces the grey primitive's look. The root keeps its collider, scale and
            // components; the Visual is counter-scaled so the mesh shows at its real size (it fills the collider box).
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            if (c.Grabbable) Object.DestroyImmediate(go.GetComponent<MeshFilter>());   // static: MeshFilter feeds the MeshCollider
            var inverse = new Vector3(1f / size.x, 1f / size.y, c.Grabbable ? 1f / size.z : 1f);
            var visual = PropAssets.AddVisual(go.transform, c.Id,
                c.Grabbable ? new Vector3(0f, -0.5f, 0f) : Vector3.zero, Quaternion.identity, inverse);
            var shell = PropAssets.AddHighlightShell(visual, c.Id);
            var highlight = go.AddComponent<ClueHighlight>();
            MainSceneBuilder.SetObject(highlight, "shell", shell);
            ClueWiring.WireHover(clue, highlight);

            ClueWiring.EnsureTagButton(clue, head);
        }

        private static string UpdateDefinition(CaseSpec spec)
        {
            string path = $"Assets/_Project/Cases/Case{spec.Case}/Case{spec.Case}_Definition.asset";
            var def = AssetDatabase.LoadAssetAtPath<CaseDefinition>(path);
            if (def == null) return "WARNING: " + path + " not found - case definition not updated.";

            var so = new SerializedObject(def);
            so.FindProperty("caseId").enumValueIndex = (int)spec.Case;
            so.FindProperty("title").stringValue = spec.Title;
            so.FindProperty("briefing").stringValue = spec.Briefing;
            var suspects = so.FindProperty("suspects");
            suspects.arraySize = spec.Suspects.Count;
            for (int i = 0; i < spec.Suspects.Count; i++)
            {
                var e = suspects.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("id").stringValue = spec.Suspects[i].Id;
                e.FindPropertyRelative("displayName").stringValue = spec.Suspects[i].Name;
                e.FindPropertyRelative("role").stringValue = spec.Suspects[i].Role;
                e.FindPropertyRelative("alibi").stringValue = spec.Suspects[i].Alibi;
            }
            so.FindProperty("correctSuspectId").stringValue = spec.Culprit.Id;
            var keys = so.FindProperty("keyEvidenceIds");
            var keyIds = spec.Clues.FindAll(c => PlayArea.IsKey(c.Slot));
            keys.arraySize = keyIds.Count;
            for (int i = 0; i < keyIds.Count; i++) keys.GetArrayElementAtIndex(i).stringValue = keyIds[i].Id;

            var notes = new StringBuilder("Generated from docs/cases.md by FYP > Build Case Scenes.\nCulprit: ")
                .Append(spec.Culprit.Name).Append('\n');
            foreach (var c in spec.Clues)
                notes.Append(c.Slot).Append(' ').Append(c.Id).Append(" - ").Append(c.ObjectName)
                     .Append(c.PointsAtSuspectId != null ? " (red herring: " + spec.Suspects.Find(s => s.Id == c.PointsAtSuspectId).Name + ")" : "")
                     .Append('\n');
            so.FindProperty("experimenterNotes").stringValue = notes.ToString();
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
            return $"Updated {path} ({spec.Title}).";
        }

        private static int HideTestContent(Transform caseRoot)
        {
            int n = 0;
            foreach (string name in TestContent)
            {
                var t = caseRoot.Find(name);
                if (t == null || !t.gameObject.activeSelf) continue;
                Undo.RecordObject(t.gameObject, "Hide test content");
                t.gameObject.SetActive(false);
                n++;
            }
            return n;
        }

        private static void PlaceBoard()
        {
            var board = Object.FindFirstObjectByType<EvidenceBoardView>(FindObjectsInactive.Include);
            if (board == null) return;   // Set Up Evidence Board places a new one at the default (= spec) spot
            Undo.RecordObject(board.transform, "Place board");
            board.transform.localPosition = EvidenceBoardTool.DefaultLocalPosition;
            board.transform.localRotation = EvidenceBoardTool.DefaultLocalRotation;
        }
    }
}
