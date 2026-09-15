using System;
using System.Collections.Generic;
using System.IO;
using ClickDungeon.Application;

namespace ClickDungeon.Telemetry.Report
{
    /// <summary>
    /// Usage: dotnet run --project Sim/ClickDungeon.Telemetry.Report -- &lt;file-or-folder&gt;... [--out summary.md]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            var files = new List<string>();
            string outPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--out" && i + 1 < args.Length)
                {
                    outPath = args[++i];
                }
                else if (Directory.Exists(args[i]))
                {
                    var found = Directory.GetFiles(args[i], "*.jsonl");
                    Array.Sort(found, StringComparer.Ordinal);
                    files.AddRange(found);
                }
                else if (File.Exists(args[i]))
                {
                    files.Add(args[i]);
                }
                else
                {
                    Console.Error.WriteLine($"Not found: {args[i]}");
                    return 2;
                }
            }

            if (files.Count == 0)
            {
                Console.Error.WriteLine("usage: ClickDungeon.Telemetry.Report <file-or-folder>... [--out summary.md]");
                return 1;
            }

            var markdown = TelemetrySummary.FromFiles(files).ToMarkdown();
            if (outPath == null)
            {
                Console.WriteLine(markdown);
            }
            else
            {
                File.WriteAllText(outPath, markdown);
                Console.WriteLine($"Wrote {outPath} from {files.Count} file(s).");
            }
            return 0;
        }
    }
}
