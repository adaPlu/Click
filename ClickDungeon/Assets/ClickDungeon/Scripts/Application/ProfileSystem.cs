using System;
using System.IO;
using System.Text;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using Newtonsoft.Json;

namespace ClickDungeon.Application
{
    /// <summary>
    /// The profile between runs: banking what a run found, and spending it. Kept apart from the run save so a corrupt or
    /// abandoned run never costs the player their coins.
    /// </summary>
    public static class ProfileSystem
    {
        /// <summary>Adds what the run carried out and counts the run. Called once, when a run ends.</summary>
        public static void Bank(ProfileState profile, RunState run)
        {
            if (profile == null || run == null) return;
            Progression.BankXp(profile, run);
            profile.Coins += Math.Max(0, run.CoinsFound);
            profile.Gems += Math.Max(0, run.GemsFound);
            // A key whose chest was never reached is not lost: it goes back in the pocket for the next run.
            if (run.Hero != null) profile.SpecialKeys += Math.Max(0, run.Hero.SpecialKeys);
            profile.RunsFinished++;
            if (run.Status == RunStatus.Won) profile.RunsWon++;
            profile.MonstersSlain += Math.Max(0, run.MonstersSlain);
            profile.ChestsOpened += Math.Max(0, run.ChestsOpened);
            profile.CoinsEarned += Math.Max(0, run.CoinsFound);
            if (run.Floor != null) profile.DeepestFloor = Math.Max(profile.DeepestFloor, run.Floor.FloorIndex);
        }

        /// <summary>
        /// Hands the bought provisions to a new run and spends them. A provision is consumed when the run starts, so quitting
        /// to the title does not get it back.
        /// </summary>
        public static void Provision(ProfileState profile, RunState run, ContentCatalog catalog)
        {
            if (profile == null || run == null) return;
            if (profile.HeartTokens > 0)
            {
                int hearts = profile.HeartTokens * catalog.Treasure.HeartTokenHearts;
                run.Hero.MaxHp += hearts;
                run.Hero.Hp += hearts;
                profile.HeartTokens = 0;
            }
            if (profile.PotionRations > 0)
            {
                run.Hero.Potions += profile.PotionRations * catalog.Treasure.PotionRationPotions;
                profile.PotionRations = 0;
            }
            // The shop's other boosts (D-036): each is spent into the run's starting numbers.
            var t = catalog.Treasure;
            run.Hero.MaxMana += profile.ManaTonics * t.ManaTonicMana;
            run.Hero.Mana += profile.ManaTonics * t.ManaTonicMana;
            run.Hero.SlashDamage += profile.StrengthElixirs * t.StrengthElixirSlash;
            run.BonusCoinsPerChestReward += profile.FortuneScrolls * t.FortuneScrollCoins;
            run.BonusXpPerFloor += profile.WisdomScrolls * t.WisdomScrollXp;
            profile.ManaTonics = profile.StrengthElixirs = profile.FortuneScrolls = profile.WisdomScrolls = 0;
            if (profile.SpecialKeys > 0)
            {
                // One premium chest per key, on the floors that can hold one; any key beyond that stays in the pocket.
                int floors = Math.Max(0, catalog.Treasure.PremiumLastFloor - catalog.Treasure.PremiumFirstFloor + 1);
                int carried = Math.Min(profile.SpecialKeys, floors);
                run.Hero.SpecialKeys += carried;
                run.PremiumChestsToPlace += carried;
                profile.SpecialKeys -= carried;
            }
        }
    }

    public interface IProfileStore
    {
        ProfileState Load();
        void Save(ProfileState profile);

        /// <summary>What to tell the player about the last <see cref="Load"/>, or null when it read cleanly.</summary>
        string LoadNotice { get; }
    }

    /// <summary>A profile that only lives as long as the process: automation runs and tests never touch a real one.</summary>
    public sealed class MemoryProfileStore : IProfileStore
    {
        ProfileState _profile = new ProfileState();

        public ProfileState Load() => _profile;
        public void Save(ProfileState profile) => _profile = profile;
        public string LoadNotice => null;
    }

    /// <summary>
    /// The player's profile on disk, with the run save's protections (D-043): a write goes to a temp file, is read back to
    /// prove it parses, and only then replaces the live file, keeping the copy it replaced as <c>profile.json.bak</c>. A
    /// profile that cannot be read falls back to that backup; if neither can be read the damaged file is set aside as
    /// <c>profile.json.broken</c> and never overwritten, because it is the only record of the player's progress.
    /// </summary>
    public sealed class FileProfileStore : IProfileStore
    {
        public readonly string MainPath;
        public readonly string TempPath;
        public readonly string BackupPath;
        public readonly string BrokenPath;

        /// <summary>False once a damaged profile could not be set aside: nothing may overwrite it until that is sorted out.</summary>
        bool _mayOverwrite = true;

        public string LoadNotice { get; private set; }

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        public FileProfileStore(string directory, string fileName = "profile.json")
        {
            Directory.CreateDirectory(directory);
            MainPath = Path.Combine(directory, fileName);
            TempPath = MainPath + ".tmp";
            BackupPath = MainPath + ".bak";
            BrokenPath = MainPath + ".broken";
        }

        /// <summary>
        /// The profile, the backup if the profile cannot be read, or an empty one — a broken file must never stop the game
        /// from starting, but it is kept rather than replaced, and <see cref="LoadNotice"/> says what happened.
        /// </summary>
        public ProfileState Load()
        {
            LoadNotice = null;
            _mayOverwrite = true;
            if (!File.Exists(MainPath) && !File.Exists(BackupPath)) return new ProfileState();
            if (TryRead(MainPath, out var loaded)) return loaded;
            if (TryRead(BackupPath, out loaded))
            {
                LoadNotice = "Your profile could not be read, so its backup was used. Anything bought or earned in the last moments before that may be missing.";
                return loaded;
            }
            KeepTheDamagedFile();
            return new ProfileState();
        }

        /// <summary>
        /// Moves an unreadable profile out of the way so a fresh one can be written without destroying it. If it cannot be
        /// moved, nothing may overwrite it: <see cref="Save"/> then refuses rather than taking the player's progress with it.
        /// </summary>
        void KeepTheDamagedFile()
        {
            try
            {
                if (File.Exists(MainPath))
                {
                    if (File.Exists(BrokenPath)) File.Delete(BrokenPath);
                    File.Move(MainPath, BrokenPath);
                }
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                LoadNotice = $"Your profile could not be read. It has been kept as {Path.GetFileName(BrokenPath)} and a new one started; nothing was thrown away.";
            }
            catch (Exception)
            {
                _mayOverwrite = false;
                LoadNotice = $"Your profile could not be read, and could not be set aside either. It will not be overwritten: close the game and move {Path.GetFileName(MainPath)} somewhere safe.";
            }
        }

        bool TryRead(string path, out ProfileState result)
        {
            result = null;
            try
            {
                if (!File.Exists(path)) return false;
                var profile = JsonConvert.DeserializeObject<ProfileState>(File.ReadAllText(path), Settings);
                // A profile from a newer build may mean anything; an older one only lacks fields, which default safely.
                if (profile == null || profile.SchemaVersion > Versions.ProfileSchema) return false;
                profile.SchemaVersion = Versions.ProfileSchema;
                result = Repair(profile);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Clamps and fills a loaded profile, so a hand-edited file cannot carry negatives or null collections in.</summary>
        static ProfileState Repair(ProfileState profile)
        {
                profile.Coins = Math.Max(0, profile.Coins);
                profile.Gems = Math.Max(0, profile.Gems);
                profile.PotionRations = Math.Max(0, profile.PotionRations);
                profile.HeartTokens = Math.Max(0, profile.HeartTokens);
                profile.SpecialKeys = Math.Max(0, profile.SpecialKeys);
                profile.ManaTonics = Math.Max(0, profile.ManaTonics);
                profile.StrengthElixirs = Math.Max(0, profile.StrengthElixirs);
                profile.FortuneScrolls = Math.Max(0, profile.FortuneScrolls);
                profile.WisdomScrolls = Math.Max(0, profile.WisdomScrolls);
                profile.Xp = Math.Max(0, profile.Xp);
                profile.DailyStreak = Math.Max(0, profile.DailyStreak);
                if (profile.Talents == null) profile.Talents = new System.Collections.Generic.Dictionary<string, int>();
                if (profile.Items == null) profile.Items = new System.Collections.Generic.List<string>();
                if (profile.Equipped == null) profile.Equipped = new System.Collections.Generic.Dictionary<string, string>();
                if (profile.Achievements == null) profile.Achievements = new System.Collections.Generic.List<string>();
                if (profile.Mail == null) profile.Mail = new System.Collections.Generic.List<MailMessage>();
                profile.Mail.RemoveAll(m => m == null);
                return profile;
        }

        /// <summary>
        /// Writes the profile the way the run save is written: to a temp file, read back to prove it parses, then put in
        /// place of the live file, which becomes the backup. The live file is never deleted before its replacement exists.
        /// </summary>
        public void Save(ProfileState profile)
        {
            if (profile == null) return;
            if (!_mayOverwrite)
                throw new IOException($"The unreadable profile could not be set aside, so it is not being overwritten. Move {MainPath} somewhere safe.");

            File.WriteAllText(TempPath, JsonConvert.SerializeObject(profile, Settings), new UTF8Encoding(false));
            if (JsonConvert.DeserializeObject<ProfileState>(File.ReadAllText(TempPath), Settings) == null)
                throw new IOException("The profile did not read back after it was written.");

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
                // Some file systems have no atomic replace; keep the old copy as the backup by hand.
                File.Copy(MainPath, BackupPath, true);
                File.Delete(MainPath);
                File.Move(TempPath, MainPath);
            }
        }
    }
}
