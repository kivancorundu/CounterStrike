using UnityEditor;
using UnityEngine;

namespace Vexa.EditorTools
{
    /// <summary>
    /// Import settings for the generated models (Tools/Blender): normal maps flagged as normal maps, mask
    /// textures kept linear, no materials imported (the game builds PBR materials from the baked textures),
    /// character clips set to loop except jump and death.
    /// </summary>
    public sealed class VexaModelImport : AssetPostprocessor
    {
        const string Root = "/Resources/Models/";

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains(Root)) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.EndsWith("_normal.png")) ti.textureType = TextureImporterType.NormalMap;
            else if (assetPath.EndsWith("_mask.png"))
            {
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
            }
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            if (assetPath.Contains("/Mobile/")) ti.maxTextureSize = 1024;
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
