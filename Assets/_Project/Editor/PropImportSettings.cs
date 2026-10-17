using UnityEditor;

namespace FYP.Detective.EditorTools
{
    /// <summary>
    /// Import settings for the meshes and textures that tools/maya/build_props.py writes to Art/Props: FBX in metres
    /// (file scale, factor 1), hard-edge normals from the file, no materials / animation / cameras / lights / colliders;
    /// Palette.png point-filtered without mipmaps (one colour per cell, no bleeding); BlobShadow.png with alpha.
    /// </summary>
    public class PropImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Art/Props/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var m = (ModelImporter)assetImporter;
            m.globalScale = 1f;
            m.useFileScale = true;
            m.materialImportMode = ModelImporterMaterialImportMode.None;
            m.importAnimation = false;
            m.animationType = ModelImporterAnimationType.None;
            m.importCameras = false;
            m.importLights = false;
            m.importBlendShapes = false;
            m.importVisibility = false;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.None;
            m.addCollider = false;
            m.generateSecondaryUV = false;
            m.isReadable = false;
            m.meshCompression = ModelImporterMeshCompression.Off;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.npotScale = TextureImporterNPOTScale.None;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            t.sRGBTexture = true;
            t.mipmapEnabled = false;
            if (assetPath.EndsWith("Palette.png"))
            {
                t.filterMode = UnityEngine.FilterMode.Point;
                t.alphaIsTransparency = false;
            }
            else
            {
                t.filterMode = UnityEngine.FilterMode.Bilinear;
                t.alphaSource = TextureImporterAlphaSource.FromInput;
                t.alphaIsTransparency = true;
            }
        }
    }
}
