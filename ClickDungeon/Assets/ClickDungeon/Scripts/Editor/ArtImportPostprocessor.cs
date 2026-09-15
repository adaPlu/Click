using UnityEditor;
using UnityEngine;

namespace ClickDungeon.EditorTools
{
    /// <summary>
    /// Applies the art brief's import standards to new files under Art/Runtime and keeps the art catalog in sync.
    /// Standards are applied on first import only, so later manual tweaks (e.g. 9-slice borders) are kept.
    /// </summary>
    public sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtCatalogBuilder.RuntimeRoot + "/") || !assetImporter.importSettingsMissing) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!Touches(imported) && !Touches(deleted) && !Touches(moved) && !Touches(movedFrom)) return;
            EditorApplication.delayCall -= ArtCatalogBuilder.Rebuild;
            EditorApplication.delayCall += ArtCatalogBuilder.Rebuild;
        }

        static bool Touches(string[] paths)
        {
            foreach (var path in paths)
                if (path.StartsWith(ArtCatalogBuilder.RuntimeRoot + "/") && !path.EndsWith(".md"))
                    return true;
            return false;
        }
    }
}
