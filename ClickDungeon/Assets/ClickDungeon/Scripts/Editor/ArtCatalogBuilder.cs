using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ClickDungeon.Content;
using ClickDungeon.Unity.Ui;
using UnityEditor;
using UnityEngine;

namespace ClickDungeon.EditorTools
{
    /// <summary>
    /// Builds the art catalog from files under Art/Runtime (decision D-016). The file name is the key;
    /// numbered files <c>key_000</c>, <c>key_001</c>… become one animation.
    /// </summary>
    public static class ArtCatalogBuilder
    {
        public const string RuntimeRoot = "Assets/ClickDungeon/Art/Runtime";
        public const string ResourcesFolder = "Assets/ClickDungeon/Art/Resources";
        public const string CatalogPath = ResourcesFolder + "/" + Art.CatalogResourceName + ".asset";
        public const string CoverageReportPath = "Art/art-coverage.md";

        static readonly Regex FramePattern = new Regex(@"^(?<key>.+)_(?<frame>\d{3,})$");

        [MenuItem("ClickDungeon/Art/Rebuild Art Catalog")]
        public static void Rebuild()
        {
            EnsureFolder(RuntimeRoot);
            EnsureFolder(ResourcesFolder);

            var sprites = new Dictionary<string, Sprite>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { RuntimeRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;
                var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (sprites.ContainsKey(name))
                {
                    Debug.LogWarning($"[ClickDungeon] Duplicate art name '{name}' at {path}; keeping the first one.");
                    continue;
                }
                sprites[name] = sprite;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ArtCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            // Keep hand-tuned fps across rebuilds.
            var previousFps = new Dictionary<string, float>();
            foreach (var entry in catalog.Entries)
                if (entry != null && !string.IsNullOrEmpty(entry.Key)) previousFps[entry.Key] = entry.Fps;

            var entries = new List<ArtCatalog.Entry>();
            foreach (var group in GroupFrames(sprites.Keys))
            {
                entries.Add(new ArtCatalog.Entry
                {
                    Key = group.Key,
                    Frames = group.Value.Select(name => sprites[name]).ToArray(),
                    Fps = previousFps.TryGetValue(group.Key, out var fps) ? fps : DefaultFps(group.Key),
                });
            }

            catalog.SetEntries(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ClickDungeon] Art catalog rebuilt: {entries.Count} keys ({entries.Count(e => e.Frames.Length > 1)} animated).");
        }

        [MenuItem("ClickDungeon/Art/Report Art Coverage")]
        public static void ReportCoverage()
        {
            Rebuild();
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            var wired = ArtKeys.Wired(ContentCatalog.CreateDefault());
            var present = wired.Where(key => catalog.TryGet(key, out _)).ToList();
            var notWired = catalog.Entries.Select(e => e.Key).Where(key => !wired.Contains(key)).OrderBy(key => key).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("# ClickDungeon art coverage");
            sb.AppendLine();
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}. Wired keys with art: {present.Count} of {wired.Count}.");
            sb.AppendLine("Keys without art keep drawing the placeholder. Naming and specs: docs/art-brief.md.");
            sb.AppendLine();
            sb.AppendLine("| Key | Status |");
            sb.AppendLine("|---|---|");
            foreach (var key in wired)
            {
                if (!catalog.TryGet(key, out var entry)) sb.AppendLine($"| `{key}` | placeholder |");
                else sb.AppendLine($"| `{key}` | art ({(entry.Frames.Length > 1 ? $"{entry.Frames.Length} frames @ {entry.Fps} fps" : "static")}) |");
            }
            sb.AppendLine();
            sb.AppendLine("## Art files not wired into the game yet");
            sb.AppendLine();
            if (notWired.Count == 0) sb.AppendLine("None.");
            foreach (var key in notWired) sb.AppendLine($"- `{key}`");

            Directory.CreateDirectory(Path.GetDirectoryName(CoverageReportPath));
            File.WriteAllText(CoverageReportPath, sb.ToString());
            Debug.Log($"[ClickDungeon] Art coverage: {present.Count}/{wired.Count} wired keys have art. Report: {Path.GetFullPath(CoverageReportPath)}");
        }

        /// <summary>Groups file names into keys; numbered frames are ordered numerically and win over a same-named single.</summary>
        public static SortedDictionary<string, List<string>> GroupFrames(IEnumerable<string> names)
        {
            var singles = new List<string>();
            var frames = new Dictionary<string, SortedList<int, string>>();
            foreach (var name in names)
            {
                var match = FramePattern.Match(name);
                if (!match.Success)
                {
                    singles.Add(name);
                    continue;
                }
                var key = match.Groups["key"].Value;
                if (!frames.TryGetValue(key, out var list)) frames[key] = list = new SortedList<int, string>();
                list[int.Parse(match.Groups["frame"].Value)] = name;
            }

            var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var name in singles) result[name] = new List<string> { name };
            foreach (var pair in frames) result[pair.Key] = pair.Value.Values.ToList();
            return result;
        }

        static float DefaultFps(string key) => key.EndsWith("_idle", StringComparison.Ordinal) ? 8f : 12f;

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
