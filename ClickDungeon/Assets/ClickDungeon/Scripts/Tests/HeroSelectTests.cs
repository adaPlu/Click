using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>
    /// D-024: a run can be taken by any hero in the catalog. The hero is chosen before the run and never changes during it,
    /// and every class is playable on every tier.
    /// </summary>
    public class HeroSelectTests
    {
        const string Dawnward = "dawnward";

        [Test]
        public void EveryHeroIdentityHasAClassAndNumbersThatMakeSense()
        {
            Assert.That(Catalog.HeroIdentities, Is.Not.Empty);
            foreach (var identity in Catalog.HeroIdentities.Values)
            {
                Assert.That(identity.DisplayName, Is.Not.Empty, identity.Id);
                Assert.That(identity.Tagline, Is.Not.Empty, identity.Id);
                var hero = Catalog.HeroClass(identity.ClassId);
                Assert.That(hero.MaxHp, Is.GreaterThan(0), identity.Id);
                Assert.That(hero.SlashDamage, Is.GreaterThan(0), identity.Id);
                Assert.That(hero.PotionHeal, Is.GreaterThan(0), identity.Id);
                Assert.That(hero.DashDistance, Is.GreaterThan(0), identity.Id);
                Assert.That(hero.ShieldCooldown, Is.GreaterThan(0), identity.Id);
                Assert.That(hero.DashCooldown, Is.GreaterThan(0), identity.Id);
            }
        }

        [Test]
        public void TheChosenHeroIsTheOneWhoPlays()
        {
            var run = RunFactory.NewRun(1234UL, Catalog, new List<GameEvent>(), Dawnward, MovementMode.Free);
            var paladin = Catalog.HeroClass("paladin");
            Assert.That(run.Hero.IdentityId, Is.EqualTo(Dawnward));
            Assert.That(run.Hero.ClassId, Is.EqualTo("paladin"));
            Assert.That(run.Hero.MaxHp, Is.EqualTo(paladin.MaxHp));
            Assert.That(run.Hero.Hp, Is.EqualTo(paladin.MaxHp));
            Assert.That(run.Hero.SlashDamage, Is.EqualTo(paladin.SlashDamage));
            Assert.That(run.Hero.Potions, Is.EqualTo(paladin.StartingPotions));
        }

        [Test]
        public void ThePaladinPlaysToItsOwnNumbers()
        {
            var run = RunFactory.NewRun(1234UL, Catalog, new List<GameEvent>(), Dawnward, MovementMode.Free);
            var paladin = Catalog.HeroClass("paladin");
            var knight = Catalog.HeroClass("knight");
            Assert.That(paladin.MaxHp, Is.GreaterThan(knight.MaxHp), "The Paladin is the tougher of the two...");
            Assert.That(paladin.DashDistance, Is.LessThan(knight.DashDistance), "...and the slower to get around.");
            Assert.That(paladin.PotionHeal, Is.GreaterThan(knight.PotionHeal));
            Assert.That(paladin.ShieldCooldown, Is.LessThan(knight.ShieldCooldown));

            // A dash of one tile is all it may do, whatever the Knight can reach.
            run.Hero.Pos = P(2, 2);
            run.Floor[P(2, 4)].Knowledge = Knowledge.Revealed;
            Assert.That(Commands.Validate(run, PlayerCommand.Dash(P(2, 4)), Catalog, out _), Is.False);
            Assert.That(Commands.Validate(run, PlayerCommand.Dash(P(2, 3)), Catalog, out _), Is.True);
        }

        [Test]
        public void AHeroChoiceIsReadByNameAndAnUnknownOneFallsBack()
        {
            Assert.That(LaunchOptions.ParseHero("dawnward", Catalog, ContentCatalog.DefaultHeroId), Is.EqualTo(Dawnward));
            Assert.That(LaunchOptions.ParseHero("DAWNWARD", Catalog, ContentCatalog.DefaultHeroId), Is.EqualTo(Dawnward));
            Assert.That(LaunchOptions.ParseHero("nobody", Catalog, ContentCatalog.DefaultHeroId), Is.EqualTo(ContentCatalog.DefaultHeroId));
            Assert.That(LaunchOptions.ParseHero(null, Catalog, Dawnward), Is.EqualTo(Dawnward), "No flag keeps the saved choice.");
        }

        [Test]
        public void ARunKeepsItsHeroAcrossASave()
        {
            var run = RunFactory.NewRun(99UL, Catalog, new List<GameEvent>(), Dawnward, MovementMode.Free);
            var restored = SaveSerializer.FromJson(SaveSerializer.ToJson(run));
            Assert.That(restored.Hero.IdentityId, Is.EqualTo(Dawnward));
            Assert.That(restored.Hero.ClassId, Is.EqualTo("paladin"));
            Assert.That(restored.Hero.MaxHp, Is.EqualTo(run.Hero.MaxHp));
        }

        [Test]
        public void EveryHeroCanFinishARunOnEveryTier()
        {
            // A hero nobody can win with is not a choice. The sighted bot plays each tier once per hero.
            foreach (var identity in Catalog.HeroIdentities.Values)
            foreach (Difficulty tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore })
            {
                var catalog = Catalog.ForDifficulty(tier);
                int won = 0;
                for (ulong seed = 1; seed <= 6; seed++)
                    if (AutoPlayer.PlayRun(catalog, seed, 400, heroId: identity.Id).Status == RunStatus.Won) won++;
                Assert.That(won, Is.GreaterThan(0), $"{identity.Id} on {tier} won none of 6 runs.");
            }
        }
    }
}
