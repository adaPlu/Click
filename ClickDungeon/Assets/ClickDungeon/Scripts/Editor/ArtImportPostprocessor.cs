using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ClickDungeon.EditorTools
{
    /// <summary>
    /// Applies the art brief's import standards to new files under Art/Runtime and keeps the art catalog in sync.
    /// Standards are applied on first import only, so later manual tweaks are kept. The exception is a 9-slice border
    /// listed in the slicer's borders.json: a frame is only usable sliced, and the slicer owns that number.
    /// </summary>
    public sealed class ArtImportPostprocessor : AssetPostprocessor
    {
        /// <summary>Written by tools/art-slicer: the border cannot be stored in the PNG itself.</summary>
        public const string BordersPath = ArtCatalogBuilder.RuntimeRoot + "/Placeholders/borders.json";

        static Dictionary<string, int> _borders;
        static string _bordersJson;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtCatalogBuilder.RuntimeRoot + "/")) return;
            var importer = (TextureImporter)assetImporter;
            if (assetImporter.importSettingsMissing)
            {
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

            int border = BorderFor(Path.GetFileNameWithoutExtension(assetPath));
            var wanted = new Vector4(border, border, border, border);
            if (border > 0 && importer.spriteBorder != wanted) importer.spriteBorder = wanted;
        }

        /// <summary>The slicer's border for this key, or 0 when it is not a generated frame.</summary>
        static int BorderFor(string key)
        {
            if (!File.Exists(BordersPath)) return 0;
            string json = File.ReadAllText(BordersPath);
            if (_borders == null || json != _bordersJson)
            {
                _bordersJson = json;
                _borders = new Dictionary<string, int>();
                // "<key>": <border>, one pair per line, as the slicer writes it.
                foreach (Match match in Regex.Matches(json, @"""([a-z0-9_]+)""\s*:\s*([0-9]+)"))
                    _borders[match.Groups[1].Value] = int.Parse(match.Groups[2].Value);
            }
            return _borders.TryGetValue(key, out int value) ? value : 0;
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
