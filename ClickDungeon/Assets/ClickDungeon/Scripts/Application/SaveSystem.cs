using System;
using System.IO;
using System.Text;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
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
            // Enums are saved by name; numbers such as "Difficulty": 7 are rejected instead of loading an undefined value.
            Converters = { new StringEnumConverter { AllowIntegerValues = false } },
        };

        public static string ToJson(RunState run) => JsonConvert.SerializeObject(run, Settings);

        /// <summary>
        /// Loads and validates a save. Anything that could crash or corrupt play later is rejected here as a FormatException,
        /// so the store can fall back to the backup.
        /// </summary>
        public static RunState FromJson(string json)
        {
            RunState run;
            try
            {
                run = JsonConvert.DeserializeObject<RunState>(json, Settings);
            }
            catch (JsonException ex)
            {
                throw new FormatException("Save is not readable: " + ex.Message, ex);
            }
            if (run == null) throw new FormatException("Save is empty.");
            if (run.SaveSchemaVersion != Versions.SaveSchema)
                throw new FormatException($"Unsupported save schema {run.SaveSchemaVersion} (expected {Versions.SaveSchema}).");
            if (run.Hero == null || run.Floor == null || run.Floor.Cells == null || run.Floor.Cells.Length != BoardRules.CellCount)
                throw new FormatException("Save is incomplete.");
            if (run.RulesetVersion > Versions.Ruleset)
                throw new FormatException($"Save was made by a newer version of the rules ({run.RulesetVersion}).");
            // Floors are generated from the current generation version, so a save from another one would diverge.
            if (run.GenerationVersion != Versions.Generation)
                throw new FormatException($"Save uses floor generation {run.GenerationVersion} (expected {Versions.Generation}).");
            Validate(run);
            // A hero retired from the roster is carried to the one that replaced it (D-057), so a run in progress survives
            // the roster changing under it: Sir Clickington's runs become Ironheart's. The successor shares the class, so
            // only the face changes — the stats, talents and board of the run are exactly as they were.
            if (ClickDungeon.Content.ContentCatalog.RetiredHeroes.TryGetValue(run.Hero.IdentityId, out var successor))
                run.Hero.IdentityId = successor;
            // Older rulesets only lack additions (ruleset 2 added the arrival heal), so the run continues under current rules.
            run.RulesetVersion = Versions.Ruleset;
            return run;
        }

        static void Validate(RunState run)
        {
            var hero = run.Hero;
            var floor = run.Floor;
            Require(Enum.IsDefined(typeof(Difficulty), run.Difficulty), "unknown difficulty");
            Require(Enum.IsDefined(typeof(MovementMode), run.Movement), "unknown movement mode");
            Require(Enum.IsDefined(typeof(RunStatus), run.Status), "unknown run status");
            Require(floor.Enemies != null && run.Rewards != null, "missing enemy or reward list");
            Require(run.FloorCount >= 1 && floor.FloorIndex >= 1 && floor.FloorIndex <= run.FloorCount, "floor number out of range");
            Require(!string.IsNullOrEmpty(hero.ClassId) && !string.IsNullOrEmpty(hero.IdentityId), "hero has no class");
            Require(hero.MaxHp > 0 && hero.Hp >= 0 && hero.Hp <= hero.MaxHp, "hero health out of range");
            Require(hero.MaxMana >= 0 && hero.Mana >= 0 && hero.Mana <= Math.Max(hero.MaxMana, 0), "hero mana out of range");
            Require(hero.Pos.InBounds, "hero off the board");
            // Hand-built boards may have no exit (Invalid); anything else must be a real tile.
            Require(floor.Exit == GridPos.Invalid || floor.Exit.InBounds, "exit off the board");

            foreach (var cell in floor.Cells)
            {
                Require(cell != null, "missing tile");
                Require(Enum.IsDefined(typeof(Terrain), cell.Terrain) && Enum.IsDefined(typeof(HazardKind), cell.Hazard)
                        && Enum.IsDefined(typeof(ContentKind), cell.Content) && Enum.IsDefined(typeof(Knowledge), cell.Knowledge)
                        && Enum.IsDefined(typeof(ChestQuality), cell.Quality),
                    "unknown tile value");
                Require(cell.ChestTaps >= 0 && cell.ChestTaps <= Chests.TapsToOpen(cell.Quality), "chest tap count out of range");
            }
            Require(floor[hero.Pos].Terrain == Terrain.Floor, "hero inside a wall or pit");

            foreach (var enemy in floor.Enemies)
            {
                Require(enemy != null && !string.IsNullOrEmpty(enemy.DefId), "enemy without a type");
                Require(enemy.Pos.InBounds, "enemy off the board");
                Require(Enum.IsDefined(typeof(EnemyMode), enemy.Mode) && Enum.IsDefined(typeof(IntentKind), enemy.Intent.Kind), "unknown enemy state");
            }
            foreach (var reward in run.Rewards) Require(reward != null, "missing reward record");

            // Inside a vault, the floor to return to travels with the save (D-018).
            if (run.OuterFloor != null)
            {
                Require(run.OuterFloor.Cells != null && run.OuterFloor.Cells.Length == BoardRules.CellCount, "outer floor is incomplete");
                Require(run.OuterFloor.Enemies != null, "outer floor has no enemy list");
                Require(run.ReturnPos.InBounds, "return position off the board");
                foreach (var cell in run.OuterFloor.Cells) Require(cell != null, "missing tile on the outer floor");
            }

            // A vault already visited travels with the save too, so the door leads back into the room it was left in (REL-26).
            // D-075: the skills a run carries are read by the resolver every time one is used, and a save is a file on
            // disk. Validate is not given the catalog, so what it can check is the SHAPE - no nulls, no duplicates, no
            // more than there are slots. Whether an id names a real skill of this hero's class is Skills.InSlot's job,
            // and it answers "no skill in that slot" rather than trusting the list (SEC-04's rule for worn gear).
            Require(run.Skills != null, "the run has no skill list");
            Require(run.Skills.Count <= BoardRules.SkillSlots, "the run carries more skills than there are slots");
            for (int i = 0; i < run.Skills.Count; i++)
            {
                Require(!string.IsNullOrEmpty(run.Skills[i]), "an empty skill slot id");
                Require(run.Skills.IndexOf(run.Skills[i]) == i, $"skill '{run.Skills[i]}' is carried twice");
            }

            if (run.VisitedVault != null)
            {
                Require(run.VisitedVault.Cells != null && run.VisitedVault.Cells.Length == BoardRules.CellCount, "visited vault is incomplete");
                Require(run.VisitedVault.Enemies != null, "visited vault has no enemy list");
                Require(run.VisitedVaultDoor.InBounds, "visited vault door off the board");
                foreach (var cell in run.VisitedVault.Cells) Require(cell != null, "missing tile in the visited vault");
            }
        }

        static void Require(bool ok, string problem)
        {
            if (!ok) throw new FormatException($"Save is invalid: {problem}.");
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
            // Main goes last: if a delete fails part-way, the newest save is what remains, never an older backup
            // (after a finished run is written, the backup still holds the turn before the end).
            foreach (var path in new[] { BackupPath, TempPath, MainPath })
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
