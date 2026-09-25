using ClickDungeon.Application;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>D-040: the deep floors answer the hero's level and worn gear.</summary>
    public class RenownTests
    {
        [Test]
        public void RenownIsLevelsPastTheFirstPlusItemsWornAndThreatIsCapped()
        {
            var profile = new ProfileState();
            Assert.That(Progression.Threat(profile, Catalog), Is.Zero, "A new hero meets the dungeon as designed.");

            // An ODD renown, first: "every 2 renown is one point of threat" only says which way it rounds if something
            // asserts it. Level 4 with nothing worn is renown 3, which must give 1 and not 2.
            profile.Xp = Progression.XpForLevel(4);
            Assert.That(Progression.Renown(profile, Catalog), Is.EqualTo(3));
            Assert.That(Progression.Threat(profile, Catalog), Is.EqualTo(1), "Renown 3 is one point of threat, rounded down.");

            Inventory.Grant(profile, Catalog, "steel_sword");
            Assert.That(Progression.Renown(profile, Catalog), Is.EqualTo(4));
            Assert.That(Progression.Threat(profile, Catalog), Is.EqualTo(2), "Renown 4 is two.");

            profile.Xp = Progression.XpForLevel(20);
            Assert.That(Progression.Threat(profile, Catalog), Is.EqualTo(Catalog.Renown.MaxThreat));
        }

        [Test]
        public void ThreatAddsHeartsAndDamageOnlyFromTheDeepFloorsAndMoreHeartsToBlobert()
        {
            // Since D-067 the threat a floor carries is Renown.Level, not the run's threat flat: it arrives with the
            // depth. What this test holds is the shape - nothing above the first floor renown reaches, hearts and blows
            // raised by the same number below it, and more hearts for a boss than for a goblin.
            var shallow = Run(Catalog.Renown.FirstFloor - 1, 7UL, ".....", ".....", ".HG..", ".....", ".....");
            shallow.Threat = 2;
            int goblinHp = Enemy(shallow, "goblin").MaxHp;
            RunFactory.ApplyThreat(shallow, Catalog);
            Assert.That(Enemy(shallow, "goblin").MaxHp, Is.EqualTo(goblinHp), "A floor above the deep ones is untouched.");
            Assert.That(Renown.Hit(shallow, Catalog, 2), Is.EqualTo(2));

            var deep = Run(Catalog.RunFloorCount, 7UL, ".....", ".....", ".HG..", ".....", ".....");
            deep.Threat = 3;
            Assert.That(Renown.Level(deep, Catalog), Is.EqualTo(3), "Test setup: full renown at the bottom is three points.");
            RunFactory.ApplyThreat(deep, Catalog);
            Assert.That(Enemy(deep, "goblin").MaxHp, Is.EqualTo(goblinHp + 3), "Three points, three hearts.");
            Assert.That(Enemy(deep, "goblin").Hp, Is.EqualTo(Enemy(deep, "goblin").MaxHp));
            Assert.That(Renown.Hit(deep, Catalog, 2), Is.EqualTo(3), "Three points raises a two-damage blow to three.");

            var court = Run(Catalog.RunFloorCount, 7UL, ".....", ".....", ".HB..", ".....", "....X");
            var blobert = Enemy(court, "lord_blobert");
            int bossHp = blobert.MaxHp;
            court.Threat = 3;
            RunFactory.ApplyThreat(court, Catalog);
            Assert.That(blobert.MaxHp, Is.EqualTo(bossHp + 6), "Three points, and a boss takes two hearts a point.");
        }

        [Test]
        public void VaultGuardsGetRenownHeartsLikeTheFloorTheVaultHangsOff()
        {
            // D-046: a vault keeps its floor's index, so Renown.Hit already raised its guards' blows. The hearts come
            // from ApplyThreat, which only SetupFloor used to run — and a vault arrives through EnterVault.
            // Deep enough that renown has arrived (D-067): a vault off floor 3 now carries as little threat as floor 3 does.
            var run = Run(Catalog.RunFloorCount, 5UL, ".....", ".....", "HdK.X", ".....", ".....");
            run.Hero.HasKey = true;
            run.Threat = 2;
            int level = Renown.Level(run, Catalog);
            Assert.That(level, Is.GreaterThan(0), "Test setup: this floor carries threat.");
            DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(run.Floor.IsVault, Is.True, "Test setup: the hero is in the vault.");
            Assert.That(run.Floor.Enemies.Count, Is.GreaterThan(0), "Test setup: a vault has guards.");

            foreach (var guard in run.Floor.Enemies)
            {
                int baseHp = Catalog.Enemy(guard.DefId).MaxHp;
                Assert.That(guard.MaxHp, Is.EqualTo(baseHp + level * Catalog.Renown.HpPerThreat), "A vault guard carries its renown hearts.");
                Assert.That(guard.Hp, Is.EqualTo(guard.MaxHp));
            }
            // The blow the guards deal is raised by the same number their hearts were.
            // Literal, not `2 + level / TPED` - that restates Renown.Hit and cannot fail for any Level-based bug,
            // which is how the committed off-by-one survived 391 tests (TEST-93).
            Assert.That(Renown.Hit(run, Catalog, 2), Is.EqualTo(3), "Level 2 at the bottom: one more damage.");
        }

        [Test]
        public void SpikesHurtMoreOnTheDeepFloorsForARenownedHero()
        {
            int Stepped(int floor, int threat)
            {
                var run = Run(floor, 7UL, ".....", ".....", ".H^..", ".....", ".....");
                run.Threat = threat;
                run.Hero.Hp = run.Hero.MaxHp = 30;
                DoOk(run, PlayerCommand.Move(P(2, 2)));
                return 30 - run.Hero.Hp;
            }

            int plain = Stepped(Catalog.Renown.FirstFloor, 0);
            Assert.That(plain, Is.EqualTo(Catalog.Hazards.SpikeDamage));
            Assert.That(Stepped(Catalog.Renown.FirstFloor - 1, 3), Is.EqualTo(plain), "Shallow floors keep their traps (D-041).");
            // The bottom, not the first floor renown reaches: threat arrives with the depth now (D-067).
            var bottom = Run(Catalog.RunFloorCount, 7UL, ".....", ".....", ".H^..", ".....", ".....");
            bottom.Threat = 3;
            Assert.That(Stepped(Catalog.RunFloorCount, 3), Is.EqualTo(plain + 1),
                "Level 3 at the bottom, and traps have their own divisor: one more, written out.");
            Assert.That(Stepped(Catalog.RunFloorCount, 3), Is.GreaterThan(plain), "A renowned hero's traps bite deeper down.");
        }
    }
}
