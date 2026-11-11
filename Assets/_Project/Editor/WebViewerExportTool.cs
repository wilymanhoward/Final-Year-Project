using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// FYP > Export Web Viewer Scenes: writes what a participant sees of each case (every prop / clue / floor / zone / board
    /// mesh under CaseRoot, with its prop material name) to tools/viewer/scenes/case_X|Y.obj, the zone footprints and clue
    /// spots to case_X|Y.json, and the palette textures, for the replay viewer tools/viewer/index.html. Coordinates are
    /// Unity CaseRoot-local, unconverted (left-handed, same frame as the CSV logs); the viewer mirrors X once for all.
    /// Run after Build Case Scenes / Set Up Evidence Board. Batch: -executeMethod FYP.Detective.EditorTools.WebViewerExportTool.ExportBatch
    /// </summary>
    internal static class WebViewerExportTool
    {
        private const string OutDir = "tools/viewer/scenes";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Hover shells and the briefing's trophy silhouette are never part of the static scene.
        private static readonly HashSet<string> SkipMaterials = new HashSet<string> { "Props_ClueHighlight", "Props_TrophyGhost" };

        public static void ExportBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity");
            Debug.Log("[Web viewer export] " + Export());
        }

        [MenuItem("FYP/Export Web Viewer Scenes")]
        public static void ExportMenu() => SetupDialogs.Show("Web viewer export", Export(), "OK");

        private static string Export()
        {
            var caseRoot = Object.FindFirstObjectByType<CaseRoot>(FindObjectsInactive.Include);
            if (caseRoot == null) return "No CaseRoot in the open scene - open Main.unity.";
            Directory.CreateDirectory(OutDir);
            var report = new StringBuilder();
            Matrix4x4 toLocal = caseRoot.transform.worldToLocalMatrix;
            Transform board = caseRoot.transform.Find("EvidenceBoard");
            foreach (string c in new[] { "X", "Y" })
            {
                Transform root = caseRoot.transform.Find("Case_" + c);
                if (root == null) { report.Append($"Case_{c} missing - run FYP > Build Case Scenes first.\n"); continue; }
                int tris = WriteObj(Path.Combine(OutDir, $"case_{c}.obj"), toLocal, root, board);
                WriteJson(Path.Combine(OutDir, $"case_{c}.json"), c, caseRoot.transform, root);
                report.Append($"Case {c}: {tris} triangles.\n");
            }
            foreach (string tex in new[] { "Palette.png", "BlobShadow.png" })
                File.Copy(PropImportSettings.Folder + tex, Path.Combine(OutDir, tex), true);
            return report + "Written to " + OutDir + ". View: python -m http.server 8000 -d tools/viewer";
        }

        private static int WriteObj(string path, Matrix4x4 toLocal, params Transform[] roots)
        {
            var sb = new StringBuilder("# FYP case scene, Unity CaseRoot-local (left-handed)\n");
            int vBase = 1, tris = 0;
            foreach (Transform root in roots)
            {
                if (root == null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    Mesh mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    Material[] mats = mr.sharedMaterials;
                    Matrix4x4 m = toLocal * mr.localToWorldMatrix;
                    Vector3[] v = mesh.vertices, n = mesh.normals;
                    Vector2[] uv = mesh.uv;
                    bool any = false;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                        if (s < mats.Length && Usable(mats[s])) any = true;
                    if (!any) continue;

                    sb.Append("o ").Append(PathOf(mr.transform, root)).Append('\n');
                    for (int i = 0; i < v.Length; i++) Vec(sb, "v ", m.MultiplyPoint3x4(v[i]));
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector2 t = i < uv.Length ? uv[i] : Vector2.zero;
                        sb.Append("vt ").Append(t.x.ToString("0.#####", Inv)).Append(' ').Append(t.y.ToString("0.#####", Inv)).Append('\n');
                    }
                    for (int i = 0; i < v.Length; i++) Vec(sb, "vn ", i < n.Length ? m.MultiplyVector(n[i]).normalized : Vector3.up);
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        if (s >= mats.Length || !Usable(mats[s])) continue;
                        sb.Append("usemtl ").Append(mats[s].name).Append('\n');
                        int[] tri = mesh.GetTriangles(s);
                        for (int i = 0; i < tri.Length; i += 3)
                        {
                            sb.Append('f');
                            for (int k = 0; k < 3; k++)
                            {
                                int id = tri[i + k] + vBase;
                                sb.Append(' ').Append(id).Append('/').Append(id).Append('/').Append(id);
                            }
                            sb.Append('\n');
                        }
                        tris += tri.Length / 3;
                    }
                    vBase += v.Length;
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return tris;
        }

        // Only the prop materials (PropAssets) are what the participant sees; debug visuals use other materials.
        private static bool Usable(Material mat) =>
            mat != null && mat.name.StartsWith("Props_") && !SkipMaterials.Contains(mat.name);

        private static void WriteJson(string path, string caseCode, Transform caseRoot, Transform root)
        {
            CaseSpec spec = CaseSpecs.For(caseCode == "X" ? CaseId.X : CaseId.Y);   // names for the viewer's event texts
            var sb = new StringBuilder("{\n  \"case\": \"").Append(caseCode).Append("\",\n  \"zones\": [");
            bool first = true;
            foreach (var z in root.GetComponentsInChildren<ExclusionZone>(true))
            {
                Vector3 p = caseRoot.InverseTransformPoint(z.transform.position);
                float yaw = (Quaternion.Inverse(caseRoot.rotation) * z.transform.rotation).eulerAngles.y;
                ZoneSpec zs = spec.Zones.Find(x => x.Id == z.ZoneId);
                sb.Append(first ? "\n" : ",\n").Append("    {\"id\": \"").Append(z.ZoneId).Append("\", \"x\": ").Append(F(p.x))
                  .Append(", \"z\": ").Append(F(p.z)).Append(", \"yaw\": ").Append(F(yaw))
                  .Append(", \"sx\": ").Append(F(z.Size.x)).Append(", \"sz\": ").Append(F(z.Size.y))
                  .Append(", \"name\": ").Append(Str(zs != null ? zs.Name : null)).Append('}');
                first = false;
            }
            sb.Append("\n  ],\n  \"clues\": [");
            first = true;
            foreach (var c in root.GetComponentsInChildren<Clue>(true))
            {
                Vector3 p = caseRoot.InverseTransformPoint(c.transform.position);
                ClueSpec cs = spec.Clues.Find(x => x.Id == c.ClueId);
                sb.Append(first ? "\n" : ",\n").Append("    {\"id\": \"").Append(c.ClueId).Append("\", \"x\": ").Append(F(p.x))
                  .Append(", \"y\": ").Append(F(p.y)).Append(", \"z\": ").Append(F(p.z));
                if (cs != null)
                    sb.Append(", \"name\": ").Append(Str(cs.ObjectName)).Append(", \"key\": ").Append(PlayArea.IsKey(cs.Slot) ? "true" : "false")
                      .Append(", \"pointsAt\": ").Append(Str(cs.PointsAtSuspectId));
                sb.Append('}');
                first = false;
            }
            sb.Append("\n  ],\n  \"suspects\": [");
            first = true;
            foreach (SuspectSpec su in spec.Suspects)
            {
                sb.Append(first ? "\n" : ",\n").Append("    {\"id\": ").Append(Str(su.Id)).Append(", \"name\": ").Append(Str(su.Name))
                  .Append(", \"role\": ").Append(Str(su.Role)).Append(", \"culprit\": ").Append(su.IsCulprit ? "true" : "false").Append('}');
                first = false;
            }
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string F(float f) => f.ToString("0.####", Inv);

        private static string Str(string v) => v == null ? "null" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static void Vec(StringBuilder sb, string tag, Vector3 p) =>
            sb.Append(tag).Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z)).Append('\n');

        private static string PathOf(Transform t, Transform root)
        {
            string p = t.name;
            for (Transform x = t.parent; x != null && x != root.parent; x = x.parent) p = x.name + "/" + p;
            return p.Replace(' ', '_');
        }
    }
}
