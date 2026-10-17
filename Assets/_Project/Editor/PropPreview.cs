using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Renders a preview sheet of the Art/Props meshes (per case: furniture in a row, clues and zone decals in front)
    /// to Logs/PropPreview_X.png and _Y.png, in a temporary scene with the same materials as the game. For checking the
    /// look without a headset. Menu: FYP > Render Prop Preview; batch: -executeMethod ...PropPreview.RenderBatch.
    /// </summary>
    public static class PropPreview
    {
        private static readonly string[] PropsX = { "X_Display", "X_FrontDesk", "X_ManagerDesk", "X_Monitor", "X_Door" };
        private static readonly string[] PropsY = { "Y_Display", "Y_ReceptionTable", "Y_TourDesk", "Y_Noticeboard", "Y_Entrance" };

        [MenuItem("FYP/Render Prop Preview")]
        public static void RenderMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string scenePath = EditorSceneManager.GetActiveScene().path;
            var files = RenderAll();
            if (!string.IsNullOrEmpty(scenePath)) EditorSceneManager.OpenScene(scenePath);
            EditorUtility.DisplayDialog("Prop preview", "Rendered:\n" + string.Join("\n", files), "OK");
        }

        public static void RenderBatch() => RenderAll();

        private static List<string> RenderAll()
        {
            var files = new List<string>();
            foreach (char c in new[] { 'X', 'Y' })
                files.Add(RenderCase(c, c == 'X' ? PropsX : PropsY));
            return files;
        }

        private static string RenderCase(char caseId, string[] props)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.6f);
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);   // stands in for the real floor in passthrough
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(14f, 8f, 1f);
            floor.transform.position = new Vector3(2.4f, 0f, 0.6f);
            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            floorMat.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.57f));
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            float x = 0f;
            foreach (string name in props)
            {
                var root = new GameObject(name);
                root.transform.position = new Vector3(x, 0f, 0f);
                root.transform.rotation = Quaternion.Euler(0f, 200f, 0f);   // front (+Z) turned towards the camera
                PropAssets.AddVisual(root.transform, name, Vector3.zero, Quaternion.identity, Vector3.one);
                var body = PropAssets.Mesh(name, "_Body");
                if (body != null) PropAssets.AddBlobShadow(root.transform, body.bounds);
                x += (body != null ? Mathf.Max(body.bounds.size.x, 0.4f) : 0.6f) + 0.45f;
            }

            // Clues on a "table" row, zone decals on the floor in front.
            float cx = 0.1f;
            foreach (string slot in new[] { "K1", "K2", "K3", "K4", "R1", "R2", "R3" })
            {
                string name = caseId + "_" + slot;
                var root = new GameObject(name);
                bool panel = slot == "K1" || slot == "R3";
                root.transform.position = new Vector3(cx, panel ? 0.12f : 0.02f, -1.1f);
                root.transform.rotation = panel ? Quaternion.Euler(0f, 20f, 0f) : Quaternion.Euler(0f, 200f, 0f);
                var visual = PropAssets.AddVisual(root.transform, name, Vector3.zero, Quaternion.identity, Vector3.one * 2.5f);
                cx += 0.62f;
                if (visual != null && slot == "K2")   // show the hover highlight on one clue
                    PropAssets.AddHighlightShell(visual, name).SetActive(true);
            }
            float zx = 0.6f;
            foreach (string z in new[] { "Z1", "Z2", "Z3" })
            {
                var root = new GameObject(caseId + "_" + z);
                root.transform.position = new Vector3(zx, 0f, -2.0f);
                root.transform.rotation = Quaternion.Euler(0f, z == "Z3" ? 90f : 0f, 0f);
                PropAssets.AddVisual(root.transform, caseId + "_" + z, Vector3.zero, Quaternion.identity, Vector3.one);
                zx += 1.1f;
            }

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.86f, 0.87f, 0.9f);
            cam.fieldOfView = 34f;
            cam.transform.position = new Vector3(x * 0.48f, 2.35f, -5.3f);
            cam.transform.rotation = Quaternion.Euler(20f, 0f, 0f);

            var rt = new RenderTexture(1800, 1000, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "PropPreview_" + caseId + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Debug.Log("[PropPreview] " + path);
            return path;
        }
    }
}
