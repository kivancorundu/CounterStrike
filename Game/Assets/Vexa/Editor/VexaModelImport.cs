using UnityEditor;
using UnityEngine;

namespace Vexa.EditorTools
{
    /// <summary>
    /// Import settings for the generated models (Tools/Blender): normal maps flagged as normal maps, mask
    /// textures kept linear, 4K allowed on PC (1K on mobile platforms), no materials imported (the game builds
    /// PBR materials from the baked textures), character clips set to loop except jump and death.
    /// </summary>
    public sealed class VexaModelImport : AssetPostprocessor
    {
        const string Root = "/Resources/Models/";

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains(Root)) return;
            var ti = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);   // .png or .jpg
            if (name.EndsWith("_normal")) ti.textureType = TextureImporterType.NormalMap;
            else if (name.EndsWith("_mask"))
            {
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
            }
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            bool mobile = assetPath.Contains("/Mobile/");
            // PC keeps the full 4K sources (Unity's default limit is 2K); mobile caps at 1K
            ti.maxTextureSize = mobile ? 1024 : 4096;
            ti.textureCompression = mobile ? TextureImporterCompression.Compressed : TextureImporterCompression.CompressedHQ;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            ti.SetPlatformTextureSettings(android);
            var ios = ti.GetPlatformTextureSettings("iPhone");
            ios.overridden = true;
            ios.maxTextureSize = 1024;
            ti.SetPlatformTextureSettings(ios);
        }

        void OnPreprocessModel()
        {
            if (!assetPath.Contains(Root)) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            if (assetPath.Contains("/Characters/"))
            {
                mi.animationType = ModelImporterAnimationType.Generic;
                mi.importAnimation = true;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.Contains(Root + "Characters/")) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
                c.loopTime = !(c.name.EndsWith("jump") || c.name.EndsWith("death"));
            mi.clipAnimations = clips;
        }
    }
}
