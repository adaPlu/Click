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

        [Test]
        public void AProfileFromANewerBuildIsKeptAndOneFromAnOlderBuildStillLoads()
        {
            var store = new FileProfileStore(_dir);
            File.WriteAllText(store.MainPath, "{ \"SchemaVersion\": 999, \"Coins\": 500 }");
            Assert.That(store.Load().Coins, Is.Zero, "A profile from a newer build is not read as this one.");
            Assert.That(File.Exists(store.BrokenPath), Is.True, "It is kept, because it is the player's only copy.");

            // An older schema only lacks fields, which default safely, so it loads rather than being thrown away.
            File.WriteAllText(store.MainPath, "{ \"SchemaVersion\": 0, \"Coins\": 40 }");
            var older = store.Load();
            Assert.That(older.Coins, Is.EqualTo(40));
            Assert.That(older.SchemaVersion, Is.EqualTo(Versions.ProfileSchema), "And is written back at this build's schema.");
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
