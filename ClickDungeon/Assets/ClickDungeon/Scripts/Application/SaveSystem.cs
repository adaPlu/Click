using System;
using System.IO;
using System.Text;
using ClickDungeon.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace ClickDungeon.Application
{
    /// <summary>Full authoritative run state as JSON (decision D-007).</summary>
    public static class SaveSerializer
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // [Serializable] state types are serialized field-by-field.
            ContractResolver = new DefaultContractResolver { IgnoreSerializableAttribute = false },
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Converters = { new StringEnumConverter() },
        };

        public static string ToJson(RunState run) => JsonConvert.SerializeObject(run, Settings);

        public static RunState FromJson(string json)
        {
            var run = JsonConvert.DeserializeObject<RunState>(json, Settings);
            if (run == null) throw new FormatException("Save is empty.");
            if (run.SaveSchemaVersion != Versions.SaveSchema)
                throw new FormatException($"Unsupported save schema {run.SaveSchemaVersion} (expected {Versions.SaveSchema}).");
            if (run.Hero == null || run.Floor == null || run.Floor.Cells == null || run.Floor.Cells.Length != BoardRules.CellCount)
                throw new FormatException("Save is incomplete.");
            return run;
        }
    }

    public interface ISaveStore
    {
        bool Exists { get; }
        void Save(RunState run);
        bool TryLoad(out RunState run, out string message);
        void Delete();
    }

    /// <summary>Atomic local save: temp → verify → replace, keeping the previous good save as .bak.</summary>
    public sealed class FileSaveStore : ISaveStore
    {
        public readonly string MainPath;
        public readonly string TempPath;
        public readonly string BackupPath;

        public FileSaveStore(string directory, string fileName = "run.json")
        {
            Directory.CreateDirectory(directory);
            MainPath = Path.Combine(directory, fileName);
            TempPath = MainPath + ".tmp";
            BackupPath = MainPath + ".bak";
        }

        public bool Exists => File.Exists(MainPath) || File.Exists(BackupPath);

        public void Save(RunState run)
        {
            File.WriteAllText(TempPath, SaveSerializer.ToJson(run), new UTF8Encoding(false));
            SaveSerializer.FromJson(File.ReadAllText(TempPath));

            if (!File.Exists(MainPath))
            {
                File.Move(TempPath, MainPath);
                return;
            }
            try
            {
                File.Replace(TempPath, MainPath, BackupPath);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException || ex is IOException)
            {
                File.Copy(MainPath, BackupPath, true);
                File.Delete(MainPath);
                File.Move(TempPath, MainPath);
            }
        }

        public bool TryLoad(out RunState run, out string message)
        {
            if (TryRead(MainPath, out run, out var mainError))
            {
                message = null;
                return true;
            }
            if (TryRead(BackupPath, out run, out var backupError))
            {
                message = $"Main save unreadable ({mainError}); restored the backup.";
                return true;
            }
            message = File.Exists(MainPath) ? mainError : backupError;
            return false;
        }

        public void Delete()
        {
            foreach (var path in new[] { MainPath, TempPath, BackupPath })
                if (File.Exists(path)) File.Delete(path);
        }

        static bool TryRead(string path, out RunState run, out string error)
        {
            run = null;
            error = null;
            if (!File.Exists(path))
            {
                error = "No save file.";
                return false;
            }
            try
            {
                run = SaveSerializer.FromJson(File.ReadAllText(path));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
