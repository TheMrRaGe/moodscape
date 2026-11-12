using UnityEditor;

namespace NightPages.EditorTools
{
    /// <summary>
    /// Import rules for the downloaded Poly Haven files: normal maps (*_nor_gl.jpg) are marked as normal maps,
    /// photo textures are kept at 1k, and models keep their real-world scale.
    /// </summary>
    public class NightPagesImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/NightPages/Resources/")) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.Contains("_nor_gl")) ti.textureType = TextureImporterType.NormalMap;
            // Packed metallic / occlusion / smoothness maps are data, not colour.
            if (assetPath.Contains("_ms_")) ti.sRGBTexture = false;
            ti.maxTextureSize = 1024;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            if (assetPath.Contains("/NPFace/"))
            {
                // The face reader's test photo is read pixel by pixel, like a camera frame.
                ti.isReadable = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 2048;
            }
        }

        void OnPreprocessModel()
        {
            if (!assetPath.Contains("/NightPages/Resources/NPModels/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.isReadable = false;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
        }
    }
}
