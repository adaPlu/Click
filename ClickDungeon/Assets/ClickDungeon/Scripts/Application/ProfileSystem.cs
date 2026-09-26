using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
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
        /// <summary>
        /// Everything a profile spends into a run: its provisions, its talents, its worn gear, the threat its renown
        /// earns and the tiles its talents reveal. Written once because there were two callers waiting to disagree -
        /// the game and the balance harness - and a harness that provisions differently is measuring a different game
        /// (TEST-23, D-069).
        /// </summary>
        public static void ProvisionRun(ProfileState profile, RunState run, ContentCatalog catalog, List<GameEvent> events)
        {
            if (run == null) return;
            FirstRunGrace(profile, run, catalog);
            if (profile == null) return;
            Provision(profile, run, catalog);
            Progression.Apply(profile, run, catalog);
            Inventory.Apply(profile, run, catalog);
            // Renown's threat (D-040, D-067). Floor 1 is already laid out, and threat only reaches the deep floors.
            run.Threat = Progression.Threat(profile, catalog);
            RunFactory.RevealByTalents(run, events ?? new List<GameEvent>());
        }

        /// <summary>
        /// The first run of a profile starts with a little more (D-076). Only the first: `RunsFinished` counts every run
        /// banked, won or lost, so the grace is gone the moment one ends.
        ///
        /// A null profile counts as a first run. That is not a special case for the harness's convenience - the game
        /// always has a profile, so null only ever means "a hero with nothing behind them", which is exactly who this is
        /// for. It also keeps the empty-profile sweeps measuring the run a real new player gets rather than one nobody
        /// will ever play.
        /// </summary>
        public static void FirstRunGrace(ProfileState profile, RunState run, ContentCatalog catalog)
        {
            if (run?.Hero == null || (profile != null && profile.RunsFinished > 0)) return;
            run.Hero.MaxHp += catalog.FirstRunHearts;
            run.Hero.Hp += catalog.FirstRunHearts;
            run.Hero.Potions += catalog.FirstRunPotions;
        }

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
                // One premium chest per key, up to the run's ceiling; any key beyond that stays in the pocket.
                // DATA-21: this used to be the width of the floor range, which at twenty floors let one run carry
                // eighteen keys and place a guaranteed-item chest on every non-boss floor.
                int floors = Math.Max(0, catalog.Treasure.PremiumLastFloor - catalog.Treasure.PremiumFirstFloor + 1);
                int carried = Math.Min(profile.SpecialKeys, Math.Min(catalog.Treasure.PremiumChestsPerRun, floors));
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
    /// <c>profile.json.broken</c> and never overwritten, because it is the only record of the player's progress. A profile
    /// from a newer build is left exactly where it is and nothing is written over it (DATA-15).
    /// </summary>
    public sealed class FileProfileStore : IProfileStore
    {
        /// <summary>Why a read did not produce a profile. The three reasons want three different answers (DATA-15).</summary>
        enum ReadOutcome
        {
            /// <summary>Read and understood.</summary>
            Ok,
            /// <summary>No such file. Nothing happened, and nothing needs saying.</summary>
            Absent,
            /// <summary>There is a file, but it does not parse — damaged, truncated or locked.</summary>
            Unreadable,
            /// <summary>It parses perfectly well; it was simply written by a later build than this one.</summary>
            Newer,
        }

        public readonly string MainPath;
        public readonly string TempPath;
        public readonly string BackupPath;
        public readonly string BrokenPath;

        /// <summary>False once the file on disk must not be written over: a damaged profile that could not be set aside, or one from a newer build.</summary>
        bool _mayOverwrite = true;
        /// <summary>Set when the backup came from a newer build: it is readable and not ours to move, so quarantine leaves it.</summary>
        bool _keepTheBackup;

        /// <summary>What <see cref="Save"/> throws while <see cref="_mayOverwrite"/> is false, in the words of the reason it is false.</summary>
        string _refusal;

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
            _refusal = null;
            _keepTheBackup = false;

            var main = TryRead(MainPath, out var loaded);
            if (main == ReadOutcome.Ok) return loaded;
            if (main == ReadOutcome.Newer) return FromANewerBuild();

            var backup = TryRead(BackupPath, out var recovered);
            if (backup == ReadOutcome.Ok)
            {
                LoadNotice = "Your profile could not be read, so its backup was used. Anything bought or earned in the last moments before that may be missing.";
                return recovered;
            }
            // A newer backup is no more ours to move than a newer profile -- but only the main file's own state can
            // say what happened to the player. Behind a damaged main file, a newer backup is not the story: the profile
            // really is unreadable, and saying otherwise would send them to update the game instead of rescuing it.
            if (backup == ReadOutcome.Newer && main == ReadOutcome.Absent) return FromANewerBuild();
            if (backup == ReadOutcome.Newer) _keepTheBackup = true;
            // Nothing on disk at all is an ordinary first launch, not a loss.
            if (main == ReadOutcome.Absent && backup == ReadOutcome.Absent) return new ProfileState();

            KeepTheDamagedFile();
            return new ProfileState();
        }

        /// <summary>
        /// A profile written by a later build parses, so nothing about it is damaged: it is left exactly where it is, both
        /// copies untouched, and nothing may write over it (DATA-15). This is the run save's answer to a newer ruleset.
        /// </summary>
        ProfileState FromANewerBuild()
        {
            _mayOverwrite = false;
            _refusal = $"The profile was made by a newer version of the game, so it is not being overwritten. {MainPath}";
            LoadNotice = "Your profile was made by a newer version of the game, so it cannot be opened here. Nothing has been changed or thrown away: update the game to carry on where you left off.";
            return new ProfileState();
        }

        /// <summary>
        /// Moves an unreadable profile out of the way so a fresh one can be written without destroying it. Nothing already
        /// set aside is deleted — the name rolls on instead — and the backup is kept beside it rather than thrown away. If
        /// the move fails, nothing may overwrite it: <see cref="Save"/> then refuses rather than taking the player's
        /// progress with it.
        /// </summary>
        void KeepTheDamagedFile()
        {
            try
            {
                string kept = null;
                if (File.Exists(MainPath))
                {
                    kept = FreeBrokenPath();
                    File.Move(MainPath, kept);
                }
                if (File.Exists(BackupPath) && !_keepTheBackup)
                {
                    string keptBackup = FreeBrokenPath();
                    File.Move(BackupPath, keptBackup);
                    kept = kept ?? keptBackup;
                }
                LoadNotice = $"Your profile could not be read. It has been kept as {Path.GetFileName(kept ?? BrokenPath)} and a new one started; nothing was thrown away.";
            }
            catch (Exception)
            {
                _mayOverwrite = false;
                _refusal = $"The unreadable profile could not be set aside, so it is not being overwritten. Move {MainPath} somewhere safe.";
                LoadNotice = $"Your profile could not be read, and could not be set aside either. It will not be overwritten: close the game and move {Path.GetFileName(MainPath)} somewhere safe.";
            }
        }

        /// <summary>A name no file has yet: <c>profile.json.broken</c>, then <c>.broken.1</c> and on. An earlier casualty is never written over.</summary>
        string FreeBrokenPath()
        {
            if (!File.Exists(BrokenPath)) return BrokenPath;
            for (int n = 1; n < 1000; n++)
            {
                string candidate = BrokenPath + "." + n;
                if (!File.Exists(candidate)) return candidate;
            }
            throw new IOException($"Too many damaged profiles beside {BrokenPath} to set another one aside.");
        }

        ReadOutcome TryRead(string path, out ProfileState result)
        {
            result = null;
            if (!File.Exists(path)) return ReadOutcome.Absent;
            ProfileState profile;
            try
            {
                profile = JsonConvert.DeserializeObject<ProfileState>(File.ReadAllText(path), Settings);
            }
            catch (Exception)
            {
                return ReadOutcome.Unreadable;
            }
            if (profile == null) return ReadOutcome.Unreadable;
            // A profile from a newer build reads fine but may mean anything; an older one only lacks fields, which default safely.
            if (profile.SchemaVersion > Versions.ProfileSchema) return ReadOutcome.Newer;
            profile.SchemaVersion = Versions.ProfileSchema;
            result = Repair(profile);
            return ReadOutcome.Ok;
        }

        static ContentCatalog _itemShapes;

        /// <summary>
        /// The item definitions <see cref="Repair"/> checks worn gear against. An item's slot is the same at every
        /// difficulty, so the default tier serves, and it is only built if a profile is actually read.
        /// </summary>
        static ContentCatalog ItemShapes => _itemShapes ?? (_itemShapes = ContentCatalog.CreateDefault());

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
                // Experience is bounded at both ends (DATA-17): past the last level there is nothing left to buy, and an
                // unbounded number here used to send the level search round for ever inside the session's constructor.
                profile.Xp = Math.Min(Progression.XpForLevel(Progression.MaxLevel), Math.Max(0, profile.Xp));
                profile.DailyStreak = Math.Max(0, profile.DailyStreak);
                if (profile.Talents == null) profile.Talents = new System.Collections.Generic.Dictionary<string, int>();
                if (profile.Items == null) profile.Items = new System.Collections.Generic.List<string>();
                if (profile.Equipped == null) profile.Equipped = new System.Collections.Generic.Dictionary<string, string>();
                if (profile.Achievements == null) profile.Achievements = new System.Collections.Generic.List<string>();
                if (profile.Mail == null) profile.Mail = new System.Collections.Generic.List<MailMessage>();
                profile.Mail.RemoveAll(m => m == null);
                // Worn gear has a shape as well as a value (SEC-04): a slot that is not a slot, an item that is not owned
                // or does not belong there, or one item worn twice, all applied their numbers to every run started after.
                Inventory.RepairEquipped(profile, ItemShapes);
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
                throw new IOException(_refusal ?? $"The profile at {MainPath} is not being overwritten.");

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
