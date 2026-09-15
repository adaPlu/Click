using System.IO;
using ClickDungeon.Application;
using UnityEditor;
using UnityEngine;

namespace ClickDungeon.EditorTools
{
    /// <summary>Editor shortcuts for the playtest logs written by play mode and local builds.</summary>
    public static class TelemetryMenu
    {
        static string Folder => Path.Combine(UnityEngine.Application.persistentDataPath, "telemetry");

        [MenuItem("ClickDungeon/Telemetry/Open Log Folder")]
        public static void OpenFolder()
        {
            Directory.CreateDirectory(Folder);
            EditorUtility.RevealInFinder(Folder);
        }

        [MenuItem("ClickDungeon/Telemetry/Summarize Logs")]
        public static void Summarize()
        {
            Directory.CreateDirectory(Folder);
            var files = Directory.GetFiles(Folder, "*.jsonl");
            if (files.Length == 0)
            {
                Debug.LogWarning($"[ClickDungeon] No telemetry logs in {Folder}. Play a run first.");
                return;
            }

            var markdown = TelemetrySummary.FromFiles(files).ToMarkdown();
            var path = Path.Combine(Folder, "summary.md");
            File.WriteAllText(path, markdown);
            Debug.Log($"[ClickDungeon] Telemetry summary written to {path}\n\n{markdown}");
            EditorUtility.RevealInFinder(path);
        }
    }
}
