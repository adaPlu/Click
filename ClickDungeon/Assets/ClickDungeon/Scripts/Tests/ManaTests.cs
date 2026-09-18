using System.IO;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>D-032: shield and dash cost mana, which comes back one a turn and in full on every new floor.</summary>
    public class ManaTests
    {
        [Test]
        public void EveryNewFloorStartsWithAFullPool()
        {
            var run = Run(".....", ".....", ".HxK.", ".....", ".....");
            run.Hero.Mana = 0;
            DoOk(run, PlayerCommand.Move(P(3, 2)));
            DoOk(run, PlayerCommand.Move(P(2, 2)));
            Assert.That(run.Floor.FloorIndex, Is.EqualTo(2));
            Assert.That(run.Hero.Mana, Is.EqualTo(run.Hero.MaxMana));
        }

        [Test]
        public void MoveSlashAndPotionsAreFree()
        {
            var run = Run(".....", ".....", ".HG..", ".....", ".....");
            run.Hero.Mana = 0;
            run.Hero.Hp = 3;
            DoOk(run, PlayerCommand.Slash(Enemy(run, "goblin").Pos));
            DoOk(run, PlayerCommand.Potion());
            Assert.That(run.Hero.Mana, Is.EqualTo(2 * Mana.PerTurn), "Only the end-of-turn gain moved it.");
        }

        [Test]
        public void TheShieldIsNeverFreeEvenWhenManaIsPlenty()
        {
            var run = Run(".....", ".....", "..H..", ".....", ".....");
            run.Hero.MaxMana = run.Hero.Mana = 50;
            DoOk(run, PlayerCommand.Shield());
            Assert.That(run.Hero.Mana, Is.EqualTo(50 - Catalog.HeroClass("knight").ShieldCost + Mana.PerTurn));
        }

        [Test]
        public void ARunSavedBeforeManaContinuesWithAFullPool()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cd-mana-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var session = new GameSession(ContentCatalog.CreateDefault(), new FileSaveStore(dir));
                session.StartNewRun(77UL);
                // What a ruleset 5 save looks like: no mana fields at all.
                session.Run.Hero.Mana = 0;
                session.Run.Hero.MaxMana = 0;
                new FileSaveStore(dir).Save(session.Run);

                var resumed = new GameSession(ContentCatalog.CreateDefault(), new FileSaveStore(dir));
                Assert.That(resumed.TryContinue(out var message), Is.True, message);
                Assert.That(resumed.Run.Hero.MaxMana, Is.EqualTo(Catalog.HeroClass("knight").MaxMana));
                Assert.That(resumed.Run.Hero.Mana, Is.EqualTo(resumed.Run.Hero.MaxMana));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
