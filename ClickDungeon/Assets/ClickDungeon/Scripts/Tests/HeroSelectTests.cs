using System;
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

        /// <summary>
        /// D-057: Ironheart is the first and default Knight, and Sir Clickington is the mascot, not a playable hero. The
        /// roster's order is what hero select shows and what the watch bot alternates through.
        /// </summary>
        [Test]
        public void IronheartLeadsTheRosterAndTheMascotIsNotPlayable()
        {
            Assert.That(ContentCatalog.DefaultHeroId, Is.EqualTo("ironheart"));
            Assert.That(Catalog.HeroIdentities.Keys.First(), Is.EqualTo(ContentCatalog.DefaultHeroId), "Ironheart is hero number one.");
            Assert.That(Catalog.HeroIdentity(ContentCatalog.DefaultHeroId).ClassId, Is.EqualTo("knight"));
            Assert.That(Catalog.HeroIdentities.ContainsKey(ContentCatalog.MascotId), Is.False,
                "The mascot must not be offered as a hero: he belongs to a campaign that does not exist yet.");
            Assert.That(ContentCatalog.MascotId, Is.Not.EqualTo(ContentCatalog.DefaultHeroId),
                "Screens that lay a face over the painted mascot compare against the mascot, not the default hero.");
        }

        /// <summary>
        /// D-057: a run saved as Sir Clickington, before he was retired from play, continues as Ironheart instead of being
        /// refused as an unknown hero. The successor shares the class, so nothing about the run itself may change.
        /// </summary>
        [Test]
        public void ARunSavedAsTheRetiredMascotContinuesAsIronheart()
        {
            var run = RunFactory.NewRun(21UL, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            run.Hero.IdentityId = ContentCatalog.MascotId;   // as a save written before D-057 has it
            int hp = run.Hero.Hp, slash = run.Hero.SlashDamage;

            var loaded = SaveSerializer.FromJson(SaveSerializer.ToJson(run));

            Assert.That(loaded.Hero.IdentityId, Is.EqualTo(ContentCatalog.DefaultHeroId));
            Assert.That(loaded.Hero.ClassId, Is.EqualTo("knight"));
            Assert.That(loaded.Hero.Hp, Is.EqualTo(hp));
            Assert.That(loaded.Hero.SlashDamage, Is.EqualTo(slash));

            var session = new GameSession(Catalog, new JsonStore(SaveSerializer.ToJson(run)));
            Assert.That(session.TryContinue(out var problem), Is.True, $"Continue refused it: {problem}");
            Assert.That(session.Run.Hero.IdentityId, Is.EqualTo(ContentCatalog.DefaultHeroId));
        }

        /// <summary>A save held as text and read back through the real serializer, so loading runs the real migration.</summary>
        sealed class JsonStore : ISaveStore
        {
            string _json;
            public JsonStore(string json) => _json = json;
            public bool Exists => _json != null;
            public void Save(RunState run) => _json = SaveSerializer.ToJson(run);
            public void Delete() => _json = null;
            public bool TryLoad(out RunState run, out string message)
            {
                run = null;
                message = null;
                if (_json == null) return false;
                try { run = SaveSerializer.FromJson(_json); return true; }
                catch (FormatException ex) { message = ex.Message; return false; }
            }
        }

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
                Assert.That(hero.ShieldCost, Is.GreaterThan(0), identity.Id);
                Assert.That(hero.DashCost, Is.GreaterThan(0), identity.Id);
                Assert.That(hero.MaxMana, Is.GreaterThanOrEqualTo(Math.Max(hero.ShieldCost, hero.DashCost)), identity.Id);
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
            Assert.That(paladin.MaxMana, Is.GreaterThan(knight.MaxMana), "More mana for shields...");
            Assert.That(paladin.DashCost, Is.GreaterThan(knight.DashCost), "...and a dearer dash.");
            Assert.That(run.Hero.Mana, Is.EqualTo(paladin.MaxMana));

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
