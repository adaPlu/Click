using System;
using System.Collections.Generic;
using System.IO;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    public class DifficultyTests
    {
        static readonly string[] Enemies = { "goblin", "crowned_slime", "fire_imp", "lord_blobert" };

        static int Power(ContentCatalog c)
        {
            int total = c.HeroClass("knight").MaxHp * -1 - c.HeroClass("knight").StartingPotions * 4 - c.FloorClearHeal
                        + c.Hazards.SpikeDamage + c.Hazards.BombDamage;
            foreach (var id in Enemies)
            {
                var e = c.Enemy(id);
                total += e.MaxHp + e.Damage + e.SlamDamage;
            }
            return total;
        }

        [Test]
        public void TiersAreNamedAndOrderedFromEasyToHardcore()
        {
            var easy = ContentCatalog.CreateDefault(Difficulty.Easy);
            var medium = ContentCatalog.CreateDefault(Difficulty.Medium);
            var hardcore = ContentCatalog.CreateDefault(Difficulty.Hardcore);

            var names = new HashSet<string>();
            foreach (var tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore })
            {
                var info = medium.DifficultyInfo(tier);
                Assert.That(info.DisplayName, Is.Not.Empty);
                Assert.That(info.Tagline, Is.Not.Empty);
                Assert.That(names.Add(info.DisplayName), Is.True);
            }

            Assert.That(Power(easy), Is.LessThan(Power(medium)));
            Assert.That(Power(medium), Is.LessThan(Power(hardcore)));
            foreach (var id in Enemies)
            {
                Assert.That(easy.Enemy(id).Damage, Is.LessThanOrEqualTo(medium.Enemy(id).Damage), id);
                Assert.That(medium.Enemy(id).Damage, Is.LessThanOrEqualTo(hardcore.Enemy(id).Damage), id);
                Assert.That(easy.Enemy(id).MaxHp, Is.LessThanOrEqualTo(hardcore.Enemy(id).MaxHp), id);
            }
            Assert.That(easy.HeroClass("knight").MaxHp, Is.GreaterThanOrEqualTo(hardcore.HeroClass("knight").MaxHp));
            Assert.That(easy.FloorClearHeal, Is.GreaterThanOrEqualTo(medium.FloorClearHeal));
            Assert.That(medium.FloorClearHeal, Is.GreaterThanOrEqualTo(hardcore.FloorClearHeal));
        }

        [Test]
        public void TuningNeverLeaksBetweenCatalogs()
        {
            int goblinDamage = ContentCatalog.CreateDefault().Enemy("goblin").Damage;
            ContentCatalog.CreateDefault(Difficulty.Hardcore);
            ContentCatalog.CreateDefault(Difficulty.Easy);
            Assert.That(ContentCatalog.CreateDefault().Enemy("goblin").Damage, Is.EqualTo(goblinDamage));

            var medium = ContentCatalog.CreateDefault();
            Assert.That(medium.Difficulty, Is.EqualTo(Difficulty.Medium));
            Assert.That(medium.ForDifficulty(Difficulty.Medium), Is.SameAs(medium));
            Assert.That(medium.ForDifficulty(Difficulty.Easy).Difficulty, Is.EqualTo(Difficulty.Easy));
        }

        [Test]
        public void EveryTierStillGeneratesValidFloors()
        {
            foreach (var tier in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore })
            {
                var catalog = ContentCatalog.CreateDefault(tier);
                for (ulong seed = 1; seed <= 200; seed++)
                for (int floor = 1; floor <= catalog.RunFloorCount; floor++)
                {
                    var generated = FloorGenerator.Generate(seed, floor, catalog);
                    string where = $"{tier} seed {seed} floor {floor}";
                    Assert.That(FloorValidator.Validate(generated, catalog), Is.True, where);
                    var profile = catalog.ProfileFor(floor);
                    if (!profile.IsBoss) Assert.That(generated.Enemies.Count, Is.GreaterThanOrEqualTo(profile.MinEnemies), where + ": enemies placed");
                }
            }
        }

        [Test]
        public void RunRecordsItsDifficultyAndSavesIt()
        {
            var easy = ContentCatalog.CreateDefault(Difficulty.Easy);
            var run = RunFactory.NewRun(5UL, easy, new List<GameEvent>());
            Assert.That(run.Difficulty, Is.EqualTo(Difficulty.Easy));
            Assert.That(run.Hero.MaxHp, Is.EqualTo(easy.HeroClass("knight").MaxHp));
            Assert.That(run.Hero.Potions, Is.EqualTo(easy.HeroClass("knight").StartingPotions));
            Assert.That(SaveSerializer.FromJson(SaveSerializer.ToJson(run)).Difficulty, Is.EqualTo(Difficulty.Easy));
        }

        [Test]
        public void SavesFromBeforeDifficultyTiersLoadAsMedium()
        {
            var json = SaveSerializer.ToJson(RunFactory.NewRun(5UL, Catalog, new List<GameEvent>()));
            json = json.Replace("\"Difficulty\": \"Medium\",", "");
            Assert.That(json, Does.Not.Contain("Difficulty"));
            Assert.That(SaveSerializer.FromJson(json).Difficulty, Is.EqualTo(Difficulty.Medium));
        }

        [Test]
        public void SessionTunesItselfToTheRunsDifficulty()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cd-difficulty-" + Guid.NewGuid().ToString("N"));
            try
            {
                var session = new GameSession(ContentCatalog.CreateDefault(), new FileSaveStore(dir));
                session.StartNewRun(9UL, Difficulty.Hardcore);
                Assert.That(session.Catalog.Difficulty, Is.EqualTo(Difficulty.Hardcore));
                Assert.That(session.Run.Difficulty, Is.EqualTo(Difficulty.Hardcore));

                var resumed = new GameSession(ContentCatalog.CreateDefault(), new FileSaveStore(dir));
                Assert.That(resumed.TryContinue(out _), Is.True);
                Assert.That(resumed.Catalog.Difficulty, Is.EqualTo(Difficulty.Hardcore), "Resuming restores the saved tier.");

                resumed.StartNewRun(10UL);
                Assert.That(resumed.Run.Difficulty, Is.EqualTo(Difficulty.Hardcore), "NEW RUN keeps the tier.");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TelemetryFollowsTheSessionsTier()
        {
            var sink = new MemoryTelemetrySink();
            var session = new GameSession(ContentCatalog.CreateDefault(), null, new TelemetryRecorder(sink, ContentCatalog.CreateDefault()));
            session.StartNewRun(4UL, Difficulty.Easy);
            Assert.That(session.Telemetry.Catalog, Is.SameAs(session.Catalog));
        }

        [Test]
        public void ArrivingOnTheNextFloorHealsByTheTiersBreather()
        {
            var easy = ContentCatalog.CreateDefault(Difficulty.Easy);
            Assume.That(easy.FloorClearHeal, Is.GreaterThan(0));

            var run = Run(
                ".....",
                ".....",
                "HKX..",
                ".....",
                ".....");
            // Scenario builds a Medium hero; give it Easy's health so this is a state the game can produce.
            run.Hero.MaxHp = easy.HeroClass("knight").MaxHp;
            // Exactly half: the stairs' mercy (D-071) contributes nothing here, so this measures the breather alone.
            run.Hero.Hp = run.Hero.MaxHp / 2;
            Assert.That(TurnResolver.Apply(run, PlayerCommand.Move(P(1, 2)), easy).Accepted, Is.True);
            var result = TurnResolver.Apply(run, PlayerCommand.Move(P(2, 2)), easy);

            Assert.That(run.Floor.FloorIndex, Is.EqualTo(2));
            Assert.That(run.Hero.Hp, Is.EqualTo(Math.Min(run.Hero.MaxHp, run.Hero.MaxHp / 2 + easy.FloorClearHeal)));
            Assert.That(result.Events.Exists(e => e.Kind == GameEventKind.HeroHealed && e.Source == "stairs"), Is.True);
        }

        [Test]
        public void TheStairsNeverLeaveABadlyHurtHeroBelowHalf()
        {
            // D-071: help that only arrives when it is needed. A careful player is under half their hearts on 6% of
            // turns and a careless one on up to 33%, so this lifts the floor of the game without raising its ceiling -
            // it is what took the weakest class from 15% of runs won to 33% while the strongest stayed where it was.
            var medium = ContentCatalog.CreateDefault(Difficulty.Medium);
            Assume.That(medium.MercyOnStairs, Is.GreaterThan(0));
            var run = Run(
                ".....",
                ".....",
                "HKX..",
                ".....",
                ".....");
            run.Hero.MaxHp = 20;
            run.Hero.Hp = 2;

            Assert.That(TurnResolver.Apply(run, PlayerCommand.Move(P(1, 2)), medium).Accepted, Is.True);
            TurnResolver.Apply(run, PlayerCommand.Move(P(2, 2)), medium);

            Assert.That(run.Floor.FloorIndex, Is.EqualTo(2), "Test setup: the hero took the stairs.");
            Assert.That(run.Hero.Hp, Is.GreaterThanOrEqualTo(run.Hero.MaxHp / medium.MercyOnStairs),
                "A hero who reaches the stairs nearly dead is brought back to half.");

            // And it is mercy, not a free top-up: a hero above half gets the breather and nothing more.
            var healthy = Run(
                ".....",
                ".....",
                "HKX..",
                ".....",
                ".....");
            healthy.Hero.MaxHp = 20;
            healthy.Hero.Hp = 15;
            Assert.That(TurnResolver.Apply(healthy, PlayerCommand.Move(P(1, 2)), medium).Accepted, Is.True);
            TurnResolver.Apply(healthy, PlayerCommand.Move(P(2, 2)), medium);
            Assert.That(healthy.Hero.Hp, Is.EqualTo(Math.Min(healthy.Hero.MaxHp, 15 + medium.FloorClearHeal)),
                "Above half, the stairs give the breather and nothing else.");
        }

        [Test]
        public void TheBreatherNeverOverheals()
        {
            var easy = ContentCatalog.CreateDefault(Difficulty.Easy);
            var run = Run(
                ".....",
                ".....",
                "HKX..",
                ".....",
                ".....");
            run.Hero.MaxHp = easy.HeroClass("knight").MaxHp;
            run.Hero.Hp = run.Hero.MaxHp;
            TurnResolver.Apply(run, PlayerCommand.Move(P(1, 2)), easy);
            var result = TurnResolver.Apply(run, PlayerCommand.Move(P(2, 2)), easy);

            Assert.That(run.Hero.Hp, Is.EqualTo(run.Hero.MaxHp));
            Assert.That(Has(result, GameEventKind.HeroHealed), Is.False);
        }
    }

    public class ExitDisplayTests
    {
        [Test]
        public void ExitReadsOpenOnceTheKeyIsHeld()
        {
            var run = Run(
                ".....",
                ".....",
                "HKX..",
                ".....",
                ".....");
            Assert.That(Board.ExitReadsOpen(run), Is.False);

            DoOk(run, PlayerCommand.Move(P(1, 2)));
            Assert.That(run.Hero.HasKey, Is.True);
            Assert.That(run.Floor.ExitUnlocked, Is.False, "The rule is unchanged: entering the exit unlocks it.");
            Assert.That(Board.ExitReadsOpen(run), Is.True);
        }

        [Test]
        public void BlobertsSealIgnoresKeys()
        {
            var run = Run(Catalog.RunFloorCount, 1UL,
                "B....",
                ".....",
                "H.X..",
                ".....",
                ".....");
            run.Hero.HasKey = true;
            Assert.That(Board.ExitReadsOpen(run), Is.False);
            run.Floor.ExitUnlocked = true;
            Assert.That(Board.ExitReadsOpen(run), Is.True);
        }
    }
}
