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
    /// numbered files <c>key_000</c>, <c>key_001</c>… become one animation. Production files win over reference
    /// slices in Art/Runtime/Placeholders (art brief D4).
    /// </summary>
    public static class ArtCatalogBuilder
    {
        public const string RuntimeRoot = "Assets/ClickDungeon/Art/Runtime";
        public const string ResourcesFolder = "Assets/ClickDungeon/Art/Resources";
        public const string CatalogPath = ResourcesFolder + "/" + Art.CatalogResourceName + ".asset";
        public const string CoverageReportPath = "Art/art-coverage.md";
        const string PlaceholderFolder = "/Placeholders/";

        static readonly Regex FramePattern = new Regex(@"^(?<key>.+)_(?<frame>\d{3,})$");

        [MenuItem("ClickDungeon/Art/Rebuild Art Catalog")]
        public static void Rebuild()
        {
            EnsureFolder(RuntimeRoot);
            EnsureFolder(ResourcesFolder);

            var sprites = new Dictionary<string, Sprite>();
            var paths = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { RuntimeRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;
                var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (paths.TryGetValue(name, out var existingPath))
                {
                    if (PreferCandidate(existingPath, path))
                    {
                        sprites[name] = sprite;
                        paths[name] = path;
                    }
                    else if (!PreferCandidate(path, existingPath))
                    {
                        Debug.LogWarning($"[ClickDungeon] Duplicate art name '{name}' at {path}; keeping {existingPath}.");
                    }
                    continue;
                }
                sprites[name] = sprite;
                paths[name] = path;
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
                    Placeholder = group.Value.All(name => IsPlaceholder(paths[name])),
                });
            }

            catalog.SetEntries(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ClickDungeon] Art catalog rebuilt: {entries.Count} keys ({entries.Count(e => e.Placeholder)} reference slices, {entries.Count(e => e.Frames.Length > 1)} animated).");
        }

        [MenuItem("ClickDungeon/Art/Report Art Coverage")]
        public static void ReportCoverage()
        {
            Rebuild();
            var catalog = AssetDatabase.LoadAssetAtPath<ArtCatalog>(CatalogPath);
            var wired = ArtKeys.Wired(ContentCatalog.CreateDefault());
            int production = 0, slices = 0, procedural = 0;
            var rows = new StringBuilder();
            foreach (var key in wired)
            {
                if (!catalog.TryGet(key, out var entry))
                {
                    procedural++;
                    rows.AppendLine($"| `{key}` | procedural placeholder |");
                    continue;
                }
                string frames = entry.Frames.Length > 1 ? $", {entry.Frames.Length} frames @ {entry.Fps} fps" : "";
                if (entry.Placeholder)
                {
                    slices++;
                    rows.AppendLine($"| `{key}` | reference slice (placeholder{frames}) |");
                }
                else
                {
                    production++;
                    rows.AppendLine($"| `{key}` | production art{frames} |");
                }
            }
            var notWired = catalog.Entries.Where(e => !wired.Contains(e.Key)).OrderBy(e => e.Key).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("# ClickDungeon art coverage");
            sb.AppendLine();
            sb.AppendLine($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}. Wired keys: {wired.Count} — production art {production}, reference slices {slices}, procedural placeholders {procedural}.");
            sb.AppendLine("Naming and specs: docs/art-brief.md.");
            sb.AppendLine();
            sb.AppendLine("| Key | Status |");
            sb.AppendLine("|---|---|");
            sb.Append(rows);
            sb.AppendLine();
            sb.AppendLine("## Art files not wired into the game yet");
            sb.AppendLine();
            if (notWired.Count == 0) sb.AppendLine("None.");
            foreach (var entry in notWired) sb.AppendLine($"- `{entry.Key}`{(entry.Placeholder ? " (reference slice)" : "")}");

            Directory.CreateDirectory(Path.GetDirectoryName(CoverageReportPath));
            File.WriteAllText(CoverageReportPath, sb.ToString());
            Debug.Log($"[ClickDungeon] Art coverage: production {production}, reference slices {slices}, procedural {procedural} of {wired.Count} wired keys. Report: {Path.GetFullPath(CoverageReportPath)}");
        }

        /// <summary>True when the candidate file should replace the existing one for the same key.</summary>
        public static bool PreferCandidate(string existingPath, string candidatePath) =>
            IsPlaceholder(existingPath) && !IsPlaceholder(candidatePath);

        public static bool IsPlaceholder(string path) => path.Replace('\\', '/').Contains(PlaceholderFolder);

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
