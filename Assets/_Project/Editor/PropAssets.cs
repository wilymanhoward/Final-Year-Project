using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Shared materials and "Visual" children for the meshes built by tools/maya/build_props.py (Art/Props/*.fbx):
    /// each FBX holds "&lt;name&gt;_Body" (opaque palette), optional "&lt;name&gt;_Glass" (transparent) and "&lt;name&gt;_Alpha"
    /// (zone outline); the room floors (X_Floor / Y_Floor) use the semi-transparent <see cref="Floor"/> material. Materials (Art/Materials/Props), all
    /// on FYP/OccludedProp (palette, Simple-Lit-like diffuse, no specular, Meta depth occlusion): opaque palette,
    /// transparent glass / floor / zone outline / blob shadow, and an unlit front-culled shell for the hover highlight.
    /// Renderers never cast or receive shadows (Quest 3: no realtime shadows).
    /// </summary>
    internal static class PropAssets
    {
        public const string MaterialFolder = "Assets/_Project/Art/Materials/Props";
        private const string PalettePath = PropImportSettings.Folder + "Palette.png";
        private const string BlobPath = PropImportSettings.Folder + "BlobShadow.png";

        public static string FbxPath(string name) => PropImportSettings.Folder + name + ".fbx";

        /// <summary>Names of the FBX files that are missing (empty = all there).</summary>
        public static List<string> Missing(IEnumerable<string> names)
        {
            var missing = new List<string>();
            foreach (string n in names)
                if (Mesh(n, "_Body") == null) missing.Add(n + ".fbx");
            return missing;
        }

        public static Mesh Mesh(string name, string suffix)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FbxPath(name)))
                if (o is Mesh m && m.name == name + suffix) return m;
            return null;
        }

        // ---- Materials ----
        // All on FYP/OccludedProp (Art/Shaders/OccludedProp.shader): palette look like URP Simple Lit without specular,
        // plus Meta's environment depth occlusion (a real person in front of a prop hides it). Output is premultiplied,
        // so opaque and transparent variants differ only in alpha, ZWrite and queue.

        public const string PropShaderName = "FYP/OccludedProp";

        /// <summary>
        /// Depth bias (fraction of the distance a surface is pulled towards the viewer before the depth test) for raised
        /// props and clues, and for things lying on the real floor, which would otherwise flicker against it. Tune on the
        /// headset (Materials/Props, property "Environment Depth Bias").
        /// </summary>
        public const float PropDepthBias = 0.02f;
        public const float FloorDepthBias = 0.06f;

        public static Material Palette => Prop("Props_Palette", Tex(PalettePath), Color.white, true, false, PropDepthBias);
        public static Material Glass => Prop("Props_Glass", Tex(PalettePath), new Color(1f, 1f, 1f, 0.30f), true, true, PropDepthBias);

        /// <summary>Opacity of the room floors (user, session 6: semi-transparent, the real floor stays faintly visible).</summary>
        public const float FloorAlpha = 0.7f;

        /// <summary>
        /// Room floor: palette, semi-transparent, drawn just before the other transparent materials (queue 2950) so blob
        /// shadows, glass and zone outlines still show on top of it; opaque decals and props are drawn before it anyway.
        /// </summary>
        public static Material Floor =>
            Prop("Props_Floor", Tex(PalettePath), new Color(1f, 1f, 1f, FloorAlpha), true, true, FloorDepthBias, (int)RenderQueue.Transparent - 50);

        public static Material Outline => Prop("Props_ZoneOutline", Tex(PalettePath), new Color(1f, 1f, 1f, 0.55f), false, true, FloorDepthBias);
        public static Material Highlight => Prop("Props_ClueHighlight", null, new Color(1f, 0.93f, 0.55f, 1f), false, false, PropDepthBias,
            cull: CullMode.Front);
        public static Material Blob => Prop("Props_BlobShadow", Tex(BlobPath), new Color(1f, 1f, 1f, 0.55f), false, true, FloorDepthBias);

        /// <summary>All prop materials (created / updated on access); FYP > Set Up Occlusion refreshes them.</summary>
        public static Material[] All => new[] { Palette, Glass, Floor, Outline, Highlight, Blob };

        private static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        private static Material Prop(string name, Texture2D tex, Color color, bool lit, bool transparent, float depthBias,
            int queue = -1, CullMode cull = CullMode.Back)
        {
            var m = GetOrCreate(name, PropShaderName);
            m.shaderKeywords = new string[0];   // drop URP Simple Lit / Unlit keywords from the old shader
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Lighting", lit ? 1f : 0f);
            m.SetFloat("_EnvironmentDepthBias", depthBias);
            m.SetFloat("_Cull", (float)cull);
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            m.renderQueue = queue >= 0 ? queue : transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material GetOrCreate(string name, string shaderName)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "Props");
            string path = MaterialFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        // ---- Scene objects ----

        /// <summary>
        /// Adds a "Visual" child with the FBX's meshes (Body + optional Glass / Alpha). Returns it, or null if the FBX
        /// is missing. Local pose is relative to <paramref name="parent"/>.
        /// </summary>
        public static GameObject AddVisual(Transform parent, string name, Vector3 localPosition, Quaternion localRotation, Vector3 localScale,
            Material bodyMaterial = null)
        {
            var body = Mesh(name, "_Body");
            if (body == null) return null;
            var visual = MeshObject("Visual", parent, body, bodyMaterial != null ? bodyMaterial : Palette);
            visual.transform.localPosition = localPosition;
            visual.transform.localRotation = localRotation;
            visual.transform.localScale = localScale;
            var glass = Mesh(name, "_Glass");
            if (glass != null) MeshObject("Glass", visual.transform, glass, Glass);
            var alpha = Mesh(name, "_Alpha");
            if (alpha != null) MeshObject("Outline", visual.transform, alpha, Outline);
            return visual;
        }

        /// <summary>Hover outline: the body mesh enlarged around its centre, front faces culled. Starts inactive.</summary>
        public static GameObject AddHighlightShell(GameObject visual, string name, float grow = 1.10f)
        {
            var body = Mesh(name, "_Body");
            var shell = MeshObject("Highlight", visual.transform, body, Highlight);
            Vector3 c = body.bounds.center;
            shell.transform.localScale = Vector3.one * grow;
            shell.transform.localPosition = c - c * grow;   // scale around the mesh centre, not its pivot
            shell.SetActive(false);
            return shell;
        }

        /// <summary>Soft blob shadow on the floor under a prop (local to <paramref name="parent"/>).</summary>
        public static void AddBlobShadow(Transform parent, Bounds footprint, float grow = 1.25f)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "BlobShadow";
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = new Vector3(footprint.center.x, 0.002f, footprint.center.z);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(Mathf.Max(0.12f, footprint.size.x * grow), Mathf.Max(0.12f, footprint.size.z * grow), 1f);
            Quiet(q.GetComponent<MeshRenderer>(), Blob);
        }

        private static GameObject MeshObject(string objName, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(objName, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            Quiet(go.GetComponent<MeshRenderer>(), material);
            return go;
        }

        private static void Quiet(MeshRenderer r, Material material)
        {
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
        }

        /// <summary>Triangles of every Visual mesh under <paramref name="root"/> (bodies, glass, outlines, highlights).</summary>
        public static int Triangles(Transform root)
        {
            int n = 0;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || f.GetComponent<MeshRenderer>() == null) continue;
                n += (int)f.sharedMesh.GetIndexCount(0) / 3;
                for (int s = 1; s < f.sharedMesh.subMeshCount; s++) n += (int)f.sharedMesh.GetIndexCount(s) / 3;
            }
            return n;
        }
    }
}
