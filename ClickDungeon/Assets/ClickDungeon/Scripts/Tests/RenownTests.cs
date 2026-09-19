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

            profile.Xp = Progression.XpForLevel(4);
            Inventory.Grant(profile, Catalog, "steel_sword");
            Assert.That(Progression.Renown(profile, Catalog), Is.EqualTo(4));
            Assert.That(Progression.Threat(profile, Catalog), Is.EqualTo(4 / Catalog.Renown.RenownPerThreat));

            profile.Xp = Progression.XpForLevel(20);
            Assert.That(Progression.Threat(profile, Catalog), Is.EqualTo(Catalog.Renown.MaxThreat));
        }

        [Test]
        public void ThreatAddsHeartsAndDamageOnlyFromTheDeepFloorsAndMoreHeartsToBlobert()
        {
            int deepFloor = Catalog.Renown.FirstFloor;
            var shallow = Run(deepFloor - 1, 7UL, ".....", ".....", ".HG..", ".....", ".....");
            shallow.Threat = 2;
            int goblinHp = Enemy(shallow, "goblin").MaxHp;
            RunFactory.ApplyThreat(shallow, Catalog);
            Assert.That(Enemy(shallow, "goblin").MaxHp, Is.EqualTo(goblinHp), "A floor above the deep ones is untouched.");
            Assert.That(Renown.Hit(shallow, Catalog, 2), Is.EqualTo(2));

            var deep = Run(deepFloor, 7UL, ".....", ".....", ".HG..", ".....", ".....");
            deep.Threat = 2;
            RunFactory.ApplyThreat(deep, Catalog);
            Assert.That(Enemy(deep, "goblin").MaxHp, Is.EqualTo(goblinHp + 2 * Catalog.Renown.HpPerThreat));
            Assert.That(Enemy(deep, "goblin").Hp, Is.EqualTo(Enemy(deep, "goblin").MaxHp));
            Assert.That(Renown.Hit(deep, Catalog, 2), Is.EqualTo(2 + 2 / Catalog.Renown.ThreatPerExtraDamage));

            var court = Run(5, 7UL, ".....", ".....", ".HB..", ".....", "....X");
            var blobert = Enemy(court, "lord_blobert");
            int bossHp = blobert.MaxHp;
            court.Threat = 2;
            RunFactory.ApplyThreat(court, Catalog);
            Assert.That(blobert.MaxHp, Is.EqualTo(bossHp + 2 * Catalog.Renown.BossHpPerThreat));
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
            Assert.That(Stepped(Catalog.Renown.FirstFloor, 3), Is.EqualTo(plain + 3 / Catalog.Renown.ThreatPerExtraTrapDamage));
        }
    }
}
