using System.Collections.Generic;
using System.IO;
using ClickDungeon.Application;
using ClickDungeon.Domain;
using NUnit.Framework;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// D-043: the profile is the player's whole progression, so its file gets the run save's protections — a verified
    /// write that keeps the copy it replaced, a fall back to that copy, and a damaged file kept rather than overwritten.
    /// </summary>
    public class ProfileStoreTests
    {
        string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "cd-profile-" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void AWriteKeepsTheCopyItReplacedAndAnUnreadableProfileFallsBackToIt()
        {
            var store = new FileProfileStore(_dir);
            store.Save(new ProfileState { Coins = 100 });
            store.Save(new ProfileState { Coins = 250 });
            Assert.That(File.Exists(store.BackupPath), Is.True, "The copy that was replaced is kept.");
            Assert.That(store.Load().Coins, Is.EqualTo(250));
            Assert.That(store.LoadNotice, Is.Null, "A profile that reads cleanly says nothing.");

            File.WriteAllText(store.MainPath, "{ truncated");
            var recovered = store.Load();
            Assert.That(recovered.Coins, Is.EqualTo(100), "The backup stands in for an unreadable profile.");
            Assert.That(store.LoadNotice, Is.Not.Null, "And the player is told the backup was used.");
        }

        [Test]
        public void AnUnreadableProfileIsKeptAsideAndNeverOverwritten()
        {
            var store = new FileProfileStore(_dir);
            const string damaged = "{ \"Coins\": 9000, this file is broken";
            File.WriteAllText(store.MainPath, damaged);

            Assert.That(store.Load().Coins, Is.Zero, "A broken profile must not stop the game from starting.");
            Assert.That(store.LoadNotice, Is.Not.Null);
            Assert.That(File.Exists(store.BrokenPath), Is.True, "The damaged file is kept, not deleted.");
            Assert.That(File.ReadAllText(store.BrokenPath), Is.EqualTo(damaged), "Kept exactly as it was.");

            // The empty profile the game starts with must not erase what was set aside.
            store.Save(new ProfileState { Coins = 5 });
            Assert.That(File.ReadAllText(store.BrokenPath), Is.EqualTo(damaged));
            Assert.That(store.Load().Coins, Is.EqualTo(5));
        }

        /// <summary>
        /// DATA-15: a profile from a newer build is not damaged — it reads perfectly well, it simply means more than this
        /// build knows. It used to be quarantined as if it were rubble, which moved it and deleted the backup, so a player
        /// who opened a downgraded build once lost both copies. It must be left exactly where it is, like a run save made
        /// under a newer ruleset.
        /// </summary>
        [Test]
        public void AProfileFromANewerBuildIsLeftWhereItIsWithItsBackup()
        {
            var store = new FileProfileStore(_dir);
            const string newer = "{ \"SchemaVersion\": 999, \"Coins\": 500 }";
            const string newerBackup = "{ \"SchemaVersion\": 999, \"Coins\": 480 }";
            File.WriteAllText(store.MainPath, newer);
            File.WriteAllText(store.BackupPath, newerBackup);

            Assert.That(store.Load().Coins, Is.Zero, "A profile from a newer build is not read as this one.");
            Assert.That(store.LoadNotice, Does.Contain("newer version"), "And the player is told why, in those words.");
            Assert.That(File.Exists(store.BrokenPath), Is.False, "Nothing is set aside: there is nothing wrong with it.");
            Assert.That(File.ReadAllText(store.MainPath), Is.EqualTo(newer), "It is left exactly where it is.");
            Assert.That(File.ReadAllText(store.BackupPath), Is.EqualTo(newerBackup), "And so is the only other copy of it.");

            // Nothing may write over it either, so launching the downgraded build cannot quietly replace it.
            Assert.Throws<IOException>(() => store.Save(new ProfileState { Coins = 1 }));
            Assert.That(File.ReadAllText(store.MainPath), Is.EqualTo(newer));
            Assert.That(File.ReadAllText(store.BackupPath), Is.EqualTo(newerBackup));
        }

        [Test]
        public void AProfileFromAnOlderBuildStillLoads()
        {
            var store = new FileProfileStore(_dir);
            // An older schema only lacks fields, which default safely, so it loads rather than being thrown away.
            File.WriteAllText(store.MainPath, "{ \"SchemaVersion\": 0, \"Coins\": 40 }");
            var older = store.Load();
            Assert.That(older.Coins, Is.EqualTo(40));
            Assert.That(older.SchemaVersion, Is.EqualTo(Versions.ProfileSchema), "And is written back at this build's schema.");
            Assert.That(store.LoadNotice, Is.Null);
        }

        /// <summary>DATA-15: setting a damaged profile aside must not destroy the one set aside last time.</summary>
        [Test]
        public void ASecondDamagedProfileIsKeptBesideTheFirstNotOverIt()
        {
            var store = new FileProfileStore(_dir);
            const string first = "{ \"Coins\": 9000, broken the first time";
            File.WriteAllText(store.MainPath, first);
            store.Load();
            Assert.That(File.ReadAllText(store.BrokenPath), Is.EqualTo(first));

            const string second = "{ \"Coins\": 40, broken all over again";
            File.WriteAllText(store.MainPath, second);
            store.Load();

            Assert.That(File.ReadAllText(store.BrokenPath), Is.EqualTo(first), "The first casualty is untouched.");
            Assert.That(File.ReadAllText(store.BrokenPath + ".1"), Is.EqualTo(second), "The second is kept beside it.");
            Assert.That(store.LoadNotice, Does.Contain(".broken.1"), "And the notice names the file it actually wrote.");
        }

        /// <summary>DATA-15: a backup that is no good either is still the player's, so it is set aside rather than deleted.</summary>
        [Test]
        public void ADamagedBackupIsSetAsideRatherThanDeleted()
        {
            var store = new FileProfileStore(_dir);
            const string main = "{ the profile is broken";
            const string backup = "{ and so is its backup";
            File.WriteAllText(store.MainPath, main);
            File.WriteAllText(store.BackupPath, backup);

            Assert.That(store.Load().Coins, Is.Zero, "Neither can be read, so the game starts fresh.");
            Assert.That(File.ReadAllText(store.BrokenPath), Is.EqualTo(main));
            Assert.That(File.ReadAllText(store.BrokenPath + ".1"), Is.EqualTo(backup), "Nothing was thrown away.");
            Assert.That(File.Exists(store.BackupPath), Is.False, "But it no longer stands in for a profile it cannot be.");
        }

        /// <summary>
        /// DATA-17: a profile carrying an impossible amount of experience used to hang the game's first frame — the level
        /// search climbs one level at a time and the curve it climbs stops rising once its arithmetic wraps.
        /// </summary>
        [Test, Timeout(20000)]
        public void AProfileCarryingAnAbsurdExperienceStillOpens()
        {
            var store = new FileProfileStore(_dir);
            File.WriteAllText(store.MainPath, "{ \"SchemaVersion\": " + Versions.ProfileSchema + ", \"Xp\": 2147483647 }");

            var loaded = store.Load();

            Assert.That(loaded.Xp, Is.EqualTo(Progression.XpForLevel(Progression.MaxLevel)), "Experience is clamped to the last level there is.");
            Assert.That(Progression.Level(loaded), Is.EqualTo(Progression.MaxLevel));
        }

        /// <summary>
        /// SEC-04: worn gear has a shape as well as a value. A hand-edited profile could wear one item in two slots (its
        /// numbers counted twice), file an item under a slot it does not belong in, or use a key that is not a slot at all
        /// and so cannot be taken off in the UI — and all of it fed Renown, and through it the Threat every run starts at.
        /// </summary>
        [Test]
        public void AHandEditedEquippedIsPutBackIntoShapeOnLoad()
        {
            var store = new FileProfileStore(_dir);
            var edited = new ProfileState();
            edited.Items.AddRange(new[] { "steel_sword", "iron_helm", "iron_shield" });
            edited.Equipped["Weapon"] = "steel_sword";
            edited.Equipped["Helmet"] = "steel_sword";  // the same sword worn twice over
            edited.Equipped["Armor"] = "iron_helm";     // a helmet worn as a breastplate
            edited.Equipped["Boots"] = "gold_treads";   // never found
            edited.Equipped["Pocket"] = "iron_shield";  // not a slot at all
            store.Save(edited);

            var loaded = store.Load();

            Assert.That(loaded.Equipped, Is.EqualTo(new Dictionary<string, string> { { "Weapon", "steel_sword" } }),
                "Only the one entry filed under its own item's slot, holding an item that is owned, survives.");
            Assert.That(Inventory.IsWorn(loaded, "steel_sword"), Is.True, "What was rightly worn is still worn.");
            Assert.That(loaded.Items, Is.EqualTo(new[] { "steel_sword", "iron_helm", "iron_shield" }), "Nothing owned is taken away.");
            Assert.That(Progression.Renown(loaded, Scenario.Catalog), Is.EqualTo(1), "One item worn is one point of renown, not four.");
        }

        [Test]
        public void AProfileIsOnlyReplacedOnceItsReplacementIsOnDisk()
        {
            var store = new FileProfileStore(_dir);
            store.Save(new ProfileState { Coins = 70, Gems = 3 });
            string beforeWrite = File.ReadAllText(store.MainPath);

            store.Save(new ProfileState { Coins = 80, Gems = 4 });

            Assert.That(File.Exists(store.TempPath), Is.False, "The temp file never survives a finished write.");
            Assert.That(File.ReadAllText(store.BackupPath), Is.EqualTo(beforeWrite));
            var loaded = store.Load();
            Assert.That(loaded.Coins, Is.EqualTo(80));
            Assert.That(loaded.Gems, Is.EqualTo(4));
        }
    }
}
