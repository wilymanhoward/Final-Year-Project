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
    /// Phase D: builds the greybox scenes of both cases under CaseRoot from <see cref="CaseSpecs"/> (docs/cases.md),
    /// after running <see cref="CaseSpecValidator"/> (refuses to build if it fails). Per case a "Case_X" / "Case_Y"
    /// root with <see cref="CaseContent"/> (only the active case is shown): grey furniture boxes with name labels,
    /// 7 clues (grabbable boxes, or flat pokeable panels for wall/screen clues) with id, name and inspect text, tag
    /// buttons, resetters, 3 exclusion zones with coloured floor markers. Also updates the two CaseDefinition assets
    /// (title, briefing, suspects with role and alibi, culprit, key evidence), puts the evidence board at the spec's
    /// spot and deactivates the old test cube and Quad. Idempotent: existing Case_X/Case_Y roots are rebuilt.
    /// Grab/poke wiring reuses the clue tools (QuickActionsAPI.AddGrabInteraction, ClueWiring).
    /// </summary>
    public static class CaseSceneTool
    {
        private const string MaterialFolder = "Assets/_Project/Art/Materials/Greybox";
        private static readonly string[] TestContent =
            { "TestCube (delete me)", "Quad", "TagButton - TestCube (delete me)", "TagButton - Quad" };

        // Greybox sizes (x = width, y = height, z = depth) per case and slot; flat papers lie flat.
        private static readonly Dictionary<string, Vector3> ClueSize = new Dictionary<string, Vector3>
        {
            { "X_K1", new Vector3(0.20f, 0.12f, 1f) }, { "Y_K1", new Vector3(0.20f, 0.12f, 1f) },   // panels (quads)
            { "X_R3", new Vector3(0.10f, 0.10f, 1f) }, { "Y_R3", new Vector3(0.15f, 0.15f, 1f) },
            { "X_K2", new Vector3(0.21f, 0.008f, 0.297f) }, { "Y_K2", new Vector3(0.20f, 0.03f, 0.28f) },
            { "X_K3", new Vector3(0.06f, 0.02f, 0.04f) }, { "Y_K3", new Vector3(0.13f, 0.02f, 0.02f) },
            { "X_K4", new Vector3(0.21f, 0.008f, 0.297f) }, { "Y_K4", new Vector3(0.21f, 0.008f, 0.297f) },
            { "X_R1", new Vector3(0.18f, 0.05f, 0.09f) }, { "Y_R1", new Vector3(0.20f, 0.04f, 0.04f) },
            { "X_R2", new Vector3(0.21f, 0.008f, 0.297f) }, { "Y_R2", new Vector3(0.09f, 0.008f, 0.055f) },
        };

        [MenuItem("FYP/Build Case Scenes (X + Y)")]
        public static void Build()
        {
            var errors = CaseSpecValidator.ValidatePair(CaseSpecs.X, CaseSpecs.Y);
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Case scenes", "The case specs fail validation - nothing built:\n" + string.Join("\n", errors), "OK");
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
                BuildCase(spec, session, caseRoot, rig.Head);
                report.Append(UpdateDefinition(spec)).Append('\n');
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

        private static void BuildCase(CaseSpec spec, SessionManager session, CaseRoot caseRoot, Transform head)
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

            foreach (var p in spec.Props) BuildProp(root.transform, p);
            foreach (var z in spec.Zones) BuildZone(root.transform, z, session);
            foreach (var c in spec.Clues) BuildClue(root.transform, spec, c, session, caseRoot, head);
        }

        private static void BuildProp(Transform parent, PropSpec p)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = p.Name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(p.X, p.Height / 2f, p.Z);
            go.transform.localScale = new Vector3(p.Width, p.Height, p.Depth);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Mat(p.Role == "door" ? "Door" : "Furniture",
                p.Role == "door" ? new Color(0.45f, 0.30f, 0.18f) : new Color(0.62f, 0.62f, 0.64f));

            // Name label above the prop, facing the start mark.
            var label = new GameObject(p.Name + " label");
            label.transform.SetParent(parent, false);
            Vector3 top = new Vector3(p.X, p.Height + 0.12f, p.Z);
            label.transform.localPosition = top;
            Vector3 away = top - new Vector3(0f, top.y, 0f);
            label.transform.localRotation = Quaternion.LookRotation(away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward);
            GameObject canvas = HeadsetUiTool.Canvas(label.transform, 260f, 46f, new Color(0f, 0f, 0f, 0.6f));
            var text = HeadsetUiTool.Label(canvas.transform, "Text", new Rect(4f, 2f, 252f, 42f), 22, TextAnchor.MiddleCenter);
            text.text = p.Name;
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

            // Visible floor marker (the zone itself is invisible; the marker is the "broken glass" etc.).
            var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = "Marker";
            marker.transform.SetParent(go.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.004f, 0f);
            marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            marker.transform.localScale = new Vector3(z.Width, z.Depth, 1f);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = Mat("Zone_" + z.Id, ZoneColor(z.Id));
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
            // Same colour for key evidence and red herrings, so the look doesn't give the answer away.
            go.GetComponent<Renderer>().sharedMaterial = Mat("Clue", new Color(0.95f, 0.85f, 0.35f));

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

        private static Color ZoneColor(string id)
        {
            if (id.EndsWith("Z1")) return new Color(0.70f, 0.90f, 0.95f);   // glass
            if (id.EndsWith("Z2")) return id.StartsWith("X") ? new Color(0.35f, 0.22f, 0.12f) : new Color(0.30f, 0.55f, 0.85f);   // coffee / water
            return id.StartsWith("X") ? new Color(0.30f, 0.22f, 0.15f) : new Color(0.85f, 0.60f, 0.20f);   // mud / varnish
        }

        /// <summary>Gets or creates a URP Lit material asset in Art/Materials/Greybox.</summary>
        private static Material Mat(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "Greybox");
            string path = MaterialFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(shader != null ? shader : Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
